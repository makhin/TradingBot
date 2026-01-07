namespace ComplexBot.Models;

public record TradeSignal
{
    private readonly decimal _price;
    private readonly decimal? _partialExitQuantity;

    private TradeSignal(
        string symbol,
        SignalType type,
        decimal price,
        decimal? stopLoss,
        decimal? takeProfit,
        string reason,
        decimal? partialExitPercent,
        decimal? partialExitQuantity,
        bool moveStopToBreakeven)
    {
        Symbol = symbol;
        Type = type;
        Price = price;
        StopLoss = stopLoss;
        TakeProfit = takeProfit;
        Reason = reason;
        PartialExitPercent = partialExitPercent;
        PartialExitQuantity = partialExitQuantity;
        MoveStopToBreakeven = moveStopToBreakeven;
    }

    public string Symbol { get; init; }
    public SignalType Type { get; init; }
    public decimal Price
    {
        get => _price;
        init
        {
            if (value <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Price must be positive.");
            }

            _price = value;
        }
    }
    public decimal? StopLoss { get; init; }
    public decimal? TakeProfit { get; init; }
    public string Reason { get; init; }
    public decimal? PartialExitPercent { get; init; }
    public decimal? PartialExitQuantity
    {
        get => _partialExitQuantity;
        init
        {
            if (value.HasValue && value.Value <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Partial exit quantity must be positive.");
            }

            _partialExitQuantity = value;
        }
    }
    public bool MoveStopToBreakeven { get; init; }

    public static TradeSignal Create(
        string symbol,
        SignalType type,
        decimal price,
        decimal? stopLoss,
        decimal? takeProfit,
        string reason,
        decimal? PartialExitPercent = null,
        decimal? PartialExitQuantity = null,
        bool MoveStopToBreakeven = false)
    {
        return new TradeSignal(
            symbol,
            type,
            price,
            stopLoss,
            takeProfit,
            reason,
            PartialExitPercent,
            PartialExitQuantity,
            MoveStopToBreakeven);
    }
}
