namespace Meridian.Execution.Logging;

/// <summary>
/// Neutralizes client-controlled strings before they reach log templates: control
/// characters (including CR/LF) are replaced with <c>_</c> so forged input cannot
/// fabricate or split log records, and unbounded values are truncated. Use for any
/// caller-supplied value (symbols, actors, reasons, metadata) flowing into a log call;
/// server-generated identifiers do not need it.
/// </summary>
public static class LogSanitizer
{
    private const int MaxLength = 256;

    public static string Sanitize(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var bounded = value.Length > MaxLength ? value[..MaxLength] : value;
        if (ContainsOtherControlCharacter(bounded))
        {
            bounded = string.Create(bounded.Length, bounded, static (span, source) =>
            {
                for (var i = 0; i < source.Length; i++)
                {
                    var ch = source[i];
                    span[i] = char.IsControl(ch) && ch is not '\r' and not '\n' ? '_' : ch;
                }
            });
        }

        // Line endings are neutralized last via String.Replace so static log-injection
        // analysis recognizes the flow as sanitized (String.Replace is the modeled barrier).
        return bounded.Replace('\r', '_').Replace('\n', '_');
    }

    private static bool ContainsOtherControlCharacter(string value)
    {
        foreach (var ch in value)
        {
            if (char.IsControl(ch) && ch is not '\r' and not '\n')
            {
                return true;
            }
        }

        return false;
    }
}
