using Binance.Net.Enums;
using Spectre.Console;
using System.Linq;
using ComplexBot.Models;
using ComplexBot.Services.Backtesting;
using ComplexBot.Services.Trading;

namespace ComplexBot;

class DataRunner
{
    public async Task<(List<Candle> candles, string symbol)> LoadData()
    {
        var source = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Data source:")
                .AddChoices("Download from Binance", "Load from CSV file")
        );

        if (source == "Load from CSV file")
        {
            var files = Directory.Exists("Data")
                ? Directory.GetFiles("Data", "*.csv")
                : Array.Empty<string>();

            if (files.Length == 0)
            {
                AnsiConsole.MarkupLine("[red]No CSV files found in Data folder[/]");
                return (new List<Candle>(), "");
            }

            var file = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("Select file:")
                    .AddChoices(files.Select(Path.GetFileName).Where(f => f != null)!)
            );

            var loader = new HistoricalDataLoader();
            var candles = await loader.LoadFromCsvAsync($"Data/{file}");
            var symbol = file!.Split('_')[0];

            AnsiConsole.MarkupLine($"[green]✓[/] Loaded {candles.Count} candles");
            return (candles, symbol);
        }
        else
        {
            var symbol = AnsiConsole.Ask("Symbol:", "BTCUSDT");
            var interval = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("Interval (recommended [green]4h[/] or [green]1d[/] for medium-term):")
                    .AddChoices("1h", "4h", "1d")
            );

            var months = AnsiConsole.Ask("History months:", 12);
            var loader = new HistoricalDataLoader();
            var candles = new List<Candle>();

            await AnsiConsole.Progress()
                .StartAsync(async ctx =>
                {
                    var task = ctx.AddTask($"Downloading {symbol}...");
                    var progress = new Progress<int>(p => task.Value = p);

                    candles = await loader.LoadAsync(
                        symbol,
                        KlineIntervalExtensions.Parse(interval),
                        DateTime.UtcNow.AddMonths(-months),
                        DateTime.UtcNow,
                        progress
                    );
                });

            AnsiConsole.MarkupLine($"[green]✓[/] Downloaded {candles.Count} candles");
            return (candles, symbol);
        }
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
