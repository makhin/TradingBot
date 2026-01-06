using ComplexBot.Configuration;
using FluentValidation;

namespace ComplexBot.Configuration.Validation;

public class ConnectionSettingsValidator : AbstractValidator<ConnectionSettings>
{
    public ConnectionSettingsValidator()
    {
        RuleFor(settings => settings.Backoff)
            .NotNull()
            .SetValidator(new BackoffSettingsValidator());

        RuleFor(settings => settings.JitterFactor)
            .InclusiveBetween(0.0, 1.0)
            .WithMessage("JitterFactor must be between 0.0 and 1.0.");
    }
}
