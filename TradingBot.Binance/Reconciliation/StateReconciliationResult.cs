using System.Collections.Generic;
using TradingBot.Core.State;

namespace TradingBot.Binance.Reconciliation;

public record StateReconciliationResult
{
    public List<SavedPosition> PositionsConfirmed { get; init; } = new();
    public List<(SavedPosition Expected, decimal Actual)> PositionsMismatch { get; init; } = new();
    public List<SavedOcoOrder> OcoOrdersActive { get; init; } = new();
    public List<SavedOcoOrder> OcoOrdersMissing { get; init; } = new();

    public bool HasMismatches => PositionsMismatch.Count > 0 || OcoOrdersMissing.Count > 0;
    public bool IsFullyReconciled => !HasMismatches;
}
