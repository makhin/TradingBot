using ComplexBot.Configuration;
using FluentValidation;

namespace ComplexBot.Configuration.Validation;

public class BackoffSettingsValidator : AbstractValidator<BackoffSettings>
{
    public BackoffSettingsValidator()
    {
        RuleFor(settings => settings.MinDelayMs)
            .GreaterThan(0)
            .WithMessage("Backoff MinDelayMs must be greater than 0.");

        RuleFor(settings => settings.MaxDelayMs)
            .GreaterThan(0)
            .WithMessage("Backoff MaxDelayMs must be greater than 0.")
            .GreaterThanOrEqualTo(settings => settings.MinDelayMs)
            .WithMessage("Backoff MaxDelayMs must be >= MinDelayMs.");

        RuleFor(settings => settings.Factor)
            .GreaterThan(1.0)
            .WithMessage("Backoff Factor must be greater than 1.0.");

        RuleFor(settings => settings.DelaysMs)
            .Must(delays => delays.All(delay => delay > 0))
            .WithMessage("Backoff DelaysMs must contain only positive values.");
    }
}
