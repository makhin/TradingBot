using Binance.Net.Enums;

namespace ComplexBot.Services.Trading;

public class ExecutionValidator
{
    private readonly decimal _maxSlippagePercent;

    public ExecutionValidator(decimal maxSlippagePercent = 1.0m)
    {
        _maxSlippagePercent = maxSlippagePercent;
    }

    public ExecutionResult ValidateExecution(
        decimal expectedPrice,
        decimal actualPrice,
        OrderSide side)
    {
        // Calculate slippage
        // For Buy: positive slippage = paid more than expected (bad)
        // For Sell: positive slippage = received less than expected (bad)
        decimal slippage = side == OrderSide.Buy
            ? (actualPrice - expectedPrice) / expectedPrice * 100
            : (expectedPrice - actualPrice) / expectedPrice * 100;

        decimal slippageAmount = Math.Abs(actualPrice - expectedPrice);
        bool isAcceptable = Math.Abs(slippage) <= _maxSlippagePercent;

        return new ExecutionResult
        {
            IsAcceptable = isAcceptable,
            ExpectedPrice = expectedPrice,
            ActualPrice = actualPrice,
            SlippagePercent = slippage,
            SlippageAmount = slippageAmount,
            RejectReason = isAcceptable
                ? null
                : $"Slippage {Math.Abs(slippage):F2}% exceeds max {_maxSlippagePercent}%"
        };
    }

    public string GetSlippageDescription(ExecutionResult result, OrderSide side)
    {
        if (Math.Abs(result.SlippagePercent) < 0.01m)
            return "✅ Excellent execution (no slippage)";

        string direction = side == OrderSide.Buy
            ? (result.SlippagePercent > 0 ? "worse" : "better")
            : (result.SlippagePercent > 0 ? "better" : "worse");

        string emoji = Math.Abs(result.SlippagePercent) switch
        {
            < 0.1m => "✅",
            < 0.5m => "⚠️",
            _ => "❌"
        };

        return $"{emoji} Slippage: {Math.Abs(result.SlippagePercent):F3}% ({direction} than expected)";
    }
}
