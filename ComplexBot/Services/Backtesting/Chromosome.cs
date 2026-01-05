namespace ComplexBot.Services.Backtesting;

/// <summary>
/// Chromosome representing a candidate solution
/// </summary>
public record Chromosome<TSettings> where TSettings : class
{
    public required TSettings Settings { get; init; }
    public decimal Fitness { get; set; }
    public bool IsEvaluated { get; set; }
}
