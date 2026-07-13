namespace Meridian.Application.Subscriptions.Services;

/// <summary>
/// Preserves an unreadable JSON store file before a service falls back to an empty in-memory set.
/// The stores in this folder follow a load-mutate-save pattern, so falling back to an empty set
/// after a swallowed load failure would let the next save atomically overwrite the user's data.
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
    /// <returns>The quarantine path the unreadable file was preserved at.</returns>
    public static string PreserveOrThrow(string path, Exception loadFailure)
    {
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
