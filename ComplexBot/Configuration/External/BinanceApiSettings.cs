namespace ComplexBot.Configuration.External;

public class BinanceApiSettings
{
    public string ApiKey { get; set; } = string.Empty;
    public string ApiSecret { get; set; } = string.Empty;
    public bool UseTestnet { get; set; } = true;
}
