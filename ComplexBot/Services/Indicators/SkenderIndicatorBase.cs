using System;
using System.Collections.Generic;
using Skender.Stock.Indicators;

namespace ComplexBot.Services.Indicators;

internal abstract class SkenderIndicatorBase<TInput, TResult> : IIndicator<TInput>
{
    private readonly QuoteSeries _series = new();
    private readonly Action<QuoteSeries, TInput> _appendInput;
    private readonly Func<IReadOnlyList<Quote>, TResult?> _calculate;
    private readonly Action<TResult?> _applyResult;

    protected SkenderIndicatorBase(
        Action<QuoteSeries, TInput> appendInput,
        Func<IReadOnlyList<Quote>, TResult?> calculate,
        Action<TResult?> applyResult)
    {
        _appendInput = appendInput ?? throw new ArgumentNullException(nameof(appendInput));
        _calculate = calculate ?? throw new ArgumentNullException(nameof(calculate));
        _applyResult = applyResult ?? throw new ArgumentNullException(nameof(applyResult));
    }

    public decimal? Value { get; protected set; }
    public virtual bool IsReady => Value.HasValue;

    public decimal? Update(TInput input)
    {
        _appendInput(_series, input);

        var result = _calculate(_series.Quotes);
        _applyResult(result);
        return Value;
    }

    public void Reset()
    {
        _series.Reset();
        ResetValues();
    }

    protected virtual void ResetValues()
    {
        Value = null;
    }
}
