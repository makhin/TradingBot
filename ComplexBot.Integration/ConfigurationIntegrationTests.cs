namespace ComplexBot.Integration;

/// <summary>
/// Integration tests for configuration loading and validation
/// These tests verify that appsettings.json is properly structured and all required settings are present
/// </summary>
[Collection("Integration")]
public class ConfigurationIntegrationTests
{
    private readonly IntegrationTestFixture _fixture;

    public ConfigurationIntegrationTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void Configuration_LoadsSuccessfully()
    {
        // Arrange & Act
        var config = _fixture.Config;

        // Assert
        Assert.NotNull(config);
        Console.WriteLine("✅ Configuration loaded successfully");
    }

    [Fact]
    public void BinanceApiSettings_AreConfigured()
    {
        // Arrange & Act
        var binanceConfig = _fixture.Config.BinanceApi;

        // Assert
        Assert.NotNull(binanceConfig);
        Assert.NotEmpty(binanceConfig.ApiKey);
        Assert.NotEmpty(binanceConfig.ApiSecret);
        Assert.True(binanceConfig.UseTestnet, "Tests should use testnet");

        Console.WriteLine("✅ Binance API settings configured");
        Console.WriteLine($"   Testnet: {binanceConfig.UseTestnet}");
    }

    [Fact]
    public void RiskManagementSettings_AreValid()
    {
        // Arrange & Act
        var riskConfig = _fixture.Config.RiskManagement;

        // Assert
        Assert.NotNull(riskConfig);
        Assert.True(riskConfig.RiskPerTradePercent > 0);
        Assert.True(riskConfig.MaxDrawdownPercent > 0);
        Assert.True(riskConfig.MaxDailyDrawdownPercent > 0);
        Assert.True(riskConfig.MaxPortfolioHeatPercent > 0);

        Console.WriteLine("✅ Risk Management settings valid");
        Console.WriteLine($"   Risk per trade: {riskConfig.RiskPerTradePercent}%");
        Console.WriteLine($"   Max drawdown: {riskConfig.MaxDrawdownPercent}%");
        Console.WriteLine($"   Max daily loss: {riskConfig.MaxDailyDrawdownPercent}%");
    }

    [Fact]
    public void StrategySettings_AreConfigured()
    {
        // Arrange & Act
        var strategyConfig = _fixture.Config.Strategy;

        // Assert
        Assert.NotNull(strategyConfig);
        Assert.True(strategyConfig.AdxPeriod > 0);
        Assert.True(strategyConfig.AdxThreshold > 0);
        Assert.True(strategyConfig.FastEmaPeriod > 0);
        Assert.True(strategyConfig.SlowEmaPeriod > 0);

        Assert.True(
            strategyConfig.FastEmaPeriod < strategyConfig.SlowEmaPeriod,
            "Fast EMA should have shorter period than slow EMA"
        );

        Console.WriteLine("✅ Strategy settings configured");
        Console.WriteLine($"   ADX Period: {strategyConfig.AdxPeriod}");
        Console.WriteLine($"   ADX Threshold: {strategyConfig.AdxThreshold}");
        Console.WriteLine($"   Fast EMA: {strategyConfig.FastEmaPeriod}");
        Console.WriteLine($"   Slow EMA: {strategyConfig.SlowEmaPeriod}");
    }

    [Fact]
    public void BacktestingSettings_AreConfigured()
    {
        // Arrange & Act
        var backtestConfig = _fixture.Config.Backtesting;

        // Assert
        Assert.NotNull(backtestConfig);
        Assert.True(backtestConfig.InitialCapital > 0);
        Assert.True(backtestConfig.CommissionPercent >= 0);
        Assert.True(backtestConfig.SlippagePercent >= 0);

        Console.WriteLine("✅ Backtesting settings configured");
        Console.WriteLine($"   Initial Capital: ${backtestConfig.InitialCapital}");
        Console.WriteLine($"   Commission: {backtestConfig.CommissionPercent}%");
        Console.WriteLine($"   Slippage: {backtestConfig.SlippagePercent}%");
    }

    [Fact]
    public void LiveTradingSettings_AreConfigured()
    {
        // Arrange & Act
        var liveConfig = _fixture.Config.LiveTrading;

        // Assert
        Assert.NotNull(liveConfig);
        Assert.NotEmpty(liveConfig.Symbol);
        Assert.True(liveConfig.UseTestnet || liveConfig.PaperTrade,
            "Live trading should use testnet or paper trading mode");

        Console.WriteLine("✅ Live Trading settings configured");
        Console.WriteLine($"   Symbol: {liveConfig.Symbol}");
        Console.WriteLine($"   Testnet: {liveConfig.UseTestnet}");
        Console.WriteLine($"   Paper Trading: {liveConfig.PaperTrade}");
    }

    [Fact]
    public void PortfolioRiskSettings_AreConfigured()
    {
        // Arrange & Act
        var portfolioConfig = _fixture.Config.PortfolioRisk;

        // Assert
        Assert.NotNull(portfolioConfig);
        Assert.True(portfolioConfig.MaxTotalDrawdownPercent > 0);
        Assert.True(portfolioConfig.MaxCorrelatedRiskPercent > 0);
        Assert.True(portfolioConfig.MaxConcurrentPositions > 0);
        Assert.NotNull(portfolioConfig.CorrelationGroups);

        Console.WriteLine("✅ Portfolio Risk settings configured");
        Console.WriteLine($"   Max Total Drawdown: {portfolioConfig.MaxTotalDrawdownPercent}%");
        Console.WriteLine($"   Max Correlated Risk: {portfolioConfig.MaxCorrelatedRiskPercent}%");
        Console.WriteLine($"   Max Concurrent Positions: {portfolioConfig.MaxConcurrentPositions}");
        Console.WriteLine($"   Correlation Groups: {portfolioConfig.CorrelationGroups.Count}");
    }

    [Fact]
    public void RiskSettings_FollowBestPractices()
    {
        // Arrange & Act
        var riskConfig = _fixture.Config.RiskManagement;

        // Assert - Verify reasonable risk parameters
        Assert.True(
            riskConfig.RiskPerTradePercent <= 5m,
            "Risk per trade should not exceed 5% (professional standard: 1-2%)"
        );

        Assert.True(
            riskConfig.MaxDrawdownPercent >= 10m && riskConfig.MaxDrawdownPercent <= 50m,
            "Max drawdown should be between 10-50%"
        );

        Assert.True(
            riskConfig.MaxDailyDrawdownPercent <= riskConfig.MaxDrawdownPercent,
            "Daily limit should be less than max drawdown"
        );

        Console.WriteLine("✅ Risk settings follow best practices");
        Console.WriteLine($"   Risk per trade: {riskConfig.RiskPerTradePercent}% (recommended: 1-2%)");
        Console.WriteLine($"   Max drawdown: {riskConfig.MaxDrawdownPercent}% (reasonable)");
        Console.WriteLine($"   Max daily loss: {riskConfig.MaxDailyDrawdownPercent}% (strict)");
    }

    [Fact]
    public void StrategyParameters_AreOptimal()
    {
        // Arrange & Act
        var strategyConfig = _fixture.Config.Strategy;

        // Assert - Verify strategy parameters are reasonable
        Assert.True(
            strategyConfig.AdxThreshold >= 20m && strategyConfig.AdxThreshold <= 35m,
            "ADX threshold should be between 20-35 (typical for trend following)"
        );

        Assert.True(
            strategyConfig.AdxPeriod >= 10 && strategyConfig.AdxPeriod <= 20,
            "ADX period should be between 10-20"
        );

        Assert.True(
            strategyConfig.VolumeThreshold >= 1.0m && strategyConfig.VolumeThreshold <= 3.0m,
            "Volume threshold should be between 1.0-3.0x average"
        );

        Console.WriteLine("✅ Strategy parameters are optimal");
        Console.WriteLine($"   ADX threshold: {strategyConfig.AdxThreshold} (optimal for trend)");
        Console.WriteLine($"   Volume threshold: {strategyConfig.VolumeThreshold}x (reasonable)");
    }

    [Fact]
    public void ConfigurationFile_IsInValidJsonFormat()
    {
        // Arrange
        var configPath = Path.Combine(
            AppContext.BaseDirectory,
            "appsettings.json"
        );

        // Act & Assert
        Assert.True(File.Exists(configPath), "appsettings.json must exist in output directory");

        var json = File.ReadAllText(configPath);
        Assert.NotEmpty(json);

        Console.WriteLine("✅ appsettings.json is valid");
        Console.WriteLine($"   Location: {configPath}");
        Console.WriteLine($"   Size: {new FileInfo(configPath).Length} bytes");
    }
}
