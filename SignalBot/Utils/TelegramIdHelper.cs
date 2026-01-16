namespace SignalBot.Utils;

/// <summary>
/// Helper for working with Telegram channel/chat IDs in different formats
/// </summary>
public static class TelegramIdHelper
{
    /// <summary>
    /// Converts full Telegram ID format to API format.
    /// Example: -1003045070745 -> 3045070745
    /// </summary>
    /// <param name="fullId">Full ID with -100 prefix</param>
    /// <returns>ID without prefix</returns>
    public static long ConvertToApiFormat(long fullId)
    {
        if (fullId >= 0)
            return fullId;

        string idStr = fullId.ToString();
        if (idStr.StartsWith("-100") && idStr.Length > 4)
        {
            return long.Parse(idStr.Substring(4));
        }

        return fullId;
    }

    /// <summary>
    /// Converts API format ID to full Telegram ID format.
    /// Example: 3045070745 -> -1003045070745
    /// </summary>
    /// <param name="apiId">API format ID (positive)</param>
    /// <returns>Full ID with -100 prefix</returns>
    public static long ConvertToFullFormat(long apiId)
    {
        if (apiId <= 0)
            return apiId;

        return long.Parse($"-100{apiId}");
    }

    /// <summary>
    /// Checks if the given peer ID matches any of the configured channel IDs,
    /// handling both full format (-1003045070745) and API format (3045070745)
    /// </summary>
    /// <param name="peerId">Peer ID from Telegram API</param>
    /// <param name="configuredIds">List of configured channel IDs</param>
    /// <returns>True if peer ID matches any configured ID</returns>
    public static bool IsMonitoredChannel(long peerId, IEnumerable<long> configuredIds)
    {
        foreach (var configId in configuredIds)
        {
            if (IdsMatch(peerId, configId))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Checks if two channel IDs refer to the same channel,
    /// handling both full format and API format.
    /// </summary>
    public static bool IdsMatch(long id1, long id2)
    {
        if (id1 == id2)
            return true;

        var apiId1 = ConvertToApiFormat(id1);
        var apiId2 = ConvertToApiFormat(id2);

        return apiId1 == apiId2;
    }

    /// <summary>
    /// Normalizes a channel username by removing @ prefix if present.
    /// </summary>
    public static string NormalizeUsername(string username)
    {
        if (string.IsNullOrWhiteSpace(username))
            return string.Empty;

        return username.TrimStart('@').Trim();
    }
}
