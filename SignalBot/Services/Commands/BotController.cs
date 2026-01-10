using SignalBot.Models;
using Serilog;

namespace SignalBot.Services.Commands;

/// <summary>
/// Controls bot operating mode and state
/// </summary>
public class BotController
{
    private readonly ILogger _logger;
    private BotOperatingMode _currentMode = BotOperatingMode.Automatic;
    private readonly object _lock = new();

    public BotOperatingMode CurrentMode
    {
        get
        {
            lock (_lock)
            {
                return _currentMode;
            }
        }
    }

    public event EventHandler<BotOperatingMode>? OnModeChanged;

    public BotController(ILogger? logger = null)
    {
        _logger = logger ?? Log.ForContext<BotController>();
    }

    public void SetMode(BotOperatingMode mode)
    {
        lock (_lock)
        {
            var previousMode = _currentMode;
            _currentMode = mode;

            _logger.Information("Bot mode changed: {Previous} → {Current}", previousMode, mode);

            OnModeChanged?.Invoke(this, mode);
        }
    }

    public bool CanAcceptNewSignals()
    {
        return CurrentMode == BotOperatingMode.Automatic;
    }

    public bool CanManagePositions()
    {
        return CurrentMode == BotOperatingMode.Automatic ||
               CurrentMode == BotOperatingMode.MonitorOnly;
    }

    public bool IsRunning()
    {
        return CurrentMode != BotOperatingMode.EmergencyStop;
    }
}
