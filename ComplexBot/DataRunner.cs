using Spectre.Console;
using ComplexBot.Configuration;
using ComplexBot.Models;
using ComplexBot.Services.Backtesting;
using ComplexBot.Utils;

namespace ComplexBot;

class DataRunner
{
    private readonly string _dataDirectory;
    private readonly IReadOnlyList<KlineInterval> _allowedIntervals;

    public DataRunner(AppSettings appSettings)
    {
        _dataDirectory = ResolvePath(appSettings.Paths.DataDirectory);
        _allowedIntervals = appSettings.AllowedIntervals;
    }

    public async Task<(List<Candle> candles, string symbol)> LoadData()
    {
        var symbol = AnsiConsole.Ask("Symbol:", "BTCUSDT");
        var interval = AnsiConsole.Prompt(
            new SelectionPrompt<KlineInterval>()
                .Title("Interval (recommended [green]4h[/] or [green]1d[/] for medium-term):")
                .UseConverter(UiMappings.GetIntervalLabel)
                .AddChoices(_allowedIntervals)
        );

        var startDate = AnsiConsole.Ask("Start date [green](yyyy-MM-dd)[/]:",
            DateTime.UtcNow.AddYears(-1).ToString("yyyy-MM-dd"));
        var endDate = AnsiConsole.Ask("End date [green](yyyy-MM-dd)[/]:",
            DateTime.UtcNow.ToString("yyyy-MM-dd"));

        var loader = new HistoricalDataLoader();
        var candles = new List<Candle>();
        var start = DateTime.Parse(startDate);
        var end = DateTime.Parse(endDate);
        var intervalLabel = UiMappings.GetIntervalLabel(interval);
        var filename = Path.Combine(_dataDirectory, $"{symbol}_{intervalLabel}_{start:yyyyMMdd}_{end:yyyyMMdd}.csv");

        if (File.Exists(filename))
        {
            candles = await loader.LoadFromCsvAsync(filename);
            AnsiConsole.MarkupLine($"[green]✓[/] Loaded {candles.Count} candles from cache [blue]{filename}[/]");
            return (candles, symbol);
        }

        await AnsiConsole.Progress()
            .StartAsync(async ctx =>
            {
                var task = ctx.AddTask($"Downloading {symbol}...");
                var progress = new Progress<int>(p => task.Value = p);

                candles = await loader.LoadAsync(
                    symbol,
                    interval,
                    start,
                    end,
                    progress
                );
            });

        Directory.CreateDirectory(_dataDirectory);
        await loader.SaveToCsvAsync(candles, filename);
        AnsiConsole.MarkupLine($"[green]✓[/] Downloaded {candles.Count} candles and cached to [blue]{filename}[/]");
        return (candles, symbol);
    }

    public async Task DownloadData()
    {
        var symbol = AnsiConsole.Ask("Symbol:", "BTCUSDT");
        var interval = AnsiConsole.Prompt(
            new SelectionPrompt<KlineInterval>()
                .Title("Interval:")
                .UseConverter(UiMappings.GetIntervalLabel)
                .AddChoices(_allowedIntervals)
        );

        var startDate = AnsiConsole.Ask("Start date [green](yyyy-MM-dd)[/]:",
            DateTime.UtcNow.AddYears(-1).ToString("yyyy-MM-dd"));
        var endDate = AnsiConsole.Ask("End date [green](yyyy-MM-dd)[/]:",
            DateTime.UtcNow.ToString("yyyy-MM-dd"));

        var loader = new HistoricalDataLoader();
        var candles = new List<Candle>();

        await AnsiConsole.Progress()
            .StartAsync(async ctx =>
            {
                var task = ctx.AddTask($"Downloading {symbol} data...");
                var progress = new Progress<int>(p => task.Value = p);

                candles = await loader.LoadAsync(
                    symbol,
                    interval,
                    DateTime.Parse(startDate),
                    DateTime.Parse(endDate),
                    progress
                );
            });

        var intervalLabel = UiMappings.GetIntervalLabel(interval);
        var filename = Path.Combine(_dataDirectory, $"{symbol}_{intervalLabel}_{startDate}_{endDate}.csv");
        Directory.CreateDirectory(_dataDirectory);
        await loader.SaveToCsvAsync(candles, filename);

        AnsiConsole.MarkupLine($"\n[green]✓[/] Saved {candles.Count} candles to [blue]{filename}[/]");
    }

    private static string ResolvePath(string path)
    {
        return Path.IsPathRooted(path)
            ? path
            : Path.Combine(Directory.GetCurrentDirectory(), path);
    }
}
