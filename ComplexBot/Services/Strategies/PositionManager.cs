namespace ComplexBot.Services.Strategies;

public class PositionManager
{
    public decimal? EntryPrice { get; private set; }
    public decimal? StopLoss { get; private set; }
    public decimal? InitialStop { get; private set; }
    public decimal? HighestSinceEntry { get; private set; }
    public decimal? LowestSinceEntry { get; private set; }
    public int BarsSinceEntry { get; private set; }

    public bool HasPosition => EntryPrice.HasValue;

    public void EnterLong(decimal entryPrice, decimal stopLoss, decimal? initialHigh = null)
    {
        Enter(entryPrice, stopLoss);
        HighestSinceEntry = initialHigh ?? entryPrice;
    }

    public void EnterShort(decimal entryPrice, decimal stopLoss, decimal? initialLow = null)
    {
        Enter(entryPrice, stopLoss);
        LowestSinceEntry = initialLow ?? entryPrice;
    }

    public void IncrementBars()
    {
        if (EntryPrice.HasValue)
            BarsSinceEntry++;
    }

    public decimal? UpdateLongStop(decimal newStop, decimal? latestHigh = null)
    {
        if (!EntryPrice.HasValue)
            return null;

        if (latestHigh.HasValue)
        {
            HighestSinceEntry = HighestSinceEntry.HasValue
                ? Math.Max(HighestSinceEntry.Value, latestHigh.Value)
                : latestHigh.Value;
        }

        StopLoss = StopLoss.HasValue ? Math.Max(StopLoss.Value, newStop) : newStop;
        return StopLoss;
    }

    public decimal? UpdateShortStop(decimal newStop, decimal? latestLow = null)
    {
        if (!EntryPrice.HasValue)
            return null;

        if (latestLow.HasValue)
        {
            LowestSinceEntry = LowestSinceEntry.HasValue
                ? Math.Min(LowestSinceEntry.Value, latestLow.Value)
                : latestLow.Value;
        }

        StopLoss = StopLoss.HasValue ? Math.Min(StopLoss.Value, newStop) : newStop;
        return StopLoss;
    }

    public void MoveStopToBreakeven()
    {
        if (EntryPrice.HasValue)
            StopLoss = EntryPrice.Value;
    }

    public void Reset()
    {
        EntryPrice = null;
        StopLoss = null;
        InitialStop = null;
        HighestSinceEntry = null;
        LowestSinceEntry = null;
        BarsSinceEntry = 0;
    }

    private void Enter(decimal entryPrice, decimal stopLoss)
    {
        EntryPrice = entryPrice;
        StopLoss = stopLoss;
        InitialStop = stopLoss;
        HighestSinceEntry = null;
        LowestSinceEntry = null;
        BarsSinceEntry = 0;
    }
}
