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
    public static IEnumerable<List<string>> BatchChannels(
        IReadOnlyList<string> channels, int maxPayloadLength)
    {
        var batch = new List<string>();
        var currentLength = 0;

        foreach (var channel in channels)
        {
            var addedLength = batch.Count == 0
                ? channel.Length
                : channel.Length + 1; // +1 for comma separator

            if (currentLength + addedLength > maxPayloadLength && batch.Count > 0)
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
}
