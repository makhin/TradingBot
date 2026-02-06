namespace SignalBot.Configuration;

/// <summary>
/// Exchange configuration settings
/// Supports multiple exchanges with a single active exchange selection
/// </summary>
public class ExchangeSettings
{
    /// <summary>
    /// Active exchange to use for trading (Binance or Bybit)
    /// </summary>
    public string ActiveExchange { get; set; } = "Binance";

    /// <summary>
    /// Binance exchange configuration
    /// </summary>
    public BinanceExchangeSettings Binance { get; set; } = new();

    /// <summary>
    /// Bybit exchange configuration
    /// </summary>
    public BybitExchangeSettings Bybit { get; set; } = new();
}

/// <summary>
/// Binance API configuration
/// </summary>
public class BinanceExchangeSettings
{
    /// <summary>
    /// Use Binance testnet instead of mainnet
    /// </summary>
    public bool UseTestnet { get; set; } = true;

    /// <summary>
    /// Binance API Key
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Binance API Secret
    /// </summary>
    public string ApiSecret { get; set; } = string.Empty;
}

/// <summary>
/// Bybit API configuration
/// </summary>
public class BybitExchangeSettings
{
    /// <summary>
    /// Use Bybit testnet instead of mainnet
    /// </summary>
    public bool UseTestnet { get; set; } = true;

    /// <summary>
    /// Bybit API Key
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Bybit API Secret
    /// </summary>
    public string ApiSecret { get; set; } = string.Empty;
}
