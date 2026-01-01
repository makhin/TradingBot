using ComplexBot.Models;
using ComplexBot.Services.Indicators;

namespace ComplexBot.Services.Strategies;

public interface IStrategy
{
    string Name { get; }
    TradeSignal? Analyze(Candle candle, decimal? currentPosition, string symbol);
    void Reset();
}

/// <summary>
/// ADX Trend Following Strategy with Volume Confirmation
/// Entry: ADX > 25 + EMA crossover + MACD confirmation + OBV trend alignment
/// Exit: ATR-based trailing stop or ADX < 20
/// Based on research: simple trend-following with proper filters achieves Sharpe 1.5-1.9
/// </summary>
public class AdxTrendStrategy : IStrategy
{
    public string Name => "ADX Trend Following + Volume";

    private readonly StrategySettings _settings;
    private readonly Adx _adx;
    private readonly Ema _fastEma;
    private readonly Ema _slowEma;
    private readonly Atr _atr;
    private readonly Macd _macd;
    private readonly Obv _obv;
    private readonly VolumeIndicator _volume;
    
    private decimal? _entryPrice;
    private decimal? _trailingStop;
    private decimal? _highestSinceEntry;
    private decimal? _lowestSinceEntry;
    private bool _wasAboveThreshold;

    public AdxTrendStrategy(StrategySettings? settings = null)
    {
        _settings = settings ?? new StrategySettings();
        _adx = new Adx(_settings.AdxPeriod);
        _fastEma = new Ema(_settings.FastEmaPeriod);
        _slowEma = new Ema(_settings.SlowEmaPeriod);
        _atr = new Atr(_settings.AtrPeriod);
        _macd = new Macd();
        _obv = new Obv(_settings.ObvPeriod);
        _volume = new VolumeIndicator(_settings.VolumePeriod, _settings.VolumeThreshold);
    }

    public decimal? CurrentAtr => _atr.Value;
    public decimal? CurrentAdx => _adx.Value;
    public bool VolumeConfirmation => _obv.IsReady;

    public TradeSignal? Analyze(Candle candle, decimal? currentPosition, string symbol)
    {
        // Update all indicators
        _adx.Update(candle);
        var fastEma = _fastEma.Update(candle.Close);
        var slowEma = _slowEma.Update(candle.Close);
        _atr.Update(candle);
        _macd.Update(candle.Close);
        _obv.Update(candle);
        _volume.Update(candle.Volume);

        // Check if indicators are ready
        if (!_adx.IsReady || !fastEma.HasValue || !slowEma.HasValue || !_atr.Value.HasValue)
            return null;

        bool hasPosition = currentPosition.HasValue && currentPosition.Value != 0;

        // Check exit conditions first if we have a position
        if (hasPosition && _entryPrice.HasValue)
        {
            var exitSignal = CheckExitConditions(candle, currentPosition!.Value, symbol);
            if (exitSignal != null)
                return exitSignal;
        }

        // Check entry conditions if no position
        if (!hasPosition)
        {
            return CheckEntryConditions(candle, fastEma.Value, slowEma.Value, symbol);
        }

        return null;
    }

    private TradeSignal? CheckEntryConditions(Candle candle, decimal fastEma, decimal slowEma, string symbol)
    {
        decimal adx = _adx.Value!.Value;
        decimal plusDi = _adx.PlusDi!.Value;
        decimal minusDi = _adx.MinusDi!.Value;
        decimal atr = _atr.Value!.Value;

        // ADX must be above threshold (trending market)
        bool isTrending = adx >= _settings.AdxThreshold;
        
        // Track if we crossed above threshold (fresh trend)
        bool freshTrend = isTrending && !_wasAboveThreshold;
        _wasAboveThreshold = isTrending;

        if (!isTrending)
            return null;

        // MACD confirmation
        bool macdBullish = _macd.Histogram > 0;
        bool macdBearish = _macd.Histogram < 0;

        // Volume confirmation (research: valid breakouts show 1.5-2x average volume)
        bool volumeConfirmed = !_settings.RequireVolumeConfirmation || 
            (_volume.IsReady && _volume.VolumeRatio >= _settings.VolumeThreshold);
        
        // OBV trend alignment
        bool obvBullish = !_settings.RequireObvConfirmation || !_obv.IsReady || _obv.IsBullish;
        bool obvBearish = !_settings.RequireObvConfirmation || !_obv.IsReady || _obv.IsBearish;

        // Calculate stops
        decimal longStop = candle.Close - (atr * _settings.AtrStopMultiplier);
        decimal shortStop = candle.Close + (atr * _settings.AtrStopMultiplier);
        decimal longTakeProfit = candle.Close + (atr * _settings.AtrStopMultiplier * _settings.TakeProfitMultiplier);
        decimal shortTakeProfit = candle.Close - (atr * _settings.AtrStopMultiplier * _settings.TakeProfitMultiplier);

        bool entryFreshTrendOk = !_settings.RequireFreshTrend || freshTrend;

        // Long entry: Fast EMA > Slow EMA + +DI > -DI + MACD bullish + volume/OBV confirmed + optional fresh trend
        if (fastEma > slowEma && plusDi > minusDi && macdBullish && volumeConfirmed && obvBullish && entryFreshTrendOk)
        {
            _entryPrice = candle.Close;
            _highestSinceEntry = candle.High;
            _trailingStop = longStop;

            return new TradeSignal(
                symbol,
                SignalType.Buy,
                candle.Close,
                longStop,
                longTakeProfit,
                $"Long: ADX={adx:F1}, +DI={plusDi:F1}>{minusDi:F1}, MACD={_macd.Histogram:F2}, Vol={_volume.VolumeRatio:F1}x{(_settings.RequireFreshTrend ? ", FreshTrend" : string.Empty)}"
            );
        }

        // Short entry: Fast EMA < Slow EMA + -DI > +DI + MACD bearish + volume/OBV confirmed + optional fresh trend
        if (fastEma < slowEma && minusDi > plusDi && macdBearish && volumeConfirmed && obvBearish && entryFreshTrendOk)
        {
            _entryPrice = candle.Close;
            _lowestSinceEntry = candle.Low;
            _trailingStop = shortStop;

            return new TradeSignal(
                symbol,
                SignalType.Sell,
                candle.Close,
                shortStop,
                shortTakeProfit,
                $"Short: ADX={adx:F1}, -DI={minusDi:F1}>{plusDi:F1}, MACD={_macd.Histogram:F2}, Vol={_volume.VolumeRatio:F1}x{(_settings.RequireFreshTrend ? ", FreshTrend" : string.Empty)}"
            );
        }

        return null;
    }

    private TradeSignal? CheckExitConditions(Candle candle, decimal position, string symbol)
    {
        decimal atr = _atr.Value!.Value;
        decimal adx = _adx.Value!.Value;
        bool isLong = position > 0;

        // Update trailing stop
        if (isLong)
        {
            if (candle.High > (_highestSinceEntry ?? candle.High))
            {
                _highestSinceEntry = candle.High;
                decimal newStop = candle.High - (atr * _settings.AtrStopMultiplier);
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
                decimal newStop = candle.Low + (atr * _settings.AtrStopMultiplier);
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

        // Exit if trend weakens significantly
        if (adx < _settings.AdxExitThreshold)
        {
            ResetPosition();
            return new TradeSignal(symbol, SignalType.Exit, candle.Close, null, null,
                $"ADX dropped below {_settings.AdxExitThreshold} (trend weakening)");
        }

        return null;
    }

    private void ResetPosition()
    {
        _entryPrice = null;
        _trailingStop = null;
        _highestSinceEntry = null;
        _lowestSinceEntry = null;
    }

    public void Reset()
    {
        _adx.Reset();
        _fastEma.Reset();
        _slowEma.Reset();
        _atr.Reset();
        _macd.Reset();
        _obv.Reset();
        _volume.Reset();
        ResetPosition();
        _wasAboveThreshold = false;
    }
}

public record StrategySettings
{
    // ADX settings
    public int AdxPeriod { get; init; } = 14;
    public decimal AdxThreshold { get; init; } = 25m;  // Entry: ADX > 25
    public decimal AdxExitThreshold { get; init; } = 18m;  // Exit: ADX < 18
    public bool RequireFreshTrend { get; init; } = false;
    
    // EMA settings (research: 20/50 optimal for medium-term)
    public int FastEmaPeriod { get; init; } = 20;
    public int SlowEmaPeriod { get; init; } = 50;
    
    // ATR settings
    public int AtrPeriod { get; init; } = 14;
    public decimal AtrStopMultiplier { get; init; } = 2.5m;  // 2.5x ATR for medium-term
    public decimal TakeProfitMultiplier { get; init; } = 1.5m;  // 1.5:1 reward:risk
    
    // Volume confirmation (research: 1.5-2x average volume confirms breakouts)
    public int VolumePeriod { get; init; } = 20;
    public decimal VolumeThreshold { get; init; } = 1.5m;  // 1.5x average volume
    public bool RequireVolumeConfirmation { get; init; } = true;
    
    // OBV settings
    public int ObvPeriod { get; init; } = 20;
    public bool RequireObvConfirmation { get; init; } = true;
}
