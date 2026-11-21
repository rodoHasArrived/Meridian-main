using System.Text;

namespace Meridian.Storage.Archival;

/// <summary>
/// Durable in-place append for append-only JSONL logs such as audit and dead-letter files.
/// </summary>
/// <remarks>
/// <see cref="AtomicFileWriter.AppendLinesAsync"/> copies the whole destination on every call,
/// which is O(file size) per record and unsuitable for a log that grows with every event. This
/// writer appends only the new lines and fsyncs them before returning, so a batch that returned
/// successfully survives a crash. A crash or failure mid-append can leave at most one torn final
/// line, which was never acknowledged to the caller; it is truncated back to the last complete
/// line on the next append, the same repair the JSONL sink applies to its day files.
/// Callers must serialize appends to one path; the file is opened only for the duration of a call.
/// </remarks>
public static class JsonlAppendWriter
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// Appends <paramref name="lines"/> to <paramref name="path"/> as one write and flushes them
    /// to disk. Either every line is appended or the file is restored to its previous length.
    /// </summary>
    public static async Task AppendLinesAsync(
        string path,
        IReadOnlyList<string> lines,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(lines);
        if (lines.Count == 0)
            return;

        ct.ThrowIfCancellationRequested();

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var payload = Encode(lines);
        bool created;
        await using (var stream = new FileStream(
            path,
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.Read | FileShare.Delete,
            bufferSize: 4096,
            FileOptions.Asynchronous))
        {
            created = stream.Length == 0;
            RepairTornTail(stream);
            var committedLength = stream.Length;
            stream.Position = committedLength;
            try
            {
                await stream.WriteAsync(payload, ct).ConfigureAwait(false);
                await stream.FlushAsync(ct).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }
            catch
            {
                try
                {
                    stream.SetLength(committedLength);
                }
                catch (Exception)
                {
                    // The original failure is the one to report. A torn tail left here is
                    // truncated by RepairTornTail on the next append.
                }

                throw;
            }
        }

        if (created && !string.IsNullOrEmpty(directory))
        {
            // Make the new directory entry durable; the data itself is already flushed.
            await AtomicFileWriter.SyncDirectoryAsync(directory, CancellationToken.None).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Truncates <paramref name="stream"/> to its last complete line when the final line has no
    /// terminating newline, i.e. when a previous append was interrupted part-way.
    /// </summary>
    internal static void RepairTornTail(FileStream stream)
    {
        if (stream.Length == 0)
            return;

        stream.Position = stream.Length - 1;
        if (stream.ReadByte() == '\n')
            return;

        const int bufferSize = 4096;
        var buffer = new byte[bufferSize];
        var position = stream.Length;
        while (position > 0)
        {
            var readLength = (int)Math.Min(bufferSize, position);
            position -= readLength;
            stream.Position = position;
            stream.ReadExactly(buffer, 0, readLength);

            for (var index = readLength - 1; index >= 0; index--)
            {
                if (buffer[index] != '\n')
                    continue;

                stream.SetLength(position + index + 1);
                return;
            }
        }

        stream.SetLength(0);
    }

    private static byte[] Encode(IReadOnlyList<string> lines)
    {
        var builder = new StringBuilder();
        for (var index = 0; index < lines.Count; index++)
            builder.Append(lines[index]).Append('\n');

        return Utf8NoBom.GetBytes(builder.ToString());
    }
}
