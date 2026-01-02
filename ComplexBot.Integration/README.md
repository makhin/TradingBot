# ComplexBot Integration Tests

Интеграционные тесты для проверки работы торгового бота на Binance Testnet и с реальными конфигурациями.

## 📋 Структура проекта

```
ComplexBot.Integration/
├── appsettings.json                     # Конфигурация (скопирована из ComplexBot)
├── ComplexBot.Integration.csproj       # Файл проекта
├── README.md                            # Этот файл
│
├── IntegrationTestFixture.cs            # Базовый fixture для загрузки конфигурации
├── ConfigurationIntegrationTests.cs     # Проверка конфигурации и settings
├── BinanceLiveTraderIntegrationTests.cs # Тесты торговли на testnet
└── StrategyIntegrationTests.cs          # Тесты стратегии с live данными
```

## 🔧 Требования

### Для всех тестов:
- .NET 8.0+
- `appsettings.json` в выходной директории
- Конфигурация в YAML/JSON формате

### Для тестов Binance Testnet:
- **Binance Testnet API ключи** (отличаются от mainnet!)
- Минимум **USDT 10-50** на testnet счете
- Интернет соединение

### Получение testnet ключей:

1. Перейти на [Binance Testnet](https://testnet.binance.vision/)
2. Войти с обычным Binance аккаунтом
3. Сгенерировать API ключ в "API Management"
4. Скопировать API Key и Secret

## ⚙️ Конфигурация

### Способ 1: appsettings.json (локально)

```json
{
  "BinanceApi": {
    "ApiKey": "your-testnet-key-here",
    "ApiSecret": "your-testnet-secret-here",
    "UseTestnet": true
  },
  "LiveTrading": {
    "UseTestnet": true,
    "PaperTrade": false
  },
  ...
}
```

⚠️ **ВАЖНО:** Никогда не коммитьте реальные ключи в Git!

### Способ 2: Environment Variables

```bash
export TRADING_BinanceApi__ApiKey="your-testnet-key"
export TRADING_BinanceApi__ApiSecret="your-testnet-secret"
export TRADING_BinanceApi__UseTestnet="true"
```

### Способ 3: .env файл (для локальной разработки)

```bash
# .env (в папке ComplexBot.Integration/)
TRADING_BinanceApi__ApiKey=your-testnet-key
TRADING_BinanceApi__ApiSecret=your-testnet-secret
TRADING_BinanceApi__UseTestnet=true
```

Затем загрузить перед запуском:
```bash
source .env
dotnet test
```

## 🚀 Запуск тестов

### 1. Все тесты
```bash
cd ComplexBot.Integration
dotnet test
```

### 2. Только тесты конфигурации (всегда работают)
```bash
dotnet test --filter "ClassName=ComplexBot.Integration.ConfigurationIntegrationTests"
```

### 3. Только тесты на Binance Testnet
```bash
dotnet test --filter "ClassName=ComplexBot.Integration.BinanceLiveTraderIntegrationTests"
```

### 4. Только тесты стратегии
```bash
dotnet test --filter "ClassName=ComplexBot.Integration.StrategyIntegrationTests"
```

### 5. С подробным выводом
```bash
dotnet test -v detailed
```

### 6. Запуск конкретного теста
```bash
dotnet test --filter "Name~GetAccountBalance_ReturnsValidBalance"
```

## 📊 Описание тестов

### ConfigurationIntegrationTests ✅
Не требуют API ключей, всегда работают:
- `Configuration_LoadsSuccessfully` - загрузка appsettings.json
- `BinanceApiSettings_AreConfigured` - проверка API конфигурации
- `RiskManagementSettings_AreValid` - валидация risk settings
- `StrategySettings_AreConfigured` - проверка параметров стратегии
- `RiskSettings_FollowBestPractices` - проверка лучших практик

**Статус:** ✅ Работают всегда

### BinanceLiveTraderIntegrationTests ⏭️
Требуют Binance Testnet API ключей:
- `GetAccountBalance_ReturnsValidBalance` - получение баланса
- `PlaceMarketOrder_Buy_Succeeds` - маркет ордер покупки
- `PlaceMarketOrder_Sell_ClosesPosition` - маркет ордер продажи
- `PlaceOcoOrder_CreatesValidOrder` - OCO ордер (стоп + тейк)
- `GetCurrentPrice_ReturnsValidPrice` - получение текущей цены
- `MultipleOrdersSequentially_ExecuteCorrectly` - последовательные ордера
- `CanExecuteMultipleSymbols_Sequentially` - торговля несколькими парами

**Статус:** ⏭️ Пропущены по умолчанию (требуют конфига)

### StrategyIntegrationTests ⏭️
Тестируют стратегию с симулированными данными:
- `Strategy_WithLiveData_GeneratesSignals` - генерация сигналов
- `Strategy_InTrend_GeneratesBuySignals` - сигналы в тренде
- `Strategy_InRangeMarket_MinimizesSignals` - фильтрация в боковике
- `Strategy_SignalProvidesStopAndTarget` - проверка стоп/тейк
- `Strategy_HandlesDifferentTimeframes` - разные таймфреймы
- `Strategy_RecoveryAfterReset` - восстановление после сброса

**Статус:** ⏭️ Пропущены по умолчанию

## 🔓 Включение пропущенных тестов

Все тесты с `[Fact(Skip = "...")]` пропущены по умолчанию для безопасности.

### Способ 1: Убрать Skip атрибут

```csharp
// Было:
[Fact(Skip = "Requires valid Testnet API credentials")]
public async Task GetAccountBalance_ReturnsValidBalance()

// Стало:
[Fact]
public async Task GetAccountBalance_ReturnsValidBalance()
```

### Способ 2: Переменная окружения

```bash
# Включить все тесты
export INTEGRATION_TESTS_ENABLED=true
dotnet test

# Включить только тесты конфигурации (это работает всегда)
dotnet test --filter "ClassName=ComplexBot.Integration.ConfigurationIntegrationTests"
```

## 💡 Примеры использования

### Проверить конфигурацию
```bash
cd ComplexBot.Integration
dotnet test --filter "Configuration"
# Все тесты пройдут ✅
```

### Подключиться к Testnet и получить баланс
```bash
# 1. Добавить ключи в appsettings.json
# 2. Раскомментировать Skip в BinanceLiveTraderIntegrationTests
# 3. Запустить:
dotnet test --filter "GetAccountBalance"
```

### Протестировать маркет ордера
```bash
# После получения баланса:
dotnet test --filter "PlaceMarketOrder"
# Тест:
# 1. Купит 0.001 BTC маркет ордером
# 2. Продаст его обратно
# 3. Проверит балансы
```

### Запустить полный цикл на testnet
```bash
# Все операции Binance:
dotnet test --filter "ClassName=ComplexBot.Integration.BinanceLiveTraderIntegrationTests"
```

## ⚠️ Важные замечания

### Безопасность
- 🔐 **Никогда** не коммитьте реальные API ключи
- ✅ Используйте только Testnet ключи для разработки
- 🛡️ Хранитe ключи в environment variables или в .env (добавьте в .gitignore)

### Стоимость
- ✅ Все операции на Testnet **бесплатны**
- ✅ Можно неограниченно тестировать
- ⚠️ Реальные деньги используются только на Mainnet

### Отладка
Если тесты не работают:

1. **Проверить конфигурацию:**
   ```bash
   dotnet test --filter "ConfigurationIntegrationTests" -v detailed
   ```

2. **Проверить подключение к testnet:**
   ```bash
   dotnet test --filter "GetAccountBalance" -v detailed
   ```

3. **Убедиться в наличии средств на testnet:**
   - Перейти на https://testnet.binance.vision/
   - Получить testnet USDT (есть кран)

4. **Проверить API ключи:**
   - Убедиться, что это **testnet** ключи
   - Проверить, что ключи **активны** и **не истекли**
   - Проверить **IP whitelist** (если включен)

## 🔄 Рабочий процесс CI/CD

### GitHub Actions пример:

```yaml
name: Integration Tests
on: [push, pull_request]
jobs:
  integration-tests:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v3
      - uses: actions/setup-dotnet@v3
        with:
          dotnet-version: '8.0'

      # 1. Запустить тесты конфигурации (всегда работают)
      - name: Test Configuration
        run: dotnet test ComplexBot.Integration --filter "ConfigurationIntegrationTests"

      # 2. Запустить стратегию с тестовыми данными (не требуют API)
      - name: Test Strategy
        run: dotnet test ComplexBot.Integration --filter "StrategyIntegrationTests"

      # 3. Binance тесты только при наличии секретов
      - name: Test Binance Integration
        if: secrets.BINANCE_TESTNET_KEY != ''
        run: dotnet test ComplexBot.Integration --filter "BinanceLiveTraderIntegrationTests"
        env:
          TRADING_BinanceApi__ApiKey: ${{ secrets.BINANCE_TESTNET_KEY }}
          TRADING_BinanceApi__ApiSecret: ${{ secrets.BINANCE_TESTNET_SECRET }}
```

## 📊 Типичный вывод

```
Test run for C:\TradingBot\ComplexBot.Integration\bin\Debug\net8.0\ComplexBot.Integration.dll

Starting test execution, please wait...

[xUnit.net] ComplexBot.Integration.ConfigurationIntegrationTests.Configuration_LoadsSuccessfully [PASS]
[xUnit.net] ComplexBot.Integration.ConfigurationIntegrationTests.RiskSettings_AreValid [PASS]
[xUnit.net] ComplexBot.Integration.ConfigurationIntegrationTests.StrategySettings_AreConfigured [PASS]
...

Test run summary:
  Passed:      12
  Skipped:     7 (Binance tests - require credentials)
  Total:       19

Duration: 2.5 seconds
```

## 🎯 Дальнейшие улучшения

- [ ] WebSocket stream для live тестирования
- [ ] Performance тесты для обработки speed
- [ ] Multi-symbol тесты
- [ ] Stress тесты на high volatility
- [ ] Сохранение результатов в БД для анализа

## 📚 Полезные ссылки

- [Binance Testnet](https://testnet.binance.vision/)
- [Binance API Documentation](https://binance-docs.github.io/apidocs/)
- [Binance.NET Library](https://github.com/JKorf/Binance.Net)
- [xUnit Documentation](https://xunit.net/docs/getting-started/netcore)

## ⚡ Быстрый старт

```bash
# 1. Клонировать репо
git clone <repo>
cd TradingBot

# 2. Получить testnet ключи на https://testnet.binance.vision/

# 3. Установить ключи
export TRADING_BinanceApi__ApiKey="your-key"
export TRADING_BinanceApi__ApiSecret="your-secret"
export TRADING_BinanceApi__UseTestnet="true"

# 4. Запустить тесты конфигурации (работают всегда)
cd ComplexBot.Integration
dotnet test --filter "ConfigurationIntegrationTests"

# 5. Запустить все тесты
dotnet test
```

---

**Status:** ✅ Ready for integration testing on Binance Testnet
**Last Updated:** 2026-01-02
