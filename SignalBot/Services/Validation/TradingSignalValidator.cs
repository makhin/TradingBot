using SignalBot.Models;

namespace SignalBot.Services.Validation;

public class TradingSignalValidator
{
    public bool TryValidate(TradingSignal signal, out string errorMessage)
    {
        if (signal.Entry <= 0)
        {
            errorMessage = "Invalid entry price";
            return false;
        }

        if (signal.OriginalStopLoss <= 0)
        {
            errorMessage = "Invalid stop loss price";
            return false;
        }

        if (signal.Targets.Count == 0)
        {
            errorMessage = "No targets specified";
            return false;
        }

        if (signal.Direction == SignalDirection.Long)
        {
            // For Long: SL < Entry < Targets
            if (signal.OriginalStopLoss >= signal.Entry)
            {
                errorMessage = $"Long: Stop Loss ({signal.OriginalStopLoss}) must be below Entry ({signal.Entry})";
                return false;
            }

            foreach (var target in signal.Targets)
            {
                if (target <= signal.Entry)
                {
                    errorMessage = $"Long: Target ({target}) must be above Entry ({signal.Entry})";
                    return false;
                }
            }
        }
        else
        {
            // For Short: Targets < Entry < SL
            if (signal.OriginalStopLoss <= signal.Entry)
            {
                errorMessage = $"Short: Stop Loss ({signal.OriginalStopLoss}) must be above Entry ({signal.Entry})";
                return false;
            }

            foreach (var target in signal.Targets)
            {
                if (target >= signal.Entry)
                {
                    errorMessage = $"Short: Target ({target}) must be below Entry ({signal.Entry})";
                    return false;
                }
            }
        }

        errorMessage = string.Empty;
        return true;
    }
}
