# TradingBot.Core.Tests

Unit tests for the **TradingBot.Core** library, covering risk management, analytics, and core trading functionality.

## Test Suites

### EquityTrackerTests
Tests for the `EquityTracker` class which tracks equity, peak equity, and calculates drawdowns.

- **Constructor_SetsInitialCapitalAsCurrentEquity**: Verifies initial state
- **Update_IncreasesCurrentEquity**: Tests equity increase with peak update
- **Update_DecreasesCurrentEquity**: Tests equity decrease while maintaining peak
- **DrawdownAbsolute_CalculatesCorrectly**: Validates absolute drawdown calculation
- **DrawdownPercent_CalculatesCorrectly**: Validates percentage drawdown
- **DrawdownPercent_ZeroWhenNoDrawdown**: Tests zero drawdown scenario
- **Add_IncreasesEquityByAmount**: Tests adding profits/losses
- **IsDrawdownExceeded_ReturnsTrueWhenThresholdCrossed**: Tests drawdown threshold checking

### RiskSettingsTests
Tests for the `RiskSettings` record containing risk management parameters.

- **RiskPerTradePercent_Default_IsValid**: Verifies default risk per trade percentage
- **MaxDailyDrawdownPercent_Default_IsValid**: Verifies maximum daily drawdown setting
- **MaxDrawdownPercent_Default_IsValid**: Verifies maximum total drawdown setting
- **Constructor_WithValues_SetsProperties**: Tests initialization with custom values
- **AtrStopMultiplier_HasDefaultValue**: Tests ATR multiplier default
- **TakeProfitMultiplier_HasDefaultValue**: Tests profit target multiplier default

### TradeCostCalculatorTests
Tests for the static `TradeCostCalculator` class handling fees and slippage.

- **CalculateFeesFromPercent_WithValidInputs_ReturnsCorrectFee**: Basic fee calculation
- **CalculateFeesFromPercent_WithZeroFeePercent_ReturnsZero**: Zero fee scenario
- **CalculateFeesFromPercent_WithHighFeePercent_ReturnsCorrectFee**: High fee calculation
- **ApplySlippage_BuyOrder_AddSlippageToPrice**: Tests slippage on buy orders
- **ApplySlippage_SellOrder_SubtractSlippageFromPrice**: Tests slippage on sell orders
- **CalculateFeesFromPercent_WithVariousInputs_CalculatesCorrectly**: Parametrized test with various inputs
- **CalculateTotalCosts_WithBothFeesAndSlippage_CalculatesCorrectly**: Combined fees and slippage

### TradeJournalTests
Tests for the `TradeJournal` class managing trade records.

- **OpenTrade_ReturnsIncrementingTradeId**: Tests trade ID assignment
- **CloseTrade_UpdatesTradeEntry**: Tests closing and updating trades
- **GetAllTrades_ReturnsOpenedTrades**: Tests retrieving all recorded trades
- **ExportToCsv_CreatesFile**: Tests CSV export functionality

## Running Tests

```bash
# Run all TradingBot.Core tests
dotnet test TradingBot.Core.Tests

# Run with verbose output
dotnet test TradingBot.Core.Tests -v normal

# Run specific test class
dotnet test TradingBot.Core.Tests --filter ClassName=TradingBot.Core.Tests.EquityTrackerTests

# Run with code coverage
dotnet test TradingBot.Core.Tests /p:CollectCoverage=true
```

## Test Statistics

- **Total Tests**: 27
- **Framework**: xUnit
- **Mocking**: Moq
- **.NET Version**: 8.0

## Coverage

The tests cover:
- Core risk management calculations
- Equity tracking and drawdown monitoring  
- Fee and slippage calculations
- Trade journal operations (open, close, export)
- Default risk settings validation
