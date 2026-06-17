using System.Text.Json;
using FluentAssertions;
using Meridian.Application.Integrations;
using Meridian.Contracts.Integrations;
using Meridian.Storage.Integrations;

namespace Meridian.Tests.Application.Integrations;

public sealed class ProviderIntegrationSchemaDriftServiceTests : IDisposable
{
    private readonly string testRoot;

    public ProviderIntegrationSchemaDriftServiceTests()
    {
        testRoot = Path.Combine(Path.GetTempPath(), $"mdc_schema_drift_test_{Guid.NewGuid():N}");
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
    public async Task CheckAsync_ReturnsHealthyResultWhenRetainedPayloadMatchesManifest()
    {
        var store = new FileProviderIntegrationManifestStore(testRoot);
        var manifest = new ProviderIntegrationTemplateCatalog().GetManifest("template-custodian-positions-v1")!;
        var connection = CreateConnection(manifest);
        await store.SaveManifestAsync(manifest);
        await store.SaveConnectionAsync(connection);
        await SaveRawPayloadAsync(
            store,
            manifest,
            connection,
            """
            {
              "positions": [
                {
                  "account_id": "A-100",
                  "cusip": "9128285M8",
                  "quantity": "100",
                  "currency": "USD",
                  "as_of_date": "2026-06-16"
                }
              ]
            }
            """);
        var service = new ProviderIntegrationSchemaDriftService(store);

        var result = await service.CheckAsync(CreateRequest(manifest, connection));

        result.DriftDetected.Should().BeFalse();
        result.ShouldPauseCapability.Should().BeFalse();
        result.RecordsInspected.Should().Be(1);
        result.Issues.Should().BeEmpty();
    }

    [Fact]
    public async Task CheckAsync_PausesCapabilityWhenRecordsPathAndRequiredMappingsDisappear()
    {
        var store = new FileProviderIntegrationManifestStore(testRoot);
        var manifest = new ProviderIntegrationTemplateCatalog().GetManifest("template-custodian-positions-v1")!;
        var connection = CreateConnection(manifest);
        await store.SaveManifestAsync(manifest);
        await store.SaveConnectionAsync(connection);
        await SaveRawPayloadAsync(
            store,
            manifest,
            connection,
            """
            {
              "holdings": [
                {
                  "account_id": "A-100",
                  "units": "100"
                }
              ]
            }
            """);
        var service = new ProviderIntegrationSchemaDriftService(store);

        var result = await service.CheckAsync(CreateRequest(manifest, connection));

        result.DriftDetected.Should().BeTrue();
        result.ShouldPauseCapability.Should().BeTrue();
        result.RecordsInspected.Should().Be(0);
        result.Issues.Should().Contain(issue => issue.Code == "schema.records-path.missing");
        result.Issues.Should().Contain(issue => issue.Code == "schema.required-path.missing");
        result.Issues.Should().OnlyContain(issue => issue.Severity == ProviderIntegrationIssueSeverityDto.Critical);
    }

    [Fact]
    public async Task CheckAsync_PausesCapabilityWhenRequiredMappingSourceIsMissingFromRecords()
    {
        var store = new FileProviderIntegrationManifestStore(testRoot);
        var manifest = new ProviderIntegrationTemplateCatalog().GetManifest("template-custodian-positions-v1")!;
        var connection = CreateConnection(manifest);
        await store.SaveManifestAsync(manifest);
        await store.SaveConnectionAsync(connection);
        await SaveRawPayloadAsync(
            store,
            manifest,
            connection,
            """
            {
              "positions": [
                {
                  "account_id": "A-100",
                  "quantity": "100",
                  "as_of_date": "2026-06-16"
                }
              ]
            }
            """);
        var service = new ProviderIntegrationSchemaDriftService(store);

        var result = await service.CheckAsync(CreateRequest(manifest, connection));

        result.DriftDetected.Should().BeTrue();
        result.ShouldPauseCapability.Should().BeTrue();
        result.RecordsInspected.Should().Be(1);
        result.Issues.Should().ContainSingle(issue =>
            issue.Code == "schema.mapping-source.missing" &&
            issue.JsonPath == "$.cusip");
    }

    [Fact]
    public async Task CheckAsync_ObservesCancellationBeforeReadingStore()
    {
        var store = new FileProviderIntegrationManifestStore(testRoot);
        var manifest = new ProviderIntegrationTemplateCatalog().GetManifest("template-custodian-positions-v1")!;
        var connection = CreateConnection(manifest);
        var service = new ProviderIntegrationSchemaDriftService(store);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => service.CheckAsync(CreateRequest(manifest, connection), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private static ProviderIntegrationSchemaDriftCheckRequestDto CreateRequest(
        ProviderIntegrationManifestDto manifest,
        ProviderConnectionDto connection)
        => new(
            manifest.ManifestId,
            connection.ConnectionId,
            ProviderCapabilityKindDto.Positions,
            "positions",
            "sync-run-schema-1",
            "payload-schema-1",
            "operator@example.com",
            DateTimeOffset.Parse("2026-06-16T12:05:00Z"));

    private static ProviderConnectionDto CreateConnection(ProviderIntegrationManifestDto manifest)
        => new(
            "connection-schema-drift",
            manifest.ProviderId,
            manifest.ManifestId,
            "Schema Drift Test",
            "test",
            ProviderIntegrationActivationStateDto.DryRunPassed,
            "vault://provider-credentials/schema-drift/test",
            [ProviderCapabilityKindDto.Positions],
            "operator@example.com",
            DateTimeOffset.Parse("2026-06-16T12:00:00Z"),
            DateTimeOffset.Parse("2026-06-16T12:00:00Z"),
            ApprovalEvidenceId: null);

    private static async Task SaveRawPayloadAsync(
        IProviderIntegrationManifestStore store,
        ProviderIntegrationManifestDto manifest,
        ProviderConnectionDto connection,
        string json)
    {
        using var document = JsonDocument.Parse(json);
        await store.SaveRawPayloadAsync(
            new RawIngestionPayloadDto(
                "payload-schema-1",
                manifest.ProviderId,
                connection.ConnectionId,
                ProviderCapabilityKindDto.Positions,
                "positions",
                "sync-run-schema-1",
                DateTimeOffset.Parse("2026-06-16T12:00:00Z"),
                new Dictionary<string, string> { ["source"] = "schema-drift-test" },
                document.RootElement.Clone(),
                $"{manifest.ManifestId}:v{manifest.ManifestVersion}",
                ProviderIntegrationProcessingStatusDto.Received));
    }
}
