namespace Meridian.Application.ProviderRouting;

/// <summary>
/// Neutralizes request-supplied identifiers (connection, tenant, migration and actor values)
/// before they reach provider-routing log templates: control characters, including CR/LF, become
/// <c>_</c> and unbounded values are truncated, so forged input cannot split or fabricate a record.
/// Server-generated identifiers do not need it.
/// </summary>
internal static class ProviderRoutingLogValues
{
    private const int MaxLength = 256;

    public static string Safe(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;
        var bounded = value.Length > MaxLength ? value[..MaxLength] : value;
        var buffer = bounded.ToCharArray();
        for (var i = 0; i < buffer.Length; i++)
            if (char.IsControl(buffer[i]) && buffer[i] is not '\r' and not '\n')
                buffer[i] = '_';
        // Line endings last via String.Replace, the barrier static log-injection analysis models.
        return new string(buffer).Replace('\r', '_').Replace('\n', '_');
    }
}
