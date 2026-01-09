# TradingBot.Binance.Tests

Unit tests for the **TradingBot.Binance** library, covering Binance exchange integration and execution validation.

## Test Suites

### ExecutionValidatorTests
Tests for the `ExecutionValidator` class validating order execution against expected parameters.

- **ValidateExecution_BuyWithNoSlippage_IsAcceptable**: Zero slippage scenario
- **ValidateExecution_BuyWithAcceptableSlippage_IsAcceptable**: Slippage within tolerance
- **ValidateExecution_BuyWithExcessiveSlippage_IsRejected**: Slippage exceeds maximum
- **ValidateExecution_SellWithNoSlippage_IsAcceptable**: Sell with no slippage
- **ValidateExecution_SellWithAcceptableSlippage_IsAcceptable**: Sell within tolerance
- **ValidateExecution_StoresExpectedAndActualPrices**: Verifies price recording
- **ValidateExecution_WithVariousSlippageThresholds**: Parametrized test with multiple thresholds

### BinanceCommonIntegrationTests
Integration-level tests for common Binance operations.

- **ExecutionValidator_IntegrationWithRealScenarios**: Tests various trading scenarios
- **ExecutionValidator_CalculatesSlippageAmountCorrectly**: Verifies slippage calculation
- **ExecutionValidator_HandlesSmallPrices**: Tests with small price values (dust trades)
- **ExecutionValidator_HandlesLargePrices**: Tests with large price values

### BinanceModelTests (CandleTests)
Tests for the `Candle` record representing OHLCV candlestick data.

- **Candle_ConstructorSetsAllProperties**: Verifies all properties are set
- **Candle_HighIsHighestPoint**: Validates high >= all other prices
- **Candle_LowIsLowestPoint**: Validates low <= all other prices
- **Candle_WithVariousPrices_CreatesSuccessfully**: Parametrized test with various price ranges

## Running Tests

```bash
# Run all TradingBot.Binance tests
dotnet test TradingBot.Binance.Tests

# Run with verbose output
dotnet test TradingBot.Binance.Tests -v normal

# Run specific test class
dotnet test TradingBot.Binance.Tests --filter ClassName=TradingBot.Binance.Tests.ExecutionValidatorTests

# Run with code coverage
dotnet test TradingBot.Binance.Tests /p:CollectCoverage=true
```

## Test Statistics

- **Total Tests**: 19
- **Framework**: xUnit
- **Mocking**: Moq
- **.NET Version**: 8.0

## Coverage

The tests cover:
- Order execution validation with slippage checks
- Price precision handling (from dust trades to large orders)
- OHLCV candle data integrity
- Execution result recording and error reasons

## Dependencies

- `Binance.Net` - Binance exchange API wrapper
- `TradingBot.Core` - Core trading models and utilities
