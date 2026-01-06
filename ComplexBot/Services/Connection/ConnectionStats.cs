namespace ComplexBot.Services.Connection;

public record ConnectionStats(
    bool IsConnected,
    int CurrentAttempt,
    int MaxAttempts,
    bool HasActiveSubscription,
    DateTimeOffset? LastConnectedAt,
    DateTimeOffset? LastDisconnectedAt,
    string? LastError,
    DateTimeOffset? LastErrorAt
);
