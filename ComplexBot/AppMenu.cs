using Spectre.Console;
using TradingBot.Core.Models;
using TradingBot.Core.Utils;
using ComplexBot.Models;
using ComplexBot.Utils;

namespace ComplexBot;

class AppMenu
{
    public AppMode PromptMode()
    {
        AnsiConsole.Write(new FigletText("Trading Bot").Color(Color.Cyan1));
        AnsiConsole.MarkupLine("[grey]ADX Trend Following Strategy with Risk Management[/]");
        AnsiConsole.MarkupLine("[grey]Based on research: target Sharpe 1.5-1.9, max DD <20%[/]\n");

        return AnsiConsole.Prompt(
            new SelectionPrompt<AppMode>()
                .Title("Select [green]mode[/]:")
                .UseConverter(UiMappings.GetAppModeLabel)
                .AddChoices(UiMappings.AppModes)
        );
    }
}
