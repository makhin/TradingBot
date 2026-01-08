using FluentValidation;

namespace ComplexBot.Configuration.Validation;

public class RiskManagementSettingsValidator : AbstractValidator<RiskManagementSettings>
{
    public RiskManagementSettingsValidator()
    {
        RuleFor(settings => settings.RiskPerTradePercent)
            .GreaterThan(0)
            .WithMessage("RiskManagement.RiskPerTradePercent must be greater than 0")
            .LessThanOrEqualTo(10)
            .WithMessage("RiskManagement.RiskPerTradePercent should not exceed 10% (recommended max 2-3%)");

        RuleFor(settings => settings.MaxPortfolioHeatPercent)
            .GreaterThan(0)
            .WithMessage("RiskManagement.MaxPortfolioHeatPercent must be greater than 0")
            .LessThanOrEqualTo(100)
            .WithMessage("RiskManagement.MaxPortfolioHeatPercent cannot exceed 100%");

        RuleFor(settings => settings.MaxDrawdownPercent)
            .GreaterThan(0)
            .WithMessage("RiskManagement.MaxDrawdownPercent must be greater than 0")
            .LessThanOrEqualTo(100)
            .WithMessage("RiskManagement.MaxDrawdownPercent cannot exceed 100%");

        RuleFor(settings => settings.MaxDailyDrawdownPercent)
            .GreaterThan(0)
            .WithMessage("RiskManagement.MaxDailyDrawdownPercent must be greater than 0")
            .LessThanOrEqualTo(settings => settings.MaxDrawdownPercent)
            .WithMessage("RiskManagement.MaxDailyDrawdownPercent should not exceed MaxDrawdownPercent");

        RuleFor(settings => settings.AtrStopMultiplier)
            .GreaterThan(0)
            .WithMessage("RiskManagement.AtrStopMultiplier must be greater than 0 (otherwise stop loss would be at entry price)");

        RuleFor(settings => settings.TakeProfitMultiplier)
            .GreaterThan(0)
            .WithMessage("RiskManagement.TakeProfitMultiplier must be greater than 0");

        RuleFor(settings => settings.MinimumEquityUsd)
            .GreaterThanOrEqualTo(0)
            .WithMessage("RiskManagement.MinimumEquityUsd cannot be negative");

        // Validate DrawdownRiskPolicy thresholds are reachable
        RuleFor(settings => settings.DrawdownRiskPolicy)
            .Must((settings, policies) => AllThresholdsReachable(settings.MaxDrawdownPercent, policies))
            .When(settings => settings.DrawdownRiskPolicy != null && settings.DrawdownRiskPolicy.Count > 0)
            .WithMessage(settings => $"RiskManagement.DrawdownRiskPolicy contains unreachable thresholds. " +
                                    $"All DrawdownThresholdPercent values must be less than or equal to MaxDrawdownPercent ({settings.MaxDrawdownPercent}%)");

        RuleFor(settings => settings.MaxPortfolioHeatPercent)
            .GreaterThanOrEqualTo(settings => settings.RiskPerTradePercent)
            .WithMessage("RiskManagement.MaxPortfolioHeatPercent should be >= RiskPerTradePercent");
    }

    private static bool AllThresholdsReachable(decimal maxDrawdown, List<Services.RiskManagement.DrawdownRiskPolicy> policies)
    {
        return policies.All(p => p.DrawdownThresholdPercent <= maxDrawdown);
    }
}
