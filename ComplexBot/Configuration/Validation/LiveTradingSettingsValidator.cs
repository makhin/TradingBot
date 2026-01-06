using ComplexBot.Configuration;
using FluentValidation;

namespace ComplexBot.Configuration.Validation;

public class LiveTradingSettingsValidator : AbstractValidator<LiveTradingSettings>
{
    public LiveTradingSettingsValidator()
    {
        RuleFor(settings => settings.WarmupCandles)
            .GreaterThan(0);

        RuleFor(settings => settings.MinimumOrderUsd)
            .GreaterThan(0);

        RuleFor(settings => settings.QuantityPrecision)
            .GreaterThanOrEqualTo(0);

        RuleFor(settings => settings.LimitOrderOffsetBps)
            .GreaterThanOrEqualTo(0);

        RuleFor(settings => settings.LimitOrderTimeoutSeconds)
            .GreaterThan(0);

        RuleFor(settings => settings.StatusLogIntervalMinutes)
            .GreaterThan(0);

        RuleFor(settings => settings.BalanceLogIntervalHours)
            .GreaterThan(0);
    }
}
