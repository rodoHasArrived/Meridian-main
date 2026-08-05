namespace Meridian.Storage.FundStructure;

public interface IFundStructureStateStore
{
    /// <summary>
    /// Filesystem path backing this store, or <see langword="null"/> when the store has no file
    /// behind it. Callers use this to preserve an unreadable snapshot before falling back to an
    /// empty working set, because the next save would otherwise overwrite it atomically.
    /// </summary>
    string? BackingFilePath { get; }

    string? Load();
    Task SaveAsync(string json, CancellationToken ct);
}
