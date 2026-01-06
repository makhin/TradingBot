using System.Reflection;
using System.Runtime.CompilerServices;
using ComplexBot;
using ComplexBot.Configuration;

namespace ComplexBot.Tests;

public class SettingsServiceTests
{
    [Fact]
    public void GetRiskSettings_ReturnsSavedSettings_WhenTradingModeSet()
    {
        var originalTradingMode = Environment.GetEnvironmentVariable("TRADING_MODE");

        try
        {
            Environment.SetEnvironmentVariable("TRADING_MODE", "paper");

            var configService = CreateConfigurationService(new BotConfiguration());
            var service = new SettingsService(configService);

            var expected = configService.GetConfiguration().RiskManagement.ToRiskSettings();

            var result = service.GetRiskSettings();

            Assert.Equivalent(expected, result);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TRADING_MODE", originalTradingMode);
        }
    }

    [Fact]
    public void GetStrategySettings_ReturnsSavedSettings_WhenTradingModeSet()
    {
        var originalTradingMode = Environment.GetEnvironmentVariable("TRADING_MODE");

        try
        {
            Environment.SetEnvironmentVariable("TRADING_MODE", "paper");

            var configService = CreateConfigurationService(new BotConfiguration());
            var service = new SettingsService(configService);

            var expected = configService.GetConfiguration().Strategy.ToStrategySettings();

            var result = service.GetStrategySettings();

            Assert.Equivalent(expected, result);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TRADING_MODE", originalTradingMode);
        }
    }

    private static ConfigurationService CreateConfigurationService(BotConfiguration configuration)
    {
        var service = (ConfigurationService)RuntimeHelpers.GetUninitializedObject(typeof(ConfigurationService));
        var currentConfigField = typeof(ConfigurationService)
            .GetField("_currentConfig", BindingFlags.NonPublic | BindingFlags.Instance);
        currentConfigField?.SetValue(service, configuration);
        return service;
    }
}
