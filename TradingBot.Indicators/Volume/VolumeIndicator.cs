using System.Linq;
using TradingBot.Indicators.Base;
using TradingBot.Indicators.Utils;

namespace TradingBot.Indicators.Volume;

/// <summary>
/// Volume indicator - detects unusual volume spikes
/// </summary>
public class VolumeIndicator : WindowedIndicator<decimal>
{
    private readonly decimal _spikeThreshold;
    private decimal? _currentVolume;

    public VolumeIndicator(int period = 20, decimal spikeThreshold = 1.5m) : base(period)
    {
        _spikeThreshold = spikeThreshold;
    }

    public decimal? AverageVolume => CurrentValue;
    public decimal? CurrentVolume => _currentVolume;
    public bool IsVolumeSpike => CurrentValue.HasValue && _currentVolume.HasValue
        && _currentVolume.Value >= CurrentValue.Value * _spikeThreshold;
    public decimal VolumeRatio => CurrentValue > 0 ? (_currentVolume ?? 0) / CurrentValue.Value : 0;

    public override decimal? Update(decimal volume)
    {
        _currentVolume = volume;
        AddToWindow(volume);

        if (IsReady)
        {
            CurrentValue = Window.Average();
            return CurrentValue;
        }
        return null;
    }

    public override void Reset()
    {
        base.Reset();
        _currentVolume = null;
    }
}
