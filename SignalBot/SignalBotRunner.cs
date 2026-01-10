using SignalBot.Configuration;
using SignalBot.Models;
using SignalBot.Services.Monitoring;
using SignalBot.Services.Telegram;
using SignalBot.Services.Trading;
using SignalBot.Services.Validation;
using SignalBot.State;
using TradingBot.Binance.Futures.Interfaces;
using TradingBot.Core.Notifications;
using Serilog;

namespace SignalBot;

/// <summary>
/// Main orchestrator for SignalBot
/// </summary>
public class SignalBotRunner
{
    private readonly SignalBotSettings _settings;
    private readonly IBinanceFuturesClient _client;
    private readonly ITelegramSignalListener _telegramListener;
    private readonly SignalParser _signalParser;
    private readonly ISignalValidator _signalValidator;
    private readonly ISignalTrader _signalTrader;
    private readonly IPositionManager _positionManager;
    private readonly IOrderMonitor _orderMonitor;
    private readonly IPositionStore<SignalPosition> _store;
    private readonly INotifier? _notifier;
    private readonly ILogger _logger;

    private readonly SemaphoreSlim _signalProcessingLock = new(1, 1);
    private CancellationTokenSource? _cts;
    private bool _isRunning;

    public SignalBotRunner(
        SignalBotSettings settings,
        IBinanceFuturesClient client,
        ITelegramSignalListener telegramListener,
        SignalParser signalParser,
        ISignalValidator signalValidator,
        ISignalTrader signalTrader,
        IPositionManager positionManager,
        IOrderMonitor orderMonitor,
        IPositionStore<SignalPosition> store,
        INotifier? notifier = null,
        ILogger? logger = null)
    {
        _settings = settings;
        _client = client;
        _telegramListener = telegramListener;
        _signalParser = signalParser;
        _signalValidator = signalValidator;
        _signalTrader = signalTrader;
        _positionManager = positionManager;
        _orderMonitor = orderMonitor;
        _store = store;
        _notifier = notifier;
        _logger = logger ?? Log.ForContext<SignalBotRunner>();
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        if (_isRunning)
        {
            _logger.Warning("SignalBot is already running");
            return;
        }

        _logger.Information("Starting SignalBot...");

        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        try
        {
            // Test connectivity
            var connected = await _client.TestConnectivityAsync(_cts.Token);
            if (!connected)
            {
                throw new InvalidOperationException("Failed to connect to Binance Futures API");
            }

            _logger.Information("Connected to Binance Futures API");

            // Subscribe to events
            _telegramListener.OnSignalReceived += HandleSignalReceived;
            _orderMonitor.OnTargetHit += HandleTargetHit;
            _orderMonitor.OnStopLossHit += HandleStopLossHit;

            // Start order monitor
            await _orderMonitor.StartAsync(_cts.Token);

            // Start Telegram listener
            await _telegramListener.StartAsync(_cts.Token);

            _isRunning = true;
            _logger.Information("SignalBot started successfully");

            if (_notifier != null)
            {
                await _notifier.SendMessageAsync(
                    "✅ SignalBot started\n" +
                    $"Monitoring {_settings.Telegram.ChannelIds.Count} Telegram channel(s)\n" +
                    $"Max concurrent positions: {_settings.Trading.MaxConcurrentPositions}",
                    _cts.Token);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to start SignalBot");
            await StopAsync();
            throw;
        }
    }

    public async Task StopAsync()
    {
        if (!_isRunning)
        {
            _logger.Warning("SignalBot is not running");
            return;
        }

        _logger.Information("Stopping SignalBot...");

        try
        {
            // Unsubscribe from events
            _telegramListener.OnSignalReceived -= HandleSignalReceived;
            _orderMonitor.OnTargetHit -= HandleTargetHit;
            _orderMonitor.OnStopLossHit -= HandleStopLossHit;

            // Stop Telegram listener
            await _telegramListener.StopAsync();

            // Stop order monitor
            await _orderMonitor.StopAsync();

            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;

            _isRunning = false;
            _logger.Information("SignalBot stopped");

            if (_notifier != null)
            {
                await _notifier.SendMessageAsync("🛑 SignalBot stopped", CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error stopping SignalBot");
        }
    }

    private async void HandleSignalReceived(TradingSignal signal)
    {
        await _signalProcessingLock.WaitAsync();
        try
        {
            _logger.Information("Received signal: {Symbol} {Direction} @ {Entry}",
                signal.Symbol, signal.Direction, signal.Entry);

            // Check concurrent positions limit
            var openPositions = await _store.GetOpenPositionsAsync(_cts!.Token);
            if (openPositions.Count >= _settings.Trading.MaxConcurrentPositions)
            {
                _logger.Warning("Max concurrent positions ({Max}) reached, ignoring signal for {Symbol}",
                    _settings.Trading.MaxConcurrentPositions, signal.Symbol);
                return;
            }

            // Check duplicate position
            var existingPosition = openPositions.FirstOrDefault(p => p.Symbol == signal.Symbol);
            if (existingPosition != null)
            {
                _logger.Warning("Position already exists for {Symbol}, handling duplicate...", signal.Symbol);
                await HandleDuplicateSignal(signal, existingPosition);
                return;
            }

            // Get account balance
            var balance = await _client.GetBalanceAsync("USDT", _cts.Token);
            _logger.Information("Current USDT balance: {Balance}", balance);

            // Validate and adjust signal
            var validationResult = await _signalValidator.ValidateAndAdjustAsync(signal, balance, _cts.Token);
            if (!validationResult.IsSuccess || validationResult.ValidatedSignal == null)
            {
                _logger.Warning("Signal validation failed for {Symbol}: {Error}",
                    signal.Symbol, validationResult.ErrorMessage);

                if (_notifier != null)
                {
                    await _notifier.SendMessageAsync(
                        $"❌ Signal validation failed\n" +
                        $"Symbol: {signal.Symbol}\n" +
                        $"Reason: {validationResult.ErrorMessage}",
                        _cts.Token);
                }
                return;
            }

            var validatedSignal = validationResult.ValidatedSignal;

            // Notify about signal
            if (_notifier != null && _settings.Notifications.NotifyOnSignalReceived)
            {
                var warnings = validatedSignal.ValidationWarnings.Any()
                    ? $"\n⚠️ {string.Join("\n", validatedSignal.ValidationWarnings)}"
                    : "";

                await _notifier.SendMessageAsync(
                    $"📊 Signal received\n" +
                    $"Symbol: {validatedSignal.Symbol}\n" +
                    $"Direction: {validatedSignal.Direction}\n" +
                    $"Entry: {validatedSignal.Entry}\n" +
                    $"SL: {validatedSignal.AdjustedStopLoss} (orig: {validatedSignal.OriginalStopLoss})\n" +
                    $"Leverage: {validatedSignal.AdjustedLeverage}x\n" +
                    $"R:R: {validatedSignal.RiskRewardRatio:F2}" +
                    warnings,
                    _cts.Token);
            }

            // Execute signal
            _logger.Information("Executing signal for {Symbol}", validatedSignal.Symbol);
            var position = await _signalTrader.ExecuteSignalAsync(validatedSignal, balance, _cts.Token);

            _logger.Information("Position opened: {PositionId} for {Symbol} @ {Entry}",
                position.Id, position.Symbol, position.ActualEntryPrice);

            // Notify about position opened
            if (_notifier != null && _settings.Notifications.NotifyOnPositionOpened)
            {
                await _notifier.SendMessageAsync(
                    $"✅ Position opened\n" +
                    $"Symbol: {position.Symbol}\n" +
                    $"Direction: {position.Direction}\n" +
                    $"Entry: {position.ActualEntryPrice}\n" +
                    $"SL: {position.CurrentStopLoss}\n" +
                    $"Quantity: {position.InitialQuantity}\n" +
                    $"Leverage: {position.Leverage}x\n" +
                    $"Targets: {position.Targets.Count}",
                    _cts.Token);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error processing signal for {Symbol}", signal.Symbol);

            if (_notifier != null)
            {
                await _notifier.SendMessageAsync(
                    $"❌ Error executing signal\n" +
                    $"Symbol: {signal.Symbol}\n" +
                    $"Error: {ex.Message}",
                    _cts!.Token);
            }
        }
        finally
        {
            _signalProcessingLock.Release();
        }
    }

    private async void HandleTargetHit(Guid positionId, int targetIndex, decimal fillPrice)
    {
        try
        {
            _logger.Information("Target {Index} hit for position {PositionId} @ {Price}",
                targetIndex, positionId, fillPrice);

            var position = await _store.GetPositionAsync(positionId, _cts!.Token);
            if (position == null)
            {
                _logger.Warning("Position {PositionId} not found", positionId);
                return;
            }

            await _positionManager.HandleTargetHitAsync(position, targetIndex, fillPrice, _cts.Token);

            _logger.Information("Target {Index} processed for {Symbol}", targetIndex, position.Symbol);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error handling target hit for position {PositionId}", positionId);
        }
    }

    private async void HandleStopLossHit(Guid positionId, decimal fillPrice)
    {
        try
        {
            _logger.Information("Stop loss hit for position {PositionId} @ {Price}",
                positionId, fillPrice);

            var position = await _store.GetPositionAsync(positionId, _cts!.Token);
            if (position == null)
            {
                _logger.Warning("Position {PositionId} not found", positionId);
                return;
            }

            await _positionManager.HandleStopLossHitAsync(position, fillPrice, _cts.Token);

            _logger.Information("Stop loss processed for {Symbol}", position.Symbol);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error handling stop loss hit for position {PositionId}", positionId);
        }
    }

    private async Task HandleDuplicateSignal(TradingSignal signal, SignalPosition existingPosition)
    {
        var sameDirection = (signal.Direction == SignalDirection.Long && existingPosition.Direction == SignalDirection.Long) ||
                           (signal.Direction == SignalDirection.Short && existingPosition.Direction == SignalDirection.Short);

        if (sameDirection)
        {
            _logger.Information("Duplicate signal for {Symbol} in same direction: {Action}",
                signal.Symbol, _settings.DuplicateHandling.SameDirection);

            // Handle according to configuration
            switch (_settings.DuplicateHandling.SameDirection)
            {
                case "Ignore":
                    _logger.Information("Ignoring duplicate signal for {Symbol}", signal.Symbol);
                    break;
                case "Add":
                    _logger.Warning("Add duplicate position not yet implemented");
                    break;
                case "Increase":
                    _logger.Warning("Increase position not yet implemented");
                    break;
            }
        }
        else
        {
            _logger.Information("Signal for {Symbol} in opposite direction: {Action}",
                signal.Symbol, _settings.DuplicateHandling.OppositeDirection);

            // Handle according to configuration
            switch (_settings.DuplicateHandling.OppositeDirection)
            {
                case "Ignore":
                    _logger.Information("Ignoring opposite direction signal for {Symbol}", signal.Symbol);
                    break;
                case "Close":
                    _logger.Warning("Close existing position not yet implemented");
                    break;
                case "Flip":
                    _logger.Warning("Flip position not yet implemented");
                    break;
            }
        }

        await Task.CompletedTask;
    }
}
