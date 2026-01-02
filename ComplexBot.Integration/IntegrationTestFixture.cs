using Microsoft.Extensions.Configuration;
using ComplexBot.Configuration;

namespace ComplexBot.Integration;

/// <summary>
/// Base fixture for integration tests that loads configuration from appsettings.json
/// </summary>
public class IntegrationTestFixture : IDisposable
{
    private readonly IConfigurationRoot _configuration;
    public BotConfiguration Config { get; }

    public IntegrationTestFixture()
    {
        // Load configuration from appsettings.json in the test output directory
        var configPath = Path.Combine(
            AppContext.BaseDirectory,
            "appsettings.json"
        );

        if (!File.Exists(configPath))
        {
            throw new FileNotFoundException(
                $"appsettings.json not found at {configPath}. " +
                $"Please ensure the file is copied to the test output directory."
            );
        }

        _configuration = new ConfigurationBuilder()
            .SetBasePath(Path.GetDirectoryName(configPath)!)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .Build();

        Config = new BotConfiguration();
        _configuration.Bind(Config);

        // Validate critical settings
        ValidateConfiguration();
    }

    private void ValidateConfiguration()
    {
        if (string.IsNullOrWhiteSpace(Config.BinanceApi.ApiKey))
        {
            throw new InvalidOperationException(
                "BinanceApi:ApiKey is not configured in appsettings.json"
            );
        }

        if (string.IsNullOrWhiteSpace(Config.BinanceApi.ApiSecret))
        {
            throw new InvalidOperationException(
                "BinanceApi:ApiSecret is not configured in appsettings.json"
            );
        }

        if (!Config.BinanceApi.UseTestnet && !Config.LiveTrading.PaperTrade)
        {
            Console.WriteLine("⚠️  WARNING: Using REAL Mainnet with real money!");
        }
    }

    public void Dispose()
    {
        // No resources to dispose
    }
}

/// <summary>
/// Collection definition for integration tests (ensures sequential execution)
/// </summary>
[CollectionDefinition("Integration")]
public class IntegrationCollection : ICollectionFixture<IntegrationTestFixture>
{
    // This has no code, and never creates an instance of the collection.
    // It's just a place to apply [CollectionDefinition] and all the
    // ICollectionFixture<T> interfaces.
}
