using Serilog;
using SignalBot.Configuration;
using SignalBot.Models;
using SignalBot.Services.Telegram;
using Xunit;

namespace SignalBot.Tests;

public class SignalParserTests
{
    private static readonly TelegramSettings DefaultSettings = new()
    {
        Parsing = new TelegramParsingSettings
        {
            DefaultParser = "default",
            DefaultLeverage = 10
        }
    };

    private static readonly ISignalMessageParser[] DefaultParsers =
    [
        new DefaultSignalParser()
    ];

    private readonly SignalParser _parser = new(
        DefaultSettings,
        DefaultParsers,
        new LoggerConfiguration().CreateLogger());

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

    [Fact]
    public void Parse_ValidSignalWithFiveTargets_ReturnsSignal()
    {
        var text = """
            #ADA/USDT - Long🟢

            Entry: 0.4005
            Stop Loss: 0.38523

            Target 1: 0.40485
            Target 2: 0.40894
            Target 3: 0.41526
            Target 4: 0.43211
            Target 5: 0.44886

            Leverage: x14
            """;

        var result = _parser.Parse(text, Source);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Signal);
        Assert.Equal("ADAUSDT", result.Signal!.Symbol);
        Assert.Equal(SignalDirection.Long, result.Signal.Direction);
        Assert.Equal(0.4005m, result.Signal.Entry);
        Assert.Equal(0.38523m, result.Signal.OriginalStopLoss);
        Assert.Equal(14, result.Signal.OriginalLeverage);
        Assert.Equal(5, result.Signal.Targets.Count);
        Assert.Equal(new List<decimal> { 0.40485m, 0.40894m, 0.41526m, 0.43211m, 0.44886m }, result.Signal.Targets);
    }

    [Fact]
    public void Parse_ValidSignalWithTenTargets_ReturnsSignal()
    {
        var text = """
            #BTC/USDT - Long🟢
            Entry: 50000
            Stop Loss: 49000
            Target 1: 50500
            Target 2: 51000
            Target 3: 51500
            Target 4: 52000
            Target 5: 52500
            Target 6: 53000
            Target 7: 53500
            Target 8: 54000
            Target 9: 54500
            Target 10: 55000
            Leverage: x10
            """;

        var result = _parser.Parse(text, Source);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Signal);
        Assert.Equal("BTCUSDT", result.Signal!.Symbol);
        Assert.Equal(10, result.Signal.Targets.Count);
        Assert.Equal(50500m, result.Signal.Targets[0]);
        Assert.Equal(55000m, result.Signal.Targets[9]);
    }
}

public class DefaultSignalParserTests
{
    private readonly DefaultSignalParser _parser = new();

    private static readonly SignalSource Source = new()
    {
        ChannelName = "test",
        ChannelId = 1,
        MessageId = 1
    };

    [Fact]
    public void Parse_ValidSignalWithoutEmoji_ReturnsSignal()
    {
        var text = """
            #LINK/USDT - Long
            Entry: 15.5
            Stop Loss: 14.5
            Target 1: 16
            Target 2: 17
            Leverage: x5
            """;

        var result = _parser.Parse(text, Source, defaultLeverage: 10);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Signal);
        Assert.Equal("LINKUSDT", result.Signal!.Symbol);
        Assert.Equal(SignalDirection.Long, result.Signal.Direction);
        Assert.Equal(15.5m, result.Signal.Entry);
        Assert.Equal(14.5m, result.Signal.OriginalStopLoss);
        Assert.Equal(5, result.Signal.OriginalLeverage);
        Assert.Equal(2, result.Signal.Targets.Count);
    }

    [Fact]
    public void Parse_SignalWithGreenEmoji_ParsesLong()
    {
        var text = """
            #AVAX/USDT - Long🟢
            Entry: 35
            Stop Loss: 33
            Target 1: 37
            Leverage: x10
            """;

        var result = _parser.Parse(text, Source, defaultLeverage: 5);

        Assert.True(result.IsSuccess);
        Assert.Equal(SignalDirection.Long, result.Signal!.Direction);
    }

    [Fact]
    public void Parse_SignalWithRedEmoji_ParsesShort()
    {
        var text = """
            #AVAX/USDT - Short🔴
            Entry: 35
            Stop Loss: 37
            Target 1: 33
            Leverage: x10
            """;

        var result = _parser.Parse(text, Source, defaultLeverage: 5);

        Assert.True(result.IsSuccess);
        Assert.Equal(SignalDirection.Short, result.Signal!.Direction);
    }

    [Fact]
    public void Name_ReturnsDefault()
    {
        Assert.Equal("default", _parser.Name);
    }
}
