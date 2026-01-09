# SignalBot - Дизайн документ

## Обзор

SignalBot - бот для автоматического исполнения торговых сигналов из Telegram каналов на Binance Futures.

## Функциональные требования

### Основной сценарий

```
1. Подключение к Telegram каналу через WTelegramClient
2. Получение сообщения с сигналом
3. Парсинг сигнала в структурированный формат
4. Валидация и корректировка параметров (SL, leverage)
5. Открытие позиции на Binance Futures
6. Размещение Stop Loss и Take Profit ордеров
7. Мониторинг исполнения ордеров
8. Уведомление о результатах
9. Логирование и статистика
```

### Формат входящих сигналов

```
#SYMBOL/USDT - Long🟢 | Short🔴

Entry: X.XXXX
Stop Loss: X.XXXX

Target 1: X.XXXX
Target 2: X.XXXX
Target 3: X.XXXX
Target 4: X.XXXX

Leverage: xNN
```

**Примеры:**

```
#ICNT/USDT - Long🟢

Entry: 0.3709
Stop Loss: 0.18569

Target 1: 0.37225
Target 2: 0.37243
Target 3: 0.37362
Target 4: 0.37452

Leverage: x32
```

```
#AVNT/USDT - Long🟢

Entry: 0.3204
Stop Loss: 0.16064

Target 1: 0.32054
Target 2: 0.32303
Target 3: 0.3233
Target 4: 0.32379

Leverage: x32
```

---

## Архитектура

### Структура проекта

```
SignalBot/
├── SignalBot.csproj
│
├── Program.cs                             # Entry point
├── SignalBotRunner.cs                     # Основной runner
│
├── Services/
│   ├── Telegram/
│   │   ├── ITelegramSignalListener.cs     # Интерфейс
│   │   ├── TelegramSignalListener.cs      # WTelegramClient реализация
│   │   ├── SignalParser.cs                # Парсинг текста сигнала
│   │   └── SignalParserResult.cs          # Результат парсинга
│   │
│   ├── Validation/
│   │   ├── ISignalValidator.cs            # Интерфейс
│   │   ├── SignalValidator.cs             # Валидация и корректировка
│   │   └── ValidationResult.cs            # Результат валидации
│   │
│   ├── Trading/
│   │   ├── ISignalTrader.cs               # Интерфейс
│   │   ├── SignalTrader.cs                # Исполнение сигналов
│   │   ├── IPositionManager.cs            # Интерфейс
│   │   ├── PositionManager.cs             # Управление позициями
│   │   └── TargetTracker.cs               # Отслеживание targets
│   │
│   └── Monitoring/
│       ├── OrderMonitor.cs                # Мониторинг ордеров
│       └── PositionMonitor.cs             # Мониторинг позиций
│
├── Models/
│   ├── TradingSignal.cs                   # Сигнал из Telegram
│   ├── SignalPosition.cs                  # Позиция по сигналу
│   ├── TargetLevel.cs                     # Уровень take profit
│   ├── SignalDirection.cs                 # Long/Short enum
│   ├── PositionStatus.cs                  # Статус позиции enum
│   └── SignalSource.cs                    # Источник сигнала
│
├── Configuration/
│   ├── SignalBotSettings.cs               # Основные настройки
│   ├── TelegramSettings.cs                # Настройки Telegram
│   ├── TradingSettings.cs                 # Настройки торговли
│   └── RiskOverrideSettings.cs            # Переопределение риска
│
├── State/
│   ├── SignalBotState.cs                  # Состояние бота
│   └── SignalPositionStore.cs             # Хранилище позиций
│
└── appsettings.json
```

### Диаграмма компонентов

```
┌────────────────────────────────────────────────────────────────────┐
│                          SignalBot                                  │
├────────────────────────────────────────────────────────────────────┤
│                                                                     │
│  ┌─────────────────┐    ┌──────────────┐    ┌─────────────────┐   │
│  │    Telegram     │    │    Signal    │    │     Signal      │   │
│  │    Listener     │───▶│    Parser    │───▶│    Validator    │   │
│  │                 │    │              │    │                 │   │
│  │ (WTelegramClient)│    │  (Regex)     │    │ (Risk checks)   │   │
│  └─────────────────┘    └──────────────┘    └────────┬────────┘   │
│                                                       │            │
│                                                       ▼            │
│  ┌─────────────────┐    ┌──────────────┐    ┌─────────────────┐   │
│  │   Position      │◀───│   Signal     │◀───│   Trading       │   │
│  │   Manager       │    │   Trader     │    │   Signal        │   │
│  │                 │    │              │    │   (validated)   │   │
│  └────────┬────────┘    └──────┬───────┘    └─────────────────┘   │
│           │                    │                                   │
│           ▼                    ▼                                   │
│  ┌─────────────────┐    ┌──────────────┐                          │
│  │   Position      │    │   Order      │                          │
│  │   Store         │    │   Monitor    │                          │
│  │                 │    │              │                          │
│  │ (JSON/SQLite)   │    │ (WebSocket)  │                          │
│  └─────────────────┘    └──────────────┘                          │
│                                                                     │
└────────────────────────────────────────────────────────────────────┘
                │                    │
                ▼                    ▼
┌───────────────────────┐  ┌───────────────────────┐
│   TradingBot.Core     │  │  TradingBot.Binance   │
│                       │  │                       │
│ • IRiskManager        │  │ • IBinanceFuturesClient│
│ • IStateManager       │  │ • IOrderUpdateListener │
│ • INotifier           │  │ • IKlineListener      │
│ • Models              │  │                       │
└───────────────────────┘  └───────────────────────┘
```

---

## Модели данных

### TradingSignal

```csharp
/// <summary>
/// Сигнал, полученный из Telegram канала
/// </summary>
public record TradingSignal
{
    // Метаданные
    public Guid Id { get; init; } = Guid.NewGuid();
    public string RawText { get; init; } = string.Empty;
    public DateTime ReceivedAt { get; init; } = DateTime.UtcNow;
    public SignalSource Source { get; init; }

    // Распарсенные данные из сигнала
    public string Symbol { get; init; } = string.Empty;        // "ICNTUSDT"
    public SignalDirection Direction { get; init; }             // Long/Short
    public decimal Entry { get; init; }                         // Цена входа
    public decimal OriginalStopLoss { get; init; }              // SL из сигнала
    public IReadOnlyList<decimal> Targets { get; init; } = [];  // T1, T2, T3, T4
    public int OriginalLeverage { get; init; }                  // Leverage из сигнала

    // Рассчитанные/скорректированные значения
    public decimal AdjustedStopLoss { get; init; }              // Реальный SL
    public int AdjustedLeverage { get; init; }                  // Реальный leverage
    public decimal LiquidationPrice { get; init; }              // Цена ликвидации
    public decimal RiskRewardRatio { get; init; }               // R:R до первого target

    // Валидация
    public bool IsValid { get; init; }
    public IReadOnlyList<string> ValidationWarnings { get; init; } = [];
}

public enum SignalDirection
{
    Long,
    Short
}

public record SignalSource
{
    public string ChannelName { get; init; } = string.Empty;
    public long ChannelId { get; init; }
    public int MessageId { get; init; }
}
```

### SignalPosition

```csharp
/// <summary>
/// Позиция, открытая по сигналу
/// </summary>
public record SignalPosition
{
    // Идентификация
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid SignalId { get; init; }

    // Параметры позиции
    public string Symbol { get; init; } = string.Empty;
    public SignalDirection Direction { get; init; }
    public PositionStatus Status { get; init; }

    // Цены
    public decimal PlannedEntryPrice { get; init; }
    public decimal ActualEntryPrice { get; init; }
    public decimal CurrentStopLoss { get; init; }
    public int Leverage { get; init; }

    // Количество
    public decimal InitialQuantity { get; init; }
    public decimal RemainingQuantity { get; init; }
    public decimal FilledQuantity => InitialQuantity - RemainingQuantity;

    // Targets
    public IReadOnlyList<TargetLevel> Targets { get; init; } = [];
    public int TargetsHit => Targets.Count(t => t.IsHit);

    // Ордера на бирже
    public long? EntryOrderId { get; init; }
    public long? StopLossOrderId { get; init; }
    public IReadOnlyList<long> TakeProfitOrderIds { get; init; } = [];

    // Время
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime? OpenedAt { get; init; }
    public DateTime? ClosedAt { get; init; }

    // P&L
    public decimal RealizedPnl { get; init; }
    public decimal UnrealizedPnl { get; init; }
    public decimal Commission { get; init; }

    // Причина закрытия
    public PositionCloseReason? CloseReason { get; init; }
}

public enum PositionStatus
{
    Pending,         // Сигнал получен, ожидает обработки
    Opening,         // Ордер на вход отправлен
    Open,            // Позиция открыта
    PartialClosed,   // Часть позиции закрыта по targets
    Closing,         // Закрытие в процессе
    Closed,          // Полностью закрыта
    Cancelled,       // Отменена до открытия
    Failed           // Ошибка при открытии
}

public enum PositionCloseReason
{
    AllTargetsHit,
    StopLossHit,
    ManualClose,
    Liquidation,
    Error
}
```

### TargetLevel

```csharp
/// <summary>
/// Уровень take profit
/// </summary>
public record TargetLevel
{
    public int Index { get; init; }                    // 0, 1, 2, 3
    public decimal Price { get; init; }                // Цена target
    public decimal PercentToClose { get; init; }       // % позиции для закрытия (25%)
    public decimal QuantityToClose { get; init; }      // Количество для закрытия

    public bool IsHit { get; init; }
    public DateTime? HitAt { get; init; }
    public decimal? ActualClosePrice { get; init; }
    public long? OrderId { get; init; }

    // Действие после достижения
    public decimal? MoveStopLossTo { get; init; }      // Куда двигать SL после hit
}
```

### SignalBotState

```csharp
/// <summary>
/// Состояние бота для persistence
/// </summary>
public record SignalBotState
{
    public DateTime LastUpdate { get; init; } = DateTime.UtcNow;
    public string Version { get; init; } = "1.0";

    // Открытые позиции
    public IReadOnlyList<SignalPosition> OpenPositions { get; init; } = [];

    // Ожидающие сигналы (не успели открыть)
    public IReadOnlyList<TradingSignal> PendingSignals { get; init; } = [];

    // Статистика сессии
    public decimal SessionStartEquity { get; init; }
    public decimal CurrentEquity { get; init; }
    public int TotalSignalsReceived { get; init; }
    public int TotalPositionsOpened { get; init; }
    public int TotalPositionsClosed { get; init; }

    // ID последнего обработанного сообщения (для дедупликации)
    public Dictionary<long, int> LastProcessedMessageIds { get; init; } = new();
}
```

---

## Конфигурация

### appsettings.json

```json
{
  "SignalBot": {
    "Telegram": {
      "ApiId": 12345678,
      "ApiHash": "your_api_hash",
      "PhoneNumber": "+1234567890",
      "ChannelIds": [
        -1001234567890
      ],
      "SessionPath": "telegram_session.dat"
    },

    "Trading": {
      "DefaultSymbolSuffix": "USDT",
      "MarginType": "Isolated",
      "PositionMode": "OneWay",

      "EntryMode": "Market",
      "MaxConcurrentPositions": 5,
      "MinTimeBetweenSignals": "00:01:00",

      "TargetStrategy": "PartialClose",
      "TargetClosePercents": [25, 25, 25, 25],
      "MoveStopToBreakeven": true,
      "TrailingStopEnabled": false
    },

    "RiskOverride": {
      "Enabled": true,
      "MaxLeverage": 10,
      "UseSignalLeverage": false,

      "StopLossMode": "Calculate",
      "StopLossPercent": 2.0,
      "SafeDistanceFromLiquidation": 0.3,

      "RiskPerTradePercent": 1.0,
      "MaxDrawdownPercent": 20.0,
      "MaxDailyLossPercent": 5.0
    },

    "Notifications": {
      "TelegramBotToken": "your_bot_token",
      "TelegramChatId": "your_chat_id",
      "NotifyOnSignalReceived": true,
      "NotifyOnPositionOpened": true,
      "NotifyOnTargetHit": true,
      "NotifyOnPositionClosed": true,
      "NotifyOnError": true
    },

    "State": {
      "StatePath": "signalbot_state.json",
      "BackupEnabled": true,
      "AutoSaveIntervalSeconds": 30
    }
  },

  "BinanceApi": {
    "UseTestnet": true,
    "ApiKey": "",
    "ApiSecret": ""
  },

  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "SignalBot": "Debug"
    }
  }
}
```

### Переменные окружения (.env)

```bash
# Telegram API (https://my.telegram.org)
TELEGRAM_API_ID=12345678
TELEGRAM_API_HASH=your_api_hash
TELEGRAM_PHONE=+1234567890

# Telegram Bot для уведомлений
TELEGRAM_BOT_TOKEN=your_bot_token
TELEGRAM_CHAT_ID=your_chat_id

# Binance Futures Testnet
BINANCE_TESTNET_KEY=your_testnet_key
BINANCE_TESTNET_SECRET=your_testnet_secret

# Binance Futures Mainnet
BINANCE_MAINNET_KEY=your_mainnet_key
BINANCE_MAINNET_SECRET=your_mainnet_secret

# Mode
TRADING_BinanceApi__UseTestnet=true
```

---

## Ключевые компоненты

### SignalParser

```csharp
public class SignalParser
{
    // Основной regex для парсинга
    private static readonly Regex SignalRegex = new(
        @"#(?<symbol>\w+)/USDT\s*-\s*(?<direction>Long|Short)[🟢🔴]?\s*" +
        @"Entry:\s*(?<entry>[\d.]+)\s*" +
        @"Stop\s*Loss:\s*(?<sl>[\d.]+)\s*" +
        @"(?:Target\s*1:\s*(?<t1>[\d.]+)\s*)?" +
        @"(?:Target\s*2:\s*(?<t2>[\d.]+)\s*)?" +
        @"(?:Target\s*3:\s*(?<t3>[\d.]+)\s*)?" +
        @"(?:Target\s*4:\s*(?<t4>[\d.]+)\s*)?" +
        @"Leverage:\s*x(?<leverage>\d+)",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    public SignalParserResult Parse(string text)
    {
        var match = SignalRegex.Match(text);

        if (!match.Success)
            return SignalParserResult.Failed("Signal format not recognized");

        var targets = new List<decimal>();
        for (int i = 1; i <= 4; i++)
        {
            var group = match.Groups[$"t{i}"];
            if (group.Success && decimal.TryParse(group.Value,
                NumberStyles.Any, CultureInfo.InvariantCulture, out var target))
            {
                targets.Add(target);
            }
        }

        return SignalParserResult.Success(new TradingSignal
        {
            RawText = text,
            Symbol = match.Groups["symbol"].Value.ToUpperInvariant() + "USDT",
            Direction = match.Groups["direction"].Value.Equals("Long",
                StringComparison.OrdinalIgnoreCase)
                    ? SignalDirection.Long
                    : SignalDirection.Short,
            Entry = decimal.Parse(match.Groups["entry"].Value, CultureInfo.InvariantCulture),
            OriginalStopLoss = decimal.Parse(match.Groups["sl"].Value, CultureInfo.InvariantCulture),
            Targets = targets,
            OriginalLeverage = int.Parse(match.Groups["leverage"].Value)
        });
    }
}
```

### SignalValidator

```csharp
public class SignalValidator : ISignalValidator
{
    private readonly RiskOverrideSettings _settings;
    private readonly IBinanceFuturesClient _client;

    public async Task<ValidationResult> ValidateAndAdjustAsync(
        TradingSignal signal,
        decimal accountEquity,
        CancellationToken ct = default)
    {
        var warnings = new List<string>();

        // 1. Проверить существование символа
        var symbolInfo = await _client.GetSymbolInfoAsync(signal.Symbol, ct);
        if (symbolInfo == null)
            return ValidationResult.Failed($"Symbol {signal.Symbol} not found");

        // 2. Определить leverage
        int leverage = _settings.UseSignalLeverage
            ? Math.Min(signal.OriginalLeverage, _settings.MaxLeverage)
            : _settings.MaxLeverage;

        if (leverage != signal.OriginalLeverage)
            warnings.Add($"Leverage adjusted: {signal.OriginalLeverage}x → {leverage}x");

        // 3. Рассчитать цену ликвидации
        decimal liquidationPrice = CalculateLiquidationPrice(
            signal.Entry,
            signal.Direction,
            leverage);

        // 4. Определить Stop Loss
        decimal stopLoss;

        if (_settings.StopLossMode == StopLossMode.FromSignal)
        {
            // Проверить что SL достижим до ликвидации
            bool slIsValid = signal.Direction == SignalDirection.Long
                ? signal.OriginalStopLoss > liquidationPrice
                : signal.OriginalStopLoss < liquidationPrice;

            if (slIsValid)
            {
                stopLoss = signal.OriginalStopLoss;
            }
            else
            {
                stopLoss = CalculateSafeStopLoss(signal.Entry, liquidationPrice, signal.Direction);
                warnings.Add($"SL unreachable (liquidation first), adjusted to {stopLoss}");
            }
        }
        else // Calculate
        {
            stopLoss = CalculateSafeStopLoss(signal.Entry, liquidationPrice, signal.Direction);
            warnings.Add($"SL calculated: {stopLoss} (signal SL ignored: {signal.OriginalStopLoss})");
        }

        // 5. Рассчитать R:R
        decimal targetPrice = signal.Targets.FirstOrDefault();
        decimal riskReward = targetPrice > 0
            ? CalculateRiskReward(signal.Entry, stopLoss, targetPrice, signal.Direction)
            : 0;

        if (riskReward < 1.0m && riskReward > 0)
            warnings.Add($"Poor Risk:Reward ratio: {riskReward:F2}");

        // 6. Проверить минимальный размер ордера
        // (будет проверено при размещении)

        return ValidationResult.Success(signal with
        {
            AdjustedStopLoss = stopLoss,
            AdjustedLeverage = leverage,
            LiquidationPrice = liquidationPrice,
            RiskRewardRatio = riskReward,
            IsValid = true,
            ValidationWarnings = warnings
        });
    }

    private decimal CalculateLiquidationPrice(decimal entry, SignalDirection direction, int leverage)
    {
        // Упрощённая формула (реальная зависит от margin type, maintenance margin, etc.)
        decimal liquidationDistance = entry / leverage;

        return direction == SignalDirection.Long
            ? entry - liquidationDistance * 0.98m  // ~2% buffer
            : entry + liquidationDistance * 0.98m;
    }

    private decimal CalculateSafeStopLoss(decimal entry, decimal liquidationPrice, SignalDirection direction)
    {
        // SL на 30% расстояния от entry до liquidation
        decimal distance = Math.Abs(entry - liquidationPrice);
        decimal safeDistance = distance * _settings.SafeDistanceFromLiquidation;

        return direction == SignalDirection.Long
            ? entry - safeDistance
            : entry + safeDistance;
    }

    private decimal CalculateRiskReward(decimal entry, decimal stopLoss, decimal target, SignalDirection direction)
    {
        decimal risk = Math.Abs(entry - stopLoss);
        decimal reward = Math.Abs(target - entry);

        return risk > 0 ? reward / risk : 0;
    }
}
```

### SignalTrader

```csharp
public class SignalTrader : ISignalTrader
{
    private readonly IBinanceFuturesClient _client;
    private readonly IPositionManager _positionManager;
    private readonly IRiskManager _riskManager;
    private readonly TradingSettings _settings;
    private readonly ILogger<SignalTrader> _logger;

    public async Task<SignalPosition> ExecuteSignalAsync(
        TradingSignal signal,
        decimal accountEquity,
        CancellationToken ct = default)
    {
        // 1. Создать позицию в статусе Pending
        var position = new SignalPosition
        {
            SignalId = signal.Id,
            Symbol = signal.Symbol,
            Direction = signal.Direction,
            Status = PositionStatus.Pending,
            PlannedEntryPrice = signal.Entry,
            CurrentStopLoss = signal.AdjustedStopLoss,
            Leverage = signal.AdjustedLeverage,
            Targets = CreateTargetLevels(signal)
        };

        await _positionManager.SavePositionAsync(position, ct);

        try
        {
            // 2. Установить leverage
            await _client.SetLeverageAsync(signal.Symbol, signal.AdjustedLeverage, ct);
            await _client.SetMarginTypeAsync(signal.Symbol, MarginType.Isolated, ct);

            // 3. Рассчитать размер позиции
            var positionSize = _riskManager.CalculatePositionSize(
                accountEquity,
                signal.Entry,
                signal.AdjustedStopLoss);

            position = position with
            {
                InitialQuantity = positionSize.Quantity,
                RemainingQuantity = positionSize.Quantity,
                Status = PositionStatus.Opening
            };
            await _positionManager.SavePositionAsync(position, ct);

            // 4. Открыть позицию (Market order)
            var entryOrder = await _client.PlaceMarketOrderAsync(new OrderRequest
            {
                Symbol = signal.Symbol,
                Side = signal.Direction == SignalDirection.Long ? OrderSide.Buy : OrderSide.Sell,
                Quantity = positionSize.Quantity
            }, ct);

            if (!entryOrder.IsSuccess)
            {
                position = position with { Status = PositionStatus.Failed };
                await _positionManager.SavePositionAsync(position, ct);
                throw new TradingException($"Entry order failed: {entryOrder.Error}");
            }

            position = position with
            {
                EntryOrderId = entryOrder.OrderId,
                ActualEntryPrice = entryOrder.AveragePrice,
                OpenedAt = DateTime.UtcNow,
                Status = PositionStatus.Open
            };

            // 5. Разместить Stop Loss
            var slOrder = await _client.PlaceStopMarketOrderAsync(new OrderRequest
            {
                Symbol = signal.Symbol,
                Side = signal.Direction == SignalDirection.Long ? OrderSide.Sell : OrderSide.Buy,
                Quantity = positionSize.Quantity,
                StopPrice = signal.AdjustedStopLoss,
                ReduceOnly = true
            }, ct);

            position = position with { StopLossOrderId = slOrder.OrderId };

            // 6. Разместить Take Profit ордера
            var tpOrderIds = new List<long>();
            foreach (var target in position.Targets)
            {
                var tpOrder = await _client.PlaceTakeProfitMarketOrderAsync(new OrderRequest
                {
                    Symbol = signal.Symbol,
                    Side = signal.Direction == SignalDirection.Long ? OrderSide.Sell : OrderSide.Buy,
                    Quantity = target.QuantityToClose,
                    StopPrice = target.Price,
                    ReduceOnly = true
                }, ct);

                tpOrderIds.Add(tpOrder.OrderId);
            }

            position = position with { TakeProfitOrderIds = tpOrderIds };
            await _positionManager.SavePositionAsync(position, ct);

            _logger.LogInformation(
                "Position opened: {Symbol} {Direction} @ {Price}, SL: {SL}, Qty: {Qty}",
                position.Symbol, position.Direction, position.ActualEntryPrice,
                position.CurrentStopLoss, position.InitialQuantity);

            return position;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute signal {SignalId}", signal.Id);

            position = position with { Status = PositionStatus.Failed };
            await _positionManager.SavePositionAsync(position, ct);

            throw;
        }
    }

    private IReadOnlyList<TargetLevel> CreateTargetLevels(TradingSignal signal)
    {
        var targets = new List<TargetLevel>();
        var percents = _settings.TargetClosePercents;

        for (int i = 0; i < signal.Targets.Count && i < percents.Count; i++)
        {
            decimal moveSlTo = i == 0
                ? signal.Entry  // После T1 двигаем SL на breakeven
                : signal.Targets[i - 1];  // После T2 двигаем на T1, etc.

            targets.Add(new TargetLevel
            {
                Index = i,
                Price = signal.Targets[i],
                PercentToClose = percents[i],
                MoveStopLossTo = _settings.MoveStopToBreakeven ? moveSlTo : null
            });
        }

        return targets;
    }
}
```

### PositionManager

```csharp
public class PositionManager : IPositionManager
{
    private readonly IPositionStore<SignalPosition> _store;
    private readonly IBinanceFuturesClient _client;
    private readonly INotifier _notifier;
    private readonly ILogger<PositionManager> _logger;

    public async Task HandleTargetHitAsync(
        SignalPosition position,
        int targetIndex,
        decimal fillPrice,
        CancellationToken ct = default)
    {
        var target = position.Targets[targetIndex];

        // 1. Обновить target как hit
        var updatedTargets = position.Targets.Select((t, i) => i == targetIndex
            ? t with { IsHit = true, HitAt = DateTime.UtcNow, ActualClosePrice = fillPrice }
            : t).ToList();

        // 2. Обновить remaining quantity
        decimal closedQty = target.QuantityToClose;
        decimal newRemaining = position.RemainingQuantity - closedQty;

        // 3. Рассчитать realized PnL для этой части
        decimal pnl = CalculatePnl(
            position.ActualEntryPrice,
            fillPrice,
            closedQty,
            position.Direction);

        // 4. Двинуть Stop Loss если нужно
        if (target.MoveStopLossTo.HasValue)
        {
            await MoveStopLossAsync(position, target.MoveStopLossTo.Value, newRemaining, ct);
        }

        // 5. Обновить позицию
        var updatedPosition = position with
        {
            Targets = updatedTargets,
            RemainingQuantity = newRemaining,
            RealizedPnl = position.RealizedPnl + pnl,
            Status = newRemaining <= 0 ? PositionStatus.Closed : PositionStatus.PartialClosed,
            ClosedAt = newRemaining <= 0 ? DateTime.UtcNow : null,
            CloseReason = newRemaining <= 0 ? PositionCloseReason.AllTargetsHit : null
        };

        await _store.SavePositionAsync(updatedPosition, ct);

        // 6. Уведомить
        await _notifier.SendMessageAsync(
            $"🎯 Target {targetIndex + 1} hit!\n" +
            $"Symbol: {position.Symbol}\n" +
            $"Price: {fillPrice}\n" +
            $"PnL: {pnl:+0.00;-0.00} USDT\n" +
            $"Remaining: {newRemaining}",
            ct);

        _logger.LogInformation(
            "Target {Index} hit for {Symbol}: {Price}, PnL: {Pnl}",
            targetIndex + 1, position.Symbol, fillPrice, pnl);
    }

    public async Task HandleStopLossHitAsync(
        SignalPosition position,
        decimal fillPrice,
        CancellationToken ct = default)
    {
        // 1. Отменить все TP ордера
        foreach (var orderId in position.TakeProfitOrderIds)
        {
            await _client.CancelOrderAsync(position.Symbol, orderId, ct);
        }

        // 2. Рассчитать PnL
        decimal pnl = CalculatePnl(
            position.ActualEntryPrice,
            fillPrice,
            position.RemainingQuantity,
            position.Direction);

        // 3. Обновить позицию
        var updatedPosition = position with
        {
            RemainingQuantity = 0,
            RealizedPnl = position.RealizedPnl + pnl,
            Status = PositionStatus.Closed,
            ClosedAt = DateTime.UtcNow,
            CloseReason = PositionCloseReason.StopLossHit
        };

        await _store.SavePositionAsync(updatedPosition, ct);

        // 4. Уведомить
        await _notifier.SendAlertAsync(
            "🛑 Stop Loss Hit",
            $"Symbol: {position.Symbol}\n" +
            $"Entry: {position.ActualEntryPrice}\n" +
            $"Exit: {fillPrice}\n" +
            $"Total PnL: {updatedPosition.RealizedPnl:+0.00;-0.00} USDT",
            ct);
    }

    private async Task MoveStopLossAsync(
        SignalPosition position,
        decimal newStopLoss,
        decimal quantity,
        CancellationToken ct)
    {
        // 1. Отменить старый SL
        if (position.StopLossOrderId.HasValue)
        {
            await _client.CancelOrderAsync(position.Symbol, position.StopLossOrderId.Value, ct);
        }

        // 2. Разместить новый SL
        var newSlOrder = await _client.PlaceStopMarketOrderAsync(new OrderRequest
        {
            Symbol = position.Symbol,
            Side = position.Direction == SignalDirection.Long ? OrderSide.Sell : OrderSide.Buy,
            Quantity = quantity,
            StopPrice = newStopLoss,
            ReduceOnly = true
        }, ct);

        _logger.LogInformation(
            "Stop loss moved for {Symbol}: {OldSL} → {NewSL}",
            position.Symbol, position.CurrentStopLoss, newStopLoss);
    }

    private decimal CalculatePnl(decimal entry, decimal exit, decimal quantity, SignalDirection direction)
    {
        decimal priceDiff = direction == SignalDirection.Long
            ? exit - entry
            : entry - exit;

        return priceDiff * quantity;
    }
}
```

---

## Workflow

### Получение и обработка сигнала

```
┌──────────────────────────────────────────────────────────────────┐
│                    SIGNAL PROCESSING FLOW                         │
└──────────────────────────────────────────────────────────────────┘

┌─────────────┐
│  Telegram   │
│  Channel    │
└──────┬──────┘
       │ New message
       ▼
┌─────────────┐     ┌─────────────┐
│  Telegram   │     │ Duplicate   │
│  Listener   │────▶│ Check       │──── Already processed? ──▶ Skip
└──────┬──────┘     └─────────────┘
       │
       ▼
┌─────────────┐
│  Signal     │──── Parse failed? ──▶ Log warning, skip
│  Parser     │
└──────┬──────┘
       │ TradingSignal
       ▼
┌─────────────┐
│  Signal     │──── Validation failed? ──▶ Notify, skip
│  Validator  │
└──────┬──────┘
       │ Validated signal
       ▼
┌─────────────┐
│ Concurrent  │──── Too many positions? ──▶ Queue or skip
│ Position    │
│ Check       │
└──────┬──────┘
       │
       ▼
┌─────────────┐
│  Signal     │──── Order failed? ──▶ Notify error
│  Trader     │
└──────┬──────┘
       │ Position opened
       ▼
┌─────────────┐
│  Position   │
│  Manager    │
│  (monitor)  │
└─────────────┘
```

### Закрытие позиции

```
┌──────────────────────────────────────────────────────────────────┐
│                    POSITION CLOSE FLOW                            │
└──────────────────────────────────────────────────────────────────┘

┌─────────────┐
│  WebSocket  │
│  Order      │
│  Updates    │
└──────┬──────┘
       │ Order filled event
       ▼
┌─────────────┐
│  Order      │
│  Monitor    │
└──────┬──────┘
       │
       ├─── Stop Loss Order? ────────────────┐
       │                                      ▼
       │                             ┌─────────────┐
       │                             │ Handle SL   │
       │                             │ - Cancel TPs│
       │                             │ - Calc PnL  │
       │                             │ - Close pos │
       │                             └─────────────┘
       │
       └─── Take Profit Order? ──────────────┐
                                              ▼
                                     ┌─────────────┐
                                     │ Handle TP   │
                                     │ - Update qty│
                                     │ - Move SL   │
                                     │ - Calc PnL  │
                                     └──────┬──────┘
                                            │
                              ┌─────────────┴─────────────┐
                              │                           │
                              ▼                           ▼
                     ┌─────────────┐            ┌─────────────┐
                     │ More targets│            │ All targets │
                     │ remaining   │            │ hit         │
                     │             │            │             │
                     │ Keep        │            │ Close       │
                     │ monitoring  │            │ position    │
                     └─────────────┘            └─────────────┘
```

---

## Startup и Shutdown

### Startup Flow

```csharp
public class SignalBotRunner
{
    public async Task RunAsync(CancellationToken ct)
    {
        // 1. Загрузить состояние
        var state = await _stateManager.LoadStateAsync(ct);

        if (state != null)
        {
            _logger.LogInformation("Restored state: {Positions} open positions",
                state.OpenPositions.Count);

            // 2. Reconcile с биржей
            var reconcileResult = await _reconciler.ReconcileAsync(state, ct);

            foreach (var mismatch in reconcileResult.PositionsMismatch)
            {
                _logger.LogWarning("Position mismatch: {Symbol} - local vs exchange",
                    mismatch.Symbol);
            }

            // 3. Восстановить мониторинг открытых позиций
            foreach (var position in reconcileResult.PositionsConfirmed)
            {
                await _positionManager.StartMonitoringAsync(position, ct);
            }
        }

        // 4. Получить баланс
        var account = await _client.GetAccountInfoAsync(ct);
        _logger.LogInformation("Account balance: {Balance} USDT", account.Balance);

        // 5. Подключиться к Telegram
        await _telegramListener.ConnectAsync(ct);

        // 6. Подписаться на WebSocket для order updates
        await _orderMonitor.StartAsync(ct);

        // 7. Запустить обработку сигналов
        await _telegramListener.StartListeningAsync(OnSignalReceived, ct);

        _logger.LogInformation("SignalBot started successfully");

        // 8. Ждать завершения
        await ct.WhenCanceled();
    }
}
```

### Shutdown Flow

```csharp
public async Task ShutdownAsync(ShutdownAction action, CancellationToken ct)
{
    _logger.LogInformation("Initiating shutdown with action: {Action}", action);

    // 1. Остановить приём новых сигналов
    await _telegramListener.StopListeningAsync(ct);

    // 2. Сохранить текущее состояние
    await _stateManager.SaveStateAsync(BuildCurrentState(), ct);

    // 3. Выполнить действие
    switch (action)
    {
        case ShutdownAction.KeepPositionsAndOrders:
            // Ничего не делаем - позиции и ордера остаются на бирже
            break;

        case ShutdownAction.ClosePositions:
            foreach (var position in _openPositions)
            {
                await ClosePositionAtMarketAsync(position, ct);
            }
            break;

        case ShutdownAction.CancelOrdersKeepPositions:
            // Отменить SL/TP, но оставить позиции (ОПАСНО!)
            foreach (var position in _openPositions)
            {
                await CancelAllOrdersAsync(position, ct);
            }
            break;
    }

    // 4. Отключиться от WebSocket
    await _orderMonitor.StopAsync(ct);

    // 5. Отключиться от Telegram
    await _telegramListener.DisconnectAsync(ct);

    // 6. Финальное сохранение
    await _stateManager.SaveStateAsync(BuildCurrentState(), ct);

    // 7. Уведомить
    await _notifier.SendMessageAsync(
        $"🔴 SignalBot shutdown\nAction: {action}\nPositions: {_openPositions.Count}",
        ct);
}
```

---

## Зависимости

### NuGet пакеты

```xml
<ItemGroup>
  <!-- Telegram -->
  <PackageReference Include="WTelegramClient" Version="4.0.0" />

  <!-- Logging -->
  <PackageReference Include="Serilog" Version="3.1.1" />
  <PackageReference Include="Serilog.Sinks.Console" Version="5.0.1" />
  <PackageReference Include="Serilog.Sinks.File" Version="5.0.0" />

  <!-- Configuration -->
  <PackageReference Include="Microsoft.Extensions.Configuration" Version="8.0.0" />
  <PackageReference Include="Microsoft.Extensions.Configuration.Json" Version="8.0.0" />
  <PackageReference Include="Microsoft.Extensions.Configuration.EnvironmentVariables" Version="8.0.0" />
  <PackageReference Include="DotNetEnv" Version="3.0.0" />

  <!-- CLI -->
  <PackageReference Include="Spectre.Console" Version="0.49.1" />
</ItemGroup>

<ItemGroup>
  <ProjectReference Include="..\TradingBot.Core\TradingBot.Core.csproj" />
  <ProjectReference Include="..\TradingBot.Binance\TradingBot.Binance.csproj" />
</ItemGroup>
```

---

## Тестирование

### Unit Tests

```
SignalBot.Tests/
├── Parsing/
│   ├── SignalParserTests.cs           # Парсинг разных форматов
│   └── SignalParserEdgeCasesTests.cs  # Edge cases, невалидные сигналы
│
├── Validation/
│   ├── SignalValidatorTests.cs        # Валидация параметров
│   └── LiquidationCalculatorTests.cs  # Расчёт цены ликвидации
│
├── Trading/
│   ├── SignalTraderTests.cs           # Mock Binance client
│   └── PositionManagerTests.cs        # Target hit, SL hit scenarios
│
└── State/
    └── StateRecoveryTests.cs          # Восстановление после restart
```

### Integration Tests

```
SignalBot.Integration/
├── TelegramListenerTests.cs           # Реальное подключение к TG
├── BinanceFuturesTests.cs             # Testnet trading
└── EndToEndTests.cs                   # Полный цикл сигнал → позиция → закрытие
```

---

## Мониторинг и метрики

### Логирование

```
[2024-01-15 10:30:15 INF] Signal received from channel "CryptoSignals"
[2024-01-15 10:30:15 INF] Parsed signal: ICNTUSDT Long @ 0.3709
[2024-01-15 10:30:15 WRN] SL adjusted: 0.18569 → 0.3593 (original unreachable)
[2024-01-15 10:30:15 WRN] Leverage adjusted: 32x → 10x (max limit)
[2024-01-15 10:30:16 INF] Position opened: ICNTUSDT Long @ 0.3710, Qty: 100
[2024-01-15 10:30:16 INF] SL order placed: 12345 @ 0.3593
[2024-01-15 10:30:16 INF] TP orders placed: 12346, 12347, 12348, 12349
[2024-01-15 10:45:30 INF] Target 1 hit: ICNTUSDT @ 0.37225, PnL: +1.35 USDT
[2024-01-15 10:45:30 INF] SL moved: 0.3593 → 0.3709 (breakeven)
```

### Telegram уведомления

```
📥 Signal Received
Symbol: ICNTUSDT
Direction: Long
Entry: 0.3709
Adjusted SL: 0.3593 (was 0.18569)
Targets: 0.37225, 0.37243, 0.37362, 0.37452
Leverage: 10x (was 32x)

---

✅ Position Opened
Symbol: ICNTUSDT Long
Entry: 0.3710
Quantity: 100
Stop Loss: 0.3593
Risk: 1.0% ($10.00)

---

🎯 Target 1 Hit!
Symbol: ICNTUSDT
Price: 0.37225
Closed: 25 (25%)
PnL: +1.35 USDT
SL moved to: 0.3709 (breakeven)

---

🛑 Stop Loss Hit
Symbol: ICNTUSDT
Entry: 0.3710
Exit: 0.3593
Total PnL: -3.51 USDT
```

---

## TODO / Будущие улучшения

- [ ] Поддержка нескольких форматов сигналов (разные каналы)
- [ ] Фильтрация сигналов по символам (whitelist/blacklist)
- [ ] Отложенный вход (Limit order вместо Market)
- [ ] Trailing stop после достижения target
- [ ] Dashboard для мониторинга (web UI)
- [ ] Статистика по каналам (какой канал прибыльнее)
- [ ] Backtest сигналов из истории канала
- [ ] Интеграция с Discord
- [ ] Мобильные push-уведомления
