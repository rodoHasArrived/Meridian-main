using System.Text.Json;
using Meridian.Contracts.Etl;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Operations;
using Meridian.Storage.Archival;
using Microsoft.Extensions.Logging;

namespace Meridian.DataIntegration.Etl;

public sealed partial class EtlJobOrchestrator
{
    private const string EtlCaseType = "etl-run";
    private const string EtlSystemActor = "system:etl-orchestrator";

    private static bool IsDeliveryConfigured(EtlJobDefinition definition) =>
        definition.PublishPortablePackage ||
        definition.PublishNormalizedExtract ||
        definition.Destination.Kind != EtlDestinationKind.StorageCatalog;

    private static async Task<IReadOnlyList<OperationArtifactReference>> BuildVerifiedArtifactReferencesAsync(
        EtlJobDefinition definition,
        EtlExportResult exportResult,
        bool requireArtifacts,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(exportResult);

        if (definition.PublishPortablePackage && exportResult.Success)
        {
            if (exportResult.PackageResult is not { Success: true } packageResult)
            {
                throw new EtlArtifactVerificationException(
                    "The configured portable-package delivery reported success without a successful package result.");
            }

            if (string.IsNullOrWhiteSpace(packageResult.PackagePath))
            {
                throw new EtlArtifactVerificationException(
                    "The configured portable-package delivery reported success without a package path.");
            }
        }

        if (exportResult.Success && exportResult.PackageResult is { Success: false } unsuccessfulPackage)
        {
            throw new EtlArtifactVerificationException(
                $"ETL export reported success while its package result failed: " +
                $"{unsuccessfulPackage.Error ?? "no package error was provided"}.");
        }

        var requestedPaths = new List<string>();
        requestedPaths.AddRange(exportResult.ArtifactPaths ?? []);
        if (!string.IsNullOrWhiteSpace(exportResult.PackageResult?.PackagePath))
            requestedPaths.Add(exportResult.PackageResult.PackagePath);
        requestedPaths.AddRange(exportResult.PackageResult?.AdditionalParts ?? []);

        if (requireArtifacts && requestedPaths.Count == 0)
        {
            throw new EtlArtifactVerificationException(
                "Configured ETL delivery reported success without declaring any retained artifact paths.");
        }

        var pathComparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        var files = new HashSet<string>(pathComparer);
        foreach (var requestedPath in requestedPaths)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(requestedPath))
            {
                throw new EtlArtifactVerificationException(
                    "Configured ETL delivery declared an empty artifact path.");
            }

            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(requestedPath);
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                throw new EtlArtifactVerificationException(
                    $"Configured ETL delivery declared invalid artifact path '{requestedPath}'.",
                    exception);
            }

            if (File.Exists(fullPath))
            {
                files.Add(fullPath);
                continue;
            }

            if (!Directory.Exists(fullPath))
            {
                throw new EtlArtifactVerificationException(
                    $"Configured ETL delivery declared missing artifact path '{fullPath}'.");
            }

            string[] directoryFiles;
            try
            {
                directoryFiles = Directory
                    .EnumerateFiles(fullPath, "*", SearchOption.AllDirectories)
                    .Select(Path.GetFullPath)
                    .Order(pathComparer)
                    .ToArray();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                throw new EtlArtifactVerificationException(
                    $"Configured ETL artifact directory '{fullPath}' could not be read.",
                    exception);
            }

            if (directoryFiles.Length == 0)
            {
                throw new EtlArtifactVerificationException(
                    $"Configured ETL artifact directory '{fullPath}' is empty.");
            }

            foreach (var file in directoryFiles)
                files.Add(file);
        }

        if (requireArtifacts && files.Count == 0)
        {
            throw new EtlArtifactVerificationException(
                "Configured ETL delivery produced no readable retained artifact files.");
        }

        var artifacts = new List<OperationArtifactReference>(files.Count);
        var index = 0;
        foreach (var file in files.Order(pathComparer))
        {
            ct.ThrowIfCancellationRequested();
            var verified = await VerifyArtifactReadbackAsync(file, ct).ConfigureAwait(false);
            ValidatePackageReadback(exportResult, file, verified);
            var pathIdentity = ComputeTextHash(file)[..12];
            artifacts.Add(new OperationArtifactReference(
                $"etl-artifact-{++index}-{pathIdentity}",
                Path.GetFileName(file),
                GetContentType(Path.GetExtension(file)),
                verified.ByteLength,
                verified.ContentHashSha256,
                Uri: new Uri(file).AbsoluteUri));
        }

        return artifacts;
    }

    private static async Task<VerifiedArtifactReadback> VerifyArtifactReadbackAsync(
        string path,
        CancellationToken ct)
    {
        try
        {
            var firstLength = new FileInfo(path).Length;
            if (firstLength <= 0)
            {
                throw new EtlArtifactVerificationException(
                    $"Configured ETL artifact '{path}' is empty.");
            }

            var firstHash = await HashFileAsync(path, ct).ConfigureAwait(false);
            var secondLength = new FileInfo(path).Length;
            var readbackHash = await HashFileAsync(path, ct).ConfigureAwait(false);
            var finalLength = new FileInfo(path).Length;
            if (firstLength != secondLength || secondLength != finalLength ||
                !string.Equals(firstHash, readbackHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new EtlArtifactVerificationException(
                    $"Configured ETL artifact '{path}' changed during retained-byte readback verification.");
            }

            return new VerifiedArtifactReadback(finalLength, readbackHash);
        }
        catch (EtlArtifactVerificationException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new EtlArtifactVerificationException(
                $"Configured ETL artifact '{path}' could not be read back.",
                exception);
        }
    }

    private static async Task<string> HashFileAsync(string path, CancellationToken ct)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await Sha256Digest.ComputeAsync(stream, ct).ConfigureAwait(false);
    }

    private static void ValidatePackageReadback(
        EtlExportResult exportResult,
        string path,
        VerifiedArtifactReadback readback)
    {
        var package = exportResult.PackageResult;
        if (package is null || string.IsNullOrWhiteSpace(package.PackagePath) ||
            !string.Equals(
                Path.GetFullPath(package.PackagePath),
                path,
                OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(package.PackageChecksum))
        {
            if (package.PackageChecksum.Length != 64 || !package.PackageChecksum.All(Uri.IsHexDigit) ||
                !string.Equals(
                    package.PackageChecksum,
                    readback.ContentHashSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new EtlArtifactVerificationException(
                    $"Configured ETL package '{path}' failed SHA-256 readback verification.");
            }
        }

        if (package.PackageSizeBytes > 0 && package.PackageSizeBytes != readback.ByteLength)
        {
            throw new EtlArtifactVerificationException(
                $"Configured ETL package '{path}' read back {readback.ByteLength} bytes, " +
                $"but the package result declared {package.PackageSizeBytes} bytes.");
        }
    }

    private async Task<VerifiedOperationOutcome> RetainTerminalOutcomeAsync(
        string jobId,
        VerifiedOperationOutcome outcome,
        bool includeCaseHistory,
        CancellationToken ct)
    {
        var receiptPath = GetTerminalOutcomePath(jobId, outcome.OperationId);
        var prepared = AddTerminalReceiptEvidence(outcome, receiptPath);
        var canonicalBytes = JsonSerializer.SerializeToUtf8Bytes(
            prepared,
            OperationsContractsJsonContext.Default.VerifiedOperationOutcome);
        var outcomeHash = Sha256Digest.Compute(canonicalBytes);

        try
        {
            var writtenHash = await AtomicFileWriter
                .WriteWithChecksumAsync(receiptPath, canonicalBytes, ct)
                .ConfigureAwait(false);
            var retainedBytes = await File.ReadAllBytesAsync(receiptPath, ct).ConfigureAwait(false);
            var readbackHash = Sha256Digest.Compute(retainedBytes);
            if (!canonicalBytes.AsSpan().SequenceEqual(retainedBytes) ||
                !string.Equals(writtenHash, outcomeHash, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(readbackHash, outcomeHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "The retained ETL terminal receipt bytes did not match the exact returned outcome.");
            }

            var retainedOutcome = JsonSerializer.Deserialize(
                retainedBytes,
                OperationsContractsJsonContext.Default.VerifiedOperationOutcome)
                ?? throw new InvalidDataException("The retained ETL terminal receipt was empty.");
            VerifiedOperationOutcomeValidator.ValidateAndThrow(retainedOutcome);
            var retainedCanonicalBytes = JsonSerializer.SerializeToUtf8Bytes(
                retainedOutcome,
                OperationsContractsJsonContext.Default.VerifiedOperationOutcome);
            if (!canonicalBytes.AsSpan().SequenceEqual(retainedCanonicalBytes))
            {
                throw new InvalidDataException(
                    "The retained ETL terminal receipt could not be read back canonically.");
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new EtlTerminalOutcomePersistenceException(
                $"ETL terminal outcome '{outcome.OperationId}' could not be retained and read back exactly.",
                caseHistoryFailed: false,
                exception);
        }

        if (includeCaseHistory && _caseHistoryStore is not null)
        {
            try
            {
                var retainedRecord = await _caseHistoryStore.AppendAsync(new OperationalCaseHistoryAppendRequest
                {
                    CaseId = jobId,
                    CaseType = EtlCaseType,
                    HistoryEventId = prepared.OperationId,
                    EventType = $"etl.terminal.{prepared.State.ToString().ToLowerInvariant()}",
                    OccurredAtUtc = prepared.CompletedAtUtc,
                    ActorId = EtlSystemActor,
                    Reason = $"ETL run retained terminal state {prepared.State}.",
                    CorrelationId = prepared.CorrelationId!,
                    InputHashSha256 = prepared.InputHashSha256!,
                    Data = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["terminalOutcomeHashSha256"] = outcomeHash,
                        ["terminalOutcomeUri"] = new Uri(receiptPath).AbsoluteUri,
                        ["terminalState"] = prepared.State.ToString()
                    },
                    Artifacts = prepared.Artifacts,
                    Evidence = prepared.Evidence,
                    TerminalOutcome = prepared
                }, ct).ConfigureAwait(false);

                if (!OperationalCaseHistoryHashing.HasValidRecordHash(retainedRecord))
                {
                    throw new InvalidDataException(
                        "The ETL terminal case-history record failed its retained record-hash check.");
                }

                var replay = await _caseHistoryStore.ReadAsync(new OperationalCaseHistoryQuery
                {
                    CaseId = jobId,
                    CaseType = EtlCaseType
                }, ct).ConfigureAwait(false);
                var replayedRecord = replay.SingleOrDefault(record =>
                    string.Equals(record.HistoryEventId, prepared.OperationId, StringComparison.Ordinal));
                if (replayedRecord?.TerminalOutcome is null ||
                    !OperationalCaseHistoryHashing.HasValidRecordHash(replayedRecord))
                {
                    throw new InvalidDataException(
                        "The ETL terminal case-history record was not readable after append.");
                }

                var replayedBytes = JsonSerializer.SerializeToUtf8Bytes(
                    replayedRecord.TerminalOutcome,
                    OperationsContractsJsonContext.Default.VerifiedOperationOutcome);
                var replayedHash = Sha256Digest.Compute(replayedBytes);
                if (!canonicalBytes.AsSpan().SequenceEqual(replayedBytes) ||
                    !string.Equals(replayedHash, outcomeHash, StringComparison.OrdinalIgnoreCase) ||
                    !replayedRecord.Data.TryGetValue("terminalOutcomeHashSha256", out var retainedHash) ||
                    !string.Equals(retainedHash, outcomeHash, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        "The ETL terminal case-history readback did not match the exact returned outcome hash.");
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                throw new EtlTerminalOutcomePersistenceException(
                    $"ETL terminal outcome '{outcome.OperationId}' could not be retained and read back from shared case history.",
                    caseHistoryFailed: true,
                    exception);
            }
        }

        return prepared;
    }

    private async Task<VerifiedOperationOutcome> RetainFailureOutcomeAsync(
        string jobId,
        VerifiedOperationOutcome outcome,
        bool includeCaseHistory,
        List<string> errors)
    {
        try
        {
            return await RetainTerminalOutcomeAsync(
                    jobId,
                    outcome,
                    includeCaseHistory,
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            var message = $"Terminal-outcome persistence failed: {exception.Message}";
            errors.Add(message);
            _logger.LogError(exception, "ETL job {JobId} terminal-outcome persistence failed", jobId);
            var evidenceId = outcome.Evidence[0].EvidenceId;
            var augmented = Validate(outcome with
            {
                Issues = outcome.Issues.Concat([new OperationIssue(
                    $"etl-terminal-outcome-persistence-{outcome.Issues.Count + 1}",
                    message,
                    OperationIssueSeverity.Warning,
                    exception.GetType().FullName,
                    evidenceId)]).ToArray()
            });

            try
            {
                return await RetainTerminalOutcomeAsync(
                        jobId,
                        augmented,
                        includeCaseHistory: false,
                        CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception fallbackException)
            {
                var fallbackMessage =
                    $"Fallback terminal-receipt persistence failed: {fallbackException.Message}";
                errors.Add(fallbackMessage);
                _logger.LogError(
                    fallbackException,
                    "ETL job {JobId} fallback terminal-receipt persistence failed",
                    jobId);
                return Validate(augmented with
                {
                    Issues = augmented.Issues.Concat([new OperationIssue(
                        $"etl-terminal-outcome-persistence-{augmented.Issues.Count + 1}",
                        fallbackMessage,
                        OperationIssueSeverity.Warning,
                        fallbackException.GetType().FullName,
                        evidenceId)]).ToArray()
                });
            }
        }
    }

    private VerifiedOperationOutcome AddTerminalReceiptEvidence(
        VerifiedOperationOutcome outcome,
        string receiptPath)
    {
        var evidenceId = $"{outcome.OperationId}:terminal-receipt";
        if (outcome.Evidence.Any(item =>
                string.Equals(item.EvidenceId, evidenceId, StringComparison.Ordinal)))
        {
            return outcome;
        }

        var receiptEvidence = new OperationEvidenceReference(
            evidenceId,
            "etl-terminal-outcome",
            "Exact terminal VerifiedOperationOutcome bytes with an adjacent SHA-256 checksum sidecar.",
            Uri: new Uri(receiptPath).AbsoluteUri,
            CapturedAtUtc: outcome.CompletedAtUtc);
        var postconditions = outcome.Postconditions
            .Select((postcondition, index) => index == 0
                ? postcondition with
                {
                    EvidenceIds = postcondition.EvidenceIds
                        .Concat([evidenceId])
                        .Distinct(StringComparer.Ordinal)
                        .ToArray()
                }
                : postcondition)
            .ToArray();
        return Validate(outcome with
        {
            Postconditions = postconditions,
            Evidence = outcome.Evidence.Concat([receiptEvidence]).ToArray()
        });
    }

    private string GetTerminalOutcomePath(string jobId, string operationId) =>
        _auditStore.GetAuditPath(
            jobId,
            Path.Combine("outcomes", $"{ComputeTextHash(operationId)}.json"));

    private static string GetContentType(string extension) => extension.ToLowerInvariant() switch
    {
        ".zip" => "application/zip",
        ".gz" => "application/gzip",
        ".csv" => "text/csv",
        ".json" or ".jsonl" => "application/json",
        ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        ".pdf" => "application/pdf",
        _ => "application/octet-stream"
    };

    private sealed record VerifiedArtifactReadback(long ByteLength, string ContentHashSha256);

    private sealed class EtlArtifactVerificationException : InvalidOperationException
    {
        public EtlArtifactVerificationException(string message)
            : base(message)
        {
        }

        public EtlArtifactVerificationException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }

    private sealed class EtlTerminalOutcomePersistenceException : IOException
    {
        public EtlTerminalOutcomePersistenceException(
            string message,
            bool caseHistoryFailed,
            Exception innerException)
            : base(message, innerException)
        {
            CaseHistoryFailed = caseHistoryFailed;
        }

        public bool CaseHistoryFailed { get; }
    }
}
