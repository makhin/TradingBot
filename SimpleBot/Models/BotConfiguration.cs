using System;

namespace SimpleBot.Models;

public class BotConfiguration
{
    public BinanceSettings Binance { get; set; } = new();
    public TradingSettings Trading { get; set; } = new();
    public StrategySettings Strategy { get; set; } = new();
}

public class BinanceSettings
{
    public string ApiKey { get; set; } = string.Empty;
    public string ApiSecret { get; set; } = string.Empty;
    public bool UseTestnet { get; set; } = true;
}

public class TradingSettings
{
    public string CryptoCurrency { get; set; } = "BTC";  // Base currency (crypto)
    public string QuoteCurrency { get; set; } = "USDT";  // Quote currency (fiat/stablecoin)
    public decimal MinTradeAmount { get; set; } = 10m;

    // Computed property for the trading symbol
    public string Symbol => $"{CryptoCurrency}{QuoteCurrency}";
}

public class StrategySettings
{
    public string Type { get; set; } = "MA";  // MA, RSI, or BollingerBands

    // Moving Average settings
    public int ShortPeriod { get; set; } = 5;
    public int LongPeriod { get; set; } = 20;

    // RSI settings
    public int RsiPeriod { get; set; } = 14;
    public decimal RsiOverbought { get; set; } = 70m;
    public decimal RsiOversold { get; set; } = 30m;

    // Bollinger Bands settings
    public int BollingerPeriod { get; set; } = 20;
    public decimal BollingerStdDev { get; set; } = 2m;
}
