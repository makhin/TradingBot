# Trading Bot — Roadmap улучшений

> Документ составлен на основе ревью кодовой базы с точки зрения опытного трейдера.
> Дата: 2026-01-01

## Оглавление

1. [Текущее состояние](#текущее-состояние)
2. [Фаза 1: Критические улучшения](#фаза-1-критические-улучшения-обязательно-до-live)
3. [Фаза 2: Операционная надёжность](#фаза-2-операционная-надёжность)
4. [Фаза 3: Улучшение стратегии](#фаза-3-улучшение-стратегии)
5. [Фаза 4: Продвинутые фичи](#фаза-4-продвинутые-фичи)
6. [Приоритетный план действий](#приоритетный-план-действий)

---

## Текущее состояние

### Архитектура
- **Стратегия**: ADX Trend Following с подтверждением через EMA, MACD, OBV, Volume
- **Risk Management**: Drawdown-adjusted sizing (правило Джерри Паркера)
- **Бэктестинг**: Walk-Forward Analysis, Monte Carlo, Parameter Optimization
- **Торговля**: Spot Binance (testnet/mainnet), Paper Trading

### Сильные стороны
- ✅ Профессиональная архитектура кода
- ✅ Research-backed параметры (ADX 25, Volume 1.5x, EMA 20/50)
- ✅ Sophisticated risk management с portfolio heat tracking
- ✅ Продвинутый бэктестинг с OOS валидацией

### Текущие ограничения
- ❌ Нет дневного лимита потерь
- ❌ Только маркет ордера (большой slippage)
- ❌ Нет persistence состояния
- ❌ Нет журнала сделок
- ❌ Нет unit tests

---

## Фаза 1: Критические улучшения (обязательно до live)

### 1.1 Дневной лимит потерь

**Проблема**: Сейчас есть только общий circuit breaker на 20% drawdown. За один волатильный день можно потерять значительную часть капитала.

**Решение**: Добавить `MaxDailyDrawdownPercent` в `RiskSettings`.

**Файлы для изменения**:
- `ComplexBot/Services/RiskManagement/RiskManager.cs`
- `ComplexBot/Models/Models.cs`

**Пример реализации**:

```csharp
// В RiskSettings добавить:
public record RiskSettings
{
    // ... существующие поля ...
    public decimal MaxDailyDrawdownPercent { get; init; } = 3m; // 3% дневной лимит
}

// В RiskManager добавить:
private decimal _dayStartEquity;
private DateTime _currentTradingDay;

public void ResetDailyTracking()
{
    var today = DateTime.UtcNow.Date;
    if (_currentTradingDay != today)
    {
        _dayStartEquity = _currentEquity;
        _currentTradingDay = today;
    }
}

public decimal GetDailyDrawdownPercent()
{
    ResetDailyTracking();
    if (_dayStartEquity <= 0) return 0;
    return (_dayStartEquity - _currentEquity) / _dayStartEquity * 100;
}

public bool IsDailyLimitExceeded()
{
    return GetDailyDrawdownPercent() >= _settings.MaxDailyDrawdownPercent;
}

// Модифицировать CanOpenPosition():
public bool CanOpenPosition()
{
    if (IsDailyLimitExceeded())
    {
        Console.WriteLine($"⛔ Daily loss limit exceeded: {GetDailyDrawdownPercent():F2}%");
        return false;
    }
    // ... остальные проверки ...
}
```

**Тестирование**:
1. Установить `MaxDailyDrawdownPercent = 1%`
2. Выполнить серию убыточных сделок
3. Проверить блокировку новых позиций после превышения лимита
4. Проверить сброс счётчика в полночь UTC

---

### 1.2 Журнал сделок (Trade Journal CSV Export)

**Проблема**: Нет детального лога сделок для анализа. Невозможно понять какие фильтры работают лучше.

**Решение**: Создать `TradeJournal` сервис с экспортом в CSV.

**Файлы для создания/изменения**:
- `ComplexBot/Services/Analytics/TradeJournal.cs` (новый)
- `ComplexBot/Models/Models.cs`

**Структура записи журнала**:

```csharp
public record TradeJournalEntry
{
    public int TradeId { get; init; }
    public DateTime EntryTime { get; init; }
    public DateTime? ExitTime { get; init; }
    public string Symbol { get; init; } = "";
    public SignalType Direction { get; init; }  // Buy/Sell

    // Цены
    public decimal EntryPrice { get; init; }
    public decimal? ExitPrice { get; init; }
    public decimal StopLoss { get; init; }
    public decimal TakeProfit { get; init; }

    // Размер позиции
    public decimal Quantity { get; init; }
    public decimal PositionValueUsd { get; init; }
    public decimal RiskAmount { get; init; }

    // Результат
    public decimal? GrossPnL { get; init; }
    public decimal? NetPnL { get; init; }  // После комиссий
    public decimal? RMultiple { get; init; }  // PnL / RiskAmount
    public TradeResult? Result { get; init; }  // Win/Loss/Breakeven

    // Индикаторы на момент входа
    public decimal AdxValue { get; init; }
    public decimal PlusDi { get; init; }
    public decimal MinusDi { get; init; }
    public decimal FastEma { get; init; }
    public decimal SlowEma { get; init; }
    public decimal Atr { get; init; }
    public decimal MacdHistogram { get; init; }
    public decimal VolumeRatio { get; init; }  // CurrentVol / AvgVol
    public decimal ObvSlope { get; init; }

    // Причины входа/выхода
    public string EntryReason { get; init; } = "";
    public string ExitReason { get; init; } = "";

    // Время в сделке
    public int BarsInTrade { get; init; }
    public TimeSpan? Duration { get; init; }

    // MAE/MFE (Maximum Adverse/Favorable Excursion)
    public decimal? MaxAdverseExcursion { get; init; }  // Худшая точка
    public decimal? MaxFavorableExcursion { get; init; }  // Лучшая точка
}

public enum TradeResult { Win, Loss, Breakeven }
```

**Пример реализации сервиса**:

```csharp
public class TradeJournal
{
    private readonly List<TradeJournalEntry> _entries = new();
    private readonly string _outputPath;
    private int _nextTradeId = 1;

    public TradeJournal(string outputPath = "trades")
    {
        _outputPath = outputPath;
        Directory.CreateDirectory(_outputPath);
    }

    public int OpenTrade(TradeJournalEntry entry)
    {
        var tradeId = _nextTradeId++;
        _entries.Add(entry with { TradeId = tradeId });
        return tradeId;
    }

    public void CloseTrade(int tradeId, TradeJournalEntry updates)
    {
        var index = _entries.FindIndex(e => e.TradeId == tradeId);
        if (index >= 0)
        {
            _entries[index] = _entries[index] with
            {
                ExitTime = updates.ExitTime,
                ExitPrice = updates.ExitPrice,
                GrossPnL = updates.GrossPnL,
                NetPnL = updates.NetPnL,
                RMultiple = updates.RMultiple,
                Result = updates.Result,
                ExitReason = updates.ExitReason,
                BarsInTrade = updates.BarsInTrade,
                Duration = updates.Duration,
                MaxAdverseExcursion = updates.MaxAdverseExcursion,
                MaxFavorableExcursion = updates.MaxFavorableExcursion
            };
        }
    }

    public void ExportToCsv(string? filename = null)
    {
        filename ??= $"trades_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv";
        var path = Path.Combine(_outputPath, filename);

        using var writer = new StreamWriter(path);

        // Header
        writer.WriteLine("TradeId,EntryTime,ExitTime,Symbol,Direction," +
            "EntryPrice,ExitPrice,StopLoss,TakeProfit," +
            "Quantity,PositionValue,RiskAmount," +
            "GrossPnL,NetPnL,RMultiple,Result," +
            "ADX,+DI,-DI,FastEMA,SlowEMA,ATR,MACD_Hist,VolumeRatio,OBV_Slope," +
            "EntryReason,ExitReason,BarsInTrade,Duration,MAE,MFE");

        foreach (var e in _entries)
        {
            writer.WriteLine($"{e.TradeId},{e.EntryTime:O},{e.ExitTime:O},{e.Symbol},{e.Direction}," +
                $"{e.EntryPrice},{e.ExitPrice},{e.StopLoss},{e.TakeProfit}," +
                $"{e.Quantity},{e.PositionValueUsd},{e.RiskAmount}," +
                $"{e.GrossPnL},{e.NetPnL},{e.RMultiple},{e.Result}," +
                $"{e.AdxValue},{e.PlusDi},{e.MinusDi},{e.FastEma},{e.SlowEma},{e.Atr},{e.MacdHistogram},{e.VolumeRatio},{e.ObvSlope}," +
                $"\"{e.EntryReason}\",\"{e.ExitReason}\",{e.BarsInTrade},{e.Duration?.TotalHours:F1}h,{e.MaxAdverseExcursion},{e.MaxFavorableExcursion}");
        }

        Console.WriteLine($"📊 Trade journal exported: {path}");
    }

    public TradeJournalStats GetStats()
    {
        var closed = _entries.Where(e => e.ExitTime.HasValue).ToList();
        var wins = closed.Count(e => e.Result == TradeResult.Win);
        var losses = closed.Count(e => e.Result == TradeResult.Loss);

        return new TradeJournalStats
        {
            TotalTrades = closed.Count,
            WinRate = closed.Count > 0 ? (decimal)wins / closed.Count * 100 : 0,
            AverageRMultiple = closed.Average(e => e.RMultiple ?? 0),
            TotalNetPnL = closed.Sum(e => e.NetPnL ?? 0),
            AverageWin = closed.Where(e => e.Result == TradeResult.Win).Average(e => e.NetPnL ?? 0),
            AverageLoss = closed.Where(e => e.Result == TradeResult.Loss).Average(e => e.NetPnL ?? 0),
            LargestWin = closed.Max(e => e.NetPnL ?? 0),
            LargestLoss = closed.Min(e => e.NetPnL ?? 0),
            AverageBarsInTrade = closed.Average(e => e.BarsInTrade)
        };
    }
}

public record TradeJournalStats
{
    public int TotalTrades { get; init; }
    public decimal WinRate { get; init; }
    public decimal AverageRMultiple { get; init; }
    public decimal TotalNetPnL { get; init; }
    public decimal AverageWin { get; init; }
    public decimal AverageLoss { get; init; }
    public decimal LargestWin { get; init; }
    public decimal LargestLoss { get; init; }
    public double AverageBarsInTrade { get; init; }
}
```

**Пример использования**:

```csharp
var journal = new TradeJournal("./trade_logs");

// При открытии сделки
var tradeId = journal.OpenTrade(new TradeJournalEntry
{
    EntryTime = DateTime.UtcNow,
    Symbol = "BTCUSDT",
    Direction = SignalType.Buy,
    EntryPrice = 45000m,
    StopLoss = 43500m,
    TakeProfit = 47250m,
    Quantity = 0.1m,
    PositionValueUsd = 4500m,
    RiskAmount = 150m,
    AdxValue = 28.5m,
    // ... остальные индикаторы
    EntryReason = "ADX>25, EMA cross, Volume spike 1.8x"
});

// При закрытии сделки
journal.CloseTrade(tradeId, new TradeJournalEntry
{
    ExitTime = DateTime.UtcNow,
    ExitPrice = 46500m,
    GrossPnL = 150m,
    NetPnL = 141m,
    RMultiple = 1.0m,
    Result = TradeResult.Win,
    ExitReason = "Take profit hit",
    BarsInTrade = 12,
    MaxAdverseExcursion = -75m,
    MaxFavorableExcursion = 200m
});

// Экспорт
journal.ExportToCsv();
var stats = journal.GetStats();
Console.WriteLine($"Win Rate: {stats.WinRate:F1}%, Avg R: {stats.AverageRMultiple:F2}");
```

---

### 1.3 Биржевые стоп-ордера (Exchange-Side Stop Orders)

**Проблема**: Сейчас стопы программные — если бот отключится, позиция останется без защиты.

**Решение**: Использовать OCO (One-Cancels-Other) ордера Binance.

**Файлы для изменения**:
- `ComplexBot/Services/Trading/BinanceLiveTrader.cs`

**Пример реализации**:

```csharp
public async Task<bool> PlaceOcoOrder(
    string symbol,
    OrderSide side,
    decimal quantity,
    decimal stopLossPrice,
    decimal stopLossLimitPrice,
    decimal takeProfitPrice)
{
    try
    {
        // OCO ордер: стоп-лосс + тейк-профит, один отменяет другой
        var result = await _client.SpotApi.Trading.PlaceOcoOrderAsync(
            symbol: symbol,
            side: side,
            quantity: quantity,
            price: takeProfitPrice,           // Limit order (take profit)
            stopPrice: stopLossPrice,          // Stop trigger price
            stopLimitPrice: stopLossLimitPrice, // Stop limit price
            stopLimitTimeInForce: TimeInForce.GoodTillCanceled
        );

        if (result.Success)
        {
            Console.WriteLine($"✅ OCO Order placed:");
            Console.WriteLine($"   Take Profit: {takeProfitPrice}");
            Console.WriteLine($"   Stop Loss: {stopLossPrice} (limit: {stopLossLimitPrice})");
            Console.WriteLine($"   Order List ID: {result.Data.OrderListId}");
            return true;
        }
        else
        {
            Console.WriteLine($"❌ OCO Order failed: {result.Error?.Message}");
            return false;
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"❌ OCO Order exception: {ex.Message}");
        return false;
    }
}

// Модифицированный метод входа в позицию:
public async Task<bool> EnterPosition(TradeSignal signal)
{
    // 1. Открыть позицию маркет ордером
    var marketOrder = await PlaceMarketOrder(signal.Symbol,
        signal.Type == SignalType.Buy ? OrderSide.Buy : OrderSide.Sell,
        _positionQuantity);

    if (!marketOrder) return false;

    // 2. Сразу выставить OCO для защиты
    var exitSide = signal.Type == SignalType.Buy ? OrderSide.Sell : OrderSide.Buy;
    var stopLimitPrice = signal.StopLoss!.Value * (signal.Type == SignalType.Buy ? 0.995m : 1.005m);

    await PlaceOcoOrder(
        signal.Symbol,
        exitSide,
        _positionQuantity,
        signal.StopLoss!.Value,
        stopLimitPrice,
        signal.TakeProfit!.Value
    );

    return true;
}
```

**Важно**: При изменении trailing stop нужно отменять старый OCO и создавать новый.

```csharp
public async Task UpdateTrailingStop(string symbol, decimal newStopPrice, decimal takeProfitPrice)
{
    // 1. Отменить существующий OCO
    await CancelOcoOrder(symbol, _currentOcoOrderListId);

    // 2. Создать новый с обновлённым стопом
    var stopLimitPrice = newStopPrice * 0.995m; // 0.5% ниже для лонга
    await PlaceOcoOrder(symbol, OrderSide.Sell, _positionQuantity,
        newStopPrice, stopLimitPrice, takeProfitPrice);
}
```

---

### 1.4 Telegram уведомления

**Проблема**: Нет оперативных уведомлений о сделках и критических событиях.

**Решение**: Интегрировать Telegram Bot API.

**Файлы для создания**:
- `ComplexBot/Services/Notifications/TelegramNotifier.cs`

**NuGet пакет**: `Telegram.Bot`

**Пример реализации**:

```csharp
using Telegram.Bot;
using Telegram.Bot.Types.Enums;

public class TelegramNotifier
{
    private readonly TelegramBotClient _bot;
    private readonly long _chatId;
    private readonly bool _enabled;

    public TelegramNotifier(string? botToken, long chatId)
    {
        _enabled = !string.IsNullOrEmpty(botToken);
        if (_enabled)
        {
            _bot = new TelegramBotClient(botToken!);
            _chatId = chatId;
        }
    }

    public async Task SendTradeOpen(TradeSignal signal, decimal quantity, decimal riskAmount)
    {
        if (!_enabled) return;

        var emoji = signal.Type == SignalType.Buy ? "🟢" : "🔴";
        var direction = signal.Type == SignalType.Buy ? "LONG" : "SHORT";

        var message = $"""
            {emoji} *NEW TRADE OPENED*

            *{signal.Symbol}* {direction}

            📍 Entry: `{signal.Price:F2}`
            🛑 Stop Loss: `{signal.StopLoss:F2}`
            🎯 Take Profit: `{signal.TakeProfit:F2}`

            📊 Size: `{quantity:F4}`
            💰 Risk: `${riskAmount:F2}`

            📝 _{signal.Reason}_
            """;

        await SendMessage(message);
    }

    public async Task SendTradeClose(string symbol, decimal entryPrice, decimal exitPrice,
        decimal pnl, decimal rMultiple, string reason)
    {
        if (!_enabled) return;

        var emoji = pnl >= 0 ? "✅" : "❌";
        var pnlEmoji = pnl >= 0 ? "📈" : "📉";

        var message = $"""
            {emoji} *TRADE CLOSED*

            *{symbol}*

            📍 Entry: `{entryPrice:F2}`
            📍 Exit: `{exitPrice:F2}`

            {pnlEmoji} PnL: `${pnl:F2}` ({rMultiple:F2}R)

            📝 _{reason}_
            """;

        await SendMessage(message);
    }

    public async Task SendDrawdownAlert(decimal currentDrawdown, decimal dailyDrawdown)
    {
        if (!_enabled) return;

        var message = $"""
            ⚠️ *DRAWDOWN ALERT*

            📉 Total Drawdown: `{currentDrawdown:F2}%`
            📉 Daily Drawdown: `{dailyDrawdown:F2}%`

            _Risk management may reduce position sizes_
            """;

        await SendMessage(message);
    }

    public async Task SendCircuitBreakerTriggered(string reason)
    {
        if (!_enabled) return;

        var message = $"""
            🚨 *CIRCUIT BREAKER TRIGGERED*

            ⛔ Trading has been stopped!

            Reason: _{reason}_

            _Manual intervention required_
            """;

        await SendMessage(message);
    }

    public async Task SendDailySummary(TradeJournalStats stats, decimal equity, decimal drawdown)
    {
        if (!_enabled) return;

        var message = $"""
            📊 *DAILY SUMMARY*

            💰 Equity: `${equity:F2}`
            📉 Drawdown: `{drawdown:F2}%`

            📈 Trades Today: `{stats.TotalTrades}`
            🎯 Win Rate: `{stats.WinRate:F1}%`
            💵 Net PnL: `${stats.TotalNetPnL:F2}`

            Best Trade: `${stats.LargestWin:F2}`
            Worst Trade: `${stats.LargestLoss:F2}`
            """;

        await SendMessage(message);
    }

    private async Task SendMessage(string message)
    {
        try
        {
            await _bot.SendTextMessageAsync(
                chatId: _chatId,
                text: message,
                parseMode: ParseMode.Markdown
            );
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Telegram error: {ex.Message}");
        }
    }
}
```

**Конфигурация**:

```csharp
// В appsettings.json или environment variables:
{
    "Telegram": {
        "BotToken": "123456789:ABCdefGHIjklMNOpqrsTUVwxyz",
        "ChatId": -1001234567890
    }
}

// Как получить:
// 1. Создать бота через @BotFather в Telegram
// 2. Получить token
// 3. Добавить бота в группу или написать ему /start
// 4. Получить chat_id через https://api.telegram.org/bot<TOKEN>/getUpdates
```

---

### 1.5 Реалтайм equity tracking

**Проблема**: Equity обновляется только при закрытии сделки. Нереализованный P&L не учитывается.

**Решение**: Добавить floating P&L в RiskManager.

**Пример реализации**:

```csharp
public class RiskManager
{
    // ... существующий код ...

    private readonly Dictionary<string, OpenPosition> _openPositions = new();

    public record OpenPosition
    {
        public string Symbol { get; init; } = "";
        public SignalType Direction { get; init; }
        public decimal EntryPrice { get; init; }
        public decimal Quantity { get; init; }
        public decimal StopLoss { get; init; }
        public decimal CurrentPrice { get; set; }
    }

    public void UpdatePositionPrice(string symbol, decimal currentPrice)
    {
        if (_openPositions.TryGetValue(symbol, out var position))
        {
            position.CurrentPrice = currentPrice;
        }
    }

    public decimal GetUnrealizedPnL()
    {
        decimal total = 0;
        foreach (var pos in _openPositions.Values)
        {
            var pnl = pos.Direction == SignalType.Buy
                ? (pos.CurrentPrice - pos.EntryPrice) * pos.Quantity
                : (pos.EntryPrice - pos.CurrentPrice) * pos.Quantity;
            total += pnl;
        }
        return total;
    }

    public decimal GetTotalEquity()
    {
        return _currentEquity + GetUnrealizedPnL();
    }

    public decimal GetTotalDrawdownPercent()
    {
        var totalEquity = GetTotalEquity();
        if (_peakEquity <= 0) return 0;
        return (_peakEquity - totalEquity) / _peakEquity * 100;
    }

    // Обновлённая проверка с учётом floating P&L
    public bool CanOpenPosition()
    {
        var totalDrawdown = GetTotalDrawdownPercent();
        if (totalDrawdown >= _settings.MaxDrawdownPercent)
        {
            Console.WriteLine($"⛔ Max drawdown exceeded (including unrealized): {totalDrawdown:F2}%");
            return false;
        }

        if (IsDailyLimitExceeded())
        {
            return false;
        }

        if (GetPortfolioHeatPercent() >= _settings.MaxPortfolioHeatPercent)
        {
            return false;
        }

        return true;
    }
}
```

---

### 1.6 Валидация исполнения ордеров

**Проблема**: Нет проверки фактической цены исполнения vs ожидаемой.

**Решение**: Добавить slippage validation.

```csharp
public class ExecutionValidator
{
    private readonly decimal _maxSlippagePercent;

    public ExecutionValidator(decimal maxSlippagePercent = 1.0m)
    {
        _maxSlippagePercent = maxSlippagePercent;
    }

    public record ExecutionResult
    {
        public bool IsAcceptable { get; init; }
        public decimal ExpectedPrice { get; init; }
        public decimal ActualPrice { get; init; }
        public decimal SlippagePercent { get; init; }
        public string? RejectReason { get; init; }
    }

    public ExecutionResult ValidateExecution(
        decimal expectedPrice,
        decimal actualPrice,
        OrderSide side)
    {
        var slippage = side == OrderSide.Buy
            ? (actualPrice - expectedPrice) / expectedPrice * 100
            : (expectedPrice - actualPrice) / expectedPrice * 100;

        var isAcceptable = slippage <= _maxSlippagePercent;

        return new ExecutionResult
        {
            IsAcceptable = isAcceptable,
            ExpectedPrice = expectedPrice,
            ActualPrice = actualPrice,
            SlippagePercent = slippage,
            RejectReason = isAcceptable ? null : $"Slippage {slippage:F2}% exceeds max {_maxSlippagePercent}%"
        };
    }
}

// Использование:
var validator = new ExecutionValidator(maxSlippagePercent: 0.5m);

var order = await PlaceMarketOrder(symbol, OrderSide.Buy, quantity);
var validation = validator.ValidateExecution(
    expectedPrice: signal.Price,
    actualPrice: order.AveragePrice,
    side: OrderSide.Buy
);

if (!validation.IsAcceptable)
{
    Console.WriteLine($"⚠️ Bad execution: {validation.RejectReason}");
    // Опционально: закрыть позицию немедленно
    await ClosePosition(symbol);
}
```

---

### 1.7 Лимитные ордера для входа

**Проблема**: Маркет ордера дают плохую цену в волатильности.

**Решение**: Использовать лимитные ордера с таймаутом.

```csharp
public async Task<OrderResult?> PlaceLimitOrderWithTimeout(
    string symbol,
    OrderSide side,
    decimal quantity,
    decimal limitPrice,
    TimeSpan timeout)
{
    // 1. Разместить лимитный ордер
    var order = await _client.SpotApi.Trading.PlaceOrderAsync(
        symbol: symbol,
        side: side,
        type: SpotOrderType.Limit,
        quantity: quantity,
        price: limitPrice,
        timeInForce: TimeInForce.GoodTillCanceled
    );

    if (!order.Success)
    {
        Console.WriteLine($"❌ Limit order failed: {order.Error?.Message}");
        return null;
    }

    var orderId = order.Data.Id;
    var startTime = DateTime.UtcNow;

    // 2. Ждать исполнения с таймаутом
    while (DateTime.UtcNow - startTime < timeout)
    {
        await Task.Delay(1000);

        var status = await _client.SpotApi.Trading.GetOrderAsync(symbol, orderId);
        if (status.Success)
        {
            if (status.Data.Status == OrderStatus.Filled)
            {
                Console.WriteLine($"✅ Limit order filled at {status.Data.AverageFillPrice}");
                return new OrderResult
                {
                    OrderId = orderId,
                    FilledQuantity = status.Data.QuantityFilled,
                    AveragePrice = status.Data.AverageFillPrice ?? limitPrice,
                    Status = OrderStatus.Filled
                };
            }
            else if (status.Data.Status == OrderStatus.PartiallyFilled)
            {
                Console.WriteLine($"⏳ Partially filled: {status.Data.QuantityFilled}/{quantity}");
            }
        }
    }

    // 3. Таймаут — отменить ордер
    Console.WriteLine($"⏰ Limit order timeout, cancelling...");
    await _client.SpotApi.Trading.CancelOrderAsync(symbol, orderId);

    // 4. Проверить частичное исполнение
    var finalStatus = await _client.SpotApi.Trading.GetOrderAsync(symbol, orderId);
    if (finalStatus.Success && finalStatus.Data.QuantityFilled > 0)
    {
        return new OrderResult
        {
            OrderId = orderId,
            FilledQuantity = finalStatus.Data.QuantityFilled,
            AveragePrice = finalStatus.Data.AverageFillPrice ?? limitPrice,
            Status = OrderStatus.PartiallyFilled
        };
    }

    return null;
}

// Использование с fallback на маркет:
public async Task<bool> EnterPositionSmart(TradeSignal signal, decimal quantity)
{
    // Попробовать лимитный ордер с улучшением цены на 0.1%
    var limitPrice = signal.Type == SignalType.Buy
        ? signal.Price * 0.999m  // Чуть ниже текущей
        : signal.Price * 1.001m; // Чуть выше текущей

    var result = await PlaceLimitOrderWithTimeout(
        signal.Symbol,
        signal.Type == SignalType.Buy ? OrderSide.Buy : OrderSide.Sell,
        quantity,
        limitPrice,
        timeout: TimeSpan.FromSeconds(30)
    );

    if (result?.Status == OrderStatus.Filled)
    {
        return true;
    }

    // Fallback на маркет если лимит не сработал
    if (result?.Status == OrderStatus.PartiallyFilled)
    {
        var remaining = quantity - result.FilledQuantity;
        await PlaceMarketOrder(signal.Symbol,
            signal.Type == SignalType.Buy ? OrderSide.Buy : OrderSide.Sell,
            remaining);
    }
    else
    {
        await PlaceMarketOrder(signal.Symbol,
            signal.Type == SignalType.Buy ? OrderSide.Buy : OrderSide.Sell,
            quantity);
    }

    return true;
}
```

---

### 1.8 Минимальный баланс для торговли

**Проблема**: Нет проверки минимального баланса. Можно торговать с околонулевым счётом.

**Решение**: Добавить `MinimumEquity` в RiskSettings.

```csharp
public record RiskSettings
{
    // ... существующие поля ...
    public decimal MinimumEquityUsd { get; init; } = 100m;  // Минимум $100
}

public bool CanOpenPosition()
{
    if (_currentEquity < _settings.MinimumEquityUsd)
    {
        Console.WriteLine($"⛔ Equity below minimum: ${_currentEquity:F2} < ${_settings.MinimumEquityUsd}");
        return false;
    }
    // ... остальные проверки ...
}
```

---

### 1.9 Корреляция позиций (Multi-Symbol)

**Проблема**: При торговле несколькими парами нет агрегированного контроля.

**Решение**: Portfolio-level risk management.

```csharp
public class PortfolioRiskManager
{
    private readonly Dictionary<string, RiskManager> _symbolManagers = new();
    private readonly decimal _maxTotalDrawdownPercent;
    private readonly decimal _maxCorrelatedRiskPercent;

    private decimal _totalPeakEquity;
    private decimal _totalCurrentEquity;

    // Группы коррелированных активов
    private readonly Dictionary<string, string[]> _correlationGroups = new()
    {
        ["BTC_CORRELATED"] = new[] { "BTCUSDT", "ETHUSDT", "BNBUSDT" },
        ["ALTCOINS"] = new[] { "SOLUSDT", "ADAUSDT", "DOTUSDT" },
        ["STABLES"] = new[] { "USDCUSDT", "BUSDUSDT" }
    };

    public PortfolioRiskManager(decimal maxTotalDrawdown = 25m, decimal maxCorrelatedRisk = 10m)
    {
        _maxTotalDrawdownPercent = maxTotalDrawdown;
        _maxCorrelatedRiskPercent = maxCorrelatedRisk;
    }

    public decimal GetCorrelatedRisk(string symbol)
    {
        // Найти группу символа
        var group = _correlationGroups
            .FirstOrDefault(g => g.Value.Contains(symbol));

        if (group.Key == null) return 0;

        // Суммировать риск по всем символам группы
        decimal totalRisk = 0;
        foreach (var correlatedSymbol in group.Value)
        {
            if (_symbolManagers.TryGetValue(correlatedSymbol, out var manager))
            {
                totalRisk += manager.GetPortfolioHeatPercent();
            }
        }

        return totalRisk;
    }

    public bool CanOpenPosition(string symbol)
    {
        // 1. Проверить общий drawdown портфеля
        var totalDrawdown = GetTotalDrawdownPercent();
        if (totalDrawdown >= _maxTotalDrawdownPercent)
        {
            Console.WriteLine($"⛔ Portfolio drawdown exceeded: {totalDrawdown:F2}%");
            return false;
        }

        // 2. Проверить корреляционный риск
        var correlatedRisk = GetCorrelatedRisk(symbol);
        if (correlatedRisk >= _maxCorrelatedRiskPercent)
        {
            Console.WriteLine($"⛔ Correlated risk too high for {symbol}: {correlatedRisk:F2}%");
            return false;
        }

        // 3. Делегировать проверку конкретному менеджеру
        if (_symbolManagers.TryGetValue(symbol, out var manager))
        {
            return manager.CanOpenPosition();
        }

        return true;
    }

    public decimal GetTotalDrawdownPercent()
    {
        if (_totalPeakEquity <= 0) return 0;
        return (_totalPeakEquity - _totalCurrentEquity) / _totalPeakEquity * 100;
    }

    public void UpdateEquity(string symbol, decimal equity)
    {
        if (_symbolManagers.TryGetValue(symbol, out var manager))
        {
            // ... update logic
        }

        RecalculateTotalEquity();
    }

    private void RecalculateTotalEquity()
    {
        _totalCurrentEquity = _symbolManagers.Values.Sum(m => m.GetTotalEquity());
        _totalPeakEquity = Math.Max(_totalPeakEquity, _totalCurrentEquity);
    }
}
```

---

## Фаза 2: Операционная надёжность

### 2.1 State Persistence (Сохранение состояния)

**Проблема**: При перезапуске бота теряется информация об открытых позициях.

**Решение**: Сохранять состояние в JSON файл.

```csharp
public class StateManager
{
    private readonly string _statePath;

    public record BotState
    {
        public DateTime LastUpdate { get; init; }
        public decimal CurrentEquity { get; init; }
        public decimal PeakEquity { get; init; }
        public List<SavedPosition> OpenPositions { get; init; } = new();
        public List<SavedOcoOrder> ActiveOcoOrders { get; init; } = new();
        public int NextTradeId { get; init; }
    }

    public record SavedPosition
    {
        public string Symbol { get; init; } = "";
        public SignalType Direction { get; init; }
        public decimal EntryPrice { get; init; }
        public decimal Quantity { get; init; }
        public decimal StopLoss { get; init; }
        public decimal TakeProfit { get; init; }
        public DateTime EntryTime { get; init; }
        public int TradeId { get; init; }
    }

    public record SavedOcoOrder
    {
        public string Symbol { get; init; } = "";
        public long OrderListId { get; init; }
    }

    public StateManager(string statePath = "bot_state.json")
    {
        _statePath = statePath;
    }

    public async Task SaveState(BotState state)
    {
        var json = JsonSerializer.Serialize(state, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        await File.WriteAllTextAsync(_statePath, json);
        Console.WriteLine($"💾 State saved: {state.OpenPositions.Count} positions");
    }

    public async Task<BotState?> LoadState()
    {
        if (!File.Exists(_statePath))
        {
            Console.WriteLine("📂 No saved state found, starting fresh");
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(_statePath);
            var state = JsonSerializer.Deserialize<BotState>(json);
            Console.WriteLine($"📂 State loaded: {state?.OpenPositions.Count ?? 0} positions");
            return state;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️ Failed to load state: {ex.Message}");
            return null;
        }
    }

    public async Task ReconcileWithExchange(IBinanceRestClient client, BotState state)
    {
        Console.WriteLine("🔄 Reconciling state with exchange...");

        foreach (var savedPos in state.OpenPositions)
        {
            // Проверить баланс на бирже
            var balance = await client.SpotApi.Account.GetBalancesAsync();
            var asset = savedPos.Symbol.Replace("USDT", "");
            var actualBalance = balance.Data.FirstOrDefault(b => b.Asset == asset);

            if (actualBalance?.Available >= savedPos.Quantity * 0.99m)
            {
                Console.WriteLine($"✅ Position confirmed: {savedPos.Symbol} {savedPos.Quantity}");
            }
            else
            {
                Console.WriteLine($"⚠️ Position mismatch: {savedPos.Symbol}");
                Console.WriteLine($"   Expected: {savedPos.Quantity}, Actual: {actualBalance?.Available ?? 0}");
            }
        }

        // Проверить OCO ордера
        foreach (var oco in state.ActiveOcoOrders)
        {
            var ocoStatus = await client.SpotApi.Trading.GetOcoOrderAsync(
                orderListId: oco.OrderListId);

            if (ocoStatus.Success)
            {
                Console.WriteLine($"✅ OCO order active: {oco.Symbol} #{oco.OrderListId}");
            }
            else
            {
                Console.WriteLine($"⚠️ OCO order not found: {oco.Symbol} #{oco.OrderListId}");
            }
        }
    }
}

// Использование при запуске:
var stateManager = new StateManager();
var savedState = await stateManager.LoadState();

if (savedState != null)
{
    await stateManager.ReconcileWithExchange(binanceClient, savedState);
    riskManager.RestoreFromState(savedState);
}

// Сохранение при каждом изменении:
async Task OnPositionOpened(SavedPosition position)
{
    var state = BuildCurrentState();
    await stateManager.SaveState(state);
}
```

---

### 2.2 Reconnection Logic

**Проблема**: Ограниченная логика переподключения при потере связи.

**Решение**: Exponential backoff с health checks.

```csharp
public class ConnectionManager
{
    private readonly IBinanceSocketClient _socketClient;
    private readonly int[] _backoffDelays = { 1000, 2000, 4000, 8000, 16000, 32000 };
    private int _reconnectAttempt = 0;
    private bool _isConnected = false;

    public event Action? OnConnected;
    public event Action<string>? OnDisconnected;
    public event Action<Exception>? OnError;

    public async Task<bool> ConnectWithRetry(string symbol,
        Action<DataEvent<IBinanceStreamKlineData>> onKline)
    {
        while (_reconnectAttempt < _backoffDelays.Length)
        {
            try
            {
                Console.WriteLine($"🔌 Connecting to {symbol} stream (attempt {_reconnectAttempt + 1})...");

                var result = await _socketClient.SpotApi.ExchangeData
                    .SubscribeToKlineUpdatesAsync(
                        symbol,
                        KlineInterval.FourHour,
                        onKline
                    );

                if (result.Success)
                {
                    _isConnected = true;
                    _reconnectAttempt = 0;
                    Console.WriteLine($"✅ Connected to {symbol} stream");
                    OnConnected?.Invoke();

                    // Настроить обработку отключения
                    result.Data.ConnectionLost += () =>
                    {
                        _isConnected = false;
                        OnDisconnected?.Invoke("Connection lost");
                        _ = ReconnectAsync(symbol, onKline);
                    };

                    return true;
                }
                else
                {
                    throw new Exception(result.Error?.Message ?? "Unknown error");
                }
            }
            catch (Exception ex)
            {
                OnError?.Invoke(ex);
                var delay = _backoffDelays[_reconnectAttempt];
                Console.WriteLine($"❌ Connection failed: {ex.Message}");
                Console.WriteLine($"⏳ Retrying in {delay}ms...");

                await Task.Delay(delay);
                _reconnectAttempt++;
            }
        }

        Console.WriteLine("❌ Max reconnection attempts reached");
        return false;
    }

    private async Task ReconnectAsync(string symbol,
        Action<DataEvent<IBinanceStreamKlineData>> onKline)
    {
        Console.WriteLine("🔄 Attempting to reconnect...");
        await ConnectWithRetry(symbol, onKline);
    }

    public async Task StartHealthCheck(TimeSpan interval)
    {
        while (true)
        {
            await Task.Delay(interval);

            if (!_isConnected)
            {
                Console.WriteLine("💔 Health check: Disconnected");
                // Trigger reconnect logic
            }
            else
            {
                Console.WriteLine("💚 Health check: Connected");
            }
        }
    }
}
```

---

### 2.3 Graceful Shutdown

**Проблема**: При остановке бота позиции могут остаться открытыми без защиты.

**Решение**: Обработка SIGTERM/SIGINT с опцией закрытия позиций.

```csharp
public class GracefulShutdown
{
    private readonly CancellationTokenSource _cts = new();
    private readonly StateManager _stateManager;
    private readonly BinanceLiveTrader _trader;
    private readonly TelegramNotifier? _notifier;

    public GracefulShutdown(StateManager stateManager, BinanceLiveTrader trader,
        TelegramNotifier? notifier = null)
    {
        _stateManager = stateManager;
        _trader = trader;
        _notifier = notifier;

        // Регистрация обработчиков сигналов
        Console.CancelKeyPress += OnCancelKeyPress;
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
    }

    public CancellationToken Token => _cts.Token;

    private void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
    {
        e.Cancel = true; // Предотвратить немедленное завершение
        _ = ShutdownAsync("Ctrl+C pressed");
    }

    private void OnProcessExit(object? sender, EventArgs e)
    {
        ShutdownAsync("Process exit").Wait(TimeSpan.FromSeconds(30));
    }

    public async Task ShutdownAsync(string reason)
    {
        Console.WriteLine($"\n🛑 Initiating graceful shutdown: {reason}");

        // 1. Остановить приём новых сигналов
        _cts.Cancel();

        // 2. Сохранить текущее состояние
        Console.WriteLine("💾 Saving current state...");
        var state = _trader.BuildCurrentState();
        await _stateManager.SaveState(state);

        // 3. Спросить о закрытии позиций (если интерактивный режим)
        if (state.OpenPositions.Any() && Console.IsInputRedirected == false)
        {
            Console.WriteLine($"\n⚠️ You have {state.OpenPositions.Count} open position(s).");
            Console.WriteLine("Choose action:");
            Console.WriteLine("  1. Keep positions open (OCO orders remain active)");
            Console.WriteLine("  2. Close all positions at market");
            Console.WriteLine("  3. Close positions and cancel OCO orders");
            Console.Write("Your choice [1]: ");

            var choice = Console.ReadLine();

            switch (choice)
            {
                case "2":
                    await CloseAllPositionsAsync(cancelOco: false);
                    break;
                case "3":
                    await CloseAllPositionsAsync(cancelOco: true);
                    break;
                default:
                    Console.WriteLine("✅ Positions kept open with OCO protection");
                    break;
            }
        }

        // 4. Уведомить
        if (_notifier != null)
        {
            await _notifier.SendMessage($"🛑 Bot shutdown: {reason}");
        }

        Console.WriteLine("👋 Goodbye!");
    }

    private async Task CloseAllPositionsAsync(bool cancelOco)
    {
        Console.WriteLine("📤 Closing all positions...");

        foreach (var position in _trader.GetOpenPositions())
        {
            if (cancelOco)
            {
                await _trader.CancelOcoOrdersForSymbol(position.Symbol);
            }

            await _trader.ClosePosition(position.Symbol, "Graceful shutdown");
            Console.WriteLine($"  ✅ Closed {position.Symbol}");
        }

        Console.WriteLine("✅ All positions closed");
    }
}

// Использование:
var shutdown = new GracefulShutdown(stateManager, trader, telegramNotifier);

try
{
    await trader.StartAsync(shutdown.Token);
}
catch (OperationCanceledException)
{
    // Нормальное завершение
}
```

---

### 2.4 Unit Tests

**Проблема**: Нет unit tests для критичных компонентов.

**Решение**: Добавить тесты с xUnit.

**Структура тестов**:
```
ComplexBot.Tests/
├── RiskManagerTests.cs
├── IndicatorsTests.cs
├── AdxTrendStrategyTests.cs
├── BacktestEngineTests.cs
└── TradeJournalTests.cs
```

**Примеры тестов**:

```csharp
// RiskManagerTests.cs
public class RiskManagerTests
{
    [Fact]
    public void CalculatePositionSize_WithNormalDrawdown_ReturnsFullSize()
    {
        // Arrange
        var settings = new RiskSettings { RiskPerTradePercent = 1.5m };
        var manager = new RiskManager(settings, initialEquity: 10000m);

        // Act
        var size = manager.CalculatePositionSize(
            entryPrice: 45000m,
            stopLoss: 43500m  // 1500 USDT stop distance
        );

        // Assert
        // Risk = 10000 * 0.015 = 150 USDT
        // Size = 150 / 1500 = 0.1 BTC
        Assert.Equal(0.1m, size, precision: 4);
    }

    [Fact]
    public void CalculatePositionSize_WithDrawdown_ReducesSize()
    {
        // Arrange
        var settings = new RiskSettings { RiskPerTradePercent = 1.5m };
        var manager = new RiskManager(settings, initialEquity: 10000m);
        manager.UpdateEquity(8500m); // 15% drawdown

        // Act
        var size = manager.CalculatePositionSize(
            entryPrice: 45000m,
            stopLoss: 43500m
        );

        // Assert
        // At 15% drawdown, risk is reduced to 50%
        // Adjusted risk = 8500 * 0.015 * 0.5 = 63.75 USDT
        // Size = 63.75 / 1500 = 0.0425 BTC
        Assert.Equal(0.0425m, size, precision: 4);
    }

    [Fact]
    public void CanOpenPosition_WithExceededDrawdown_ReturnsFalse()
    {
        var settings = new RiskSettings { MaxDrawdownPercent = 20m };
        var manager = new RiskManager(settings, initialEquity: 10000m);
        manager.UpdateEquity(7900m); // 21% drawdown

        Assert.False(manager.CanOpenPosition());
    }

    [Fact]
    public void DailyDrawdown_ResetsAtMidnight()
    {
        var settings = new RiskSettings { MaxDailyDrawdownPercent = 3m };
        var manager = new RiskManager(settings, initialEquity: 10000m);

        // Simulate loss
        manager.UpdateEquity(9700m); // 3% daily loss
        Assert.True(manager.IsDailyLimitExceeded());

        // Simulate new day
        manager.SimulateNewDay(); // Internal method for testing
        Assert.False(manager.IsDailyLimitExceeded());
    }
}

// IndicatorsTests.cs
public class IndicatorsTests
{
    [Fact]
    public void CalculateEMA_WithKnownValues_ReturnsCorrectResult()
    {
        // Arrange
        var prices = new[] { 22.27m, 22.19m, 22.08m, 22.17m, 22.18m,
                            22.13m, 22.23m, 22.43m, 22.24m, 22.29m };

        // Act
        var ema = Indicators.CalculateEMA(prices.ToList(), period: 10);

        // Assert
        Assert.Equal(22.22m, ema, precision: 2);
    }

    [Fact]
    public void CalculateATR_WithGap_IncludesTrueRange()
    {
        // Arrange
        var candles = new List<Candle>
        {
            new(default, 100, 105, 98, 102, 1000, default),  // H-L = 7
            new(default, 103, 108, 101, 107, 1000, default), // Gap up, TR includes gap
        };

        // Act
        var atr = Indicators.CalculateATR(candles, period: 2);

        // Assert
        // True Range for second candle = max(108-101, |108-102|, |101-102|) = max(7, 6, 1) = 7
        Assert.True(atr > 0);
    }

    [Fact]
    public void CalculateADX_InTrend_ReturnsHighValue()
    {
        // Arrange - Create strong uptrend data
        var candles = GenerateUptrendCandles(50);

        // Act
        var (adx, plusDi, minusDi) = Indicators.CalculateADX(candles, period: 14);

        // Assert
        Assert.True(adx > 25); // Strong trend
        Assert.True(plusDi > minusDi); // Uptrend
    }

    private List<Candle> GenerateUptrendCandles(int count)
    {
        var candles = new List<Candle>();
        decimal price = 100;

        for (int i = 0; i < count; i++)
        {
            price *= 1.02m; // 2% daily increase
            candles.Add(new Candle(
                OpenTime: DateTime.UtcNow.AddDays(-count + i),
                Open: price * 0.99m,
                High: price * 1.01m,
                Low: price * 0.98m,
                Close: price,
                Volume: 1000,
                CloseTime: DateTime.UtcNow.AddDays(-count + i + 1)
            ));
        }

        return candles;
    }
}

// AdxTrendStrategyTests.cs
public class AdxTrendStrategyTests
{
    [Fact]
    public void CheckEntry_AllConditionsMet_ReturnsBuySignal()
    {
        // Arrange
        var settings = new StrategySettings();
        var strategy = new AdxTrendStrategy(settings);

        var candles = GenerateBullishSetup();
        strategy.UpdateIndicators(candles);

        // Act
        var signal = strategy.CheckEntryConditions(candles.Last());

        // Assert
        Assert.Equal(SignalType.Buy, signal.Type);
        Assert.NotNull(signal.StopLoss);
        Assert.NotNull(signal.TakeProfit);
    }

    [Fact]
    public void CheckEntry_LowADX_ReturnsNoSignal()
    {
        // Arrange - Ranging market with low ADX
        var settings = new StrategySettings { AdxThreshold = 25 };
        var strategy = new AdxTrendStrategy(settings);

        var candles = GenerateRangingMarket(); // ADX < 20
        strategy.UpdateIndicators(candles);

        // Act
        var signal = strategy.CheckEntryConditions(candles.Last());

        // Assert
        Assert.Equal(SignalType.None, signal.Type);
    }

    [Fact]
    public void CheckExit_StopLossHit_ReturnsExitSignal()
    {
        // Arrange
        var settings = new StrategySettings();
        var strategy = new AdxTrendStrategy(settings);

        strategy.OpenPosition(SignalType.Buy, entryPrice: 45000m, stopLoss: 43500m);

        var exitCandle = new Candle(default, 44000, 44100, 43400, 43600, 1000, default);

        // Act
        var signal = strategy.CheckExitConditions(exitCandle);

        // Assert
        Assert.Equal(SignalType.Exit, signal.Type);
        Assert.Contains("stop loss", signal.Reason.ToLower());
    }
}
```

---

### 2.5 Integration Tests на Testnet

```csharp
[Collection("Binance Testnet")]
public class BinanceIntegrationTests : IAsyncLifetime
{
    private BinanceLiveTrader _trader;
    private IBinanceRestClient _client;

    public async Task InitializeAsync()
    {
        var settings = new LiveTraderSettings
        {
            Symbol = "BTCUSDT",
            UseTestnet = true,
            PaperTrade = false, // Real testnet orders
            ApiKey = Environment.GetEnvironmentVariable("BINANCE_TESTNET_KEY")!,
            ApiSecret = Environment.GetEnvironmentVariable("BINANCE_TESTNET_SECRET")!
        };

        _trader = new BinanceLiveTrader(settings);
        await _trader.InitializeAsync();
    }

    public async Task DisposeAsync()
    {
        await _trader.DisposeAsync();
    }

    [Fact]
    public async Task PlaceMarketOrder_OnTestnet_Succeeds()
    {
        // Act
        var result = await _trader.PlaceMarketOrder(
            symbol: "BTCUSDT",
            side: OrderSide.Buy,
            quantity: 0.001m
        );

        // Assert
        Assert.True(result.Success);
        Assert.True(result.FilledQuantity > 0);

        // Cleanup
        await _trader.PlaceMarketOrder("BTCUSDT", OrderSide.Sell, result.FilledQuantity);
    }

    [Fact]
    public async Task PlaceOcoOrder_OnTestnet_CreatesValidOrder()
    {
        // Arrange - First buy some BTC
        await _trader.PlaceMarketOrder("BTCUSDT", OrderSide.Buy, 0.001m);
        var currentPrice = await _trader.GetCurrentPrice("BTCUSDT");

        // Act
        var result = await _trader.PlaceOcoOrder(
            symbol: "BTCUSDT",
            side: OrderSide.Sell,
            quantity: 0.001m,
            stopLossPrice: currentPrice * 0.95m,
            stopLossLimitPrice: currentPrice * 0.949m,
            takeProfitPrice: currentPrice * 1.05m
        );

        // Assert
        Assert.True(result.Success);
        Assert.True(result.OrderListId > 0);

        // Cleanup
        await _trader.CancelOcoOrder("BTCUSDT", result.OrderListId);
        await _trader.PlaceMarketOrder("BTCUSDT", OrderSide.Sell, 0.001m);
    }
}
```

---

## Фаза 3: Улучшение стратегии

### 3.1 Volatility Regime Filter

**Проблема**: Стратегия работает одинаково в разных рыночных условиях.

**Решение**: Определять режим волатильности и адаптировать параметры.

```csharp
public enum VolatilityRegime
{
    Low,      // ATR < 20 percentile
    Normal,   // 20-80 percentile
    High,     // > 80 percentile
    Extreme   // > 95 percentile (don't trade)
}

public class VolatilityFilter
{
    private readonly int _lookbackPeriod;
    private readonly List<decimal> _atrHistory = new();

    public VolatilityFilter(int lookbackPeriod = 100)
    {
        _lookbackPeriod = lookbackPeriod;
    }

    public void UpdateATR(decimal currentAtr)
    {
        _atrHistory.Add(currentAtr);
        if (_atrHistory.Count > _lookbackPeriod)
        {
            _atrHistory.RemoveAt(0);
        }
    }

    public VolatilityRegime GetCurrentRegime()
    {
        if (_atrHistory.Count < _lookbackPeriod / 2)
        {
            return VolatilityRegime.Normal; // Not enough data
        }

        var sorted = _atrHistory.OrderBy(x => x).ToList();
        var currentAtr = _atrHistory.Last();
        var percentile = (decimal)sorted.IndexOf(sorted.First(x => x >= currentAtr)) / sorted.Count * 100;

        return percentile switch
        {
            >= 95 => VolatilityRegime.Extreme,
            >= 80 => VolatilityRegime.High,
            >= 20 => VolatilityRegime.Normal,
            _ => VolatilityRegime.Low
        };
    }

    public StrategySettings AdjustSettings(StrategySettings baseSettings, VolatilityRegime regime)
    {
        return regime switch
        {
            VolatilityRegime.Extreme => baseSettings with
            {
                // Не торговать в экстремальной волатильности
            },
            VolatilityRegime.High => baseSettings with
            {
                AtrStopMultiplier = baseSettings.AtrStopMultiplier * 1.5m, // Шире стопы
                AdxThreshold = 30m, // Требовать более сильный тренд
                VolumeThreshold = 2.0m // Требовать больший объём
            },
            VolatilityRegime.Low => baseSettings with
            {
                AtrStopMultiplier = baseSettings.AtrStopMultiplier * 0.75m, // Уже стопы
                AdxThreshold = 20m // Можно входить при меньшем ADX
            },
            _ => baseSettings
        };
    }

    public bool ShouldTrade(VolatilityRegime regime)
    {
        return regime != VolatilityRegime.Extreme;
    }
}

// Использование в стратегии:
public TradeSignal CheckEntryConditions(Candle candle)
{
    var regime = _volatilityFilter.GetCurrentRegime();

    if (!_volatilityFilter.ShouldTrade(regime))
    {
        return TradeSignal.None("Extreme volatility - no trading");
    }

    var adjustedSettings = _volatilityFilter.AdjustSettings(_settings, regime);

    // Использовать adjustedSettings для проверки условий
    // ...
}
```

---

### 3.2 RSI Divergence для выхода

**Проблема**: Нет раннего предупреждения о развороте тренда.

**Решение**: Обнаружение дивергенций RSI.

```csharp
public class DivergenceDetector
{
    public enum DivergenceType
    {
        None,
        BullishRegular,    // Цена: lower low, RSI: higher low
        BearishRegular,    // Цена: higher high, RSI: lower high
        BullishHidden,     // Цена: higher low, RSI: lower low
        BearishHidden      // Цена: lower high, RSI: higher high
    }

    private readonly int _lookbackBars;
    private readonly decimal _minPriceChange;

    public DivergenceDetector(int lookbackBars = 14, decimal minPriceChangePercent = 1m)
    {
        _lookbackBars = lookbackBars;
        _minPriceChange = minPriceChangePercent;
    }

    public DivergenceType Detect(List<Candle> candles, List<decimal> rsiValues)
    {
        if (candles.Count < _lookbackBars || rsiValues.Count < _lookbackBars)
            return DivergenceType.None;

        var recentCandles = candles.TakeLast(_lookbackBars).ToList();
        var recentRsi = rsiValues.TakeLast(_lookbackBars).ToList();

        // Найти локальные экстремумы цены
        var priceHighs = FindLocalMaxima(recentCandles.Select(c => c.High).ToList());
        var priceLows = FindLocalMinima(recentCandles.Select(c => c.Low).ToList());

        // Найти локальные экстремумы RSI
        var rsiHighs = FindLocalMaxima(recentRsi);
        var rsiLows = FindLocalMinima(recentRsi);

        // Проверить bearish divergence (для выхода из лонга)
        if (priceHighs.Count >= 2 && rsiHighs.Count >= 2)
        {
            var lastPriceHigh = priceHighs.Last();
            var prevPriceHigh = priceHighs[^2];
            var lastRsiHigh = rsiHighs.Last();
            var prevRsiHigh = rsiHighs[^2];

            // Regular bearish: price higher high, RSI lower high
            if (lastPriceHigh.value > prevPriceHigh.value * (1 + _minPriceChange / 100) &&
                lastRsiHigh.value < prevRsiHigh.value)
            {
                return DivergenceType.BearishRegular;
            }
        }

        // Проверить bullish divergence (для выхода из шорта)
        if (priceLows.Count >= 2 && rsiLows.Count >= 2)
        {
            var lastPriceLow = priceLows.Last();
            var prevPriceLow = priceLows[^2];
            var lastRsiLow = rsiLows.Last();
            var prevRsiLow = rsiLows[^2];

            // Regular bullish: price lower low, RSI higher low
            if (lastPriceLow.value < prevPriceLow.value * (1 - _minPriceChange / 100) &&
                lastRsiLow.value > prevRsiLow.value)
            {
                return DivergenceType.BullishRegular;
            }
        }

        return DivergenceType.None;
    }

    private List<(int index, decimal value)> FindLocalMaxima(List<decimal> values)
    {
        var maxima = new List<(int, decimal)>();
        for (int i = 1; i < values.Count - 1; i++)
        {
            if (values[i] > values[i - 1] && values[i] > values[i + 1])
            {
                maxima.Add((i, values[i]));
            }
        }
        return maxima;
    }

    private List<(int index, decimal value)> FindLocalMinima(List<decimal> values)
    {
        var minima = new List<(int, decimal)>();
        for (int i = 1; i < values.Count - 1; i++)
        {
            if (values[i] < values[i - 1] && values[i] < values[i + 1])
            {
                minima.Add((i, values[i]));
            }
        }
        return minima;
    }
}

// Использование для выхода:
public TradeSignal CheckExitConditions(Candle candle)
{
    // ... существующие проверки ...

    var divergence = _divergenceDetector.Detect(_candles, _rsiValues);

    if (_currentPosition == SignalType.Buy &&
        divergence == DivergenceType.BearishRegular)
    {
        return new TradeSignal(
            Symbol: _symbol,
            Type: SignalType.Exit,
            Price: candle.Close,
            Reason: "Bearish RSI divergence detected"
        );
    }

    if (_currentPosition == SignalType.Sell &&
        divergence == DivergenceType.BullishRegular)
    {
        return new TradeSignal(
            Symbol: _symbol,
            Type: SignalType.Exit,
            Price: candle.Close,
            Reason: "Bullish RSI divergence detected"
        );
    }

    // ... остальные проверки ...
}
```

---

### 3.3 Множественные тейк-профиты

**Проблема**: Сейчас только один тейк-профит, упускается потенциал большого движения.

**Решение**: Ступенчатые выходы с трейлингом.

```csharp
public class ScaledExitManager
{
    public record ExitLevel
    {
        public decimal RMultiple { get; init; }     // Выход на N*R прибыли
        public decimal ExitPercent { get; init; }   // Процент позиции для выхода
        public bool MoveStopToBreakeven { get; init; }
        public decimal? NewTrailingMultiplier { get; init; } // Новый ATR multiplier
        public bool Triggered { get; set; } = false;
    }

    private readonly List<ExitLevel> _exitLevels;
    private decimal _entryPrice;
    private decimal _initialStopLoss;
    private decimal _riskPerUnit;
    private decimal _remainingPosition = 1.0m;

    public ScaledExitManager()
    {
        // Стандартная конфигурация: 25% на 1R, 25% на 2R, 50% trailing
        _exitLevels = new List<ExitLevel>
        {
            new ExitLevel
            {
                RMultiple = 1.0m,
                ExitPercent = 0.25m,
                MoveStopToBreakeven = true,
                NewTrailingMultiplier = null
            },
            new ExitLevel
            {
                RMultiple = 2.0m,
                ExitPercent = 0.25m,
                MoveStopToBreakeven = false,
                NewTrailingMultiplier = 1.5m  // Tighter trailing после 2R
            },
            new ExitLevel
            {
                RMultiple = 3.0m,
                ExitPercent = 0.25m,
                MoveStopToBreakeven = false,
                NewTrailingMultiplier = 1.0m  // Ещё тighter
            }
            // Оставшиеся 25% выходят по трейлинг стопу
        };
    }

    public void Initialize(decimal entryPrice, decimal stopLoss, SignalType direction)
    {
        _entryPrice = entryPrice;
        _initialStopLoss = stopLoss;
        _riskPerUnit = direction == SignalType.Buy
            ? entryPrice - stopLoss
            : stopLoss - entryPrice;
        _remainingPosition = 1.0m;

        foreach (var level in _exitLevels)
        {
            level.Triggered = false;
        }
    }

    public List<TradeSignal> CheckExits(decimal currentPrice, SignalType direction,
        decimal currentAtr, string symbol)
    {
        var signals = new List<TradeSignal>();

        decimal currentPnlR = direction == SignalType.Buy
            ? (currentPrice - _entryPrice) / _riskPerUnit
            : (_entryPrice - currentPrice) / _riskPerUnit;

        foreach (var level in _exitLevels.Where(l => !l.Triggered))
        {
            if (currentPnlR >= level.RMultiple)
            {
                level.Triggered = true;

                var exitQuantityPercent = level.ExitPercent / _remainingPosition;
                _remainingPosition -= level.ExitPercent;

                signals.Add(new TradeSignal(
                    Symbol: symbol,
                    Type: SignalType.PartialExit,
                    Price: currentPrice,
                    Reason: $"Take profit at {level.RMultiple}R",
                    PartialExitPercent: exitQuantityPercent,
                    MoveStopToBreakeven: level.MoveStopToBreakeven
                ));

                if (level.NewTrailingMultiplier.HasValue)
                {
                    // Также отправить сигнал на изменение трейлинг стопа
                    var newStop = direction == SignalType.Buy
                        ? currentPrice - currentAtr * level.NewTrailingMultiplier.Value
                        : currentPrice + currentAtr * level.NewTrailingMultiplier.Value;

                    signals.Add(new TradeSignal(
                        Symbol: symbol,
                        Type: SignalType.UpdateStop,
                        Price: currentPrice,
                        StopLoss: newStop,
                        Reason: $"Tighten trailing stop to {level.NewTrailingMultiplier.Value}x ATR"
                    ));
                }
            }
        }

        return signals;
    }

    public decimal GetRemainingPositionPercent() => _remainingPosition;
}

// Использование:
public class AdxTrendStrategy
{
    private readonly ScaledExitManager _exitManager = new();

    public TradeSignal CheckExitConditions(Candle candle)
    {
        var exitSignals = _exitManager.CheckExits(
            currentPrice: candle.Close,
            direction: _currentPosition,
            currentAtr: _currentAtr,
            symbol: _symbol
        );

        foreach (var signal in exitSignals)
        {
            // Обработать каждый сигнал
            if (signal.Type == SignalType.PartialExit)
            {
                yield return signal;
            }
        }

        // ... остальные проверки выхода ...
    }
}
```

---

### 3.4 Мульти-инструментная торговля

**Проблема**: Бот торгует только одной парой.

**Решение**: Multi-symbol архитектура.

```csharp
public class MultiSymbolTrader
{
    private readonly Dictionary<string, SymbolTrader> _traders = new();
    private readonly PortfolioRiskManager _portfolioManager;
    private readonly TelegramNotifier _notifier;

    public record SymbolConfig
    {
        public string Symbol { get; init; } = "";
        public KlineInterval Interval { get; init; }
        public decimal AllocationPercent { get; init; } // % от портфеля
        public StrategySettings? CustomSettings { get; init; }
    }

    public MultiSymbolTrader(
        List<SymbolConfig> symbols,
        decimal totalCapital,
        PortfolioRiskManager portfolioManager,
        TelegramNotifier notifier)
    {
        _portfolioManager = portfolioManager;
        _notifier = notifier;

        foreach (var config in symbols)
        {
            var symbolCapital = totalCapital * config.AllocationPercent / 100;
            var settings = config.CustomSettings ?? new StrategySettings();

            _traders[config.Symbol] = new SymbolTrader(
                symbol: config.Symbol,
                interval: config.Interval,
                capital: symbolCapital,
                settings: settings,
                portfolioManager: _portfolioManager
            );
        }
    }

    public async Task StartAllAsync(CancellationToken ct)
    {
        var tasks = _traders.Values.Select(t => t.StartAsync(ct));

        // Запустить все трейдеры параллельно
        await Task.WhenAll(tasks);
    }

    public PortfolioStatus GetPortfolioStatus()
    {
        var positions = _traders.Values
            .Where(t => t.HasOpenPosition)
            .Select(t => new PositionInfo
            {
                Symbol = t.Symbol,
                Direction = t.CurrentDirection,
                EntryPrice = t.EntryPrice,
                CurrentPrice = t.CurrentPrice,
                UnrealizedPnL = t.UnrealizedPnL,
                RiskAmount = t.RiskAmount
            })
            .ToList();

        return new PortfolioStatus
        {
            TotalEquity = _traders.Values.Sum(t => t.CurrentEquity),
            UnrealizedPnL = positions.Sum(p => p.UnrealizedPnL),
            OpenPositions = positions,
            TotalRisk = positions.Sum(p => p.RiskAmount),
            DrawdownPercent = _portfolioManager.GetTotalDrawdownPercent()
        };
    }
}

public class SymbolTrader
{
    private readonly string _symbol;
    private readonly AdxTrendStrategy _strategy;
    private readonly RiskManager _riskManager;
    private readonly PortfolioRiskManager _portfolioManager;

    public async Task StartAsync(CancellationToken ct)
    {
        // Подписка на свечи
        await SubscribeToKlines(ct);

        // Обработка свечей
        while (!ct.IsCancellationRequested)
        {
            await ProcessNextCandle();
        }
    }

    private async Task ProcessNextCandle()
    {
        // Получить сигнал от стратегии
        var signal = _strategy.ProcessCandle(_lastCandle);

        if (signal.Type == SignalType.Buy || signal.Type == SignalType.Sell)
        {
            // Проверить портфельный риск
            if (_portfolioManager.CanOpenPosition(_symbol))
            {
                await ExecuteEntry(signal);
            }
        }
        else if (signal.Type == SignalType.Exit || signal.Type == SignalType.PartialExit)
        {
            await ExecuteExit(signal);
        }
    }
}

// Конфигурация:
var symbols = new List<SymbolConfig>
{
    new() { Symbol = "BTCUSDT", Interval = KlineInterval.FourHour, AllocationPercent = 40 },
    new() { Symbol = "ETHUSDT", Interval = KlineInterval.FourHour, AllocationPercent = 30 },
    new() { Symbol = "SOLUSDT", Interval = KlineInterval.FourHour, AllocationPercent = 15 },
    new() { Symbol = "BNBUSDT", Interval = KlineInterval.FourHour, AllocationPercent = 15 },
};

var multiTrader = new MultiSymbolTrader(
    symbols: symbols,
    totalCapital: 10000m,
    portfolioManager: new PortfolioRiskManager(maxTotalDrawdown: 25m),
    notifier: telegramNotifier
);

await multiTrader.StartAllAsync(cancellationToken);
```

---

## Фаза 4: Продвинутые фичи

### 4.1 Strategy Ensemble

**Идея**: Комбинировать сигналы нескольких стратегий.

```csharp
public class StrategyEnsemble
{
    public record StrategyVote
    {
        public string StrategyName { get; init; } = "";
        public SignalType Signal { get; init; }
        public decimal Confidence { get; init; }  // 0-1
        public decimal Weight { get; init; }      // Вес стратегии
    }

    private readonly List<(IStrategy strategy, decimal weight)> _strategies;
    private readonly decimal _minimumAgreement;  // Минимальный % согласия

    public StrategyEnsemble(decimal minimumAgreement = 0.6m)
    {
        _minimumAgreement = minimumAgreement;
        _strategies = new List<(IStrategy, decimal)>
        {
            (new AdxTrendStrategy(new StrategySettings()), 0.4m),
            (new MaStrategy(new MaSettings()), 0.3m),
            (new RsiStrategy(new RsiSettings()), 0.3m)
        };
    }

    public (SignalType signal, decimal confidence) GetConsensusSignal(List<Candle> candles)
    {
        var votes = new List<StrategyVote>();

        foreach (var (strategy, weight) in _strategies)
        {
            var signal = strategy.GenerateSignal(candles);
            votes.Add(new StrategyVote
            {
                StrategyName = strategy.Name,
                Signal = signal.Type,
                Confidence = signal.Confidence,
                Weight = weight
            });
        }

        // Подсчитать взвешенное голосование
        var buyScore = votes
            .Where(v => v.Signal == SignalType.Buy)
            .Sum(v => v.Weight * v.Confidence);

        var sellScore = votes
            .Where(v => v.Signal == SignalType.Sell)
            .Sum(v => v.Weight * v.Confidence);

        var totalWeight = _strategies.Sum(s => s.weight);

        if (buyScore / totalWeight >= _minimumAgreement)
        {
            return (SignalType.Buy, buyScore / totalWeight);
        }

        if (sellScore / totalWeight >= _minimumAgreement)
        {
            return (SignalType.Sell, sellScore / totalWeight);
        }

        return (SignalType.None, 0);
    }
}
```

---

### 4.2 Machine Learning Parameter Optimization

**Идея**: Генетический алгоритм для подбора параметров.

```csharp
public class GeneticOptimizer
{
    public record Chromosome
    {
        public StrategySettings Settings { get; init; } = new();
        public decimal Fitness { get; set; }
    }

    private readonly int _populationSize;
    private readonly int _generations;
    private readonly decimal _mutationRate;
    private readonly List<Candle> _trainingData;

    public GeneticOptimizer(
        List<Candle> trainingData,
        int populationSize = 100,
        int generations = 50,
        decimal mutationRate = 0.1m)
    {
        _trainingData = trainingData;
        _populationSize = populationSize;
        _generations = generations;
        _mutationRate = mutationRate;
    }

    public StrategySettings Optimize()
    {
        // 1. Инициализировать популяцию
        var population = InitializePopulation();

        for (int gen = 0; gen < _generations; gen++)
        {
            // 2. Оценить fitness каждой особи
            foreach (var chromosome in population)
            {
                chromosome.Fitness = EvaluateFitness(chromosome.Settings);
            }

            // 3. Селекция лучших
            var parents = SelectBest(population, count: _populationSize / 2);

            // 4. Кроссовер
            var offspring = Crossover(parents);

            // 5. Мутация
            Mutate(offspring);

            // 6. Формирование новой популяции
            population = parents.Concat(offspring).ToList();

            // Логирование
            var best = population.MaxBy(c => c.Fitness);
            Console.WriteLine($"Gen {gen}: Best fitness = {best?.Fitness:F4}");
        }

        return population.MaxBy(c => c.Fitness)!.Settings;
    }

    private List<Chromosome> InitializePopulation()
    {
        var random = new Random();
        return Enumerable.Range(0, _populationSize)
            .Select(_ => new Chromosome
            {
                Settings = new StrategySettings
                {
                    AdxPeriod = random.Next(10, 20),
                    AdxThreshold = random.Next(20, 35),
                    FastEmaPeriod = random.Next(10, 30),
                    SlowEmaPeriod = random.Next(40, 100),
                    AtrStopMultiplier = 1.5m + (decimal)random.NextDouble() * 2,
                    VolumeThreshold = 1.0m + (decimal)random.NextDouble() * 1.5m
                }
            })
            .ToList();
    }

    private decimal EvaluateFitness(StrategySettings settings)
    {
        var engine = new BacktestEngine(_trainingData, settings);
        var results = engine.Run();

        // Fitness = Sharpe ratio с penalty за drawdown
        var sharpe = results.SharpeRatio;
        var drawdownPenalty = Math.Max(0, results.MaxDrawdownPercent - 20) * 0.1m;

        return sharpe - drawdownPenalty;
    }

    private Chromosome Crossover(Chromosome parent1, Chromosome parent2)
    {
        var random = new Random();

        return new Chromosome
        {
            Settings = new StrategySettings
            {
                AdxPeriod = random.NextDouble() > 0.5
                    ? parent1.Settings.AdxPeriod
                    : parent2.Settings.AdxPeriod,
                AdxThreshold = random.NextDouble() > 0.5
                    ? parent1.Settings.AdxThreshold
                    : parent2.Settings.AdxThreshold,
                // ... аналогично для остальных параметров
            }
        };
    }

    private void Mutate(List<Chromosome> population)
    {
        var random = new Random();

        foreach (var chromosome in population)
        {
            if ((decimal)random.NextDouble() < _mutationRate)
            {
                // Случайная мутация одного параметра
                var paramIndex = random.Next(6);
                chromosome.Settings = paramIndex switch
                {
                    0 => chromosome.Settings with { AdxPeriod = random.Next(10, 20) },
                    1 => chromosome.Settings with { AdxThreshold = random.Next(20, 35) },
                    2 => chromosome.Settings with { FastEmaPeriod = random.Next(10, 30) },
                    3 => chromosome.Settings with { SlowEmaPeriod = random.Next(40, 100) },
                    4 => chromosome.Settings with { AtrStopMultiplier = 1.5m + (decimal)random.NextDouble() * 2 },
                    _ => chromosome.Settings with { VolumeThreshold = 1.0m + (decimal)random.NextDouble() * 1.5m }
                };
            }
        }
    }
}
```

---

### 4.3 News/Events Filter

**Идея**: Не торговать во время важных новостей.

```csharp
public class EconomicCalendarFilter
{
    private readonly HttpClient _httpClient;
    private readonly List<EconomicEvent> _events = new();

    public record EconomicEvent
    {
        public DateTime Time { get; init; }
        public string Title { get; init; } = "";
        public string Currency { get; init; } = "";
        public ImpactLevel Impact { get; init; }
    }

    public enum ImpactLevel { Low, Medium, High }

    public async Task LoadEventsAsync()
    {
        // Пример: загрузка из investing.com API или другого источника
        // В реальности нужен API key или парсинг

        // Для крипто-специфичных событий:
        // - FOMC заседания
        // - CPI данные
        // - Крипто-конференции
        // - Halving events
        // - Major upgrades (Ethereum merge, etc.)
    }

    public bool ShouldTrade(DateTime time)
    {
        // Не торговать за 30 минут до и после высоко-импактных событий
        var buffer = TimeSpan.FromMinutes(30);

        var nearbyHighImpact = _events
            .Where(e => e.Impact == ImpactLevel.High)
            .Any(e => Math.Abs((e.Time - time).TotalMinutes) < buffer.TotalMinutes);

        if (nearbyHighImpact)
        {
            Console.WriteLine("⚠️ High impact event nearby, skipping trade");
            return false;
        }

        return true;
    }

    public List<EconomicEvent> GetUpcomingEvents(int hours = 24)
    {
        var now = DateTime.UtcNow;
        return _events
            .Where(e => e.Time > now && e.Time < now.AddHours(hours))
            .OrderBy(e => e.Time)
            .ToList();
    }
}
```

---

## Приоритетный план действий

```
┌─────────────────────────────────────────────────────────────────┐
│                    ROADMAP IMPLEMENTATION                        │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│  ФАЗА 1: Критические (до live торговли)                         │
│  ─────────────────────────────────────────                       │
│  □ 1.1 Дневной лимит потерь                                     │
│  □ 1.2 Журнал сделок CSV                                        │
│  □ 1.3 Биржевые стоп-ордера (OCO)                               │
│  □ 1.4 Telegram уведомления                                     │
│  □ 1.5 Реалтайм equity tracking                                 │
│  □ 1.6 Валидация исполнения ордеров                             │
│  □ 1.7 Лимитные ордера                                          │
│  □ 1.8 Минимальный баланс                                       │
│  □ 1.9 Корреляция позиций                                       │
│                                                                  │
│  ФАЗА 2: Операционная надёжность                                │
│  ────────────────────────────────                                │
│  □ 2.1 State persistence                                        │
│  □ 2.2 Reconnection logic                                       │
│  □ 2.3 Graceful shutdown                                        │
│  □ 2.4 Unit tests                                               │
│  □ 2.5 Integration tests                                        │
│                                                                  │
│  ФАЗА 3: Улучшение стратегии                                    │
│  ───────────────────────────                                     │
│  □ 3.1 Volatility regime filter                                 │
│  □ 3.2 RSI divergence выходы                                    │
│  □ 3.3 Множественные тейк-профиты                               │
│  □ 3.4 Мульти-инструментная торговля                            │
│                                                                  │
│  ФАЗА 4: Продвинутые фичи                                       │
│  ────────────────────────                                        │
│  □ 4.1 Strategy ensemble                                        │
│  □ 4.2 ML parameter optimization                                │
│  □ 4.3 News/events filter                                       │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
```

---

## Метрики успеха

| Метрика | Текущее | Цель |
|---------|---------|------|
| Sharpe Ratio | N/A | > 1.5 |
| Max Drawdown | 20% limit | < 15% |
| Win Rate | N/A | > 45% |
| Profit Factor | N/A | > 1.5 |
| Average R-Multiple | N/A | > 0.5 |
| Daily Loss Limit | ❌ | 3% |
| Test Coverage | 0% | > 80% |

---

*Документ создан: 2026-01-01*
*Последнее обновление: 2026-01-01*
