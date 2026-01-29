using SignalBot.Services.Telegram;
using Xunit;

namespace SignalBot.Tests;

public class SignalMessageHeuristicsTests
{
    [Fact]
    public void LooksLikeSignal_WithValidSignal_ReturnsTrue()
    {
        var text = """
            #BTC/USDT - Long🟢
            Entry: 100.5
            Stop Loss: 95
            Target 1: 110
            Leverage: x10
            """;

        var result = SignalMessageHeuristics.LooksLikeSignal(text);

        Assert.True(result);
    }

    [Fact]
    public void LooksLikeSignal_WithTickerOnly_ReturnsFalse()
    {
        var text = "#BTC/USDT";

        var result = SignalMessageHeuristics.LooksLikeSignal(text);

        Assert.False(result);
    }

    [Fact]
    public void LooksLikeSignal_WithPromoText_ReturnsFalse()
    {
        var text = "📈 VIP Channel's Profit in the last 24 hours";

        var result = SignalMessageHeuristics.LooksLikeSignal(text);

        Assert.False(result);
    }
}
