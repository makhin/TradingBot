using ComplexBot.Models;

namespace ComplexBot.Services.Indicators;

/// <summary>
/// Exponential Moving Average
/// </summary>
public class Ema : ExponentialIndicator<decimal>
{
    public Ema(int period) : base(period) { }

    public override decimal? Update(decimal price)
    {
        Smooth(price);
        return IsReady ? CurrentValue : null;
    }
}

/// <summary>
/// Simple Moving Average
/// </summary>
public class Sma : WindowedIndicator<decimal>
{
    public Sma(int period) : base(period) { }

    public override decimal? Update(decimal price)
    {
        AddToWindow(price);

        if (IsReady)
        {
            CurrentValue = Window.Average();
            return CurrentValue;
        }
        return null;
    }
}

/// <summary>
/// Average True Range - measures volatility
/// </summary>
public class Atr : WindowedIndicator<Candle>
{
    private decimal? _previousClose;

    public Atr(int period = 14) : base(period) { }

    public override decimal? Update(Candle candle)
    {
        decimal trueRange = CalculateTrueRange(candle);
        AddToWindow(trueRange);
        _previousClose = candle.Close;

        if (IsReady)
        {
            CurrentValue = Window.Average();
            return CurrentValue;
        }
        return null;
    }

    private decimal CalculateTrueRange(Candle candle)
    {
        if (_previousClose == null)
            return candle.High - candle.Low;

        return Math.Max(
            candle.High - candle.Low,
            Math.Max(
                Math.Abs(candle.High - _previousClose.Value),
                Math.Abs(candle.Low - _previousClose.Value)
            )
        );
    }

    public override void Reset()
    {
        base.Reset();
        _previousClose = null;
    }
}

/// <summary>
/// Average Directional Index - measures trend strength
/// </summary>
public class Adx : IIndicator<Candle>, IMultiValueIndicator
{
    private readonly int _period;
    private readonly Ema _smoothedPlusDm;
    private readonly Ema _smoothedMinusDm;
    private readonly Ema _smoothedTr;
    private readonly Ema _adxEma;
    private decimal? _previousHigh;
    private decimal? _previousLow;
    private decimal? _previousClose;

    public Adx(int period = 14)
    {
        _period = period;
        _smoothedPlusDm = new Ema(period);
        _smoothedMinusDm = new Ema(period);
        _smoothedTr = new Ema(period);
        _adxEma = new Ema(period);
    }

    public decimal? Value { get; private set; }
    public decimal? PlusDi { get; private set; }
    public decimal? MinusDi { get; private set; }
    public bool IsReady => Value.HasValue;

    public IReadOnlyDictionary<string, decimal?> Values => new Dictionary<string, decimal?>
    {
        ["ADX"] = Value,
        ["+DI"] = PlusDi,
        ["-DI"] = MinusDi
    };

    public decimal? Update(Candle candle)
    {
        if (_previousHigh == null)
        {
            _previousHigh = candle.High;
            _previousLow = candle.Low;
            _previousClose = candle.Close;
            return null;
        }

        // Calculate directional movement
        decimal upMove = candle.High - _previousHigh!.Value;
        decimal downMove = _previousLow!.Value - candle.Low;

        decimal plusDm = (upMove > downMove && upMove > 0) ? upMove : 0;
        decimal minusDm = (downMove > upMove && downMove > 0) ? downMove : 0;

        // Calculate True Range
        decimal tr = CalculateTrueRange(candle);

        // Smooth the values
        var smoothedTr = _smoothedTr.Update(tr);
        var smoothedPlusDm = _smoothedPlusDm.Update(plusDm);
        var smoothedMinusDm = _smoothedMinusDm.Update(minusDm);

        _previousHigh = candle.High;
        _previousLow = candle.Low;
        _previousClose = candle.Close;

        if (smoothedTr > 0 && _smoothedTr.IsReady)
        {
            PlusDi = 100 * smoothedPlusDm / smoothedTr;
            MinusDi = 100 * smoothedMinusDm / smoothedTr;

            decimal diSum = PlusDi!.Value + MinusDi!.Value;
            if (diSum > 0)
            {
                decimal dx = 100 * Math.Abs(PlusDi.Value - MinusDi.Value) / diSum;
                Value = _adxEma.Update(dx);
            }
        }

        return Value;
    }

    private decimal CalculateTrueRange(Candle candle)
    {
        return Math.Max(
            candle.High - candle.Low,
            Math.Max(
                Math.Abs(candle.High - _previousClose!.Value),
                Math.Abs(candle.Low - _previousClose.Value)
            )
        );
    }

    public void Reset()
    {
        _smoothedPlusDm.Reset();
        _smoothedMinusDm.Reset();
        _smoothedTr.Reset();
        _adxEma.Reset();
        _previousHigh = null;
        _previousLow = null;
        _previousClose = null;
        Value = null;
        PlusDi = null;
        MinusDi = null;
    }
}

/// <summary>
/// Moving Average Convergence Divergence
/// </summary>
public class Macd : IIndicator<decimal>, IMultiValueIndicator
{
    private readonly Ema _fastEma;
    private readonly Ema _slowEma;
    private readonly Ema _signalEma;

    public Macd(int fastPeriod = 12, int slowPeriod = 26, int signalPeriod = 9)
    {
        _fastEma = new Ema(fastPeriod);
        _slowEma = new Ema(slowPeriod);
        _signalEma = new Ema(signalPeriod);
    }

    public decimal? Value => MacdLine;
    public decimal? MacdLine { get; private set; }
    public decimal? SignalLine { get; private set; }
    public decimal? Histogram { get; private set; }
    public bool IsReady => _slowEma.IsReady && _signalEma.IsReady;

    public IReadOnlyDictionary<string, decimal?> Values => new Dictionary<string, decimal?>
    {
        ["MACD"] = MacdLine,
        ["Signal"] = SignalLine,
        ["Histogram"] = Histogram
    };

    public decimal? Update(decimal price)
    {
        var fast = _fastEma.Update(price);
        var slow = _slowEma.Update(price);

        if (fast.HasValue && slow.HasValue)
        {
            MacdLine = fast.Value - slow.Value;
            SignalLine = _signalEma.Update(MacdLine.Value);

            if (SignalLine.HasValue)
                Histogram = MacdLine.Value - SignalLine.Value;
        }

        return MacdLine;
    }

    public void Reset()
    {
        _fastEma.Reset();
        _slowEma.Reset();
        _signalEma.Reset();
        MacdLine = null;
        SignalLine = null;
        Histogram = null;
    }
}

/// <summary>
/// On-Balance Volume - confirms trend strength via volume
/// </summary>
public class Obv : IIndicator<Candle>
{
    private decimal _obv;
    private decimal? _previousClose;
    private readonly Sma _obvSma;

    public Obv(int signalPeriod = 20)
    {
        _obvSma = new Sma(signalPeriod);
    }

    public decimal? Value => _obv;
    public decimal? Signal => _obvSma.Value;
    public bool IsReady => _obvSma.IsReady;

    public bool IsBullish => _obvSma.Value.HasValue && _obv > _obvSma.Value;
    public bool IsBearish => _obvSma.Value.HasValue && _obv < _obvSma.Value;

    public decimal? Update(Candle candle)
    {
        if (_previousClose.HasValue)
        {
            if (candle.Close > _previousClose.Value)
                _obv += candle.Volume;
            else if (candle.Close < _previousClose.Value)
                _obv -= candle.Volume;
        }

        _previousClose = candle.Close;
        _obvSma.Update(_obv);

        return _obv;
    }

    public void Reset()
    {
        _obv = 0;
        _previousClose = null;
        _obvSma.Reset();
    }
}

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

/// <summary>
/// Relative Strength Index
/// </summary>
public class Rsi : IIndicator<decimal>
{
    private readonly int _period;
    private readonly Queue<decimal> _prices = new();
    private decimal? _avgGain;
    private decimal? _avgLoss;

    public Rsi(int period = 14)
    {
        _period = period;
    }

    public decimal? Value { get; private set; }
    public bool IsReady => _prices.Count > _period;

    public decimal? Update(decimal price)
    {
        _prices.Enqueue(price);

        if (_prices.Count > _period + 1)
            _prices.Dequeue();

        if (_prices.Count < _period + 1)
            return null;

        var changes = _prices.Zip(_prices.Skip(1), (prev, curr) => curr - prev).ToList();
        var gains = changes.Where(c => c > 0).ToList();
        var losses = changes.Where(c => c < 0).Select(Math.Abs).ToList();

        if (_avgGain == null)
        {
            _avgGain = gains.DefaultIfEmpty(0).Average();
            _avgLoss = losses.DefaultIfEmpty(0).Average();
        }
        else
        {
            decimal currentGain = changes.Last() > 0 ? changes.Last() : 0;
            decimal currentLoss = changes.Last() < 0 ? Math.Abs(changes.Last()) : 0;
            _avgGain = (_avgGain * (_period - 1) + currentGain) / _period;
            _avgLoss = (_avgLoss * (_period - 1) + currentLoss) / _period;
        }

        if (_avgLoss == 0)
        {
            Value = 100m;
        }
        else
        {
            var rs = _avgGain / _avgLoss;
            Value = 100m - (100m / (1m + rs));
        }

        return Value;
    }

    public void Reset()
    {
        _prices.Clear();
        _avgGain = null;
        _avgLoss = null;
        Value = null;
    }
}

/// <summary>
/// Bollinger Bands
/// </summary>
public class BollingerBands : WindowedIndicator<decimal>, IMultiValueIndicator
{
    private readonly decimal _stdDevMultiplier;

    public BollingerBands(int period = 20, decimal stdDevMultiplier = 2m) : base(period)
    {
        _stdDevMultiplier = stdDevMultiplier;
    }

    public decimal? Middle => CurrentValue;
    public decimal? Upper { get; private set; }
    public decimal? Lower { get; private set; }

    public IReadOnlyDictionary<string, decimal?> Values => new Dictionary<string, decimal?>
    {
        ["Middle"] = Middle,
        ["Upper"] = Upper,
        ["Lower"] = Lower
    };

    public override decimal? Update(decimal price)
    {
        AddToWindow(price);

        if (!IsReady)
            return null;

        CurrentValue = Window.Average();
        var variance = Window.Average(p => (p - CurrentValue.Value) * (p - CurrentValue.Value));
        var stdDev = (decimal)Math.Sqrt((double)variance);

        Upper = CurrentValue + (_stdDevMultiplier * stdDev);
        Lower = CurrentValue - (_stdDevMultiplier * stdDev);

        return CurrentValue;
    }

    public override void Reset()
    {
        base.Reset();
        Upper = null;
        Lower = null;
    }
}
