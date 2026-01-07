using ComplexBot.Configuration;
using Spectre.Console;
using Serilog;
using Serilog.Events;

namespace ComplexBot;

class Program
{
    static async Task Main(string[] args)
    {
        // Configure Serilog early
        ConfigureLogging();

        try
        {
            Log.Information("=== TradingBot Starting ===");
            Log.Information("Environment: {Environment}", Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production");

            var configService = new ConfigurationService();

            var menu = new AppMenu();
            var settingsService = new SettingsService(configService);
            var strategyFactory = new StrategyFactory(configService);
            var resultsRenderer = new ResultsRenderer();
            var dataRunner = new DataRunner();

            var backtestRunner = new BacktestRunner(dataRunner, settingsService, strategyFactory, resultsRenderer);
            var optimizationRunner = new OptimizationRunner(settingsService, resultsRenderer, configService);
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
            string mode;

            if (!string.IsNullOrEmpty(tradingMode))
            {
                // Map environment variable to menu option
                mode = tradingMode.ToLowerInvariant() switch
                {
                    "live" => "Live Trading (Paper)",
                    "live-real" => "Live Trading (Real)",
                    "backtest" => "Backtest",
                    "optimize" => "Parameter Optimization",
                    "walkforward" => "Walk-Forward Analysis",
                    "montecarlo" => "Monte Carlo Simulation",
                    "download" => "Download Data",
                    _ => throw new ArgumentException($"Unknown TRADING_MODE: {tradingMode}. Valid values: live, live-real, backtest, optimize, walkforward, montecarlo, download")
                };

                AnsiConsole.Write(new FigletText("Trading Bot").Color(Color.Cyan1));
                AnsiConsole.MarkupLine("[grey]ADX Trend Following Strategy with Risk Management[/]");
                AnsiConsole.MarkupLine($"[green]Auto-starting mode:[/] {mode}\n");
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

    private static void ConfigureLogging()
    {
        // Ensure logs directory exists
        var logDirectory = Path.Combine(AppContext.BaseDirectory, "data", "logs");
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
}
