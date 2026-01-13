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
            // Direct match
            if (configId == peerId)
                return true;

            // Try converting config ID and compare
            var convertedId = ConvertToApiFormat(configId);
            if (convertedId == peerId)
                return true;
        }

        return false;
    }
}
