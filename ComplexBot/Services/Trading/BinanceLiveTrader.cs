using Binance.Net.Clients;
using Binance.Net.Enums;
using Binance.Net.Objects.Models.Spot.Socket;
using Binance.Net.Interfaces;
using CryptoExchange.Net.Authentication;
using CryptoExchange.Net.Sockets;
using ComplexBot.Models;
using ComplexBot.Services.Strategies;
using ComplexBot.Services.RiskManagement;
using ComplexBot.Services.Notifications;
using CryptoExchange.Net.Objects.Sockets;

namespace ComplexBot.Services.Trading;

public class BinanceLiveTrader : IDisposable
{
    private readonly BinanceRestClient _restClient;
    private readonly BinanceSocketClient _socketClient;
    private readonly IStrategy _strategy;
    private readonly RiskManager _riskManager;
    private readonly LiveTraderSettings _settings;
    private readonly TelegramNotifier? _telegram;
    private readonly ExecutionValidator _executionValidator;
    private readonly List<Candle> _candleBuffer = new();
    
    private decimal _currentPosition;
    private decimal? _entryPrice;
    private decimal? _stopLoss;
    private decimal? _takeProfit;
    private decimal _paperEquity;
    private bool _isRunning;
    private UpdateSubscription? _subscription;
    private long? _currentOcoOrderListId;

    public event Action<string>? OnLog;
    public event Action<TradeSignal>? OnSignal;
    public event Action<Trade>? OnTrade;
    public event Action<decimal>? OnEquityUpdate;

    public BinanceLiveTrader(
        string apiKey,
        string apiSecret,
        IStrategy strategy,
        RiskSettings riskSettings,
        LiveTraderSettings? settings = null,
        TelegramNotifier? telegram = null)
    {
        _settings = settings ?? new LiveTraderSettings();
        _strategy = strategy;
        _riskManager = new RiskManager(riskSettings, _settings.InitialCapital);
        _telegram = telegram;
        _executionValidator = new ExecutionValidator(maxSlippagePercent: 0.5m);

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

    private async Task<bool> PlaceOcoOrderAsync(
        OrderSide side,
        decimal quantity,
        decimal stopLossPrice,
        decimal stopLossLimitPrice,
        decimal takeProfitPrice)
    {
        if (_settings.PaperTrade)
        {
            // Paper trading - just log, no actual OCO order
            Log($"[PAPER] OCO would be placed: TP={takeProfitPrice:F2}, SL={stopLossPrice:F2}");
            return true;
        }

        try
        {
            // OCO ордер: стоп-лосс + тейк-профит, один отменяет другой
            var result = await _restClient.SpotApi.Trading.PlaceOcoOrderAsync(
                symbol: _settings.Symbol,
                side: side,
                quantity: quantity,
                price: takeProfitPrice,                // Limit order (take profit)
                stopPrice: stopLossPrice,              // Stop trigger price
                stopLimitPrice: stopLossLimitPrice,    // Stop limit price
                stopLimitTimeInForce: TimeInForce.GoodTillCanceled
            );

            if (result.Success)
            {
                _currentOcoOrderListId = result.Data.Id;
                Log($"✅ OCO Order placed:");
                Log($"   Take Profit: {takeProfitPrice:F2}");
                Log($"   Stop Loss: {stopLossPrice:F2} (limit: {stopLossLimitPrice:F2})");
                Log($"   Order List ID: {result.Data.Id}");
                return true;
            }
            else
            {
                Log($"❌ OCO Order failed: {result.Error?.Message}");
                return false;
            }
        }
        catch (Exception ex)
        {
            Log($"❌ OCO Order exception: {ex.Message}");
            return false;
        }
    }

    private async Task<bool> CancelOcoOrderAsync()
    {
        if (!_currentOcoOrderListId.HasValue || _settings.PaperTrade)
            return true;

        try
        {
            var result = await _restClient.SpotApi.Trading.CancelOcoOrderAsync(
                _settings.Symbol,
                orderListId: _currentOcoOrderListId.Value
            );

            if (result.Success)
            {
                Log($"✅ OCO Order cancelled (ID: {_currentOcoOrderListId})");
                _currentOcoOrderListId = null;
                return true;
            }
            else
            {
                Log($"❌ OCO Cancel failed: {result.Error?.Message}");
                return false;
            }
        }
        catch (Exception ex)
        {
            Log($"❌ OCO Cancel exception: {ex.Message}");
            return false;
        }
    }

    public async Task UpdateTrailingStopAsync(decimal newStopPrice, decimal takeProfitPrice)
    {
        if (_currentPosition == 0 || _settings.PaperTrade)
        {
            if (_settings.PaperTrade)
            {
                _stopLoss = newStopPrice;
                Log($"[PAPER] Trailing stop updated to {newStopPrice:F2}");
            }
            return;
        }

        // 1. Отменить существующий OCO
        await CancelOcoOrderAsync();

        // 2. Создать новый с обновлённым стопом
        var quantity = Math.Abs(_currentPosition);
        var side = _currentPosition > 0 ? OrderSide.Sell : OrderSide.Buy;

        // Stop limit price должен быть чуть ниже/выше stop price для защиты от проскальзывания
        var stopLimitPrice = _currentPosition > 0
            ? newStopPrice * 0.995m  // 0.5% ниже для лонга
            : newStopPrice * 1.005m; // 0.5% выше для шорта

        await PlaceOcoOrderAsync(side, quantity, newStopPrice, stopLimitPrice, takeProfitPrice);

        _stopLoss = newStopPrice;
        Log($"🔄 Trailing stop updated to {newStopPrice:F2}");
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_settings.TradingMode == TradingMode.Futures)
        {
            Log("Futures/margin trading is not supported by BinanceLiveTrader. Please select spot mode.");
            throw new NotSupportedException("BinanceLiveTrader supports spot trading only.");
        }

        _isRunning = true;
        Log($"Starting {(_settings.PaperTrade ? "PAPER" : "LIVE")} trading on {_settings.Symbol}");
        Log($"Trading mode: {_settings.TradingMode}");
        Log($"Testnet: {_settings.UseTestnet}");
        
        // Get initial balance
        var balance = await GetAccountBalanceAsync();
        _paperEquity = _settings.PaperTrade ? _settings.InitialCapital : balance;
        Log($"USDT Balance: {balance:F2}");
        _riskManager.UpdateEquity(_settings.PaperTrade ? _paperEquity : balance);
        OnEquityUpdate?.Invoke(_settings.PaperTrade ? _paperEquity : balance);

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

        // Cancel OCO orders and close any open position
        if (_currentPosition != 0)
        {
            Log("Closing open position...");
            await ClosePositionAsync("Manual stop");
        }
        else
        {
            // Cancel OCO even if position was closed by exchange
            await CancelOcoOrderAsync();
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
            _strategy.Analyze(candle, _currentPosition, _settings.Symbol);
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

        // Update position price for unrealized P&L tracking
        if (_currentPosition != 0)
        {
            _riskManager.UpdatePositionPrice(_settings.Symbol, candle.Close);
        }

        // Check stop loss/take profit on current candle
        await CheckTakeProfitAsync(candle);
        await CheckStopLossAsync(candle);

        // Analyze for signals
        var signal = _strategy.Analyze(candle, _currentPosition, _settings.Symbol);

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

    private async Task CheckTakeProfitAsync(Candle candle)
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
                        await OpenPositionAsync(
                            TradeDirection.Long,
                            sizing.Quantity,
                            candle.Close,
                            signal.StopLoss.Value,
                            signal.TakeProfit);
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
                        await OpenPositionAsync(
                            TradeDirection.Short,
                            sizing.Quantity,
                            candle.Close,
                            signal.StopLoss.Value,
                            signal.TakeProfit);
                }
                break;

            case SignalType.Exit when _currentPosition != 0:
                await ClosePositionAsync(signal.Reason);
                break;
            case SignalType.PartialExit when _currentPosition != 0:
                await ClosePartialPositionAsync(signal);
                break;
        }
    }

    private async Task OpenPositionAsync(
        TradeDirection direction,
        decimal quantity,
        decimal price,
        decimal stopLoss,
        decimal? takeProfit)
    {
        if (_settings.TradingMode == TradingMode.Spot && direction == TradeDirection.Short)
        {
            Log("Short positions are not allowed in spot mode without margin.");
            return;
        }

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
            _takeProfit = takeProfit;
            _riskManager.AddPosition(
                _settings.Symbol,
                direction == TradeDirection.Long ? SignalType.Buy : SignalType.Sell,
                Math.Abs(_currentPosition),
                Math.Abs(price - stopLoss) * quantity,
                price,
                stopLoss,
                price);

            var takeProfitText = takeProfit.HasValue ? $", TP: {takeProfit:F2}" : string.Empty;
            Log($"[PAPER] Opened {direction} {quantity:F5} @ {price:F2}, SL: {stopLoss:F2}{takeProfitText}");
        }
        else
        {
            // Real trade
            try
            {
                var side = direction == TradeDirection.Long ? OrderSide.Buy : OrderSide.Sell;

                // 1. Открыть позицию маркет ордером
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

                // Validate execution slippage
                var actualPrice = result.Data.AverageFillPrice ?? price;
                var validation = _executionValidator.ValidateExecution(price, actualPrice, side);
                var slippageDesc = _executionValidator.GetSlippageDescription(validation, side);
                Log(slippageDesc);

                if (!validation.IsAcceptable)
                {
                    Log($"⚠️ WARNING: {validation.RejectReason}");
                    Log($"   Expected: {validation.ExpectedPrice:F2}, Actual: {validation.ActualPrice:F2}");
                }

                _currentPosition = direction == TradeDirection.Long ? quantity : -quantity;
                _entryPrice = actualPrice;
                _stopLoss = stopLoss;
                _takeProfit = takeProfit;

                _riskManager.AddPosition(
                    _settings.Symbol,
                    direction == TradeDirection.Long ? SignalType.Buy : SignalType.Sell,
                    Math.Abs(_currentPosition),
                    Math.Abs(price - stopLoss) * quantity,
                    price,
                    stopLoss,
                    price);

                var takeProfitText = takeProfit.HasValue ? $", TP: {takeProfit:F2}" : string.Empty;
                Log($"Opened {direction} {quantity:F5} @ {_entryPrice:F2}, SL: {stopLoss:F2}{takeProfitText}");

                // 2. Сразу выставить OCO для защиты (если есть тейк-профит)
                if (takeProfit.HasValue)
                {
                    var exitSide = direction == TradeDirection.Long ? OrderSide.Sell : OrderSide.Buy;
                    var stopLimitPrice = direction == TradeDirection.Long
                        ? stopLoss * 0.995m  // 0.5% ниже для лонга
                        : stopLoss * 1.005m; // 0.5% выше для шорта

                    await PlaceOcoOrderAsync(exitSide, quantity, stopLoss, stopLimitPrice, takeProfit.Value);
                }

                // 3. Отправить Telegram уведомление
                if (_telegram != null)
                {
                    var riskAmt = Math.Abs(price - stopLoss) * quantity;
                    var signal = new TradeSignal(
                        _settings.Symbol,
                        direction == TradeDirection.Long ? SignalType.Buy : SignalType.Sell,
                        price,
                        stopLoss,
                        takeProfit,
                        $"{direction} position opened"
                    );
                    await _telegram.SendTradeOpen(signal, quantity, riskAmt);
                }
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

        // Отменить OCO, если был выставлен
        await CancelOcoOrderAsync();

        var currentPrice = await GetCurrentPriceAsync();
        var direction = _currentPosition > 0 ? TradeDirection.Long : TradeDirection.Short;
        var quantity = Math.Abs(_currentPosition);
        
        decimal grossPnl = direction == TradeDirection.Long
            ? (currentPrice - _entryPrice!.Value) * quantity
            : (_entryPrice!.Value - currentPrice) * quantity;
        var tradingCosts = CalculateTradingCosts(_entryPrice!.Value, currentPrice, quantity);
        var netPnl = grossPnl - tradingCosts;

        if (_settings.PaperTrade)
        {
            _paperEquity += netPnl;
            Log($"[PAPER] Closed {direction} {quantity:F5} @ {currentPrice:F2}, Gross PnL: {grossPnl:F2} USDT, Net PnL: {netPnl:F2} USDT (costs: {tradingCosts:F2}) - {reason}");
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

                // Validate execution slippage
                var fillPrice = result.Data.AverageFillPrice ?? currentPrice;
                var validation = _executionValidator.ValidateExecution(currentPrice, fillPrice, side);
                var slippageDesc = _executionValidator.GetSlippageDescription(validation, side);

                grossPnl = direction == TradeDirection.Long
                    ? (fillPrice - _entryPrice!.Value) * quantity
                    : (_entryPrice!.Value - fillPrice) * quantity;
                tradingCosts = CalculateTradingCosts(_entryPrice!.Value, fillPrice, quantity);
                netPnl = grossPnl - tradingCosts;

                Log($"Closed {direction} {quantity:F5} @ {fillPrice:F2}, Gross PnL: {grossPnl:F2} USDT, Net PnL: {netPnl:F2} USDT (costs: {tradingCosts:F2}) - {reason}");
                Log(slippageDesc);

                if (!validation.IsAcceptable)
                {
                    Log($"⚠️ WARNING: Exit {validation.RejectReason}");
                }
            }
            catch (Exception ex)
            {
                Log($"Close error: {ex.Message}");
                return;
            }
        }

        // Update equity
        var equity = _settings.PaperTrade
            ? _paperEquity
            : await GetAccountBalanceAsync();
        _riskManager.UpdateEquity(equity);
        OnEquityUpdate?.Invoke(equity);

        // Send Telegram notification
        if (_telegram != null && _entryPrice.HasValue)
        {
            var riskAmount = Math.Abs(_entryPrice.Value - (_stopLoss ?? _entryPrice.Value)) * quantity;
            var rMultiple = riskAmount > 0 ? netPnl / riskAmount : 0;
            await _telegram.SendTradeClose(_settings.Symbol, _entryPrice.Value, currentPrice, netPnl, rMultiple, reason);
        }

        // Reset position
        _currentPosition = 0;
        _entryPrice = null;
        _stopLoss = null;
        _takeProfit = null;
        _riskManager.RemovePosition(_settings.Symbol);
    }

    private async Task ClosePartialPositionAsync(TradeSignal signal)
    {
        if (_currentPosition == 0) return;

        decimal exitFraction = signal.PartialExitPercent ?? 0m;
        if (exitFraction > 1m)
        {
            exitFraction /= 100m;
        }

        var currentQuantity = Math.Abs(_currentPosition);
        decimal exitQuantity = signal.PartialExitQuantity ?? currentQuantity * exitFraction;
        if (exitQuantity <= 0)
            return;

        exitQuantity = Math.Min(exitQuantity, currentQuantity);
        var currentPrice = await GetCurrentPriceAsync();
        var direction = _currentPosition > 0 ? TradeDirection.Long : TradeDirection.Short;

        decimal grossPnl = direction == TradeDirection.Long
            ? (currentPrice - _entryPrice!.Value) * exitQuantity
            : (_entryPrice!.Value - currentPrice) * exitQuantity;
        var tradingCosts = CalculateTradingCosts(_entryPrice!.Value, currentPrice, exitQuantity);
        var netPnl = grossPnl - tradingCosts;

        if (_settings.PaperTrade)
        {
            _paperEquity += netPnl;
            Log($"[PAPER] Partial close {direction} {exitQuantity:F5} @ {currentPrice:F2}, Net PnL: {netPnl:F2} USDT - {signal.Reason}");
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
                    exitQuantity
                );

                if (!result.Success)
                {
                    Log($"Partial close failed: {result.Error?.Message}");
                    return;
                }

                // Validate execution slippage
                var fillPrice = result.Data.AverageFillPrice ?? currentPrice;
                var validation = _executionValidator.ValidateExecution(currentPrice, fillPrice, side);
                var slippageDesc = _executionValidator.GetSlippageDescription(validation, side);

                grossPnl = direction == TradeDirection.Long
                    ? (fillPrice - _entryPrice!.Value) * exitQuantity
                    : (_entryPrice!.Value - fillPrice) * exitQuantity;
                tradingCosts = CalculateTradingCosts(_entryPrice!.Value, fillPrice, exitQuantity);
                netPnl = grossPnl - tradingCosts;

                Log($"Partial close {direction} {exitQuantity:F5} @ {fillPrice:F2}, Net PnL: {netPnl:F2} USDT - {signal.Reason}");
                Log(slippageDesc);

                if (!validation.IsAcceptable)
                {
                    Log($"⚠️ WARNING: Partial exit {validation.RejectReason}");
                }
            }
            catch (Exception ex)
            {
                Log($"Partial close error: {ex.Message}");
                return;
            }
        }

        var remainingQuantity = currentQuantity - exitQuantity;
        _currentPosition = direction == TradeDirection.Long ? remainingQuantity : -remainingQuantity;

        if (signal.StopLoss.HasValue)
        {
            _stopLoss = signal.StopLoss;
        }

        var equity = _settings.PaperTrade
            ? _paperEquity
            : await GetAccountBalanceAsync();
        _riskManager.UpdateEquity(equity);
        OnEquityUpdate?.Invoke(equity);

        if (remainingQuantity <= 0)
        {
            _currentPosition = 0;
            _entryPrice = null;
            _stopLoss = null;
            _takeProfit = null;
            _riskManager.RemovePosition(_settings.Symbol);
            await CancelOcoOrderAsync();
            return;
        }

        _riskManager.UpdatePositionAfterPartialExit(
            _settings.Symbol,
            remainingQuantity,
            _stopLoss ?? _entryPrice!.Value,
            signal.MoveStopToBreakeven,
            currentPrice);

        // Обновить OCO для оставшейся позиции
        if (!_settings.PaperTrade && _takeProfit.HasValue && _stopLoss.HasValue)
        {
            await UpdateTrailingStopAsync(_stopLoss.Value, _takeProfit.Value);
        }
    }

    private decimal CalculateTradingCosts(decimal entryPrice, decimal exitPrice, decimal quantity)
    {
        var notional = (entryPrice + exitPrice) * quantity;
        var feeCosts = notional * _settings.FeeRate;
        var slippageRate = _settings.SlippageBps / 10000m;
        var slippageCosts = notional * slippageRate;
        return feeCosts + slippageCosts;
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
    public TradingMode TradingMode { get; init; } = TradingMode.Spot;
    public decimal FeeRate { get; init; } = 0.001m;
    public decimal SlippageBps { get; init; } = 2m;
}

public enum TradingMode
{
    Spot,
    Futures
}
