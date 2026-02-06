using Serilog;

namespace TradingBot.Bybit.Common;

/// <summary>
/// Validates Bybit order execution results for slippage and price deviation
/// </summary>
public class BybitExecutionValidator
{
    private readonly decimal _maxSlippagePercent;
    private readonly ILogger _logger;

    public BybitExecutionValidator(decimal maxSlippagePercent, ILogger? logger = null)
    {
        _maxSlippagePercent = maxSlippagePercent;
        _logger = logger ?? Log.ForContext<BybitExecutionValidator>();
    }

    /// <summary>
    /// Validates that the executed price is within acceptable slippage range
    /// </summary>
    public bool ValidateExecution(decimal expectedPrice, decimal executedPrice, string symbol)
    {
        if (expectedPrice <= 0 || executedPrice <= 0)
        {
            _logger.Warning("Invalid prices for validation: Expected={Expected}, Executed={Executed}",
                expectedPrice, executedPrice);
            return false;
        }

        var slippage = Math.Abs((executedPrice - expectedPrice) / expectedPrice) * 100m;

        if (slippage > _maxSlippagePercent)
        {
            _logger.Warning(
                "Excessive slippage detected for {Symbol}: Expected={Expected}, Executed={Executed}, Slippage={Slippage}%",
                symbol, expectedPrice, executedPrice, slippage);
            return false;
        }

        _logger.Debug("Execution validated for {Symbol}: Slippage={Slippage}% (max {MaxSlippage}%)",
            symbol, slippage, _maxSlippagePercent);

        return true;
    }
}
