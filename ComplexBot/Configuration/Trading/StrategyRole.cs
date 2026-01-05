namespace ComplexBot.Configuration.Trading;

/// <summary>
/// Role of a symbol trader in multi-timeframe setup.
/// </summary>
public enum StrategyRole
{
    /// <summary>
    /// Primary strategy that generates entry/exit signals.
    /// </summary>
    Primary,

    /// <summary>
    /// Filter strategy that confirms, vetoes, or scores signals from primary.
    /// </summary>
    Filter,

    /// <summary>
    /// Exit-only strategy that manages position closure.
    /// </summary>
    Exit
}
