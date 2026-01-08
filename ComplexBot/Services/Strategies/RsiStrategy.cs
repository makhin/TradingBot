using ComplexBot.Models;
using ComplexBot.Services.Indicators;
using ComplexBot.Services.Filters;

namespace ComplexBot.Services.Strategies;

/// <summary>
/// RSI Mean Reversion Strategy (Counter-Trend)
///
/// Philosophy: TRADES AGAINST EXTREMES (opposite of trend following)
/// - Buys oversold conditions (expects bounce)
/// - Sells overbought conditions (expects pullback)
///
/// Entry: RSI oversold/overbought with price confirmation
/// Exit: RSI returns to neutral zone or stop loss
///
/// Note: Works best in ranging/oscillating markets. In strong trends, signals may be premature.
/// </summary>
public class RsiStrategy : StrategyBase<RsiStrategySettings>, IHasConfidence, IProvidesIndicatorSnapshot
{
    public override string Name => "RSI Mean Reversion";
    public override decimal? CurrentStopLoss => _positionManager.StopLoss;

    private readonly Rsi _rsi;
    private readonly Atr _atr;
    private readonly Ema _trendFilter;
    private readonly VolumeFilter _volumeFilter;
    private readonly PositionManager _positionManager;

    private decimal? _previousRsi;
    private decimal? _currentRsi;
    private decimal? _currentTrendEma;

    public RsiStrategy(RsiStrategySettings? settings = null) : base(settings)
    {
        _rsi = new Rsi(Settings.RsiPeriod);
        _atr = new Atr(Settings.AtrPeriod);
        _trendFilter = new Ema(Settings.TrendFilterPeriod);
        _volumeFilter = new VolumeFilter(Settings.VolumePeriod, Settings.VolumeThreshold, Settings.RequireVolumeConfirmation);
        _positionManager = new PositionManager();
    }

    public decimal? CurrentRsi => _rsi.Value;
    public override decimal? CurrentAtr => _atr.Value;
    public IndicatorSnapshot GetIndicatorSnapshot()
        => IndicatorSnapshot.FromPairs(
            (IndicatorValueKey.Rsi, CurrentRsi),
            (IndicatorValueKey.Atr, CurrentAtr),
            (IndicatorValueKey.VolumeRatio, _volumeFilter.IsReady ? _volumeFilter.VolumeRatio : null)
        );

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
        _volumeFilter.Update(candle.Volume);
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

        bool volumeOk = _volumeFilter.IsConfirmed();
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

            _positionManager.EnterLong(candle.Close, stopLoss, candle.High);

            return TradeSignal.Create(
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

            _positionManager.EnterShort(candle.Close, stopLoss, candle.Low);

            return TradeSignal.Create(
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
        if (!_positionManager.StopLoss.HasValue || !_positionManager.EntryPrice.HasValue || !_currentRsi.HasValue)
            return null;

        bool isLong = position > 0;
        var rsi = _currentRsi.Value;

        // Stop loss check
        if (isLong && candle.Low <= _positionManager.StopLoss.Value)
        {
            var stopLoss = _positionManager.StopLoss.Value;
            ResetPosition();
            return TradeSignal.Create(symbol, SignalType.Exit, candle.Close, null, null,
                $"Stop loss hit @ {stopLoss:F2}");
        }
        else if (!isLong && candle.High >= _positionManager.StopLoss.Value)
        {
            var stopLoss = _positionManager.StopLoss.Value;
            ResetPosition();
            return TradeSignal.Create(symbol, SignalType.Exit, candle.Close, null, null,
                $"Stop loss hit @ {stopLoss:F2}");
        }

        // RSI exit conditions
        if (isLong)
        {
            // Exit long when RSI reaches overbought (mean reversion complete)
            if (rsi >= Settings.OverboughtLevel)
            {
                ResetPosition();
                return TradeSignal.Create(symbol, SignalType.Exit, candle.Close, null, null,
                    $"RSI reached overbought ({rsi:F1}) - taking profit");
            }

            // Exit long when RSI returns to neutral zone
            if (Settings.ExitOnNeutral && rsi >= Settings.NeutralZoneHigh)
            {
                ResetPosition();
                return TradeSignal.Create(symbol, SignalType.Exit, candle.Close, null, null,
                    $"RSI reached neutral zone ({rsi:F1}) - exit long");
            }
        }
        else
        {
            // Exit short when RSI reaches oversold (mean reversion complete)
            if (rsi <= Settings.OversoldLevel)
            {
                ResetPosition();
                return TradeSignal.Create(symbol, SignalType.Exit, candle.Close, null, null,
                    $"RSI reached oversold ({rsi:F1}) - taking profit");
            }

            // Exit short when RSI returns to neutral zone
            if (Settings.ExitOnNeutral && rsi <= Settings.NeutralZoneLow)
            {
                ResetPosition();
                return TradeSignal.Create(symbol, SignalType.Exit, candle.Close, null, null,
                    $"RSI reached neutral zone ({rsi:F1}) - exit short");
            }
        }

        return null;
    }

    private void ResetPosition()
    {
        _positionManager.Reset();
    }

    public override void Reset()
    {
        _rsi.Reset();
        _atr.Reset();
        _trendFilter.Reset();
        _volumeFilter.Reset();
        _previousRsi = null;
        ResetPosition();
    }
}
