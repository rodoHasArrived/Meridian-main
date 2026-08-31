using Meridian.Contracts.Etl;
using Meridian.Contracts.Integrity;
using Meridian.Infrastructure.Etl.Sftp;
using Meridian.Storage.Etl;

namespace Meridian.Infrastructure.Etl;

public sealed class SftpFileSourceReader : IEtlSourceReader
{
    private readonly EtlStagingStore _stagingStore;
    private readonly ISftpClientFactory _clientFactory;
    private readonly ISftpCredentialResolver _credentialResolver;
    private readonly ISftpCapabilityService _capabilityService;

    /// <summary>
    /// Creates a reader. Every dependency is required, including the capability gate.
    /// </summary>
    /// <remarks>
    /// The convenience overloads that defaulted <paramref name="capabilityService"/> to a fresh
    /// <see cref="SftpCapabilityService"/> are gone. In a default EnableSftp=false build that
    /// default reports not-ready, so a caller supplying a working custom
    /// <see cref="ISftpClientFactory"/> through a short overload had every connection path throw
    /// before its factory was reached — the overload silently disabled the transport it was given.
    /// Requiring the argument makes that a compile error instead of a runtime surprise, and
    /// matches <see cref="SftpFilePublisher"/>.
    /// </remarks>
    public SftpFileSourceReader(
        EtlStagingStore stagingStore,
        ISftpClientFactory clientFactory,
        ISftpCredentialResolver credentialResolver,
        ISftpCapabilityService capabilityService)
    {
        _stagingStore = stagingStore;
        _clientFactory = clientFactory;
        _credentialResolver = credentialResolver;
        _capabilityService = capabilityService;
    }

    public EtlSourceKind Kind => EtlSourceKind.Sftp;

    public async Task<IReadOnlyList<EtlRemoteFile>> ListFilesAsync(EtlSourceDefinition source, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        EnsureCapable(source);
        var location = SftpRemoteLocation.ParseRequired(source.Location, "source");
        var credential = await _credentialResolver.ResolveAsync(source, ct).ConfigureAwait(false);
        using var client = CreateClient(source, location, credential);
        ct.ThrowIfCancellationRequested();
        client.Connect();
        try
        {
            ct.ThrowIfCancellationRequested();
            var pattern = string.IsNullOrWhiteSpace(source.FilePattern) ? "*.csv;*.xlsx" : source.FilePattern!;
            var files = client.ListDirectory(location.RemotePath)
                .Where(f => !f.IsDirectory && !f.IsSymbolicLink)
                .Where(f => MatchesAny(f.Name, pattern))
                .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
                .Select(f => new EtlRemoteFile
                {
                    Path = f.FullName,
                    Name = f.Name,
                    SizeBytes = f.Length,
                    LastModifiedUtc = f.LastWriteTimeUtc
                })
                .ToList();
            return files;
        }
        finally
        {
            client.Disconnect();
        }
    }

    public async Task<EtlStagedFile> StageFileAsync(string jobId, EtlSourceDefinition source, EtlRemoteFile file, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        EnsureCapable(source);
        var location = SftpRemoteLocation.ParseRequired(source.Location, "source");
        if (!location.ContainsFile(file.Path))
            throw new InvalidOperationException("SFTP remote file must be under the configured source path.");

        var credential = await _credentialResolver.ResolveAsync(source, ct).ConfigureAwait(false);
        using var client = CreateClient(source, location, credential);
        ct.ThrowIfCancellationRequested();
        client.Connect();
        var tempPath = Path.Combine(Path.GetTempPath(), "meridian-sftp-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            await using (var temp = new FileStream(tempPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.DeleteOnClose))
            {
                ct.ThrowIfCancellationRequested();
                client.DownloadFile(file.Path, temp);
                ct.ThrowIfCancellationRequested();
                temp.Position = 0;
                return await _stagingStore.StageAsync(jobId, file, temp, ct).ConfigureAwait(false);
            }
        }
        finally
        {
            client.Disconnect();
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    public async Task PostProcessFileAsync(EtlSourceDefinition source, EtlRemoteFile file, bool succeeded, CancellationToken ct = default)
    {
        if (source.PostProcessingAction == EtlSourcePostProcessingAction.LeaveInPlace ||
            (succeeded && source.PostProcessingAction == EtlSourcePostProcessingAction.MoveToError) ||
            (!succeeded && source.PostProcessingAction != EtlSourcePostProcessingAction.MoveToError))
        {
            return;
        }

        EnsureCapable(source);
        var location = SftpRemoteLocation.ParseRequired(source.Location, "source");
        if (!location.ContainsFile(file.Path))
            throw new InvalidOperationException("SFTP remote file must be under the configured source path.");

        var credential = await _credentialResolver.ResolveAsync(source, ct).ConfigureAwait(false);
        using var client = CreateClient(source, location, credential);
        client.Connect();
        try
        {
            switch (source.PostProcessingAction)
            {
                case EtlSourcePostProcessingAction.Delete:
                    client.DeleteFile(file.Path);
                    break;
                case EtlSourcePostProcessingAction.MoveToArchive:
                    MoveRemoteFile(client, file, source.ArchiveLocation);
                    break;
                case EtlSourcePostProcessingAction.MoveToError:
                    MoveRemoteFile(client, file, source.ErrorLocation);
                    break;
                case EtlSourcePostProcessingAction.WriteDoneMarker:
                    using (var marker = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(DateTimeOffset.UtcNow.ToString("O"))))
                        client.UploadFile(marker, file.Path + ".done", canOverwrite: true);
                    break;
            }
        }
        finally
        {
            client.Disconnect();
        }
    }

    /// <summary>
    /// Moves a processed remote source into its retention directory, verifying the destination
    /// contents before anything is removed. A free name is renamed into directly; a name holding
    /// identical content means the move already ran, so the source is dropped; a name holding
    /// different content resolves to a deterministic content-addressed sibling so both survive.
    /// </summary>
    private static void MoveRemoteFile(ISftpClient client, EtlRemoteFile file, string? remoteDirectory)
    {
        if (string.IsNullOrWhiteSpace(remoteDirectory))
            throw new InvalidOperationException("Remote post-processing move requires an archive or error location.");

        var normalizedDirectory = SftpRemoteLocation.NormalizePath(remoteDirectory.TrimEnd('/'));
        EnsureRemoteDirectory(client, normalizedDirectory);

        var destination = SftpRemoteLocation.Combine(normalizedDirectory, file.Name);
        if (!client.Exists(destination))
        {
            // Existence was just checked, so overwriting is never the intent here.
            client.RenameFile(file.Path, destination, canOverwrite: false);
            return;
        }

        // Only a collision pays for the content comparison; the ordinary path above adds one
        // existence check and no transfers.
        var sourceHash = ComputeRemoteHash(client, file.Path);
        if (string.Equals(sourceHash, ComputeRemoteHash(client, destination), StringComparison.Ordinal))
        {
            client.DeleteFile(file.Path);
            return;
        }

        var disambiguated = SftpRemoteLocation.Combine(
            normalizedDirectory,
            EtlArchiveNaming.BuildCollisionSafeName(file.Name, sourceHash));
        if (client.Exists(disambiguated))
        {
            if (!string.Equals(sourceHash, ComputeRemoteHash(client, disambiguated), StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"ETL retention path '{disambiguated}' already holds different content than source '{file.Path}'. " +
                    "The source was left in place; resolve the retained file before retrying.");
            }

            client.DeleteFile(file.Path);
            return;
        }

        client.RenameFile(file.Path, disambiguated, canOverwrite: false);
    }

    /// <summary>
    /// Hashes a remote file through a temporary spill rather than buffering it in memory, the same
    /// shape <see cref="StageFileAsync"/> already uses for downloads. Only a name collision pays
    /// for this.
    /// </summary>
    private static string ComputeRemoteHash(ISftpClient client, string path)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), "meridian-sftp-hash-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using var temp = new FileStream(
                tempPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920, FileOptions.DeleteOnClose);
            client.DownloadFile(path, temp);
            temp.Position = 0;
            return Sha256Digest.Compute(temp);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    private static void EnsureRemoteDirectory(ISftpClient client, string normalizedDirectory)
    {
        var segments = normalizedDirectory.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var current = normalizedDirectory.StartsWith('/') ? "/" : string.Empty;
        foreach (var segment in segments)
        {
            current = current == "/"
                ? "/" + segment
                : string.IsNullOrEmpty(current)
                    ? segment
                    : current + "/" + segment;
            if (!client.Exists(current))
                client.CreateDirectory(current);
        }
    }

    /// <summary>
    /// Rejects a source that is not ready before any other work happens on the read path.
    /// </summary>
    /// <remarks>
    /// The read path was left ungated while the publisher checked capability, so a default
    /// EnableSftp=false build accepted an SFTP source and then surfaced the disabled stub's
    /// NotSupportedException from list, preview, and ingestion as a transport failure. That is
    /// the same accepted-then-broken shape the destination fix removed, and it made the stated
    /// "reject in production, fail closed" disposition true of exports only.
    ///
    /// This runs before <see cref="SftpRemoteLocation.ParseRequired"/> and credential resolution
    /// rather than at client construction. Both of those throw on their own for a malformed
    /// location or an unset <c>env:</c> variable, so a source that is *both* misconfigured and
    /// running on a build without SFTP reported only the configuration error and never mentioned
    /// that real SFTP is absent — the operator fixes the URI, retries, and hits the same wall for
    /// a reason they were never told. Evaluate aggregates every readiness issue, so checking it
    /// first reports all of them at once, which is what the disposition advertises.
    /// </remarks>
    private void EnsureCapable(EtlSourceDefinition source)
    {
        var status = _capabilityService.Evaluate(source);
        if (!status.Ready)
        {
            throw new InvalidOperationException(
                "SFTP import is not available for this source: " + string.Join(" ", status.Issues));
        }
    }

    private ISftpClient CreateClient(EtlSourceDefinition source, SftpRemoteLocation location, SftpCredentialMaterial credential)
    {
        // Re-checked here so a future caller that reaches CreateClient without going through a
        // public entry point cannot skip the gate. Evaluate is pure and cheap.
        EnsureCapable(source);

        return _clientFactory.Create(SftpConnectionOptions.Create(
            location.Host,
            location.Port,
            credential.Username,
            credential.Password,
            source.HostKeySha256Fingerprint));
    }

    private static bool MatchesAny(string fileName, string pattern)
        => pattern
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(part => Matches(fileName, part));

    private static bool Matches(string fileName, string pattern)
        => pattern.StartsWith("*.", StringComparison.Ordinal)
            ? fileName.EndsWith(pattern[1..], StringComparison.OrdinalIgnoreCase)
            : fileName.Contains(pattern, StringComparison.OrdinalIgnoreCase);
}
