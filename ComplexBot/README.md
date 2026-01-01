# Trading Bot - ADX Trend Following с Volume Confirmation

## Описание

Торговый бот для Binance на .NET 8, реализующий среднесрочную стратегию тренд-фолловинга с упором на стабильность. Целевые метрики основаны на исследовании:
- **Sharpe Ratio**: 1.5-1.9
- **Max Drawdown**: < 20%
- **Holding period**: дни-недели

## Архитектура

```
ComplexBot/
├── Models/
│   └── Models.cs           # Candle, Trade, Signal, Metrics
├── Services/
│   ├── Indicators/         
│   │   └── Indicators.cs   # EMA, SMA, ATR, ADX, MACD, OBV, Volume
│   ├── Strategies/         
│   │   └── AdxTrendStrategy.cs  # Основная стратегия
│   ├── RiskManagement/     
│   │   └── RiskManager.cs  # Position sizing, drawdown control
│   ├── Backtesting/        
│   │   ├── BacktestEngine.cs
│   │   ├── HistoricalDataLoader.cs
│   │   ├── WalkForwardAnalyzer.cs
│   │   └── MonteCarloSimulator.cs
│   └── Trading/            
│       └── BinanceLiveTrader.cs  # Live/Paper trading
└── Program.cs              # CLI интерфейс
```

## Стратегия

**ADX Trend Following + Volume Confirmation**

### Условия входа:
1. ADX > 25 (подтверждение тренда)
2. Fast EMA (20) > Slow EMA (50) для Long / наоборот для Short
3. +DI > -DI для Long / -DI > +DI для Short
4. MACD histogram положительный/отрицательный
5. ATR% цены в заданном диапазоне (фильтр волатильности)
6. ADX растёт относительно среднего за N баров (опционально)
7. Volume >= 1.5x от среднего (подтверждение объёмом)
8. OBV растёт/падает в направлении сделки

### Условия выхода:
- ATR-based trailing stop (2.5x ATR)
- ADX падает ниже 18 (ослабление тренда)

### Risk Management:
- 1.5% риска на сделку
- Max portfolio heat: 15%
- Drawdown circuit breaker: 20%
- Position sizing на основе ATR

## Установка

```bash
cd ComplexBot
dotnet restore
dotnet build
```

## Использование

```bash
dotnet run
```

### Режимы:
1. **Backtest** - тестирование на исторических данных
2. **Walk-Forward Analysis** - валидация робастности (WFE > 50%)
3. **Monte Carlo Simulation** - анализ рисков
4. **Live Trading (Paper)** - paper trading на testnet
5. **Live Trading (Real)** - реальная торговля
6. **Download Data** - загрузка исторических данных

## Конфигурация

### Настройки стратегии (StrategySettings):
```csharp
AdxPeriod = 14;
AdxThreshold = 25m;
AdxExitThreshold = 18m;
RequireAdxRising = false;
AdxSlopeLookback = 5;
FastEmaPeriod = 20;
SlowEmaPeriod = 50;
AtrPeriod = 14;
AtrStopMultiplier = 2.5m;
TakeProfitMultiplier = 1.5m;  // 1.5:1 reward:risk
MinAtrPercent = 0m;
MaxAtrPercent = 100m;
VolumeThreshold = 1.5m;
RequireVolumeConfirmation = true;
RequireObvConfirmation = true;
```

### Настройки риска (RiskSettings):
```csharp
RiskPerTradePercent = 1.5m;
MaxPortfolioHeatPercent = 15m;
MaxDrawdownPercent = 20m;
```

## Testnet

Для тестирования без реальных денег:
1. Регистрация: https://testnet.binance.vision/
2. Создание API ключей
3. Выбор "Use Binance Testnet" при запуске

## Метрики качества

После backtest проверяйте:
- **Sharpe Ratio** > 1.0 (хорошо > 1.5)
- **Max Drawdown** < 20%
- **Profit Factor** > 1.5
- **Win Rate** > 40% (с хорошим avg win/loss ratio)
- **Walk-Forward Efficiency** > 50%
- **Monte Carlo Ruin Probability** < 5%

## Зависимости

- Binance.Net 10.3.0
- CryptoExchange.Net 8.3.0
- MathNet.Numerics 5.0.0
- Spectre.Console 0.49.1

## ⚠️ Disclaimer

Этот бот предназначен для образовательных целей. Торговля криптовалютой несёт значительные риски. Всегда тестируйте стратегии на paper trading перед использованием реальных средств.
