# ComplexBot Unit Tests

Отдельный проект для unit тестирования критических компонентов торгового бота.

## Структура

```
ComplexBot.Tests/
├── RiskManagerTests.cs          # Тесты системы управления рисками
├── IndicatorsTests.cs           # Тесты технических индикаторов (EMA, ATR, ADX)
├── AdxTrendStrategyTests.cs     # Тесты стратегии ADX Trend Following
├── BacktestEngineTests.cs       # Тесты бэктестинг движка
├── TradeJournalTests.cs         # Тесты журнала сделок
└── README.md
```

## Запуск тестов

```bash
cd ComplexBot.Tests
dotnet test
```

### Запуск конкретного файла тестов

```bash
dotnet test --filter "ClassName=ComplexBot.Tests.RiskManagerTests"
```

### Запуск с verbose выводом

```bash
dotnet test -v detailed
```

### Запуск с coverage (требует coverlet)

```bash
dotnet test /p:CollectCoverage=true
```

## Компоненты

### RiskManagerTests
Тесты для проверки:
- Расчета размера позиции с учетом стоп-лосса
- Применения снижения риска при drawdown
- Отслеживания дневного лимита потерь
- Расчета portfolio heat

**Ключевые тесты:**
- `CalculatePositionSize_WithNormalDrawdown_ReturnsFullSize` - размер позиции без drawdown
- `GetDrawdownAdjustedRisk_WithHighDrawdown_ReducesRiskSignificantly` - редукция риска при 20% drawdown
- `IsDailyLimitExceeded_WithExceededLimit_ReturnsTrue` - проверка дневного лимита

### IndicatorsTests
Тесты для проверки:
- EMA (Exponential Moving Average)
- SMA (Simple Moving Average)
- ATR (Average True Range)
- ADX (Average Directional Index) + DI+/DI-

**Ключевые тесты:**
- `Ema_WithKnownValues_ReturnsCorrectResult` - корректность расчета EMA
- `Atr_WithGapUp_IncludesTrueRange` - учет gaps при расчете ATR
- `Adx_InUptrend_ReturnsHighValue` - высокое значение ADX в тренде
- `Adx_InRangingMarket_ReturnsLowValue` - низкое значение ADX в боковом движении

### AdxTrendStrategyTests
Тесты для проверки:
- Генерация сигналов покупки/продажи
- Проверка условий входа (ADX > threshold, EMA cross, volume confirmation)
- Проверка условий выхода
- Реакция на различные рыночные условия

**Ключевые тесты:**
- `Analyze_WithBullishSetup_ReturnsBuySignal` - сигнал при бычьей установке
- `Analyze_WithLowAdx_ReturnsNoneSignal` - отсутствие сигнала при низком ADX
- `Analyze_ConsecutiveBullishCandles_BuildsTrend` - нарастание тренда

### BacktestEngineTests
Тесты для проверки:
- Запуск бэктеста на исторических данных
- Расчет метрик прибыльности
- Применение комиссий
- Влияние слиппажа на результаты

**Ключевые тесты:**
- `Run_WithSimpleUptrend_GeneratesProfit` - прибыль на восходящем тренде
- `Run_WithRangingMarket_MinimizesTrades` - минимизация сделок в боковом движении
- `Run_WithConsecLosses_AppliesDrawdownAdjustment` - применение правила Джерри Паркера

### TradeJournalTests
Тесты для проверки:
- Открытие и закрытие сделок
- Расчет статистики (win rate, R-multiple)
- Экспорт в CSV
- Отслеживание метрик MAE/MFE

**Ключевые тесты:**
- `OpenTrade_ReturnsUniqueTradeId` - уникальные ID сделок
- `GetStats_WithWinningTrades_CalculatesWinRate` - расчет процента побеждающих сделок
- `ExportToCsv_CreatesValidFile` - создание CSV файла журнала
- `GetAllTrades_ReturnsAllOpenedTrades` - извлечение всех сделок

## Знакомые тестовые паттерны

### Arrange-Act-Assert

```csharp
[Fact]
public void Test_Description()
{
    // Arrange - подготовка данных
    var strategy = new AdxTrendStrategy();
    var candles = GenerateTestCandles(10);

    // Act - выполнение тестируемого действия
    var signal = strategy.Analyze(candles[0], null, "BTCUSDT");

    // Assert - проверка результатов
    Assert.NotNull(signal);
    Assert.Equal(SignalType.Buy, signal.Type);
}
```

### Генерация тестовых свечей

```csharp
var candles = TestDataFactory.GenerateUptrendCandles(10);
```

## Текущее состояние тестов

- **Всего тестов:** 39
- **Успешных:** 32+
- **Требуют доработки:** 7 (связаны с реальным поведением индикаторов)

## Дальнейшие улучшения

1. **Добавить моки** для внешних зависимостей (API Binance, файловая система)
2. **Увеличить покрытие** до 80%+ для критических путей
3. **Добавить интеграционные тесты** на Binance Testnet
4. **Добавить performance тесты** для проверки скорости расчетов

## Использование в CI/CD

Рекомендуется добавить запуск тестов в процесс CI/CD:

```yaml
# .github/workflows/test.yml
name: Tests
on: [push, pull_request]
jobs:
  test:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v3
      - uses: actions/setup-dotnet@v3
        with:
          dotnet-version: '8.0'
      - run: dotnet test ComplexBot.Tests --verbosity minimal
```

## Ресурсы

- [xUnit Documentation](https://xunit.net/docs/getting-started/netcore)
- [Moq - Mocking library](https://github.com/moq/moq4)
- [Unit Testing Best Practices](https://docs.microsoft.com/en-us/dotnet/core/testing/)
