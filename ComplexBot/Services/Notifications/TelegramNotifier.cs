using Telegram.Bot;
using Telegram.Bot.Types.Enums;
using ComplexBot.Models;
using ComplexBot.Services.Analytics;

namespace ComplexBot.Services.Notifications;

public class TelegramNotifier
{
    private readonly TelegramBotClient? _bot;
    private readonly long _chatId;
    private readonly bool _enabled;

    public TelegramNotifier(string? botToken, long chatId)
    {
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
        var direction = signal.Type == SignalType.Buy ? "LONG" : "SHORT";

        var message = $"""
            {emoji} *NEW TRADE OPENED*

            *{signal.Symbol}* {direction}

            📍 Entry: `{signal.Price:F2}`
            🛑 Stop Loss: `{signal.StopLoss:F2}`
            🎯 Take Profit: `{signal.TakeProfit:F2}`

            📊 Size: `{quantity:F4}`
            💰 Risk: `${riskAmount:F2}`

            📝 _{signal.Reason}_
            """;

        await SendMessage(message, cancellationToken);
    }

    public async Task SendTradeClose(string symbol, decimal entryPrice, decimal exitPrice,
        decimal pnl, decimal rMultiple, string reason, CancellationToken cancellationToken = default)
    {
        if (!_enabled) return;

        var emoji = pnl >= 0 ? "✅" : "❌";
        var pnlEmoji = pnl >= 0 ? "📈" : "📉";

        var message = $"""
            {emoji} *TRADE CLOSED*

            *{symbol}*

            📍 Entry: `{entryPrice:F2}`
            📍 Exit: `{exitPrice:F2}`

            {pnlEmoji} PnL: `${pnl:F2}` ({rMultiple:F2}R)

            📝 _{reason}_
            """;

        await SendMessage(message, cancellationToken);
    }

    public async Task SendDrawdownAlert(decimal currentDrawdown, decimal dailyDrawdown, CancellationToken cancellationToken = default)
    {
        if (!_enabled) return;

        var message = $"""
            ⚠️ *DRAWDOWN ALERT*

            📉 Total Drawdown: `{currentDrawdown:F2}%`
            📉 Daily Drawdown: `{dailyDrawdown:F2}%`

            _Risk management may reduce position sizes_
            """;

        await SendMessage(message, cancellationToken);
    }

    public async Task SendCircuitBreakerTriggered(string reason, CancellationToken cancellationToken = default)
    {
        if (!_enabled) return;

        var message = $"""
            🚨 *CIRCUIT BREAKER TRIGGERED*

            ⛔ Trading has been stopped!

            Reason: _{reason}_

            _Manual intervention required_
            """;

        await SendMessage(message, cancellationToken);
    }

    public async Task SendDailySummary(TradeJournalStats stats, decimal equity, decimal drawdown, CancellationToken cancellationToken = default)
    {
        if (!_enabled) return;

        var message = $"""
            📊 *DAILY SUMMARY*

            💰 Equity: `${equity:F2}`
            📉 Drawdown: `{drawdown:F2}%`

            📈 Trades Today: `{stats.TotalTrades}`
            🎯 Win Rate: `{stats.WinRate:F1}%`
            💵 Net PnL: `${stats.TotalNetPnL:F2}`

            Best Trade: `${stats.LargestWin:F2}`
            Worst Trade: `${stats.LargestLoss:F2}`
            """;

        await SendMessage(message, cancellationToken);
    }

    public async Task SendError(string errorMessage, CancellationToken cancellationToken = default)
    {
        if (!_enabled) return;

        var message = $"""
            ❌ *ERROR*

            {errorMessage}

            _Check logs for details_
            """;

        await SendMessage(message, cancellationToken);
    }

    public async Task SendMessageAsync(string message, CancellationToken cancellationToken = default)
    {
        await SendMessage(message, cancellationToken);
    }

    private async Task SendMessage(string message, CancellationToken cancellationToken)
    {
        if (_bot == null) return;

        try
        {
            await _bot.SendMessage(
                chatId: _chatId,
                text: message,
                parseMode: ParseMode.Markdown,
                cancellationToken: cancellationToken
            );
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Telegram error: {ex.Message}");
        }
    }
}
