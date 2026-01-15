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

public class RiskOrderSignalParserTests
{
    private static readonly TelegramSettings Settings = new()
    {
        Parsing = new TelegramParsingSettings
        {
            DefaultParser = "risk-order",
            DefaultLeverage = 5,
            ParserDefaultLeverages = new Dictionary<string, int>
            {
                ["risk-order"] = 3
            }
        }
    };

    private static readonly ISignalMessageParser[] Parsers =
    [
        new RiskOrderSignalParser()
    ];

    private readonly SignalParser _parser = new(
        Settings,
        Parsers,
        new LoggerConfiguration().CreateLogger());

    private static readonly SignalSource Source = new()
    {
        ChannelName = "risk-order-channel",
        ChannelId = 123,
        MessageId = 1
    };

    [Fact]
    public void Parse_ValidRiskOrderSignal_ReturnsSignal()
    {
        var text = """
            Long - $BTC

            Entry 1: 95000
            Entry 2: 94500

            SL: 93000

            TP 1: 96000
            TP 2: 97000
            TP 3: 98000
            """;

        var result = _parser.Parse(text, Source);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Signal);
        Assert.Equal("BTCUSDT", result.Signal!.Symbol);
        Assert.Equal(SignalDirection.Long, result.Signal.Direction);
        Assert.Equal(94750m, result.Signal.Entry); // Average of 95000 and 94500
        Assert.Equal(93000m, result.Signal.OriginalStopLoss);
        Assert.Equal(3, result.Signal.OriginalLeverage); // From ParserDefaultLeverages
    }

    [Fact]
    public void Parse_RiskOrderSignalWithMultipleTargets_CapturesAllTargets()
    {
        var text = """
            Short - $ETH

            Entry 1: 3500

            SL: 3600

            TP 1: 3400
            TP 2: 3300
            TP 3: 3200
            TP 4: 3100
            """;

        var result = _parser.Parse(text, Source);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Signal);
        Assert.Equal("ETHUSDT", result.Signal!.Symbol);
        Assert.Equal(SignalDirection.Short, result.Signal.Direction);
        Assert.Equal(4, result.Signal.Targets.Count);
        Assert.Equal(new List<decimal> { 3400m, 3300m, 3200m, 3100m }, result.Signal.Targets);
    }

    [Fact]
    public void Parse_RiskOrderSignalWithSingleEntry_ReturnsSignal()
    {
        var text = """
            Long - $SOL

            Entry 1: 150.5

            Stop Loss: 145

            TP 1: 155
            TP 2: 160
            """;

        var result = _parser.Parse(text, Source);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Signal);
        Assert.Equal("SOLUSDT", result.Signal!.Symbol);
        Assert.Equal(150.5m, result.Signal.Entry);
        Assert.Equal(145m, result.Signal.OriginalStopLoss);
        Assert.Equal(2, result.Signal.Targets.Count);
    }

    [Fact]
    public void Parse_RiskOrderSignalWithFiveTargets_CapturesAllTargets()
    {
        var text = """
            Long - $DOGE

            Entry 1: 0.10
            Entry 2: 0.095

            SL: 0.08

            TP 1: 0.11
            TP 2: 0.12
            TP 3: 0.13
            TP 4: 0.14
            TP 5: 0.15
            """;

        var result = _parser.Parse(text, Source);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Signal);
        Assert.Equal("DOGEUSDT", result.Signal!.Symbol);
        Assert.Equal(0.0975m, result.Signal.Entry); // Average of 0.10 and 0.095
        Assert.Equal(5, result.Signal.Targets.Count);
        Assert.Equal(new List<decimal> { 0.11m, 0.12m, 0.13m, 0.14m, 0.15m }, result.Signal.Targets);
    }

    [Fact]
    public void Parse_InvalidFormat_ReturnsFailure()
    {
        var text = "Random message without risk order format";

        var result = _parser.Parse(text, Source);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void Parse_MissingTargets_ReturnsFailure()
    {
        var text = """
            Long - $BTC

            Entry 1: 95000

            SL: 93000
            """;

        var result = _parser.Parse(text, Source);

        Assert.False(result.IsSuccess);
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

public class RiskOrderSignalParserUnitTests
{
    private readonly RiskOrderSignalParser _parser = new();

    private static readonly SignalSource Source = new()
    {
        ChannelName = "risk",
        ChannelId = 1,
        MessageId = 1
    };

    [Fact]
    public void Name_ReturnsRiskOrder()
    {
        Assert.Equal("risk-order", _parser.Name);
    }

    [Fact]
    public void Parse_DirectCall_ReturnsCorrectResult()
    {
        var text = """
            Long - $XRP

            Entry 1: 0.5

            SL: 0.45

            TP 1: 0.55
            TP 2: 0.60
            TP 3: 0.65
            """;

        var result = _parser.Parse(text, Source, defaultLeverage: 2);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Signal);
        Assert.Equal("XRPUSDT", result.Signal!.Symbol);
        Assert.Equal(0.5m, result.Signal.Entry);
        Assert.Equal(0.45m, result.Signal.OriginalStopLoss);
        Assert.Equal(2, result.Signal.OriginalLeverage);
        Assert.Equal(3, result.Signal.Targets.Count);
        Assert.Equal(new List<decimal> { 0.55m, 0.60m, 0.65m }, result.Signal.Targets);
    }
}
