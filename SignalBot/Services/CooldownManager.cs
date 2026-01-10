using SignalBot.Configuration;
using SignalBot.Models;
using Serilog;

namespace SignalBot.Services;

/// <summary>
/// Управляет cooldown периодами после убытков для предотвращения revenge trading
/// </summary>
public class CooldownManager
{
    private readonly CooldownSettings _settings;
    private readonly ILogger _logger;

    private int _consecutiveLosses = 0;
    private int _consecutiveWins = 0;
    private DateTime? _cooldownUntil = null;
    private string? _cooldownReason = null;
    private readonly object _lock = new();

    public CooldownManager(CooldownSettings settings, ILogger? logger = null)
    {
        _settings = settings;
        _logger = logger ?? Log.ForContext<CooldownManager>();
    }

    /// <summary>
    /// Бот в режиме cooldown
    /// </summary>
    public bool IsInCooldown
    {
        get
        {
            lock (_lock)
            {
                return _cooldownUntil.HasValue && DateTime.UtcNow < _cooldownUntil.Value;
            }
        }
    }

    /// <summary>
    /// Оставшееся время cooldown
    /// </summary>
    public TimeSpan? RemainingCooldown
    {
        get
        {
            lock (_lock)
            {
                return IsInCooldown ? _cooldownUntil!.Value - DateTime.UtcNow : null;
            }
        }
    }

    /// <summary>
    /// Получить текущий статус cooldown
    /// </summary>
    public CooldownStatus GetStatus()
    {
        lock (_lock)
        {
            return new CooldownStatus
            {
                IsInCooldown = IsInCooldown,
                CooldownUntil = _cooldownUntil,
                RemainingTime = RemainingCooldown,
                Reason = _cooldownReason,
                ConsecutiveLosses = _consecutiveLosses,
                CurrentSizeMultiplier = GetCurrentSizeMultiplier()
            };
        }
    }

    /// <summary>
    /// Обработать закрытие позиции
    /// </summary>
    public void OnPositionClosed(SignalPosition position)
    {
        if (!_settings.Enabled) return;

        switch (position.CloseReason)
        {
            case PositionCloseReason.StopLossHit:
                HandleStopLoss();
                break;

            case PositionCloseReason.Liquidation:
                HandleLiquidation();
                break;

            case PositionCloseReason.AllTargetsHit:
                HandleWin();
                break;

            // PartialClose, ManualClose, Error - не влияют на cooldown
        }
    }

    private void HandleStopLoss()
    {
        lock (_lock)
        {
            _consecutiveLosses++;
            _consecutiveWins = 0;

            TimeSpan cooldown = _consecutiveLosses >= _settings.ConsecutiveLossesForLongCooldown
                ? _settings.LongCooldownDuration
                : _settings.CooldownAfterStopLoss;

            SetCooldown(cooldown, $"Stop loss #{_consecutiveLosses}");

            _logger.Warning(
                "Cooldown activated: {Duration} after {Losses} consecutive losses",
                cooldown, _consecutiveLosses);
        }
    }

    private void HandleLiquidation()
    {
        lock (_lock)
        {
            _consecutiveLosses++;
            _consecutiveWins = 0;

            SetCooldown(_settings.CooldownAfterLiquidation, "Liquidation");

            _logger.Error("Cooldown activated after LIQUIDATION: {Duration}",
                _settings.CooldownAfterLiquidation);
        }
    }

    private void HandleWin()
    {
        lock (_lock)
        {
            _consecutiveWins++;

            if (_consecutiveWins >= _settings.WinsToResetLossCounter)
            {
                _consecutiveLosses = 0;
                _consecutiveWins = 0;
                _logger.Information("Loss counter reset after {Wins} consecutive wins",
                    _settings.WinsToResetLossCounter);
            }
        }
    }

    private void SetCooldown(TimeSpan duration, string reason)
    {
        _cooldownUntil = DateTime.UtcNow + duration;
        _cooldownReason = reason;
    }

    /// <summary>
    /// Получить текущий множитель размера позиции на основе убытков
    /// </summary>
    public decimal GetCurrentSizeMultiplier()
    {
        lock (_lock)
        {
            if (!_settings.ReduceSizeAfterLosses) return 1.0m;

            return _consecutiveLosses switch
            {
                0 => 1.0m,
                1 => _settings.SizeMultiplierAfter1Loss,
                2 => _settings.SizeMultiplierAfter2Losses,
                _ => _settings.SizeMultiplierAfter3PlusLosses
            };
        }
    }

    /// <summary>
    /// Принудительно сбросить cooldown (для manual override)
    /// </summary>
    public void ForceResetCooldown()
    {
        lock (_lock)
        {
            _cooldownUntil = null;
            _cooldownReason = null;
            _logger.Warning("Cooldown manually reset");
        }
    }

    /// <summary>
    /// Принудительно сбросить счётчик убытков
    /// </summary>
    public void ForceResetLossCounter()
    {
        lock (_lock)
        {
            _consecutiveLosses = 0;
            _consecutiveWins = 0;
            _logger.Warning("Loss counter manually reset");
        }
    }
}
