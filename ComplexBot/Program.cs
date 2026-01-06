using ComplexBot.Configuration;
using ComplexBot.Models;
using ComplexBot.Services.Strategies;
using ComplexBot.Utils;
using Spectre.Console;
using Serilog;
using Serilog.Events;

namespace ComplexBot;

class Program
{
    static async Task Main(string[] args)
    {
        var configService = new ConfigurationService();
        ConfigureLogging(configService.GetConfiguration().App.Paths);

        try
        {
            Log.Information("=== TradingBot Starting ===");
            Log.Information("Environment: {Environment}", Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production");

            var menu = new AppMenu();
            var settingsService = new SettingsService(configService);
            var strategyRegistry = new StrategyRegistry(configService);
            var strategyFactory = new StrategyFactory(strategyRegistry);
            var resultsRenderer = new ResultsRenderer();
            var dataRunner = new DataRunner(configService.GetConfiguration().App);

            var backtestRunner = new BacktestRunner(dataRunner, settingsService, configService, strategyFactory, resultsRenderer);
            var optimizationRunner = new OptimizationRunner(dataRunner, settingsService, resultsRenderer, configService, strategyRegistry);
            var analysisRunner = new AnalysisRunner(dataRunner, settingsService, strategyFactory, resultsRenderer);
            var liveTradingRunner = new LiveTradingRunner(configService, settingsService);

            var dispatcher = new ModeDispatcher(
                backtestRunner,
                optimizationRunner,
                analysisRunner,
                liveTradingRunner,
                dataRunner,
                settingsService,
                configService);

            // Check for TRADING_MODE environment variable for non-interactive Docker execution
            var tradingMode = Environment.GetEnvironmentVariable("TRADING_MODE");
            AppMode mode;

            if (!string.IsNullOrEmpty(tradingMode))
            {
                // Map environment variable to menu option
                if (!UiMappings.TryGetAppModeFromEnv(tradingMode, out mode))
                {
                    var validModes = string.Join(", ", UiMappings.AppModeEnvValues);
                    throw new ArgumentException($"Unknown TRADING_MODE: {tradingMode}. Valid values: {validModes}");
                }

                AnsiConsole.Write(new FigletText("Trading Bot").Color(Color.Cyan1));
                AnsiConsole.MarkupLine("[grey]ADX Trend Following Strategy with Risk Management[/]");
                AnsiConsole.MarkupLine($"[green]Auto-starting mode:[/] {UiMappings.GetAppModeLabel(mode)}\n");
            }
            else
            {
                // Interactive mode - show menu
                mode = menu.PromptMode();
            }

            await dispatcher.DispatchAsync(mode);

            Log.Information("=== TradingBot Completed Successfully ===");
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "=== TradingBot Terminated Unexpectedly ===");
            AnsiConsole.MarkupLine($"[red]Fatal error: {ex.Message}[/]");
            throw;
        }
        finally
        {
            await Log.CloseAndFlushAsync();
        }
    }

    private static void ConfigureLogging(PathSettings paths)
    {
        // Ensure logs directory exists
        var logDirectory = ResolvePath(AppContext.BaseDirectory, paths.LogsDirectory);
        Directory.CreateDirectory(logDirectory);

        var logFilePath = Path.Combine(logDirectory, "tradingbot-.log");

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
            .MinimumLevel.Override("System", LogEventLevel.Information)
            .Enrich.FromLogContext()
            .Enrich.WithThreadId()
            .Enrich.WithMachineName()
            .WriteTo.Console(
                restrictedToMinimumLevel: LogEventLevel.Information,
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.File(
                logFilePath,
                rollingInterval: RollingInterval.Day,
                restrictedToMinimumLevel: LogEventLevel.Debug,
                outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} {Level:u3}] [{SourceContext}] [{ThreadId}] {Message:lj}{NewLine}{Exception}",
                retainedFileCountLimit: 30,
                fileSizeLimitBytes: 100_000_000,
                rollOnFileSizeLimit: true,
                shared: true)
            .CreateLogger();

        Log.Debug("Logging configured - File: {LogFilePath}, MinLevel: DEBUG", logFilePath);
    }

    private static string ResolvePath(string basePath, string configuredPath)
    {
        return Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.Combine(basePath, configuredPath);
    }
}
