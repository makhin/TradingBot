using ComplexBot.Models;
using ComplexBot.Services.Indicators.MovingAverages;

namespace ComplexBot.Services.Indicators.Volume;

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
