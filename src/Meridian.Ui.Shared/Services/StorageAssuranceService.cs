using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Meridian.Contracts.Workstation;
using Meridian.DataIntegration.Canonicalization;
using Meridian.Storage;
using Meridian.Storage.Services;
using Meridian.Ui.Shared.Evidence;

namespace Meridian.Ui.Shared.Services;

public sealed class StorageAssuranceService
{
    private static readonly TimeSpan PreviewLifetime = TimeSpan.FromMinutes(15);
    private readonly StorageOptions _options;
    private readonly IDataQualityService? _quality;
    private readonly ITierMigrationService? _tiers;
    private readonly IEvidenceArtifactStore _evidence;
    private readonly ConcurrentDictionary<string, StorageMaintenancePreviewDto> _previews = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, StorageMaintenanceResultDto> _idempotency = new(StringComparer.Ordinal);

    public StorageAssuranceService(
        StorageOptions options,
        IEvidenceArtifactStore evidence,
        IDataQualityService? quality = null,
        ITierMigrationService? tiers = null)
    {
        _options = options;
        _evidence = evidence;
        _quality = quality;
        _tiers = tiers;
    }

    public async Task<StorageAssuranceSnapshotDto> GetSnapshotAsync(StorageAssurancePermissionsDto permissions, CancellationToken ct)
    {
        var root = ResolveRoot();
        var files = EnumerateSafeFiles(root).ToArray();
        var temporary = files.Count(static file => IsTemporary(file));
        var totalBytes = files.Sum(static file => file.Length);
        var drive = new DriveInfo(Path.GetPathRoot(root)!);
        var usedPercent = drive.TotalSize > 0 ? Math.Round(100d * (drive.TotalSize - drive.AvailableFreeSpace) / drive.TotalSize, 2) : 0d;

        var qualitySummary = await BuildQualitySummaryAsync(root, ct).ConfigureAwait(false);
        var alerts = await BuildAlertsAsync(ct).ConfigureAwait(false);
        var canonical = CanonicalizationMetrics.GetSnapshot();
        var canonicalProviders = canonical.ProviderParity.OrderBy(static item => item.Key).Select(item =>
            new CanonicalizationProviderSummaryDto(item.Key, item.Value.Total, item.Value.Success, item.Value.SoftFail, item.Value.HardFail, item.Value.MatchRatePercent)).ToArray();
        var totalCanonical = canonical.SuccessTotal + canonical.SoftFailTotal + canonical.HardFailTotal;
        var tierRows = await BuildTierRowsAsync(ct).ConfigureAwait(false);

        return new StorageAssuranceSnapshotDto(
            DateTimeOffset.UtcNow,
            new StorageHealthSummaryDto(
                Directory.Exists(root) ? "Healthy" : "Unavailable",
                Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
                totalBytes,
                files.Length,
                Directory.Exists(root),
                CanWrite(root),
                0,
                temporary,
                Directory.Exists(root) ? null : "The configured storage root is unavailable."),
            qualitySummary,
            new CanonicalizationAssuranceDto(
                canonical.ActiveVersion > 0,
                canonical.ActiveVersion,
                totalCanonical,
                canonical.SuccessTotal,
                canonical.SoftFailTotal,
                canonical.HardFailTotal,
                totalCanonical > 0 ? Math.Round(100d * canonical.SuccessTotal / totalCanonical, 2) : 0d,
                canonicalProviders),
            new StorageCapacitySummaryDto(totalBytes, drive.AvailableFreeSpace, usedPercent, null, usedPercent >= 90 ? "Critical" : usedPercent >= 75 ? "Warning" : "Healthy"),
            tierRows,
            alerts,
            permissions);
    }

    public async Task<StorageMaintenancePreviewDto> PreviewAsync(StorageMaintenancePreviewRequestDto request, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var previewId = Guid.NewGuid().ToString("N");
        var candidates = request.Action switch
        {
            StorageMaintenanceActionDto.Cleanup => BuildCleanupCandidates(),
            StorageMaintenanceActionDto.QualityCheck => BuildQualityCandidate(request.RelativePath),
            StorageMaintenanceActionDto.TierMigration => BuildTierCandidate(request.TargetTier),
            _ => throw new ArgumentOutOfRangeException(nameof(request.Action))
        };
        if (request.Action == StorageMaintenanceActionDto.TierMigration && _tiers is null)
            throw new InvalidOperationException("Tier migration service is not available.");
        if (request.Action == StorageMaintenanceActionDto.QualityCheck && _quality is null)
            throw new InvalidOperationException("Data quality service is not available.");

        var digest = ComputeDigest(string.Join('|', candidates.Select(static item => item.Fingerprint)));
        var confirmation = request.Action switch
        {
            StorageMaintenanceActionDto.Cleanup => $"DELETE {candidates.Count} FILES",
            StorageMaintenanceActionDto.TierMigration => $"MIGRATE TO {request.TargetTier?.Trim().ToUpperInvariant()}",
            _ => "RUN QUALITY CHECK"
        };
        var warnings = candidates.Count == 0 ? new[] { "No eligible items were found." } : Array.Empty<string>();
        var preview = new StorageMaintenancePreviewDto(
            previewId,
            request.Action,
            now,
            now + PreviewLifetime,
            digest,
            confirmation,
            candidates.Sum(static item => item.SizeBytes),
            candidates,
            NormalizeOptional(request.RelativePath),
            NormalizeOptional(request.TargetTier),
            warnings);
        _previews[previewId] = preview;
        await Task.CompletedTask;
        return preview;
    }

    public async Task<StorageMaintenanceResultDto> ExecuteAsync(
        StorageMaintenanceCommandRequestDto request,
        string actor,
        CancellationToken ct)
    {
        var idempotencyKey = $"{request.PreviewId}:{request.IdempotencyKey.Trim()}";
        if (_idempotency.TryGetValue(idempotencyKey, out var prior))
            return prior;
        if (!_previews.TryGetValue(request.PreviewId, out var preview))
            throw new KeyNotFoundException("Maintenance preview was not found.");
        if (preview.ExpiresAt <= DateTimeOffset.UtcNow)
            throw new TimeoutException("Maintenance preview has expired.");
        if (!string.Equals(preview.ConfirmationText, request.ConfirmationText.Trim(), StringComparison.Ordinal))
            throw new InvalidOperationException("Typed confirmation does not match the current preview.");
        if (string.IsNullOrWhiteSpace(request.Rationale))
            throw new ArgumentException("Rationale is required.", nameof(request));

        var started = DateTimeOffset.UtcNow;
        var items = preview.Action switch
        {
            StorageMaintenanceActionDto.Cleanup => ExecuteCleanup(preview),
            StorageMaintenanceActionDto.QualityCheck => await ExecuteQualityCheckAsync(preview, ct).ConfigureAwait(false),
            StorageMaintenanceActionDto.TierMigration => await ExecuteTierMigrationAsync(preview, ct).ConfigureAwait(false),
            _ => throw new ArgumentOutOfRangeException()
        };
        var completed = DateTimeOffset.UtcNow;
        var status = items.Any(static item => item.Status == "Failed") ? "PartialFailure" : "Completed";
        var affectedBytes = items.Where(static item => item.Status == "Completed").Sum(item => preview.Candidates.First(candidate => candidate.CandidateId == item.CandidateId).SizeBytes);
        var provisional = new StorageMaintenanceResultDto(
            Guid.NewGuid().ToString("N"), preview.Action, started, completed, status, affectedBytes, items, [], null, null);
        var intake = await RetainEvidenceAsync(preview, provisional, request.Rationale.Trim(), actor, ct).ConfigureAwait(false);
        var result = provisional with
        {
            EvidenceVaultId = intake.VaultIdentity.VaultId,
            EvidenceRoute = $"/data/evidence?subjectKind=run&subjectId={Uri.EscapeDataString(provisional.RunId)}"
        };
        _idempotency.TryAdd(idempotencyKey, result);
        _previews.TryRemove(preview.PreviewId, out _);
        return result;
    }

    public StorageMaintenanceActionDto? GetExecuteAction(string previewId, string idempotencyKey)
    {
        if (_previews.TryGetValue(previewId, out var preview))
            return preview.Action;

        var key = $"{previewId}:{idempotencyKey.Trim()}";
        return _idempotency.TryGetValue(key, out var result) ? result.Action : null;
    }

    private IReadOnlyList<StorageMaintenanceCandidateDto> BuildCleanupCandidates()
    {
        var root = ResolveRoot();
        return EnumerateSafeFiles(root)
            .Where(static file => IsTemporary(file))
            .Take(500)
            .Select(file => ToCandidate(root, file, "Temporary"))
            .ToArray();
    }

    private IReadOnlyList<StorageMaintenanceCandidateDto> BuildQualityCandidate(string? relativePath)
    {
        var full = ResolveWithinRoot(relativePath ?? ".");
        if (File.Exists(full))
        {
            var info = new FileInfo(full);
            return [ToCandidate(ResolveRoot(), info, "QualityTarget")];
        }
        if (!Directory.Exists(full))
            throw new FileNotFoundException("The quality-check target was not found.", full);
        return EnumerateSafeFiles(full)
            .Where(static file => file.Name.Contains(".jsonl", StringComparison.OrdinalIgnoreCase))
            .Take(500)
            .Select(file => ToCandidate(ResolveRoot(), file, "QualityTarget"))
            .ToArray();
    }

    private IReadOnlyList<StorageMaintenanceCandidateDto> BuildTierCandidate(string? targetTier)
    {
        if (!Enum.TryParse<StorageTier>(targetTier, true, out _))
            throw new ArgumentException("Target tier must be Hot, Warm, Cold, or Archive.", nameof(targetTier));
        var root = ResolveRoot();
        var info = new DirectoryInfo(root);
        return [new StorageMaintenanceCandidateDto(
            ComputeDigest(root)[..16], ".", "StorageRoot", EnumerateSafeFiles(root).Sum(static file => file.Length), new DateTimeOffset(info.LastWriteTimeUtc), ComputeDigest($"{root}|{targetTier}"))];
    }

    private IReadOnlyList<StorageMaintenanceItemResultDto> ExecuteCleanup(StorageMaintenancePreviewDto preview)
    {
        var results = new List<StorageMaintenanceItemResultDto>();
        foreach (var candidate in preview.Candidates)
        {
            try
            {
                var full = ResolveWithinRoot(candidate.RelativePath);
                var info = new FileInfo(full);
                if (!info.Exists || !IsTemporary(info) || info.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    results.Add(new(candidate.CandidateId, candidate.RelativePath, "Skipped", "The file is missing, changed, linked, or no longer eligible."));
                    continue;
                }
                if (!string.Equals(candidate.Fingerprint, Fingerprint(info), StringComparison.Ordinal))
                {
                    results.Add(new(candidate.CandidateId, candidate.RelativePath, "Skipped", "The file changed after preview."));
                    continue;
                }
                info.Delete();
                results.Add(new(candidate.CandidateId, candidate.RelativePath, "Completed", null));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                results.Add(new(candidate.CandidateId, candidate.RelativePath, "Failed", ex.Message));
            }
        }
        return results;
    }

    private async Task<IReadOnlyList<StorageMaintenanceItemResultDto>> ExecuteQualityCheckAsync(StorageMaintenancePreviewDto preview, CancellationToken ct)
    {
        var results = new List<StorageMaintenanceItemResultDto>();
        foreach (var candidate in preview.Candidates)
        {
            try
            {
                var full = ResolveWithinRoot(candidate.RelativePath);
                var info = new FileInfo(full);
                if (!info.Exists || !string.Equals(candidate.Fingerprint, Fingerprint(info), StringComparison.Ordinal))
                {
                    results.Add(new(candidate.CandidateId, candidate.RelativePath, "Skipped", "The file changed after preview."));
                    continue;
                }
                var score = await _quality!.ScoreAsync(full, ct).ConfigureAwait(false);
                results.Add(new(candidate.CandidateId, candidate.RelativePath, "Completed", $"Quality score {score.OverallScore:P1}."));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                results.Add(new(candidate.CandidateId, candidate.RelativePath, "Failed", ex.Message));
            }
        }
        return results;
    }

    private async Task<IReadOnlyList<StorageMaintenanceItemResultDto>> ExecuteTierMigrationAsync(StorageMaintenancePreviewDto preview, CancellationToken ct)
    {
        var candidate = preview.Candidates.Single();
        var target = Enum.Parse<StorageTier>(preview.TargetTier!, true);
        var result = await _tiers!.MigrateAsync(ResolveRoot(), target, new MigrationOptions(DeleteSource: false, VerifyChecksum: true), ct).ConfigureAwait(false);
        return [new(candidate.CandidateId, candidate.RelativePath, result.Success ? "Completed" : "Failed", result.Errors.Count == 0 ? null : string.Join("; ", result.Errors))];
    }

    private async Task<StorageQualitySummaryDto> BuildQualitySummaryAsync(string root, CancellationToken ct)
    {
        if (_quality is null)
            return new("Unavailable", 0, 0, 0, [], "Data quality service is not available.");
        var report = await _quality.GenerateReportAsync(new QualityReportOptions([root], IncludeRecommendations: true), ct).ConfigureAwait(false);
        return new("Available", report.FilesAnalyzed, report.AverageScore, report.LowQualityFiles.Count, report.Recommendations, null);
    }

    private async Task<IReadOnlyList<StorageQualityAlertDto>> BuildAlertsAsync(CancellationToken ct)
    {
        if (_quality is null)
            return [];
        var alerts = await _quality.GetQualityAlertsAsync(ct).ConfigureAwait(false);
        return alerts.Select(alert => new StorageQualityAlertDto(
            ComputeDigest($"{alert.Symbol}|{alert.Issue}")[..16],
            alert.CurrentScore < 0.5 ? "Critical" : "Warning",
            alert.Symbol,
            $"{alert.Issue}: {alert.CurrentScore:P1} (threshold {alert.Threshold:P1}). {alert.Recommendation}",
            DateTimeOffset.UtcNow)).ToArray();
    }

    private async Task<IReadOnlyList<StorageTierSummaryDto>> BuildTierRowsAsync(CancellationToken ct)
    {
        if (_tiers is null)
            return [];
        var stats = await _tiers.GetTierStatisticsAsync(ct).ConfigureAwait(false);
        return stats.TierInfo.OrderBy(static item => item.Key).Select(item => new StorageTierSummaryDto(item.Key.ToString(), item.Value.FileCount, item.Value.TotalBytes)).ToArray();
    }

    private async Task<EvidenceVaultIntakeResponseDto> RetainEvidenceAsync(
        StorageMaintenancePreviewDto preview,
        StorageMaintenanceResultDto result,
        string rationale,
        string actor,
        CancellationToken ct)
    {
        var receipt = JsonSerializer.Serialize(new { preview, result, rationale, actor });
        return await _evidence.WriteIntakeArtifactAsync(new EvidenceVaultIntakeRequestDto(
            "run",
            result.RunId,
            "WorkstationOperation",
            $"storage-maintenance-{result.RunId}.json",
            Convert.ToBase64String(Encoding.UTF8.GetBytes(receipt)),
            "application/json",
            "Meridian.StorageAssurance",
            preview.PreviewId,
            actor)
        {
            Classification = EvidenceDocumentClassificationDto.AuditRequestSupport,
            Actor = actor,
            Scope = "Data"
        }, ct).ConfigureAwait(false);
    }

    private string ResolveRoot() => Path.GetFullPath(_options.RootPath);

    private string ResolveWithinRoot(string relativePath)
    {
        var root = ResolveRoot();
        var full = Path.GetFullPath(Path.Combine(root, relativePath));
        var prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!full.Equals(root, StringComparison.OrdinalIgnoreCase) && !full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Path must resolve within the configured storage root.", nameof(relativePath));
        return full;
    }

    private static IEnumerable<FileInfo> EnumerateSafeFiles(string root)
    {
        if (!Directory.Exists(root))
            yield break;
        var pending = new Stack<DirectoryInfo>();
        pending.Push(new DirectoryInfo(root));
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            FileSystemInfo[] entries;
            try
            {
                entries = directory.GetFileSystemInfos();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }
            foreach (var entry in entries)
            {
                FileAttributes attributes;
                try
                {
                    attributes = entry.Attributes;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    continue;
                }
                if (attributes.HasFlag(FileAttributes.ReparsePoint))
                    continue;
                if (entry is DirectoryInfo child)
                    pending.Push(child);
                else if (entry is FileInfo file)
                    yield return file;
            }
        }
    }

    private static bool IsTemporary(FileInfo file) =>
        file.Extension.Equals(".tmp", StringComparison.OrdinalIgnoreCase) ||
        file.Extension.Equals(".partial", StringComparison.OrdinalIgnoreCase) ||
        file.Name.EndsWith(".tmp.jsonl", StringComparison.OrdinalIgnoreCase);

    private static StorageMaintenanceCandidateDto ToCandidate(string root, FileInfo file, string kind) => new(
        ComputeDigest(file.FullName)[..16],
        Path.GetRelativePath(root, file.FullName),
        kind,
        file.Length,
        new DateTimeOffset(file.LastWriteTimeUtc),
        Fingerprint(file));

    private static string Fingerprint(FileInfo file) => ComputeDigest($"{file.FullName}|{file.Length}|{file.LastWriteTimeUtc.Ticks}");
    private static string ComputeDigest(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static string? NormalizeOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool CanWrite(string root)
    {
        if (!Directory.Exists(root))
            return false;
        var probe = Path.Combine(root, $".assurance-probe-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(probe, "probe");
            File.Delete(probe);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
