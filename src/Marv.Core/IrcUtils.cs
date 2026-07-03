namespace Marv.Core;

/// <summary>
/// Utility methods for working with IRC protocol constraints.
/// </summary>
public static class IrcUtils
{
    /// <summary>
    /// Splits a list of channel names into batches where each batch's
    /// comma-separated representation fits within
    /// <paramref name="maxPayloadLength"/> bytes. Useful for batching
    /// JOIN, MODE, and other channel-list commands within the 512-byte
    /// IRC line limit.
    /// </summary>
    /// <param name="channels">The channel names to batch.</param>
    /// <param name="maxPayloadLength">
    /// Maximum byte length for the comma-separated channel list.
    /// The caller must account for the command prefix and CRLF when
    /// computing this value (e.g., 512 - len("JOIN ") - len("\r\n")).
    /// </param>
    /// <param name="maxTargets">
    /// Maximum number of targets (channels) per command, typically from
    /// the TARGMAX ISUPPORT token. Pass null or 0 for no limit.
    /// </param>
    public static IEnumerable<List<string>> BatchChannels(
        IReadOnlyList<string> channels, int maxPayloadLength, int? maxTargets = null)
    {
        var batch = new List<string>();
        var currentLength = 0;
        var effectiveMaxTargets = maxTargets is > 0 ? maxTargets.Value : int.MaxValue;

        foreach (var channel in channels)
        {
            var addedLength = batch.Count == 0
                ? channel.Length
                : channel.Length + 1; // +1 for comma separator

            if ((currentLength + addedLength > maxPayloadLength || batch.Count >= effectiveMaxTargets)
                && batch.Count > 0)
            {
                yield return batch;
                batch = [];
                currentLength = 0;
                addedLength = channel.Length;
            }

            batch.Add(channel);
            currentLength += addedLength;
        }

        if (batch.Count > 0)
            yield return batch;
    }

    /// <summary>
    /// Parses the TARGMAX ISUPPORT value to extract the target limit for a
    /// specific command. TARGMAX format: "cmd1:limit1,cmd2:limit2,..."
    /// </summary>
    /// <param name="targmax">The raw TARGMAX value from ISUPPORT.</param>
    /// <param name="command">The command to look up (e.g. "JOIN").</param>
    /// <returns>The target limit, or null if the command is not listed or has no limit.</returns>
    public static int? ParseTargMax(string? targmax, string command)
    {
        if (string.IsNullOrEmpty(targmax)) return null;

        foreach (var entry in targmax.Split(','))
        {
            var colonIndex = entry.IndexOf(':');
            if (colonIndex < 0) continue;

            var cmd = entry[..colonIndex];
            if (!cmd.Equals(command, StringComparison.OrdinalIgnoreCase)) continue;

            var limitStr = entry[(colonIndex + 1)..];
            if (limitStr.Length > 0 && int.TryParse(limitStr, out var limit))
                return limit;

            return null; // command listed but no limit specified
        }

        return null;
    }
}
