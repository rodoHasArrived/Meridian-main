using System.Text;

namespace Meridian.Storage.Policies;

/// <summary>
/// Encodes provider and symbol identities as single, collision-free filesystem path segments.
/// </summary>
/// <remarks>
/// Plain ASCII identifiers remain readable. Any value that could be ambiguous, unsafe, or conflict
/// with the flat-file underscore delimiter is encoded as canonical uppercase UTF-8 hex prefixed by
/// <c>~</c>. Because the prefix is never emitted verbatim, encoded and plain values cannot collide.
/// </remarks>
internal static class StoragePathSegmentCodec
{
    private const char EncodedPrefix = '~';
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public static string Encode(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return CanRemainVerbatim(value)
            ? value
            : string.Concat(EncodedPrefix, Convert.ToHexString(StrictUtf8.GetBytes(value)));
    }

    /// <summary>
    /// Encodes a symbol using the same invariant uppercase identity used by <c>SymbolId</c>.
    /// This keeps writer paths and query candidate pruning aligned for case variants.
    /// </summary>
    public static string EncodeSymbol(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return Encode(value.ToUpperInvariant());
    }

    public static string Decode(string segment)
    {
        ArgumentNullException.ThrowIfNull(segment);

        if (segment.Length == 0 || segment[0] != EncodedPrefix)
            return segment;

        var hex = segment[1..];
        if (hex.Length % 2 != 0)
            return segment;

        try
        {
            return StrictUtf8.GetString(Convert.FromHexString(hex));
        }
        catch (FormatException)
        {
            return segment;
        }
        catch (DecoderFallbackException)
        {
            return segment;
        }
    }

    /// <summary>
    /// Produces the pre-codec path spelling so readers can still discover existing JSONL files.
    /// New writes must use <see cref="Encode"/> or <see cref="EncodeSymbol"/>.
    /// </summary>
    public static string EncodeLegacyForLookup(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (string.IsNullOrWhiteSpace(value))
            return "_unknown";

        Span<char> buffer = stackalloc char[value.Length];
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            buffer[index] = char.IsLetterOrDigit(character) || character is '-' or '.'
                ? character
                : '_';
        }

        return new string(buffer);
    }

    private static bool CanRemainVerbatim(string value)
    {
        if (value.Length == 0 ||
            value[0] == EncodedPrefix ||
            value is "." or ".." ||
            value[^1] == '.' ||
            IsReservedWindowsDeviceName(value))
        {
            return false;
        }

        foreach (var character in value)
        {
            var isAsciiLetter = character is >= 'A' and <= 'Z' or >= 'a' and <= 'z';
            var isAsciiDigit = character is >= '0' and <= '9';
            if (!isAsciiLetter && !isAsciiDigit && character is not '-' and not '.')
                return false;
        }

        return true;
    }

    private static bool IsReservedWindowsDeviceName(string value)
    {
        var dotIndex = value.IndexOf('.');
        var baseName = dotIndex >= 0 ? value[..dotIndex] : value;

        if (baseName.Equals("CON", StringComparison.OrdinalIgnoreCase) ||
            baseName.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
            baseName.Equals("AUX", StringComparison.OrdinalIgnoreCase) ||
            baseName.Equals("NUL", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return baseName.Length == 4 &&
               baseName[3] is >= '1' and <= '9' &&
               (baseName.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ||
                baseName.StartsWith("LPT", StringComparison.OrdinalIgnoreCase));
    }
}
