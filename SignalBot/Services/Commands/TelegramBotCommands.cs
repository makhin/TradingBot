using System.Text;
using SignalBot.Models;
using SignalBot.Services.Trading;
using SignalBot.Services.Statistics;
using SignalBot.State;
using TradingBot.Binance.Futures.Interfaces;
using TradingBot.Core.Models;
using Serilog;
using SignalBot.Services;
using Microsoft.Extensions.Options;
using SignalBot.Configuration;

namespace SignalBot.Services.Commands;

/// <summary>
/// Implementation of bot commands for Telegram
/// </summary>
public class TelegramBotCommands : IBotCommands
{
    private readonly BotController _controller;
    private readonly CooldownManager _cooldownManager;
    private readonly IPositionManager _positionManager;
    private readonly IPositionStore<SignalPosition> _store;
    private readonly IFuturesOrderExecutor _orderExecutor;
    private readonly IBinanceFuturesClient _client;
    private readonly ITradeStatisticsService _tradeStatistics;
    private readonly string _quoteCurrency;
    private readonly string _symbolExample;
    private readonly ILogger _logger;

    public TelegramBotCommands(
        BotController controller,
        CooldownManager cooldownManager,
        IPositionManager positionManager,
        IPositionStore<SignalPosition> store,
        IFuturesOrderExecutor orderExecutor,
        IBinanceFuturesClient client,
        ITradeStatisticsService tradeStatistics,
        IOptions<SignalBotSettings> settings,
        ILogger? logger = null)
    {
        _controller = controller;
        _cooldownManager = cooldownManager;
        _positionManager = positionManager;
        _store = store;
        _orderExecutor = orderExecutor;
        _client = client;
        _tradeStatistics = tradeStatistics;
        _quoteCurrency = string.IsNullOrWhiteSpace(settings.Value.Trading.DefaultSymbolSuffix)
            ? "USDT"
            : settings.Value.Trading.DefaultSymbolSuffix.Trim().ToUpperInvariant();
        _symbolExample = $"BTC{_quoteCurrency}";
        _logger = logger ?? Log.ForContext<TelegramBotCommands>();
    }

    public async Task<string> GetStatusAsync(CancellationToken ct = default)
    {
        try
        {
            var positions = await _store.GetOpenPositionsAsync(ct);
            var balance = await _client.GetBalanceAsync(_quoteCurrency, ct);
            var cooldownStatus = _cooldownManager.GetStatus();

            var sb = new StringBuilder();
            sb.AppendLine("🤖 **SignalBot Status**");
            sb.AppendLine();
            sb.AppendLine($"Mode: `{_controller.CurrentMode}`");
            sb.AppendLine($"Balance: `{balance:F2} {_quoteCurrency}`");
            sb.AppendLine($"Open positions: `{positions.Count}`");

            if (positions.Any())
            {
                decimal totalPnl = positions.Sum(p => p.RealizedPnl + p.UnrealizedPnl);
                sb.AppendLine($"Total P&L: `{totalPnl:+0.00;-0.00} {_quoteCurrency}`");
            }

            var statsReport = await _tradeStatistics.GetReportAsync(ct: ct);
            if (statsReport?.Windows?.Any() == true)
            {
                sb.AppendLine();
                sb.AppendLine("📈 **Trade Stats**");
                foreach (var window in statsReport.Windows)
                {
                    sb.AppendLine(
                        $"{window.Name}: trades `{window.TradeCount}`, " +
                        $"profit `{window.Profit:+0.00;-0.00} {_quoteCurrency}`, " +
                        $"loss `{window.Loss:+0.00;-0.00} {_quoteCurrency}`, " +
                        $"net `{window.Net:+0.00;-0.00} {_quoteCurrency}`");
                }
            }

            // Cooldown status
            if (cooldownStatus.IsInCooldown)
            {
                sb.AppendLine();
                sb.AppendLine("⏳ **Cooldown Active**");
                sb.AppendLine($"Reason: {cooldownStatus.Reason}");
                sb.AppendLine(@$"Remaining: `{cooldownStatus.RemainingTime:hh\:mm\:ss}`");
                sb.AppendLine($"Consecutive losses: `{cooldownStatus.ConsecutiveLosses}`");
            }
            else if (cooldownStatus.ConsecutiveLosses > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"⚠️ Consecutive losses: `{cooldownStatus.ConsecutiveLosses}`");
                sb.AppendLine($"Position size: `{cooldownStatus.CurrentSizeMultiplier:P0}`");
            }

            return sb.ToString();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error getting status");
            return $"❌ Error: {ex.Message}";
        }
    }

    public async Task<string> GetPositionsAsync(CancellationToken ct = default)
    {
        try
        {
            var positions = await _store.GetOpenPositionsAsync(ct);

            if (!positions.Any())
            {
                return "📭 No open positions";
            }

            var sb = new StringBuilder();
            sb.AppendLine("📊 **Open Positions**");
            sb.AppendLine();

            foreach (var pos in positions)
            {
                var direction = pos.Direction == SignalDirection.Long ? "🟢 LONG" : "🔴 SHORT";
                var pnl = pos.RealizedPnl + pos.UnrealizedPnl;
                var pnlEmoji = pnl >= 0 ? "📈" : "📉";

                decimal? markPrice = null;
                decimal? entryDeltaPercent = null;
                decimal? stopLossDeltaPercent = null;

                try
                {
                    markPrice = await _client.GetMarkPriceAsync(pos.Symbol, ct);
                    if (markPrice > 0 && pos.ActualEntryPrice > 0)
                    {
                        entryDeltaPercent = (markPrice - pos.ActualEntryPrice) / pos.ActualEntryPrice * 100m;
                    }

                    if (markPrice > 0 && pos.CurrentStopLoss > 0)
                    {
                        stopLossDeltaPercent = (markPrice - pos.CurrentStopLoss) / pos.CurrentStopLoss * 100m;
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "Failed to fetch mark price for {Symbol}", pos.Symbol);
                }

                sb.AppendLine($"**{pos.Symbol}** {direction}");
                sb.AppendLine($"  Entry: `{pos.ActualEntryPrice:F4}`");
                sb.AppendLine($"  Qty: `{pos.RemainingQuantity:F4}` / `{pos.InitialQuantity:F4}`");
                sb.AppendLine($"  SL: `{pos.CurrentStopLoss:F4}`");
                if (markPrice.HasValue)
                {
                    var entryDeltaText = entryDeltaPercent.HasValue
                        ? $" ({entryDeltaPercent:+0.00;-0.00}%)"
                        : string.Empty;
                    var stopLossDeltaText = stopLossDeltaPercent.HasValue
                        ? $" ({stopLossDeltaPercent:+0.00;-0.00}%)"
                        : string.Empty;

                    sb.AppendLine($"  Mark: `{markPrice.Value:F4}`{entryDeltaText}");
                    sb.AppendLine($"  Distance to SL: `{markPrice.Value - pos.CurrentStopLoss:+0.0000;-0.0000}`{stopLossDeltaText}");
                }
                else
                {
                    sb.AppendLine("  Mark: `N/A`");
                }
                sb.AppendLine($"  Targets hit: `{pos.TargetsHit}` / `{pos.Targets.Count}`");
                sb.AppendLine($"  P&L: {pnlEmoji} `{pnl:+0.00;-0.00} {_quoteCurrency}`");
                sb.AppendLine();
            }

            return sb.ToString();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error getting positions");
            return $"❌ Error: {ex.Message}";
        }
    }

    public Task<string> PauseAsync(CancellationToken ct = default)
    {
        try
        {
            _controller.SetMode(BotOperatingMode.Paused);
            _logger.Warning("Bot paused by command");

            return Task.FromResult(
                "⏸️ **Bot Paused**\n" +
                "New signals will be ignored.\n" +
                "Existing positions remain open.\n" +
                "Use /resume to continue trading.");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error pausing bot");
            return Task.FromResult($"❌ Error: {ex.Message}");
        }
    }

    public Task<string> ResumeAsync(CancellationToken ct = default)
    {
        try
        {
            _controller.SetMode(BotOperatingMode.Automatic);
            _logger.Information("Bot resumed by command");

            return Task.FromResult(
                "▶️ **Bot Resumed**\n" +
                "Trading has been resumed.\n" +
                "Bot will now accept new signals.");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error resuming bot");
            return Task.FromResult($"❌ Error: {ex.Message}");
        }
    }

    public async Task<string> CloseAllAsync(CancellationToken ct = default)
    {
        try
        {
            var positions = await _store.GetOpenPositionsAsync(ct);

            if (!positions.Any())
            {
                return "📭 No positions to close";
            }

            _logger.Warning("Closing all positions by command ({Count} positions)", positions.Count);

            var results = new List<string>();

            foreach (var position in positions)
            {
                try
                {
                    await ClosePositionInternalAsync(position, ct);
                    results.Add($"✅ {position.Symbol}: Closed @ market");
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Failed to close position {Symbol}", position.Symbol);
                    results.Add($"❌ {position.Symbol}: {ex.Message}");
                }
            }

            var sb = new StringBuilder();
            sb.AppendLine("🚪 **Closing All Positions**");
            sb.AppendLine();
            foreach (var result in results)
            {
                sb.AppendLine(result);
            }

            return sb.ToString();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error closing all positions");
            return $"❌ Error: {ex.Message}";
        }
    }

    public async Task<string> ClosePositionAsync(string symbol, CancellationToken ct = default)
    {
        try
        {
            var position = await _store.GetPositionBySymbolAsync(symbol.ToUpperInvariant(), ct);

            if (position == null)
            {
                return $"❌ Position not found: {symbol}";
            }

            if (position.Status != PositionStatus.Open && position.Status != PositionStatus.PartialClosed)
            {
                return $"❌ Position {symbol} is not open (status: {position.Status})";
            }

            _logger.Information("Closing position {Symbol} by command", symbol);

            await ClosePositionInternalAsync(position, ct);

            return $"✅ Position {symbol} closed at market price";
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error closing position {Symbol}", symbol);
            return $"❌ Error closing {symbol}: {ex.Message}";
        }
    }

    public async Task<string> EmergencyStopAsync(CancellationToken ct = default)
    {
        try
        {
            _logger.Error("EMERGENCY STOP initiated by command");

            _controller.SetMode(BotOperatingMode.EmergencyStop);

            var closeResult = await CloseAllAsync(ct);

            var sb = new StringBuilder();
            sb.AppendLine("🛑 **EMERGENCY STOP**");
            sb.AppendLine();
            sb.AppendLine("Bot has been stopped.");
            sb.AppendLine("All positions are being closed.");
            sb.AppendLine();
            sb.Append(closeResult);

            return sb.ToString();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error during emergency stop");
            return $"❌ Error during emergency stop: {ex.Message}";
        }
    }

    public Task<string> ResetCooldownAsync(CancellationToken ct = default)
    {
        try
        {
            var cooldownStatus = _cooldownManager.GetStatus();

            if (!cooldownStatus.IsInCooldown)
            {
                return Task.FromResult("ℹ️ No active cooldown to reset");
            }

            _cooldownManager.ForceResetCooldown();

            _logger.Warning("Cooldown manually reset via command");

            return Task.FromResult(
                "✅ **Cooldown Reset**\n" +
                "\n" +
                "Cooldown period has been cleared.\n" +
                "Bot can now accept new signals.\n" +
                "\n" +
                "⚠️ Use this command with caution!");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error resetting cooldown");
            return Task.FromResult($"❌ Error: {ex.Message}");
        }
    }

    public string GetHelp()
    {
        return
            "📚 **Available Commands**\n" +
            "\n" +
            "/status - Show bot status and balance\n" +
            "/positions - List open positions\n" +
            "/pause - Pause trading (ignore new signals)\n" +
            "/resume - Resume trading\n" +
            "/closeall - Close all open positions\n" +
            $"/close {_symbolExample} - Close specific position\n" +
            "/resetcooldown - Reset cooldown period\n" +
            "/stop - Emergency stop (close all & stop bot)\n" +
            "/help - Show this help message\n" +
            "\n" +
            "⚠️ Use commands carefully. All actions are logged.";
    }

    private async Task ClosePositionInternalAsync(SignalPosition position, CancellationToken ct)
    {
        // Cancel all pending orders (SL/TP)
        if (position.StopLossOrderId.HasValue)
        {
            try
            {
                await _orderExecutor.CancelOrderAsync(position.Symbol, position.StopLossOrderId.Value, ct);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Failed to cancel SL order for {Symbol}", position.Symbol);
            }
        }

        foreach (var tpOrderId in position.TakeProfitOrderIds)
        {
            try
            {
                await _orderExecutor.CancelOrderAsync(position.Symbol, tpOrderId, ct);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Failed to cancel TP order {OrderId} for {Symbol}", tpOrderId, position.Symbol);
            }
        }

        // Close position at market
        var direction = position.Direction == SignalDirection.Long
            ? TradeDirection.Short
            : TradeDirection.Long;

        var closeResult = await _orderExecutor.PlaceMarketOrderAsync(
            position.Symbol,
            direction,
            position.RemainingQuantity,
            ct);

        if (!closeResult.IsAcceptable)
        {
            throw new InvalidOperationException($"Failed to close position: {closeResult.RejectReason}");
        }

        // Update position
        var pnl = PnlCalculator.Calculate(
            position.ActualEntryPrice,
            closeResult.ActualPrice,
            position.RemainingQuantity,
            position.Direction);

        var updatedPosition = position with
        {
            RemainingQuantity = 0,
            RealizedPnl = position.RealizedPnl + pnl,
            Status = PositionStatus.Closed,
            ClosedAt = DateTime.UtcNow,
            CloseReason = PositionCloseReason.ManualClose
        };

        await _store.SavePositionAsync(updatedPosition, ct);
        await _tradeStatistics.RecordClosedPositionAsync(updatedPosition, ct);
    }
}
