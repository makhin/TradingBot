using ComplexBot.Configuration;
using FluentValidation;

namespace ComplexBot.Configuration.Validation;

public class AppSettingsValidator : AbstractValidator<AppSettings>
{
    public AppSettingsValidator()
    {
        RuleFor(settings => settings.Paths)
            .NotNull()
            .SetValidator(new PathSettingsValidator());

        RuleFor(settings => settings.AllowedIntervals)
            .NotNull()
            .Must(intervals => intervals.Count > 0)
            .WithMessage("AllowedIntervals must contain at least one interval.")
            .Must(intervals => intervals.Distinct().Count() == intervals.Count)
            .WithMessage("AllowedIntervals must not contain duplicates.");
    }
}
