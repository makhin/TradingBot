using ComplexBot.Models;
using ComplexBot.Services.Indicators;
using ComplexBot.Services.Filters;

namespace ComplexBot.Services.Strategies;

/// <summary>
/// Simple Moving Average Crossover Strategy (Trend-Following)
///
/// Philosophy: TRADES WITH THE TREND (follows EMA crossovers)
/// - Buys when fast EMA crosses above slow EMA (uptrend starts)
/// - Sells when fast EMA crosses below slow EMA (downtrend starts)
///
/// Entry: Fast MA crosses above/below Slow MA with volume confirmation
/// Exit: Opposite crossover or trailing stop
/// </summary>
public class MaStrategy : StrategyBase<MaStrategySettings>, IHasConfidence
{
    public override string Name => "MA Crossover";
    public override decimal? CurrentStopLoss => _trailingStop;

    private readonly Ema _fastMa;
    private readonly Ema _slowMa;
    private readonly Atr _atr;
    private readonly VolumeFilter _volumeFilter;

    private decimal? _previousFastMa;
    private decimal? _previousSlowMa;
    private decimal? _currentFastMa;
    private decimal? _currentSlowMa;
    private decimal? _entryPrice;
    private decimal? _trailingStop;

    public MaStrategy(MaStrategySettings? settings = null) : base(settings)
    {
        _fastMa = new Ema(Settings.FastMaPeriod);
        _slowMa = new Ema(Settings.SlowMaPeriod);
        _atr = new Atr(Settings.AtrPeriod);
        _volumeFilter = new VolumeFilter(Settings.VolumePeriod, Settings.VolumeThreshold, Settings.RequireVolumeConfirmation);
    }

    public override decimal? CurrentAtr => _atr.Value;

    /// <summary>
    /// Returns confidence based on MA separation and trend strength
    /// </summary>
    public decimal GetConfidence()
    {
        if (!_fastMa.Value.HasValue || !_slowMa.Value.HasValue)
            return 0m;

        var separation = Math.Abs(_fastMa.Value.Value - _slowMa.Value.Value) / _slowMa.Value.Value * 100;

        // Confidence based on MA separation (0-2% maps to 0.5-1.0)
        return Math.Min(1m, 0.5m + separation / 4m);
    }

    protected override void UpdateIndicators(Candle candle)
    {
        _currentFastMa = _fastMa.Update(candle.Close);
        _currentSlowMa = _slowMa.Update(candle.Close);
        _atr.Update(candle);
        _volumeFilter.Update(candle.Volume);
    }

    protected override bool IndicatorsReady =>
        _currentFastMa.HasValue && _currentSlowMa.HasValue && _atr.Value.HasValue;

    protected override void OnIndicatorsNotReady()
    {
        _previousFastMa = _currentFastMa;
        _previousSlowMa = _currentSlowMa;
    }

    protected override void AfterSignal(TradeSignal signal)
    {
        _previousFastMa = _currentFastMa;
        _previousSlowMa = _currentSlowMa;
    }

    protected override void AfterNoSignal()
    {
        _previousFastMa = _currentFastMa;
        _previousSlowMa = _currentSlowMa;
    }

    protected override TradeSignal? CheckEntryConditions(Candle candle, string symbol)
    {
        if (!_previousFastMa.HasValue || !_previousSlowMa.HasValue || !_atr.Value.HasValue
            || !_currentFastMa.HasValue || !_currentSlowMa.HasValue)
            return null;

        bool volumeOk = _volumeFilter.IsConfirmed();
        var fastMa = _currentFastMa.Value;
        var slowMa = _currentSlowMa.Value;

        // Bullish crossover: Fast MA crosses above Slow MA
        if (_previousFastMa.Value <= _previousSlowMa.Value && fastMa > slowMa && volumeOk)
        {
            var atr = _atr.Value.Value;
            var stopLoss = candle.Close - atr * Settings.AtrStopMultiplier;
            var takeProfit = candle.Close + atr * Settings.AtrStopMultiplier * Settings.TakeProfitMultiplier;

            _entryPrice = candle.Close;
            _trailingStop = stopLoss;

            return new TradeSignal(
                symbol,
                SignalType.Buy,
                candle.Close,
                stopLoss,
                takeProfit,
                $"MA Crossover: Fast({Settings.FastMaPeriod}) crossed above Slow({Settings.SlowMaPeriod})"
            );
        }

        // Bearish crossover: Fast MA crosses below Slow MA
        if (_previousFastMa.Value >= _previousSlowMa.Value && fastMa < slowMa && volumeOk)
        {
            var atr = _atr.Value.Value;
            var stopLoss = candle.Close + atr * Settings.AtrStopMultiplier;
            var takeProfit = candle.Close - atr * Settings.AtrStopMultiplier * Settings.TakeProfitMultiplier;

            _entryPrice = candle.Close;
            _trailingStop = stopLoss;

            return new TradeSignal(
                symbol,
                SignalType.Sell,
                candle.Close,
                stopLoss,
                takeProfit,
                $"MA Crossover: Fast({Settings.FastMaPeriod}) crossed below Slow({Settings.SlowMaPeriod})"
            );
        }

        return null;
    }

    protected override TradeSignal? CheckExitConditions(Candle candle, decimal position, string symbol)
    {
        if (!_atr.Value.HasValue || !_entryPrice.HasValue
            || !_currentFastMa.HasValue || !_currentSlowMa.HasValue)
            return null;

        bool isLong = position > 0;
        var atr = _atr.Value.Value;
        var fastMa = _currentFastMa.Value;
        var slowMa = _currentSlowMa.Value;

        // Update trailing stop
        if (isLong && _trailingStop.HasValue)
        {
            var newStop = candle.Close - atr * Settings.AtrStopMultiplier;
            if (newStop > _trailingStop.Value)
                _trailingStop = newStop;

            // Stop loss hit
            if (candle.Low <= _trailingStop.Value)
            {
                ResetPosition();
                return new TradeSignal(symbol, SignalType.Exit, candle.Close, null, null,
                    $"Trailing stop hit @ {_trailingStop.Value:F2}");
            }
        }
        else if (!isLong && _trailingStop.HasValue)
        {
            var newStop = candle.Close + atr * Settings.AtrStopMultiplier;
            if (newStop < _trailingStop.Value)
                _trailingStop = newStop;

            // Stop loss hit
            if (candle.High >= _trailingStop.Value)
            {
                ResetPosition();
                return new TradeSignal(symbol, SignalType.Exit, candle.Close, null, null,
                    $"Trailing stop hit @ {_trailingStop.Value:F2}");
            }
        }

        // Opposite crossover exit
        if (isLong && _previousFastMa.HasValue && _previousSlowMa.HasValue)
        {
            if (_previousFastMa.Value >= _previousSlowMa.Value && fastMa < slowMa)
            {
                ResetPosition();
                return new TradeSignal(symbol, SignalType.Exit, candle.Close, null, null,
                    "Bearish MA crossover - exit long");
            }
        }
        else if (!isLong && _previousFastMa.HasValue && _previousSlowMa.HasValue)
        {
            if (_previousFastMa.Value <= _previousSlowMa.Value && fastMa > slowMa)
            {
                ResetPosition();
                return new TradeSignal(symbol, SignalType.Exit, candle.Close, null, null,
                    "Bullish MA crossover - exit short");
            }
        }

        return null;
    }

    private void ResetPosition()
    {
        _entryPrice = null;
        _trailingStop = null;
    }

    public override void Reset()
    {
        _fastMa.Reset();
        _slowMa.Reset();
        _atr.Reset();
        _volumeFilter.Reset();
        _previousFastMa = null;
        _previousSlowMa = null;
        ResetPosition();
    }
}

public record MaStrategySettings
{
    public int FastMaPeriod { get; init; } = 10;
    public int SlowMaPeriod { get; init; } = 30;
    public int AtrPeriod { get; init; } = 14;
    public decimal AtrStopMultiplier { get; init; } = 2.0m;
    public decimal TakeProfitMultiplier { get; init; } = 2.0m;
    public int VolumePeriod { get; init; } = 20;
    public decimal VolumeThreshold { get; init; } = 1.2m;
    public bool RequireVolumeConfirmation { get; init; } = true;
}
