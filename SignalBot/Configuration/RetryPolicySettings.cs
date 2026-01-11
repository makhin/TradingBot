namespace SignalBot.Configuration;

public static class RetryPolicySettings
{
    public const int MaxRetryAttempts = 3;
    public const int RetryCount = MaxRetryAttempts - 1;
}
