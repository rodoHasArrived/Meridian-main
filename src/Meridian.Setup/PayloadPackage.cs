using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace Meridian.Setup;

/// <summary>
/// Reads the product payload that the release build appends to the installer executable.
/// </summary>
/// <remarks>
/// <para>
/// The payload used to be a set of <c>EmbeddedResource</c> items compiled into the assembly. That
/// cannot work at this size: Roslyn lays every resource into the PE image's mapped field data and
/// the writer fails with <c>ArgumentOutOfRangeException (mappedFieldDataStreamRva)</c> once the
/// combined payload outgrows the section it serialises into, so the release build could never
/// produce <c>Meridian-Setup.exe</c> at all. Two self-contained .NET publishes plus a PostgreSQL
/// server distribution per runtime are far past that ceiling.
/// </para>
/// <para>
/// The payload is therefore appended to the finished executable as a ZIP archive, followed by a
/// fixed 138-byte ASCII trailer:
/// </para>
/// <code>
/// [ executable image ][ ZIP archive ][ trailer ][ Authenticode certificate table ]
///
/// MDNSETUP1\n                          magic and format version    (10 bytes)
/// offset=00000000000000000000\n        archive start, zero-padded  (28 bytes)
/// length=00000000000000000000\n        archive bytes, zero-padded  (28 bytes)
/// sha256=&lt;64 lowercase hex digits&gt;\n    archive digest              (72 bytes)
/// </code>
/// <para>
/// Appending before signing keeps the payload inside the Authenticode hash, which covers
/// everything except the checksum field and the certificate table itself. Because signing then
/// adds the certificate table after the trailer, the trailer is located by scanning backwards for
/// the magic rather than by reading from the end of the file; a candidate is only accepted when
/// its own position equals <c>offset + length</c>, so a magic occurring inside the payload bytes
/// cannot be mistaken for the real trailer. The offset cannot be derived from the PE headers
/// instead: single-file publishing already appends its bundle past the last section.
/// </para>
/// <para>
/// The archive is laid out with one top-level directory per runtime identifier
/// (<c>win-x64/…</c>, <c>win-arm64/…</c>); <see cref="GetRuntimePayload"/> selects one subtree.
/// <c>build/scripts/install/build-consumer-setup.ps1</c> writes this format and then runs the
/// packaged executable with <c>--verify-payload</c>, so the writer is checked against this reader
/// in the same job that produces the artifact.
/// </para>
/// </remarks>
internal sealed class PayloadPackage : IDisposable
{
    internal const string TrailerMagic = "MDNSETUP1\n";
    internal const int TrailerLength = 138;

    // Generous next to a signature block of a few kilobytes, and cheap to scan.
    private const int TrailerSearchWindow = 1024 * 1024;

    // A download truncated anywhere inside the payload also loses the trailer, which sits at the
    // very end, so "no trailer" and "incomplete file" are the same diagnosis to a user.
    private const string MissingPayloadMessage =
        "This Meridian installer does not contain a readable product payload. The download may be incomplete; fetch Meridian-Setup.exe again from the official release page.";

    private readonly FileStream _file;
    private readonly ZipArchive _archive;

    private PayloadPackage(FileStream file, ZipArchive archive)
    {
        _file = file;
        _archive = archive;
    }

    internal static PayloadPackage Open(string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);

        // The running image is locked for writing but stays readable; share deletion too so a
        // detached copy of setup can be cleaned up underneath us.
        var file = new FileStream(
            executablePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        try
        {
            var (offset, length, sha256) = ReadTrailer(file);
            VerifyArchiveDigest(file, offset, length, sha256);
            var archive = new ZipArchive(new BoundedStream(file, offset, length), ZipArchiveMode.Read);
            return new PayloadPackage(file, archive);
        }
        catch
        {
            file.Dispose();
            throw;
        }
    }

    /// <summary>Returns the payload files recorded for <paramref name="runtime"/>.</summary>
    internal IReadOnlyList<PayloadSource> GetRuntimePayload(string runtime)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runtime);

        var prefix = runtime + "/";
        var sources = new List<PayloadSource>();
        foreach (var entry in _archive.Entries)
        {
            // Directory markers carry a trailing separator and no content.
            if (entry.FullName.EndsWith('/') ||
                !entry.FullName.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            var relativePath = entry.FullName[prefix.Length..];
            if (relativePath.Length == 0)
            {
                continue;
            }

            sources.Add(new PayloadSource(relativePath, entry.Open));
        }

        return sources;
    }

    /// <summary>Returns every runtime identifier the archive carries a payload for.</summary>
    internal IReadOnlyList<string> GetRuntimes()
    {
        var runtimes = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var entry in _archive.Entries)
        {
            var separator = entry.FullName.IndexOf('/');
            if (separator > 0 && separator + 1 < entry.FullName.Length)
            {
                runtimes.Add(entry.FullName[..separator]);
            }
        }

        return [.. runtimes];
    }

    public void Dispose()
    {
        _archive.Dispose();
        _file.Dispose();
    }

    private static (long Offset, long Length, string Sha256) ReadTrailer(FileStream file)
    {
        var fileLength = file.Length;
        var window = (int)Math.Min(fileLength, TrailerSearchWindow);
        if (window < TrailerLength)
        {
            throw new InvalidDataException(MissingPayloadMessage);
        }

        var windowStart = fileLength - window;
        var buffer = new byte[window];
        file.Seek(windowStart, SeekOrigin.Begin);
        file.ReadExactly(buffer);

        var magic = Encoding.ASCII.GetBytes(TrailerMagic);
        for (var index = window - TrailerLength; index >= 0; index--)
        {
            if (!buffer.AsSpan(index, magic.Length).SequenceEqual(magic))
            {
                continue;
            }

            if (!TryParseTrailer(Encoding.ASCII.GetString(buffer, index, TrailerLength), out var trailer))
            {
                continue;
            }

            // The trailer sits immediately after the archive it describes. Anything else is a
            // magic that happens to occur inside the payload bytes.
            if (trailer.Offset + trailer.Length != windowStart + index)
            {
                continue;
            }

            return trailer;
        }

        throw new InvalidDataException(MissingPayloadMessage);
    }

    private static bool TryParseTrailer(string text, out (long Offset, long Length, string Sha256) trailer)
    {
        trailer = default;
        var lines = text.Split('\n');
        if (lines.Length != 5 || lines[4].Length != 0)
        {
            return false;
        }

        if (!TryParseCount(lines[1], "offset=", out var offset) ||
            !TryParseCount(lines[2], "length=", out var length) ||
            !lines[3].StartsWith("sha256=", StringComparison.Ordinal))
        {
            return false;
        }

        var digest = lines[3]["sha256=".Length..];
        if (digest.Length != 64 || !digest.All(Uri.IsHexDigit))
        {
            return false;
        }

        trailer = (offset, length, digest);
        return true;
    }

    private static bool TryParseCount(string line, string name, out long value)
    {
        value = 0;
        if (!line.StartsWith(name, StringComparison.Ordinal))
        {
            return false;
        }

        var digits = line[name.Length..];
        return digits.Length == 20 &&
            long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

    private static void VerifyArchiveDigest(FileStream file, long offset, long length, string expected)
    {
        file.Seek(offset, SeekOrigin.Begin);
        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[1024 * 1024];
        var remaining = length;
        while (remaining > 0)
        {
            var read = file.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
            if (read <= 0)
            {
                throw new InvalidDataException(MissingPayloadMessage);
            }

            hasher.AppendData(buffer, 0, read);
            remaining -= read;
        }

        var actual = Convert.ToHexString(hasher.GetHashAndReset());
        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "This Meridian installer failed SHA-256 verification. The download is incomplete or the file has been modified.");
        }
    }

    /// <summary>A read-only seekable view over one range of the installer executable.</summary>
    /// <remarks>
    /// <see cref="ZipArchive"/> reads the central directory from the end of its stream, so the
    /// view has to be seekable and has to end at the archive rather than at the end of the file.
    /// The view never owns the underlying file; <see cref="PayloadPackage.Dispose"/> does.
    /// </remarks>
    private sealed class BoundedStream(FileStream file, long offset, long length) : Stream
    {
        private long _position;

        public override bool CanRead => true;

        public override bool CanSeek => true;

        public override bool CanWrite => false;

        public override long Length => length;

        public override long Position
        {
            get => _position;
            set => Seek(value, SeekOrigin.Begin);
        }

        public override int Read(byte[] buffer, int offsetInBuffer, int count)
        {
            ArgumentNullException.ThrowIfNull(buffer);
            ArgumentOutOfRangeException.ThrowIfNegative(offsetInBuffer);
            ArgumentOutOfRangeException.ThrowIfNegative(count);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(count, buffer.Length - offsetInBuffer);
            return Read(buffer.AsSpan(offsetInBuffer, count));
        }

        public override int Read(Span<byte> buffer)
        {
            var available = (int)Math.Min(buffer.Length, length - _position);
            if (available <= 0)
            {
                return 0;
            }

            file.Seek(offset + _position, SeekOrigin.Begin);
            var read = file.Read(buffer[..available]);
            _position += read;
            return read;
        }

        public override long Seek(long target, SeekOrigin origin)
        {
            var resolved = origin switch
            {
                SeekOrigin.Begin => target,
                SeekOrigin.Current => _position + target,
                SeekOrigin.End => length + target,
                _ => throw new ArgumentOutOfRangeException(nameof(origin))
            };

            if (resolved < 0 || resolved > length)
            {
                throw new ArgumentOutOfRangeException(nameof(target));
            }

            _position = resolved;
            return _position;
        }

        public override void Flush()
        {
        }

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offsetInBuffer, int count) =>
            throw new NotSupportedException();
    }
}
