using SignalBot.Models;

namespace SignalBot.Services.Trading;

public static class PnlCalculator
{
    public static decimal Calculate(decimal entry, decimal exit, decimal quantity, SignalDirection direction)
    {
        var priceDiff = direction == SignalDirection.Long
            ? exit - entry
            : entry - exit;

        return priceDiff * quantity;
    }
}
