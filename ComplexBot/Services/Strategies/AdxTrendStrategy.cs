using ComplexBot.Models;
using ComplexBot.Services.Indicators;
using ComplexBot.Services.Filters;
using System.Linq;

namespace ComplexBot.Services.Strategies;

/// <summary>
/// ADX Trend Following Strategy (Simplified Trend-Following)
///
/// Philosophy: TRADES WITH THE TREND (waits for directional moves)
/// - Enters when trend is confirmed (ADX > threshold)
/// - Exits when trend weakens or trailing stop hit
///
/// Entry: ADX > threshold + EMA crossover + DI confirmation
/// Exit: ATR-based trailing stop or ADX < exit threshold
///
/// Based on research: simple trend-following with minimal filters achieves Sharpe 1.5-1.9
/// </summary>
public class AdxTrendStrategy : StrategyBase<StrategySettings>, IHasConfidence
{
    public override string Name => "ADX Trend Following + Volume";
    public override decimal? CurrentStopLoss => _trailingStop;

    private readonly Adx _adx;
    private readonly Ema _fastEma;
    private readonly Ema _slowEma;
    private readonly Atr _atr;
    private readonly Macd _macd;
    private readonly Obv _obv;
    private readonly VolumeFilter _volumeFilter;
    private readonly Queue<decimal> _adxHistory;
    private decimal? _currentFastEma;
    private decimal? _currentSlowEma;
    private bool _adxRising;
    
    private decimal? _entryPrice;
    private decimal? _trailingStop;
    private decimal? _initialStop;
    private decimal? _highestSinceEntry;
    private decimal? _lowestSinceEntry;
    private bool _wasAboveThreshold;
    private int _adxFallingStreak;
    private decimal? _previousAdx;
    private bool _breakevenMoved;
    private int _barsSinceEntry;

    public AdxTrendStrategy(StrategySettings? settings = null) : base(settings)
    {
        _adx = new Adx(Settings.AdxPeriod);
        _fastEma = new Ema(Settings.FastEmaPeriod);
        _slowEma = new Ema(Settings.SlowEmaPeriod);
        _atr = new Atr(Settings.AtrPeriod);
        _macd = new Macd();
        _obv = new Obv(Settings.ObvPeriod);
        _volumeFilter = new VolumeFilter(Settings.VolumePeriod, Settings.VolumeThreshold, Settings.RequireVolumeConfirmation);
        _adxHistory = new Queue<decimal>(Settings.AdxSlopeLookback);
    }

    public override decimal? CurrentAtr => _atr.Value;
    public decimal? CurrentAdx => _adx.Value;
    public bool VolumeConfirmation => _obv.IsReady;

    /// <summary>
    /// Returns confidence based on ADX strength
    /// ADX 25 → 0.5, ADX 40 → 0.75, ADX 55+ → 1.0
    /// </summary>
    public decimal GetConfidence()
    {
        if (!_adx.Value.HasValue)
            return 0m;

        var adxValue = _adx.Value.Value;
        return Math.Min(1m, 0.5m + (adxValue - 25) / 60m);
    }

    protected override void UpdateIndicators(Candle candle)
    {
        _adx.Update(candle);
        _currentFastEma = _fastEma.Update(candle.Close);
        _currentSlowEma = _slowEma.Update(candle.Close);
        _atr.Update(candle);
        _macd.Update(candle.Close);
        _obv.Update(candle);
        _volumeFilter.Update(candle.Volume);

        bool indicatorsReady = _adx.IsReady
            && _currentFastEma.HasValue
            && _currentSlowEma.HasValue
            && _atr.Value.HasValue;

        if (indicatorsReady)
        {
            var adxValue = _adx.Value!.Value;
            _adxRising = IsAdxRising(adxValue);
            UpdateAdxHistory(adxValue);
            UpdateAdxFallingStreak(adxValue);
        }
    }

    protected override bool IndicatorsReady =>
        _adx.IsReady && _currentFastEma.HasValue && _currentSlowEma.HasValue && _atr.Value.HasValue;

    protected override TradeSignal? CheckEntryConditions(Candle candle, string symbol)
    {
        if (!_currentFastEma.HasValue || !_currentSlowEma.HasValue)
            return null;

        var fastEma = _currentFastEma.Value;
        var slowEma = _currentSlowEma.Value;
        decimal adx = _adx.Value!.Value;
        decimal plusDi = _adx.PlusDi!.Value;
        decimal minusDi = _adx.MinusDi!.Value;
        decimal atr = _atr.Value!.Value;
        decimal atrPercent = candle.Close == 0 ? 0 : atr / candle.Close * 100m;

        // ADX must be above threshold (trending market)
        bool isTrending = adx >= Settings.AdxThreshold;
        
        // Track if we crossed above threshold (fresh trend)
        bool freshTrend = isTrending && !_wasAboveThreshold;
        _wasAboveThreshold = isTrending;

        if (!isTrending)
            return null;

        if (atrPercent < Settings.MinAtrPercent || atrPercent > Settings.MaxAtrPercent)
            return null;

        // Volume confirmation (research: valid breakouts show 1.5-2x average volume)
        bool volumeConfirmed = _volumeFilter.IsConfirmed();

        // OBV trend alignment (optional)
        bool obvBullish = !Settings.RequireObvConfirmation || !_obv.IsReady || _obv.IsBullish;
        bool obvBearish = !Settings.RequireObvConfirmation || !_obv.IsReady || _obv.IsBearish;

        // Calculate stops
        decimal longStop = candle.Close - (atr * Settings.AtrStopMultiplier);
        decimal shortStop = candle.Close + (atr * Settings.AtrStopMultiplier);
        decimal longTakeProfit = candle.Close + (atr * Settings.AtrStopMultiplier * Settings.TakeProfitMultiplier);
        decimal shortTakeProfit = candle.Close - (atr * Settings.AtrStopMultiplier * Settings.TakeProfitMultiplier);

        bool entryFreshTrendOk = !Settings.RequireFreshTrend || freshTrend;

        // Long entry: Fast EMA > Slow EMA + +DI > -DI + volume/OBV confirmed + optional fresh trend
        if (fastEma > slowEma && plusDi > minusDi && volumeConfirmed && obvBullish && entryFreshTrendOk)
        {
            _entryPrice = candle.Close;
            _highestSinceEntry = candle.High;
            _trailingStop = longStop;
            _initialStop = longStop;
            _barsSinceEntry = 0;
            _adxFallingStreak = 0;
            _previousAdx = _adx.Value;
            _breakevenMoved = false;

            return new TradeSignal(
                symbol,
                SignalType.Buy,
                candle.Close,
                longStop,
                longTakeProfit,
                $"Long: ADX={adx:F1}, +DI={plusDi:F1}>{minusDi:F1}, EMA={fastEma:F2}>{slowEma:F2}{(Settings.RequireFreshTrend ? ", FreshTrend" : string.Empty)}"
            );
        }

        // Short entry: Fast EMA < Slow EMA + -DI > +DI + volume/OBV confirmed + optional fresh trend
        if (fastEma < slowEma && minusDi > plusDi && volumeConfirmed && obvBearish && entryFreshTrendOk)
        {
            _entryPrice = candle.Close;
            _lowestSinceEntry = candle.Low;
            _trailingStop = shortStop;
            _initialStop = shortStop;
            _barsSinceEntry = 0;
            _adxFallingStreak = 0;
            _previousAdx = _adx.Value;
            _breakevenMoved = false;

            return new TradeSignal(
                symbol,
                SignalType.Sell,
                candle.Close,
                shortStop,
                shortTakeProfit,
                $"Short: ADX={adx:F1}, -DI={minusDi:F1}>{plusDi:F1}, EMA={fastEma:F2}<{slowEma:F2}{(Settings.RequireFreshTrend ? ", FreshTrend" : string.Empty)}"
            );
        }

        return null;
    }

    protected override TradeSignal? CheckExitConditions(Candle candle, decimal position, string symbol)
    {
        if (!_entryPrice.HasValue)
            return null;

        _barsSinceEntry++;
        decimal atr = _atr.Value!.Value;
        decimal adx = _adx.Value!.Value;
        bool isLong = position > 0;

        // Update trailing stop
        if (isLong)
        {
            if (candle.High > (_highestSinceEntry ?? candle.High))
            {
                _highestSinceEntry = candle.High;
                decimal newStop = candle.High - (atr * Settings.AtrStopMultiplier);
                _trailingStop = Math.Max(_trailingStop ?? 0, newStop);
            }

            // Exit conditions for long
            if (candle.Low <= _trailingStop)
            {
                ResetPosition();
                return new TradeSignal(symbol, SignalType.Exit, candle.Close, null, null, 
                    $"Trailing stop hit at {_trailingStop:F2}");
            }
        }
        else
        {
            if (candle.Low < (_lowestSinceEntry ?? candle.Low))
            {
                _lowestSinceEntry = candle.Low;
                decimal newStop = candle.Low + (atr * Settings.AtrStopMultiplier);
                _trailingStop = Math.Min(_trailingStop ?? decimal.MaxValue, newStop);
            }

            // Exit conditions for short
            if (candle.High >= _trailingStop)
            {
                ResetPosition();
                return new TradeSignal(symbol, SignalType.Exit, candle.Close, null, null,
                    $"Trailing stop hit at {_trailingStop:F2}");
            }
        }

        if (!_breakevenMoved && _entryPrice.HasValue && _initialStop.HasValue
            && Settings.PartialExitRMultiple > 0 && Settings.PartialExitFraction > 0)
        {
            decimal riskPerUnit = Math.Abs(_entryPrice.Value - _initialStop.Value);
            if (riskPerUnit > 0)
            {
                decimal achievedR = isLong
                    ? (candle.High - _entryPrice.Value) / riskPerUnit
                    : (_entryPrice.Value - candle.Low) / riskPerUnit;

                if (achievedR >= Settings.PartialExitRMultiple)
                {
                    _breakevenMoved = true;
                    _trailingStop = _entryPrice.Value;

                    return new TradeSignal(
                        symbol,
                        SignalType.PartialExit,
                        candle.Close,
                        _entryPrice.Value,
                        null,
                        $"Partial exit at {Settings.PartialExitRMultiple:F1}R, move stop to breakeven",
                        PartialExitPercent: Settings.PartialExitFraction,
                        MoveStopToBreakeven: true);
                }
            }
        }

        if (Settings.MaxBarsInTrade > 0 && _barsSinceEntry >= Settings.MaxBarsInTrade)
        {
            ResetPosition();
            return new TradeSignal(symbol, SignalType.Exit, candle.Close, null, null,
                "Time stop");
        }

        if (Settings.AdxFallingExitBars > 0 && _adxFallingStreak >= Settings.AdxFallingExitBars)
        {
            ResetPosition();
            return new TradeSignal(symbol, SignalType.Exit, candle.Close, null, null,
                $"ADX falling {_adxFallingStreak} bars in a row");
        }

        // Exit if trend weakens significantly
        if (adx < Settings.AdxExitThreshold)
        {
            ResetPosition();
            return new TradeSignal(symbol, SignalType.Exit, candle.Close, null, null,
                $"ADX dropped below {Settings.AdxExitThreshold} (trend weakening)");
        }

        return null;
    }

    private void ResetPosition()
    {
        _entryPrice = null;
        _trailingStop = null;
        _initialStop = null;
        _highestSinceEntry = null;
        _lowestSinceEntry = null;
        _barsSinceEntry = 0;
        _breakevenMoved = false;
    }

    public override void Reset()
    {
        _adx.Reset();
        _fastEma.Reset();
        _slowEma.Reset();
        _atr.Reset();
        _macd.Reset();
        _obv.Reset();
        _volumeFilter.Reset();
        ResetPosition();
        _wasAboveThreshold = false;
        _adxHistory.Clear();
        _adxFallingStreak = 0;
        _previousAdx = null;
    }

    private void UpdateAdxHistory(decimal currentAdx)
    {
        if (Settings.AdxSlopeLookback <= 0)
            return;

        if (_adxHistory.Count == Settings.AdxSlopeLookback)
            _adxHistory.Dequeue();

        _adxHistory.Enqueue(currentAdx);
    }

    private bool IsAdxRising(decimal currentAdx)
    {
        // If rising not required, always return true
        if (!Settings.RequireAdxRising)
            return true;

        // If rising IS required, check if we have enough data
        if (Settings.AdxSlopeLookback <= 0 || _adxHistory.Count < Settings.AdxSlopeLookback)
            return false;

        decimal averageAdx = _adxHistory.Average();
        return currentAdx > averageAdx;
    }

    private void UpdateAdxFallingStreak(decimal currentAdx)
    {
        if (_previousAdx.HasValue && currentAdx < _previousAdx.Value)
        {
            _adxFallingStreak++;
        }
        else
        {
            _adxFallingStreak = 0;
        }

        _previousAdx = currentAdx;
    }
}
