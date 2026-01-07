# Короткое руководство: multi-pair optimization и backtesting

Ниже — практические шаги для подбора фильтров (multi-timeframe optimization) и оценки результатов для multi-pair конфигурации. Оптимизация и бэктест сейчас выполняются по одному символу за раз, а multi-pair портфель собирается из этих результатов.

## 0) Что реально есть в коде
- Multi-timeframe optimization для одного символа: `OptimizationRunner` → “Multi-Timeframe Optimization (Primary + Filters)”.
- Бэктест только для одного символа: `BacktestRunner`.
- Multi-pair live trading (primary + filters) через `MultiPairLiveTrading` в конфиге.

## 1) Подготовка данных
1. Запусти `dotnet run` и выбери **Download Data** (или через меню `DataRunner`).
2. Скачай данные для:
   - primary интервала (например, `FourHour`),
   - фильтровых интервалов (например, `OneHour`, `ThirtyMinutes`).
3. Убедись, что временной диапазон у всех интервалов совпадает (иначе фильтры будут “срезаны”).

## 2) Multi-timeframe optimization (per-symbol)
**Цель** — подобрать фильтр (RSI/ADX), интервал и режим (Confirm/Veto/Score) для конкретного символа.

Шаги:
1. В `ComplexBot/appsettings.json` настрой `MultiTimeframeOptimizer` (примеры ниже).
2. Убедись, что в `MultiPairLiveTrading.TradingPairs` есть primary‑пары (Role = `Primary`).
3. Запусти `dotnet run`.
4. В меню выбери **Parameter Optimization** → **Multi-Timeframe Optimization (Primary + Filters)**.
5. Укажи период анализа (Start/End). Данные будут взяты с диска, а при отсутствии — скачаны и сохранены в `Data/`.
6. По каждой primary‑паре будет выполнен отдельный прогон. В таблице результатов зафиксируй лучшие комбинации: Filter, Interval, Mode, Params.
7. При желании сохрани лучший фильтр в `appsettings.user.json` (будет обновлен `MultiPairLiveTrading` для этого символа).

Если primary‑пар нет, оптимизация перейдет в single‑symbol режим с выбором стратегии и источника данных.

Пример настроек оптимизации:
```json
"MultiTimeframeOptimizer": {
  "OptimizeFilters": true,
  "FilterIntervalCandidates": [ "FifteenMinutes", "ThirtyMinutes", "OneHour" ],
  "RsiOverboughtRange": [ 70.0, 75.0 ],
  "RsiOversoldRange": [ 25.0, 30.0 ],
  "AdxMinThresholdRange": [ 20.0, 25.0 ],
  "AdxStrongThresholdRange": [ 30.0, 35.0 ],
  "FilterModesToTest": [ "Confirm", "Veto" ],
  "TestNoFilterBaseline": true,
  "OptimizeFor": "RiskAdjusted"
}
```

## 3) Применение результата в multi-pair конфиге
В `MultiPairLiveTrading.TradingPairs` добавь:
- primary запись (Role = `Primary`),
- фильтр запись (Role = `Filter`, `FilterMode`, другой `Interval`) **с тем же Symbol**.

Пример:
```json
"MultiPairLiveTrading": {
  "Enabled": true,
  "TotalCapital": 10000.0,
  "AllocationMode": "Equal",
  "UsePortfolioRiskManager": true,
  "TradingPairs": [
    { "Symbol": "BTCUSDT", "Interval": "FourHour", "Strategy": "ADX", "Role": "Primary" },
    { "Symbol": "BTCUSDT", "Interval": "OneHour", "Strategy": "RSI", "Role": "Filter", "FilterMode": "Confirm",
      "RsiOverbought": 70.0, "RsiOversold": 30.0 },
    { "Symbol": "ETHUSDT", "Interval": "FourHour", "Strategy": "ADX", "Role": "Primary" },
    { "Symbol": "ETHUSDT", "Interval": "OneHour", "Strategy": "RSI", "Role": "Filter", "FilterMode": "Confirm",
      "RsiOverbought": 70.0, "RsiOversold": 30.0 }
  ]
}
```

Важно: фильтры читают пороги из `TradingPairConfig`:
- `RsiOverbought` / `RsiOversold` (дефолт 70/30),
- `AdxMinThreshold` / `AdxStrongThreshold` (дефолт 20/30).
Если оптимизация дала другие значения — перенеси их в соответствующий Filter-элемент.

## 4) Корреляционные группы (PortfolioRisk)
Корреляционные группы ограничивают суммарный риск по “связанных” парам.  
Учитываются только если `UsePortfolioRiskManager: true` в `MultiPairLiveTrading`.

Шаги:
1. Открой `ComplexBot/appsettings.json`.
2. В блоке `PortfolioRisk` настрой лимиты и группы.
3. Убедись, что символы совпадают с `TradingPairs.Symbol` (например, `BTCUSDT`).
4. Не дублируй один символ в нескольких группах — берется первое совпадение.

Пример:
```json
"PortfolioRisk": {
  "MaxTotalDrawdownPercent": 25.0,
  "MaxCorrelatedRiskPercent": 10.0,
  "MaxConcurrentPositions": 5,
  "CorrelationGroups": {
    "BTC_CORRELATED": [ "BTCUSDT", "ETHUSDT", "BNBUSDT", "SOLUSDT" ],
    "ALTCOINS_L1": [ "ADAUSDT", "DOTUSDT", "AVAXUSDT", "MATICUSDT" ],
    "MEMECOINS": [ "DOGEUSDT", "SHIBUSDT", "PEPEUSDT" ]
  }
}
```

Если символ не входит ни в одну группу — он считается независимым (корреляционный лимит не применяется).

## 5) Multi-pair backtesting (per-symbol)
Полноценного портфельного бэктеста в CLI пока нет, поэтому:
1. Запусти **Backtest** отдельно для каждого символа и primary интервала.
2. Сравни метрики: `Sharpe`, `Max Drawdown`, `Total Return`, `Total Trades`.
3. Выбери набор символов и веса (Equal/Weighted) на основе этих результатов.
4. При необходимости повтори оптимизацию фильтров на каждом символе.

Подсказка: единые параметры бэктеста (capital/commission/slippage) должны быть одинаковыми для честного сравнения.

## 6) Рекомендации по криптовалютам
Ориентируйся на ликвидные пары с узкими спредами и стабильной историей.

**Базовый набор (максимальная ликвидность):**
- `BTCUSDT`, `ETHUSDT`

**Дополнительные ликвидные пары:**
- `BNBUSDT`, `SOLUSDT`, `XRPUSDT`, `ADAUSDT`

**Высокая волатильность (по желанию, меньшим весом):**
- `DOGEUSDT`, `AVAXUSDT`

**Избегать:**
- низколиквидные микро-альты,
- пары с частыми делистингами,
- редкие/экзотические пары с широкими спредами.

## 7) Мини-чеклист
- Данные скачаны для всех интервалов.
- Оптимизация фильтров выполнена для каждого символа.
- В `MultiPairLiveTrading` у каждого фильтра тот же `Symbol`, что у primary.
- Включен `UsePortfolioRiskManager` при 3+ символах.
