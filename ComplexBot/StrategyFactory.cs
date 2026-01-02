using Spectre.Console;
using ComplexBot.Configuration;
using ComplexBot.Models;
using ComplexBot.Services.Strategies;

namespace ComplexBot;

class StrategyFactory
{
    private readonly ConfigurationService _configService;

    public StrategyFactory(ConfigurationService configService)
    {
        _configService = configService;
    }

    public IStrategy SelectStrategy(StrategySettings? adxSettings = null)
    {
        var ensembleSettings = _configService.GetConfiguration()
            .Ensemble
            .ToEnsembleSettings();

        var strategyChoice = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Select [green]strategy[/]:")
                .AddChoices(
                    "ADX Trend Following (Recommended)",
                    "RSI Mean Reversion",
                    "MA Crossover",
                    "Strategy Ensemble (All Combined)")
        );

        return strategyChoice switch
        {
            "ADX Trend Following (Recommended)" => new AdxTrendStrategy(adxSettings),
            "RSI Mean Reversion" => new RsiStrategy(),
            "MA Crossover" => new MaStrategy(),
            "Strategy Ensemble (All Combined)" => StrategyEnsemble.CreateDefault(ensembleSettings),
            _ => new AdxTrendStrategy(adxSettings)
        };
    }

    public (string name, Func<IStrategy> factory) SelectStrategyWithFactory(StrategySettings? adxSettings = null)
    {
        var config = _configService.GetConfiguration();
        var ensembleSettings = config.Ensemble.ToEnsembleSettings();
        var maSettings = config.MaStrategy.ToMaStrategySettings();
        var rsiSettings = config.RsiStrategy.ToRsiStrategySettings();

        var strategyChoice = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Select [green]strategy[/] for analysis:")
                .AddChoices(
                    "ADX Trend Following",
                    "RSI Mean Reversion",
                    "MA Crossover",
                    "Strategy Ensemble (All Combined)")
        );

        return strategyChoice switch
        {
            "ADX Trend Following" => ("ADX", () => new AdxTrendStrategy(adxSettings)),
            "RSI Mean Reversion" => ("RSI", () => new RsiStrategy(rsiSettings)),
            "MA Crossover" => ("MA", () => new MaStrategy(maSettings)),
            "Strategy Ensemble (All Combined)" => ("Ensemble", () => StrategyEnsemble.CreateDefault(ensembleSettings)),
            _ => ("ADX", () => new AdxTrendStrategy(adxSettings))
        };
    }
}
