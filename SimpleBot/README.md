# Trading Bot для Binance

Автоматический торговый бот с использованием стратегии скользящих средних (Moving Average).

## Настройка

1. Скопируйте `appsettings.example.json` в `appsettings.json`
2. Укажите свои API ключи от Binance Testnet
3. Настройте валютную пару и параметры торговли

## Конфигурация валютной пары

В файле `appsettings.json` можно настроить любую торговую пару:

```json
"Trading": {
  "CryptoCurrency": "BTC",     // Базовая валюта (криптовалюта)
  "QuoteCurrency": "USDT",     // Котируемая валюта (фиат/стейблкоин)
  "MinTradeAmount": 10.0       // Минимальная сумма сделки
}
```

### Примеры различных пар:

**Ethereum / USDT:**
```json
"CryptoCurrency": "ETH",
"QuoteCurrency": "USDT",
"MinTradeAmount": 10.0
```

**Solana / USDC:**
```json
"CryptoCurrency": "SOL",
"QuoteCurrency": "USDC",
"MinTradeAmount": 10.0
```

**Dogecoin / BTC:**
```json
"CryptoCurrency": "DOGE",
"QuoteCurrency": "BTC",
"MinTradeAmount": 0.0001
```

## Запуск

```bash
dotnet run
```

## Режимы работы

- **Testnet** (по умолчанию): `"UseTestnet": true` - тестовая торговля без реальных денег
- **Live**: `"UseTestnet": false` - **ОСТОРОЖНО!** Реальная торговля с настоящими деньгами

## Выбор торговой стратегии

Бот поддерживает три стратегии. Выберите нужную через параметр `"Type"`:

### 1. Moving Average (MA)
```json
"Strategy": {
  "Type": "MA",
  "ShortPeriod": 5,   // Период короткой MA
  "LongPeriod": 20    // Период длинной MA
}
```
- **Golden Cross** (покупка): короткая MA пересекает длинную MA снизу вверх
- **Death Cross** (продажа): короткая MA пересекает длинную MA сверху вниз

### 2. RSI (Relative Strength Index)
```json
"Strategy": {
  "Type": "RSI",
  "RsiPeriod": 14,      // Период расчета RSI
  "RsiOverbought": 70,  // Уровень перекупленности
  "RsiOversold": 30     // Уровень перепроданности
}
```
- **Покупка**: RSI < 30 (актив перепродан)
- **Продажа**: RSI > 70 (актив перекуплен)

### 3. Bollinger Bands
```json
"Strategy": {
  "Type": "BollingerBands",
  "BollingerPeriod": 20,    // Период расчета
  "BollingerStdDev": 2      // Множитель станд. отклонения
}
```
- **Покупка**: цена касается нижней полосы
- **Продажа**: цена касается верхней полосы

### 4. Composite (MA + RSI)
```json
"Strategy": {
  "Type": "Composite",
  "ShortPeriod": 5,         // Период короткой MA
  "LongPeriod": 20,         // Период длинной MA
  "RsiPeriod": 14,          // Период RSI
  "RsiOverbought": 70,      // Уровень перекупленности RSI
  "RsiOversold": 30         // Уровень перепроданности RSI
}
```
- **Покупка**: обе стратегии (MA и RSI) дают сигнал BUY одновременно
- **Продажа**: обе стратегии (MA и RSI) дают сигнал SELL одновременно
- Более консервативная стратегия, меньше ложных сигналов
