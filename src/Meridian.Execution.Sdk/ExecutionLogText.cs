namespace Meridian.Execution.Sdk;

/// <summary>
/// Renders caller-supplied order text for log output.
/// <para>
/// Nothing upstream of the pre-trade gate is required to constrain <c>OrderRequest.Symbol</c>: the
/// Security Master gate that would reject an unregistered symbol is optional, and a rule's
/// rejection reason can embed the symbol verbatim. A value carrying a line break renders as an
/// extra line in any text sink, which would let an order submitter forge risk decisions in the
/// record the risk gate exists to produce. Structured sinks escape it; text sinks do not, and the
/// pre-trade log has to be trustworthy under both.
/// </para>
/// <para>
/// This is a rendering concern only. The unaltered value still reaches the audit trail, where the
/// serializer escapes it, so sanitizing here loses no evidence.
/// </para>
/// </summary>
public static class ExecutionLogText
{
    /// <summary>
    /// Stands in for a character that must not reach a log line. Not valid in any symbol, so its
    /// presence tells a reader the value was substituted rather than submitted that way.
    /// </summary>
    private const char Replacement = '?';

    /// <summary>Marks a value cut short by <see cref="MaxRenderedLength"/>.</summary>
    private const string TruncationMarker = "...";

    /// <summary>
    /// Caps a rendered value so that one order cannot dominate the log. Comfortably above any real
    /// symbol or rule reason.
    /// </summary>
    public const int MaxRenderedLength = 256;

    /// <summary>
    /// Returns <paramref name="value"/> with line breaks and other control characters replaced, and
    /// over-long input truncated. Null and empty input pass through unchanged.
    /// </summary>
    public static string? ForLog(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        var truncated = value.Length > MaxRenderedLength;
        var length = truncated ? MaxRenderedLength : value.Length;

        var rendered = string.Create(length, value, static (destination, source) =>
        {
            for (var i = 0; i < destination.Length; i++)
            {
                var candidate = source[i];
                destination[i] = IsUnsafeForLog(candidate) ? Replacement : candidate;
            }
        });

        return truncated ? rendered + TruncationMarker : rendered;
    }

    private static bool IsUnsafeForLog(char candidate) =>
        char.IsControl(candidate)
        // Neither is a control character, but line- and paragraph-separator start a new line in
        // enough log viewers to be worth the same treatment.
        || candidate is '\u2028' or '\u2029';
}
