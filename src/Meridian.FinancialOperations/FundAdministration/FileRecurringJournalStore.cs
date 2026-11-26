using System.Text.Json;
using Meridian.Storage.Archival;
using static Meridian.FinancialOperations.FundAdministration.RecurringJournalDocumentRules;

namespace Meridian.FinancialOperations.FundAdministration;

/// <summary>
/// Durable recurring definitions and claims. All processes sharing this directory serialize
/// their entire intake cycle using an OS-exclusive lease, not an instance-only semaphore.
/// The snapshot is flushed before atomic replacement. Missing/corrupt state never becomes empty state.
/// Hosts with a ledger database use <see cref="PostgresRecurringJournalStore"/> instead, whose lease
/// spans processes and hosts rather than one shared filesystem root.
/// </summary>
public sealed class FileRecurringJournalStore : IRecurringJournalStore, IRecurringJournalStoreProvisioning
{
    private const string Marker = "meridian-recurring-journals-v1";
    private readonly string _directory;
    private string StatePath => Path.Combine(_directory, "recurring-journals.json");
    private string MarkerPath => Path.Combine(_directory, "initialized");
    // A sibling marker survives loss of the registry directory itself.
    private string ProvisionedPath => _directory + ".provisioned";

    public FileRecurringJournalStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
    }

    /// <summary>
    /// Explicit provisioning only; never called by the runner. The permanent marker refuses
    /// reinitialization if an initialized snapshot is later deleted or lost.
    /// </summary>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        Directory.CreateDirectory(_directory);
        await using var lease = await AcquireAsync(ct).ConfigureAwait(false);
        if (RetainedPathExists(MarkerPath) || RetainedPathExists(ProvisionedPath))
        {
            _ = await ReadAsync(ct).ConfigureAwait(false);
            return;
        }

        if (RetainedPathExists(StatePath))
            _ = await ReadSnapshotAsync(ct).ConfigureAwait(false);
        else
            await WriteAsync(Empty(), ct).ConfigureAwait(false);
        await AtomicFileWriter.WriteAsync(MarkerPath, Marker, ct).ConfigureAwait(false);
        await EnsureProvisioningWitnessAsync(ct).ConfigureAwait(false);
    }

    public async Task<IRecurringJournalSession> OpenSessionAsync(CancellationToken ct = default)
    {
        // Directory creation on read would disguise an unavailable mount or an incorrect data root.
        if (!RetainedPathExists(_directory))
        {
            if (RetainedPathExists(ProvisionedPath))
                throw new InvalidOperationException("The previously provisioned recurring journal directory is unavailable. Restore it before continuing.");
            throw new RecurringJournalStoreNotProvisionedException();
        }
        var lease = await AcquireAsync(ct).ConfigureAwait(false);
        try
        {
            if (!RetainedPathExists(MarkerPath) && !RetainedPathExists(StatePath) && !RetainedPathExists(ProvisionedPath))
                throw new RecurringJournalStoreNotProvisionedException();
            return new RecurringJournalDocumentSession(new FileLease(this, lease), await ReadAsync(ct).ConfigureAwait(false));
        }
        catch
        {
            await lease.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Reads the verified retained document under the lease for a one-time import into another
    /// backend. Returns <see langword="null"/> only when this store was never provisioned; a marker
    /// or snapshot that exists but cannot be verified fails closed rather than reading as empty.
    /// </summary>
    internal async Task<RecurringJournalDocument?> ReadForImportAsync(CancellationToken ct)
    {
        if (!Directory.Exists(_directory) || (!File.Exists(MarkerPath) && !File.Exists(StatePath)))
            return null;
        await using var lease = await AcquireAsync(ct).ConfigureAwait(false);
        return await ReadAsync(ct).ConfigureAwait(false);
    }

    private async Task<FileStream> AcquireAsync(CancellationToken ct)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                // Never unlink: replacing the inode while another process owns it could grant two leases.
                return new FileStream(Path.Combine(_directory, "recurring-journals.lock"),
                    FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException ex) when ((ex.HResult & 0xffff) is 11 or 32 or 33)
            {
                await Task.Delay(25, ct).ConfigureAwait(false);
            }
        }
    }

    private async Task<RecurringJournalDocument> ReadAsync(CancellationToken ct)
    {
        if (!RetainedPathExists(MarkerPath) || await File.ReadAllTextAsync(MarkerPath, ct).ConfigureAwait(false) != Marker)
            throw new InvalidOperationException("The durable recurring journal initialization marker is unavailable or invalid.");
        var document = await ReadSnapshotAsync(ct).ConfigureAwait(false);
        await EnsureProvisioningWitnessAsync(ct).ConfigureAwait(false);
        return document;
    }

    private async Task EnsureProvisioningWitnessAsync(CancellationToken ct)
    {
        if (!RetainedPathExists(ProvisionedPath))
            await AtomicFileWriter.WriteAsync(ProvisionedPath, Marker, ct).ConfigureAwait(false);
        else if (await File.ReadAllTextAsync(ProvisionedPath, ct).ConfigureAwait(false) != Marker)
            throw new InvalidOperationException("The durable recurring journal provisioning marker is invalid.");
    }

    private static bool RetainedPathExists(string path)
    {
        try
        {
            _ = File.GetAttributes(path);
            return true;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return false;
        }
    }

    private async Task<RecurringJournalDocument> ReadSnapshotAsync(CancellationToken ct)
    {
        try
        {
            var bytes = await File.ReadAllBytesAsync(StatePath, ct).ConfigureAwait(false);
            return ReadEnvelope(bytes);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or ArgumentException or NullReferenceException)
        {
            throw new InvalidOperationException("The durable recurring journal state is unavailable or invalid; restore the retained store before retrying.", ex);
        }
    }

    private Task WriteAsync(RecurringJournalDocument document, CancellationToken ct)
        => AtomicFileWriter.WriteAsync(StatePath, SerializeEnvelope(document).Envelope, ct);

    private sealed class FileLease(FileRecurringJournalStore owner, FileStream lockFile) : IRecurringJournalDocumentLease
    {
        public Task WriteAsync(RecurringJournalDocument document, CancellationToken ct) => owner.WriteAsync(document, ct);

        public ValueTask DisposeAsync() => lockFile.DisposeAsync();
    }
}
