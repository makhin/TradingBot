using Binance.Net.Clients;
using Binance.Net.Enums;
using Binance.Net.Objects.Models.Spot.Socket;
using Binance.Net.Interfaces;
using CryptoExchange.Net.Authentication;
using CryptoExchange.Net.Sockets;
using ComplexBot.Models;
using ComplexBot.Services.Strategies;
using ComplexBot.Services.Backtesting;
using TradingBot.Core.Models;
using TradingBot.Core.RiskManagement;
using TradingBot.Core.Notifications;
using TradingBot.Core.State;
using TradingBot.Core.Analytics;
using TradingBot.Core.Lifecycle;
using CryptoExchange.Net.Objects.Sockets;
using Serilog;
using Serilog.Events;

namespace ComplexBot.Services.Trading;

public class BinanceLiveTrader : IAsyncDisposable, ILiveTrader
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
    private DateTime? _entryTime;
    private decimal? _stopLoss;
    private decimal? _takeProfit;
    private decimal _paperEquity;
    private volatile bool _isRunning;
    private UpdateSubscription? _subscription;
    private int _reconnectAttempts = 0;
    private const int MaxReconnectAttempts = 10;
    private long? _currentOcoOrderListId;
    private readonly ILogger _logger = Serilog.Log.ForContext<BinanceLiveTrader>();
    private DateTime _lastStatusLogUtc = DateTime.MinValue;
    private DateTime _lastBalanceLogUtc = DateTime.MinValue;

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

    private async Task<OrderResult> PlaceLimitOrderWithTimeout(
        OrderSide side,
        decimal quantity,
        decimal limitPrice,
        int timeoutSeconds)
    {
        if (_settings.PaperTrade)
        {
            // Paper trading - simulate immediate fill at limit price
            await Task.Delay(100); // Small delay to simulate
            Log($"[PAPER] Limit order filled: {quantity:F5} @ {limitPrice:F2}");
            return new OrderResult(true, quantity, limitPrice, null);
        }

        try
        {
            // Place limit order
            var orderResult = await _restClient.SpotApi.Trading.PlaceOrderAsync(
                _settings.Symbol,
                side,
                SpotOrderType.Limit,
                quantity,
                price: limitPrice,
                timeInForce: TimeInForce.GoodTillCanceled
            );

            if (!orderResult.Success)
            {
                return new OrderResult(false, 0, 0, orderResult.Error?.Message);
            }

            var orderId = orderResult.Data.Id;
            Log($"Limit order placed: ID={orderId}, Price={limitPrice:F2}");

            // Wait for fill with timeout
            var startTime = DateTime.UtcNow;
            while ((DateTime.UtcNow - startTime).TotalSeconds < timeoutSeconds)
            {
                await Task.Delay(200); // Check every 200ms

                var queryResult = await _restClient.SpotApi.Trading.GetOrderAsync(_settings.Symbol, orderId);
                if (!queryResult.Success)
                    continue;

                var order = queryResult.Data;

                // Check if fully filled
                if (order.Status == Binance.Net.Enums.OrderStatus.Filled)
                {
                    var avgPrice = order.AverageFillPrice ?? limitPrice;
                    Log($"✅ Limit order filled: {order.QuantityFilled:F5} @ {avgPrice:F2}");
                    return new OrderResult(true, order.QuantityFilled, avgPrice, null);
                }

                // Check if partially filled
                if (order.QuantityFilled > 0 && order.Status == Binance.Net.Enums.OrderStatus.PartiallyFilled)
                {
                    // Continue waiting for full fill
                    continue;
                }

                // Check if cancelled or rejected
                if (order.Status == Binance.Net.Enums.OrderStatus.Canceled ||
                    order.Status == Binance.Net.Enums.OrderStatus.Rejected ||
                    order.Status == Binance.Net.Enums.OrderStatus.Expired)
                {
                    return new OrderResult(false, 0, 0, $"Order {order.Status}");
                }
            }

            // Timeout - cancel the order
            Log("⏱️ Limit order timeout, cancelling...", LogEventLevel.Warning);
            var cancelResult = await _restClient.SpotApi.Trading.CancelOrderAsync(_settings.Symbol, orderId);

            if (cancelResult.Success)
            {
                // Check if any partial fill occurred
                var finalQuery = await _restClient.SpotApi.Trading.GetOrderAsync(_settings.Symbol, orderId);
                if (finalQuery.Success && finalQuery.Data.QuantityFilled > 0)
                {
                    var avgPrice = finalQuery.Data.AverageFillPrice ?? limitPrice;
                    Log($"⚠️ Partial fill: {finalQuery.Data.QuantityFilled:F5} @ {avgPrice:F2}", LogEventLevel.Warning);
                    return new OrderResult(true, finalQuery.Data.QuantityFilled, avgPrice, "Partial fill");
                }
            }

            return new OrderResult(false, 0, 0, "Timeout - order not filled");
        }
        catch (Exception ex)
        {
            Log($"Limit order error: {ex.Message}", LogEventLevel.Error);
            return new OrderResult(false, 0, 0, ex.Message);
        }
    }

    private async Task<OrderResult> EnterPositionSmart(
        OrderSide side,
        decimal quantity,
        decimal currentPrice)
    {
        if (_settings.PaperTrade)
        {
            // Paper trading - just use market simulation
            return new OrderResult(true, quantity, currentPrice, null);
        }

        // Try limit order first (slightly better price)
        var offsetMultiplier = _settings.LimitOrderOffsetBps / 10000m;
        decimal limitPrice = side == OrderSide.Buy
            ? currentPrice * (1 - offsetMultiplier)
            : currentPrice * (1 + offsetMultiplier);

        Log($"Attempting limit order @ {limitPrice:F2} (market: {currentPrice:F2})", LogEventLevel.Debug);
        var limitResult = await PlaceLimitOrderWithTimeout(
            side,
            quantity,
            limitPrice,
            _settings.LimitOrderTimeoutSeconds);

        if (limitResult.Success)
        {
            // Limit order filled - great!
            var savedAmount = Math.Abs(limitResult.AveragePrice - currentPrice) * limitResult.FilledQuantity;
            Log($"💰 Limit order saved ${savedAmount:F2} vs market price", LogEventLevel.Information);
            return limitResult;
        }

        // Limit order failed - fallback to market order
        Log("Limit order failed, using market order...", LogEventLevel.Warning);

        try
        {
            var marketResult = await _restClient.SpotApi.Trading.PlaceOrderAsync(
                _settings.Symbol,
                side,
                SpotOrderType.Market,
                quantity
            );

            if (!marketResult.Success)
            {
                return new OrderResult(false, 0, 0, marketResult.Error?.Message);
            }

            var avgPrice = marketResult.Data.AverageFillPrice ?? currentPrice;
            Log($"Market order filled: {quantity:F5} @ {avgPrice:F2}", LogEventLevel.Information);
            return new OrderResult(true, quantity, avgPrice, null);
        }
        catch (Exception ex)
        {
            return new OrderResult(false, 0, 0, ex.Message);
        }
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
                Log("✅ OCO Order placed:", LogEventLevel.Information);
                Log($"   Take Profit: {takeProfitPrice:F2}", LogEventLevel.Information);
                Log($"   Stop Loss: {stopLossPrice:F2} (limit: {stopLossLimitPrice:F2})", LogEventLevel.Information);
                Log($"   Order List ID: {result.Data.Id}", LogEventLevel.Information);
                return true;
            }
            else
            {
                Log($"❌ OCO Order failed: {result.Error?.Message}", LogEventLevel.Error);
                return false;
            }
        }
        catch (Exception ex)
        {
            Log($"❌ OCO Order exception: {ex.Message}", LogEventLevel.Error);
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
                Log($"✅ OCO Order cancelled (ID: {_currentOcoOrderListId})", LogEventLevel.Information);
                _currentOcoOrderListId = null;
                return true;
            }
            else
            {
                Log($"❌ OCO Cancel failed: {result.Error?.Message}", LogEventLevel.Error);
                return false;
            }
        }
        catch (Exception ex)
        {
            Log($"❌ OCO Cancel exception: {ex.Message}", LogEventLevel.Error);
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
        Log($"🔄 Trailing stop updated to {newStopPrice:F2}", LogEventLevel.Information);
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_settings.TradingMode == TradingMode.Futures)
        {
            Log("Futures/margin trading is not supported by BinanceLiveTrader. Please select spot mode.", LogEventLevel.Error);
            throw new NotSupportedException("BinanceLiveTrader supports spot trading only.");
        }

        _isRunning = true;
        Log($"Starting {(_settings.PaperTrade ? "PAPER" : "LIVE")} trading on {_settings.Symbol}", LogEventLevel.Information);
        Log($"Trading mode: {_settings.TradingMode}", LogEventLevel.Information);
        Log($"Testnet: {_settings.UseTestnet}", LogEventLevel.Information);
        
        // Get initial balance
        var balance = await GetAccountBalanceAsync();
        _paperEquity = _settings.PaperTrade ? _settings.InitialCapital : balance;
        Log($"USDT Balance: {balance:F2}", LogEventLevel.Information);
        _riskManager.UpdateEquity(_settings.PaperTrade ? _paperEquity : balance);
        OnEquityUpdate?.Invoke(_settings.PaperTrade ? _paperEquity : balance);

        // Load historical candles for indicator warmup
        await WarmupIndicatorsAsync();

        // Subscribe to kline updates with retry logic
        await SubscribeToKlineWithRetryAsync(cancellationToken);
        Log("Subscribed to kline updates. Waiting for signals...", LogEventLevel.Information);

        // Keep running until cancelled
        try
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            Log("Stopping trader...", LogEventLevel.Information);
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _isRunning = false;

        if (_subscription != null)
        {
            await _subscription.CloseAsync();
            _subscription = null;
        }

        // Cancel OCO orders and close any open position
        if (_currentPosition != 0)
        {
            Log("Closing open position...", LogEventLevel.Information);
            await ClosePositionAsync("Manual stop");
        }
        else
        {
            // Cancel OCO even if position was closed by exchange
            await CancelOcoOrderAsync();
        }

        cancellationToken.ThrowIfCancellationRequested();
        Log("Trader stopped.", LogEventLevel.Information);
    }

    private async Task WarmupIndicatorsAsync()
    {
        Log("Loading historical data for indicator warmup...", LogEventLevel.Information);
        
        var klines = await _restClient.SpotApi.ExchangeData.GetKlinesAsync(
            _settings.Symbol,
            _settings.Interval.ToBinanceInterval(),
            limit: _settings.WarmupCandles
        );

        if (!klines.Success)
        {
            Log($"Failed to load historical data: {klines.Error?.Message}", LogEventLevel.Warning);
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

        Log($"Warmed up with {klines.Data.Count()} candles", LogEventLevel.Information);
    }

    private async Task SubscribeToKlineWithRetryAsync(CancellationToken cancellationToken)
    {
        while (_isRunning && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                var subscribeResult = await _socketClient.SpotApi.ExchangeData.SubscribeToKlineUpdatesAsync(
                    _settings.Symbol,
                    _settings.Interval.ToBinanceInterval(),
                    async data => await OnKlineUpdateAsync(data.Data)
                );

                if (subscribeResult.Success)
                {
                    _subscription = subscribeResult.Data;
                    _reconnectAttempts = 0;
                    Log("WebSocket subscription successful", LogEventLevel.Information);
                    return;
                }
                else
                {
                    _reconnectAttempts++;
                    var errorMsg = subscribeResult.Error?.Message ?? "Unknown error";
                    Log($"WebSocket subscription failed (attempt {_reconnectAttempts}/{MaxReconnectAttempts}): {errorMsg}", LogEventLevel.Error);

                    if (_reconnectAttempts >= MaxReconnectAttempts)
                    {
                        Log($"Max reconnection attempts reached. Stopping trader.", LogEventLevel.Error);
                        throw new Exception($"Failed to subscribe after {MaxReconnectAttempts} attempts: {errorMsg}");
                    }

                    // Exponential backoff: 2^attempt seconds (capped at 60s)
                    var delaySeconds = Math.Min(Math.Pow(2, _reconnectAttempts), 60);
                    Log($"Retrying in {delaySeconds:F0} seconds...", LogEventLevel.Warning);
                    await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                Log("WebSocket subscription cancelled", LogEventLevel.Information);
                throw;
            }
            catch (Exception ex)
            {
                _reconnectAttempts++;
                Log($"WebSocket subscription exception (attempt {_reconnectAttempts}/{MaxReconnectAttempts}): {ex.Message}", LogEventLevel.Error);

                if (_reconnectAttempts >= MaxReconnectAttempts)
                {
                    Log($"Max reconnection attempts reached. Stopping trader.", LogEventLevel.Error);
                    throw;
                }

                var delaySeconds = Math.Min(Math.Pow(2, _reconnectAttempts), 60);
                Log($"Retrying in {delaySeconds:F0} seconds...", LogEventLevel.Warning);
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);
            }
        }
    }

    private async Task OnKlineUpdateAsync(IBinanceStreamKlineData data)
    {
        try
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
            await CheckExitConditionsAsync(candle);

            // Analyze for signals
            var signal = _strategy.Analyze(candle, _currentPosition, _settings.Symbol);

            if (signal != null)
            {
                Log($"Signal: {signal.Type} at {signal.Price:F2} - {signal.Reason}", LogEventLevel.Information);
                OnSignal?.Invoke(signal);

                await ProcessSignalAsync(signal, candle);
            }
            else
            {
                LogWaitingStatus(candle);
            }

            await LogBalanceSnapshotAsync();
        }
        catch (Exception ex)
        {
            Log($"Error in OnKlineUpdateAsync: {ex.Message}", LogEventLevel.Error);
            Log($"Stack trace: {ex.StackTrace}", LogEventLevel.Debug);

            // Continue running - don't crash the trading session
            // The error is logged and the system will try to process the next candle
        }
    }

    private async Task CheckExitConditionsAsync(Candle candle)
    {
        if (_currentPosition == 0 || (!_stopLoss.HasValue && !_takeProfit.HasValue))
        {
            return;
        }

        var direction = _currentPosition > 0 ? TradeDirection.Long : TradeDirection.Short;
        var result = ExitConditionChecker.CheckExit(
            candle,
            _stopLoss,
            _takeProfit,
            direction,
            price => price,
            stopLossFirst: false);

        if (!result.ShouldExit)
        {
            return;
        }

        var logLevel = result.Reason == "Stop Loss"
            ? LogEventLevel.Warning
            : LogEventLevel.Information;

        Log($"{result.Reason} triggered at {result.ExitPrice:F2}", logLevel);
        await ClosePositionAsync(result.Reason);
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
        decimal? takeProfit,
        CancellationToken cancellationToken = default)
    {
        if (_settings.TradingMode == TradingMode.Spot && direction == TradeDirection.Short)
        {
            Log("Short positions are not allowed in spot mode without margin.", LogEventLevel.Warning);
            return;
        }

        // Round quantity to valid precision
        quantity = Math.Round(quantity, _settings.QuantityPrecision);
        
        if (quantity * price < _settings.MinimumOrderUsd) // Binance minimum order
        {
            Log($"Position too small: {quantity * price:F2} USDT", LogEventLevel.Warning);
            return;
        }

        if (_settings.PaperTrade)
        {
            // Paper trade - just track position
            _currentPosition = direction == TradeDirection.Long ? quantity : -quantity;
            _entryPrice = price;
            _entryTime = DateTime.UtcNow;
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
            Log($"[PAPER] Opened {direction} {quantity:F5} @ {price:F2}, SL: {stopLoss:F2}{takeProfitText}", LogEventLevel.Information);
            OnTrade?.Invoke(Trade.Create(
                _settings.Symbol,
                _entryTime.Value,
                null,
                price,
                null,
                quantity,
                direction,
                stopLoss,
                takeProfit,
                null));

            if (_telegram != null)
            {
                var riskAmt = Math.Abs(price - stopLoss) * quantity;
                var signal = TradeSignal.Create(
                    _settings.Symbol,
                    direction == TradeDirection.Long ? SignalType.Buy : SignalType.Sell,
                    price,
                    stopLoss,
                    takeProfit,
                    $"{direction} position opened (paper)"
                );
                await _telegram.SendTradeOpen(signal, quantity, riskAmt, cancellationToken);
            }
        }
        else
        {
            // Real trade
            try
            {
                var side = direction == TradeDirection.Long ? OrderSide.Buy : OrderSide.Sell;

                // 1. Открыть позицию умным способом (limit -> market fallback)
                var orderResult = await EnterPositionSmart(side, quantity, price);

                if (!orderResult.Success)
                {
                    Log($"Order failed: {orderResult.ErrorMessage}", LogEventLevel.Error);
                    return;
                }

                var actualPrice = orderResult.AveragePrice;
                var actualQuantity = orderResult.FilledQuantity;

                // Validate execution slippage
                var validation = _executionValidator.ValidateExecution(price, actualPrice, side);
                var slippageDesc = _executionValidator.GetSlippageDescription(validation, side);
                Log(slippageDesc, LogEventLevel.Debug);

                if (!validation.IsAcceptable)
                {
                    Log($"⚠️ WARNING: {validation.RejectReason}", LogEventLevel.Warning);
                    Log($"   Expected: {validation.ExpectedPrice:F2}, Actual: {validation.ActualPrice:F2}", LogEventLevel.Warning);
                }

                _currentPosition = direction == TradeDirection.Long ? actualQuantity : -actualQuantity;
                _entryPrice = actualPrice;
                _entryTime = DateTime.UtcNow;
                _stopLoss = stopLoss;
                _takeProfit = takeProfit;

                _riskManager.AddPosition(
                    _settings.Symbol,
                    direction == TradeDirection.Long ? SignalType.Buy : SignalType.Sell,
                    Math.Abs(_currentPosition),
                    Math.Abs(actualPrice - stopLoss) * actualQuantity,
                    actualPrice,
                    stopLoss,
                    actualPrice);

                var takeProfitText = takeProfit.HasValue ? $", TP: {takeProfit:F2}" : string.Empty;
                Log($"Opened {direction} {actualQuantity:F5} @ {_entryPrice:F2}, SL: {stopLoss:F2}{takeProfitText}", LogEventLevel.Information);
                OnTrade?.Invoke(Trade.Create(
                    _settings.Symbol,
                    _entryTime.Value,
                    null,
                    actualPrice,
                    null,
                    actualQuantity,
                    direction,
                    stopLoss,
                    takeProfit,
                    null));

                // 2. Сразу выставить OCO для защиты (если есть тейк-профит)
                if (takeProfit.HasValue)
                {
                    var exitSide = direction == TradeDirection.Long ? OrderSide.Sell : OrderSide.Buy;
                    var stopLimitPrice = direction == TradeDirection.Long
                        ? stopLoss * 0.995m  // 0.5% ниже для лонга
                        : stopLoss * 1.005m; // 0.5% выше для шорта

                    await PlaceOcoOrderAsync(exitSide, actualQuantity, stopLoss, stopLimitPrice, takeProfit.Value);
                }

                // 3. Отправить Telegram уведомление
                if (_telegram != null)
                {
                    var riskAmt = Math.Abs(actualPrice - stopLoss) * actualQuantity;
                    var signal = TradeSignal.Create(
                        _settings.Symbol,
                        direction == TradeDirection.Long ? SignalType.Buy : SignalType.Sell,
                        actualPrice,
                        stopLoss,
                        takeProfit,
                        $"{direction} position opened"
                    );
                    await _telegram.SendTradeOpen(signal, actualQuantity, riskAmt, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                Log($"Order error: {ex.Message}", LogEventLevel.Error);
            }
        }
    }

    private async Task ClosePositionAsync(string reason, CancellationToken cancellationToken = default)
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
        var tradingCosts = TradeCostCalculator.CalculateTotalCosts(
            _entryPrice!.Value,
            currentPrice,
            quantity,
            _settings.FeeRate,
            _settings.SlippageBps / 10000m);
        var netPnl = grossPnl - tradingCosts;
        var exitPrice = currentPrice;

        if (_settings.PaperTrade)
        {
            _paperEquity += netPnl;
            Log($"[PAPER] Closed {direction} {quantity:F5} @ {currentPrice:F2}, Gross PnL: {grossPnl:F2} USDT, Net PnL: {netPnl:F2} USDT (costs: {tradingCosts:F2}) - {reason}", LogEventLevel.Information);
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
                    Log($"Close order failed: {result.Error?.Message}", LogEventLevel.Error);
                    return;
                }

                // Validate execution slippage
                var fillPrice = result.Data.AverageFillPrice ?? currentPrice;
                var validation = _executionValidator.ValidateExecution(currentPrice, fillPrice, side);
                var slippageDesc = _executionValidator.GetSlippageDescription(validation, side);

                grossPnl = direction == TradeDirection.Long
                    ? (fillPrice - _entryPrice!.Value) * quantity
                    : (_entryPrice!.Value - fillPrice) * quantity;
                tradingCosts = TradeCostCalculator.CalculateTotalCosts(
                    _entryPrice!.Value,
                    fillPrice,
                    quantity,
                    _settings.FeeRate,
                    _settings.SlippageBps / 10000m);
                netPnl = grossPnl - tradingCosts;
                exitPrice = fillPrice;

                Log($"Closed {direction} {quantity:F5} @ {fillPrice:F2}, Gross PnL: {grossPnl:F2} USDT, Net PnL: {netPnl:F2} USDT (costs: {tradingCosts:F2}) - {reason}", LogEventLevel.Information);
                Log(slippageDesc, LogEventLevel.Debug);

                if (!validation.IsAcceptable)
                {
                    Log($"⚠️ WARNING: Exit {validation.RejectReason}", LogEventLevel.Warning);
                }
            }
            catch (Exception ex)
            {
                Log($"Close error: {ex.Message}", LogEventLevel.Error);
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
            await _telegram.SendTradeClose(_settings.Symbol, _entryPrice.Value, exitPrice, netPnl, rMultiple, reason, cancellationToken);
        }

        if (_entryPrice.HasValue)
        {
            var trade = Trade.Create(
                _settings.Symbol,
                _entryTime ?? DateTime.UtcNow,
                DateTime.UtcNow,
                _entryPrice.Value,
                exitPrice,
                quantity,
                direction,
                _stopLoss,
                _takeProfit,
                reason);
            OnTrade?.Invoke(trade);
        }

        // Reset position
        _currentPosition = 0;
        _entryPrice = null;
        _entryTime = null;
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
        var tradingCosts = TradeCostCalculator.CalculateTotalCosts(
            _entryPrice!.Value,
            currentPrice,
            exitQuantity,
            _settings.FeeRate,
            _settings.SlippageBps / 10000m);
        var netPnl = grossPnl - tradingCosts;

        var exitPrice = currentPrice;

        if (_settings.PaperTrade)
        {
            _paperEquity += netPnl;
            Log($"[PAPER] Partial close {direction} {exitQuantity:F5} @ {currentPrice:F2}, Net PnL: {netPnl:F2} USDT - {signal.Reason}", LogEventLevel.Information);
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
                    Log($"Partial close failed: {result.Error?.Message}", LogEventLevel.Error);
                    return;
                }

                // Validate execution slippage
                var fillPrice = result.Data.AverageFillPrice ?? currentPrice;
                var validation = _executionValidator.ValidateExecution(currentPrice, fillPrice, side);
                var slippageDesc = _executionValidator.GetSlippageDescription(validation, side);

                grossPnl = direction == TradeDirection.Long
                    ? (fillPrice - _entryPrice!.Value) * exitQuantity
                    : (_entryPrice!.Value - fillPrice) * exitQuantity;
                tradingCosts = TradeCostCalculator.CalculateTotalCosts(
                    _entryPrice!.Value,
                    fillPrice,
                    exitQuantity,
                    _settings.FeeRate,
                    _settings.SlippageBps / 10000m);
                netPnl = grossPnl - tradingCosts;
                exitPrice = fillPrice;

                Log($"Partial close {direction} {exitQuantity:F5} @ {fillPrice:F2}, Net PnL: {netPnl:F2} USDT - {signal.Reason}", LogEventLevel.Information);
                Log(slippageDesc, LogEventLevel.Debug);

                if (!validation.IsAcceptable)
                {
                    Log($"⚠️ WARNING: Partial exit {validation.RejectReason}", LogEventLevel.Warning);
                }
            }
            catch (Exception ex)
            {
                Log($"Partial close error: {ex.Message}", LogEventLevel.Error);
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

        if (_telegram != null && _entryPrice.HasValue)
        {
            var riskAmount = Math.Abs(_entryPrice.Value - (_stopLoss ?? _entryPrice.Value)) * exitQuantity;
            var rMultiple = riskAmount > 0 ? netPnl / riskAmount : 0;
            var reason = string.IsNullOrWhiteSpace(signal.Reason)
                ? "Partial Exit"
                : $"Partial Exit - {signal.Reason}";
            await _telegram.SendTradeClose(_settings.Symbol, _entryPrice.Value, exitPrice, netPnl, rMultiple, reason);
        }

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

    private void Log(string message)
    {
        Log(message, LogEventLevel.Information);
    }

    private void Log(string message, LogEventLevel level)
    {
        _logger.Write(level, message);
        OnLog?.Invoke(message);
    }

    private void LogWaitingStatus(Candle candle)
    {
        var now = DateTime.UtcNow;
        if (now - _lastStatusLogUtc < TimeSpan.FromMinutes(_settings.StatusLogIntervalMinutes))
        {
            return;
        }

        _lastStatusLogUtc = now;

        var positionState = _currentPosition switch
        {
            > 0 => "LONG",
            < 0 => "SHORT",
            _ => "FLAT"
        };

        var stopLossText = _stopLoss.HasValue ? _stopLoss.Value.ToString("F2") : "n/a";
        var takeProfitText = _takeProfit.HasValue ? _takeProfit.Value.ToString("F2") : "n/a";
        var unrealizedPnl = _riskManager.GetUnrealizedPnL();
        var totalEquity = _riskManager.GetTotalEquity();

        Log(
            $"Waiting for signal... {candle.CloseTime:HH:mm} UTC | Price {candle.Close:F2} | " +
            $"Pos {positionState} | SL {stopLossText} | TP {takeProfitText} | " +
            $"uPnL {unrealizedPnl:F2} | Equity {totalEquity:F2}");
    }

    private async Task LogBalanceSnapshotAsync()
    {
        var now = DateTime.UtcNow;
        if (now - _lastBalanceLogUtc < TimeSpan.FromHours(_settings.BalanceLogIntervalHours))
        {
            return;
        }

        _lastBalanceLogUtc = now;

        if (_settings.PaperTrade)
        {
            var positionState = _currentPosition switch
            {
                > 0 => "LONG",
                < 0 => "SHORT",
                _ => "FLAT"
            };

            Log(
                $"[PAPER] Balance snapshot: Equity {_paperEquity:F2} USDT | " +
                $"Pos {positionState} {_currentPosition:F5}",
                LogEventLevel.Information);
            return;
        }

        var result = await _restClient.SpotApi.Account.GetAccountInfoAsync();
        if (!result.Success)
        {
            Log($"Balance snapshot failed: {result.Error?.Message}", LogEventLevel.Warning);
            return;
        }

        var balances = result.Data.Balances
            .Select(balance => new
            {
                balance.Asset,
                balance.Available,
                balance.Total
            })
            .Where(balance => balance.Total > 0)
            .OrderByDescending(balance => balance.Total)
            .ToList();

        var summary = balances.Count == 0
            ? "no assets"
            : string.Join(", ", balances.Select(balance =>
                $"{balance.Asset}:{balance.Available:F6}/{balance.Total:F6}"));

        Log(
            $"Balance snapshot: {summary}",
            LogEventLevel.Information);
    }

    // Graceful Shutdown Support Methods

    public async Task<BotState> BuildCurrentState()
    {
        var equity = _settings.PaperTrade ? _paperEquity : await GetAccountBalanceAsync();

        var openPositions = new List<SavedPosition>();
        if (_currentPosition != 0 && _entryPrice.HasValue)
        {
            var currentPrice = await GetCurrentPriceAsync();
            openPositions.Add(new SavedPosition
            {
                Symbol = _settings.Symbol,
                Direction = _currentPosition > 0 ? SignalType.Buy : SignalType.Sell,
                EntryPrice = _entryPrice.Value,
                Quantity = Math.Abs(_currentPosition),
                RemainingQuantity = Math.Abs(_currentPosition),
                StopLoss = _stopLoss ?? _entryPrice.Value,
                TakeProfit = _takeProfit ?? 0,
                RiskAmount = _stopLoss.HasValue
                    ? Math.Abs(_entryPrice.Value - _stopLoss.Value) * Math.Abs(_currentPosition)
                    : 0,
                EntryTime = DateTime.UtcNow,
                TradeId = 0,
                CurrentPrice = currentPrice,
                BreakevenMoved = false
            });
        }

        var activeOcoOrders = new List<SavedOcoOrder>();
        if (_currentOcoOrderListId.HasValue)
        {
            activeOcoOrders.Add(new SavedOcoOrder
            {
                Symbol = _settings.Symbol,
                OrderListId = _currentOcoOrderListId.Value
            });
        }

        return new BotState
        {
            LastUpdate = DateTime.UtcNow,
            CurrentEquity = equity,
            PeakEquity = _riskManager.GetTotalEquity(),
            DayStartEquity = equity,
            CurrentTradingDay = DateTime.UtcNow.Date,
            OpenPositions = openPositions,
            ActiveOcoOrders = activeOcoOrders,
            NextTradeId = 1,
            Symbol = _settings.Symbol,
            Version = "1.0"
        };
    }

    /// <summary>
    /// Restores trader state from saved BotState
    /// </summary>
    public async Task RestoreFromStateAsync(BotState state, CancellationToken ct = default)
    {
        _logger.Information("🔄 Restoring bot state...");

        try
        {
            // Restore positions
            foreach (var pos in state.OpenPositions)
            {
                _currentPosition = pos.Direction == SignalType.Buy
                    ? pos.RemainingQuantity
                    : -pos.RemainingQuantity;
                _entryPrice = pos.EntryPrice;
                _stopLoss = pos.StopLoss;
                _takeProfit = pos.TakeProfit > 0 ? pos.TakeProfit : null;
                _entryTime = pos.EntryTime;

                // Restore in risk manager
                _riskManager.AddPosition(
                    pos.Symbol,
                    pos.Direction,
                    pos.RemainingQuantity,
                    pos.RiskAmount,
                    pos.EntryPrice,
                    pos.StopLoss,
                    pos.CurrentPrice
                );

                _logger.Information("Restored position: {Symbol} {Dir} {Qty:F5} @ ${Entry:F2}",
                    pos.Symbol, pos.Direction, pos.RemainingQuantity, pos.EntryPrice);
            }

            // Restore OCO orders
            foreach (var oco in state.ActiveOcoOrders)
            {
                _currentOcoOrderListId = oco.OrderListId;
                _logger.Information("Restored OCO order: {Symbol} #{OrderListId}",
                    oco.Symbol, oco.OrderListId);
            }

            // Restore equity
            if (_settings.PaperTrade)
            {
                _paperEquity = state.CurrentEquity;
            }
            _riskManager.RestoreEquityState(state.CurrentEquity, state.PeakEquity, state.DayStartEquity);

            _logger.Information("✅ State restored: Equity ${Equity:F2}, Positions: {Count}",
                state.CurrentEquity, state.OpenPositions.Count);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to restore state");
            throw;
        }
    }

    public async Task<List<SavedPosition>> GetOpenPositions()
    {
        var positions = new List<SavedPosition>();

        if (_currentPosition != 0 && _entryPrice.HasValue)
        {
            var currentPrice = await GetCurrentPriceAsync();
            positions.Add(new SavedPosition
            {
                Symbol = _settings.Symbol,
                Direction = _currentPosition > 0 ? SignalType.Buy : SignalType.Sell,
                EntryPrice = _entryPrice.Value,
                Quantity = Math.Abs(_currentPosition),
                RemainingQuantity = Math.Abs(_currentPosition),
                StopLoss = _stopLoss ?? _entryPrice.Value,
                TakeProfit = _takeProfit ?? 0,
                RiskAmount = 0,
                EntryTime = DateTime.UtcNow,
                TradeId = 0,
                CurrentPrice = currentPrice,
                BreakevenMoved = false
            });
        }

        return positions;
    }

    public async Task CancelOcoOrdersForSymbol(string symbol)
    {
        if (symbol == _settings.Symbol && _currentOcoOrderListId.HasValue)
        {
            await CancelOcoOrderAsync();
        }
    }

    public async Task ClosePosition(string symbol, string reason)
    {
        if (symbol == _settings.Symbol && _currentPosition != 0)
        {
            await ClosePositionAsync(reason);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_subscription != null)
        {
            await _subscription.CloseAsync();
            _subscription = null;
        }
        _restClient.Dispose();
        _socketClient.Dispose();
    }
}
