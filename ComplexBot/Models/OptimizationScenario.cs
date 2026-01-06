namespace ComplexBot.Models;

public enum OptimizationMode
{
    Full,
    Genetic,
    Quick,
    EnsembleWeightsOnly,
    EnsembleFull
}

public readonly record struct OptimizationScenario(
    StrategyKind Kind,
    OptimizationMode Mode);
