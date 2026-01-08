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

        RuleFor(config => config.BinanceApi)
            .NotNull()
            .SetValidator(new BinanceApiSettingsValidator());

        RuleFor(config => config.Telegram)
            .NotNull()
            .SetValidator(new TelegramSettingsValidator());

        RuleFor(config => config.RiskManagement)
            .NotNull()
            .SetValidator(new RiskManagementSettingsValidator());

        RuleFor(config => config.LiveTrading)
            .NotNull()
            .SetValidator(new LiveTradingSettingsValidator());
    }
}
