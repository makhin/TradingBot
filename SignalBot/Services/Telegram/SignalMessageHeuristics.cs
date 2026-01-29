namespace SignalBot.Services.Telegram;

public static class SignalMessageHeuristics
{
    public static bool LooksLikeSignal(string? messageText)
    {
        if (string.IsNullOrWhiteSpace(messageText))
        {
            return false;
        }

        if (messageText.Length < 20)
        {
            return false;
        }

        var lower = messageText.ToLowerInvariant();

        if (!lower.Contains("entry"))
        {
            return false;
        }

        if (!(lower.Contains("stop") || lower.Contains("sl")))
        {
            return false;
        }

        if (!(lower.Contains("target") || lower.Contains("tp")))
        {
            return false;
        }

        return true;
    }
}
