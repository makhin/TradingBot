using ComplexBot.Models;

namespace ComplexBot.Services.Indicators;

public class Ema
{
    private readonly int _period;
    private readonly decimal _multiplier;
    private decimal? _currentValue;
    private int _count;

    public Ema(int period)
    {
        _period = period;
        _multiplier = 2m / (period + 1);
    }

    public decimal? Value => _currentValue;
    public bool IsReady => _count >= _period;

    public decimal? Update(decimal price)
    {
        _count++;
        if (_currentValue == null)
            _currentValue = price;
        else
            _currentValue = (price - _currentValue.Value) * _multiplier + _currentValue.Value;
        
        return IsReady ? _currentValue : null;
    }

    public void Reset()
    {
        _currentValue = null;
        _count = 0;
    }
}

public class Atr
{
    private readonly int _period;
    private readonly Queue<decimal> _trueRanges = new();
    private decimal? _previousClose;
    private decimal? _currentValue;

    public Atr(int period = 14)
    {
        _period = period;
    }

    public decimal? Value => _currentValue;
    public bool IsReady => _trueRanges.Count >= _period;

    public decimal? Update(Candle candle)
    {
        decimal trueRange;
        
        if (_previousClose == null)
        {
            trueRange = candle.High - candle.Low;
        }
        else
        {
            trueRange = Math.Max(
                candle.High - candle.Low,
                Math.Max(
                    Math.Abs(candle.High - _previousClose.Value),
                    Math.Abs(candle.Low - _previousClose.Value)
                )
            );
        }

        _trueRanges.Enqueue(trueRange);
        if (_trueRanges.Count > _period)
            _trueRanges.Dequeue();

        _previousClose = candle.Close;

        if (_trueRanges.Count >= _period)
        {
            _currentValue = _trueRanges.Average();
            return _currentValue;
        }

        return null;
    }

    public void Reset()
    {
        _trueRanges.Clear();
        _previousClose = null;
        _currentValue = null;
    }
}

public class Adx
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

    public decimal? Update(Candle candle)
    {
        if (_previousHigh == null)
        {
            _previousHigh = candle.High;
            _previousLow = candle.Low;
            _previousClose = candle.Close;
            return null;
        }

        // Calculate +DM and -DM
        decimal upMove = candle.High - _previousHigh!.Value;
        decimal downMove = _previousLow!.Value - candle.Low;

        decimal plusDm = (upMove > downMove && upMove > 0) ? upMove : 0;
        decimal minusDm = (downMove > upMove && downMove > 0) ? downMove : 0;

        // Calculate True Range
        decimal tr = Math.Max(
            candle.High - candle.Low,
            Math.Max(
                Math.Abs(candle.High - _previousClose!.Value),
                Math.Abs(candle.Low - _previousClose.Value)
            )
        );

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

public class Macd
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

    public decimal? MacdLine { get; private set; }
    public decimal? SignalLine { get; private set; }
    public decimal? Histogram { get; private set; }
    public bool IsReady => _slowEma.IsReady && _signalEma.IsReady;

    public void Update(decimal price)
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

public class Sma
{
    private readonly int _period;
    private readonly Queue<decimal> _values = new();

    public Sma(int period)
    {
        _period = period;
    }

    public decimal? Value { get; private set; }
    public bool IsReady => _values.Count >= _period;

    public decimal? Update(decimal price)
    {
        _values.Enqueue(price);
        if (_values.Count > _period)
            _values.Dequeue();

        if (_values.Count >= _period)
        {
            Value = _values.Average();
            return Value;
        }
        return null;
    }

    public void Reset()
    {
        _values.Clear();
        Value = null;
    }
}

/// <summary>
/// On-Balance Volume - confirms trend strength via volume
/// </summary>
public class Obv
{
    private decimal _obv;
    private decimal? _previousClose;
    private readonly Sma _obvSma;
    
    public Obv(int signalPeriod = 20)
    {
        _obvSma = new Sma(signalPeriod);
    }

    public decimal Value => _obv;
    public decimal? Signal => _obvSma.Value;
    public bool IsReady => _obvSma.IsReady;
    
    // OBV rising = bullish, OBV falling = bearish
    public bool IsBullish => _obvSma.Value.HasValue && _obv > _obvSma.Value;
    public bool IsBearish => _obvSma.Value.HasValue && _obv < _obvSma.Value;

    public decimal Update(Candle candle)
    {
        if (_previousClose.HasValue)
        {
            if (candle.Close > _previousClose.Value)
                _obv += candle.Volume;
            else if (candle.Close < _previousClose.Value)
                _obv -= candle.Volume;
            // Equal: OBV unchanged
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
public class VolumeIndicator
{
    private readonly int _period;
    private readonly Queue<decimal> _volumes = new();
    private readonly decimal _spikeThreshold;

    public VolumeIndicator(int period = 20, decimal spikeThreshold = 1.5m)
    {
        _period = period;
        _spikeThreshold = spikeThreshold;
    }

    public decimal? AverageVolume { get; private set; }
    public decimal? CurrentVolume { get; private set; }
    public bool IsReady => _volumes.Count >= _period;
    public bool IsVolumeSpike => AverageVolume.HasValue && CurrentVolume.HasValue 
        && CurrentVolume.Value >= AverageVolume.Value * _spikeThreshold;
    public decimal VolumeRatio => AverageVolume > 0 ? (CurrentVolume ?? 0) / AverageVolume.Value : 0;

    public void Update(decimal volume)
    {
        CurrentVolume = volume;
        _volumes.Enqueue(volume);
        
        if (_volumes.Count > _period)
            _volumes.Dequeue();

        if (_volumes.Count >= _period)
            AverageVolume = _volumes.Average();
    }

    public void Reset()
    {
        _volumes.Clear();
        AverageVolume = null;
        CurrentVolume = null;
    }
}
