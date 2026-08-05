// Deliberately not "Meridian.Application.Storage": that name shadows the top-level Meridian.Storage
// for every file inside Meridian.Application, breaking unqualified references such as
// Storage.Archival.WriteAheadLog in PipelineFeatureRegistration.
namespace Meridian.Application.Durability;

/// <summary>
/// Preserves an unreadable JSON store file before a service falls back to an empty in-memory set.
/// Stores that follow a load-mutate-save pattern would otherwise let the next save atomically
/// overwrite the user's data after a swallowed load failure, leaving nothing to recover from.
/// </summary>
internal static class CorruptStoreQuarantine
{
    /// <summary>
    /// Copies the unreadable store file to <c>&lt;path&gt;.corrupt-&lt;UTC timestamp&gt;</c> so the
    /// original contents survive any subsequent save. Throws <see cref="InvalidOperationException"/>
    /// (with the load failure as inner exception) when the copy fails, because continuing with an
    /// empty fallback without a preserved copy would let a destructive save permanently wipe the store.
    /// </summary>
    /// <param name="path">Path of the store file that failed to load.</param>
    /// <param name="loadFailure">The exception that caused the load to fail.</param>
    /// <returns>
    /// The quarantine path the unreadable file was preserved at, or <see langword="null"/>
    /// when the file no longer exists (e.g. removed between the failed read and the
    /// quarantine attempt) — nothing is left to preserve, so the empty fallback is safe.
    /// </returns>
    public static string? PreserveOrThrow(string path, Exception loadFailure)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        var quarantinePath = $"{path}.corrupt-{DateTime.UtcNow:yyyyMMddHHmmss}";
        try
        {
            File.Copy(path, quarantinePath, overwrite: true);
            return quarantinePath;
        }
        catch (Exception copyEx)
        {
            throw new InvalidOperationException(
                $"Failed to load '{path}' and could not quarantine the unreadable file to '{quarantinePath}' ({copyEx.GetType().Name}: {copyEx.Message}). " +
                "Refusing to continue with an empty store because a subsequent save would overwrite the existing data.",
                loadFailure);
        }
    }
}
