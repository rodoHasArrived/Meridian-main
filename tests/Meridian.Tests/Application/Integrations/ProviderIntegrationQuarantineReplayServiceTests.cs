using System.Text.Json;
using FluentAssertions;
using Meridian.Application.Integrations;
using Meridian.Contracts.Integrations;
using Meridian.Storage.Integrations;
using Moq;

namespace Meridian.Tests.Application.Integrations;

public sealed class ProviderIntegrationQuarantineReplayServiceTests : IDisposable
{
    private readonly string testRoot;

    public ProviderIntegrationQuarantineReplayServiceTests()
    {
        testRoot = Path.Combine(Path.GetTempPath(), $"mdc_provider_quarantine_replay_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(testRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(testRoot))
        {
            Directory.Delete(testRoot, recursive: true);
        }
    }

    [Fact]
    public async Task ReplayAsync_RemediatesWithSelectedNewerMappingAndRetainsBothProvenances()
    {
        var store = new FileProviderIntegrationManifestStore(testRoot);
        var original = CreateReplayReadyManifest(includeSecurityMapping: false);
        var selected = CreateReplayReadyManifest() with { ManifestVersion = 2 };
        await SeedQuarantineAsync(store, original);
        await PublishAsync(store, original, selected);
        await PublishAsync(store, selected, selected with { ManifestVersion = 3, FieldMappings = original.FieldMappings });
        var service = new ProviderIntegrationQuarantineReplayService(store);

        var result = await service.ReplayAsync(CreateRequest() with
        {
            Mode = ProviderIntegrationReplayModeDto.Remediation,
            TargetManifestVersion = selected.ManifestVersion,
            TargetManifestDigest = ProviderIntegrationManifestIdentity.Create(selected).ContentDigest.ToUpperInvariant()
        });

        result.ReplaySyncRunId.Should().Be("sync-run-replay-1");
        result.RecordsReplayed.Should().Be(1);
        result.RecordsAccepted.Should().Be(1);
        result.RecordsRequarantined.Should().Be(0);
        result.Status.Should().Be(ProviderIntegrationProcessingStatusDto.Validated);
        result.Issues.Should().BeEmpty();
        var payload = (await store.GetRawPayloadAsync(result.ReplaySyncRunId, result.RawPayloadId))!;
        var run = (await store.GetSyncRunAsync(result.ReplaySyncRunId))!;
        run.RecordsAccepted.Should().Be(1);
        run.ManifestReference.Should().Be(ProviderIntegrationManifestIdentity.Create(selected));
        run.OriginalManifestReference.Should().Be(ProviderIntegrationManifestIdentity.Create(original));
        run.ReplayMode.Should().Be(ProviderIntegrationReplayModeDto.Remediation);
        run.SourceSyncRunId.Should().Be("sync-run-quarantine-1");
        payload.ManifestReference.Should().Be(run.ManifestReference);
        payload.OriginalManifestReference.Should().Be(run.OriginalManifestReference);
        payload.ReplayMode.Should().Be(run.ReplayMode);
        payload.SourceSyncRunId.Should().Be(run.SourceSyncRunId);
        var staged = await store.ListStagingRecordsAsync(result.ReplaySyncRunId);
        staged.Should().ContainSingle();
        staged[0].SourceRecordId.Should().Be("POS-1");
        staged[0].MappedRecord.GetProperty("security").GetProperty("cusip").GetString().Should().Be("9128285M8");
        (await store.ListQuarantinedRecordsAsync(result.ReplaySyncRunId)).Should().BeEmpty();
    }

    [Fact]
    public async Task ReplayAsync_UsesOriginalMappingAfterUpdateAndRestart()
    {
        var store = new FileProviderIntegrationManifestStore(testRoot);
        var original = CreateReplayReadyManifest(includeSecurityMapping: false);
        await SeedQuarantineAsync(store, original);
        await PublishAsync(store, original, CreateReplayReadyManifest() with { ManifestVersion = 2 });

        var restartedStore = new FileProviderIntegrationManifestStore(testRoot);
        var result = await new ProviderIntegrationQuarantineReplayService(restartedStore).ReplayAsync(CreateRequest());

        result.RecordsAccepted.Should().Be(0);
        result.RecordsRequarantined.Should().Be(1);
        result.Issues.Should().Contain(issue => issue.Code == "required.missing" && issue.TargetField == "security.cusip");
        var run = (await restartedStore.GetSyncRunAsync(result.ReplaySyncRunId))!;
        var payload = (await restartedStore.GetRawPayloadAsync(result.ReplaySyncRunId, result.RawPayloadId))!;
        var originalReference = ProviderIntegrationManifestIdentity.Create(original);
        run.ManifestReference.Should().Be(originalReference);
        run.OriginalManifestReference.Should().Be(originalReference);
        run.ReplayMode.Should().Be(ProviderIntegrationReplayModeDto.Original);
        payload.ManifestReference.Should().Be(originalReference);
        payload.OriginalManifestReference.Should().Be(originalReference);
        payload.MappingVersion.Should().Be($"{original.ManifestId}:v1");
        (await restartedStore.GetManifestAsync(original.ManifestId))!.ManifestVersion.Should().Be(2);
    }

    [Fact]
    public async Task ReplayAsync_OriginalReplayOfRemediationPreservesFirstIngestionMapping()
    {
        var store = new FileProviderIntegrationManifestStore(testRoot);
        var original = CreateReplayReadyManifest(includeSecurityMapping: false);
        var remediation = original with { ManifestVersion = 2, ChangeReason = "Investigate missing security" };
        await SeedQuarantineAsync(store, original);
        await PublishAsync(store, original, remediation);
        var service = new ProviderIntegrationQuarantineReplayService(store);
        var remediated = await service.ReplayAsync(CreateRequest() with
        {
            Mode = ProviderIntegrationReplayModeDto.Remediation,
            TargetManifestVersion = remediation.ManifestVersion,
            TargetManifestDigest = ProviderIntegrationManifestIdentity.Create(remediation).ContentDigest
        });
        var records = await store.ListQuarantinedRecordsAsync(remediated.ReplaySyncRunId);

        var replay = await service.ReplayAsync(CreateRequest() with
        {
            ReplaySyncRunId = "sync-run-replay-2",
            SourceSyncRunId = remediated.ReplaySyncRunId,
            QuarantineRecordIds = records.Select(record => record.QuarantineRecordId).ToArray()
        });

        var run = (await store.GetSyncRunAsync(replay.ReplaySyncRunId))!;
        run.ManifestReference.Should().Be(ProviderIntegrationManifestIdentity.Create(original));
        run.OriginalManifestReference.Should().Be(run.ManifestReference);
        run.SourceSyncRunId.Should().Be(remediated.ReplaySyncRunId);
        (await store.GetSyncRunAsync(remediated.ReplaySyncRunId))!.ManifestReference
            .Should().Be(ProviderIntegrationManifestIdentity.Create(remediation));
    }

    [Theory]
    [InlineData(IntegrationTypeDto.ManualUpload)]
    [InlineData(IntegrationTypeDto.Hybrid)]
    [InlineData(IntegrationTypeDto.SftpFile)]
    public async Task ReplayAsync_ManualCsvPreservesOriginalMappingAndExplicitRemediationAfterRestart(
        IntegrationTypeDto integrationType)
    {
        var store = new FileProviderIntegrationManifestStore(testRoot);
        var original = CreateReplayReadyManifest(includeSecurityMapping: false) with
        {
            IntegrationType = integrationType,
            FieldMappings =
            [
                Mapping("$.account_id", "providerAccountId"),
                Mapping("$['trade.quantity']", "quantity", new TransformRuleDto("signedAmount", new Dictionary<string, string>
                {
                    ["conditionSourcePath"] = "$.position.side",
                    ["negativeValues"] = "Short"
                })),
                Mapping("$.as_of_date", "asOf", new TransformRuleDto("date", new Dictionary<string, string>())),
                Mapping("$.position_id", "sourceRecordId"),
                Mapping("$.missing_cusip", "security.cusip", new TransformRuleDto("uppercase", new Dictionary<string, string>()))
            ]
        };
        await store.SaveManifestAsync(original);
        await store.SaveConnectionAsync(CreateConnection());
        var ingestion = await new ProviderIntegrationDryRunService(store).RunManualCsvDryRunAsync(
            new ManualCsvProviderIntegrationDryRunRequestDto(
                "csv-source-run", original.ManifestId, "connection-alpha", ProviderCapabilityKindDto.Positions,
                "positions.csv",
                """
                ACCOUNT_ID,TRADE.QUANTITY,POSITION.SIDE,AS_OF_DATE,POSITION_ID,CUSIP
                A-100,100,Short,2026-06-16,POS-1,9128285m8
                """,
                "operator@example.com", DateTimeOffset.Parse("2026-06-16T12:00:00Z")));
        ingestion.RecordsQuarantined.Should().Be(1);
        var sourceRecord = (await store.ListQuarantinedRecordsAsync(ingestion.SyncRunId)).Should().ContainSingle().Which;
        sourceRecord.MappedRecord!.Value.GetProperty("quantity").GetDecimal().Should().Be(-100m);
        sourceRecord.RawRecord.GetProperty("fields").GetProperty("TRADE.QUANTITY").GetString().Should().Be("100");
        var sourceRun = (await store.GetSyncRunAsync(ingestion.SyncRunId))!;
        var sourcePayload = (await store.GetRawPayloadAsync(ingestion.SyncRunId, ingestion.RawPayloadId))!;

        var selected = original with
        {
            ManifestVersion = 2,
            // Source format belongs to retained ingestion evidence, even when remediation changes integration type.
            IntegrationType = IntegrationTypeDto.Rest,
            FieldMappings = original.FieldMappings.Select(mapping => mapping.TargetField == "security.cusip"
                ? mapping with { SourcePath = "$.cusip" }
                : mapping).ToArray()
        };
        await PublishAsync(store, original, selected);
        await PublishAsync(store, selected, original with { ManifestVersion = 3 });
        var restarted = new FileProviderIntegrationManifestStore(testRoot);
        var service = new ProviderIntegrationQuarantineReplayService(restarted);
        var request = CreateRequest() with
        {
            SourceSyncRunId = ingestion.SyncRunId,
            QuarantineRecordIds = [sourceRecord.QuarantineRecordId]
        };

        var replay = await service.ReplayAsync(request);

        replay.RecordsAccepted.Should().Be(0);
        replay.RecordsRequarantined.Should().Be(1);
        var replayedRecord = (await restarted.ListQuarantinedRecordsAsync(replay.ReplaySyncRunId)).Should().ContainSingle().Which;
        JsonElement.DeepEquals(replayedRecord.MappedRecord!.Value, sourceRecord.MappedRecord!.Value).Should().BeTrue();
        replayedRecord.ValidationErrors.Select(issue => (issue.Code, issue.TargetField))
            .Should().BeEquivalentTo(sourceRecord.ValidationErrors.Select(issue => (issue.Code, issue.TargetField)));
        JsonElement.DeepEquals(replayedRecord.RawRecord, sourceRecord.RawRecord).Should().BeTrue();
        var originalReference = ProviderIntegrationManifestIdentity.Create(original);
        var replayRun = (await restarted.GetSyncRunAsync(replay.ReplaySyncRunId))!;
        var replayPayload = (await restarted.GetRawPayloadAsync(replay.ReplaySyncRunId, replay.RawPayloadId))!;
        replayRun.ManifestReference.Should().Be(originalReference);
        replayRun.OriginalManifestReference.Should().Be(originalReference);
        replayRun.SourceSyncRunId.Should().Be(ingestion.SyncRunId);
        replayRun.ReplayMode.Should().Be(ProviderIntegrationReplayModeDto.Original);
        replayPayload.ManifestReference.Should().Be(originalReference);
        replayPayload.OriginalManifestReference.Should().Be(originalReference);

        // Remediate the replayed CSV row to prove its retained wrapper and source format survive a replay chain.
        var remediation = await service.ReplayAsync(request with
        {
            ReplaySyncRunId = "csv-remediation-run",
            SourceSyncRunId = replay.ReplaySyncRunId,
            QuarantineRecordIds = [replayedRecord.QuarantineRecordId],
            Mode = ProviderIntegrationReplayModeDto.Remediation,
            TargetManifestVersion = selected.ManifestVersion,
            TargetManifestDigest = ProviderIntegrationManifestIdentity.Create(selected).ContentDigest
        });

        remediation.RecordsAccepted.Should().Be(1);
        remediation.RecordsRequarantined.Should().Be(0);
        var staged = (await restarted.ListStagingRecordsAsync(remediation.ReplaySyncRunId)).Should().ContainSingle().Which;
        staged.SourceRecordId.Should().Be("POS-1");
        staged.MappedRecord.GetProperty("providerAccountId").GetString().Should().Be("A-100");
        staged.MappedRecord.GetProperty("quantity").GetDecimal().Should().Be(-100m);
        staged.MappedRecord.GetProperty("security").GetProperty("cusip").GetString().Should().Be("9128285M8");
        var remediationRun = (await restarted.GetSyncRunAsync(remediation.ReplaySyncRunId))!;
        var remediationPayload = (await restarted.GetRawPayloadAsync(remediation.ReplaySyncRunId, remediation.RawPayloadId))!;
        remediationRun.ManifestReference.Should().Be(ProviderIntegrationManifestIdentity.Create(selected));
        remediationRun.OriginalManifestReference.Should().Be(originalReference);
        remediationRun.SourceSyncRunId.Should().Be(replay.ReplaySyncRunId);
        remediationRun.ReplayMode.Should().Be(ProviderIntegrationReplayModeDto.Remediation);
        remediationPayload.ManifestReference.Should().Be(remediationRun.ManifestReference);
        remediationPayload.OriginalManifestReference.Should().Be(originalReference);
        remediationPayload.SourceSyncRunId.Should().Be(replay.ReplaySyncRunId);
        remediationPayload.ReplayMode.Should().Be(ProviderIntegrationReplayModeDto.Remediation);
        (await restarted.GetManifestAsync(original.ManifestId))!.ManifestVersion.Should().Be(3);
        (await restarted.GetSyncRunAsync(ingestion.SyncRunId)).Should().BeEquivalentTo(sourceRun);
        var retainedSourcePayload = (await restarted.GetRawPayloadAsync(ingestion.SyncRunId, ingestion.RawPayloadId))!;
        retainedSourcePayload.Should().BeEquivalentTo(sourcePayload, options => options.Excluding(payload => payload.RawPayload));
        JsonElement.DeepEquals(retainedSourcePayload.RawPayload, sourcePayload.RawPayload).Should().BeTrue();
    }

    [Fact]
    public async Task ReplayAsync_RestWithCsvEndpointAndEnvelopeKeepsJsonPathSemantics()
    {
        var store = new FileProviderIntegrationManifestStore(testRoot);
        var manifest = CreateReplayReadyManifest() with
        {
            IntegrationType = IntegrationTypeDto.Hybrid,
            Endpoints =
            [
                new EndpointDefinitionDto(
                    "manual-csv-upload", ProviderCapabilityKindDto.Positions, ProviderIntegrationHttpMethodDto.Get,
                    "/positions", new Dictionary<string, string>(), new Dictionary<string, string>(), null, null,
                    new EndpointPaginationDto(ProviderIntegrationPaginationTypeDto.None, null, null, null, null),
                    new EndpointResponseShapeDto("$.records", null, []))
            ],
            FieldMappings = CreateReplayReadyManifest().FieldMappings.Select(mapping => mapping with
            {
                SourcePath = mapping.SourcePath.Replace("$.", "$.fields.", StringComparison.Ordinal)
            }).ToArray()
        };
        await store.SaveManifestAsync(manifest);
        await store.SaveConnectionAsync(CreateConnection());
        var transport = new Mock<IProviderIntegrationHttpTransport>();
        transport.Setup(candidate => candidate.SendAsync(It.IsAny<ProviderIntegrationHttpRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProviderIntegrationHttpResponse(200, new Dictionary<string, string>(),
                """
                {"fileName":"positions.csv","contentType":"text/csv","records":[
                    {"rowNumber":2,"fields":{"account_id":"A-100","quantity":"invalid","as_of_date":"2026-06-16","position_id":"POS-1"}}
                ]}
                """));
        var ingestion = await new ProviderIntegrationRestDryRunService(store, transport.Object).RunRestDryRunAsync(
            new ProviderIntegrationRestDryRunRequestDto(
                "rest-source-run", manifest.ManifestId, "connection-alpha", ProviderCapabilityKindDto.Positions,
                "manual-csv-upload", new Dictionary<string, string>(), new Dictionary<string, string>(),
                "operator@example.com", DateTimeOffset.Parse("2026-06-16T12:00:00Z"), MaxPages: 1));
        var sourceRecord = (await store.ListQuarantinedRecordsAsync(ingestion.SyncRunId)).Should().ContainSingle().Which;

        var result = await new ProviderIntegrationQuarantineReplayService(new FileProviderIntegrationManifestStore(testRoot))
            .ReplayAsync(CreateRequest() with
            {
                SourceSyncRunId = ingestion.SyncRunId,
                QuarantineRecordIds = [sourceRecord.QuarantineRecordId]
            });

        var replayedRecord = (await store.ListQuarantinedRecordsAsync(result.ReplaySyncRunId)).Should().ContainSingle().Which;
        JsonElement.DeepEquals(replayedRecord.MappedRecord!.Value, sourceRecord.MappedRecord!.Value).Should().BeTrue();
        replayedRecord.ValidationErrors.Select(issue => (issue.Code, issue.TargetField))
            .Should().BeEquivalentTo(sourceRecord.ValidationErrors.Select(issue => (issue.Code, issue.TargetField)));
    }

    [Theory]
    [InlineData("retained")]
    [InlineData("missing")]
    [InlineData("cycle")]
    [InlineData("scope")]
    [InlineData("unsupported-format")]
    [InlineData("malformed-csv-row")]
    [InlineData("malformed-csv-envelope")]
    public async Task ReplayAsync_UnmarkedReplayRequiresVerifiedCsvSourceHistory(string history)
    {
        var store = new FileProviderIntegrationManifestStore(testRoot);
        var manifest = CreateReplayReadyManifest(includeSecurityMapping: false) with { IntegrationType = IntegrationTypeDto.ManualUpload };
        await store.SaveManifestAsync(manifest);
        await store.SaveConnectionAsync(CreateConnection());
        var ingestion = await new ProviderIntegrationDryRunService(store).RunManualCsvDryRunAsync(
            new ManualCsvProviderIntegrationDryRunRequestDto(
                "csv-source-run", manifest.ManifestId, "connection-alpha", ProviderCapabilityKindDto.Positions,
                "positions.csv", "account_id,quantity,as_of_date,position_id\nA-100,100,2026-06-16,POS-1",
                "operator@example.com", DateTimeOffset.Parse("2026-06-16T12:00:00Z")));
        var sourceRecord = (await store.ListQuarantinedRecordsAsync(ingestion.SyncRunId)).Should().ContainSingle().Which;
        var sourceRun = (await store.GetSyncRunAsync(ingestion.SyncRunId))!;
        var sourcePayload = (await store.GetRawPayloadAsync(ingestion.SyncRunId, ingestion.RawPayloadId))!;

        async Task SaveOldReplayAsync(string runId, string parentId, string connectionId = "connection-alpha")
        {
            var run = sourceRun with
            {
                SyncRunId = runId,
                ConnectionId = connectionId,
                RawPayloadId = $"payload-{runId}",
                SourceSyncRunId = parentId,
                ReplayMode = ProviderIntegrationReplayModeDto.Original
            };
            var metadata = new Dictionary<string, string> { ["sourceSyncRunId"] = parentId };
            if (history == "unsupported-format")
            {
                metadata["sourceRecordFormat"] = "unsupported-v99";
            }

            await store.SaveSyncRunAsync(run);
            await store.SaveRawPayloadAsync(sourcePayload with
            {
                PayloadId = run.RawPayloadId,
                SyncRunId = runId,
                ConnectionId = connectionId,
                SourceSyncRunId = parentId,
                ReplayMode = ProviderIntegrationReplayModeDto.Original,
                RequestMetadata = metadata,
                RawPayload = JsonSerializer.SerializeToElement(new
                {
                    sourceSyncRunId = parentId,
                    records = new[] { new { quarantineRecordId = sourceRecord.QuarantineRecordId, rawRecord = sourceRecord.RawRecord } }
                })
            });
        }

        var parentId = history switch
        {
            "missing" => "missing-source-run",
            "cycle" or "scope" => "old-replay-parent",
            _ => ingestion.SyncRunId
        };
        await SaveOldReplayAsync("old-replay", parentId);
        if (history is "cycle" or "scope")
        {
            await SaveOldReplayAsync("old-replay-parent", history == "cycle" ? "old-replay" : ingestion.SyncRunId,
                history == "scope" ? "other-connection" : "connection-alpha");
        }

        var oldRecord = sourceRecord with
        {
            SyncRunId = "old-replay",
            QuarantineRecordId = "old-quarantine",
            RawRecord = history == "malformed-csv-row" ? Json("""{"rowNumber":2,"fields":null}""") : sourceRecord.RawRecord
        };
        await store.SaveQuarantinedRecordAsync(oldRecord);
        var request = CreateRequest() with { SourceSyncRunId = oldRecord.SyncRunId, QuarantineRecordIds = [oldRecord.QuarantineRecordId] };
        var restarted = new FileProviderIntegrationManifestStore(testRoot);
        IProviderIntegrationManifestStore replayStore = restarted;
        if (history == "malformed-csv-envelope")
        {
            replayStore = ForwardReplayStore(restarted, restarted.GetSyncRunAsync);
            Mock.Get(replayStore)
                .Setup(candidate => candidate.GetRawPayloadAsync(ingestion.SyncRunId, ingestion.RawPayloadId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(sourcePayload with { RawPayload = Json("{}") });
        }

        var service = new ProviderIntegrationQuarantineReplayService(replayStore);

        if (history != "retained")
        {
            var replay = () => service.ReplayAsync(request);
            await replay.Should().ThrowAsync<InvalidOperationException>();
            (await store.GetSyncRunAsync(request.ReplaySyncRunId)).Should().BeNull();
            (await store.ListQuarantinedRecordsAsync(request.ReplaySyncRunId)).Should().BeEmpty();
            (await store.ListStagingRecordsAsync(request.ReplaySyncRunId)).Should().BeEmpty();
            return;
        }

        var result = await service.ReplayAsync(request);
        var replayedRecord = (await store.ListQuarantinedRecordsAsync(result.ReplaySyncRunId)).Should().ContainSingle().Which;
        JsonElement.DeepEquals(replayedRecord.MappedRecord!.Value, sourceRecord.MappedRecord!.Value).Should().BeTrue();
        var payload = (await store.GetRawPayloadAsync(result.ReplaySyncRunId, result.RawPayloadId))!;
        payload.RequestMetadata["sourceRecordFormat"].Should().Be("manual-csv-v1");
        payload.ManifestReference.Should().Be(sourceRun.ManifestReference);
        payload.OriginalManifestReference.Should().Be(sourceRun.OriginalManifestReference);
        payload.SourceSyncRunId.Should().Be("old-replay");
    }

    [Theory]
    [InlineData(ProviderIntegrationReplayModeDto.Original, 2, "digest")]
    [InlineData(ProviderIntegrationReplayModeDto.Remediation, null, null)]
    [InlineData(ProviderIntegrationReplayModeDto.Remediation, 1, "digest")]
    [InlineData(ProviderIntegrationReplayModeDto.Remediation, 2, null)]
    [InlineData((ProviderIntegrationReplayModeDto)99, null, null)]
    public async Task ReplayAsync_RejectsImplicitOrNonNewerRemediation(
        ProviderIntegrationReplayModeDto mode, int? targetVersion, string? targetDigest)
    {
        var store = new FileProviderIntegrationManifestStore(testRoot);
        await SeedQuarantineAsync(store, CreateReplayReadyManifest());
        var service = new ProviderIntegrationQuarantineReplayService(store);

        var act = () => service.ReplayAsync(CreateRequest() with
        {
            Mode = mode,
            TargetManifestVersion = targetVersion,
            TargetManifestDigest = targetDigest
        });

        await act.Should().ThrowAsync<ArgumentException>();
        (await store.GetSyncRunAsync("sync-run-replay-1")).Should().BeNull();
        (await store.ListStagingRecordsAsync("sync-run-replay-1")).Should().BeEmpty();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ReplayAsync_RejectsUnavailableOrMismatchedRemediationSnapshot(bool snapshotExists)
    {
        var store = new FileProviderIntegrationManifestStore(testRoot);
        var original = CreateReplayReadyManifest();
        await SeedQuarantineAsync(store, original);
        if (snapshotExists)
        {
            await PublishAsync(store, original, original with { ManifestVersion = 2 });
        }

        var act = () => new ProviderIntegrationQuarantineReplayService(store).ReplayAsync(CreateRequest() with
        {
            Mode = ProviderIntegrationReplayModeDto.Remediation,
            TargetManifestVersion = 2,
            TargetManifestDigest = new string('0', 64)
        });

        await act.Should().ThrowAsync<InvalidOperationException>();
        (await store.GetSyncRunAsync("sync-run-replay-1")).Should().BeNull();
        (await store.ListStagingRecordsAsync("sync-run-replay-1")).Should().BeEmpty();
    }

    [Theory]
    [InlineData("legacy-unbound-evidence")]
    [InlineData("missing-run-reference")]
    [InlineData("missing-original-reference")]
    [InlineData("missing-payload")]
    [InlineData("mismatched-payload-reference")]
    [InlineData("tampered-both-digests")]
    [InlineData("missing-original-snapshot")]
    [InlineData("different-payload-scope")]
    [InlineData("mismatched-mapping-version")]
    public async Task ReplayAsync_RejectsMissingOrInconsistentSourceProvenanceBeforeWrites(string corruption)
    {
        var store = new FileProviderIntegrationManifestStore(testRoot);
        var manifest = CreateReplayReadyManifest();
        await SeedQuarantineAsync(store, manifest);
        var sourceRun = (await store.GetSyncRunAsync("sync-run-quarantine-1"))!;
        var sourcePayload = (await store.GetRawPayloadAsync(sourceRun.SyncRunId, sourceRun.RawPayloadId!))!;
        var falseReference = sourceRun.ManifestReference! with { ContentDigest = new string('0', 64) };
        switch (corruption)
        {
            case "legacy-unbound-evidence":
                sourceRun = sourceRun with { ManifestReference = null, OriginalManifestReference = null };
                sourcePayload = sourcePayload with { ManifestReference = null, OriginalManifestReference = null };
                break;
            case "missing-run-reference":
                sourceRun = sourceRun with { ManifestReference = null };
                break;
            case "missing-original-reference":
                sourceRun = sourceRun with { OriginalManifestReference = null };
                break;
            case "mismatched-payload-reference":
                sourcePayload = sourcePayload with { ManifestReference = falseReference };
                break;
            case "tampered-both-digests":
                sourceRun = sourceRun with { ManifestReference = falseReference, OriginalManifestReference = falseReference };
                sourcePayload = sourcePayload with { ManifestReference = falseReference, OriginalManifestReference = falseReference };
                break;
            case "different-payload-scope":
                sourcePayload = sourcePayload with { ConnectionId = "another-connection" };
                break;
            case "mismatched-mapping-version":
                sourcePayload = sourcePayload with { MappingVersion = "another-manifest:v1" };
                break;
        }

        var damaged = new Mock<IProviderIntegrationManifestStore>(MockBehavior.Strict);
        damaged.Setup(candidate => candidate.GetSyncRunAsync("sync-run-replay-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProviderIntegrationSyncRunDto?)null);
        damaged.Setup(candidate => candidate.GetSyncRunAsync(sourceRun.SyncRunId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(sourceRun);
        damaged.Setup(candidate => candidate.GetConnectionAsync("connection-alpha", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateConnection());
        damaged.Setup(candidate => candidate.GetRawPayloadAsync(sourceRun.SyncRunId, "payload-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(corruption == "missing-payload" ? null : sourcePayload);
        damaged.Setup(candidate => candidate.GetManifestVersionAsync(manifest.ManifestId, manifest.ManifestVersion, It.IsAny<CancellationToken>()))
            .ReturnsAsync(corruption == "missing-original-snapshot" ? null : manifest);

        var act = () => new ProviderIntegrationQuarantineReplayService(damaged.Object).ReplayAsync(CreateRequest());

        await act.Should().ThrowAsync<InvalidOperationException>();
        damaged.Verify(candidate => candidate.SaveRawPayloadAsync(It.IsAny<RawIngestionPayloadDto>(), It.IsAny<CancellationToken>()), Times.Never);
        damaged.Verify(candidate => candidate.SaveSyncRunAsync(It.IsAny<ProviderIntegrationSyncRunDto>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ReplayAsync_RejectsSourceRunIdReuseWithoutChangingSourceEvidence()
    {
        var store = new FileProviderIntegrationManifestStore(testRoot);
        await SeedQuarantineAsync(store, CreateReplayReadyManifest());
        var before = await store.GetSyncRunAsync("sync-run-quarantine-1");

        var act = () => new ProviderIntegrationQuarantineReplayService(store).ReplayAsync(CreateRequest() with
        {
            ReplaySyncRunId = "sync-run-quarantine-1"
        });

        await act.Should().ThrowAsync<ArgumentException>();
        (await store.GetSyncRunAsync("sync-run-quarantine-1")).Should().BeEquivalentTo(before);
    }

    [Fact]
    public async Task ReplayAsync_ConcurrentDifferentSourcesClaimOneRunBeforePublishingAnyRows()
    {
        var store = new FileProviderIntegrationManifestStore(testRoot);
        var manifest = CreateReplayReadyManifest();
        await SeedQuarantineAsync(store, manifest);
        var firstRun = (await store.GetSyncRunAsync("sync-run-quarantine-1"))!;
        var firstPayload = (await store.GetRawPayloadAsync(firstRun.SyncRunId, firstRun.RawPayloadId!))!;
        var firstRecord = (await store.ListQuarantinedRecordsAsync(firstRun.SyncRunId)).Single();
        var secondRun = firstRun with { SyncRunId = "sync-run-quarantine-2", RawPayloadId = "payload-2" };
        await store.SaveRawPayloadAsync(firstPayload with
        {
            SyncRunId = secondRun.SyncRunId,
            PayloadId = secondRun.RawPayloadId!,
            RawPayload = Json(firstPayload.RawPayload.GetRawText().Replace("POS-1", "POS-2", StringComparison.Ordinal))
        });
        await store.SaveSyncRunAsync(secondRun);
        await store.SaveQuarantinedRecordAsync(firstRecord with
        {
            SyncRunId = secondRun.SyncRunId,
            QuarantineRecordId = "quarantine-other",
            RawRecord = Json(firstRecord.RawRecord.GetRawText().Replace("POS-1", "POS-2", StringComparison.Ordinal))
        });

        var firstRequest = CreateRequest();
        var secondRequest = firstRequest with
        {
            SourceSyncRunId = secondRun.SyncRunId,
            QuarantineRecordIds = ["quarantine-other"]
        };
        var bothReadUnusedRun = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var arrivals = 0;
        async Task<ProviderIntegrationSyncRunDto?> ReadRunAsync(IProviderIntegrationManifestStore underlying, string id, CancellationToken ct)
        {
            var run = await underlying.GetSyncRunAsync(id, ct);
            if (id == firstRequest.ReplaySyncRunId)
            {
                run.Should().BeNull();
                if (Interlocked.Increment(ref arrivals) == 2)
                {
                    bothReadUnusedRun.SetResult();
                }

                await bothReadUnusedRun.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
            }

            return run;
        }

        var firstService = new ProviderIntegrationQuarantineReplayService(
            ForwardReplayStore(store, (id, ct) => ReadRunAsync(store, id, ct)));
        var secondStore = new FileProviderIntegrationManifestStore(testRoot);
        var secondService = new ProviderIntegrationQuarantineReplayService(
            ForwardReplayStore(secondStore, (id, ct) => ReadRunAsync(secondStore, id, ct)));
        async Task<(string SourceId, Exception? Error)> AttemptAsync(
            ProviderIntegrationQuarantineReplayService service, ProviderIntegrationQuarantineReplayRequestDto request)
        {
            try
            {
                await service.ReplayAsync(request);
                return (request.SourceSyncRunId, null);
            }
            catch (InvalidOperationException error)
            {
                return (request.SourceSyncRunId, error);
            }
        }

        var attempts = await Task.WhenAll(AttemptAsync(firstService, firstRequest), AttemptAsync(secondService, secondRequest));

        var winner = attempts.Where(attempt => attempt.Error is null).Should().ContainSingle().Which;
        attempts.Where(attempt => attempt.Error is not null).Should().ContainSingle()
            .Which.Error.Should().BeOfType<InvalidOperationException>().Which.Message.Should().Contain("unused sync run id");
        var retained = (await store.GetSyncRunAsync(firstRequest.ReplaySyncRunId))!;
        retained.SourceSyncRunId.Should().Be(winner.SourceId);
        retained.Status.Should().Be(ProviderIntegrationProcessingStatusDto.Validated);
        retained.ManifestReference.Should().Be(ProviderIntegrationManifestIdentity.Create(manifest));
        retained.OriginalManifestReference.Should().Be(retained.ManifestReference);
        var staged = (await store.ListStagingRecordsAsync(firstRequest.ReplaySyncRunId)).Should().ContainSingle().Which;
        staged.SourceRecordId.Should().Be(winner.SourceId == firstRun.SyncRunId ? "POS-1" : "POS-2");
        staged.RawPayloadId.Should().Be(retained.RawPayloadId);
        (await store.ListQuarantinedRecordsAsync(firstRequest.ReplaySyncRunId)).Should().BeEmpty();
        var replayPayloads = Directory.EnumerateFiles(Path.Combine(testRoot, "_integrations", "raw-payloads"), "*.json", SearchOption.AllDirectories)
            .Select(path => JsonSerializer.Deserialize(File.ReadAllText(path), ProviderIntegrationContractsJsonContext.Default.RawIngestionPayloadDto)!)
            .Where(payload => payload.SyncRunId == firstRequest.ReplaySyncRunId);
        var payload = replayPayloads.Should().ContainSingle().Which;
        payload.SourceSyncRunId.Should().Be(winner.SourceId);
        payload.PayloadId.Should().Be(retained.RawPayloadId);
        payload.ManifestReference.Should().Be(retained.ManifestReference);
        payload.RawPayload.GetProperty("sourceSyncRunId").GetString().Should().Be(winner.SourceId);
    }

    [Fact]
    public async Task ReplayAsync_RequarantinesRecordsWhenMappingStillFails()
    {
        var store = new FileProviderIntegrationManifestStore(testRoot);
        await SeedQuarantineAsync(store, CreateReplayReadyManifest(includeSecurityMapping: false));
        var service = new ProviderIntegrationQuarantineReplayService(store);

        var result = await service.ReplayAsync(CreateRequest());

        result.Status.Should().Be(ProviderIntegrationProcessingStatusDto.Quarantined);
        result.RecordsAccepted.Should().Be(0);
        result.RecordsRequarantined.Should().Be(1);
        result.Issues.Should().Contain(issue =>
            issue.Code == "required.missing" &&
            issue.TargetField == "security.cusip");
        (await store.ListStagingRecordsAsync(result.ReplaySyncRunId)).Should().BeEmpty();
        (await store.ListQuarantinedRecordsAsync(result.ReplaySyncRunId)).Should().ContainSingle()
            .Which.ValidationErrors.Should().Contain(error => error.TargetField == "security.cusip");
    }

    [Fact]
    public async Task ReplayAsync_RequarantinesDuplicateReplayedProviderIdentity()
    {
        var store = new FileProviderIntegrationManifestStore(testRoot);
        await SeedQuarantineAsync(store, CreateReplayReadyManifest(), includeDuplicateRecord: true);
        var service = new ProviderIntegrationQuarantineReplayService(store);

        var result = await service.ReplayAsync(new ProviderIntegrationQuarantineReplayRequestDto(
            "sync-run-replay-1",
            "sync-run-quarantine-1",
            "manifest-custodian-abc-v1",
            "connection-alpha",
            ProviderCapabilityKindDto.Positions,
            ["quarantine-1", "quarantine-2"],
            "operator@example.com",
            DateTimeOffset.Parse("2026-06-16T12:30:00Z")));

        result.RecordsReplayed.Should().Be(2);
        result.RecordsAccepted.Should().Be(1);
        result.RecordsRequarantined.Should().Be(1);
        result.Status.Should().Be(ProviderIntegrationProcessingStatusDto.Quarantined);
        result.Issues.Should().Contain(issue => issue.Code == "duplicate.dedupe-key");
        (await store.ListStagingRecordsAsync(result.ReplaySyncRunId)).Should().ContainSingle()
            .Which.SourceRecordId.Should().Be("POS-1");
        (await store.ListQuarantinedRecordsAsync(result.ReplaySyncRunId)).Should().ContainSingle()
            .Which.ValidationErrors.Should().Contain(error => error.Code == "duplicate.dedupe-key");
    }

    [Fact]
    public async Task ReplayAsync_RequarantinesMoneyAmountWithoutCurrency()
    {
        var store = new FileProviderIntegrationManifestStore(testRoot);
        await SeedQuarantineAsync(store, CreateReplayReadyManifest(includeMarketValueWithoutCurrency: true));
        var service = new ProviderIntegrationQuarantineReplayService(store);

        var result = await service.ReplayAsync(CreateRequest());

        result.RecordsAccepted.Should().Be(0);
        result.RecordsRequarantined.Should().Be(1);
        result.Status.Should().Be(ProviderIntegrationProcessingStatusDto.Quarantined);
        result.Issues.Should().Contain(issue =>
            issue.Code == "money.currency.missing" &&
            issue.TargetField == "marketValue.currency");
        (await store.ListStagingRecordsAsync(result.ReplaySyncRunId)).Should().BeEmpty();
        (await store.ListQuarantinedRecordsAsync(result.ReplaySyncRunId)).Should().ContainSingle()
            .Which.ValidationErrors.Should().Contain(error => error.Code == "money.currency.missing");
    }

    [Fact]
    public async Task ReplayAsync_ObservesCancellationBeforeEvidence()
    {
        var store = new FileProviderIntegrationManifestStore(testRoot);
        await SeedQuarantineAsync(store, CreateReplayReadyManifest());
        var service = new ProviderIntegrationQuarantineReplayService(store);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => service.ReplayAsync(CreateRequest(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        (await store.GetSyncRunAsync("sync-run-replay-1")).Should().BeNull();
        (await store.ListStagingRecordsAsync("sync-run-replay-1")).Should().BeEmpty();
        (await store.ListQuarantinedRecordsAsync("sync-run-replay-1")).Should().BeEmpty();
    }

    private static ProviderIntegrationQuarantineReplayRequestDto CreateRequest()
        => new(
            "sync-run-replay-1",
            "sync-run-quarantine-1",
            "manifest-custodian-abc-v1",
            "connection-alpha",
            ProviderCapabilityKindDto.Positions,
            ["quarantine-1"],
            "operator@example.com",
            DateTimeOffset.Parse("2026-06-16T12:30:00Z"));

    private static IProviderIntegrationManifestStore ForwardReplayStore(
        IProviderIntegrationManifestStore underlying,
        Func<string, CancellationToken, Task<ProviderIntegrationSyncRunDto?>> readRun)
    {
        var proxy = new Mock<IProviderIntegrationManifestStore>(MockBehavior.Strict);
        proxy.Setup(store => store.GetSyncRunAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(readRun);
        proxy.Setup(store => store.GetConnectionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string id, CancellationToken ct) => underlying.GetConnectionAsync(id, ct));
        proxy.Setup(store => store.GetRawPayloadAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string run, string id, CancellationToken ct) => underlying.GetRawPayloadAsync(run, id, ct));
        proxy.Setup(store => store.GetManifestVersionAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns((string id, int version, CancellationToken ct) => underlying.GetManifestVersionAsync(id, version, ct));
        proxy.Setup(store => store.ListQuarantinedRecordsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string id, CancellationToken ct) => underlying.ListQuarantinedRecordsAsync(id, ct));
        proxy.Setup(store => store.TryCreateSyncRunAsync(It.IsAny<ProviderIntegrationSyncRunDto>(), It.IsAny<CancellationToken>()))
            .Returns((ProviderIntegrationSyncRunDto run, CancellationToken ct) => underlying.TryCreateSyncRunAsync(run, ct));
        proxy.Setup(store => store.SaveSyncRunAsync(It.IsAny<ProviderIntegrationSyncRunDto>(), It.IsAny<CancellationToken>()))
            .Returns((ProviderIntegrationSyncRunDto run, CancellationToken ct) => underlying.SaveSyncRunAsync(run, ct));
        proxy.Setup(store => store.SaveRawPayloadAsync(It.IsAny<RawIngestionPayloadDto>(), It.IsAny<CancellationToken>()))
            .Returns((RawIngestionPayloadDto payload, CancellationToken ct) => underlying.SaveRawPayloadAsync(payload, ct));
        proxy.Setup(store => store.SaveStagingRecordAsync(It.IsAny<IntegrationStagingRecordDto>(), It.IsAny<CancellationToken>()))
            .Returns((IntegrationStagingRecordDto record, CancellationToken ct) => underlying.SaveStagingRecordAsync(record, ct));
        proxy.Setup(store => store.SaveQuarantinedRecordAsync(It.IsAny<QuarantinedRecordDto>(), It.IsAny<CancellationToken>()))
            .Returns((QuarantinedRecordDto record, CancellationToken ct) => underlying.SaveQuarantinedRecordAsync(record, ct));
        return proxy.Object;
    }

    private static async Task SeedQuarantineAsync(
        IProviderIntegrationManifestStore store,
        ProviderIntegrationManifestDto manifest,
        bool includeDuplicateRecord = false)
    {
        await store.SaveManifestAsync(manifest).ConfigureAwait(false);
        await store.SaveConnectionAsync(CreateConnection()).ConfigureAwait(false);
        var reference = ProviderIntegrationManifestIdentity.Create(manifest);
        await store.SaveRawPayloadAsync(new RawIngestionPayloadDto(
            "payload-1",
            manifest.ProviderId,
            "connection-alpha",
            ProviderCapabilityKindDto.Positions,
            "positions",
            "sync-run-quarantine-1",
            DateTimeOffset.Parse("2026-06-16T12:00:00Z"),
            new Dictionary<string, string>(),
            Json("""{"positions":[{"account_id":"A-100","quantity":"100","as_of_date":"2026-06-16","position_id":"POS-1"}]}"""),
            $"{manifest.ManifestId}:v{manifest.ManifestVersion}",
            ProviderIntegrationProcessingStatusDto.Received)
        {
            ManifestReference = reference,
            OriginalManifestReference = reference
        }).ConfigureAwait(false);
        await store.SaveSyncRunAsync(new ProviderIntegrationSyncRunDto(
            "sync-run-quarantine-1",
            manifest.ManifestId,
            "connection-alpha",
            manifest.ProviderId,
            ProviderCapabilityKindDto.Positions,
            "positions",
            DateTimeOffset.Parse("2026-06-16T12:00:00Z"),
            DateTimeOffset.Parse("2026-06-16T12:02:00Z"),
            ProviderIntegrationProcessingStatusDto.Quarantined,
            RecordsReceived: 1,
            RecordsAccepted: 0,
            RecordsQuarantined: 1,
            RawPayloadId: "payload-1",
            Issues:
            [
                new ValidationIssueDto(
                    "required.missing",
                    ProviderIntegrationIssueSeverityDto.Critical,
                    "Required field 'security.cusip' is missing.",
                    "security.cusip",
                    "Map CUSIP, ISIN, ticker, or provider security id.")
            ])
        {
            ManifestReference = reference,
            OriginalManifestReference = reference
        }).ConfigureAwait(false);
        await store.SaveQuarantinedRecordAsync(new QuarantinedRecordDto(
            "quarantine-1",
            "sync-run-quarantine-1",
            "connection-alpha",
            ProviderCapabilityKindDto.Positions,
            Json("""{"account_id":"A-100","quantity":"100","as_of_date":"2026-06-16","position_id":"POS-1"}"""),
            MappedRecord: Json("""{"providerAccountId":"A-100","quantity":"100","asOf":"2026-06-16"}"""),
            [
                new ValidationIssueDto(
                    "required.missing",
                    ProviderIntegrationIssueSeverityDto.Critical,
                    "Required field 'security.cusip' is missing.",
                    "security.cusip",
                    "Map CUSIP, ISIN, ticker, or provider security id.")
            ],
            ProviderIntegrationProcessingStatusDto.Quarantined,
            DateTimeOffset.Parse("2026-06-16T12:01:00Z"))).ConfigureAwait(false);
        if (includeDuplicateRecord)
        {
            await store.SaveQuarantinedRecordAsync(new QuarantinedRecordDto(
                "quarantine-2",
                "sync-run-quarantine-1",
                "connection-alpha",
                ProviderCapabilityKindDto.Positions,
                Json("""{"account_id":"A-100","quantity":"100","as_of_date":"2026-06-16","position_id":"POS-1"}"""),
                MappedRecord: Json("""{"providerAccountId":"A-100","quantity":"100","asOf":"2026-06-16"}"""),
                [
                    new ValidationIssueDto(
                        "required.missing",
                        ProviderIntegrationIssueSeverityDto.Critical,
                        "Required field 'security.cusip' is missing.",
                        "security.cusip",
                        "Map CUSIP, ISIN, ticker, or provider security id.")
                ],
                ProviderIntegrationProcessingStatusDto.Quarantined,
                DateTimeOffset.Parse("2026-06-16T12:01:30Z"))).ConfigureAwait(false);
        }
    }

    private static async Task PublishAsync(
        IProviderIntegrationManifestStore store,
        ProviderIntegrationManifestDto current,
        ProviderIntegrationManifestDto next)
    {
        await store.SaveManifestVersionAsync(next);
        (await store.CompareExchangeCurrentManifestAsync(
            current.ManifestId,
            ProviderIntegrationManifestIdentity.Create(current),
            ProviderIntegrationManifestIdentity.Create(next))).Should().BeTrue();
    }

    private static ProviderIntegrationManifestDto CreateReplayReadyManifest(
        bool includeSecurityMapping = true,
        bool includeMarketValueWithoutCurrency = false)
    {
        var mappings = new List<FieldMappingDto>
        {
            Mapping("$.account_id", "providerAccountId"),
            Mapping("$.quantity", "quantity", new TransformRuleDto("decimal", new Dictionary<string, string>())),
            Mapping("$.as_of_date", "asOf", new TransformRuleDto("date", new Dictionary<string, string>())),
            Mapping("$.position_id", "sourceRecordId")
        };
        if (includeMarketValueWithoutCurrency)
        {
            mappings.Add(new FieldMappingDto(
                ProviderCapabilityKindDto.Positions,
                "$.market_value",
                "marketValue.amount",
                new TransformRuleDto("decimal", new Dictionary<string, string>()),
                Required: false,
                ProviderMappingConfidenceDto.Approved,
                DefaultValue: null,
                ConstantValue: "1000.00"));
        }

        if (includeSecurityMapping)
        {
            mappings.Add(new FieldMappingDto(
                ProviderCapabilityKindDto.Positions,
                "$.missing_cusip",
                "security.cusip",
                new TransformRuleDto("trimUppercase", new Dictionary<string, string>()),
                Required: true,
                ProviderMappingConfidenceDto.Approved,
                DefaultValue: null,
                ConstantValue: "9128285M8"));
        }

        return new ProviderIntegrationManifestDto(
            "manifest-custodian-abc-v1",
            1,
            "custodian-abc",
            "Custodian ABC",
            IntegrationTypeDto.Rest,
            "production",
            new ProviderIntegrationAuthConfigDto(
                ProviderIntegrationAuthTypeDto.OAuth2,
                "https://api.example.com/oauth/token",
                ["positions.read"],
                new Dictionary<string, string>()),
            [
                new ProviderCapabilityDto(
                    ProviderCapabilityKindDto.Positions,
                    Enabled: true,
                    RequiresCertifiedAdapter: false,
                    RequiredCanonicalFields: ["providerAccountId", "quantity", "asOf", "security.cusip"])
            ],
            [],
            mappings,
            new SyncScheduleDto(
                "incremental",
                "daily",
                "06:00",
                "America/New_York",
                ProviderIntegrationCursorTypeDto.Timestamp,
                "updated_at",
                "monthly"),
            [],
            new ProviderIntegrationActivationPolicyDto(
                RequiresAuthenticationTest: true,
                RequiresEndpointTest: true,
                RequiresDryRun: true,
                RequiresApproval: true,
                ProductionWriteCapabilitiesAllowed: false,
                RequiredIssueCodes: []),
            ProviderIntegrationActivationStateDto.DryRunPassed,
            "operator@example.com",
            DateTimeOffset.Parse("2026-06-16T11:50:00Z"),
            ApprovedBy: null,
            ApprovedAt: null,
            ChangeReason: "Initial mapping");
    }

    private static FieldMappingDto Mapping(
        string sourcePath,
        string targetField,
        TransformRuleDto? transform = null)
        => new(
            ProviderCapabilityKindDto.Positions,
            sourcePath,
            targetField,
            transform,
            Required: true,
            ProviderMappingConfidenceDto.Approved,
            DefaultValue: null,
            ConstantValue: null);

    private static ProviderConnectionDto CreateConnection()
        => new(
            "connection-alpha",
            "custodian-abc",
            "manifest-custodian-abc-v1",
            "Custodian ABC General Account",
            "production",
            ProviderIntegrationActivationStateDto.DryRunPassed,
            "vault://provider-credentials/custodian-abc/general-account",
            [ProviderCapabilityKindDto.Positions],
            "operator@example.com",
            DateTimeOffset.Parse("2026-06-16T12:00:00Z"),
            DateTimeOffset.Parse("2026-06-16T12:00:00Z"),
            ApprovalEvidenceId: null);

    private static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
