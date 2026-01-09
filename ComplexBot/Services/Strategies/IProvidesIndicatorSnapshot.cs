using TradingBot.Core.Models;

namespace ComplexBot.Services.Strategies;

public interface IProvidesIndicatorSnapshot
{
    IndicatorSnapshot GetIndicatorSnapshot();
}
