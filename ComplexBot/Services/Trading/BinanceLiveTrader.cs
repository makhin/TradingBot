using Binance.Net.Clients;
using Binance.Net.Enums;
using Binance.Net.Objects.Models.Spot.Socket;
using Binance.Net.Interfaces;
using CryptoExchange.Net.Authentication;
using CryptoExchange.Net.Sockets;
using ComplexBot.Models;
using ComplexBot.Services.Strategies;
using ComplexBot.Services.RiskManagement;
using CryptoExchange.Net.Objects.Sockets;

namespace ComplexBot.Services.Trading;

public class BinanceLiveTrader : IDisposable
{
    private readonly BinanceRestClient _restClient;
    private readonly BinanceSocketClient _socketClient;
    private readonly IStrategy _strategy;
    private readonly RiskManager _riskManager;
    private readonly LiveTraderSettings _settings;
    private readonly List<Candle> _candleBuffer = new();
    
    private decimal _currentPosition;
    private decimal? _entryPrice;
    private decimal? _stopLoss;
    private bool _isRunning;
    private UpdateSubscription? _subscription;

    public event Action<string>? OnLog;
    public event Action<TradeSignal>? OnSignal;
    public event Action<Trade>? OnTrade;
    public event Action<decimal>? OnEquityUpdate;

    public BinanceLiveTrader(
        string apiKey,
        string apiSecret,
        IStrategy strategy,
        RiskSettings riskSettings,
        LiveTraderSettings? settings = null)
    {
        _settings = settings ?? new LiveTraderSettings();
        _strategy = strategy;
        _riskManager = new RiskManager(riskSettings, _settings.InitialCapital);

        _restClient = new BinanceRestClient(options =>
        {
            options.ApiCredentials = new ApiCredentials(apiKey, apiSecret);
            if (_settings.UseTestnet)
            {
                options.Environment = Binance.Net.BinanceEnvironment.Testnet;
            }
        });

        _socketClient = new BinanceSocketClient(options =>
        {
            options.ApiCredentials = new ApiCredentials(apiKey, apiSecret);
            if (_settings.UseTestnet)
            {
                options.Environment = Binance.Net.BinanceEnvironment.Testnet;
            }
        });
    }

    public async Task<decimal> GetAccountBalanceAsync(string asset = "USDT")
    {
        var result = await _restClient.SpotApi.Account.GetAccountInfoAsync();
        if (!result.Success)
            throw new Exception($"Failed to get balance: {result.Error?.Message}");

        var balance = result.Data.Balances.FirstOrDefault(b => b.Asset == asset);
        return balance?.Available ?? 0;
    }

    public async Task<decimal> GetCurrentPriceAsync()
    {
        var result = await _restClient.SpotApi.ExchangeData.GetPriceAsync(_settings.Symbol);
        if (!result.Success)
            throw new Exception($"Failed to get price: {result.Error?.Message}");
        
        return result.Data.Price;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        _isRunning = true;
        Log($"Starting {(_settings.PaperTrade ? "PAPER" : "LIVE")} trading on {_settings.Symbol}");
        Log($"Testnet: {_settings.UseTestnet}");
        
        // Get initial balance
        var balance = await GetAccountBalanceAsync();
        Log($"USDT Balance: {balance:F2}");
        _riskManager.UpdateEquity(balance);

        // Load historical candles for indicator warmup
        await WarmupIndicatorsAsync();

        // Subscribe to kline updates
        var subscribeResult = await _socketClient.SpotApi.ExchangeData.SubscribeToKlineUpdatesAsync(
            _settings.Symbol,
            _settings.Interval,
            async data => await OnKlineUpdateAsync(data.Data)
        );

        if (!subscribeResult.Success)
        {
            Log($"Failed to subscribe: {subscribeResult.Error?.Message}");
            return;
        }

        _subscription = subscribeResult.Data;
        Log("Subscribed to kline updates. Waiting for signals...");

        // Keep running until cancelled
        try
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            Log("Stopping trader...");
        }
    }

    public async Task StopAsync()
    {
        _isRunning = false;
        
        if (_subscription != null)
        {
            await _subscription.CloseAsync();
            _subscription = null;
        }

        // Close any open position
        if (_currentPosition != 0)
        {
            Log("Closing open position...");
            await ClosePositionAsync("Manual stop");
        }

        Log("Trader stopped.");
    }

    private async Task WarmupIndicatorsAsync()
    {
        Log("Loading historical data for indicator warmup...");
        
        var klines = await _restClient.SpotApi.ExchangeData.GetKlinesAsync(
            _settings.Symbol,
            _settings.Interval,
            limit: _settings.WarmupCandles
        );

        if (!klines.Success)
        {
            Log($"Warning: Failed to load historical data: {klines.Error?.Message}");
            return;
        }

        foreach (var kline in klines.Data)
        {
            var candle = new Candle(
                kline.OpenTime,
                kline.OpenPrice,
                kline.HighPrice,
                kline.LowPrice,
                kline.ClosePrice,
                kline.Volume,
                kline.CloseTime
            );
            
            _candleBuffer.Add(candle);
            _strategy.Analyze(candle, _currentPosition);
        }

        Log($"Warmed up with {klines.Data.Count()} candles");
    }

    private async Task OnKlineUpdateAsync(IBinanceStreamKlineData data)
    {
        if (!_isRunning) return;

        var kline = data.Data;
        
        // Only process closed candles for the main strategy
        if (!kline.Final) return;

        var candle = new Candle(
            kline.OpenTime,
            kline.OpenPrice,
            kline.HighPrice,
            kline.LowPrice,
            kline.ClosePrice,
            kline.Volume,
            kline.CloseTime
        );

        _candleBuffer.Add(candle);
        
        // Keep buffer size manageable
        while (_candleBuffer.Count > _settings.WarmupCandles * 2)
            _candleBuffer.RemoveAt(0);

        // Check stop loss on current candle
        await CheckStopLossAsync(candle);

        // Analyze for signals
        var signal = _strategy.Analyze(candle, _currentPosition);

        if (signal != null)
        {
            Log($"Signal: {signal.Type} at {signal.Price:F2} - {signal.Reason}");
            OnSignal?.Invoke(signal);
            
            await ProcessSignalAsync(signal, candle);
        }
    }

    private async Task CheckStopLossAsync(Candle candle)
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

    private async Task ProcessSignalAsync(TradeSignal signal, Candle candle)
    {
        switch (signal.Type)
        {
            case SignalType.Buy when _currentPosition <= 0:
                if (_currentPosition < 0)
                    await ClosePositionAsync("Signal Reversal");
                
                if (_riskManager.CanOpenPosition() && signal.StopLoss.HasValue)
                {
                    var sizing = _riskManager.CalculatePositionSize(
                        candle.Close,
                        signal.StopLoss.Value,
                        (_strategy as AdxTrendStrategy)?.CurrentAtr
                    );

                    if (sizing.Quantity > 0)
                        await OpenPositionAsync(TradeDirection.Long, sizing.Quantity, candle.Close, signal.StopLoss.Value);
                }
                break;

            case SignalType.Sell when _currentPosition >= 0:
                if (_currentPosition > 0)
                    await ClosePositionAsync("Signal Reversal");
                
                if (_riskManager.CanOpenPosition() && signal.StopLoss.HasValue)
                {
                    var sizing = _riskManager.CalculatePositionSize(
                        candle.Close,
                        signal.StopLoss.Value,
                        (_strategy as AdxTrendStrategy)?.CurrentAtr
                    );

                    if (sizing.Quantity > 0)
                        await OpenPositionAsync(TradeDirection.Short, sizing.Quantity, candle.Close, signal.StopLoss.Value);
                }
                break;

            case SignalType.Exit when _currentPosition != 0:
                await ClosePositionAsync(signal.Reason);
                break;
        }
    }

    private async Task OpenPositionAsync(TradeDirection direction, decimal quantity, decimal price, decimal stopLoss)
    {
        // Round quantity to valid precision
        quantity = Math.Round(quantity, 5);
        
        if (quantity * price < 10) // Binance minimum order
        {
            Log($"Position too small: {quantity * price:F2} USDT");
            return;
        }

        if (_settings.PaperTrade)
        {
            // Paper trade - just track position
            _currentPosition = direction == TradeDirection.Long ? quantity : -quantity;
            _entryPrice = price;
            _stopLoss = stopLoss;
            
            Log($"[PAPER] Opened {direction} {quantity:F5} @ {price:F2}, SL: {stopLoss:F2}");
        }
        else
        {
            // Real trade
            try
            {
                var side = direction == TradeDirection.Long ? OrderSide.Buy : OrderSide.Sell;
                
                var result = await _restClient.SpotApi.Trading.PlaceOrderAsync(
                    _settings.Symbol,
                    side,
                    SpotOrderType.Market,
                    quantity
                );

                if (!result.Success)
                {
                    Log($"Order failed: {result.Error?.Message}");
                    return;
                }

                _currentPosition = direction == TradeDirection.Long ? quantity : -quantity;
                _entryPrice = result.Data.AverageFillPrice ?? price;
                _stopLoss = stopLoss;
                
                _riskManager.AddPosition(_settings.Symbol, Math.Abs(_currentPosition), 
                    Math.Abs(price - stopLoss) * quantity, price, stopLoss);

                Log($"Opened {direction} {quantity:F5} @ {_entryPrice:F2}, SL: {stopLoss:F2}");
            }
            catch (Exception ex)
            {
                Log($"Order error: {ex.Message}");
            }
        }
    }

    private async Task ClosePositionAsync(string reason)
    {
        if (_currentPosition == 0) return;

        var currentPrice = await GetCurrentPriceAsync();
        var direction = _currentPosition > 0 ? TradeDirection.Long : TradeDirection.Short;
        var quantity = Math.Abs(_currentPosition);
        
        decimal pnl = direction == TradeDirection.Long
            ? (currentPrice - _entryPrice!.Value) * quantity
            : (_entryPrice!.Value - currentPrice) * quantity;

        if (_settings.PaperTrade)
        {
            Log($"[PAPER] Closed {direction} {quantity:F5} @ {currentPrice:F2}, PnL: {pnl:F2} USDT - {reason}");
        }
        else
        {
            try
            {
                var side = direction == TradeDirection.Long ? OrderSide.Sell : OrderSide.Buy;
                
                var result = await _restClient.SpotApi.Trading.PlaceOrderAsync(
                    _settings.Symbol,
                    side,
                    SpotOrderType.Market,
                    quantity
                );

                if (!result.Success)
                {
                    Log($"Close order failed: {result.Error?.Message}");
                    return;
                }

                var fillPrice = result.Data.AverageFillPrice ?? currentPrice;
                pnl = direction == TradeDirection.Long
                    ? (fillPrice - _entryPrice!.Value) * quantity
                    : (_entryPrice!.Value - fillPrice) * quantity;

                Log($"Closed {direction} {quantity:F5} @ {fillPrice:F2}, PnL: {pnl:F2} USDT - {reason}");
            }
            catch (Exception ex)
            {
                Log($"Close error: {ex.Message}");
                return;
            }
        }

        // Update equity
        var balance = _settings.PaperTrade 
            ? _riskManager.GetDrawdownAdjustedRisk() + pnl // Simplified for paper
            : await GetAccountBalanceAsync();
        _riskManager.UpdateEquity(balance);
        OnEquityUpdate?.Invoke(balance);

        // Reset position
        _currentPosition = 0;
        _entryPrice = null;
        _stopLoss = null;
        _riskManager.ClearPositions();
    }

    private void Log(string message)
    {
        var timestamp = DateTime.UtcNow.ToString("HH:mm:ss");
        var formatted = $"[{timestamp}] {message}";
        OnLog?.Invoke(formatted);
        Console.WriteLine(formatted);
    }

    public void Dispose()
    {
        _subscription?.CloseAsync().Wait();
        _restClient.Dispose();
        _socketClient.Dispose();
    }
}

public record LiveTraderSettings
{
    public string Symbol { get; init; } = "BTCUSDT";
    public KlineInterval Interval { get; init; } = KlineInterval.FourHour;
    public decimal InitialCapital { get; init; } = 10000m;
    public bool UseTestnet { get; init; } = true;
    public bool PaperTrade { get; init; } = true;  // Paper trade by default for safety
    public int WarmupCandles { get; init; } = 100;
}
