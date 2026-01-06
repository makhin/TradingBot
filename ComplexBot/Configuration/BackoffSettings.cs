namespace ComplexBot.Configuration;

public class BackoffSettings
{
    public int MinDelayMs { get; set; } = 1000;
    public int MaxDelayMs { get; set; } = 32000;
    public double Factor { get; set; } = 2.0;
    public List<int> DelaysMs { get; set; } = new();
}
