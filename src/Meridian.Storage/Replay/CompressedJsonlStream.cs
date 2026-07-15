using System.IO.Compression;
using K4os.Compression.LZ4.Streams;
using ZstdSharp;

namespace Meridian.Storage.Replay;

/// <summary>
/// Shared decompression front-end for the JSONL replay readers.
///
/// The captured-JSONL readers (<see cref="JsonlReplayer"/>, <see cref="MemoryMappedJsonlReader"/>,
/// and <c>JsonlMarketDataStore</c>) previously hand-rolled their own compression handling and had
/// drifted apart — some recognized only <c>.gz</c> while <see cref="Policies.JsonlStoragePolicy"/>
/// can name output files <c>.jsonl.gz</c>, <c>.jsonl.zst</c>, <c>.jsonl.lz4</c>, or <c>.jsonl.br</c>.
/// A file emitted with a codec suffix a reader did not recognize was fed to the reader as raw
/// compressed bytes, so every line silently failed to parse and the data was dropped.
///
/// This helper centralizes detection and decompression. It prefers the file's leading magic bytes
/// over the extension, so a file is decoded by its actual content even when the writer named it with
/// a codec suffix whose bytes it did not actually produce.
/// </summary>
internal static class CompressedJsonlStream
{
    /// <summary>
    /// True when <paramref name="path"/> carries a compression suffix the storage policy can emit,
    /// meaning the file must be decoded through a decompressor rather than read (or memory-mapped)
    /// as raw UTF-8.
    /// </summary>
    public static bool IsCompressed(string path) =>
        HasSuffix(path, ".gz") ||
        HasSuffix(path, ".gzip") ||
        HasSuffix(path, ".zst") ||
        HasSuffix(path, ".lz4") ||
        HasSuffix(path, ".br");

    /// <summary>
    /// Wraps <paramref name="source"/> in the correct decompressor for its content, or returns it
    /// unchanged when the file is not compressed. The returned decompressor is created with
    /// <c>leaveOpen</c>, so the caller retains ownership of <paramref name="source"/>'s lifetime.
    /// </summary>
    public static Stream Decompress(Stream source, string path)
    {
        ArgumentNullException.ThrowIfNull(source);

        return DetectCodec(source, path) switch
        {
            Codec.Gzip => new GZipStream(source, CompressionMode.Decompress, leaveOpen: true),
            Codec.Zstd => new DecompressionStream(source, leaveOpen: true),
            Codec.Lz4 => LZ4Stream.Decode(source, leaveOpen: true),
            Codec.Brotli => new BrotliStream(source, CompressionMode.Decompress, leaveOpen: true),
            _ => source
        };
    }

    private enum Codec
    {
        None,
        Gzip,
        Zstd,
        Lz4,
        Brotli
    }

    private static Codec DetectCodec(Stream source, string path)
    {
        // Prefer the actual content signature so a mislabeled file (e.g. gzip bytes written under a
        // ".zst" name) is still decoded correctly rather than corrupting every line.
        if (source.CanSeek)
        {
            // Save and restore the exact starting position rather than seeking to absolute 0, so a
            // stream that was handed to us mid-read (or a substream) is left where it started.
            var originalPosition = source.Position;
            Span<byte> header = stackalloc byte[4];
            var read = ReadHeader(source, header);
            source.Position = originalPosition;

            if (read >= 2 && header[0] == 0x1F && header[1] == 0x8B)
                return Codec.Gzip;
            if (read >= 4 && header[0] == 0x28 && header[1] == 0xB5 && header[2] == 0x2F && header[3] == 0xFD)
                return Codec.Zstd;
            if (read >= 4 && header[0] == 0x04 && header[1] == 0x22 && header[2] == 0x4D && header[3] == 0x18)
                return Codec.Lz4;
        }

        // No stable signature matched (or the stream is not seekable): fall back to the declared
        // extension. Brotli has no reliable magic number, so the extension is the only signal for it.
        if (HasSuffix(path, ".br"))
            return Codec.Brotli;
        if (HasSuffix(path, ".gz") || HasSuffix(path, ".gzip"))
            return Codec.Gzip;
        if (HasSuffix(path, ".zst"))
            return Codec.Zstd;
        if (HasSuffix(path, ".lz4"))
            return Codec.Lz4;

        return Codec.None;
    }

    private static int ReadHeader(Stream source, Span<byte> header)
    {
        var total = 0;
        while (total < header.Length)
        {
            var read = source.Read(header[total..]);
            if (read == 0)
                break;

            total += read;
        }

        return total;
    }

    private static bool HasSuffix(string path, string suffix) =>
        path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
}
