using System.Diagnostics;

namespace SignalBot.Telemetry;

public static class SignalBotTelemetry
{
    public const string ActivitySourceName = "SignalBot";
    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);
}
