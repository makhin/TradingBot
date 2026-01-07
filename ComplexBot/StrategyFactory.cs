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
        var strategyChoice = SelectStrategyKind("Select [green]strategy[/]:");

        return _strategyRegistry.CreateStrategy(strategyChoice, adxSettings);
    }

    public (string name, Func<IStrategy> factory) SelectStrategyWithFactory(StrategySettings? adxSettings = null)
    {
        var strategyChoice = SelectStrategyKind("Select [green]strategy[/] for analysis:");

        return (
            StrategyRegistry.GetStrategyShortName(strategyChoice),
            _strategyRegistry.GetStrategyFactory(strategyChoice, adxSettings)
        );
    }

    public StrategyKind SelectStrategyKind(string title, StrategyKind? defaultKind = null)
    {
        var prompt = new SelectionPrompt<StrategyKind>()
            .Title(title)
            .UseConverter(StrategyRegistry.GetStrategyLabel)
            .AddChoices(StrategyRegistry.StrategyOrder);

        return AnsiConsole.Prompt(prompt);
    }

    public IStrategy CreateStrategy(StrategyKind kind, StrategySettings? adxSettings = null)
    {
        return _strategyRegistry.CreateStrategy(kind, adxSettings);
    }
}
