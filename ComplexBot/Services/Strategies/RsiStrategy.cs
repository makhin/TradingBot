using ComplexBot.Models;
using ComplexBot.Services.Indicators;

namespace ComplexBot.Services.Strategies;

/// <summary>
/// RSI Mean Reversion Strategy
/// Entry: RSI oversold/overbought with price confirmation
/// Exit: RSI returns to neutral zone or stop loss
/// </summary>
public class RsiStrategy : StrategyBase<RsiStrategySettings>, IHasConfidence
{
    public override string Name => "RSI Mean Reversion";
    public override decimal? CurrentStopLoss => _stopLoss;

    private readonly Rsi _rsi;
    private readonly Atr _atr;
    private readonly Ema _trendFilter;
    private readonly VolumeIndicator _volume;

    private decimal? _previousRsi;
    private decimal? _currentRsi;
    private decimal? _currentTrendEma;
    private decimal? _entryPrice;
    private decimal? _stopLoss;

    public RsiStrategy(RsiStrategySettings? settings = null) : base(settings)
    {
        _rsi = new Rsi(Settings.RsiPeriod);
        _atr = new Atr(Settings.AtrPeriod);
        _trendFilter = new Ema(Settings.TrendFilterPeriod);
        _volume = new VolumeIndicator(Settings.VolumePeriod, Settings.VolumeThreshold);
    }

    public decimal? CurrentRsi => _rsi.Value;
    public override decimal? CurrentAtr => _atr.Value;

    /// <summary>
    /// Returns confidence based on RSI extremity
    /// More extreme RSI = higher confidence for mean reversion
    /// </summary>
    public decimal GetConfidence()
    {
        if (!_rsi.Value.HasValue)
            return 0m;

        var rsi = _rsi.Value.Value;

        // Confidence increases with RSI extremity
        if (rsi <= Settings.OversoldLevel)
        {
            // RSI 30 → 0.5, RSI 20 → 0.75, RSI 10 → 1.0
            return Math.Min(1m, 0.5m + (Settings.OversoldLevel - rsi) / 40m);
        }
        else if (rsi >= Settings.OverboughtLevel)
        {
            // RSI 70 → 0.5, RSI 80 → 0.75, RSI 90 → 1.0
            return Math.Min(1m, 0.5m + (rsi - Settings.OverboughtLevel) / 40m);
        }

        return 0m;
    }

    protected override void UpdateIndicators(Candle candle)
    {
        _currentRsi = _rsi.Update(candle.Close);
        _atr.Update(candle);
        _currentTrendEma = _trendFilter.Update(candle.Close);
        _volume.Update(candle.Volume);
    }

    protected override bool IndicatorsReady =>
        _currentRsi.HasValue && _atr.Value.HasValue && _currentTrendEma.HasValue;

    protected override void OnIndicatorsNotReady()
    {
        _previousRsi = _currentRsi;
    }

    protected override void AfterSignal(TradeSignal signal)
    {
        _previousRsi = _currentRsi;
    }

    protected override void AfterNoSignal()
    {
        _previousRsi = _currentRsi;
    }

    protected override TradeSignal? CheckEntryConditions(Candle candle, string symbol)
    {
        if (!_previousRsi.HasValue || !_atr.Value.HasValue
            || !_currentRsi.HasValue || !_currentTrendEma.HasValue)
            return null;

        bool volumeOk = !Settings.RequireVolumeConfirmation ||
            (_volume.IsReady && _volume.VolumeRatio >= Settings.VolumeThreshold);
        var atr = _atr.Value.Value;
        var rsi = _currentRsi.Value;
        var trendEma = _currentTrendEma.Value;

        // Oversold condition - potential long entry
        // RSI crosses above oversold level (was oversold, now recovering)
        if (_previousRsi.Value <= Settings.OversoldLevel && rsi > Settings.OversoldLevel && volumeOk)
        {
            // Optional: Only take longs above trend EMA
            if (Settings.UseTrendFilter && candle.Close < trendEma)
            {
                return null; // Skip - price below trend
            }

            var stopLoss = candle.Close - atr * Settings.AtrStopMultiplier;
            var takeProfit = candle.Close + atr * Settings.AtrStopMultiplier * Settings.TakeProfitMultiplier;

            _entryPrice = candle.Close;
            _stopLoss = stopLoss;

            return new TradeSignal(
                symbol,
                SignalType.Buy,
                candle.Close,
                stopLoss,
                takeProfit,
                $"RSI Oversold Recovery: RSI crossed above {Settings.OversoldLevel} (now {rsi:F1})"
            );
        }

        // Overbought condition - potential short entry
        // RSI crosses below overbought level (was overbought, now declining)
        if (_previousRsi.Value >= Settings.OverboughtLevel && rsi < Settings.OverboughtLevel && volumeOk)
        {
            // Optional: Only take shorts below trend EMA
            if (Settings.UseTrendFilter && candle.Close > trendEma)
            {
                return null; // Skip - price above trend
            }

            var stopLoss = candle.Close + atr * Settings.AtrStopMultiplier;
            var takeProfit = candle.Close - atr * Settings.AtrStopMultiplier * Settings.TakeProfitMultiplier;

            _entryPrice = candle.Close;
            _stopLoss = stopLoss;

            return new TradeSignal(
                symbol,
                SignalType.Sell,
                candle.Close,
                stopLoss,
                takeProfit,
                $"RSI Overbought Reversal: RSI crossed below {Settings.OverboughtLevel} (now {rsi:F1})"
            );
        }

        return null;
    }

    protected override TradeSignal? CheckExitConditions(Candle candle, decimal position, string symbol)
    {
        if (!_stopLoss.HasValue || !_entryPrice.HasValue || !_currentRsi.HasValue)
            return null;

        bool isLong = position > 0;
        var rsi = _currentRsi.Value;

        // Stop loss check
        if (isLong && candle.Low <= _stopLoss.Value)
        {
            ResetPosition();
            return new TradeSignal(symbol, SignalType.Exit, candle.Close, null, null,
                $"Stop loss hit @ {_stopLoss.Value:F2}");
        }
        else if (!isLong && candle.High >= _stopLoss.Value)
        {
            ResetPosition();
            return new TradeSignal(symbol, SignalType.Exit, candle.Close, null, null,
                $"Stop loss hit @ {_stopLoss.Value:F2}");
        }

        // RSI exit conditions
        if (isLong)
        {
            // Exit long when RSI reaches overbought (mean reversion complete)
            if (rsi >= Settings.OverboughtLevel)
            {
                ResetPosition();
                return new TradeSignal(symbol, SignalType.Exit, candle.Close, null, null,
                    $"RSI reached overbought ({rsi:F1}) - taking profit");
            }

            // Exit long when RSI returns to neutral zone
            if (Settings.ExitOnNeutral && rsi >= Settings.NeutralZoneHigh)
            {
                ResetPosition();
                return new TradeSignal(symbol, SignalType.Exit, candle.Close, null, null,
                    $"RSI reached neutral zone ({rsi:F1}) - exit long");
            }
        }
        else
        {
            // Exit short when RSI reaches oversold (mean reversion complete)
            if (rsi <= Settings.OversoldLevel)
            {
                ResetPosition();
                return new TradeSignal(symbol, SignalType.Exit, candle.Close, null, null,
                    $"RSI reached oversold ({rsi:F1}) - taking profit");
            }

            // Exit short when RSI returns to neutral zone
            if (Settings.ExitOnNeutral && rsi <= Settings.NeutralZoneLow)
            {
                ResetPosition();
                return new TradeSignal(symbol, SignalType.Exit, candle.Close, null, null,
                    $"RSI reached neutral zone ({rsi:F1}) - exit short");
            }
        }

        return null;
    }

    private void ResetPosition()
    {
        _entryPrice = null;
        _stopLoss = null;
    }

    public override void Reset()
    {
        _rsi.Reset();
        _atr.Reset();
        _trendFilter.Reset();
        _volume.Reset();
        _previousRsi = null;
        ResetPosition();
    }
}

public record RsiStrategySettings
{
    public int RsiPeriod { get; init; } = 14;
    public decimal OversoldLevel { get; init; } = 30m;
    public decimal OverboughtLevel { get; init; } = 70m;
    public decimal NeutralZoneLow { get; init; } = 45m;
    public decimal NeutralZoneHigh { get; init; } = 55m;
    public bool ExitOnNeutral { get; init; } = false;
    public int AtrPeriod { get; init; } = 14;
    public decimal AtrStopMultiplier { get; init; } = 1.5m;
    public decimal TakeProfitMultiplier { get; init; } = 2.0m;
    public int TrendFilterPeriod { get; init; } = 50;
    public bool UseTrendFilter { get; init; } = true;
    public int VolumePeriod { get; init; } = 20;
    public decimal VolumeThreshold { get; init; } = 1.0m;
    public bool RequireVolumeConfirmation { get; init; } = false;
}
