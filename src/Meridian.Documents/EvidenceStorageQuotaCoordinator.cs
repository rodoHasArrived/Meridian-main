using System.Text.Json;
using Meridian.Storage.Archival;

namespace Meridian.Documents;

/// <summary>
/// Durable, filesystem-coordinated Evidence Vault admission. Published usage is supplied by the
/// vault adapter; quota policy and reservation ownership are independent of UI contracts.
/// </summary>
public sealed class EvidenceStorageQuotaCoordinator
{
    private readonly string _dataRoot;
    private readonly string _journalDirectory;
    private readonly EvidenceStorageQuotaOptions _options;
    private readonly Func<string, long> _publishedTenantBytes;
    private readonly Func<string, long> _availableDiskBytes;
    private readonly Func<string, CancellationToken, Task<bool>> _recoverAttempt;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        // Missing accounting fields must not silently become zero and release reserved capacity.
        RespectRequiredConstructorParameters = true
    };

    /// <summary>
    /// Creates a coordinator for one data root. The recovery callback must preserve published
    /// evidence and return false when owned staging could not be safely removed. It runs while
    /// the global gate and the attempt lease are held, including during normal disposal.
    /// </summary>
    public EvidenceStorageQuotaCoordinator(
        string dataRoot,
        EvidenceStorageQuotaOptions options,
        Func<string, long> publishedTenantBytes,
        Func<string, long>? availableDiskBytes = null,
        Func<string, CancellationToken, Task<bool>>? recoverAbandonedAttempt = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(publishedTenantBytes);
        _dataRoot = Path.GetFullPath(dataRoot);
        _journalDirectory = Path.Combine(_dataRoot, "workstation", "evidence-quota");
        _options = options.ValidateAndSnapshot();
        _publishedTenantBytes = publishedTenantBytes;
        _availableDiskBytes = availableDiskBytes ?? AvailableDiskBytes;
        _recoverAttempt = recoverAbandonedAttempt ?? ((_, _) => Task.FromResult(true));
    }

    /// <summary>Atomically admits a package against tenant, package, count, and disk budgets.</summary>
    public async Task<EvidenceStorageReservation> ReserveAsync(
        string tenantId, long estimatedBytes, int artifactCount, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentOutOfRangeException.ThrowIfNegative(estimatedBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(artifactCount);
        await using var gate = await AcquireGateAsync(ct).ConfigureAwait(false);
        await RecoverUnderGateAsync(ct).ConfigureAwait(false);
        var id = Guid.NewGuid().ToString("N");
        var record = new ReservationRecord(id, tenantId.Trim(), estimatedBytes, 0, 0, artifactCount);
        CheckCapacity(record, ReadRecords());
        var lease = OpenLease(id);
        try
        {
            await SaveAsync(record, ct).ConfigureAwait(false);
            return new EvidenceStorageReservation(this, id, lease);
        }
        catch
        {
            await lease.DisposeAsync().ConfigureAwait(false);
            File.Delete(LeasePath(id));
            throw;
        }
    }

    /// <summary>Reclaims only abandoned attempts whose operating-system ownership lease is free.</summary>
    public async Task<int> RecoverAbandonedAsync(CancellationToken ct = default)
    {
        await using var gate = await AcquireGateAsync(ct).ConfigureAwait(false);
        return await RecoverUnderGateAsync(ct).ConfigureAwait(false);
    }

    internal async Task BeforeWriteAsync(EvidenceStorageReservation reservation, long bytes, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bytes);
        await using var gate = await AcquireGateAsync(ct).ConfigureAwait(false);
        var record = ReadActive(reservation);
        var required = Add(Add(record.WrittenBytes, record.PreparedBytes), bytes);
        var updated = record with
        {
            ReservedBytes = Math.Max(record.ReservedBytes, required),
            PreparedBytes = Add(record.PreparedBytes, bytes)
        };
        CheckCapacity(updated, ReadRecords());
        await SaveAsync(updated, ct).ConfigureAwait(false);
    }

    internal async Task AfterWriteAsync(EvidenceStorageReservation reservation, long bytes, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bytes);
        await using var gate = await AcquireGateAsync(ct).ConfigureAwait(false);
        var record = ReadActive(reservation);
        if (bytes > record.PreparedBytes)
        {
            throw new InvalidOperationException("Evidence writes must reserve capacity before writing.");
        }
        await SaveAsync(record with
        {
            WrittenBytes = Add(record.WrittenBytes, bytes),
            PreparedBytes = record.PreparedBytes - bytes
        }, ct).ConfigureAwait(false);
    }

    internal async Task PublishAsync(
        EvidenceStorageReservation reservation, Func<CancellationToken, Task> publish, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(publish);
        await using var gate = await AcquireGateAsync(ct).ConfigureAwait(false);
        await RecoverUnderGateAsync(ct).ConfigureAwait(false);
        var record = ReadActive(reservation);
        if (record.PreparedBytes != 0)
        {
            throw new InvalidOperationException("Evidence publication requires reconciliation of all completed writes.");
        }
        var reconciled = record with { ReservedBytes = record.WrittenBytes };
        CheckCapacity(reconciled, ReadRecords());
        await SaveAsync(reconciled, ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        await publish(ct).ConfigureAwait(false);
        // Once the adapter publishes its index, request cancellation cannot release evidence
        // or report a cancellation merely because the reservation journal still needs cleanup.
        if (await _recoverAttempt(record.Id, CancellationToken.None).ConfigureAwait(false))
        {
            await DeleteRecordAsync(record.Id).ConfigureAwait(false);
        }
        await CloseLeaseAsync(reservation).ConfigureAwait(false);
    }

    internal async Task ReleaseAsync(EvidenceStorageReservation reservation)
    {
        await using var gate = await AcquireGateAsync(CancellationToken.None).ConfigureAwait(false);
        if (reservation.Lease is null)
        {
            return;
        }
        try
        {
            if (await _recoverAttempt(reservation.Id, CancellationToken.None).ConfigureAwait(false))
            {
                await DeleteRecordAsync(reservation.Id).ConfigureAwait(false);
            }
        }
        finally
        {
            // Failed cleanup retains its journal charge but relinquishes the ownership lease
            // so the next operation (or restarted process) can retry safe cleanup.
            await CloseLeaseAsync(reservation).ConfigureAwait(false);
        }
    }

    private async Task<int> RecoverUnderGateAsync(CancellationToken ct)
    {
        var recovered = 0;
        // Include a lease created immediately before a crash that preceded journal persistence.
        var ids = Directory.EnumerateFiles(_journalDirectory, "*.json")
            .Concat(Directory.EnumerateFiles(_journalDirectory, "*.lease"))
            .Select(Path.GetFileNameWithoutExtension)
            .Concat(Directory.EnumerateFiles(_journalDirectory, ".*.tmp").Select(TemporaryRecordOwner)
                .Where(static id => id is not null))
            .Distinct(StringComparer.Ordinal).ToArray();
        foreach (var id in ids)
        {
            ct.ThrowIfCancellationRequested();
            ValidateId(id!);
            FileStream lease;
            try
            {
                lease = OpenLease(id!);
            }
            catch (IOException ex) when (IsLockContention(ex))
            {
                // A lifetime lease held by an active owner is never expired by wall-clock age.
                continue;
            }
            var cleaned = false;
            await using (lease.ConfigureAwait(false))
            {
                // Atomic journal replacement may have been interrupted before rename. Only
                // this unlocked attempt's exact temporary-file pattern is eligible for removal.
                foreach (var temporaryPath in Directory.EnumerateFiles(_journalDirectory, $".{id}.json.*.tmp")
                    .Where(path => TemporaryRecordOwner(path) == id))
                {
                    File.Delete(temporaryPath);
                }
                cleaned = await _recoverAttempt(id!, CancellationToken.None).ConfigureAwait(false);
                if (cleaned)
                {
                    await DeleteRecordAsync(id!).ConfigureAwait(false);
                    recovered++;
                }
            }
            if (cleaned)
            {
                File.Delete(LeasePath(id!));
            }
        }
        return recovered;
    }

    private void CheckCapacity(ReservationRecord candidate, IReadOnlyList<ReservationRecord> records)
    {
        if (candidate.ArtifactCount > _options.MaxArtifactsPerPackage)
        {
            throw new EvidenceStorageQuotaExceededException("package-count", "Evidence package exceeds its retained artifact count limit.");
        }
        if (candidate.ReservedBytes > _options.MaxPackageBytes)
        {
            throw new EvidenceStorageQuotaExceededException("package-bytes", "Evidence package exceeds its byte limit.");
        }
        var tenantBytes = _publishedTenantBytes(candidate.TenantId);
        ArgumentOutOfRangeException.ThrowIfNegative(tenantBytes);
        var outstandingBytes = 0L;
        foreach (var record in records.Where(record => record.Id != candidate.Id).Append(candidate))
        {
            if (string.Equals(record.TenantId, candidate.TenantId, StringComparison.OrdinalIgnoreCase))
            {
                tenantBytes = Add(tenantBytes, record.ReservedBytes);
            }
            outstandingBytes = Add(outstandingBytes, record.ReservedBytes - record.WrittenBytes);
        }
        var tenantBudget = _options.TenantBudgetBytes.GetValueOrDefault(candidate.TenantId, _options.DefaultTenantBudgetBytes);
        if (tenantBytes > tenantBudget)
        {
            throw new EvidenceStorageQuotaExceededException("tenant-bytes", "Evidence tenant storage budget is exhausted.");
        }
        var availableBytes = _availableDiskBytes(_dataRoot);
        if (availableBytes < _options.MinimumDiskHeadroomBytes
            || outstandingBytes > availableBytes - _options.MinimumDiskHeadroomBytes)
        {
            throw new EvidenceStorageQuotaExceededException("disk-headroom", "Evidence storage has insufficient disk headroom for reserved writes.");
        }
    }

    private ReservationRecord ReadActive(EvidenceStorageReservation reservation)
    {
        ObjectDisposedException.ThrowIf(reservation.Lease is null, reservation);
        return ReadRecord(RecordPath(reservation.Id));
    }

    private ReservationRecord[] ReadRecords() =>
        Directory.EnumerateFiles(_journalDirectory, "*.json").Select(ReadRecord).ToArray();

    private static ReservationRecord ReadRecord(string path)
    {
        ReservationRecord record;
        try
        {
            record = JsonSerializer.Deserialize<ReservationRecord>(File.ReadAllBytes(path), JsonOptions)
                ?? throw new InvalidDataException("Evidence storage reservation is empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Evidence storage reservation is unreadable; admission is blocked.", ex);
        }
        ValidateId(record.Id);
        if (record.Id != Path.GetFileNameWithoutExtension(path)
            || string.IsNullOrWhiteSpace(record.TenantId)
            || record.ReservedBytes < 0 || record.WrittenBytes < 0 || record.PreparedBytes < 0
            || record.WrittenBytes > record.ReservedBytes
            || record.PreparedBytes > record.ReservedBytes - record.WrittenBytes
            || record.ArtifactCount < 0)
        {
            throw new InvalidDataException("Evidence storage reservation is invalid; admission is blocked.");
        }
        return record;
    }

    private Task SaveAsync(ReservationRecord record, CancellationToken ct) =>
        AtomicFileWriter.WriteAsync(RecordPath(record.Id), JsonSerializer.Serialize(record, JsonOptions), ct);

    private async Task DeleteRecordAsync(string id)
    {
        File.Delete(RecordPath(id));
        await AtomicFileWriter.SyncDirectoryAsync(_journalDirectory, CancellationToken.None).ConfigureAwait(false);
    }

    private async Task<FileStream> AcquireGateAsync(CancellationToken ct)
    {
        Directory.CreateDirectory(_journalDirectory);
        var lockPath = Path.Combine(_journalDirectory, "quota.lock");
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                // Never unlink this shared lock: another process may already be waiting on
                // its inode. Attempt-specific lease files can be removed under this gate.
                return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException ex) when (IsLockContention(ex))
            {
                await Task.Delay(25, ct).ConfigureAwait(false);
            }
        }
    }

    private async Task CloseLeaseAsync(EvidenceStorageReservation reservation)
    {
        var lease = reservation.Lease;
        reservation.Lease = null;
        if (lease is not null)
        {
            await lease.DisposeAsync().ConfigureAwait(false);
            if (!File.Exists(RecordPath(reservation.Id)))
            {
                File.Delete(LeasePath(reservation.Id));
            }
        }
    }

    private FileStream OpenLease(string id) =>
        new(LeasePath(id), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    private string RecordPath(string id) => Path.Combine(_journalDirectory, $"{id}.json");
    private string LeasePath(string id) => Path.Combine(_journalDirectory, $"{id}.lease");

    private static void ValidateId(string id)
    {
        if (!Guid.TryParseExact(id, "N", out _))
        {
            throw new InvalidDataException("Evidence storage reservation has an invalid attempt identifier.");
        }
    }

    private static bool IsLockContention(IOException exception) =>
        // Unix EAGAIN/EWOULDBLOCK and Windows sharing/lock violations. Disk-full or other I/O
        // failures are not contention and must propagate instead of entering an endless retry.
        (exception.HResult & 0xffff) is 11 or 32 or 33;

    private static string? TemporaryRecordOwner(string path)
    {
        var parts = Path.GetFileName(path).Split('.');
        return parts.Length == 5 && parts[0].Length == 0 && parts[2] == "json" && parts[4] == "tmp"
            && Guid.TryParseExact(parts[1], "N", out _) && Guid.TryParseExact(parts[3], "N", out _)
            ? parts[1] : null;
    }

    private static long Add(long left, long right)
    {
        if (right > long.MaxValue - left)
        {
            throw new EvidenceStorageQuotaExceededException("package-bytes", "Evidence storage byte accounting exceeds its supported limit.");
        }
        return left + right;
    }

    private static long AvailableDiskBytes(string path)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var drive = DriveInfo.GetDrives()
            .Where(drive => path.Equals(Path.TrimEndingDirectorySeparator(drive.Name), comparison)
                || path.StartsWith(Path.EndsInDirectorySeparator(drive.Name) ? drive.Name : drive.Name + Path.DirectorySeparatorChar, comparison))
            .OrderByDescending(drive => drive.Name.Length).First();
        return drive.AvailableFreeSpace;
    }

    private sealed record ReservationRecord(
        string Id, string TenantId, long ReservedBytes, long WrittenBytes, long PreparedBytes, int ArtifactCount);
}
