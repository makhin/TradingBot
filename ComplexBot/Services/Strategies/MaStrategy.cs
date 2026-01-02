using ComplexBot.Models;
using ComplexBot.Services.Indicators;

namespace ComplexBot.Services.Strategies;

/// <summary>
/// Simple Moving Average Crossover Strategy
/// Entry: Fast MA crosses above/below Slow MA with volume confirmation
/// Exit: Opposite crossover or trailing stop
/// </summary>
public class MaStrategy : IStrategy
{
    public string Name => "MA Crossover";

    private readonly MaStrategySettings _settings;
    private readonly Ema _fastMa;
    private readonly Ema _slowMa;
    private readonly Atr _atr;
    private readonly VolumeIndicator _volume;

    private decimal? _previousFastMa;
    private decimal? _previousSlowMa;
    private decimal? _entryPrice;
    private decimal? _trailingStop;
    private bool _inPosition;
    private SignalType _positionDirection;

    public MaStrategy(MaStrategySettings? settings = null)
    {
        _settings = settings ?? new MaStrategySettings();
        _fastMa = new Ema(_settings.FastMaPeriod);
        _slowMa = new Ema(_settings.SlowMaPeriod);
        _atr = new Atr(_settings.AtrPeriod);
        _volume = new VolumeIndicator(_settings.VolumePeriod, _settings.VolumeThreshold);
    }

    public decimal? CurrentAtr => _atr.Value;

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

    public TradeSignal? Analyze(Candle candle, decimal? currentPosition, string symbol)
    {
        // Update indicators
        var fastMa = _fastMa.Update(candle.Close);
        var slowMa = _slowMa.Update(candle.Close);
        _atr.Update(candle);
        _volume.Update(candle.Volume);

        // Check if indicators are ready
        if (!fastMa.HasValue || !slowMa.HasValue || !_atr.Value.HasValue)
        {
            _previousFastMa = fastMa;
            _previousSlowMa = slowMa;
            return null;
        }

        bool hasPosition = currentPosition.HasValue && currentPosition.Value != 0;
        _inPosition = hasPosition;

        // Check exit conditions first
        if (hasPosition && _entryPrice.HasValue)
        {
            var exitSignal = CheckExitConditions(candle, currentPosition!.Value, fastMa.Value, slowMa.Value, symbol);
            if (exitSignal != null)
            {
                _previousFastMa = fastMa;
                _previousSlowMa = slowMa;
                return exitSignal;
            }
        }

        // Check entry conditions
        if (!hasPosition && _previousFastMa.HasValue && _previousSlowMa.HasValue)
        {
            var entrySignal = CheckEntryConditions(candle, fastMa.Value, slowMa.Value, symbol);
            if (entrySignal != null)
            {
                _previousFastMa = fastMa;
                _previousSlowMa = slowMa;
                return entrySignal;
            }
        }

        _previousFastMa = fastMa;
        _previousSlowMa = slowMa;
        return null;
    }

    private TradeSignal? CheckEntryConditions(Candle candle, decimal fastMa, decimal slowMa, string symbol)
    {
        if (!_previousFastMa.HasValue || !_previousSlowMa.HasValue || !_atr.Value.HasValue)
            return null;

        bool volumeOk = !_settings.RequireVolumeConfirmation || _volume.Value > _settings.VolumeThreshold;

        // Bullish crossover: Fast MA crosses above Slow MA
        if (_previousFastMa.Value <= _previousSlowMa.Value && fastMa > slowMa && volumeOk)
        {
            var atr = _atr.Value.Value;
            var stopLoss = candle.Close - atr * _settings.AtrStopMultiplier;
            var takeProfit = candle.Close + atr * _settings.AtrStopMultiplier * _settings.TakeProfitMultiplier;

            _entryPrice = candle.Close;
            _trailingStop = stopLoss;
            _positionDirection = SignalType.Buy;

            return new TradeSignal(
                symbol,
                SignalType.Buy,
                candle.Close,
                stopLoss,
                takeProfit,
                $"MA Crossover: Fast({_settings.FastMaPeriod}) crossed above Slow({_settings.SlowMaPeriod})"
            );
        }

        // Bearish crossover: Fast MA crosses below Slow MA
        if (_previousFastMa.Value >= _previousSlowMa.Value && fastMa < slowMa && volumeOk)
        {
            var atr = _atr.Value.Value;
            var stopLoss = candle.Close + atr * _settings.AtrStopMultiplier;
            var takeProfit = candle.Close - atr * _settings.AtrStopMultiplier * _settings.TakeProfitMultiplier;

            _entryPrice = candle.Close;
            _trailingStop = stopLoss;
            _positionDirection = SignalType.Sell;

            return new TradeSignal(
                symbol,
                SignalType.Sell,
                candle.Close,
                stopLoss,
                takeProfit,
                $"MA Crossover: Fast({_settings.FastMaPeriod}) crossed below Slow({_settings.SlowMaPeriod})"
            );
        }

        return null;
    }

    private TradeSignal? CheckExitConditions(Candle candle, decimal position, decimal fastMa, decimal slowMa, string symbol)
    {
        if (!_atr.Value.HasValue || !_entryPrice.HasValue)
            return null;

        bool isLong = position > 0;
        var atr = _atr.Value.Value;

        // Update trailing stop
        if (isLong && _trailingStop.HasValue)
        {
            var newStop = candle.Close - atr * _settings.AtrStopMultiplier;
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
            var newStop = candle.Close + atr * _settings.AtrStopMultiplier;
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
        _inPosition = false;
    }

    public void Reset()
    {
        _fastMa.Reset();
        _slowMa.Reset();
        _atr.Reset();
        _volume.Reset();
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
