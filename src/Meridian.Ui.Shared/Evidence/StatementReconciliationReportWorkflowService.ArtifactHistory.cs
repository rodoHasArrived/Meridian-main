using System.Text.Json;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Workstation;

namespace Meridian.Ui.Shared.Evidence;

public sealed partial class StatementReconciliationReportWorkflowService
{
    private async Task VerifyRetainedArtifactAuthorityAsync(
        string workflowDirectory,
        WorkflowSnapshot snapshot,
        CancellationToken ct)
    {
        await VerifyArtifactHistoryAsync(workflowDirectory, snapshot, ct).ConfigureAwait(false);
        var artifacts = snapshot.Workflow.RetainedArtifacts ?? [];
        if (snapshot.Workflow.Status == StatementReconciliationReportWorkflowStatusDto.Completed
            && artifacts.Count == 0)
        {
            throw new InvalidDataException(
                $"Completed statement reconciliation report workflow '{snapshot.Workflow.WorkflowId}' has no current artifact authority.");
        }

        if (artifacts.Count > 0)
        {
            await VerifyCurrentArtifactGenerationAsync(workflowDirectory, snapshot.Workflow, ct)
                .ConfigureAwait(false);
        }
    }

    private static async Task VerifyArtifactHistoryAsync(
        string directory,
        WorkflowSnapshot snapshot,
        CancellationToken ct)
    {
        var workflow = snapshot.Workflow;
        var history = workflow.ArtifactHistory ?? [];
        if (history.Any(static item => item.Generation <= 0)
            || history.Select(static item => item.Generation).Distinct().Count() != history.Count)
        {
            throw new InvalidDataException(
                $"Statement reconciliation report workflow '{workflow.WorkflowId}' contains invalid or duplicate artifact history generations.");
        }

        var orderedHistory = history
            .OrderBy(static item => item.Generation)
            .ToArray();
        for (var index = 0; index < orderedHistory.Length; index++)
        {
            if (orderedHistory[index].Generation != index + 1)
            {
                throw new InvalidDataException(
                    $"Statement reconciliation report workflow '{workflow.WorkflowId}' artifact history is not contiguous from generation 1.");
            }
        }

        var artifacts = workflow.RetainedArtifacts ?? [];
        var expectedHistoryCount = artifacts.Count > 0
            ? workflow.ArtifactGeneration - 1
            : workflow.ArtifactGeneration;
        if (workflow.ArtifactGeneration < 0
            || expectedHistoryCount < 0
            || orderedHistory.Length != expectedHistoryCount)
        {
            throw new InvalidDataException(
                $"Statement reconciliation report workflow '{workflow.WorkflowId}' artifact generation state conflicts with its immutable history.");
        }

        var archivedDirectories = ReadArchivedArtifactGenerationDirectories(directory);
        var recordedGenerations = orderedHistory
            .Select(static item => item.Generation)
            .ToHashSet();
        var permittedStagedGeneration = artifacts.Count > 0
            ? workflow.ArtifactGeneration
            : 0;
        var unexpectedGeneration = archivedDirectories.FirstOrDefault(generation =>
            !recordedGenerations.Contains(generation)
            && generation != permittedStagedGeneration);
        if (unexpectedGeneration > 0)
        {
            throw new InvalidDataException(
                $"Statement reconciliation report workflow '{workflow.WorkflowId}' omits retained artifact history generation {unexpectedGeneration}.");
        }

        foreach (var generation in orderedHistory)
        {
            var archiveDirectory = GetArtifactGenerationArchiveDirectory(
                directory,
                generation.Generation);
            var receiptPath = Path.Combine(
                archiveDirectory,
                ArtifactArchiveReceiptFileName);
            if (!File.Exists(receiptPath))
            {
                throw new InvalidDataException(
                    $"Statement reconciliation report artifact generation {generation.Generation} archive receipt is missing.");
            }

            var receiptBytes = await File.ReadAllBytesAsync(receiptPath, ct).ConfigureAwait(false);
            var receiptHash = Sha256Digest.Compute(receiptBytes);
            if (!string.Equals(
                    receiptHash,
                    generation.ArchiveReceiptContentHashSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"Statement reconciliation report artifact generation {generation.Generation} archive receipt failed hash verification.");
            }

            var receipt = JsonSerializer.Deserialize<ArtifactGenerationArchiveReceipt>(
                    receiptBytes,
                    JsonOptions)
                ?? throw new InvalidDataException(
                    $"Statement reconciliation report artifact generation {generation.Generation} archive receipt is empty.");
            if (receipt.SchemaVersion != ArtifactArchiveReceiptSchemaVersion
                || receipt.Generation != generation.Generation
                || !ArtifactDescriptorsMatch(receipt.Artifacts, generation.Artifacts)
                || receipt.EvidenceReferences is null
                || !string.Equals(
                    receipt.ManifestFileName,
                    generation.ManifestFileName,
                    StringComparison.Ordinal)
                || receipt.ManifestByteLength != generation.ManifestByteLength
                || !string.Equals(
                    receipt.ManifestContentHashSha256,
                    generation.ManifestContentHashSha256,
                    StringComparison.OrdinalIgnoreCase)
                || receipt.GeneratedAtUtc != generation.GeneratedAtUtc
                || receipt.ArchivedAtUtc != generation.ArchivedAtUtc)
            {
                throw new InvalidDataException(
                    $"Statement reconciliation report artifact generation {generation.Generation} metadata conflicts with its immutable archive receipt.");
            }

            var expectedEvidence = receipt.EvidenceReferences
                .Concat(BuildArtifactGenerationEvidenceReferences(
                    generation.Generation,
                    receipt.Artifacts,
                    receipt.ManifestContentHashSha256))
                .Append(
                    $"artifact-generation:{generation.Generation}:archive-receipt:sha256:{receiptHash}")
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static item => item, StringComparer.Ordinal)
                .ToArray();
            if (generation.EvidenceReferences is null
                || !generation.EvidenceReferences.SequenceEqual(
                    expectedEvidence,
                    StringComparer.Ordinal))
            {
                throw new InvalidDataException(
                    $"Statement reconciliation report artifact generation {generation.Generation} audit evidence conflicts with its immutable archive receipt.");
            }

            foreach (var descriptor in generation.Artifacts)
            {
                var fileName = ValidateArchiveFileName(descriptor.FileName);
                var artifactPath = Path.Combine(archiveDirectory, fileName);
                if (!File.Exists(artifactPath))
                {
                    throw new InvalidDataException(
                        $"Statement reconciliation report artifact generation {generation.Generation} file '{fileName}' is missing.");
                }

                var content = await File.ReadAllBytesAsync(artifactPath, ct).ConfigureAwait(false);
                ValidateArtifactContent(descriptor, content);
            }

            var manifestFileName = ValidateArchiveFileName(generation.ManifestFileName);
            var manifestPath = Path.Combine(archiveDirectory, manifestFileName);
            if (!File.Exists(manifestPath))
            {
                throw new InvalidDataException(
                    $"Statement reconciliation report artifact generation {generation.Generation} manifest is missing.");
            }

            var manifest = await File.ReadAllBytesAsync(manifestPath, ct).ConfigureAwait(false);
            var actualManifestHash = Sha256Digest.Compute(manifest);
            if (manifest.LongLength != generation.ManifestByteLength
                || !string.Equals(
                    actualManifestHash,
                    generation.ManifestContentHashSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"Statement reconciliation report artifact generation {generation.Generation} manifest failed hash verification.");
            }
        }
    }

    private static IReadOnlyList<int> ReadArchivedArtifactGenerationDirectories(
        string workflowDirectory)
    {
        var historyDirectory = Path.Combine(
            workflowDirectory,
            "artifacts",
            "history");
        if (!Directory.Exists(historyDirectory))
            return [];

        var generations = new List<int>();
        foreach (var path in Directory.EnumerateDirectories(historyDirectory))
        {
            var name = Path.GetFileName(path);
            const string prefix = "generation-";
            if (!name.StartsWith(prefix, StringComparison.Ordinal)
                || !int.TryParse(
                    name.AsSpan(prefix.Length),
                    System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var generation)
                || generation <= 0
                || !string.Equals(
                    name,
                    $"generation-{generation:D6}",
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Statement reconciliation report artifact history contains an invalid generation directory '{name}'.");
            }

            generations.Add(generation);
        }

        if (generations.Count != generations.Distinct().Count())
        {
            throw new InvalidDataException(
                "Statement reconciliation report artifact history contains duplicate generation directories.");
        }

        return generations.Order().ToArray();
    }

    private async Task VerifyCurrentArtifactGenerationAsync(
        string workflowDirectory,
        StatementReconciliationReportWorkflowDto workflow,
        CancellationToken ct)
    {
        var artifacts = workflow.RetainedArtifacts ?? [];
        if (artifacts.Count == 0)
            return;
        if (workflow.ArtifactGeneration <= 0)
        {
            throw new InvalidDataException(
                $"Statement reconciliation report workflow '{workflow.WorkflowId}' advertises current artifacts without a valid generation.");
        }

        foreach (var descriptor in artifacts)
        {
            var retainedFileName = ValidateArchiveFileName(descriptor.FileName);
            var artifactPath = ResolveArtifactPath(workflow.WorkflowId, descriptor.ArtifactId);
            if (!string.Equals(
                    Path.GetFileName(artifactPath),
                    retainedFileName,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"Statement reconciliation report artifact '{descriptor.ArtifactId}' file name no longer matches its retained descriptor.");
            }

            if (!File.Exists(artifactPath))
            {
                throw new InvalidDataException(
                    $"Statement reconciliation report current artifact '{descriptor.ArtifactId}' is missing.");
            }

            var content = await File.ReadAllBytesAsync(artifactPath, ct).ConfigureAwait(false);
            ValidateArtifactContent(descriptor, content);
        }

        var manifestPath = Path.Combine(
            workflowDirectory,
            "artifacts",
            ArtifactManifestFileName);
        if (!File.Exists(manifestPath))
        {
            throw new InvalidDataException(
                $"Statement reconciliation report workflow '{workflow.WorkflowId}' current artifact manifest is missing.");
        }

        var manifestBytes = await File.ReadAllBytesAsync(manifestPath, ct).ConfigureAwait(false);
        var manifestHash = Sha256Digest.Compute(manifestBytes);
        ValidateCurrentArtifactManifest(manifestBytes, manifestHash, workflow);
    }

    private static string ValidateArchiveFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)
            || !string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Statement reconciliation report artifact history contains an invalid file name.");
        }

        return fileName;
    }

    private static string GetArtifactGenerationArchiveDirectory(
        string workflowDirectory,
        int generation)
    {
        if (generation <= 0)
            throw new InvalidDataException("Statement reconciliation report artifact generation must be positive.");
        return Path.Combine(
            workflowDirectory,
            "artifacts",
            "history",
            $"generation-{generation:D6}");
    }
}
