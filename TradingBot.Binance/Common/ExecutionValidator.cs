using Binance.Net.Enums;
using TradingBot.Core.Models;
using TradingBot.Binance.Common.Models;

namespace TradingBot.Binance.Common;

/// <summary>
/// Validates order execution against expected parameters (slippage, price)
/// </summary>
public class ExecutionValidator
{
    private readonly decimal _maxSlippagePercent;

    public ExecutionValidator(decimal maxSlippagePercent = 1.0m)
    {
        _maxSlippagePercent = maxSlippagePercent;
    }

    /// <summary>
    /// Validates execution result against expected price and direction
    /// </summary>
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

    /// <summary>
    /// Validates execution using TradeDirection instead of OrderSide
    /// </summary>
    public ExecutionResult ValidateExecution(
        decimal expectedPrice,
        decimal actualPrice,
        TradeDirection direction)
    {
        var side = direction == TradeDirection.Long ? OrderSide.Buy : OrderSide.Sell;
        return ValidateExecution(expectedPrice, actualPrice, side);
    }

    /// <summary>
    /// Returns a human-readable description of slippage quality
    /// </summary>
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

    /// <summary>
    /// GetSlippageDescription overload for TradeDirection
    /// </summary>
    public string GetSlippageDescription(ExecutionResult result, TradeDirection direction)
    {
        var side = direction == TradeDirection.Long ? OrderSide.Buy : OrderSide.Sell;
        return GetSlippageDescription(result, side);
    }
}
