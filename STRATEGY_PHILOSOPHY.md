# Trading Strategy Philosophy

## Overview

Торговый бот поддерживает **две противоположные торговые философии**, которые можно использовать отдельно или комбинировать через Strategy Ensemble.

## Философии

### 1. Trend Following (Следование за трендом)

**Стратегии:**
- [AdxTrendStrategy.cs](ComplexBot/Services/Strategies/AdxTrendStrategy.cs:40-52) - ADX Trend Following + Volume
- [MaStrategy.cs](ComplexBot/Services/Strategies/MaStrategy.cs:7-16) - MA Crossover

**Принцип:**
```
ТОРГУЕТ С ТРЕНДОМ (enters when trend confirmed)

📈 Uptrend:   Fast EMA > Slow EMA, ADX > 25  →  BUY
📉 Downtrend: Fast EMA < Slow EMA, ADX > 25  →  SELL
😴 No trend:  ADX < 20                       →  WAIT
```

**Когда работает лучше:**
- ✅ Сильные трендовые движения (ADX > 25)
- ✅ Пробои уровней с импульсом
- ✅ Trending markets (Forex majors, crypto в bull/bear)

**Когда работает хуже:**
- ❌ Боковики (ranging markets, ADX < 20)
- ❌ Частые ложные пробои
- ❌ Choppy/volatile markets без направления

**Характеристики:**
- Win Rate: 40-50% (много мелких стопов, редкие большие прибыли)
- Risk/Reward: 1:2 или выше (TP > SL)
- Drawdown: Средний (может быть серия стопов в боковике)

---

### 2. Mean Reversion (Возврат к среднему)

**Стратегии:**
- [RsiStrategy.cs](ComplexBot/Services/Strategies/RsiStrategy.cs:7-18) - RSI Mean Reversion

**Принцип:**
```
ТОРГУЕТ ПРОТИВ ЭКСТРЕМУМОВ (enters at extremes, expects bounce)

📉 RSI < 30 (oversold)   →  BUY (expects bounce up)
📈 RSI > 70 (overbought) →  SELL (expects pullback down)
```

**Когда работает лучше:**
- ✅ Боковые рынки (ranging/oscillating)
- ✅ После резких импульсных движений (exhaustion)
- ✅Ликвидные инструменты с "памятью среднего"

**Когда работает хуже:**
- ❌ Сильные тренды (RSI может оставаться >70 или <30 долго)
- ❌ Breakouts (входит слишком рано против импульса)
- ❌ Trending markets с momentum

**Характеристики:**
- Win Rate: 60-70% (много мелких прибылей, редкие большие стопы)
- Risk/Reward: 1:1 или ниже (часто фиксирует быстро)
- Drawdown: Низкий в ranging, высокий в trending

---

## Strategy Ensemble: Комбинация философий

[StrategyEnsemble.cs](ComplexBot/Services/Strategies/StrategyEnsemble.cs:46-57)

### Идея

Объединить противоположные подходы для **диверсификации**:
- Trend Following (75% вес) - доминирует в трендах
- Mean Reversion (25% вес) - фильтруется в трендах, активна в боковиках

### Default Weights

```csharp
"StrategyWeights": {
  "ADX Trend Following + Volume": 0.5,   // 50% - primary trend
  "MA Crossover": 0.25,                   // 25% - secondary trend
  "RSI Mean Reversion": 0.25              // 25% - counter-trend
}
```

**Логика весов:**
- **75% Trend Following (ADX + MA)**: В сильном тренде эти стратегии дают сигнал, RSI молчит → consensus ≥ 60% → ENTRY
- **25% Mean Reversion (RSI)**: В боковике RSI может дать сигнал, но ADX/MA фильтруют → consensus < 60% → NO ENTRY (защита)

### Примеры работы

#### Сценарий 1: Сильный тренд вверх

```
[14:00:00] Ensemble voting:
  ADX Trend: BUY  (confidence: 0.80, weight: 0.5)  → 0.40
  MA Cross:  BUY  (confidence: 0.70, weight: 0.25) → 0.175
  RSI:       NONE (confidence: 0.00, weight: 0.25) → 0.00
  ───────────────────────────────────────────────────────
  Total score: 0.575 / 1.0 = 57.5%

  ❌ NO ENTRY (< 60% MinimumAgreement)
```

Если увеличить ADX confidence до 0.85:
```
  ADX: 0.85 × 0.5 = 0.425
  MA:  0.70 × 0.25 = 0.175
  RSI: 0.00 × 0.25 = 0.00
  ─────────────────────────
  Total: 60% → ✅ ENTRY
```

#### Сценарий 2: Боковик с откатом

```
[16:00:00] Ensemble voting:
  ADX Trend: NONE (confidence: 0.00, weight: 0.5)  → 0.00
  MA Cross:  NONE (confidence: 0.00, weight: 0.25) → 0.00
  RSI:       BUY  (confidence: 0.75, weight: 0.25) → 0.1875
  ───────────────────────────────────────────────────────
  Total score: 18.75%

  ❌ NO ENTRY (trend filters protect against false RSI signals)
```

#### Сценарий 3: Все согласны (идеальная ситуация)

```
[18:00:00] Ensemble voting:
  ADX Trend: BUY (confidence: 0.85, weight: 0.5)  → 0.425
  MA Cross:  BUY (confidence: 0.80, weight: 0.25) → 0.20
  RSI:       BUY (confidence: 0.65, weight: 0.25) → 0.1625
  ───────────────────────────────────────────────────────
  Total score: 78.75%

  ✅ STRONG ENTRY (all philosophies agree - rare but high quality)
```

### Преимущества Ensemble

✅ **Фильтрация ложных сигналов** - RSI не входит в контртренд без одобрения ADX/MA
✅ **Адаптация к рынку** - автоматически снижает активность в неподходящих условиях
✅ **Диверсификация** - не зависит от одной философии
✅ **Улучшенный Sharpe** - меньше drawdown при сопоставимой доходности

❌ **Меньше сделок** - требуется consensus (60%), фильтрует много сигналов
❌ **Сложнее анализировать** - нужно понимать взаимодействие стратегий

---

## Выбор режима

### Single Strategy Mode (по умолчанию)

```json
{
  "Ensemble": {
    "Enabled": false  // Uses only AdxTrendStrategy
  }
}
```

**Когда использовать:**
- Вы торгуете трендовые инструменты (crypto в bull/bear, Forex majors)
- Хотите простоту и понятность
- Готовы терпеть просадки в боковиках

### Ensemble Mode

```json
{
  "Ensemble": {
    "Enabled": true,
    "MinimumAgreement": 0.6,  // 60% consensus required
    "UseConfidenceWeighting": true
  }
}
```

**Когда использовать:**
- Торгуете смешанные рынки (периоды тренда + боковика)
- Хотите снизить количество ложных сигналов
- Готовы пропустить часть сделок ради quality over quantity

---

## Настройка весов Ensemble

### Более агрессивный Trend Following

Увеличьте вес ADX, уменьшите RSI:

```json
"StrategyWeights": {
  "ADX Trend Following + Volume": 0.6,
  "MA Crossover": 0.3,
  "RSI Mean Reversion": 0.1
}
```

### Баланс 50/50

```json
"StrategyWeights": {
  "ADX Trend Following + Volume": 0.35,
  "MA Crossover": 0.15,
  "RSI Mean Reversion": 0.5
}
```

⚠️ **Осторожно**: RSI 50% может давать много контртрендовых сигналов в тренде!

### Только Trend Following

Отключите RSI полностью:

```csharp
var ensemble = new StrategyEnsemble();
ensemble.AddStrategy(new AdxTrendStrategy(), 0.6m);
ensemble.AddStrategy(new MaStrategy(), 0.4m);
// RSI не добавлен
```

---

## Research Notes

### ADX Trend Following

- **Target Sharpe**: 1.5-1.9
- **Max Drawdown**: < 20%
- **Best markets**: Trending (crypto bull/bear, Forex trends)
- **Optimal ADX threshold**: 25-30 (backtest validated)

### RSI Mean Reversion

- **Win Rate**: 60-70% в ranging markets
- **Drawdown в трендах**: может быть > 30% если не фильтровать
- **UseTrendFilter = true**: рекомендуется (торгует RSI только по тренду EMA)

### Ensemble

- **Optimal MinimumAgreement**: 0.6 (60%)
- **UseConfidenceWeighting**: true (учитывает силу сигнала)
- **Genetic optimization**: может найти оптимальные веса для конкретного инструмента

---

## Summary Table

| Approach | Philosophy | Win Rate | R:R | Best Market | Drawdown |
|----------|-----------|----------|-----|-------------|----------|
| **ADX Trend** | Follow trend | 40-50% | 1:2+ | Trending | Medium |
| **MA Crossover** | Follow trend | 45-55% | 1:2 | Trending | Medium |
| **RSI Mean Rev** | Counter-trend | 60-70% | 1:1 | Ranging | Low in range, High in trend |
| **Ensemble** | Mixed | 50-60% | 1:1.5 | All markets | Low (filtered) |

---

## Code References

- **AdxTrendStrategy**: [AdxTrendStrategy.cs:40-52](ComplexBot/Services/Strategies/AdxTrendStrategy.cs#L40-L52)
- **MaStrategy**: [MaStrategy.cs:7-16](ComplexBot/Services/Strategies/MaStrategy.cs#L7-L16)
- **RsiStrategy**: [RsiStrategy.cs:7-18](ComplexBot/Services/Strategies/RsiStrategy.cs#L7-L18)
- **StrategyEnsemble**: [StrategyEnsemble.cs:46-57](ComplexBot/Services/Strategies/StrategyEnsemble.cs#L46-L57)
- **EnsembleSettings**: [StrategyEnsemble.cs:323-336](ComplexBot/Services/Strategies/StrategyEnsemble.cs#L323-L336)

## User Guide

См. также [USER_GUIDE_RU.md](USER_GUIDE_RU.md) для подробного руководства для начинающих.
