using ComplexBot.Models;
using ComplexBot.Services.Strategies;
using ComplexBot.Services.RiskManagement;
using ComplexBot.Services.Notifications;

namespace ComplexBot.Services.Trading;

/// <summary>
/// Base class for symbol traders with common position tracking and risk management.
/// Extracts shared logic from exchange-specific implementations.
/// </summary>
/// <typeparam name="TSettings">Settings type specific to the trader implementation</typeparam>
public abstract class SymbolTraderBase<TSettings> : ISymbolTrader
    where TSettings : class, new()
{
    // Configuration
    protected readonly TSettings Settings;
    protected readonly IStrategy Strategy;
    protected readonly RiskManager RiskManager;
    protected readonly TelegramNotifier? Telegram;

    // Portfolio integration (optional - for multi-pair trading)
    protected readonly PortfolioRiskManager? PortfolioRiskManager;
    protected readonly SharedEquityManager? SharedEquityManager;

    // Position state
    protected decimal _currentPosition;
    protected decimal? _entryPrice;
    protected decimal? _stopLoss;
    protected decimal? _takeProfit;
    protected bool _isRunning;

    // Buffer for candles
    protected readonly List<Candle> CandleBuffer = new();

    // Events
    public event Action<string>? OnLog;
    public event Action<TradeSignal>? OnSignal;
    public event Action<Trade>? OnTrade;
    public event Action<decimal>? OnEquityUpdate;

    // Properties (implementing ISymbolTrader)
    public abstract string Symbol { get; }
    public abstract string Exchange { get; }
    public decimal CurrentPosition => _currentPosition;
    public decimal? EntryPrice => _entryPrice;
    public abstract decimal CurrentEquity { get; }
    public bool IsRunning => _isRunning;
    public StrategyState GetStrategyState() => Strategy.GetCurrentState();

    protected SymbolTraderBase(
        IStrategy strategy,
        RiskSettings riskSettings,
        TSettings settings,
        TelegramNotifier? telegram = null,
        PortfolioRiskManager? portfolioRiskManager = null,
        SharedEquityManager? sharedEquityManager = null)
    {
        Strategy = strategy;
        Settings = settings;
        Telegram = telegram;
        PortfolioRiskManager = portfolioRiskManager;
        SharedEquityManager = sharedEquityManager;
        RiskManager = new RiskManager(riskSettings, GetInitialCapital());
    }

    // Abstract methods - exchange-specific implementation required
    protected abstract decimal GetInitialCapital();
    protected abstract Task<decimal> GetCurrentPriceAsync();
    protected abstract Task<decimal> GetAccountBalanceAsync(string asset = "USDT");
    protected abstract Task SubscribeToKlineUpdatesAsync(CancellationToken ct);
    protected abstract Task WarmupIndicatorsAsync();

    // Template method for lifecycle - can be overridden if needed
    public virtual async Task StartAsync(CancellationToken cancellationToken = default)
    {
        _isRunning = true;
        Log($"Starting trader on {Symbol}");

        var balance = await GetAccountBalanceAsync();
        UpdateEquity(balance);

        await WarmupIndicatorsAsync();
        await SubscribeToKlineUpdatesAsync(cancellationToken);
    }

    public abstract Task StopAsync();
    public abstract Task ClosePositionAsync(string reason);
    public abstract void Dispose();

    /// <summary>
    /// Processes a new candle - shared logic for all traders.
    /// Updates indicators, checks SL/TP, analyzes for signals.
    /// </summary>
    protected async Task ProcessCandleAsync(Candle candle)
    {
        if (!_isRunning) return;

        CandleBuffer.Add(candle);
        TrimCandleBuffer();

        // Update position price for unrealized P&L tracking
        if (_currentPosition != 0)
        {
            RiskManager.UpdatePositionPrice(Symbol, candle.Close);
        }

        // Analyze for signals
        var signal = Strategy.Analyze(candle, _currentPosition, Symbol);

        if (signal != null)
        {
            Log($"Signal: {signal.Type} at {signal.Price:F2} - {signal.Reason}");
            OnSignal?.Invoke(signal);
            await ProcessSignalAsync(signal, candle);
        }
    }

    /// <summary>
    /// Portfolio risk check - verifies both symbol-level and portfolio-level limits.
    /// </summary>
    protected bool CanOpenPositionWithPortfolioCheck()
    {
        // Symbol-level check
        if (!RiskManager.CanOpenPosition())
        {
            Log("Symbol risk limit reached");
            return false;
        }

        // Portfolio-level check (if connected to multi-pair trading)
        if (PortfolioRiskManager != null && !PortfolioRiskManager.CanOpenPosition(Symbol))
        {
            Log($"Portfolio risk limit reached for {Symbol}");
            return false;
        }

        return true;
    }

    /// <summary>
    /// Updates equity in both symbol-level and portfolio-level trackers.
    /// </summary>
    protected void UpdateEquity(decimal equity)
    {
        RiskManager.UpdateEquity(equity);
        SharedEquityManager?.UpdateSymbolEquity(Symbol, equity);
        OnEquityUpdate?.Invoke(equity);
    }

    /// <summary>
    /// Centralized logging with timestamp and symbol prefix.
    /// </summary>
    protected void Log(string message)
    {
        var formatted = $"[{DateTime.UtcNow:HH:mm:ss}] [{Symbol}] {message}";
        OnLog?.Invoke(formatted);
        Console.WriteLine(formatted);
    }

    /// <summary>
    /// Signal processing - must be implemented by derived class.
    /// Different exchanges have different order APIs.
    /// </summary>
    protected abstract Task ProcessSignalAsync(TradeSignal signal, Candle candle);

    /// <summary>
    /// Trims candle buffer to prevent unbounded growth.
    /// </summary>
    protected virtual void TrimCandleBuffer(int maxSize = 200)
    {
        while (CandleBuffer.Count > maxSize)
            CandleBuffer.RemoveAt(0);
    }

    /// <summary>
    /// Checks if stop loss was hit on current candle (for paper trading).
    /// </summary>
    protected async Task CheckStopLossAsync(Candle candle)
    {
        if (_currentPosition == 0 || !_stopLoss.HasValue) return;

        bool stopHit = _currentPosition > 0
            ? candle.Low <= _stopLoss.Value
            : candle.High >= _stopLoss.Value;

        if (stopHit)
        {
            Log($"Stop loss triggered at {_stopLoss:F2}");
            await ClosePositionAsync("Stop Loss");
        }
    }

    /// <summary>
    /// Checks if take profit was hit on current candle (for paper trading).
    /// </summary>
    protected async Task CheckTakeProfitAsync(Candle candle)
    {
        if (_currentPosition == 0 || !_takeProfit.HasValue) return;

        bool takeProfitHit = _currentPosition > 0
            ? candle.High >= _takeProfit.Value
            : candle.Low <= _takeProfit.Value;

        if (takeProfitHit)
        {
            Log($"Take profit triggered at {_takeProfit:F2}");
            await ClosePositionAsync("Take Profit");
        }
    }

    /// <summary>
    /// Records a completed trade to the shared equity manager (for multi-pair tracking).
    /// </summary>
    protected void RecordTrade(decimal realizedPnL, Trade trade)
    {
        SharedEquityManager?.RecordTradePnL(Symbol, realizedPnL);
        OnTrade?.Invoke(trade);
    }
}

/// <summary>
/// Result of an order execution (shared across all exchanges).
/// </summary>
public record OrderResult(bool Success, decimal FilledQuantity, decimal AveragePrice, string? ErrorMessage);
