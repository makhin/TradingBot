using Serilog;
using SignalBot.Models;
using SignalBot.Services.Telegram;
using Xunit;

namespace SignalBot.Tests;

public class SignalParserTests
{
    private readonly SignalParser _parser = new(new LoggerConfiguration().CreateLogger());
    private static readonly SignalSource Source = new()
    {
        ChannelName = "test",
        ChannelId = 1,
        MessageId = 1
    };

    [Fact]
    public void Parse_ValidSignalWithSingleTarget_ReturnsSignal()
    {
        var text = """
            #BTC/USDT - Long🟢
            Entry: 100.5
            Stop Loss: 95
            Target 1: 110
            Leverage: x10
            """;

        var result = _parser.Parse(text, Source);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Signal);
        Assert.Equal("BTCUSDT", result.Signal!.Symbol);
        Assert.Equal(SignalDirection.Long, result.Signal.Direction);
        Assert.Equal(100.5m, result.Signal.Entry);
        Assert.Equal(95m, result.Signal.OriginalStopLoss);
        Assert.Equal(10, result.Signal.OriginalLeverage);
        Assert.Single(result.Signal.Targets);
        Assert.Equal(110m, result.Signal.Targets[0]);
    }

    [Fact]
    public void Parse_ValidSignalWithMultipleTargets_ReturnsSignal()
    {
        var text = """
            #ETH/USDT - Short🔴
            Entry: 2000
            Stop Loss: 2100
            Target 1: 1950
            Target 2: 1900
            Target 3: 1850
            Leverage: x5
            """;

        var result = _parser.Parse(text, Source);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Signal);
        Assert.Equal("ETHUSDT", result.Signal!.Symbol);
        Assert.Equal(SignalDirection.Short, result.Signal.Direction);
        Assert.Equal(2000m, result.Signal.Entry);
        Assert.Equal(2100m, result.Signal.OriginalStopLoss);
        Assert.Equal(5, result.Signal.OriginalLeverage);
        Assert.Equal(new List<decimal> { 1950m, 1900m, 1850m }, result.Signal.Targets);
    }

    [Fact]
    public void Parse_NoTargets_ReturnsFailure()
    {
        var text = """
            #XRP/USDT - Long
            Entry: 0.50
            Stop Loss: 0.45
            Leverage: x3
            """;

        var result = _parser.Parse(text, Source);

        Assert.False(result.IsSuccess);
        Assert.Equal("No targets found in signal", result.ErrorMessage);
    }

    [Fact]
    public void Parse_InvalidFormat_ReturnsFailure()
    {
        var text = "Random message without signal structure";

        var result = _parser.Parse(text, Source);

        Assert.False(result.IsSuccess);
        Assert.Equal("Signal format not recognized", result.ErrorMessage);
    }
}
