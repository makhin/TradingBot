# План рефакторинга TradingBot

## Цель

Разделить монолитный проект ComplexBot на переиспользуемые библиотеки для поддержки нескольких ботов (ComplexBot, SignalBot, будущих ботов).

## Целевая архитектура

```
TradingBot.sln
│
├── 📦 TradingBot.Core/                    [netstandard2.1 / net8.0]
├── 📦 TradingBot.Binance/                 [net8.0]
├── 📦 TradingBot.Indicators/              [netstandard2.1 / net8.0]
│
├── 🤖 ComplexBot/                         [net8.0] - Существующий бот
├── 🤖 SignalBot/                          [net8.0] - Новый бот
│
├── 🧪 TradingBot.Core.Tests/
├── 🧪 TradingBot.Binance.Tests/
├── 🧪 ComplexBot.Tests/                   - Существующие тесты
└── 🧪 SignalBot.Tests/
```

---

## Этап 1: TradingBot.Core

### Описание
Базовая библиотека с общими моделями, интерфейсами и сервисами, не зависящими от конкретной биржи.

### Структура

```
TradingBot.Core/
├── TradingBot.Core.csproj
│
├── Models/
│   ├── Candle.cs                          ← из ComplexBot/Models/
│   ├── Trade.cs                           ← из ComplexBot/Models/
│   ├── TradeSignal.cs                     ← из ComplexBot/Models/
│   ├── TradeDirection.cs                  ← из ComplexBot/Models/
│   ├── SignalType.cs                      ← из ComplexBot/Models/
│   ├── TradeResult.cs                     ← из ComplexBot/Models/
│   ├── PerformanceMetrics.cs              ← из ComplexBot/Models/
│   ├── PositionSizeResult.cs              ← из ComplexBot/Models/
│   └── KlineInterval.cs                   ← из ComplexBot/Models/
│
├── RiskManagement/
│   ├── Interfaces/
│   │   ├── IRiskManager.cs                ← НОВЫЙ интерфейс
│   │   └── IEquityTracker.cs              ← НОВЫЙ интерфейс
│   │
│   ├── RiskManager.cs                     ← из ComplexBot/Services/RiskManagement/
│   ├── EquityTracker.cs                   ← из ComplexBot/Services/RiskManagement/
│   ├── AggregatedEquityTracker.cs         ← из ComplexBot/Services/RiskManagement/
│   ├── PortfolioRiskManager.cs            ← из ComplexBot/Services/RiskManagement/
│   ├── RiskSettings.cs                    ← из ComplexBot/Services/RiskManagement/
│   └── DrawdownRiskPolicy.cs              ← из ComplexBot/Services/RiskManagement/
│
├── State/
│   ├── Interfaces/
│   │   ├── IStateManager.cs               ← НОВЫЙ интерфейс
│   │   └── IPositionStore.cs              ← НОВЫЙ интерфейс
│   │
│   ├── JsonStateManager.cs                ← из ComplexBot/Services/State/StateManager.cs
│   ├── BotState.cs                        ← из ComplexBot/Services/State/
│   ├── SavedPosition.cs                   ← из ComplexBot/Services/State/
│   └── SavedOcoOrder.cs                   ← из ComplexBot/Services/State/
│
├── Notifications/
│   ├── Interfaces/
│   │   └── INotifier.cs                   ← НОВЫЙ интерфейс
│   │
│   └── TelegramNotifier.cs                ← из ComplexBot/Services/Notifications/
│
├── Analytics/
│   ├── Interfaces/
│   │   └── ITradeJournal.cs               ← НОВЫЙ интерфейс
│   │
│   ├── TradeJournal.cs                    ← из ComplexBot/Services/Analytics/
│   └── TradeCostCalculator.cs             ← из ComplexBot/Services/Analytics/
│
├── Lifecycle/
│   ├── Interfaces/
│   │   └── IGracefulShutdownHandler.cs    ← НОВЫЙ интерфейс
│   │
│   └── GracefulShutdownHandler.cs         ← из ComplexBot/Services/Lifecycle/
│
└── Utils/
    └── SpectreHelpers.cs                  ← из ComplexBot/Utils/
```

### Новые интерфейсы

```csharp
// IRiskManager.cs
public interface IRiskManager
{
    PositionSizeResult CalculatePositionSize(
        decimal equity,
        decimal entryPrice,
        decimal stopLossPrice,
        decimal? atr = null);

    decimal GetDrawdownAdjustedRisk(decimal currentDrawdownPercent);
    bool CanOpenPosition(decimal equity, decimal currentDrawdownPercent);
    void UpdateEquity(decimal newEquity);
}

// IStateManager.cs
public interface IStateManager<TState> where TState : class
{
    Task SaveStateAsync(TState state, CancellationToken ct = default);
    Task<TState?> LoadStateAsync(CancellationToken ct = default);
    Task<TState?> LoadBackupAsync(CancellationToken ct = default);
    Task DeleteStateAsync(CancellationToken ct = default);
}

// IPositionStore.cs
public interface IPositionStore<TPosition> where TPosition : class
{
    Task<IReadOnlyList<TPosition>> GetOpenPositionsAsync(CancellationToken ct = default);
    Task<TPosition?> GetPositionAsync(Guid id, CancellationToken ct = default);
    Task SavePositionAsync(TPosition position, CancellationToken ct = default);
    Task UpdatePositionAsync(TPosition position, CancellationToken ct = default);
    Task DeletePositionAsync(Guid id, CancellationToken ct = default);
}

// INotifier.cs
public interface INotifier
{
    Task SendMessageAsync(string message, CancellationToken ct = default);
    Task SendTradeOpenedAsync(Trade trade, CancellationToken ct = default);
    Task SendTradeClosedAsync(Trade trade, decimal pnl, CancellationToken ct = default);
    Task SendAlertAsync(string title, string message, CancellationToken ct = default);
}

// ITradeJournal.cs
public interface ITradeJournal
{
    void OpenTrade(Trade trade);
    void CloseTrade(Guid tradeId, decimal exitPrice, DateTime exitTime);
    void UpdateTradeMAE(Guid tradeId, decimal price);
    void UpdateTradeMFE(Guid tradeId, decimal price);
    Task ExportToCsvAsync(string filePath, CancellationToken ct = default);
}
```

### Зависимости (NuGet)

```xml
<ItemGroup>
  <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="8.0.0" />
  <PackageReference Include="System.Text.Json" Version="8.0.0" />
  <PackageReference Include="Telegram.Bot" Version="19.0.0" />
  <PackageReference Include="Spectre.Console" Version="0.49.1" />
  <PackageReference Include="CsvHelper" Version="31.0.0" />
</ItemGroup>
```

### Задачи

- [ ] Создать проект TradingBot.Core
- [ ] Перенести Models с минимальными изменениями
- [ ] Создать интерфейсы IRiskManager, IEquityTracker
- [ ] Перенести RiskManagement, реализовать интерфейсы
- [ ] Создать интерфейсы IStateManager, IPositionStore
- [ ] Перенести State (StateManager → JsonStateManager)
- [ ] Создать интерфейс INotifier
- [ ] Перенести TelegramNotifier
- [ ] Создать интерфейс ITradeJournal
- [ ] Перенести Analytics
- [ ] Перенести Lifecycle
- [ ] Перенести Utils/SpectreHelpers
- [ ] Написать unit-тесты для Core

---

## Этап 2: TradingBot.Indicators

### Описание
Библиотека технических индикаторов, независимая от торговой логики.

### Структура

```
TradingBot.Indicators/
├── TradingBot.Indicators.csproj
│
├── Interfaces/
│   ├── IIndicator.cs                      ← из ComplexBot/Services/Indicators/
│   ├── IIndicatorOfT.cs                   ← из ComplexBot/Services/Indicators/
│   └── IMultiValueIndicator.cs            ← из ComplexBot/Services/Indicators/
│
├── Base/
│   ├── IndicatorBase.cs                   ← НОВЫЙ базовый класс
│   ├── WindowedIndicator.cs               ← из ComplexBot/Services/Indicators/
│   └── ExponentialIndicator.cs            ← из ComplexBot/Services/Indicators/
│
├── Trend/
│   ├── Ema.cs                             ← из ComplexBot/Services/Indicators/
│   ├── Sma.cs                             ← из ComplexBot/Services/Indicators/
│   ├── Adx.cs                             ← из ComplexBot/Services/Indicators/
│   └── Macd.cs                            ← из ComplexBot/Services/Indicators/
│
├── Volatility/
│   ├── Atr.cs                             ← из ComplexBot/Services/Indicators/
│   └── BollingerBands.cs                  ← из ComplexBot/Services/Indicators/
│
├── Momentum/
│   └── Rsi.cs                             ← из ComplexBot/Services/Indicators/
│
├── Volume/
│   ├── Obv.cs                             ← из ComplexBot/Services/Indicators/
│   └── VolumeIndicator.cs                 ← из ComplexBot/Services/Indicators/
│
└── Utils/
    ├── IndicatorValueConverter.cs         ← из ComplexBot/Services/Indicators/
    ├── PnLCalculator.cs                   ← из ComplexBot/Services/Indicators/
    └── QuoteSeries.cs                     ← из ComplexBot/Services/Indicators/
```

### Зависимости

```xml
<ItemGroup>
  <PackageReference Include="Skender.Stock.Indicators" Version="2.5.0" />
  <PackageReference Include="MathNet.Numerics" Version="5.0.0" />
</ItemGroup>

<ItemGroup>
  <ProjectReference Include="..\TradingBot.Core\TradingBot.Core.csproj" />
</ItemGroup>
```

### Задачи

- [ ] Создать проект TradingBot.Indicators
- [ ] Создать структуру папок (Trend, Volatility, Momentum, Volume)
- [ ] Перенести интерфейсы индикаторов
- [ ] Перенести базовые классы
- [ ] Перенести все индикаторы с группировкой по категориям
- [ ] Перенести утилиты
- [ ] Написать unit-тесты

---

## Этап 3: TradingBot.Binance

### Описание
Библиотека для работы с Binance API (Spot и Futures), WebSocket подписки, управление ордерами.

### Структура

```
TradingBot.Binance/
├── TradingBot.Binance.csproj
│
├── Common/
│   ├── Interfaces/
│   │   ├── IBinanceClient.cs              ← НОВЫЙ базовый интерфейс
│   │   ├── IOrderExecutor.cs              ← НОВЫЙ интерфейс
│   │   └── IPositionQuery.cs              ← НОВЫЙ интерфейс
│   │
│   ├── Models/
│   │   ├── OrderRequest.cs                ← НОВЫЙ
│   │   ├── OrderResult.cs                 ← из ComplexBot/Services/Trading/
│   │   ├── ExecutionResult.cs             ← из ComplexBot/Services/Trading/
│   │   ├── PositionInfo.cs                ← НОВЫЙ
│   │   ├── AccountInfo.cs                 ← НОВЫЙ
│   │   └── SymbolInfo.cs                  ← НОВЫЙ (precision, min qty, etc)
│   │
│   ├── Settings/
│   │   └── BinanceSettings.cs             ← из ComplexBot/Configuration/BinanceApiSettings.cs
│   │
│   └── Validation/
│       ├── ExecutionValidator.cs          ← из ComplexBot/Services/Trading/
│       └── OrderValidator.cs              ← НОВЫЙ (проверка min qty, precision)
│
├── Spot/
│   ├── Interfaces/
│   │   └── IBinanceSpotClient.cs          ← НОВЫЙ
│   │
│   ├── BinanceSpotClient.cs               ← Извлечь из BinanceLiveTrader
│   └── SpotOrderExecutor.cs               ← Извлечь из BinanceLiveTrader
│
├── Futures/
│   ├── Interfaces/
│   │   └── IBinanceFuturesClient.cs       ← НОВЫЙ
│   │
│   ├── BinanceFuturesClient.cs            ← НОВЫЙ
│   ├── FuturesOrderExecutor.cs            ← НОВЫЙ
│   ├── FuturesPositionManager.cs          ← НОВЫЙ
│   │
│   └── Models/
│       ├── FuturesPosition.cs             ← НОВЫЙ
│       ├── LeverageInfo.cs                ← НОВЫЙ
│       └── MarginType.cs                  ← НОВЫЙ (Isolated/Cross)
│
├── WebSocket/
│   ├── Interfaces/
│   │   ├── IKlineListener.cs              ← НОВЫЙ
│   │   ├── IOrderUpdateListener.cs        ← НОВЫЙ
│   │   └── IUserDataListener.cs           ← НОВЫЙ
│   │
│   ├── BinanceWebSocketManager.cs         ← Извлечь из BinanceLiveTrader
│   ├── KlineWebSocketHandler.cs           ← НОВЫЙ
│   └── UserDataWebSocketHandler.cs        ← НОВЫЙ (для order updates)
│
└── Reconciliation/
    ├── Interfaces/
    │   └── IStateReconciler.cs            ← НОВЫЙ
    │
    ├── SpotStateReconciler.cs             ← из ComplexBot/Services/State/StateReconciler.cs
    └── FuturesStateReconciler.cs          ← НОВЫЙ
```

### Ключевые интерфейсы

```csharp
// IBinanceClient.cs - базовый интерфейс
public interface IBinanceClient
{
    Task<AccountInfo> GetAccountInfoAsync(CancellationToken ct = default);
    Task<SymbolInfo> GetSymbolInfoAsync(string symbol, CancellationToken ct = default);
    Task<decimal> GetCurrentPriceAsync(string symbol, CancellationToken ct = default);
    Task<IReadOnlyList<Candle>> GetKlinesAsync(
        string symbol,
        KlineInterval interval,
        int limit = 500,
        CancellationToken ct = default);
}

// IOrderExecutor.cs
public interface IOrderExecutor
{
    Task<OrderResult> PlaceMarketOrderAsync(OrderRequest request, CancellationToken ct = default);
    Task<OrderResult> PlaceLimitOrderAsync(OrderRequest request, CancellationToken ct = default);
    Task<OrderResult> PlaceStopMarketOrderAsync(OrderRequest request, CancellationToken ct = default);
    Task<OrderResult> PlaceTakeProfitMarketOrderAsync(OrderRequest request, CancellationToken ct = default);
    Task<bool> CancelOrderAsync(string symbol, long orderId, CancellationToken ct = default);
    Task<IReadOnlyList<OrderResult>> GetOpenOrdersAsync(string symbol, CancellationToken ct = default);
}

// IBinanceFuturesClient.cs
public interface IBinanceFuturesClient : IBinanceClient, IOrderExecutor
{
    Task<FuturesPosition?> GetPositionAsync(string symbol, CancellationToken ct = default);
    Task<IReadOnlyList<FuturesPosition>> GetAllPositionsAsync(CancellationToken ct = default);
    Task<bool> SetLeverageAsync(string symbol, int leverage, CancellationToken ct = default);
    Task<bool> SetMarginTypeAsync(string symbol, MarginType marginType, CancellationToken ct = default);
    Task<LeverageInfo> GetLeverageInfoAsync(string symbol, CancellationToken ct = default);
    Task<decimal> GetLiquidationPriceAsync(string symbol, CancellationToken ct = default);
}

// IKlineListener.cs
public interface IKlineListener : IAsyncDisposable
{
    event EventHandler<KlineEventArgs>? OnKlineReceived;
    event EventHandler<KlineEventArgs>? OnKlineClosed;

    Task SubscribeAsync(string symbol, KlineInterval interval, CancellationToken ct = default);
    Task UnsubscribeAsync(string symbol, CancellationToken ct = default);
}

// IOrderUpdateListener.cs
public interface IOrderUpdateListener : IAsyncDisposable
{
    event EventHandler<OrderUpdateEventArgs>? OnOrderFilled;
    event EventHandler<OrderUpdateEventArgs>? OnOrderCanceled;
    event EventHandler<OrderUpdateEventArgs>? OnOrderExpired;

    Task StartAsync(CancellationToken ct = default);
    Task StopAsync(CancellationToken ct = default);
}
```

### Модели

```csharp
// OrderRequest.cs
public record OrderRequest
{
    public required string Symbol { get; init; }
    public required OrderSide Side { get; init; }
    public required decimal Quantity { get; init; }
    public decimal? Price { get; init; }           // Для Limit
    public decimal? StopPrice { get; init; }       // Для Stop/TP
    public decimal? TakeProfitPrice { get; init; }
    public bool ReduceOnly { get; init; }          // Для Futures
    public string? ClientOrderId { get; init; }
}

// FuturesPosition.cs
public record FuturesPosition
{
    public required string Symbol { get; init; }
    public required PositionSide Side { get; init; }  // Long/Short
    public required decimal Quantity { get; init; }
    public required decimal EntryPrice { get; init; }
    public required decimal MarkPrice { get; init; }
    public required decimal UnrealizedPnl { get; init; }
    public required decimal LiquidationPrice { get; init; }
    public required int Leverage { get; init; }
    public required MarginType MarginType { get; init; }
}

// SymbolInfo.cs
public record SymbolInfo
{
    public required string Symbol { get; init; }
    public required int PricePrecision { get; init; }
    public required int QuantityPrecision { get; init; }
    public required decimal MinQuantity { get; init; }
    public required decimal MinNotional { get; init; }
    public required decimal TickSize { get; init; }
    public required decimal StepSize { get; init; }
}
```

### Зависимости

```xml
<ItemGroup>
  <PackageReference Include="Binance.Net" Version="10.3.0" />
  <PackageReference Include="CryptoExchange.Net" Version="8.3.0" />
  <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="8.0.0" />
</ItemGroup>

<ItemGroup>
  <ProjectReference Include="..\TradingBot.Core\TradingBot.Core.csproj" />
</ItemGroup>
```

### Задачи

- [ ] Создать проект TradingBot.Binance
- [ ] Определить базовые интерфейсы (IBinanceClient, IOrderExecutor)
- [ ] Создать Common/Models
- [ ] Извлечь Spot логику из BinanceLiveTrader
- [ ] Реализовать BinanceSpotClient
- [ ] Создать интерфейс IBinanceFuturesClient
- [ ] Реализовать BinanceFuturesClient (НОВЫЙ)
- [ ] Извлечь WebSocket логику
- [ ] Реализовать IKlineListener
- [ ] Реализовать IOrderUpdateListener (User Data Stream)
- [ ] Перенести StateReconciler, создать FuturesStateReconciler
- [ ] Написать integration-тесты (testnet)

---

## Этап 4: Обновление ComplexBot

### Описание
Обновить ComplexBot для использования новых библиотек вместо локальных классов.

### Изменения

```
ComplexBot/
├── ComplexBot.csproj                      ← Добавить ссылки на библиотеки
│
├── Services/
│   ├── Strategies/                        ← Остаётся (специфично для ComplexBot)
│   │   ├── StrategyBase.cs
│   │   ├── AdxTrendStrategy.cs
│   │   ├── MaStrategy.cs
│   │   ├── RsiStrategy.cs
│   │   └── StrategyEnsemble.cs
│   │
│   ├── Backtesting/                       ← Остаётся (специфично для ComplexBot)
│   │   ├── BacktestEngine.cs
│   │   ├── HistoricalDataLoader.cs        ← Использует TradingBot.Binance
│   │   ├── WalkForwardAnalyzer.cs
│   │   ├── MonteCarloSimulator.cs
│   │   └── *Optimizer.cs
│   │
│   ├── Trading/
│   │   └── BinanceLiveTrader.cs           ← Рефакторинг: использует TradingBot.Binance
│   │
│   ├── Indicators/                        ← УДАЛИТЬ (перенесено в TradingBot.Indicators)
│   ├── RiskManagement/                    ← УДАЛИТЬ (перенесено в TradingBot.Core)
│   ├── State/                             ← УДАЛИТЬ (перенесено в TradingBot.Core)
│   ├── Analytics/                         ← УДАЛИТЬ (перенесено в TradingBot.Core)
│   ├── Notifications/                     ← УДАЛИТЬ (перенесено в TradingBot.Core)
│   └── Lifecycle/                         ← УДАЛИТЬ (перенесено в TradingBot.Core)
│
├── Models/                                ← УДАЛИТЬ большинство (перенесено в TradingBot.Core)
│   └── AppMode.cs                         ← Остаётся (специфично)
│
├── Configuration/                         ← Остаётся (специфично для ComplexBot)
└── Utils/
    ├── SettingsPrompts.cs                 ← Остаётся
    └── UiMappings.cs                      ← Остаётся
```

### Обновлённые зависимости

```xml
<ItemGroup>
  <ProjectReference Include="..\TradingBot.Core\TradingBot.Core.csproj" />
  <ProjectReference Include="..\TradingBot.Binance\TradingBot.Binance.csproj" />
  <ProjectReference Include="..\TradingBot.Indicators\TradingBot.Indicators.csproj" />
</ItemGroup>
```

### Задачи

- [ ] Добавить ссылки на библиотеки в csproj
- [ ] Обновить using statements во всех файлах
- [ ] Рефакторинг BinanceLiveTrader для использования TradingBot.Binance
- [ ] Рефакторинг HistoricalDataLoader для использования TradingBot.Binance
- [ ] Удалить перенесённые файлы
- [ ] Обновить Configuration для использования интерфейсов
- [ ] Запустить все тесты, исправить ошибки
- [ ] Проверить работоспособность всех режимов (backtest, live, optimize)

---

## Этап 5: Тестирование и стабилизация

### Задачи

- [ ] Создать TradingBot.Core.Tests
- [ ] Перенести релевантные тесты из ComplexBot.Tests
- [ ] Создать TradingBot.Binance.Tests (integration tests)
- [ ] Обновить ComplexBot.Tests
- [ ] Создать CI pipeline для всех проектов
- [ ] Документация API для библиотек

---

## Диаграмма зависимостей

```
┌─────────────────────────────────────────────────────────────────┐
│                        APPLICATIONS                              │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│    ┌─────────────┐                    ┌─────────────┐           │
│    │ ComplexBot  │                    │  SignalBot  │           │
│    │             │                    │             │           │
│    │ • Strategies│                    │ • TG Client │           │
│    │ • Backtest  │                    │ • Parser    │           │
│    │ • Optimize  │                    │ • Position  │           │
│    └──────┬──────┘                    └──────┬──────┘           │
│           │                                  │                   │
└───────────┼──────────────────────────────────┼───────────────────┘
            │                                  │
            ▼                                  ▼
┌─────────────────────────────────────────────────────────────────┐
│                         LIBRARIES                                │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│    ┌───────────────────┐      ┌───────────────────────┐         │
│    │TradingBot.Indicators│    │  TradingBot.Binance   │         │
│    │                   │      │                       │         │
│    │ • EMA, SMA, ADX   │      │ • Spot Client         │         │
│    │ • ATR, RSI, MACD  │      │ • Futures Client      │         │
│    │ • Bollinger, OBV  │      │ • WebSocket           │         │
│    └─────────┬─────────┘      │ • Reconciliation      │         │
│              │                └───────────┬───────────┘         │
│              │                            │                      │
│              ▼                            ▼                      │
│    ┌─────────────────────────────────────────────────┐          │
│    │              TradingBot.Core                     │          │
│    │                                                  │          │
│    │ • Models (Candle, Trade, Signal)                │          │
│    │ • RiskManagement (IRiskManager)                 │          │
│    │ • State (IStateManager, IPositionStore)         │          │
│    │ • Notifications (INotifier)                     │          │
│    │ • Analytics (ITradeJournal)                     │          │
│    │ • Lifecycle (GracefulShutdown)                  │          │
│    │ • Utils (SpectreHelpers)                        │          │
│    └─────────────────────────────────────────────────┘          │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
```

---

## Оценка трудозатрат

| Этап | Описание | Сложность | Файлов |
|------|----------|-----------|--------|
| 1 | TradingBot.Core | Средняя | ~25 |
| 2 | TradingBot.Indicators | Низкая | ~20 |
| 3 | TradingBot.Binance | Высокая | ~20 |
| 4 | Обновление ComplexBot | Средняя | ~15 изменений |
| 5 | Тестирование | Средняя | ~10 |

---

## Риски и митигация

| Риск | Вероятность | Митигация |
|------|-------------|-----------|
| Breaking changes в API | Высокая | Версионирование, deprecation warnings |
| Регрессии в ComplexBot | Средняя | Полное тестовое покрытие перед рефакторингом |
| Circular dependencies | Низкая | Чёткое разделение по слоям |
| Binance API changes | Низкая | Абстракция через интерфейсы |

---

## Чеклист готовности к SignalBot

После завершения рефакторинга, для SignalBot будут доступны:

- [x] Модели (Candle, Trade, TradeSignal)
- [x] IRiskManager для расчёта позиций
- [x] IStateManager для persistence
- [x] IPositionStore для хранения позиций
- [x] INotifier для уведомлений
- [x] IBinanceFuturesClient для торговли
- [x] IOrderUpdateListener для отслеживания исполнения
- [x] GracefulShutdownHandler для корректного завершения
