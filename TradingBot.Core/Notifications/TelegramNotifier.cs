using System.Globalization;
using System.Text;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;
using TradingBot.Core.Models;
using TradingBot.Core.Analytics;
using Serilog;

namespace TradingBot.Core.Notifications;

public class TelegramNotifier : INotifier
{
    private readonly TelegramBotClient? _bot;
    private readonly long _chatId;
    private readonly bool _enabled;
    private readonly ILogger _logger;

    public TelegramNotifier(string? botToken, long chatId, ILogger? logger = null)
    {
        _logger = logger ?? Log.ForContext<TelegramNotifier>();
        _enabled = !string.IsNullOrEmpty(botToken);
        if (_enabled)
        {
            _bot = new TelegramBotClient(botToken!);
            _chatId = chatId;
        }
    }

    public async Task SendTradeOpen(TradeSignal signal, decimal quantity, decimal riskAmount, CancellationToken cancellationToken = default)
    {
        if (!_enabled) return;

        var emoji = signal.Type == SignalType.Buy ? "🟢" : "🔴";
        var direction = EscapeMarkdownV2(signal.Type == SignalType.Buy ? "LONG" : "SHORT");
        var symbol = EscapeMarkdownV2(signal.Symbol);
        var reason = EscapeMarkdownV2(signal.Reason);
        var entryText = FormatDecimal(signal.Price);
        var stopLossText = FormatNullableDecimal(signal.StopLoss);
        var takeProfitText = FormatNullableDecimal(signal.TakeProfit);
        var quantityText = FormatDecimal(quantity, "F4");
        var riskText = FormatMoney(riskAmount);

        var message = $"""
            {emoji} *NEW TRADE OPENED*

            *{symbol}* {direction}

            📍 Entry: `{entryText}`
            🛑 Stop Loss: `{stopLossText}`
            🎯 Take Profit: `{takeProfitText}`

            📊 Size: `{quantityText}`
            💰 Risk: `{riskText}`

            📝 _{reason}_
            """;

        await SendMessage(message, cancellationToken);
    }

    public async Task SendTradeClose(string symbol, decimal entryPrice, decimal exitPrice,
        decimal pnl, decimal rMultiple, string reason, CancellationToken cancellationToken = default)
    {
        if (!_enabled) return;

        var emoji = pnl >= 0 ? "✅" : "❌";
        var pnlEmoji = pnl >= 0 ? "📈" : "📉";
        var symbolText = EscapeMarkdownV2(symbol);
        var entryText = FormatDecimal(entryPrice);
        var exitText = FormatDecimal(exitPrice);
        var pnlText = FormatMoney(pnl);
        var rMultipleText = FormatDecimal(rMultiple);
        var reasonText = EscapeMarkdownV2(reason);

        var message = $"""
            {emoji} *TRADE CLOSED*

            *{symbolText}*

            📍 Entry: `{entryText}`
            📍 Exit: `{exitText}`

            {pnlEmoji} PnL: `{pnlText}` `{rMultipleText}R`

            📝 _{reasonText}_
            """;

        await SendMessage(message, cancellationToken);
    }

    public async Task SendDrawdownAlert(decimal currentDrawdown, decimal dailyDrawdown, CancellationToken cancellationToken = default)
    {
        if (!_enabled) return;

        var totalDrawdownText = FormatPercentage(currentDrawdown);
        var dailyDrawdownText = FormatPercentage(dailyDrawdown);

        var message = $"""
            ⚠️ *DRAWDOWN ALERT*

            📉 Total Drawdown: `{totalDrawdownText}`
            📉 Daily Drawdown: `{dailyDrawdownText}`

            _Risk management may reduce position sizes_
            """;

        await SendMessage(message, cancellationToken);
    }

    public async Task SendCircuitBreakerTriggered(string reason, CancellationToken cancellationToken = default)
    {
        if (!_enabled) return;

        var reasonText = EscapeMarkdownV2(reason);

        var message = $"""
            🚨 *CIRCUIT BREAKER TRIGGERED*

            ⛔ Trading has been stopped!

            Reason: _{reasonText}_

            _Manual intervention required_
            """;

        await SendMessage(message, cancellationToken);
    }

    public async Task SendDailySummary(TradeJournalStats stats, decimal equity, decimal drawdown, CancellationToken cancellationToken = default)
    {
        if (!_enabled) return;

        var equityText = FormatMoney(equity);
        var drawdownText = FormatPercentage(drawdown);
        var tradesText = FormatInteger(stats.TotalTrades);
        var winRateText = FormatDecimal(stats.WinRate, "F1");
        var netPnlText = FormatMoney(stats.TotalNetPnL);
        var bestTradeText = FormatMoney(stats.LargestWin);
        var worstTradeText = FormatMoney(stats.LargestLoss);

        var message = $"""
            📊 *DAILY SUMMARY*

            💰 Equity: `{equityText}`
            📉 Drawdown: `{drawdownText}`

            📈 Trades Today: `{tradesText}`
            🎯 Win Rate: `{winRateText}%`
            💵 Net PnL: `{netPnlText}`

            Best Trade: `{bestTradeText}`
            Worst Trade: `{worstTradeText}`
            """;

        await SendMessage(message, cancellationToken);
    }

    public async Task SendError(string errorMessage, CancellationToken cancellationToken = default)
    {
        if (!_enabled) return;

        var errorText = EscapeMarkdownV2(errorMessage);

        var message = $"""
            ❌ *ERROR*

            {errorText}

            _Check logs for details_
            """;

        await SendMessage(message, cancellationToken);
    }

    public async Task SendMessageAsync(string message, CancellationToken cancellationToken = default)
    {
        await SendMessage(EscapeMarkdownV2(message), cancellationToken);
    }

    private async Task SendMessage(string message, CancellationToken cancellationToken)
    {
        if (_bot == null) return;

        try
        {
            await _bot.SendMessage(
                chatId: _chatId,
                text: message,
                parseMode: ParseMode.MarkdownV2,
                cancellationToken: cancellationToken
            );
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Telegram error sending message to chat {ChatId}", _chatId);
        }
    }

    private static string FormatDecimal(decimal value, string format = "F2")
        => EscapeMarkdownV2Code(value.ToString(format, CultureInfo.InvariantCulture));

    private static string FormatNullableDecimal(decimal? value, string format = "F2")
        => value.HasValue ? FormatDecimal(value.Value, format) : EscapeMarkdownV2Code("N/A");

    private static string FormatMoney(decimal value)
        => EscapeMarkdownV2Code($"${value.ToString("F2", CultureInfo.InvariantCulture)}");

    private static string FormatPercentage(decimal value)
        => EscapeMarkdownV2Code($"{value.ToString("F2", CultureInfo.InvariantCulture)}%");

    private static string FormatInteger(int value)
        => EscapeMarkdownV2Code(value.ToString(CultureInfo.InvariantCulture));

    private static string EscapeMarkdownV2(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            builder.Append(ch switch
            {
                '_' or '*' or '[' or ']' or '(' or ')' or '~' or '`' or '>' or '#' or '+' or '-' or '=' or '|' or '{'
                    or '}' or '.' or '!' => $"\\{ch}",
                _ => ch.ToString()
            });
        }

        return builder.ToString();
    }

    private static string EscapeMarkdownV2Code(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            builder.Append(ch switch
            {
                '\\' or '`' => $"\\{ch}",
                _ => ch.ToString()
            });
        }

        return builder.ToString();
    }
}
