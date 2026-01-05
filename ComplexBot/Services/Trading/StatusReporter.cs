using Spectre.Console;
using Serilog;
using System.Text;

namespace ComplexBot.Services.Trading;

/// <summary>
/// Handles periodic status reporting to console and logs during live trading.
/// Displays current price, indicator values, position status, and other metrics.
/// </summary>
public class StatusReporter
{
    private readonly string _symbol;
    private readonly int _reportIntervalSeconds;
    private DateTime _lastReportTime = DateTime.MinValue;
    private int _candleCount = 0;

    public StatusReporter(string symbol, int reportIntervalSeconds = 60)
    {
        _symbol = symbol;
        _reportIntervalSeconds = reportIntervalSeconds;
    }

    /// <summary>
    /// Check if it's time to report and display status if needed.
    /// Call this on each candle update.
    /// </summary>
    public void CheckAndReport(decimal currentPrice, decimal? currentPosition, decimal equity, 
        decimal? entryPrice, decimal? stopLoss, decimal? takeProfit, object? strategyState = null)
    {
        _candleCount++;
        var now = DateTime.UtcNow;
        
        if ((now - _lastReportTime).TotalSeconds >= _reportIntervalSeconds)
        {
            ReportStatus(currentPrice, currentPosition, equity, entryPrice, stopLoss, takeProfit, strategyState);
            _lastReportTime = now;
        }
    }

    /// <summary>
    /// Immediately display and log current trading status.
    /// </summary>
    public void ReportStatus(decimal currentPrice, decimal? currentPosition, decimal equity,
        decimal? entryPrice, decimal? stopLoss, decimal? takeProfit, object? strategyState = null)
    {
        var timestamp = DateTime.UtcNow.ToString("HH:mm:ss");
        var isInPosition = currentPosition.HasValue && currentPosition != 0;
        
        // Build status message
        var statusMessage = new StringBuilder();
        statusMessage.AppendLine($"[yellow]╔══ {_symbol} Status @ {timestamp} ══╗[/]");
        statusMessage.AppendLine($"[cyan]Price:[/] [green]{currentPrice:F4}[/] USDT");
        statusMessage.AppendLine($"[cyan]Equity:[/] [green]{equity:F2}[/] USDT");
        
        if (isInPosition && currentPosition.HasValue)
        {
            var unrealizedPnL = CalculateUnrealizedPnL(currentPrice, entryPrice, currentPosition.Value);
            var pnlPercent = entryPrice.HasValue ? (unrealizedPnL / ((entryPrice!.Value) * Math.Abs(currentPosition.Value))) * 100 : 0;
            var pnlColor = unrealizedPnL >= 0 ? "green" : "red";
            
            statusMessage.AppendLine($"[cyan]Position:[/] [{pnlColor}]{currentPosition:F4}[/] contracts");
            var entryStr = entryPrice.HasValue ? entryPrice.Value.ToString("F4") : "N/A";
            var slStr = stopLoss.HasValue ? stopLoss.Value.ToString("F4") : "N/A";
            var tpStr = takeProfit.HasValue ? takeProfit.Value.ToString("F4") : "N/A";
            statusMessage.AppendLine($"[cyan]Entry:[/] {entryStr} | SL: {slStr} | TP: {tpStr}");
            statusMessage.AppendLine($"[cyan]Unrealized P&L:[/] [{pnlColor}]{unrealizedPnL:F2} ({pnlPercent:F2}%)[/]");
        }
        else
        {
            statusMessage.AppendLine("[cyan]Position:[/] [yellow]NONE (waiting for signal)[/]");
        }
        
        if (strategyState != null)
        {
            statusMessage.AppendLine($"[cyan]Strategy:[/] {strategyState}");
        }
        
        statusMessage.Append("[yellow]╚═══════════════════════════════════╝[/]");

        // Display to console
        AnsiConsole.MarkupLine(statusMessage.ToString());
        
        // Log to file
        var posStr = isInPosition && currentPosition.HasValue ? currentPosition.Value.ToString("F4") : "NONE";
        var logMessage = $"Status Update @ {timestamp} | Price: {currentPrice:F4} | Equity: {equity:F2} USDT | " +
                        $"Position: {posStr}";
        if (isInPosition && entryPrice.HasValue && currentPosition.HasValue)
        {
            var unrealizedPnL = CalculateUnrealizedPnL(currentPrice, entryPrice, currentPosition.Value);
            logMessage += $" | Unrealized P&L: {unrealizedPnL:F2}";
        }
        
        Log.Information(logMessage);
    }

    /// <summary>
    /// Report when waiting for a signal (less frequent logging).
    /// </summary>
    public void ReportWaitingForSignal(decimal currentPrice, decimal equity)
    {
        if (_candleCount % 10 == 0)  // Log every 10 candles when waiting
        {
            var timestamp = DateTime.UtcNow.ToString("HH:mm:ss");
            Log.Debug("[{Timestamp}] Waiting for signal | {Symbol} @ {Price:F4} USDT | Equity: {Equity:F2} USDT",
                timestamp, _symbol, currentPrice, equity);
        }
    }

    /// <summary>
    /// Report immediately when signal is generated.
    /// </summary>
    public void ReportSignalGenerated(string signalType, decimal price, decimal? currentPosition, decimal equity)
    {
        var timestamp = DateTime.UtcNow.ToString("HH:mm:ss");
        var color = signalType switch
        {
            "Buy" => "green",
            "Sell" => "red",
            "Exit" => "yellow",
            _ => "cyan"
        };
        
        AnsiConsole.MarkupLine($"\n[{color}]╔══ SIGNAL GENERATED @ {timestamp} ══╗[/]");
        AnsiConsole.MarkupLine($"[{color}]Type: {signalType}[/]");
        AnsiConsole.MarkupLine($"[{color}]Price: {price:F4}[/]");
        AnsiConsole.MarkupLine($"[{color}]Position: {(currentPosition == 0 || !currentPosition.HasValue ? "NONE" : currentPosition.Value.ToString("F4"))}[/]");
        AnsiConsole.MarkupLine($"[{color}]Equity: {equity:F2}[/]");
        AnsiConsole.MarkupLine($"[{color}]╚═══════════════════════════════════╝[/]\n");
        
        Log.Warning("[{Symbol}] SIGNAL {SignalType} @ {Price:F4}",
            _symbol, signalType, price);
    }

    /// <summary>
    /// Report trade execution with entry and exit details.
    /// </summary>
    public void ReportTradeExecution(string direction, decimal entryPrice, decimal exitPrice, decimal quantity, decimal realizedPnL, string tradeResult)
    {
        var timestamp = DateTime.UtcNow.ToString("HH:mm:ss");
        var color = realizedPnL >= 0 ? "green" : "red";
        
        AnsiConsole.MarkupLine($"\n[{color}]╔══ TRADE EXECUTED @ {timestamp} ══╗[/]");
        AnsiConsole.MarkupLine($"[{color}]Direction: {direction}[/]");
        AnsiConsole.MarkupLine($"[{color}]Quantity: {quantity:F4}[/]");
        AnsiConsole.MarkupLine($"[{color}]Entry Price: {entryPrice:F4}[/]");
        AnsiConsole.MarkupLine($"[{color}]Exit Price: {exitPrice:F4}[/]");
        AnsiConsole.MarkupLine($"[{color}]Result: {tradeResult} | P&L: {realizedPnL:F2}[/]");
        AnsiConsole.MarkupLine($"[{color}]╚═══════════════════════════════════╝[/]\n");
        
        Log.Warning("[{Symbol}] TRADE EXECUTED | Direction: {Direction} | Entry: {Entry:F4} | Exit: {Exit:F4} | P&L: {PnL:F2}",
            _symbol, direction, entryPrice, exitPrice, realizedPnL);
    }

    private decimal CalculateUnrealizedPnL(decimal currentPrice, decimal? entryPrice, decimal position)
    {
        if (!entryPrice.HasValue || position == 0)
            return 0;

        return (currentPrice - entryPrice.Value) * Math.Abs(position);
    }
}
