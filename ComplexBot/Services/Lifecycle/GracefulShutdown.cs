using ComplexBot.Services.State;
using ComplexBot.Services.Trading;
using ComplexBot.Services.Notifications;

namespace ComplexBot.Services.Lifecycle;

public class GracefulShutdown
{
    private readonly CancellationTokenSource _cts = new();
    private readonly StateManager _stateManager;
    private readonly BinanceLiveTrader _trader;
    private readonly TelegramNotifier? _notifier;
    private bool _isShuttingDown = false;

    public GracefulShutdown(
        StateManager stateManager,
        BinanceLiveTrader trader,
        TelegramNotifier? notifier = null)
    {
        _stateManager = stateManager;
        _trader = trader;
        _notifier = notifier;

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
            Console.WriteLine("⚠️ Shutdown timed out during process exit.");
        }
    }

    public async Task ShutdownAsync(string reason, CancellationToken cancellationToken = default)
    {
        if (_isShuttingDown)
            return;

        _isShuttingDown = true;

        Console.WriteLine($"\n🛑 Initiating graceful shutdown: {reason}");

        // 1. Остановить приём новых сигналов
        _cts.Cancel();

        // 2. Сохранить текущее состояние
        Console.WriteLine("💾 Saving current state...");
        var state = await _trader.BuildCurrentState();
        await _stateManager.SaveState(state, cancellationToken);

        // 3. Спросить о закрытии позиций (если интерактивный режим)
        if (state.OpenPositions.Any() && Console.IsInputRedirected == false)
        {
            Console.WriteLine($"\n⚠️ You have {state.OpenPositions.Count} open position(s).");
            Console.WriteLine("Choose action:");
            Console.WriteLine("  1. Keep positions open (OCO orders remain active)");
            Console.WriteLine("  2. Close all positions at market");
            Console.WriteLine("  3. Close positions and cancel OCO orders");
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
                    Console.WriteLine("✅ Positions kept open with OCO protection");
                    break;
            }
        }

        // 4. Остановить трейдер
        Console.WriteLine("🛑 Stopping trader...");
        await _trader.StopAsync(cancellationToken);

        // 5. Уведомить
        if (_notifier != null)
        {
            var positionsInfo = state.OpenPositions.Any()
                ? $"\n📊 Open positions: {state.OpenPositions.Count}"
                : "\n✅ No open positions";

            await _notifier.SendMessageAsync($"🛑 Bot shutdown: {reason}{positionsInfo}", cancellationToken);
        }

        Console.WriteLine("👋 Goodbye!");
    }

    private async Task CloseAllPositionsAsync(bool cancelOco)
    {
        Console.WriteLine("📤 Closing all positions...");

        var positions = await _trader.GetOpenPositions();
        foreach (var position in positions)
        {
            if (cancelOco)
            {
                await _trader.CancelOcoOrdersForSymbol(position.Symbol);
            }

            await _trader.ClosePosition(position.Symbol, "Graceful shutdown");
            Console.WriteLine($"  ✅ Closed {position.Symbol}");
        }

        Console.WriteLine("✅ All positions closed");
    }

    public void Dispose()
    {
        _cts?.Dispose();
        Console.CancelKeyPress -= OnCancelKeyPress;
        AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
    }
}
