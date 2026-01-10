namespace SignalBot.Models;

/// <summary>
/// Статус системы cooldown
/// </summary>
public record CooldownStatus
{
    /// <summary>
    /// Бот находится в режиме cooldown (пауза после убытков)
    /// </summary>
    public bool IsInCooldown { get; init; }

    /// <summary>
    /// Когда закончится cooldown
    /// </summary>
    public DateTime? CooldownUntil { get; init; }

    /// <summary>
    /// Оставшееся время cooldown
    /// </summary>
    public TimeSpan? RemainingTime { get; init; }

    /// <summary>
    /// Причина cooldown
    /// </summary>
    public string? Reason { get; init; }

    /// <summary>
    /// Количество убытков подряд
    /// </summary>
    public int ConsecutiveLosses { get; init; }

    /// <summary>
    /// Текущий множитель размера позиции (1.0 = нормальный, 0.5 = половина)
    /// </summary>
    public decimal CurrentSizeMultiplier { get; init; }
}
