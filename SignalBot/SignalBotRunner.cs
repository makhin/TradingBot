using SignalBot.Configuration;
using SignalBot.Models;
using SignalBot.Services;
using SignalBot.Services.Commands;
using SignalBot.Services.Monitoring;
using SignalBot.Services.Telegram;
using SignalBot.Services.Trading;
using SignalBot.Services.Validation;
using SignalBot.State;
using TradingBot.Binance.Futures.Interfaces;
using TradingBot.Core.Notifications;
using Serilog;
using Serilog.Context;
using Microsoft.Extensions.Options;

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
    private readonly BotController _botController;
    private readonly CooldownManager _cooldownManager;
    private readonly TelegramCommandHandler? _commandHandler;
    private readonly INotifier? _notifier;
    private readonly ILogger _logger;

    private readonly SemaphoreSlim _signalProcessingLock = new(1, 1);
    private CancellationTokenSource? _cts;
    private bool _isRunning;

    public SignalBotRunner(
        IOptions<SignalBotSettings> settings,
        IBinanceFuturesClient client,
        ITelegramSignalListener telegramListener,
        SignalParser signalParser,
        ISignalValidator signalValidator,
        ISignalTrader signalTrader,
        IPositionManager positionManager,
        IOrderMonitor orderMonitor,
        IPositionStore<SignalPosition> store,
        BotController botController,
        CooldownManager cooldownManager,
        TelegramCommandHandler? commandHandler = null,
        INotifier? notifier = null,
        ILogger? logger = null)
    {
        _settings = settings.Value;
        _client = client;
        _telegramListener = telegramListener;
        _signalParser = signalParser;
        _signalValidator = signalValidator;
        _signalTrader = signalTrader;
        _positionManager = positionManager;
        _orderMonitor = orderMonitor;
        _store = store;
        _botController = botController;
        _cooldownManager = cooldownManager;
        _commandHandler = commandHandler;
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
            // Check if Futures trading is enabled
            if (!_settings.EnableFuturesTrading)
            {
                _logger.Warning("⚠️ Futures trading is DISABLED in configuration");
                _logger.Information("SignalBot will run in monitoring-only mode");
                await StartInMonitoringOnlyMode(_cts.Token);
                return;
            }

            // Test connectivity to Futures API
            try
            {
                var connected = await _client.TestConnectivityAsync(_cts.Token);
                if (!connected)
                {
                    throw new InvalidOperationException("Failed to connect to Binance Futures API");
                }

                _logger.Information("Connected to Binance Futures API");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to connect to Binance Futures API");
                
                if (ex.Message.Contains("Invalid API-key") || ex.Message.Contains("permissions"))
                {
                    _logger.Warning("⚠️ Futures API credentials issue detected");
                    _logger.Information("To disable Futures trading, set 'EnableFuturesTrading' to false in appsettings.json");
                    _logger.Information("Or set environment variable: TRADING_SignalBot__EnableFuturesTrading=false");
                }
                
                throw;
            }

            // Subscribe to events
            _telegramListener.OnSignalReceived += HandleSignalReceived;
            _orderMonitor.OnTargetHit += HandleTargetHit;
            _orderMonitor.OnStopLossHit += HandleStopLossHit;

            // Start order monitor
            await _orderMonitor.StartAsync(_cts.Token);

            // Start Telegram listener
            await _telegramListener.StartAsync(_cts.Token);

            // Start command handler (if configured)
            if (_commandHandler != null)
            {
                await _commandHandler.StartAsync(_cts.Token);
                _logger.Information("Telegram command handler started");
            }

            _isRunning = true;
            _logger.Information("SignalBot started successfully");

            await SendNotificationAsync(BuildStartMessage(), _cts.Token);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to start SignalBot");
            await StopAsync();
            throw;
        }
    }

    /// <summary>
    /// Start SignalBot in monitoring-only mode (no trading)
    /// </summary>
    private async Task StartInMonitoringOnlyMode(CancellationToken ct)
    {
        try
        {
            // Subscribe to events (except trading-related ones)
            _telegramListener.OnSignalReceived += (signal) =>
            {
                _logger.Information("Received signal (MONITORING ONLY): {Symbol} {Direction} at {Price}",
                    signal.Symbol, signal.Direction, signal.Entry);
            };

            // Start Telegram listener only
            await _telegramListener.StartAsync(ct);

            // Start command handler (if configured)
            if (_commandHandler != null)
            {
                await _commandHandler.StartAsync(ct);
                _logger.Information("Telegram command handler started (monitoring mode)");
            }

            _isRunning = true;
            _logger.Information("✅ SignalBot started in MONITORING-ONLY mode (no trading)");

            await SendNotificationAsync(BuildStartMessage(monitoringOnly: true), ct);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to start monitoring-only mode");
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

            // Stop command handler
            if (_commandHandler != null)
                await _commandHandler.StopAsync();

            // Stop Telegram listener
            await _telegramListener.StopAsync();

            // Stop order monitor
            await _orderMonitor.StopAsync();

            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;

            _isRunning = false;
            _logger.Information("SignalBot stopped");

            await SendNotificationAsync(BuildStopMessage(), CancellationToken.None);
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
            using var signalContext = LogContext.PushProperty("SignalId", signal.Id);
            _logger.Information("Received signal: {Symbol} {Direction} @ {Entry}",
                signal.Symbol, signal.Direction, signal.Entry);

            var (canProcess, rejectReason, existingPosition) = await CanProcessSignalAsync(signal);
            if (!canProcess)
            {
                _logger.Warning("Signal ignored: {Reason}", rejectReason);
                if (existingPosition != null)
                {
                    await HandleDuplicateSignal(signal, existingPosition);
                }
                return;
            }

            var (isValid, validatedSignal, balance) = await TryValidateSignalAsync(signal);
            if (!isValid || validatedSignal == null)
            {
                return;
            }

            await NotifySignalReceivedAsync(validatedSignal);

            var position = await OpenPositionAsync(validatedSignal, balance);

            if (position.Status == PositionStatus.Cancelled)
            {
                _logger.Information("Signal for {Symbol} was cancelled (e.g., price deviation)", position.Symbol);
                return;
            }

            await NotifyPositionOpenedAsync(position);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error processing signal for {Symbol}", signal.Symbol);

            await SendNotificationAsync(
                $"❌ Error executing signal\n" +
                $"Symbol: {signal.Symbol}\n" +
                $"Error: {ex.Message}",
                _cts!.Token);
        }
        finally
        {
            _signalProcessingLock.Release();
        }
    }

    private async Task<(bool CanProcess, string? RejectReason, SignalPosition? ExistingPosition)> CanProcessSignalAsync(
        TradingSignal signal)
    {
        if (!_botController.CanAcceptNewSignals())
        {
            return (false, $"Bot is in {_botController.CurrentMode} mode", null);
        }

        if (_cooldownManager.IsInCooldown)
        {
            var cooldownStatus = _cooldownManager.GetStatus();
            return (false,
                $"Cooldown active until {cooldownStatus.CooldownUntil} ({cooldownStatus.RemainingTime} remaining). Reason: {cooldownStatus.Reason}",
                null);
        }

        var openPositions = await _store.GetOpenPositionsAsync(_cts!.Token);
        if (openPositions.Count >= _settings.Trading.MaxConcurrentPositions)
        {
            return (false, $"Max concurrent positions ({_settings.Trading.MaxConcurrentPositions}) reached", null);
        }

        var existingPosition = openPositions.FirstOrDefault(p => p.Symbol == signal.Symbol);
        if (existingPosition != null)
        {
            return (false, $"Position already exists for {signal.Symbol}", existingPosition);
        }

        return (true, null, null);
    }

    private async Task<(bool IsValid, TradingSignal? ValidatedSignal, decimal Balance)> TryValidateSignalAsync(
        TradingSignal signal)
    {
        var quoteCurrency = string.IsNullOrWhiteSpace(_settings.Trading.DefaultSymbolSuffix)
            ? "USDT"
            : _settings.Trading.DefaultSymbolSuffix.Trim().ToUpperInvariant();
        var balance = await _client.GetBalanceAsync(quoteCurrency, _cts!.Token);
        _logger.Information("Current {Currency} balance: {Balance}", quoteCurrency, balance);

        var validationResult = await _signalValidator.ValidateAndAdjustAsync(signal, balance, _cts.Token);
        if (!validationResult.IsSuccess || validationResult.ValidatedSignal == null)
        {
            _logger.Warning("Signal validation failed for {Symbol}: {Error}",
                signal.Symbol, validationResult.ErrorMessage);

            await SendNotificationAsync(
                BuildValidationFailedMessage(signal, validationResult.ErrorMessage),
                _cts.Token);

            return (false, null, balance);
        }

        return (true, validationResult.ValidatedSignal, balance);
    }

    private async Task NotifySignalReceivedAsync(TradingSignal validatedSignal)
    {
        if (!_settings.Notifications.NotifyOnSignalReceived)
        {
            return;
        }

        var warnings = validatedSignal.ValidationWarnings.Any()
            ? $"\n⚠️ {string.Join("\n", validatedSignal.ValidationWarnings)}"
            : "";

        await SendNotificationAsync(
            $"📊 Signal received\n" +
            $"Symbol: {validatedSignal.Symbol}\n" +
            $"Direction: {validatedSignal.Direction}\n" +
            $"Entry: {validatedSignal.Entry}\n" +
            $"SL: {validatedSignal.AdjustedStopLoss} (orig: {validatedSignal.OriginalStopLoss})\n" +
            $"Leverage: {validatedSignal.AdjustedLeverage}x\n" +
            $"R:R: {validatedSignal.RiskRewardRatio:F2}" +
            warnings,
            _cts!.Token);
    }

    private async Task<SignalPosition> OpenPositionAsync(TradingSignal validatedSignal, decimal balance)
    {
        _logger.Information("Executing signal for {Symbol}", validatedSignal.Symbol);
        var position = await _signalTrader.ExecuteSignalAsync(validatedSignal, balance, _cts!.Token);

        using (LogContext.PushProperty("PositionId", position.Id))
        {
            _logger.Information("Position opened: {PositionId} for {Symbol} @ {Entry}",
                position.Id, position.Symbol, position.ActualEntryPrice);
        }

        return position;
    }

    private async Task NotifyPositionOpenedAsync(SignalPosition position)
    {
        if (!_settings.Notifications.NotifyOnPositionOpened)
        {
            return;
        }

        await SendNotificationAsync(BuildPositionOpenedMessage(position), _cts!.Token);
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

            // Get updated position to pass to cooldown manager if fully closed
            var updatedPosition = await _store.GetPositionAsync(positionId, _cts.Token);
            if (updatedPosition is { Status: PositionStatus.Closed, CloseReason: PositionCloseReason.AllTargetsHit })
            {
                _cooldownManager.OnPositionClosed(updatedPosition);
            }

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

            // Get updated position to pass to cooldown manager
            var updatedPosition = await _store.GetPositionAsync(positionId, _cts.Token);
            if (updatedPosition is { Status: PositionStatus.Closed })
            {
                _cooldownManager.OnPositionClosed(updatedPosition);
            }

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

    private async Task SendNotificationAsync(string message, CancellationToken ct)
    {
        if (_notifier == null)
        {
            return;
        }

        await _notifier.SendMessageAsync(message, ct);
    }

    private string BuildStartMessage(bool monitoringOnly = false)
    {
        var monitoredChannels = _settings.Telegram.Parsing.ChannelParsers.Count;
        if (monitoringOnly)
        {
            return "⚠️ SignalBot started in MONITORING-ONLY mode\n" +
                   "Futures trading is DISABLED\n" +
                   $"Monitoring {monitoredChannels} Telegram channel(s)\n" +
                   "To enable trading, set EnableFuturesTrading to true";
        }

        return "✅ SignalBot started\n" +
               $"Monitoring {monitoredChannels} Telegram channel(s)\n" +
               $"Max concurrent positions: {_settings.Trading.MaxConcurrentPositions}";
    }

    private string BuildStopMessage()
    {
        return "🛑 SignalBot stopped";
    }

    private string BuildValidationFailedMessage(TradingSignal signal, string? errorMessage)
    {
        return "❌ Signal validation failed\n" +
               $"Symbol: {signal.Symbol}\n" +
               $"Reason: {errorMessage}";
    }

    private string BuildPositionOpenedMessage(SignalPosition position)
    {
        return "✅ Position opened\n" +
               $"Symbol: {position.Symbol}\n" +
               $"Direction: {position.Direction}\n" +
               $"Entry: {position.ActualEntryPrice}\n" +
               $"SL: {position.CurrentStopLoss}\n" +
               $"Quantity: {position.InitialQuantity}\n" +
               $"Leverage: {position.Leverage}x\n" +
               $"Targets: {position.Targets.Count}";
    }
}
