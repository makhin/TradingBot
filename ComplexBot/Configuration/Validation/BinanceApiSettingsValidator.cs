using FluentValidation;

namespace ComplexBot.Configuration.Validation;

public class BinanceApiSettingsValidator : AbstractValidator<BinanceApiSettings>
{
    public BinanceApiSettingsValidator()
    {
        RuleFor(settings => settings.ApiKey)
            .NotEmpty()
            .WithMessage("BinanceApi.ApiKey must not be empty. Set valid API credentials in configuration or environment variables.");

        RuleFor(settings => settings.ApiSecret)
            .NotEmpty()
            .WithMessage("BinanceApi.ApiSecret must not be empty. Set valid API credentials in configuration or environment variables.");

        RuleFor(settings => settings.ApiKey)
            .MinimumLength(20)
            .When(settings => !string.IsNullOrEmpty(settings.ApiKey))
            .WithMessage("BinanceApi.ApiKey appears invalid (too short). Should be at least 20 characters.");

        RuleFor(settings => settings.ApiSecret)
            .MinimumLength(20)
            .When(settings => !string.IsNullOrEmpty(settings.ApiSecret))
            .WithMessage("BinanceApi.ApiSecret appears invalid (too short). Should be at least 20 characters.");
    }
}
