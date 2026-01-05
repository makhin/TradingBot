using Binance.Net.Clients;
using Binance.Net.Enums;
using Binance.Net.Objects.Models.Spot.Socket;
using Binance.Net.Interfaces;
using CryptoExchange.Net.Authentication;
using CryptoExchange.Net.Objects.Sockets;
using ComplexBot.Models;
using ComplexBot.Services.Strategies;
using ComplexBot.Services.RiskManagement;
using ComplexBot.Services.Notifications;
using ComplexBot.Services.State;

namespace ComplexBot.Services.Trading;

/// <summary>
/// Binance-specific implementation of symbol trader.
/// Inherits common logic from SymbolTraderBase, implements Binance API specifics.
/// </summary>
public class BinanceLiveTrader : SymbolTraderBase<LiveTraderSettings>
{
    // Binance-specific clients
    private readonly BinanceRestClient _restClient;
    private readonly BinanceSocketClient _socketClient;
    private readonly ExecutionValidator _executionValidator;

    // Binance-specific state
    private UpdateSubscription? _subscription;
    private long? _currentOcoOrderListId;
    private decimal _paperEquity;

    // ISymbolTrader implementation
    public override string Symbol => Settings.Symbol;
    public override string Exchange => "Binance";
    public override decimal CurrentEquity => Settings.PaperTrade ? _paperEquity : GetAccountBalanceAsync().Result;
    protected override bool CanExecuteTrades => Settings.EnableTradeExecution;

    public BinanceLiveTrader(
        string apiKey,
        string apiSecret,
        IStrategy strategy,
        RiskSettings riskSettings,
        LiveTraderSettings? settings = null,
        TelegramNotifier? telegram = null,
        PortfolioRiskManager? portfolioRiskManager = null,
        SharedEquityManager? sharedEquityManager = null)
        : base(strategy, riskSettings, settings ?? new LiveTraderSettings(), telegram, portfolioRiskManager, sharedEquityManager)
    {
        _executionValidator = new ExecutionValidator(maxSlippagePercent: 0.5m);
        _paperEquity = Settings.InitialCapital;

        // Initialize Binance clients
        _restClient = new BinanceRestClient(options =>
        {
            options.ApiCredentials = new ApiCredentials(apiKey, apiSecret);
            if (Settings.UseTestnet)
                options.Environment = Binance.Net.BinanceEnvironment.Testnet;
        });

        _socketClient = new BinanceSocketClient(options =>
        {
            options.ApiCredentials = new ApiCredentials(apiKey, apiSecret);
            if (Settings.UseTestnet)
                options.Environment = Binance.Net.BinanceEnvironment.Testnet;
        });
    }

    // Abstract method implementations
    protected override decimal GetInitialCapital() => Settings.InitialCapital;

    protected override async Task<decimal> GetCurrentPriceAsync()
    {
        var result = await _restClient.SpotApi.ExchangeData.GetPriceAsync(Settings.Symbol);
        if (!result.Success)
            throw new Exception($"Failed to get price: {result.Error?.Message}");
        return result.Data.Price;
    }

    protected override async Task<decimal> GetAccountBalanceAsync(string asset = "USDT")
    {
        if (Settings.PaperTrade)
            return _paperEquity;

        var result = await _restClient.SpotApi.Account.GetAccountInfoAsync();
        if (!result.Success)
            throw new Exception($"Failed to get balance: {result.Error?.Message}");
        var balance = result.Data.Balances.FirstOrDefault(b => b.Asset == asset);
        return balance?.Available ?? 0;
    }

    protected override async Task SubscribeToKlineUpdatesAsync(CancellationToken ct)
    {
        var subscribeResult = await _socketClient.SpotApi.ExchangeData.SubscribeToKlineUpdatesAsync(
            Settings.Symbol,
            Settings.Interval,
            async data => await OnKlineUpdateAsync(data.Data)
        );

        if (!subscribeResult.Success)
            throw new Exception($"Failed to subscribe: {subscribeResult.Error?.Message}");

        _subscription = subscribeResult.Data;
        Log("Subscribed to kline updates");
    }

    protected override async Task WarmupIndicatorsAsync()
    {
        Log("Loading historical data for indicator warmup...");

        var klines = await _restClient.SpotApi.ExchangeData.GetKlinesAsync(
            Settings.Symbol,
            Settings.Interval,
            limit: Settings.WarmupCandles
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

            CandleBuffer.Add(candle);
            Strategy.Analyze(candle, _currentPosition, Settings.Symbol);
        }

        Log($"Warmed up with {klines.Data.Count()} candles");
    }

    public override async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (Settings.TradingMode == TradingMode.Futures)
        {
            Log("Futures/margin trading is not supported by BinanceLiveTrader. Please select spot mode.");
            throw new NotSupportedException("BinanceLiveTrader supports spot trading only.");
        }

        // Call base implementation
        await base.StartAsync(cancellationToken);

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

    public override async Task StopAsync()
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

    // Binance-specific: Kline update handler
    private async Task OnKlineUpdateAsync(IBinanceStreamKlineData data)
    {
        if (!_isRunning) return;

        var kline = data.Data;

        // Only process closed candles
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

        // Check SL/TP before processing signal
        await CheckStopLossAsync(candle);
        await CheckTakeProfitAsync(candle);

        // Process candle using base class logic
        await ProcessCandleAsync(candle);
    }

    protected override async Task ProcessSignalAsync(TradeSignal signal, Candle candle)
    {
        switch (signal.Type)
        {
            case SignalType.Buy when _currentPosition <= 0:
                if (_currentPosition < 0)
                    await ClosePositionAsync("Signal Reversal");

                if (CanOpenPositionWithPortfolioCheck() && signal.StopLoss.HasValue)
                {
                    var sizing = RiskManager.CalculatePositionSize(
                        candle.Close,
                        signal.StopLoss.Value,
                        (Strategy as AdxTrendStrategy)?.CurrentAtr
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

                if (CanOpenPositionWithPortfolioCheck() && signal.StopLoss.HasValue)
                {
                    var sizing = RiskManager.CalculatePositionSize(
                        candle.Close,
                        signal.StopLoss.Value,
                        (Strategy as AdxTrendStrategy)?.CurrentAtr
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
                await ClosePositionAsync(signal.Reason ?? "Exit signal");
                break;

            case SignalType.PartialExit when _currentPosition != 0:
                await ClosePartialPositionAsync(signal);
                break;
        }
    }

    // Binance-specific: Open position logic
    private async Task OpenPositionAsync(
        TradeDirection direction,
        decimal quantity,
        decimal price,
        decimal stopLoss,
        decimal? takeProfit)
    {
        if (Settings.TradingMode == TradingMode.Spot && direction == TradeDirection.Short)
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

        if (Settings.PaperTrade)
        {
            // Paper trade - just track position
            _currentPosition = direction == TradeDirection.Long ? quantity : -quantity;
            _entryPrice = price;
            _stopLoss = stopLoss;
            _takeProfit = takeProfit;
            RiskManager.AddPosition(
                Settings.Symbol,
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
            // Real trade - use smart entry
            var side = direction == TradeDirection.Long ? Binance.Net.Enums.OrderSide.Buy : Binance.Net.Enums.OrderSide.Sell;
            var orderResult = await EnterPositionSmart(side, quantity, price);

            if (!orderResult.Success)
            {
                Log($"Order failed: {orderResult.ErrorMessage}");
                return;
            }

            var actualPrice = orderResult.AveragePrice;
            var actualQuantity = orderResult.FilledQuantity;

            // Validate slippage
            var validation = _executionValidator.ValidateExecution(price, actualPrice, side);
            var slippageDesc = _executionValidator.GetSlippageDescription(validation, side);
            Log(slippageDesc);

            _currentPosition = direction == TradeDirection.Long ? actualQuantity : -actualQuantity;
            _entryPrice = actualPrice;
            _stopLoss = stopLoss;
            _takeProfit = takeProfit;

            RiskManager.AddPosition(
                Settings.Symbol,
                direction == TradeDirection.Long ? SignalType.Buy : SignalType.Sell,
                Math.Abs(_currentPosition),
                Math.Abs(actualPrice - stopLoss) * actualQuantity,
                actualPrice,
                stopLoss,
                actualPrice);

            var takeProfitText = takeProfit.HasValue ? $", TP: {takeProfit:F2}" : string.Empty;
            Log($"Opened {direction} {actualQuantity:F5} @ {_entryPrice:F2}, SL: {stopLoss:F2}{takeProfitText}");

            // Place OCO if we have take profit
            if (takeProfit.HasValue)
            {
                var exitSide = direction == TradeDirection.Long ? Binance.Net.Enums.OrderSide.Sell : Binance.Net.Enums.OrderSide.Buy;
                var stopLimitPrice = direction == TradeDirection.Long
                    ? stopLoss * 0.995m
                    : stopLoss * 1.005m;

                await PlaceOcoOrderAsync(exitSide, actualQuantity, stopLoss, stopLimitPrice, takeProfit.Value);
            }

            // Telegram notification
            if (Telegram != null)
            {
                var riskAmt = Math.Abs(actualPrice - stopLoss) * actualQuantity;
                var signal = new TradeSignal(
                    Settings.Symbol,
                    direction == TradeDirection.Long ? SignalType.Buy : SignalType.Sell,
                    actualPrice,
                    stopLoss,
                    takeProfit,
                    $"{direction} position opened"
                );
                await Telegram.SendTradeOpen(signal, actualQuantity, riskAmt);
            }
        }
    }

    public override async Task ClosePositionAsync(string reason)
    {
        if (_currentPosition == 0) return;

        // Cancel OCO
        await CancelOcoOrderAsync();

        var currentPrice = await GetCurrentPriceAsync();
        var direction = _currentPosition > 0 ? TradeDirection.Long : TradeDirection.Short;
        var quantity = Math.Abs(_currentPosition);

        decimal grossPnl = direction == TradeDirection.Long
            ? (currentPrice - _entryPrice!.Value) * quantity
            : (_entryPrice!.Value - currentPrice) * quantity;
        var tradingCosts = CalculateTradingCosts(_entryPrice!.Value, currentPrice, quantity);
        var netPnl = grossPnl - tradingCosts;

        if (Settings.PaperTrade)
        {
            _paperEquity += netPnl;
            Log($"[PAPER] Closed {direction} {quantity:F5} @ {currentPrice:F2}, Net PnL: {netPnl:F2} USDT - {reason}");
        }
        else
        {
            var side = direction == TradeDirection.Long ? Binance.Net.Enums.OrderSide.Sell : Binance.Net.Enums.OrderSide.Buy;

            var result = await _restClient.SpotApi.Trading.PlaceOrderAsync(
                Settings.Symbol,
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
            grossPnl = direction == TradeDirection.Long
                ? (fillPrice - _entryPrice!.Value) * quantity
                : (_entryPrice!.Value - fillPrice) * quantity;
            tradingCosts = CalculateTradingCosts(_entryPrice!.Value, fillPrice, quantity);
            netPnl = grossPnl - tradingCosts;

            Log($"Closed {direction} {quantity:F5} @ {fillPrice:F2}, Net PnL: {netPnl:F2} USDT - {reason}");
        }

        // Update equity
        var equity = await GetAccountBalanceAsync();
        UpdateEquity(equity);

        // Record trade
        var trade = new Trade(
            Settings.Symbol,
            DateTime.UtcNow.AddMinutes(-30), // Approximate entry time
            DateTime.UtcNow,
            _entryPrice!.Value,
            currentPrice,
            quantity,
            direction,
            _stopLoss,
            _takeProfit,
            reason
        );
        RecordTrade(netPnl, trade);

        // Telegram notification
        if (Telegram != null && _entryPrice.HasValue)
        {
            var riskAmount = Math.Abs(_entryPrice.Value - (_stopLoss ?? _entryPrice.Value)) * quantity;
            var rMultiple = riskAmount > 0 ? netPnl / riskAmount : 0;
            await Telegram.SendTradeClose(Settings.Symbol, _entryPrice.Value, currentPrice, netPnl, rMultiple, reason);
        }

        // Reset position
        _currentPosition = 0;
        _entryPrice = null;
        _stopLoss = null;
        _takeProfit = null;
        RiskManager.RemovePosition(Settings.Symbol);
    }

    // Binance-specific: Smart entry (limit -> market fallback)
    private async Task<OrderResult> EnterPositionSmart(
        Binance.Net.Enums.OrderSide side,
        decimal quantity,
        decimal currentPrice)
    {
        if (Settings.PaperTrade)
        {
            return new OrderResult(true, quantity, currentPrice, null);
        }

        // Try limit order first
        decimal limitPrice = side == Binance.Net.Enums.OrderSide.Buy
            ? currentPrice * 0.9995m
            : currentPrice * 1.0005m;

        Log($"Attempting limit order @ {limitPrice:F2}");
        var limitResult = await PlaceLimitOrderWithTimeout(side, quantity, limitPrice, 3);

        if (limitResult.Success)
        {
            var saved = Math.Abs(limitResult.AveragePrice - currentPrice) * limitResult.FilledQuantity;
            Log($"💰 Limit order saved ${saved:F2}");
            return limitResult;
        }

        // Fallback to market
        Log("Limit failed, using market order");
        var marketResult = await _restClient.SpotApi.Trading.PlaceOrderAsync(
            Settings.Symbol,
            side,
            SpotOrderType.Market,
            quantity
        );

        if (!marketResult.Success)
            return new OrderResult(false, 0, 0, marketResult.Error?.Message);

        var avgPrice = marketResult.Data.AverageFillPrice ?? currentPrice;
        return new OrderResult(true, quantity, avgPrice, null);
    }

    private async Task<OrderResult> PlaceLimitOrderWithTimeout(
        Binance.Net.Enums.OrderSide side,
        decimal quantity,
        decimal limitPrice,
        int timeoutSeconds)
    {
        var orderResult = await _restClient.SpotApi.Trading.PlaceOrderAsync(
            Settings.Symbol,
            side,
            SpotOrderType.Limit,
            quantity,
            price: limitPrice,
            timeInForce: TimeInForce.GoodTillCanceled
        );

        if (!orderResult.Success)
            return new OrderResult(false, 0, 0, orderResult.Error?.Message);

        var orderId = orderResult.Data.Id;
        var startTime = DateTime.UtcNow;

        while ((DateTime.UtcNow - startTime).TotalSeconds < timeoutSeconds)
        {
            await Task.Delay(200);

            var queryResult = await _restClient.SpotApi.Trading.GetOrderAsync(Settings.Symbol, orderId);
            if (!queryResult.Success) continue;

            if (queryResult.Data.Status == Binance.Net.Enums.OrderStatus.Filled)
            {
                var avgPrice = queryResult.Data.AverageFillPrice ?? limitPrice;
                return new OrderResult(true, queryResult.Data.QuantityFilled, avgPrice, null);
            }

            if (queryResult.Data.Status == Binance.Net.Enums.OrderStatus.Canceled ||
                queryResult.Data.Status == Binance.Net.Enums.OrderStatus.Rejected)
            {
                return new OrderResult(false, 0, 0, $"Order {queryResult.Data.Status}");
            }
        }

        // Timeout - cancel
        await _restClient.SpotApi.Trading.CancelOrderAsync(Settings.Symbol, orderId);
        return new OrderResult(false, 0, 0, "Timeout");
    }

    private async Task<bool> PlaceOcoOrderAsync(
        Binance.Net.Enums.OrderSide side,
        decimal quantity,
        decimal stopLossPrice,
        decimal stopLossLimitPrice,
        decimal takeProfitPrice)
    {
        if (Settings.PaperTrade)
        {
            Log($"[PAPER] OCO: TP={takeProfitPrice:F2}, SL={stopLossPrice:F2}");
            return true;
        }

        var result = await _restClient.SpotApi.Trading.PlaceOcoOrderAsync(
            symbol: Settings.Symbol,
            side: side,
            quantity: quantity,
            price: takeProfitPrice,
            stopPrice: stopLossPrice,
            stopLimitPrice: stopLossLimitPrice,
            stopLimitTimeInForce: TimeInForce.GoodTillCanceled
        );

        if (result.Success)
        {
            _currentOcoOrderListId = result.Data.Id;
            Log($"✅ OCO: TP={takeProfitPrice:F2}, SL={stopLossPrice:F2}");
            return true;
        }

        Log($"❌ OCO failed: {result.Error?.Message}");
        return false;
    }

    private async Task<bool> CancelOcoOrderAsync()
    {
        if (!_currentOcoOrderListId.HasValue || Settings.PaperTrade)
            return true;

        var result = await _restClient.SpotApi.Trading.CancelOcoOrderAsync(
            Settings.Symbol,
            orderListId: _currentOcoOrderListId.Value
        );

        if (result.Success)
        {
            Log($"✅ OCO cancelled");
            _currentOcoOrderListId = null;
            return true;
        }

        return false;
    }

    private async Task ClosePartialPositionAsync(TradeSignal signal)
    {
        if (_currentPosition == 0) return;

        decimal exitFraction = signal.PartialExitPercent ?? 0m;
        if (exitFraction > 1m)
            exitFraction /= 100m;

        var currentQuantity = Math.Abs(_currentPosition);
        decimal exitQuantity = signal.PartialExitQuantity ?? currentQuantity * exitFraction;
        if (exitQuantity <= 0) return;

        exitQuantity = Math.Min(exitQuantity, currentQuantity);
        var currentPrice = await GetCurrentPriceAsync();
        var direction = _currentPosition > 0 ? TradeDirection.Long : TradeDirection.Short;

        decimal grossPnl = direction == TradeDirection.Long
            ? (currentPrice - _entryPrice!.Value) * exitQuantity
            : (_entryPrice!.Value - currentPrice) * exitQuantity;
        var tradingCosts = CalculateTradingCosts(_entryPrice!.Value, currentPrice, exitQuantity);
        var netPnl = grossPnl - tradingCosts;

        if (Settings.PaperTrade)
        {
            _paperEquity += netPnl;
            Log($"[PAPER] Partial close {direction} {exitQuantity:F5} @ {currentPrice:F2}, Net PnL: {netPnl:F2} USDT - {signal.Reason}");
        }
        else
        {
            var side = direction == TradeDirection.Long ? Binance.Net.Enums.OrderSide.Sell : Binance.Net.Enums.OrderSide.Buy;
            var result = await _restClient.SpotApi.Trading.PlaceOrderAsync(
                Settings.Symbol,
                side,
                SpotOrderType.Market,
                exitQuantity
            );

            if (!result.Success)
            {
                Log($"Partial close failed: {result.Error?.Message}");
                return;
            }

            var fillPrice = result.Data.AverageFillPrice ?? currentPrice;
            grossPnl = direction == TradeDirection.Long
                ? (fillPrice - _entryPrice!.Value) * exitQuantity
                : (_entryPrice!.Value - fillPrice) * exitQuantity;
            tradingCosts = CalculateTradingCosts(_entryPrice!.Value, fillPrice, exitQuantity);
            netPnl = grossPnl - tradingCosts;

            Log($"Partial close {direction} {exitQuantity:F5} @ {fillPrice:F2}, Net PnL: {netPnl:F2} USDT - {signal.Reason}");
        }

        var remainingQuantity = currentQuantity - exitQuantity;
        _currentPosition = direction == TradeDirection.Long ? remainingQuantity : -remainingQuantity;

        if (signal.StopLoss.HasValue)
            _stopLoss = signal.StopLoss;

        var equity = await GetAccountBalanceAsync();
        UpdateEquity(equity);

        if (remainingQuantity <= 0)
        {
            _currentPosition = 0;
            _entryPrice = null;
            _stopLoss = null;
            _takeProfit = null;
            RiskManager.RemovePosition(Settings.Symbol);
            await CancelOcoOrderAsync();
            return;
        }

        RiskManager.UpdatePositionAfterPartialExit(
            Settings.Symbol,
            remainingQuantity,
            _stopLoss ?? _entryPrice!.Value,
            signal.MoveStopToBreakeven,
            currentPrice);

        // Update OCO for remaining position
        if (!Settings.PaperTrade && _takeProfit.HasValue && _stopLoss.HasValue)
        {
            await UpdateTrailingStopAsync(_stopLoss.Value, _takeProfit.Value);
        }
    }

    public async Task UpdateTrailingStopAsync(decimal newStopPrice, decimal takeProfitPrice)
    {
        if (_currentPosition == 0 || Settings.PaperTrade)
        {
            if (Settings.PaperTrade)
            {
                _stopLoss = newStopPrice;
                Log($"[PAPER] Trailing stop updated to {newStopPrice:F2}");
            }
            return;
        }

        // Cancel existing OCO
        await CancelOcoOrderAsync();

        // Create new one with updated stop
        var quantity = Math.Abs(_currentPosition);
        var side = _currentPosition > 0 ? Binance.Net.Enums.OrderSide.Sell : Binance.Net.Enums.OrderSide.Buy;

        var stopLimitPrice = _currentPosition > 0
            ? newStopPrice * 0.995m
            : newStopPrice * 1.005m;

        await PlaceOcoOrderAsync(side, quantity, newStopPrice, stopLimitPrice, takeProfitPrice);

        _stopLoss = newStopPrice;
        Log($"🔄 Trailing stop updated to {newStopPrice:F2}");
    }

    private decimal CalculateTradingCosts(decimal entryPrice, decimal exitPrice, decimal quantity)
    {
        var notional = (entryPrice + exitPrice) * quantity;
        var feeCosts = notional * Settings.FeeRate;
        var slippageCosts = notional * (Settings.SlippageBps / 10000m);
        return feeCosts + slippageCosts;
    }

    public override void Dispose()
    {
        _subscription?.CloseAsync().Wait();
        _restClient.Dispose();
        _socketClient.Dispose();
    }

    // Graceful Shutdown Support Methods
    public StateManager.BotState BuildCurrentState()
    {
        var equity = Settings.PaperTrade ? _paperEquity : GetAccountBalanceAsync().Result;

        var openPositions = new List<StateManager.SavedPosition>();
        if (_currentPosition != 0 && _entryPrice.HasValue)
        {
            openPositions.Add(new StateManager.SavedPosition
            {
                Symbol = Settings.Symbol,
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
                CurrentPrice = GetCurrentPriceAsync().Result,
                BreakevenMoved = false
            });
        }

        var activeOcoOrders = new List<StateManager.SavedOcoOrder>();
        if (_currentOcoOrderListId.HasValue)
        {
            activeOcoOrders.Add(new StateManager.SavedOcoOrder
            {
                Symbol = Settings.Symbol,
                OrderListId = _currentOcoOrderListId.Value
            });
        }

        return new StateManager.BotState
        {
            LastUpdate = DateTime.UtcNow,
            CurrentEquity = equity,
            PeakEquity = RiskManager.GetTotalEquity(),
            DayStartEquity = equity,
            CurrentTradingDay = DateTime.UtcNow.Date,
            OpenPositions = openPositions,
            ActiveOcoOrders = activeOcoOrders,
            NextTradeId = 1
        };
    }

    public List<StateManager.SavedPosition> GetOpenPositions()
    {
        var positions = new List<StateManager.SavedPosition>();

        if (_currentPosition != 0 && _entryPrice.HasValue)
        {
            positions.Add(new StateManager.SavedPosition
            {
                Symbol = Settings.Symbol,
                Direction = _currentPosition > 0 ? SignalType.Buy : SignalType.Sell,
                EntryPrice = _entryPrice.Value,
                Quantity = Math.Abs(_currentPosition),
                RemainingQuantity = Math.Abs(_currentPosition),
                StopLoss = _stopLoss ?? _entryPrice.Value,
                TakeProfit = _takeProfit ?? 0,
                RiskAmount = 0,
                EntryTime = DateTime.UtcNow,
                TradeId = 0,
                CurrentPrice = GetCurrentPriceAsync().Result,
                BreakevenMoved = false
            });
        }

        return positions;
    }

    public async Task CancelOcoOrdersForSymbol(string symbol)
    {
        if (symbol == Settings.Symbol && _currentOcoOrderListId.HasValue)
        {
            await CancelOcoOrderAsync();
        }
    }

    public async Task ClosePosition(string symbol, string reason)
    {
        if (symbol == Settings.Symbol && _currentPosition != 0)
        {
            await ClosePositionAsync(reason);
        }
    }
}
