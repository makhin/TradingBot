using Spectre.Console;
using System.Globalization;

namespace TradingBot.Core.Utils;

/// <summary>
/// Helper methods for safe number formatting in Spectre.Console prompts.
/// Prevents locale-specific decimal separators (commas) from being interpreted as color codes.
/// </summary>
public static class SpectreHelpers
{
    /// <summary>
    /// Prompts the user for a decimal value with locale-safe formatting.
    /// </summary>
    /// <param name="prompt">The prompt message</param>
    /// <param name="defaultValue">Default value if user presses Enter</param>
    /// <param name="min">Minimum allowed value (optional)</param>
    /// <param name="max">Maximum allowed value (optional)</param>
    /// <returns>The validated decimal value</returns>
    public static decimal AskDecimal(string prompt, decimal defaultValue, decimal? min = null, decimal? max = null)
    {
        while (true)
        {
            // Format default value with invariant culture to avoid locale issues
            var formattedDefault = defaultValue.ToString("F2", CultureInfo.InvariantCulture);

            // Use [[ ]] to escape brackets in Spectre.Console markup
            var input = AnsiConsole.Ask($"{prompt} [[{formattedDefault}]]:",
                defaultValue.ToString(CultureInfo.InvariantCulture));

            if (string.IsNullOrWhiteSpace(input))
                return defaultValue;

            // Support both comma and dot as decimal separator
            if (decimal.TryParse(input.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out var value))
            {
                if (value < min)
                {
                    AnsiConsole.MarkupLine($"[red]Value must be at least {FormatDecimal(min.Value)}[/]");
                    continue;
                }

                if (value > max)
                {
                    AnsiConsole.MarkupLine($"[red]Value must be at most {FormatDecimal(max.Value)}[/]");
                    continue;
                }

                if (min.HasValue && max.HasValue)
                {
                    if (value >= min.Value && value <= max.Value)
                        return value;

                    AnsiConsole.MarkupLine($"[red]Value must be between {FormatDecimal(min.Value)} and {FormatDecimal(max.Value)}[/]");
                }
                else
                {
                    return value;
                }
            }
            else
            {
                AnsiConsole.MarkupLine("[red]Invalid number format[/]");
            }
        }
    }

    /// <summary>
    /// Prompts the user for an integer value with locale-safe formatting.
    /// </summary>
    /// <param name="prompt">The prompt message</param>
    /// <param name="defaultValue">Default value if user presses Enter</param>
    /// <param name="min">Minimum allowed value (optional)</param>
    /// <param name="max">Maximum allowed value (optional)</param>
    /// <returns>The validated integer value</returns>
    public static int AskInt(string prompt, int defaultValue, int? min = null, int? max = null)
    {
        while (true)
        {
            // Use [[ ]] to escape brackets
            var input = AnsiConsole.Ask($"{prompt} [[{defaultValue}]]:",
                defaultValue.ToString(CultureInfo.InvariantCulture));

            if (string.IsNullOrWhiteSpace(input))
                return defaultValue;

            if (int.TryParse(input, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            {
                if (value < min)
                {
                    AnsiConsole.MarkupLine($"[red]Value must be at least {min.Value}[/]");
                    continue;
                }

                if (value > max)
                {
                    AnsiConsole.MarkupLine($"[red]Value must be at most {max.Value}[/]");
                    continue;
                }

                if (min.HasValue && max.HasValue)
                {
                    if (value >= min.Value && value <= max.Value)
                        return value;

                    AnsiConsole.MarkupLine($"[red]Value must be between {min.Value} and {max.Value}[/]");
                }
                else
                {
                    return value;
                }
            }
            else
            {
                AnsiConsole.MarkupLine("[red]Invalid number format[/]");
            }
        }
    }

    /// <summary>
    /// Formats a decimal number for safe display in Spectre.Console markup.
    /// Uses InvariantCulture to avoid comma decimal separators.
    /// </summary>
    public static string FormatDecimal(decimal value, int decimals = 2)
    {
        return value.ToString($"F{decimals}", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Formats an integer for safe display in Spectre.Console markup.
    /// Uses InvariantCulture to avoid locale-specific formatting.
    /// </summary>
    public static string FormatInt(int value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }
}
