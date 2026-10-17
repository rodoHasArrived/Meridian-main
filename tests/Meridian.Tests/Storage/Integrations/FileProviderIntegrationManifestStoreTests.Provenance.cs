using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Integrations;
using Meridian.Contracts.Integrity;
using Meridian.Storage.Integrations;

namespace Meridian.Tests.Storage.Integrations;

public sealed partial class FileProviderIntegrationManifestStoreTests
{
    [Fact]
    public async Task SaveRawPayloadAsync_RetainsOriginalAndRemediationReferencesAcrossRestart()
    {
        var store = new FileProviderIntegrationManifestStore(_testRoot);
        var original = CreateManifest();
        var updated = original with { ManifestVersion = 2, ChangeReason = "Map corrected provider field" };
        await store.SaveManifestAsync(original);
        await store.SaveManifestVersionAsync(updated);
        var payload = CreateBoundPayload(original) with
        {
            ManifestReference = ProviderIntegrationManifestIdentity.Create(updated),
            SourceSyncRunId = "original-run",
            ReplayMode = ProviderIntegrationReplayModeDto.Remediation
        };

        await store.SaveRawPayloadAsync(payload);
        var restarted = new FileProviderIntegrationManifestStore(_testRoot);
        await restarted.SaveRawPayloadAsync(payload);
        var retained = await restarted.GetRawPayloadAsync(payload.SyncRunId, payload.PayloadId);

        retained!.ManifestReference.Should().Be(payload.ManifestReference);
        retained.OriginalManifestReference.Should().Be(payload.OriginalManifestReference);
        retained.SourceSyncRunId.Should().Be("original-run");
        retained.ReplayMode.Should().Be(ProviderIntegrationReplayModeDto.Remediation);
    }

    [Fact]
    public async Task SaveRawPayloadAsync_RejectsChangedContentAndProvenanceWithoutReplacingEvidence()
    {
        var store = new FileProviderIntegrationManifestStore(_testRoot);
        var manifest = CreateManifest();
        await store.SaveManifestAsync(manifest);
        var payload = CreateBoundPayload(manifest);
        await store.SaveRawPayloadAsync(payload);

        var changeContent = () => new FileProviderIntegrationManifestStore(_testRoot)
            .SaveRawPayloadAsync(payload with { RawPayload = Json("""{"quantity":999}""") });
        var changeProvenance = () => new FileProviderIntegrationManifestStore(_testRoot)
            .SaveRawPayloadAsync(payload with { OriginalManifestReference = null });

        await changeContent.Should().ThrowAsync<InvalidOperationException>();
        await changeProvenance.Should().ThrowAsync<InvalidOperationException>();
        var retained = await store.GetRawPayloadAsync(payload.SyncRunId, payload.PayloadId);
        retained!.RawPayload.GetProperty("quantity").GetInt32().Should().Be(100);
        retained.OriginalManifestReference.Should().Be(payload.OriginalManifestReference);
    }

    [Fact]
    public async Task SaveRawPayloadAsync_RejectsMissingVersionAndWrongDigest()
    {
        var store = new FileProviderIntegrationManifestStore(_testRoot);
        var manifest = CreateManifest();
        var payload = CreateBoundPayload(manifest);
        var missing = () => store.SaveRawPayloadAsync(payload);
        await missing.Should().ThrowAsync<InvalidDataException>();

        await store.SaveManifestAsync(manifest);
        var forged = payload.ManifestReference! with { ContentDigest = new string('f', 64) };
        var wrongDigest = () => store.SaveRawPayloadAsync(payload with { ManifestReference = forged, OriginalManifestReference = forged });
        await wrongDigest.Should().ThrowAsync<InvalidDataException>();

        (await store.GetRawPayloadAsync(payload.SyncRunId, payload.PayloadId)).Should().BeNull();
    }

    [Fact]
    public async Task SaveLegacyEvidenceAsync_DoesNotInventOrAllowRetroactiveBinding()
    {
        var store = new FileProviderIntegrationManifestStore(_testRoot);
        var manifest = CreateManifest();
        var boundPayload = CreateBoundPayload(manifest);
        var boundRun = CreateBoundRun(manifest);
        var legacyPayload = boundPayload with { ManifestReference = null, OriginalManifestReference = null };
        var legacyRun = boundRun with { ManifestReference = null, OriginalManifestReference = null };
        await WriteLegacyEvidenceAsync(legacyPayload, legacyRun);
        await store.SaveRawPayloadAsync(legacyPayload);
        await store.SaveSyncRunAsync(legacyRun with { Status = ProviderIntegrationProcessingStatusDto.Quarantined });
        await store.SaveManifestAsync(manifest);
        (await store.TryCreateSyncRunAsync(boundRun)).Should().BeFalse();

        var bindPayload = () => store.SaveRawPayloadAsync(boundPayload);
        var bindRun = () => store.SaveSyncRunAsync(boundRun);
        await bindPayload.Should().ThrowAsync<InvalidOperationException>();
        await bindRun.Should().ThrowAsync<InvalidOperationException>();

        var restarted = new FileProviderIntegrationManifestStore(_testRoot);
        (await restarted.GetRawPayloadAsync(boundPayload.SyncRunId, boundPayload.PayloadId))!.ManifestReference.Should().BeNull();
        (await restarted.GetSyncRunAsync(boundRun.SyncRunId))!.ManifestReference.Should().BeNull();
        (await restarted.GetSyncRunAsync(boundRun.SyncRunId))!.Status.Should().Be(ProviderIntegrationProcessingStatusDto.Quarantined);
    }

    [Fact]
    public async Task SaveNewEvidenceAsync_RejectsUnboundPayloadAndRunWithoutInferringCurrentManifest()
    {
        var store = new FileProviderIntegrationManifestStore(_testRoot);
        var manifest = CreateManifest();
        await store.SaveManifestAsync(manifest);
        var payload = CreateBoundPayload(manifest) with { ManifestReference = null, OriginalManifestReference = null };
        var run = CreateBoundRun(manifest) with { ManifestReference = null, OriginalManifestReference = null };

        var savePayload = () => store.SaveRawPayloadAsync(payload);
        var saveRun = () => store.SaveSyncRunAsync(run);
        var claimRun = () => store.TryCreateSyncRunAsync(run);

        await savePayload.Should().ThrowAsync<InvalidDataException>();
        await saveRun.Should().ThrowAsync<InvalidDataException>();
        await claimRun.Should().ThrowAsync<InvalidDataException>();
        (await store.GetRawPayloadAsync(payload.SyncRunId, payload.PayloadId)).Should().BeNull();
        (await store.GetSyncRunAsync(run.SyncRunId)).Should().BeNull();
    }

    [Fact]
    public async Task SaveSyncRunAsync_AllowsProgressButRejectsRedirectingRetainedEvidence()
    {
        var store = new FileProviderIntegrationManifestStore(_testRoot);
        var manifest = CreateManifest();
        await store.SaveManifestAsync(manifest);
        var run = CreateBoundRun(manifest);
        await store.SaveSyncRunAsync(run);
        var completed = run with
        {
            Status = ProviderIntegrationProcessingStatusDto.Validated,
            RecordsAccepted = 1,
            CompletedAt = run.StartedAt.AddMinutes(1)
        };
        await store.SaveSyncRunAsync(completed);

        ProviderIntegrationSyncRunDto[] redirected =
        [
            completed with { ManifestReference = null, OriginalManifestReference = null },
            completed with { ConnectionId = "another-connection" },
            completed with { RawPayloadId = "another-payload" },
            completed with { SourceSyncRunId = "another-run", ReplayMode = ProviderIntegrationReplayModeDto.Original },
            completed with { ManifestId = "another-manifest" }
        ];
        foreach (var change in redirected)
        {
            var save = () => new FileProviderIntegrationManifestStore(_testRoot).SaveSyncRunAsync(change);
            await save.Should().ThrowAsync<InvalidOperationException>();
        }

        (await store.GetSyncRunAsync(run.SyncRunId)).Should().BeEquivalentTo(completed);
    }

    [Fact]
    public async Task ConcurrentSyncRunCreation_DifferentProvenanceCannotOverwriteWinner()
    {
        var manifest = CreateManifest();
        var store = new FileProviderIntegrationManifestStore(_testRoot);
        await store.SaveManifestAsync(manifest);
        var run = CreateBoundRun(manifest) with
        {
            SourceSyncRunId = "source-alpha",
            ReplayMode = ProviderIntegrationReplayModeDto.Original
        };
        var competing = run with { SourceSyncRunId = "source-beta" };

        async Task<bool> TrySaveAsync(ProviderIntegrationSyncRunDto candidate)
        {
            try
            {
                await new FileProviderIntegrationManifestStore(_testRoot).SaveSyncRunAsync(candidate);
                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        var results = await Task.WhenAll(TrySaveAsync(run), TrySaveAsync(competing));
        results.Should().ContainSingle(success => success);
        var retained = await new FileProviderIntegrationManifestStore(_testRoot).GetSyncRunAsync(run.SyncRunId);
        retained!.SourceSyncRunId.Should().Be(results[0] ? "source-alpha" : "source-beta");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TryCreateSyncRunAsync_OnlyOneConcurrentClaimCanWriteTheRun(bool sameProvenance)
    {
        var manifest = CreateManifest();
        var store = new FileProviderIntegrationManifestStore(_testRoot);
        await store.SaveManifestAsync(manifest);
        var first = CreateBoundRun(manifest) with
        {
            SourceSyncRunId = "source-alpha",
            ReplayMode = ProviderIntegrationReplayModeDto.Original
        };
        var second = sameProvenance ? first : first with { SourceSyncRunId = "source-beta", RawPayloadId = "other-payload" };
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var claims = new[] { first, second }.Select(async candidate =>
        {
            await gate.Task;
            var created = await new FileProviderIntegrationManifestStore(_testRoot).TryCreateSyncRunAsync(candidate);
            return (Candidate: candidate, Created: created);
        }).ToArray();

        gate.SetResult();
        var results = await Task.WhenAll(claims);

        var winner = results.Should().ContainSingle(result => result.Created).Which.Candidate;
        results.Should().ContainSingle(result => !result.Created);
        var restarted = new FileProviderIntegrationManifestStore(_testRoot);
        (await restarted.GetSyncRunAsync(first.SyncRunId)).Should().BeEquivalentTo(winner);
        (await restarted.TryCreateSyncRunAsync(winner)).Should().BeFalse();
        var completed = winner with { Status = ProviderIntegrationProcessingStatusDto.Validated, CompletedAt = winner.StartedAt.AddMinutes(1) };
        await restarted.SaveSyncRunAsync(completed);
        (await restarted.GetSyncRunAsync(first.SyncRunId)).Should().BeEquivalentTo(completed);
    }

    [Fact]
    public async Task TryCreateSyncRunAsync_DoesNotReplaceAnUnreadableExistingRun()
    {
        var manifest = CreateManifest();
        var run = CreateBoundRun(manifest);
        var path = Path.Combine(_testRoot, "_integrations", "sync-runs", $"{Sha256Digest.ComputeUtf8(run.SyncRunId)}.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, "null");

        (await new FileProviderIntegrationManifestStore(_testRoot).TryCreateSyncRunAsync(run)).Should().BeFalse();

        (await File.ReadAllTextAsync(path)).Should().Be("null");
    }

    private static RawIngestionPayloadDto CreateBoundPayload(ProviderIntegrationManifestDto manifest)
        => new("bound-payload", manifest.ProviderId, "connection-alpha", ProviderCapabilityKindDto.Positions,
            "positions", "bound-run", DateTimeOffset.Parse("2026-06-16T12:00:00Z"), new Dictionary<string, string>(),
            Json("""{"quantity":100}"""), manifest.ManifestId, ProviderIntegrationProcessingStatusDto.Received)
        {
            ManifestReference = ProviderIntegrationManifestIdentity.Create(manifest),
            OriginalManifestReference = ProviderIntegrationManifestIdentity.Create(manifest)
        };

    private static ProviderIntegrationSyncRunDto CreateBoundRun(ProviderIntegrationManifestDto manifest)
        => new("bound-run", manifest.ManifestId, "connection-alpha", manifest.ProviderId,
            ProviderCapabilityKindDto.Positions, "positions", DateTimeOffset.Parse("2026-06-16T12:00:00Z"), null,
            ProviderIntegrationProcessingStatusDto.Received, 1, 0, 0, "bound-payload", [])
        {
            ManifestReference = ProviderIntegrationManifestIdentity.Create(manifest),
            OriginalManifestReference = ProviderIntegrationManifestIdentity.Create(manifest)
        };

    private async Task WriteLegacyEvidenceAsync(RawIngestionPayloadDto payload, ProviderIntegrationSyncRunDto run)
    {
        var payloadPath = Path.Combine(_testRoot, "_integrations", "raw-payloads",
            Sha256Digest.ComputeUtf8(payload.SyncRunId), $"{Sha256Digest.ComputeUtf8(payload.PayloadId)}.json");
        var runPath = Path.Combine(_testRoot, "_integrations", "sync-runs", $"{Sha256Digest.ComputeUtf8(run.SyncRunId)}.json");
        Directory.CreateDirectory(Path.GetDirectoryName(payloadPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(runPath)!);
        await File.WriteAllTextAsync(payloadPath,
            JsonSerializer.Serialize(payload, ProviderIntegrationContractsJsonContext.Default.RawIngestionPayloadDto));
        await File.WriteAllTextAsync(runPath,
            JsonSerializer.Serialize(run, ProviderIntegrationContractsJsonContext.Default.ProviderIntegrationSyncRunDto));
    }
}
