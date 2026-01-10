# Отключение Futures в SignalBot - Краткое руководство

## Проблема

При запуске SignalBot вы получаете ошибку:
```
[19:01:10 WRN] Futures account balances failed: Invalid API-key, IP, or permissions
[19:01:10 ERR] Failed to start SignalBot
[19:01:10 FTL] SignalBot terminated unexpectedly
System.InvalidOperationException: Failed to connect to Binance Futures API
```

## Решение: Отключить Futures и использовать режим мониторинга

В режиме мониторинга SignalBot будет:
- ✅ Слушать Telegram каналы
- ✅ Парсить сигналы
- ✅ Логировать и валидировать сигналы  
- ✅ Отправлять уведомления
- ❌ **НЕ выполнять** торговые операции

## 3 способа отключить Futures

### 1️⃣ Быстрый способ (через переменную окружения)

**Windows PowerShell:**
```powershell
$env:TRADING_SignalBot__EnableFuturesTrading = "false"
cd C:\code\TradingBot\SignalBot
dotnet run
```

**Windows Command Prompt:**
```cmd
set TRADING_SignalBot__EnableFuturesTrading=false
cd C:\code\TradingBot\SignalBot
dotnet run
```

**Linux/Mac:**
```bash
export TRADING_SignalBot__EnableFuturesTrading=false
cd ~/TradingBot/SignalBot
dotnet run
```

### 2️⃣ Через appsettings.json (постоянно)

Отредактируйте `SignalBot/appsettings.json`:

```json
{
  "SignalBot": {
    "EnableFuturesTrading": false,     // ← Добавить эту строку (false)
    
    "Telegram": {
      ...
    }
  }
}
```

### 3️⃣ Через .env файл

Создайте/отредактируйте `.env` в папке `SignalBot`:

```dotenv
TRADING_SignalBot__EnableFuturesTrading=false
TRADING_BinanceApi__UseTestnet=true
```

## Ожидаемый результат

```
[19:01:06 INF] SignalBot starting up...
[19:01:06 INF] Using Binance Testnet API
[19:01:06 INF] Futures Trading: DISABLED
[19:01:08 INF] Starting SignalBot...
[19:01:10 WRN] ⚠️ Futures trading is DISABLED in configuration
[19:01:10 INF] SignalBot will run in monitoring-only mode
[19:01:10 INF] ✅ SignalBot started in MONITORING-ONLY mode (no trading)
```

## Использование в Docker

```bash
docker run \
  -e TRADING_SignalBot__EnableFuturesTrading=false \
  -e TRADING_BinanceApi__UseTestnet=true \
  -v $(pwd)/.env:/app/.env \
  signalbot:latest
```

## docker-compose.yml

```yaml
services:
  signalbot:
    environment:
      - TRADING_SignalBot__EnableFuturesTrading=false
      - TRADING_BinanceApi__UseTestnet=true
```

## Когда это потребуется?

- 🔐 Проблемы с API ключами (неправильные права, IP whitelist)
- 🧪 Тестирование парсинга сигналов без торговли
- 📊 Мониторинг паттернов перед включением торговли
- 🔒 Безопасная работа в режиме "только чтение"

## Возврат к нормальному режиму

Когда API ключи будут исправлены:

```powershell
$env:TRADING_SignalBot__EnableFuturesTrading = "true"
dotnet run
```

Или отредактируйте `appsettings.json`:
```json
"EnableFuturesTrading": true
```

## Полная документация

📖 Подробное руководство: [DISABLE_FUTURES_GUIDE.md](./DISABLE_FUTURES_GUIDE.md)

## Поддержка

Если возникли вопросы:
1. Проверьте логи на предмет конкретной ошибки
2. Убедитесь, что используются правильные API ключи (Testnet vs Mainnet)
3. Проверьте IP whitelist в Binance settings
4. Обратитесь к документации Binance API
