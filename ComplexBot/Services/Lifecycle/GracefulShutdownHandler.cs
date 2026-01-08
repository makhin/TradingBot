using System;
using System.Threading;
using System.Threading.Tasks;
using ComplexBot.Services.State;
using ComplexBot.Services.Trading;
using ComplexBot.Services.Notifications;
using Spectre.Console;
using Serilog;

namespace ComplexBot.Services.Lifecycle;

/// <summary>
/// Action to take with open positions on shutdown
/// </summary>
public enum ShutdownAction
{
    KeepPositionsAndOrders,      // Do nothing, leave everything active
    ClosePositionsKeepOrders,    // Close at market, leave OCO orders
    ClosePositionsCancelOrders   // Close at market, cancel all OCO orders
}

/// <summary>
/// Manages graceful shutdown with state saving and user interaction
/// </summary>
public class GracefulShutdownHandler
{
    private readonly StateManager _stateManager;
    private readonly BinanceLiveTrader _trader;
    private readonly TelegramNotifier? _telegram;
    private readonly bool _isInteractive;
    private readonly ShutdownAction _defaultAction;
    private readonly ILogger _logger;
    private bool _isShuttingDown;

    public GracefulShutdownHandler(
        StateManager stateManager,
        BinanceLiveTrader trader,
        bool isInteractive,
        ShutdownAction defaultAction = ShutdownAction.KeepPositionsAndOrders,
        TelegramNotifier? telegram = null,
        ILogger? logger = null)
    {
        _stateManager = stateManager;
        _trader = trader;
        _isInteractive = isInteractive;
        _defaultAction = defaultAction;
        _telegram = telegram;
        _logger = logger ?? Log.ForContext<GracefulShutdownHandler>();
    }

    /// <summary>
    /// Initiates graceful shutdown process
    /// </summary>
    public async Task InitiateShutdownAsync(string reason, CancellationToken ct = default)
    {
        if (_isShuttingDown) return;
        _isShuttingDown = true;

        _logger.Information("🛑 Initiating graceful shutdown: {Reason}", reason);

        try
        {
            // 1. Build current state
            var state = await _trader.BuildCurrentState();

            // 2. Save state FIRST (before any actions)
            await _stateManager.SaveStateAsync(state, ct);
            _logger.Information("✅ State saved successfully");

            // 3. Determine action
            ShutdownAction action;
            if (state.OpenPositions.Count == 0)
            {
                _logger.Information("No open positions, proceeding with shutdown");
                action = ShutdownAction.KeepPositionsAndOrders;
            }
            else if (_isInteractive)
            {
                action = await PromptUserActionAsync(state);
            }
            else
            {
                action = _defaultAction;
                _logger.Information("Non-interactive mode: using default action {Action}", action);
            }

            // 4. Execute action
            await ExecuteShutdownActionAsync(action, state, ct);

            // 5. Stop trader
            await _trader.StopAsync();

            // 6. Send Telegram notification
            if (_telegram != null)
            {
                var posInfo = state.OpenPositions.Count > 0
                    ? $"\n📊 Positions: {state.OpenPositions.Count} ({action})"
                    : "\n✅ No open positions";
                await _telegram.SendMessageAsync($"🛑 Shutdown: {reason}{posInfo}", ct);
            }

            _logger.Information("👋 Shutdown complete");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error during graceful shutdown");
            throw;
        }
    }

    /// <summary>
    /// Prompts user for action in interactive mode
    /// </summary>
    private async Task<ShutdownAction> PromptUserActionAsync(BotState state)
    {
        await Task.CompletedTask; // Make it async for consistency

        AnsiConsole.MarkupLine($"\n[yellow]⚠️ You have {state.OpenPositions.Count} open position(s)[/]");

        // Display position details
        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Symbol")
            .AddColumn("Direction")
            .AddColumn("Qty")
            .AddColumn("Entry")
            .AddColumn("Current")
            .AddColumn("Unrealized PnL");

        foreach (var pos in state.OpenPositions)
        {
            var pnl = pos.Direction == Models.SignalType.Buy
                ? (pos.CurrentPrice - pos.EntryPrice) * pos.RemainingQuantity
                : (pos.EntryPrice - pos.CurrentPrice) * pos.RemainingQuantity;
            var pnlColor = pnl >= 0 ? "green" : "red";

            table.AddRow(
                pos.Symbol,
                pos.Direction.ToString(),
                $"{pos.RemainingQuantity:F5}",
                $"${pos.EntryPrice:F2}",
                $"${pos.CurrentPrice:F2}",
                $"[{pnlColor}]${pnl:F2}[/]"
            );
        }

        AnsiConsole.Write(table);

        var choice = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("\n[yellow]What would you like to do?[/]")
                .AddChoices(new[]
                {
                    "Keep positions & OCO orders (recommended)",
                    "Close positions at market, keep OCO orders",
                    "Close positions & cancel all OCO orders"
                })
        );

        return choice switch
        {
            var c when c.StartsWith("Keep") => ShutdownAction.KeepPositionsAndOrders,
            var c when c.Contains("keep OCO") => ShutdownAction.ClosePositionsKeepOrders,
            _ => ShutdownAction.ClosePositionsCancelOrders
        };
    }

    /// <summary>
    /// Executes the chosen shutdown action
    /// </summary>
    private async Task ExecuteShutdownActionAsync(ShutdownAction action, BotState state, CancellationToken ct)
    {
        switch (action)
        {
            case ShutdownAction.KeepPositionsAndOrders:
                _logger.Information("Keeping positions and OCO orders active on exchange");
                if (_isInteractive)
                {
                    AnsiConsole.MarkupLine("[green]✓[/] Positions and OCO orders remain active on exchange");
                }
                break;

            case ShutdownAction.ClosePositionsKeepOrders:
                _logger.Information("Closing positions at market, keeping OCO orders");
                if (_isInteractive)
                {
                    AnsiConsole.MarkupLine("[yellow]![/] Closing positions at market...");
                }
                foreach (var pos in state.OpenPositions)
                {
                    await _trader.ClosePosition(pos.Symbol, "Shutdown - close positions");
                }
                if (_isInteractive)
                {
                    AnsiConsole.MarkupLine("[green]✓[/] Positions closed, OCO orders remain active");
                }
                break;

            case ShutdownAction.ClosePositionsCancelOrders:
                _logger.Information("Closing positions and cancelling all OCO orders");
                if (_isInteractive)
                {
                    AnsiConsole.MarkupLine("[yellow]![/] Closing positions and cancelling OCO orders...");
                }
                foreach (var pos in state.OpenPositions)
                {
                    await _trader.ClosePosition(pos.Symbol, "Shutdown - close and cleanup");
                }
                foreach (var oco in state.ActiveOcoOrders)
                {
                    await _trader.CancelOcoOrdersForSymbol(oco.Symbol);
                }
                if (_isInteractive)
                {
                    AnsiConsole.MarkupLine("[green]✓[/] Positions closed, OCO orders cancelled");
                }
                break;
        }
    }
}
