namespace ComplexBot.Models.Records;

public record TradeJournalStats
{
    public int TotalTrades { get; init; }
    public decimal WinRate { get; init; }
    public decimal AverageRMultiple { get; init; }
    public decimal TotalNetPnL { get; init; }
    public decimal AverageWin { get; init; }
    public decimal AverageLoss { get; init; }
    public decimal LargestWin { get; init; }
    public decimal LargestLoss { get; init; }
    public double AverageBarsInTrade { get; init; }
}
