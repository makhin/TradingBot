using ComplexBot.Configuration;
using FluentValidation;

namespace ComplexBot.Configuration.Validation;

public class PathSettingsValidator : AbstractValidator<PathSettings>
{
    public PathSettingsValidator()
    {
        RuleFor(settings => settings.DataDirectory)
            .NotEmpty();

        RuleFor(settings => settings.LogsDirectory)
            .NotEmpty();
    }
}
