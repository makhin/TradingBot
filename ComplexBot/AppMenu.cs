using Spectre.Console;

namespace ComplexBot;

class AppMenu
{
    public string PromptMode()
    {
        AnsiConsole.Write(new FigletText("Trading Bot").Color(Color.Cyan1));
        AnsiConsole.MarkupLine("[grey]ADX Trend Following Strategy with Risk Management[/]");
        AnsiConsole.MarkupLine("[grey]Based on research: target Sharpe 1.5-1.9, max DD <20%[/]\n");

        return AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Select [green]mode[/]:")
                .AddChoices(
                    "Backtest",
                    "Parameter Optimization",
                    "Walk-Forward Analysis",
                    "Monte Carlo Simulation",
                    "Live Trading (Paper)",
                    "Live Trading (Real)",
                    "Download Data",
                    "Configuration Settings",
                    "Reset to Defaults",
                    "Exit")
        );
    }
}
