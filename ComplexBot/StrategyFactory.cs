using Spectre.Console;
using ComplexBot.Models;
using ComplexBot.Services.Strategies;

namespace ComplexBot;

class StrategyFactory
{
    private readonly StrategyRegistry _strategyRegistry;

    public StrategyFactory(StrategyRegistry strategyRegistry)
    {
        _strategyRegistry = strategyRegistry;
    }

    public IStrategy SelectStrategy(StrategySettings? adxSettings = null)
    {
        var strategyChoice = AnsiConsole.Prompt(
            new SelectionPrompt<StrategyKind>()
                .Title("Select [green]strategy[/]:")
                .UseConverter(StrategyRegistry.GetStrategyLabel)
                .AddChoices(StrategyRegistry.StrategyOrder)
        );

        return _strategyRegistry.CreateStrategy(strategyChoice, adxSettings);
    }

    public (string name, Func<IStrategy> factory) SelectStrategyWithFactory(StrategySettings? adxSettings = null)
    {
        var strategyChoice = AnsiConsole.Prompt(
            new SelectionPrompt<StrategyKind>()
                .Title("Select [green]strategy[/] for analysis:")
                .UseConverter(StrategyRegistry.GetStrategyLabel)
                .AddChoices(StrategyRegistry.StrategyOrder)
        );

        return (
            StrategyRegistry.GetStrategyShortName(strategyChoice),
            _strategyRegistry.GetStrategyFactory(strategyChoice, adxSettings)
        );
    }
}
