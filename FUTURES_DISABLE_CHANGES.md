# Изменения: Отключение Futures в SignalBot

## Дата: 10 января 2026

## Проблема

SignalBot падал с ошибкой при недоступности/неправильной конфигурации Binance Futures API:
```
System.InvalidOperationException: Failed to connect to Binance Futures API
```

Не было способа продолжить работу в режиме только мониторинга.

## Решение

Добавлена опция `EnableFuturesTrading` для отключения требования к Futures API и работы в режиме только мониторинга.

## Измененные файлы

### 1. SignalBot/Configuration/SignalBotSettings.cs
✅ Добавлено свойство:
```csharp
/// <summary>
/// Enable/disable Futures trading. If disabled, only monitoring is available
/// </summary>
public bool EnableFuturesTrading { get; set; } = true;
```

### 2. SignalBot/appsettings.json
✅ Добавлена конфигурация:
```json
"SignalBot": {
  "EnableFuturesTrading": true,
  ...
}
```

### 3. SignalBot/Program.cs
✅ Добавлена поддержка переменной окружения:
```csharp
// Override EnableFuturesTrading from environment if specified
if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TRADING_SignalBot__EnableFuturesTrading")))
{
    signalBotSettings.EnableFuturesTrading = 
        bool.Parse(Environment.GetEnvironmentVariable("TRADING_SignalBot__EnableFuturesTrading")!);
}

Log.Information("Futures Trading: {Status}", 
    signalBotSettings.EnableFuturesTrading ? "ENABLED" : "DISABLED");
```

### 4. SignalBot/SignalBotRunner.cs
✅ Переработан метод `StartAsync()`:
- Проверка `EnableFuturesTrading` перед подключением к Futures API
- При отключении Futures → переход в `StartInMonitoringOnlyMode()`
- Улучшенная обработка ошибок с подсказками

✅ Добавлен новый метод `StartInMonitoringOnlyMode()`:
- Слушает Telegram каналы
- Логирует сигналы как "MONITORING ONLY"
- НЕ выполняет торговые операции
- Отправляет соответствующие уведомления

### 5. SignalBot/.env.example
✅ Добавлена новая переменная с комментариями:
```dotenv
# Enable/disable Futures trading
# true = trading enabled (default)
# false = monitoring-only mode
TRADING_SignalBot__EnableFuturesTrading=true
```

## Новые файлы документации

### 6. SignalBot/DISABLE_FUTURES_GUIDE.md
📖 Полное руководство включает:
- Обзор режима мониторинга
- 3 способа конфигурации (appsettings.json, env переменные, .env файл)
- Примеры логов для каждого режима
- Troubleshooting и решение проблем с API
- Docker примеры
- Переход между режимами

### 7. DISABLE_FUTURES_QUICKSTART.md (в корне)
🚀 Краткое руководство для быстрого запуска в режиме мониторинга

## Поведение

### ✅ С Futures ВКЛЮЧЕНЫ (EnableFuturesTrading = true)
```
[INF] Starting SignalBot...
[INF] Connected to Binance Futures API
[INF] SignalBot started successfully
```
→ Нормальная работа с торговлей

### ⚠️ С Futures ОТКЛЮЧЕНЫ (EnableFuturesTrading = false)
```
[WRN] ⚠️ Futures trading is DISABLED in configuration
[INF] SignalBot will run in monitoring-only mode
[INF] ✅ SignalBot started in MONITORING-ONLY mode (no trading)
```
→ Режим мониторинга, без торговли

### ❌ Futures ОТКЛЮЧЕНЫ + Ошибка подключения
```
[WRN] ⚠️ Futures API credentials issue detected
[INF] To disable Futures trading, set 'EnableFuturesTrading' to false
[INF] Or set environment variable: TRADING_SignalBot__EnableFuturesTrading=false
```
→ Подсказка пользователю

## Использование

### Способ 1: Переменная окружения (быстро)
```powershell
$env:TRADING_SignalBot__EnableFuturesTrading = "false"
dotnet run
```

### Способ 2: appsettings.json (постоянно)
```json
"EnableFuturesTrading": false
```

### Способ 3: .env файл
```
TRADING_SignalBot__EnableFuturesTrading=false
```

## Режим мониторинга

Когда Futures отключены, SignalBot:

| Функция | Статус |
|---------|--------|
| Прослушивание Telegram | ✅ |
| Парсинг сигналов | ✅ |
| Валидация сигналов | ✅ |
| Логирование сигналов | ✅ |
| Уведомления | ✅ |
| Размещение ордеров | ❌ |
| Управление позициями | ❌ |
| Мониторинг стоп-лосса | ❌ |

## Тестирование

✅ Проверено:
- Сборка проекта без ошибок
- EnableFuturesTrading = true → нормальное поведение
- EnableFuturesTrading = false → режим мониторинга
- Переменная окружения переопределяет конфиг
- Логирование корректного статуса

## Возможные дальнейшие улучшения

1. Добавить CLI флаг для быстрого переключения режимов
2. Хранить режим в runtime состоянии (менять без перезагрузки)
3. Добавить метрику о количестве пропущенных торговых сигналов
4. Интеграция с системой оповещений о смене режима
5. Web UI для переключения режимов

## Обратная совместимость

✅ **Полная обратная совместимость**
- По умолчанию `EnableFuturesTrading = true`
- Существующие конфигурации работают без изменений
- Старые логи и конфиги совместимы

---

**Статус**: ✅ Завершено и протестировано  
**Время**: ~30 минут
