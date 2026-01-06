using Binance.Net.Enums;
using Spectre.Console;
using System.Linq;
using ComplexBot.Models;
using ComplexBot.Services.Backtesting;
using ComplexBot.Services.Trading;
using ComplexBot.Utils;

namespace ComplexBot;

class DataRunner
{
    public async Task<(List<Candle> candles, string symbol)> LoadData()
    {
        var symbol = AnsiConsole.Ask("Symbol:", "BTCUSDT");
        var interval = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Interval (recommended [green]4h[/] or [green]1d[/] for medium-term):")
                .AddChoices("1h", "4h", "1d")
        );

        var startDate = AnsiConsole.Ask("Start date [green](yyyy-MM-dd)[/]:",
            DateTime.UtcNow.AddYears(-1).ToString("yyyy-MM-dd"));
        var endDate = AnsiConsole.Ask("End date [green](yyyy-MM-dd)[/]:",
            DateTime.UtcNow.ToString("yyyy-MM-dd"));

        var loader = new HistoricalDataLoader();
        var candles = new List<Candle>();
        var start = DateTime.Parse(startDate);
        var end = DateTime.Parse(endDate);
        var filename = $"Data/{symbol}_{interval}_{start:yyyyMMdd}_{end:yyyyMMdd}.csv";

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
                    KlineIntervalExtensions.Parse(interval),
                    start,
                    end,
                    progress
                );
            });

        Directory.CreateDirectory("Data");
        await loader.SaveToCsvAsync(candles, filename);
        AnsiConsole.MarkupLine($"[green]✓[/] Downloaded {candles.Count} candles and cached to [blue]{filename}[/]");
        return (candles, symbol);
    }

    public async Task DownloadData()
    {
        var symbol = AnsiConsole.Ask("Symbol:", "BTCUSDT");
        var interval = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Interval:")
                .AddChoices("1h", "4h", "1d")
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
                    KlineIntervalExtensions.Parse(interval),
                    DateTime.Parse(startDate),
                    DateTime.Parse(endDate),
                    progress
                );
            });

        var filename = $"Data/{symbol}_{interval}_{startDate}_{endDate}.csv";
        Directory.CreateDirectory("Data");
        await loader.SaveToCsvAsync(candles, filename);

        AnsiConsole.MarkupLine($"\n[green]✓[/] Saved {candles.Count} candles to [blue]{filename}[/]");
    }
}
