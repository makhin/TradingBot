# SignalBot

Автоматическое исполнение торговых сигналов из Telegram на Binance Futures.

## Статус реализации

### ✅ Реализовано

**Модели данных:**
- `TradingSignal` - Сигнал из Telegram с валидацией и корректировкой
- `SignalPosition` - Позиция с таргетами, стоп-лоссом и P&L
- `TargetLevel` - Уровни take profit
- `SignalBotState` - Состояние бота для persistence
- Enums: `SignalDirection`, `PositionStatus`, `PositionCloseReason`

**Конфигурация (11 классов):**
- `SignalBotSettings` - Основные настройки
- `TelegramSettings` - WTelegramClient настройки
- `TradingSettings` - Настройки торговли
- `RiskOverrideSettings` - Переопределение риск-параметров
- `PositionSizingSettings` - Расчёт размера позиции
- `DuplicateHandlingSettings` - Обработка дубликатов
- `EntrySettings`, `CooldownSettings`, `EmergencySettings`
- `NotificationSettings`, `StateSettings`

**Services:**
- `SignalParser` - Парсинг сигналов из Telegram (regex)
- `SignalValidator` - Валидация и корректировка leverage, SL, liquidation price
- `SignalTrader` - Исполнение сигналов на Binance Futures
  - Установка leverage и margin type
  - Открытие позиции (market order)
  - Размещение stop-loss и take-profit ордеров
  - Retry logic для всех операций
- `PositionManager` - Управление позициями
  - Обработка достижения targets
  - Частичное закрытие позиции
  - Движение stop-loss в breakeven
  - Расчёт P&L
  - Уведомления

**State Persistence:**
- `IPositionStore<T>` - Интерфейс хранилища
- `JsonPositionStore` - JSON файловое хранилище

**TradingBot.Binance расширения:**
- `IFuturesOrderExecutor` - Расширенный интерфейс с SL/TP методами
- `FuturesOrderExecutor` реализует `IFuturesOrderExecutor`
- `ExecutionResult.OrderId` - Добавлено поле для tracking

**Telegram Integration:**
- ✅ `ITelegramSignalListener` - Интерфейс Telegram listener
- ✅ `TelegramSignalListener` - WTelegramClient интеграция
- ✅ Подключение к каналам через WTelegram.Client
- ✅ Обработка сообщений (UpdateNewMessage, UpdateNewChannelMessage)
- ✅ Дедупликация по message ID
- ✅ Автоматическая аутентификация (phone, code, 2FA)

**Monitoring:**
- ✅ `OrderMonitor` - Мониторинг ордеров через WebSocket
- ✅ Обработка order updates (заполнение targets/SL)
- ✅ Event-based уведомления (OnTargetHit, OnStopLossHit)

**Main Runner:**
- ✅ `SignalBotRunner` - Основной orchestrator
- ✅ Инициализация компонентов через DI
- ✅ Lifecycle management (StartAsync/StopAsync)
- ✅ Graceful shutdown с CancellationToken
- ✅ Event-driven signal flow

**Program.cs & DI:**
- ✅ Dependency injection setup
- ✅ Configuration loading (appsettings.json + environment)
- ✅ Binance REST/WebSocket clients
- ✅ Logging с Serilog

### 🚧 Требуется реализация

**Advanced Features:**
- Duplicate signal handling (same/opposite direction)
- Position sizing modes (FixedAmount, RiskPercent, FixedMargin)
- Entry timing (price deviation handling)
- Cooldown после losses
- Emergency circuit breaker
- Portfolio-level risk management

**Testing:**
- Unit tests для SignalParser, SignalValidator
- Integration tests для SignalTrader, PositionManager
- End-to-end тестирование с testnet

## Формат сигналов

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

**Пример:**
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

## Конфигурация

Основная конфигурация в [appsettings.json](appsettings.json).

**Переменные окружения (.env):**
```bash
# Telegram API
TELEGRAM_API_ID=12345678
TELEGRAM_API_HASH=your_api_hash
TELEGRAM_PHONE=+1234567890

# Telegram Bot для уведомлений
TELEGRAM_BOT_TOKEN=your_bot_token
TELEGRAM_CHAT_ID=your_chat_id

# Binance Futures
BINANCE_TESTNET_KEY=your_testnet_key
BINANCE_TESTNET_SECRET=your_testnet_secret
```

## Зависимости

- **WTelegramClient 4.0.0** - Telegram MTProto клиент
- **TradingBot.Core** - Базовые модели и интерфейсы
- **TradingBot.Binance** - Binance Futures API клиент
- **Serilog 4.3.0** - Логирование
- **Spectre.Console 0.54.0** - CLI интерфейс

## Build

```bash
dotnet build SignalBot/SignalBot.csproj
```

## Архитектура

```
Telegram Channel
    ↓
TelegramSignalListener
    ↓
SignalParser → TradingSignal
    ↓
SignalValidator → Validated TradingSignal
    ↓
SignalTrader → SignalPosition
    ↓
PositionManager → Target tracking, SL movement
    ↓
JsonPositionStore → Persistence
```

## Следующие шаги

1. ✅ ~~Реализовать `TelegramSignalListener` с WTelegramClient~~
2. ✅ ~~Создать `OrderMonitor` для WebSocket updates~~
3. ✅ ~~Реализовать `SignalBotRunner` с полным lifecycle~~
4. 🚧 Добавить duplicate handling logic
5. 🚧 Реализовать advanced position sizing modes
6. 🚧 Добавить cooldown и emergency circuit breaker
7. 🚧 Написать unit и integration тесты
8. 📝 Создать пример .env файла
9. 📝 Документировать процесс получения Telegram API credentials

## Дизайн документ

Полный дизайн: [docs/SIGNALBOT_DESIGN.md](../docs/SIGNALBOT_DESIGN.md)
