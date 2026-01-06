namespace ComplexBot.Configuration;

public class ConnectionSettings
{
    public BackoffSettings Backoff { get; set; } = new();
    public double JitterFactor { get; set; } = 0.2;
}
