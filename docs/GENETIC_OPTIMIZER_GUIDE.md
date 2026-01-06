# Genetic Optimizer - Usage Guide

## Overview

`GeneticOptimizer<TSettings>` - обобщенный (generic) генетический алгоритм для оптимизации параметров любой стратегии. Использует паттерн делегатов для специфичных операций со стратегиями.

## Архитектура

### До рефакторинга
```csharp
// Жестко привязано к StrategySettings (ADX)
var optimizer = new GeneticOptimizer(settings);
var result = optimizer.Optimize(candles, symbol, riskSettings, backtestSettings);
```

**Проблемы:**
- Hardcoded параметры ADX стратегии
- Невозможно оптимизировать другие стратегии (MA, RSI, Ensemble)
- Код мутации/кроссовера дублируется для каждой стратегии

### После рефакторинга
```csharp
// Generic optimizer с делегатами
var optimizer = new GeneticOptimizer<TSettings>(
    createRandom,    // () => TSettings
    mutate,          // TSettings => TSettings
    crossover,       // (TSettings, TSettings) => TSettings
    validate,        // TSettings => bool
    settings
);

var result = optimizer.Optimize(evaluateFitness); // TSettings => decimal
```

**Преимущества:**
- Один оптимизатор для всех стратегий
- Специфичная логика вынесена в делегаты
- Легко добавлять новые стратегии
- Разделение ответственности (SRP)

## Использование

### Вариант 1: AdxStrategyOptimizer (Helper)

Для ADX стратегии уже есть готовый helper:

```csharp
using ComplexBot.Services.Backtesting;

var candles = dataLoader.LoadCandles("BTCUSDT", KlineInterval.OneHour, 1000);

// Создаем ADX оптимизатор с дефолтной конфигурацией
var adxOptimizer = new AdxStrategyOptimizer();

// Запускаем оптимизацию
var result = adxOptimizer.Optimize(
    candles,
    "BTCUSDT",
    settings: new GeneticOptimizerSettings
    {
        PopulationSize = 100,
        Generations = 50
    },
    progress: new Progress<GeneticProgress<StrategySettings>>(p =>
    {
        Console.WriteLine($"Gen {p.CurrentGeneration}: Best={p.BestFitness:F2}");
    })
);

Console.WriteLine($"Best ADX settings: {result.BestSettings}");
Console.WriteLine($"Fitness: {result.BestFitness:F2}");
```

### Вариант 2: Кастомная стратегия (Manual)

Для любой другой стратегии создайте делегаты:

```csharp
var random = new Random();

// 1. CreateRandom - создание случайных настроек
Func<MaStrategySettings> createRandom = () => new MaStrategySettings
{
    FastMaPeriod = random.Next(5, 20),
    SlowMaPeriod = random.Next(25, 100),
    AtrStopMultiplier = 1.5m + (decimal)random.NextDouble() * 2.5m,
    VolumeThreshold = 1.0m + (decimal)random.NextDouble() * 1.5m
};

// 2. Mutate - мутация одного параметра
Func<MaStrategySettings, MaStrategySettings> mutate = settings =>
{
    var param = random.Next(4);
    return param switch
    {
        0 => settings with { FastMaPeriod = MutateInt(settings.FastMaPeriod, 5, 20) },
        1 => settings with { SlowMaPeriod = MutateInt(settings.SlowMaPeriod, 25, 100) },
        2 => settings with { AtrStopMultiplier = MutateDecimal(settings.AtrStopMultiplier, 1.5m, 4.0m) },
        _ => settings with { VolumeThreshold = MutateDecimal(settings.VolumeThreshold, 1.0m, 2.5m) }
    };
};

// 3. Crossover - скрещивание двух родителей
Func<MaStrategySettings, MaStrategySettings, MaStrategySettings> crossover = (p1, p2) =>
    new MaStrategySettings
    {
        FastMaPeriod = Pick(p1.FastMaPeriod, p2.FastMaPeriod),
        SlowMaPeriod = Pick(p1.SlowMaPeriod, p2.SlowMaPeriod),
        AtrStopMultiplier = Pick(p1.AtrStopMultiplier, p2.AtrStopMultiplier),
        VolumeThreshold = Pick(p1.VolumeThreshold, p2.VolumeThreshold)
    };

// 4. Validate - проверка корректности
Func<MaStrategySettings, bool> validate = settings =>
    settings.FastMaPeriod < settings.SlowMaPeriod;

// Создаем оптимизатор
var optimizer = new GeneticOptimizer<MaStrategySettings>(
    createRandom,
    mutate,
    crossover,
    validate,
    new GeneticOptimizerSettings { PopulationSize = 80, Generations = 40 }
);

// 5. Evaluate - фитнес функция
Func<MaStrategySettings, decimal> evaluateFitness = settings =>
{
    var strategy = new MaStrategy(settings);
    var engine = new BacktestEngine(strategy, riskSettings, backtestSettings);
    var result = engine.Run(candles, symbol);

    if (result.Metrics.TotalTrades < 20) return -100m;
    return result.Metrics.SharpeRatio;
};

// Запуск
var result = optimizer.Optimize(evaluateFitness);
```

## Конфигурация

### GeneticOptimizerSettings (Generic)

Общие настройки для всех стратегий:

```json
{
  "GeneticOptimizer": {
    "PopulationSize": 100,        // Размер популяции
    "Generations": 50,             // Количество поколений
    "EliteCount": 5,               // Сколько лучших особей переходят без изменений
    "TournamentSize": 5,           // Размер турнира для селекции
    "CrossoverRate": 0.8,          // Вероятность кроссовера (0-1)
    "MutationRate": 0.15,          // Вероятность мутации (0-1)
    "EarlyStoppingPatience": 10,   // Остановка если нет улучшения N поколений
    "EarlyStoppingThreshold": 0.01, // Минимальное улучшение
    "RandomSeed": null             // Seed для воспроизводимости (null = random)
  }
}
```

### AdxOptimizerConfig (Strategy-specific)

Параметры ranges для ADX стратегии:

```json
{
  "AdxOptimizer": {
    "AdxPeriodMin": 10,
    "AdxPeriodMax": 25,
    "AdxThresholdMin": 18.0,
    "AdxThresholdMax": 35.0,
    "FastEmaMin": 8,
    "FastEmaMax": 30,
    "SlowEmaMin": 35,
    "SlowEmaMax": 100,
    "AtrMultiplierMin": 1.5,
    "AtrMultiplierMax": 4.0,
    "VolumeThresholdMin": 1.0,
    "VolumeThresholdMax": 2.5
  }
}
```

## Создание оптимизатора для новой стратегии

Пример для RSI стратегии:

```csharp
public class RsiStrategyOptimizer
{
    private readonly Random _random = new();

    public GeneticOptimizer<RsiStrategySettings> CreateOptimizer()
    {
        return new GeneticOptimizer<RsiStrategySettings>(
            createRandom: CreateRandom,
            mutate: Mutate,
            crossover: Crossover,
            validate: Validate,
            settings: new GeneticOptimizerSettings()
        );
    }

    private RsiStrategySettings CreateRandom() => new()
    {
        RsiPeriod = _random.Next(10, 20),
        OversoldLevel = 20m + (decimal)_random.NextDouble() * 15m,
        OverboughtLevel = 65m + (decimal)_random.NextDouble() * 15m,
        // ... другие параметры
    };

    private RsiStrategySettings Mutate(RsiStrategySettings settings)
    {
        // Мутировать случайный параметр
        var param = _random.Next(7);
        return param switch
        {
            0 => settings with { RsiPeriod = MutateInt(settings.RsiPeriod, 10, 20) },
            1 => settings with { OversoldLevel = MutateDecimal(settings.OversoldLevel, 20m, 35m) },
            // ...
        };
    }

    private RsiStrategySettings Crossover(RsiStrategySettings p1, RsiStrategySettings p2) => new()
    {
        RsiPeriod = Pick(p1.RsiPeriod, p2.RsiPeriod),
        OversoldLevel = Pick(p1.OversoldLevel, p2.OversoldLevel),
        // ...
    };

    private bool Validate(RsiStrategySettings settings) =>
        settings.OversoldLevel < 50 && settings.OverboughtLevel > 50;
}
```

## Fitness Functions

Доступные функции оценки (AdxStrategyOptimizer):

- **Sharpe**: Коэффициент Шарпа
- **Sortino**: Коэффициент Сортино
- **ProfitFactor**: Profit Factor
- **Return**: Общая доходность
- **RiskAdjusted**: Sharpe - penalty за большие просадки
- **Combined**: Sharpe × (1 + PF/10) × (1 - MaxDD/100)

```csharp
var optimizer = new AdxStrategyOptimizer(
    fitnessFunction: FitnessFunction.RiskAdjusted,
    minTrades: 30
);
```

## Результаты оптимизации

```csharp
public record GeneticOptimizationResult<TSettings>
{
    TSettings BestSettings;               // Лучшие найденные настройки
    decimal BestFitness;                  // Лучший фитнес
    List<GenerationStats<TSettings>> GenerationHistory;  // История поколений
    decimal ConvergenceRate;              // Скорость сходимости
}
```

## Performance

- **Параллельная оценка**: Используется `Parallel.ForEach` для оценки популяции
- **Early stopping**: Останавливается при отсутствии улучшений
- **Elitism**: Сохраняет лучшие решения между поколениями

## Примеры кода

### Оптимизация ADX через helper

```csharp
var adxOptimizer = new AdxStrategyOptimizer();
var settings = new GeneticOptimizerSettings
{
    PopulationSize = 100,
    Generations = 50
};

var result = adxOptimizer.Optimize(
    candles,
    symbol,
    settings,
    progress: new Progress<GeneticProgress<StrategySettings>>(p =>
    {
        Log.Information("Generation {CurrentGeneration}/{TotalGenerations}: Best={BestFitness:F2}",
            p.CurrentGeneration,
            p.TotalGenerations,
            p.BestFitness);
    })
);
```

### Оптимизация MA вручную (делегаты)

```csharp
var optimizer = new GeneticOptimizer<MaStrategySettings>(
    createRandom,
    mutate,
    crossover,
    validate,
    new GeneticOptimizerSettings { PopulationSize = 50, Generations = 30 }
);

var result = optimizer.Optimize(evaluateFitness);
```

### Создание оптимизатора для RSI

```csharp
var rsiOptimizer = new GeneticOptimizer<RsiStrategySettings>(
    createRandom: () => new RsiStrategySettings { /* ... */ },
    mutate: settings => settings with { /* ... */ },
    crossover: (p1, p2) => new RsiStrategySettings { /* ... */ },
    validate: settings => settings.OversoldLevel < 50 && settings.OverboughtLevel > 50,
    settings: new GeneticOptimizerSettings { PopulationSize = 80, Generations = 40 }
);
```

## Migration Guide

### Старый код
```csharp
var optimizer = new GeneticOptimizer(settings);
var result = optimizer.Optimize(candles, symbol, riskSettings, backtestSettings);
```

### Новый код
```csharp
var optimizer = new AdxStrategyOptimizer(
    config: adxConfig,
    riskSettings: riskSettings,
    backtestSettings: backtestSettings
);
var result = optimizer.Optimize(candles, symbol, geneticSettings);
```

## Расширяемость

Для добавления новой стратегии:

1. Создайте `XxxStrategySettings` record
2. Создайте `XxxStrategyOptimizer` класс с делегатами
3. Опционально добавьте конфиг в `appsettings.json`
4. Используйте `GeneticOptimizer<XxxStrategySettings>`

**Без копипаста!** Вся логика генетического алгоритма в одном месте.
