# VolumeFilter - Usage Guide

## Overview

`VolumeFilter` - унифицированный фильтр объема для подтверждения торговых сигналов. Инкапсулирует общую логику проверки объема, используемую во всех стратегиях.

## Проблема (до рефакторинга)

Каждая стратегия дублировала проверку объема:

```csharp
// AdxTrendStrategy.cs
bool volumeConfirmed = !Settings.RequireVolumeConfirmation ||
    (_volume.IsReady && _volume.VolumeRatio >= Settings.VolumeThreshold);

// MaStrategy.cs
bool volumeOk = !Settings.RequireVolumeConfirmation ||
    (_volume.IsReady && _volume.VolumeRatio >= Settings.VolumeThreshold);

// RsiStrategy.cs
bool volumeOk = !Settings.RequireVolumeConfirmation ||
    (_volume.IsReady && _volume.VolumeRatio >= Settings.VolumeThreshold);
```

**Нарушение DRY (Don't Repeat Yourself)** - одна и та же логика повторяется в 3+ местах.

## Решение (после рефакторинга)

Единый `VolumeFilter` класс:

```csharp
// Создание фильтра
_volumeFilter = new VolumeFilter(
    period: 20,                    // Период для расчета среднего объема
    threshold: 1.5m,               // Минимальный коэффициент (текущий/средний)
    isRequired: true               // Требуется ли проверка (false = всегда OK)
);

// Обновление данных
_volumeFilter.Update(candle.Volume);

// Проверка подтверждения
if (_volumeFilter.IsConfirmed())
{
    // Объем подтверждает сигнал
}
```

## API Reference

### Конструктор

```csharp
public VolumeFilter(
    int period = 20,           // Период для SMA объема
    decimal threshold = 1.0m,  // Минимальный VolumeRatio
    bool isRequired = true     // Если false, фильтр всегда пропускает
)
```

### Методы

**`Update(decimal volume)`** - обновляет индикатор новым значением объема

**`IsConfirmed()`** - проверяет подтверждение объема
- Возвращает `true` если:
  - `isRequired == false` (проверка отключена), ИЛИ
  - Индикатор готов И `VolumeRatio >= threshold`

**`Reset()`** - сбрасывает состояние фильтра

**`GetDiagnostics()`** - возвращает строку с диагностической информацией

### Свойства

- **`VolumeRatio`** - текущий объем / средний объем
- **`IsReady`** - готов ли индикатор (достаточно данных)
- **`AverageVolume`** - средний объем за период
- **`CurrentVolume`** - текущий объем
- **`IsVolumeSpike`** - превышен ли threshold (всплеск объема)

## Использование в стратегиях

### Пример: AdxTrendStrategy

**До:**
```csharp
private readonly VolumeIndicator _volume;

public AdxTrendStrategy(StrategySettings? settings = null)
{
    _volume = new VolumeIndicator(Settings.VolumePeriod, Settings.VolumeThreshold);
}

protected override void UpdateIndicators(Candle candle)
{
    _volume.Update(candle.Volume);
}

protected override TradeSignal? CheckEntryConditions(...)
{
    bool volumeConfirmed = !Settings.RequireVolumeConfirmation ||
        (_volume.IsReady && _volume.VolumeRatio >= Settings.VolumeThreshold);

    if (/* ... */ && volumeConfirmed)
    {
        // Entry
    }
}

public override void Reset()
{
    _volume.Reset();
}
```

**После:**
```csharp
private readonly VolumeFilter _volumeFilter;

public AdxTrendStrategy(StrategySettings? settings = null)
{
    _volumeFilter = new VolumeFilter(
        Settings.VolumePeriod,
        Settings.VolumeThreshold,
        Settings.RequireVolumeConfirmation
    );
}

protected override void UpdateIndicators(Candle candle)
{
    _volumeFilter.Update(candle.Volume);
}

protected override TradeSignal? CheckEntryConditions(...)
{
    bool volumeConfirmed = _volumeFilter.IsConfirmed();

    if (/* ... */ && volumeConfirmed)
    {
        // Entry
    }
}

public override void Reset()
{
    _volumeFilter.Reset();
}
```

### Преимущества

✅ **DRY** - логика в одном месте
✅ **Меньше кода** - 1 строка вместо 3
✅ **Единообразие** - все стратегии используют одинаковый подход
✅ **Легче тестировать** - фильтр можно протестировать отдельно
✅ **Расширяемость** - легко добавить новую логику (напр., trailing average)

## Диагностика

```csharp
// Получение детальной информации
var diagnostics = _volumeFilter.GetDiagnostics();
Console.WriteLine(diagnostics);
// Output: "Vol: 1.85x avg (req: 1.50x, status: OK)"
```

## Расширение

Можно создать другие фильтры по тому же паттерну:

### TrendFilter
```csharp
public class TrendFilter
{
    private readonly Ema _ema;
    private readonly bool _isRequired;

    public bool IsConfirmed(decimal price)
    {
        if (!_isRequired) return true;
        return IsReady && price > _ema.Value;
    }
}
```

### VolatilityFilter
```csharp
public class VolatilityFilter
{
    private readonly Atr _atr;
    private readonly decimal _minAtr;

    public bool IsConfirmed()
    {
        return IsReady && _atr.Value >= _minAtr;
    }
}
```

## Migration Checklist

При переносе стратегии на `VolumeFilter`:

1. ✅ Добавить `using ComplexBot.Services.Filters;`
2. ✅ Заменить `VolumeIndicator _volume` → `VolumeFilter _volumeFilter`
3. ✅ Обновить конструктор: передать `isRequired` параметр
4. ✅ Заменить все `!Settings.RequireVolumeConfirmation || (_volume.IsReady && _volume.VolumeRatio >= Settings.VolumeThreshold)` на `_volumeFilter.IsConfirmed()`
5. ✅ Заменить `_volume.VolumeRatio` на `_volumeFilter.VolumeRatio` в логах
6. ✅ Заменить `_volume.Update()` на `_volumeFilter.Update()`
7. ✅ Заменить `_volume.Reset()` на `_volumeFilter.Reset()`

## Примеры использования

### Базовая проверка
```csharp
var filter = new VolumeFilter(period: 20, threshold: 1.5m);
filter.Update(1000); // Update with volume data
if (filter.IsConfirmed())
{
    Console.WriteLine("High volume confirmed!");
}
```

### Отключение фильтра
```csharp
var filter = new VolumeFilter(isRequired: false);
// IsConfirmed() всегда возвращает true
```

### Проверка всплеска объема
```csharp
if (filter.IsVolumeSpike)
{
    Console.WriteLine($"Volume spike detected: {filter.VolumeRatio:F2}x average");
}
```

## Связанные компоненты

- **VolumeIndicator** ([Indicators.cs:294-328](ComplexBot/Services/Indicators/Indicators.cs#L294-L328)) - низкоуровневый индикатор
- **VolumeFilter** ([VolumeFilter.cs](ComplexBot/Services/Filters/VolumeFilter.cs)) - высокоуровневый фильтр
- **AdxTrendStrategy** - использует VolumeFilter
- **MaStrategy** - использует VolumeFilter
- **RsiStrategy** - использует VolumeFilter
