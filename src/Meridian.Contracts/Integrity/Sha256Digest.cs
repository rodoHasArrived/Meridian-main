using System.Security.Cryptography;
using System.Text;

namespace Meridian.Contracts.Integrity;

/// <summary>
/// The canonical SHA-256 digest primitive for Meridian. Every integrity check that compares a
/// retained digest against a recomputed one must route through this type rather than redeclaring a
/// local helper, so that a single digest cannot verify in one code path and be reported as a
/// mismatch in another.
/// </summary>
/// <remarks>
/// <para>
/// Two distinct questions are deliberately kept apart, because collapsing them is what turns a
/// data-hygiene problem into a phantom tamper alert:
/// </para>
/// <list type="bullet">
/// <item>
/// <description>
/// <b>Is this digest canonical?</b> — <see cref="IsCanonical"/>. Meridian's canonical form is 64
/// <i>lowercase</i> hex characters, which is what <see cref="Compute(ReadOnlySpan{byte})"/> emits.
/// This is the write-side contract: enforce it when persisting or certifying a digest so that
/// stored values stay canonical.
/// </description>
/// </item>
/// <item>
/// <description>
/// <b>Do these two digests denote the same bytes?</b> — <see cref="Compare"/> and
/// <see cref="FixedEquals"/>. Comparison accepts any well-formed hex, in <i>either</i> case, and
/// compares the decoded bytes. Hex casing is a presentation detail, not a security property, so an
/// uppercase digest that decodes to identical bytes is a match — not a tamper signal.
/// </description>
/// </item>
/// </list>
/// <para>
/// Comparison also reports a malformed input distinctly from a genuine mismatch, so callers can
/// surface "this digest is not a SHA-256 value" separately from "these bytes do not match".
/// </para>
/// </remarks>
public static class Sha256Digest
{
    /// <summary>Number of hex characters in a SHA-256 digest.</summary>
    public const int HexLength = 64;

    /// <summary>Computes the canonical (lowercase hex) SHA-256 digest of <paramref name="value"/>.</summary>
    public static string Compute(ReadOnlySpan<byte> value) =>
        Convert.ToHexStringLower(SHA256.HashData(value));

    /// <summary>
    /// Computes the canonical (lowercase hex) SHA-256 digest of the UTF-8 encoding of
    /// <paramref name="value"/>.
    /// </summary>
    public static string ComputeUtf8(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return Compute(Encoding.UTF8.GetBytes(value));
    }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="value"/> is in Meridian's canonical
    /// digest form: exactly <see cref="HexLength"/> lowercase hex characters. Use this on write and
    /// certification paths; use <see cref="Compare"/> or <see cref="FixedEquals"/> to compare two
    /// digests.
    /// </summary>
    public static bool IsCanonical(string? value) => IsHex(value, requireLowercase: true);

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="value"/> is <see cref="HexLength"/> hex
    /// characters in either case. Well-formed values decode to a comparable digest even when they
    /// are not canonical.
    /// </summary>
    public static bool IsWellFormed(string? value) => IsHex(value, requireLowercase: false);

    /// <summary>
    /// Returns the canonical lowercase form of a well-formed digest, or <see langword="null"/> when
    /// <paramref name="value"/> is not a well-formed digest. Use this to repair non-canonical input
    /// at an ingest boundary instead of rejecting it deeper in the stack.
    /// </summary>
    public static string? Normalize(string? value) =>
        IsWellFormed(value) ? value!.ToLowerInvariant() : null;

    /// <summary>
    /// Compares two digests in constant time, distinguishing a malformed input from a genuine
    /// mismatch.
    /// </summary>
    public static Sha256DigestComparison Compare(string? left, string? right)
    {
        var leftWellFormed = IsWellFormed(left);
        var rightWellFormed = IsWellFormed(right);

        if (!leftWellFormed || !rightWellFormed)
        {
            return (leftWellFormed, rightWellFormed) switch
            {
                (false, false) => Sha256DigestComparison.MalformedBoth,
                (false, true) => Sha256DigestComparison.MalformedLeft,
                _ => Sha256DigestComparison.MalformedRight
            };
        }

        // Convert.FromHexString is case-insensitive, so two digests that differ only in casing
        // decode to identical bytes and compare equal.
        return CryptographicOperations.FixedTimeEquals(
            Convert.FromHexString(left!),
            Convert.FromHexString(right!))
            ? Sha256DigestComparison.Match
            : Sha256DigestComparison.Mismatch;
    }

    /// <summary>
    /// Returns <see langword="true"/> only when both digests are well-formed and denote the same
    /// bytes. Prefer <see cref="Compare"/> where the caller can report a malformed digest
    /// separately from a mismatch.
    /// </summary>
    public static bool FixedEquals(string? left, string? right) =>
        Compare(left, right) == Sha256DigestComparison.Match;

    private static bool IsHex(string? value, bool requireLowercase)
    {
        if (value is not { Length: HexLength })
        {
            return false;
        }

        foreach (var character in value.AsSpan())
        {
            var isDigit = character is >= '0' and <= '9';
            var isLower = character is >= 'a' and <= 'f';
            var isUpper = character is >= 'A' and <= 'F';

            if (isDigit || isLower)
            {
                continue;
            }

            if (isUpper && !requireLowercase)
            {
                continue;
            }

            return false;
        }

        return true;
    }
}

/// <summary>Outcome of comparing two SHA-256 digests via <see cref="Sha256Digest.Compare"/>.</summary>
public enum Sha256DigestComparison
{
    /// <summary>Both digests are well-formed and denote the same bytes.</summary>
    Match = 0,

    /// <summary>Both digests are well-formed and denote different bytes — a genuine mismatch.</summary>
    Mismatch = 1,

    /// <summary>The left digest is not a well-formed SHA-256 value; no comparison was performed.</summary>
    MalformedLeft = 2,

    /// <summary>The right digest is not a well-formed SHA-256 value; no comparison was performed.</summary>
    MalformedRight = 3,

    /// <summary>Neither digest is a well-formed SHA-256 value; no comparison was performed.</summary>
    MalformedBoth = 4
}
