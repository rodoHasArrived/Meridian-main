using FluentAssertions;
using Meridian.Application.Pipeline;
using Meridian.Contracts.Pipeline;
using Meridian.Contracts.Workstation;
using Meridian.Storage;
using Meridian.Ui.Shared.Evidence;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meridian.Tests.Ui;

public sealed class DataOperationsAssuranceServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"meridian-data-assurance-{Guid.NewGuid():N}");

    public DataOperationsAssuranceServiceTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task IngestionRetry_TransitionsDurableJobAndRetainsEvidence()
    {
        using var jobs = new IngestionJobService(Path.Combine(_root, "jobs"));
        var evidence = new FileEvidenceArtifactStore(_root, NullLogger<FileEvidenceArtifactStore>.Instance);
        var service = new IngestionOperationsService(jobs, evidence);
        var job = await jobs.CreateJobAsync(IngestionWorkloadType.Historical, ["AAPL"], "alpaca");
        await jobs.TransitionAsync(job.JobId, IngestionJobState.Queued);
        await jobs.TransitionAsync(job.JobId, IngestionJobState.Running);
        await jobs.UpdateCheckpointAsync(job.JobId, new IngestionCheckpointToken { LastSymbol = "AAPL", CapturedAt = DateTime.UtcNow });
        await jobs.TransitionAsync(job.JobId, IngestionJobState.Failed, "Provider timeout");

        var result = await service.ApplyActionAsync(
            job.JobId,
            "retry",
            new IngestionOperationActionRequestDto("retry-1", "Operator reviewed provider recovery."),
            "operator@example.com",
            CancellationToken.None);

        result.Should().NotBeNull();
        result!.PreviousState.Should().Be("Failed");
        result.CurrentState.Should().Be("Queued");
        result.EvidenceVaultId.Should().NotBeNullOrWhiteSpace();
        result.EvidenceRoute.Should().Contain("subjectKind=run");
        jobs.GetJob(job.JobId)!.RetryEnvelope.AttemptCount.Should().Be(1);
    }

    [Fact]
    public async Task Cleanup_DeletesOnlyPreviewedUnchangedTemporaryFileAndRetainsEvidence()
    {
        var storageRoot = Path.Combine(_root, "storage");
        Directory.CreateDirectory(storageRoot);
        var temporary = Path.Combine(storageRoot, "stale.partial");
        var retained = Path.Combine(storageRoot, "prices.jsonl");
        await File.WriteAllTextAsync(temporary, "temporary");
        await File.WriteAllTextAsync(retained, "retained");
        var evidence = new FileEvidenceArtifactStore(_root, NullLogger<FileEvidenceArtifactStore>.Instance);
        var service = new StorageAssuranceService(new StorageOptions { RootPath = storageRoot }, evidence);

        var preview = await service.PreviewAsync(
            new StorageMaintenancePreviewRequestDto(StorageMaintenanceActionDto.Cleanup),
            CancellationToken.None);
        var result = await service.ExecuteAsync(
            new StorageMaintenanceCommandRequestDto(preview.PreviewId, "cleanup-1", "Reviewed temporary-file cleanup.", preview.ConfirmationText),
            "operator@example.com",
            CancellationToken.None);

        preview.Candidates.Should().ContainSingle(candidate => candidate.RelativePath == "stale.partial");
        File.Exists(temporary).Should().BeFalse();
        File.Exists(retained).Should().BeTrue();
        result.Status.Should().Be("Completed");
        result.EvidenceVaultId.Should().NotBeNullOrWhiteSpace();
        service.GetExecuteAction(preview.PreviewId, "cleanup-1").Should().Be(StorageMaintenanceActionDto.Cleanup);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
