using ComplexBot.Services.Indicators;

namespace ComplexBot.Services.Filters;

/// <summary>
/// Volume confirmation filter for strategy signals
/// Encapsulates volume filtering logic to avoid duplication across strategies
/// </summary>
public class VolumeFilter
{
    private readonly VolumeIndicator _volumeIndicator;
    private readonly bool _isRequired;
    private readonly decimal _threshold;

    /// <summary>
    /// Creates a volume filter
    /// </summary>
    /// <param name="period">Period for average volume calculation</param>
    /// <param name="threshold">Minimum volume ratio (current/average) required</param>
    /// <param name="isRequired">If false, filter always passes (volume check disabled)</param>
    public VolumeFilter(int period = 20, decimal threshold = 1.0m, bool isRequired = true)
    {
        _volumeIndicator = new VolumeIndicator(period, threshold);
        _isRequired = isRequired;
        _threshold = threshold;
    }

    /// <summary>
    /// Current volume ratio (current volume / average volume)
    /// </summary>
    public decimal VolumeRatio => _volumeIndicator.VolumeRatio;

    /// <summary>
    /// Whether the indicator has enough data to provide reliable signals
    /// </summary>
    public bool IsReady => _volumeIndicator.IsReady;

    /// <summary>
    /// Average volume over the configured period
    /// </summary>
    public decimal? AverageVolume => _volumeIndicator.AverageVolume;

    /// <summary>
    /// Current volume value
    /// </summary>
    public decimal? CurrentVolume => _volumeIndicator.CurrentVolume;

    /// <summary>
    /// Whether current volume is a spike (exceeds threshold)
    /// </summary>
    public bool IsVolumeSpike => _volumeIndicator.IsVolumeSpike;

    /// <summary>
    /// Updates the filter with new volume data
    /// </summary>
    public void Update(decimal volume)
    {
        _volumeIndicator.Update(volume);
    }

    /// <summary>
    /// Checks if volume confirmation is satisfied
    /// Returns true if:
    /// - Volume check is disabled (!isRequired), OR
    /// - Indicator is ready AND volume ratio meets threshold
    /// </summary>
    public bool IsConfirmed()
    {
        if (!_isRequired)
            return true;

        return IsReady && VolumeRatio >= _threshold;
    }

    /// <summary>
    /// Resets the filter state
    /// </summary>
    public void Reset()
    {
        _volumeIndicator.Reset();
    }

    /// <summary>
    /// Gets diagnostic information about current volume state
    /// </summary>
    public string GetDiagnostics()
    {
        if (!IsReady)
            return "Volume: Not ready";

        return $"Vol: {VolumeRatio:F2}x avg (req: {_threshold:F2}x, status: {(IsConfirmed() ? "OK" : "FAIL")})";
    }
}
