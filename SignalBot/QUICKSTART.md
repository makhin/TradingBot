# SignalBot Quick Start

Быстрая инструкция для опытных пользователей.

## Установка

```bash
cd SignalBot
dotnet restore
dotnet build
```

## Настройка

1. **Скопируйте .env файл:**
   ```bash
   cp .env.example .env
   ```

2. **Получите credentials:**
   - Telegram API: https://my.telegram.org/apps
   - Binance Testnet: https://testnet.binance.vision/
   - Telegram Bot (опционально): @BotFather

3. **Заполните .env:**
   ```bash
   TELEGRAM_API_ID=12345678
   TELEGRAM_API_HASH=your_hash
   TELEGRAM_PHONE=+1234567890

   BINANCE_TESTNET_KEY=your_key
   BINANCE_TESTNET_SECRET=your_secret

   TRADING_BinanceApi__UseTestnet=true
   ```

4. **Настройте appsettings.json:**
   ```json
   {
     "SignalBot": {
       "Telegram": {
         "ChannelIds": [-1001234567890]
       },
       "RiskOverride": {
         "MaxLeverage": 10,
         "RiskPerTradePercent": 1.0,
         "MaxDrawdownPercent": 20.0
       },
       "Trading": {
         "MaxConcurrentPositions": 5
       }
     }
   }
   ```

## Запуск

```bash
dotnet run
```

При первом запуске введите:
1. Verification code из Telegram
2. 2FA password (если включен)

## Формат сигнала

```
#BTC/USDT - Long🟢

Entry: 50000
Stop Loss: 49000

Target 1: 50500
Target 2: 51000

Leverage: x5
```

## Проверка

1. Логи: `logs/signalbot-*.txt`
2. Binance Testnet: https://testnet.binance.vision/
3. State: `signalbot_state.json`

## Production

⚠️ **Только после тестирования!**

```bash
# В .env измените:
TRADING_BinanceApi__UseTestnet=false
```

## Документация

- Полная инструкция: [SETUP.md](SETUP.md)
- Дизайн: [../docs/SIGNALBOT_DESIGN.md](../docs/SIGNALBOT_DESIGN.md)
- README: [README.md](README.md)
