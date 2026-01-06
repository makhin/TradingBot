using Spectre.Console;
using ComplexBot.Configuration;
using ComplexBot.Models;
using ComplexBot.Services.Strategies;
using ComplexBot.Utils;

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
            new SelectionPrompt<StrategyKind>()
                .Title("Select [green]strategy[/]:")
                .UseConverter(UiMappings.GetStrategyLabel)
                .AddChoices(UiMappings.StrategyModes)
        );

        return strategyChoice switch
        {
            StrategyKind.AdxTrendFollowing => new AdxTrendStrategy(adxSettings),
            StrategyKind.RsiMeanReversion => new RsiStrategy(),
            StrategyKind.MaCrossover => new MaStrategy(),
            StrategyKind.StrategyEnsemble => StrategyEnsemble.CreateDefault(ensembleSettings),
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
            new SelectionPrompt<StrategyKind>()
                .Title("Select [green]strategy[/] for analysis:")
                .UseConverter(UiMappings.GetStrategyLabel)
                .AddChoices(UiMappings.StrategyModes)
        );

        return strategyChoice switch
        {
            StrategyKind.AdxTrendFollowing => (UiMappings.GetStrategyShortName(strategyChoice), () => new AdxTrendStrategy(adxSettings)),
            StrategyKind.RsiMeanReversion => (UiMappings.GetStrategyShortName(strategyChoice), () => new RsiStrategy(rsiSettings)),
            StrategyKind.MaCrossover => (UiMappings.GetStrategyShortName(strategyChoice), () => new MaStrategy(maSettings)),
            StrategyKind.StrategyEnsemble => (UiMappings.GetStrategyShortName(strategyChoice), () => StrategyEnsemble.CreateDefault(ensembleSettings)),
            _ => (UiMappings.GetStrategyShortName(StrategyKind.AdxTrendFollowing), () => new AdxTrendStrategy(adxSettings))
        };
    }
}
