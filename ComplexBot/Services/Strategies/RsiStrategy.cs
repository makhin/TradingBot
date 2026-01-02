using ComplexBot.Models;
using ComplexBot.Services.Indicators;

namespace ComplexBot.Services.Strategies;

/// <summary>
/// RSI Mean Reversion Strategy
/// Entry: RSI oversold/overbought with price confirmation
/// Exit: RSI returns to neutral zone or stop loss
/// </summary>
public class RsiStrategy : IStrategy
{
    public string Name => "RSI Mean Reversion";

    private readonly RsiStrategySettings _settings;
    private readonly Rsi _rsi;
    private readonly Atr _atr;
    private readonly Ema _trendFilter;
    private readonly VolumeIndicator _volume;

    private decimal? _previousRsi;
    private decimal? _entryPrice;
    private decimal? _stopLoss;
    private bool _inPosition;
    private SignalType _positionDirection;

    public RsiStrategy(RsiStrategySettings? settings = null)
    {
        _settings = settings ?? new RsiStrategySettings();
        _rsi = new Rsi(_settings.RsiPeriod);
        _atr = new Atr(_settings.AtrPeriod);
        _trendFilter = new Ema(_settings.TrendFilterPeriod);
        _volume = new VolumeIndicator(_settings.VolumePeriod, _settings.VolumeThreshold);
    }

    public decimal? CurrentRsi => _rsi.Value;
    public decimal? CurrentAtr => _atr.Value;

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
        if (rsi <= _settings.OversoldLevel)
        {
            // RSI 30 → 0.5, RSI 20 → 0.75, RSI 10 → 1.0
            return Math.Min(1m, 0.5m + (_settings.OversoldLevel - rsi) / 40m);
        }
        else if (rsi >= _settings.OverboughtLevel)
        {
            // RSI 70 → 0.5, RSI 80 → 0.75, RSI 90 → 1.0
            return Math.Min(1m, 0.5m + (rsi - _settings.OverboughtLevel) / 40m);
        }

        return 0m;
    }

    public TradeSignal? Analyze(Candle candle, decimal? currentPosition, string symbol)
    {
        // Update indicators
        var rsi = _rsi.Update(candle.Close);
        _atr.Update(candle);
        var trendEma = _trendFilter.Update(candle.Close);
        _volume.Update(candle.Volume);

        // Check if indicators are ready
        if (!rsi.HasValue || !_atr.Value.HasValue || !trendEma.HasValue)
        {
            _previousRsi = rsi;
            return null;
        }

        bool hasPosition = currentPosition.HasValue && currentPosition.Value != 0;
        _inPosition = hasPosition;

        // Check exit conditions first
        if (hasPosition && _entryPrice.HasValue)
        {
            var exitSignal = CheckExitConditions(candle, currentPosition!.Value, rsi.Value, symbol);
            if (exitSignal != null)
            {
                _previousRsi = rsi;
                return exitSignal;
            }
        }

        // Check entry conditions
        if (!hasPosition && _previousRsi.HasValue)
        {
            var entrySignal = CheckEntryConditions(candle, rsi.Value, trendEma.Value, symbol);
            if (entrySignal != null)
            {
                _previousRsi = rsi;
                return entrySignal;
            }
        }

        _previousRsi = rsi;
        return null;
    }

    private TradeSignal? CheckEntryConditions(Candle candle, decimal rsi, decimal trendEma, string symbol)
    {
        if (!_previousRsi.HasValue || !_atr.Value.HasValue)
            return null;

        bool volumeOk = !_settings.RequireVolumeConfirmation || _volume.Value > _settings.VolumeThreshold;
        var atr = _atr.Value.Value;

        // Oversold condition - potential long entry
        // RSI crosses above oversold level (was oversold, now recovering)
        if (_previousRsi.Value <= _settings.OversoldLevel && rsi > _settings.OversoldLevel && volumeOk)
        {
            // Optional: Only take longs above trend EMA
            if (_settings.UseTrendFilter && candle.Close < trendEma)
            {
                return null; // Skip - price below trend
            }

            var stopLoss = candle.Close - atr * _settings.AtrStopMultiplier;
            var takeProfit = candle.Close + atr * _settings.AtrStopMultiplier * _settings.TakeProfitMultiplier;

            _entryPrice = candle.Close;
            _stopLoss = stopLoss;
            _positionDirection = SignalType.Buy;

            return new TradeSignal(
                symbol,
                SignalType.Buy,
                candle.Close,
                stopLoss,
                takeProfit,
                $"RSI Oversold Recovery: RSI crossed above {_settings.OversoldLevel} (now {rsi:F1})"
            );
        }

        // Overbought condition - potential short entry
        // RSI crosses below overbought level (was overbought, now declining)
        if (_previousRsi.Value >= _settings.OverboughtLevel && rsi < _settings.OverboughtLevel && volumeOk)
        {
            // Optional: Only take shorts below trend EMA
            if (_settings.UseTrendFilter && candle.Close > trendEma)
            {
                return null; // Skip - price above trend
            }

            var stopLoss = candle.Close + atr * _settings.AtrStopMultiplier;
            var takeProfit = candle.Close - atr * _settings.AtrStopMultiplier * _settings.TakeProfitMultiplier;

            _entryPrice = candle.Close;
            _stopLoss = stopLoss;
            _positionDirection = SignalType.Sell;

            return new TradeSignal(
                symbol,
                SignalType.Sell,
                candle.Close,
                stopLoss,
                takeProfit,
                $"RSI Overbought Reversal: RSI crossed below {_settings.OverboughtLevel} (now {rsi:F1})"
            );
        }

        return null;
    }

    private TradeSignal? CheckExitConditions(Candle candle, decimal position, decimal rsi, string symbol)
    {
        if (!_stopLoss.HasValue || !_entryPrice.HasValue)
            return null;

        bool isLong = position > 0;

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
            if (rsi >= _settings.OverboughtLevel)
            {
                ResetPosition();
                return new TradeSignal(symbol, SignalType.Exit, candle.Close, null, null,
                    $"RSI reached overbought ({rsi:F1}) - taking profit");
            }

            // Exit long when RSI returns to neutral zone
            if (_settings.ExitOnNeutral && rsi >= _settings.NeutralZoneHigh)
            {
                ResetPosition();
                return new TradeSignal(symbol, SignalType.Exit, candle.Close, null, null,
                    $"RSI reached neutral zone ({rsi:F1}) - exit long");
            }
        }
        else
        {
            // Exit short when RSI reaches oversold (mean reversion complete)
            if (rsi <= _settings.OversoldLevel)
            {
                ResetPosition();
                return new TradeSignal(symbol, SignalType.Exit, candle.Close, null, null,
                    $"RSI reached oversold ({rsi:F1}) - taking profit");
            }

            // Exit short when RSI returns to neutral zone
            if (_settings.ExitOnNeutral && rsi <= _settings.NeutralZoneLow)
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
        _inPosition = false;
    }

    public void Reset()
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
