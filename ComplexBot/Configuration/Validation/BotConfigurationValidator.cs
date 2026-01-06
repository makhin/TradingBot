using ComplexBot.Configuration;
using FluentValidation;

namespace ComplexBot.Configuration.Validation;

public class BotConfigurationValidator : AbstractValidator<BotConfiguration>
{
    public BotConfigurationValidator()
    {
        RuleFor(config => config.App)
            .NotNull()
            .SetValidator(new AppSettingsValidator());

        RuleFor(config => config.LiveTrading)
            .NotNull()
            .SetValidator(new LiveTradingSettingsValidator());
    }
}
