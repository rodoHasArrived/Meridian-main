namespace Meridian.Core.Config;

/// <summary>
/// Single registry of sensitive key/property-name fragments shared by every masking and
/// redaction surface (<see cref="SensitiveValueMasker"/>,
/// <see cref="Meridian.Core.Diagnostics.RuntimeDiagnosticRedactor"/>). The surfaces previously
/// carried overlapping-but-different lists, so a value could be masked in one output and leak
/// in another. Add new fragments here, never to a per-surface list.
/// </summary>
public static class SensitiveKeyRegistry
{
    /// <summary>
    /// Case-insensitive fragments; a key containing any fragment is considered sensitive.
    /// The union of the historical masker and redactor taxonomies — removing an entry
    /// narrows redaction somewhere, so entries may only be added.
    /// </summary>
    public static readonly IReadOnlyList<string> Fragments =
    [
        "password", "pwd", "secret", "key", "token", "credential",
        "connectionstring", "connection_string",
        "auth", "authorization", "session", "refresh", "bearer",
        "certificate"
    ];

    /// <summary>
    /// Returns true when the key/property/env-var name contains any sensitive fragment.
    /// </summary>
    public static bool IsSensitive(string? key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return false;
        }

        foreach (var fragment in Fragments)
        {
            if (key.Contains(fragment, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
