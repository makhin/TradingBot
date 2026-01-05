namespace ComplexBot.Services.Connection;

public record ConnectionStats(
    bool IsConnected,
    int CurrentAttempt,
    int MaxAttempts,
    bool HasActiveSubscription
);
