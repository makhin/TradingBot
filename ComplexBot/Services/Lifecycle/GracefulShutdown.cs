using ComplexBot.Services.State;
using ComplexBot.Services.Trading;
using ComplexBot.Services.Notifications;
using Serilog;

namespace ComplexBot.Services.Lifecycle;

public class GracefulShutdown
{
    private readonly CancellationTokenSource _cts = new();
    private readonly StateManager _stateManager;
    private readonly BinanceLiveTrader _trader;
    private readonly TelegramNotifier? _notifier;
    private readonly ILogger _logger;
    private bool _isShuttingDown = false;

    public GracefulShutdown(
        StateManager stateManager,
        BinanceLiveTrader trader,
        TelegramNotifier? notifier = null,
        ILogger? logger = null)
    {
        _stateManager = stateManager;
        _trader = trader;
        _notifier = notifier;
        _logger = logger ?? Log.ForContext<GracefulShutdown>();

        // Регистрация обработчиков сигналов
        Console.CancelKeyPress += OnCancelKeyPress;
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
    }

    public CancellationToken Token => _cts.Token;

    private void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
    {
        e.Cancel = true; // Предотвратить немедленное завершение
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        _ = ShutdownAsync("Ctrl+C pressed", timeoutCts.Token);
    }

    private void OnProcessExit(object? sender, EventArgs e)
    {
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            ShutdownAsync("Process exit", timeoutCts.Token).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            _logger.Warning("Shutdown timed out during process exit.");
        }
    }

    public async Task ShutdownAsync(string reason, CancellationToken cancellationToken = default)
    {
        if (_isShuttingDown)
            return;

        _isShuttingDown = true;

        _logger.Information("Initiating graceful shutdown: {Reason}", reason);

        // 1. Остановить приём новых сигналов
        _cts.Cancel();

        // 2. Сохранить текущее состояние
        _logger.Information("Saving current state...");
        var state = await _trader.BuildCurrentState();
        await _stateManager.SaveState(state, cancellationToken);

        // 3. Спросить о закрытии позиций (если интерактивный режим)
        if (state.OpenPositions.Any() && Console.IsInputRedirected == false)
        {
            _logger.Warning("You have {PositionCount} open position(s).", state.OpenPositions.Count);
            _logger.Information("Choose action:");
            _logger.Information("  1. Keep positions open (OCO orders remain active)");
            _logger.Information("  2. Close all positions at market");
            _logger.Information("  3. Close positions and cancel OCO orders");
            Console.Write("Your choice [1]: ");

            var choice = Console.ReadLine();

            switch (choice)
            {
                case "2":
                    await CloseAllPositionsAsync(cancelOco: false);
                    break;
                case "3":
                    await CloseAllPositionsAsync(cancelOco: true);
                    break;
                default:
                    _logger.Information("Positions kept open with OCO protection");
                    break;
            }
        }

        // 4. Остановить трейдер
        _logger.Information("Stopping trader...");
        await _trader.StopAsync(cancellationToken);

        // 5. Уведомить
        if (_notifier != null)
        {
            var positionsInfo = state.OpenPositions.Any()
                ? $"\n📊 Open positions: {state.OpenPositions.Count}"
                : "\n✅ No open positions";

            await _notifier.SendMessageAsync($"🛑 Bot shutdown: {reason}{positionsInfo}", cancellationToken);
        }

        _logger.Information("Goodbye!");
    }

    private async Task CloseAllPositionsAsync(bool cancelOco)
    {
        _logger.Information("Closing all positions...");

        var positions = await _trader.GetOpenPositions();
        foreach (var position in positions)
        {
            if (cancelOco)
            {
                await _trader.CancelOcoOrdersForSymbol(position.Symbol);
            }

            await _trader.ClosePosition(position.Symbol, "Graceful shutdown");
            _logger.Information("Closed {Symbol}", position.Symbol);
        }

        _logger.Information("All positions closed");
    }

    public void Dispose()
    {
        _cts?.Dispose();
        Console.CancelKeyPress -= OnCancelKeyPress;
        AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
    }
}
