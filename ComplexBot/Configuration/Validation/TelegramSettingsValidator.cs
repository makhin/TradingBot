using FluentValidation;

namespace ComplexBot.Configuration.Validation;

public class TelegramSettingsValidator : AbstractValidator<TelegramSettings>
{
    public TelegramSettingsValidator()
    {
        RuleFor(settings => settings.BotToken)
            .NotEmpty()
            .When(settings => settings.Enabled)
            .WithMessage("Telegram.BotToken must not be empty when Telegram is enabled. Either set a valid token or disable Telegram notifications.");

        RuleFor(settings => settings.ChatId)
            .NotEqual(0L)
            .When(settings => settings.Enabled)
            .WithMessage("Telegram.ChatId must not be 0 when Telegram is enabled. Set a valid chat ID or disable Telegram notifications.");

        RuleFor(settings => settings.BotToken)
            .Matches(@"^\d+:[A-Za-z0-9_-]+$")
            .When(settings => settings.Enabled && !string.IsNullOrEmpty(settings.BotToken))
            .WithMessage("Telegram.BotToken format is invalid. Expected format: '123456:ABC-DEF1234ghIkl-zyx57W2v1u123ew11'");
    }
}
