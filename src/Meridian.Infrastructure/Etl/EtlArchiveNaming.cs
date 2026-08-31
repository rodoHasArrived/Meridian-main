namespace Meridian.Infrastructure.Etl;

/// <summary>
/// Collision-safe naming for ETL source post-processing moves.
/// <para>
/// An archive or error location is a single configured directory shared by every run of a source,
/// and sources are enumerated by pattern with no cross-run name dedupe. A scheduled drop that
/// always lands the same well-known name — <c>positions.csv</c> each morning — therefore resolves
/// to one destination path forever. Overwriting it destroys the previously retained source with no
/// record that it existed, so a name that is already taken by *different* content is disambiguated
/// by that content's hash instead.
/// </para>
/// </summary>
internal static class EtlArchiveNaming
{
    /// <summary>
    /// Deterministic sibling name for <paramref name="fileName"/> carrying content
    /// <paramref name="contentHashSha256"/>. Deterministic by construction: identical content
    /// always resolves to the same name, so a retried move is idempotent rather than duplicating.
    /// The extension is preserved last so downstream pattern matching still selects the file.
    /// </summary>
    public static string BuildCollisionSafeName(string fileName, string contentHashSha256)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentHashSha256);

        var stem = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        return $"{stem}.sha256-{contentHashSha256[..16]}{extension}";
    }
}
