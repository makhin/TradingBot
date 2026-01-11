using System.Diagnostics;
using SignalBot.Configuration;
using SignalBot.Models;
using SignalBot.Telemetry;
using Serilog;
using Serilog.Context;

namespace SignalBot.Services.Validation;

/// <summary>
/// Validates and adjusts trading signals based on risk settings
/// </summary>
public class SignalValidator : ISignalValidator
{
    private readonly RiskOverrideSettings _settings;
    private readonly ILogger _logger;
    private readonly TradingSignalValidator _signalValidator = new();

    public SignalValidator(RiskOverrideSettings settings, ILogger? logger = null)
    {
        _settings = settings;
        _logger = logger ?? Log.ForContext<SignalValidator>();
    }

    public async Task<ValidationResult> ValidateAndAdjustAsync(
        TradingSignal signal,
        decimal accountEquity,
        CancellationToken ct = default)
    {
        try
        {
            using var activity = SignalBotTelemetry.ActivitySource.StartActivity("Validate", ActivityKind.Internal);
            activity?.SetTag("signal.id", signal.Id);
            activity?.SetTag("signal.symbol", signal.Symbol);
            activity?.SetTag("signal.direction", signal.Direction.ToString());

            var warnings = new List<string>();

            // 1. Validate basic signal data
            if (!_signalValidator.TryValidate(signal, out var validationError))
            {
                activity?.SetStatus(ActivityStatusCode.Error, validationError);
                return ValidationResult.Failed(validationError);
            }

            // 2. Determine leverage
            int leverage = _settings.Enabled
                ? (_settings.UseSignalLeverage
                    ? Math.Min(signal.OriginalLeverage, _settings.MaxLeverage)
                    : _settings.MaxLeverage)
                : signal.OriginalLeverage;

            if (leverage != signal.OriginalLeverage)
            {
                warnings.Add($"Leverage adjusted: {signal.OriginalLeverage}x → {leverage}x");
            }

            // 3. Calculate liquidation price
            decimal liquidationPrice = CalculateLiquidationPrice(
                signal.Entry,
                signal.Direction,
                leverage);

            // 4. Determine Stop Loss
            decimal stopLoss;

            if (_settings.Enabled && _settings.StopLossMode == "Calculate")
            {
                stopLoss = CalculateSafeStopLoss(signal.Entry, liquidationPrice, signal.Direction);
                warnings.Add($"SL calculated: {stopLoss:F8} (signal SL ignored: {signal.OriginalStopLoss:F8})");
            }
            else
            {
                // Validate that SL is reachable before liquidation
                bool slIsValid = signal.Direction == SignalDirection.Long
                    ? signal.OriginalStopLoss > liquidationPrice
                    : signal.OriginalStopLoss < liquidationPrice;

                if (slIsValid)
                {
                    stopLoss = signal.OriginalStopLoss;
                }
                else
                {
                    stopLoss = CalculateSafeStopLoss(signal.Entry, liquidationPrice, signal.Direction);
                    warnings.Add($"SL unreachable (liquidation first), adjusted to {stopLoss:F8}");
                }
            }

            // 5. Calculate Risk:Reward ratio
            decimal targetPrice = signal.Targets.FirstOrDefault();
            decimal riskReward = targetPrice > 0
                ? CalculateRiskReward(signal.Entry, stopLoss, targetPrice, signal.Direction)
                : 0;

            if (riskReward > 0 && riskReward < 1.0m)
            {
                warnings.Add($"Poor Risk:Reward ratio: {riskReward:F2}");
            }

            // 6. Create validated signal
            var validatedSignal = signal with
            {
                AdjustedStopLoss = stopLoss,
                AdjustedLeverage = leverage,
                LiquidationPrice = liquidationPrice,
                RiskRewardRatio = riskReward,
                IsValid = true,
                ValidationWarnings = warnings
            };

            using (LogContext.PushProperty("SignalId", signal.Id))
            {
                _logger.Information(
                    "Signal validated: {Symbol} {Direction}, Entry: {Entry}, SL: {SL} (orig: {OrigSL}), " +
                    "Liq: {Liq}, Leverage: {Lev}x, R:R: {RR:F2}",
                    signal.Symbol, signal.Direction, signal.Entry, stopLoss, signal.OriginalStopLoss,
                    liquidationPrice, leverage, riskReward);

                if (warnings.Any())
                {
                    _logger.Warning("Validation warnings for {Symbol}: {Warnings}",
                        signal.Symbol, string.Join("; ", warnings));
                }
            }

            return ValidationResult.Success(validatedSignal);
        }
        catch (Exception ex)
        {
            Activity.Current?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.Error(ex, "Error validating signal {SignalId}", signal.Id);
            return ValidationResult.Failed($"Validation error: {ex.Message}");
        }
    }

    private decimal CalculateLiquidationPrice(decimal entry, SignalDirection direction, int leverage)
    {
        // Simplified formula (real one depends on margin type, maintenance margin, etc.)
        // For isolated margin: liquidation happens at approximately entry ± (entry / leverage)
        decimal liquidationDistance = entry / leverage;

        return direction == SignalDirection.Long
            ? entry - liquidationDistance * 0.98m  // ~2% buffer
            : entry + liquidationDistance * 0.98m;
    }

    private decimal CalculateSafeStopLoss(decimal entry, decimal liquidationPrice, SignalDirection direction)
    {
        // SL at SafeDistanceFromLiquidation percent of distance from entry to liquidation
        decimal distance = Math.Abs(entry - liquidationPrice);
        decimal safeDistance = distance * _settings.SafeDistanceFromLiquidation;

        return direction == SignalDirection.Long
            ? entry - safeDistance
            : entry + safeDistance;
    }

    private decimal CalculateRiskReward(decimal entry, decimal stopLoss, decimal target, SignalDirection direction)
    {
        decimal risk = Math.Abs(entry - stopLoss);
        decimal reward = Math.Abs(target - entry);

        return risk > 0 ? reward / risk : 0;
    }
}
