namespace SignalBot.Configuration;

/// <summary>
/// Настройки системы cooldown для предотвращения revenge trading
/// </summary>
public record CooldownSettings
{
    /// <summary>
    /// Включить систему cooldown
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Пауза после каждого стоп-лосса
    /// </summary>
    public TimeSpan CooldownAfterStopLoss { get; init; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Пауза после ликвидации (если произошла)
    /// </summary>
    public TimeSpan CooldownAfterLiquidation { get; init; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Количество убытков подряд для длинного cooldown
    /// </summary>
    public int ConsecutiveLossesForLongCooldown { get; init; } = 3;

    /// <summary>
    /// Длинный cooldown после серии убытков
    /// </summary>
    public TimeSpan LongCooldownDuration { get; init; } = TimeSpan.FromHours(2);

    /// <summary>
    /// Уменьшить размер позиции после убытков
    /// </summary>
    public bool ReduceSizeAfterLosses { get; init; } = true;

    /// <summary>
    /// Множитель размера после 1 убытка
    /// </summary>
    public decimal SizeMultiplierAfter1Loss { get; init; } = 0.75m;

    /// <summary>
    /// Множитель размера после 2 убытков подряд
    /// </summary>
    public decimal SizeMultiplierAfter2Losses { get; init; } = 0.5m;

    /// <summary>
    /// Множитель размера после 3+ убытков подряд
    /// </summary>
    public decimal SizeMultiplierAfter3PlusLosses { get; init; } = 0.25m;

    /// <summary>
    /// Сколько прибыльных трейдов нужно для сброса счётчика убытков
    /// </summary>
    public int WinsToResetLossCounter { get; init; } = 2;
}
