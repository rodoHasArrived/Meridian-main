using FluentAssertions;
using Meridian.Application.Integrations;
using Meridian.Contracts.Integrations;
using Meridian.Storage.Integrations;

namespace Meridian.Tests.Application.Integrations;

public sealed class ProviderIntegrationDryRunServiceTests : IDisposable
{
    private readonly string testRoot;

    public ProviderIntegrationDryRunServiceTests()
    {
        testRoot = Path.Combine(Path.GetTempPath(), $"mdc_csv_dry_run_test_{Guid.NewGuid():N}");
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
    public async Task RunManualCsvDryRunAsync_StagesAcceptedRowsAndRetainsRawPayload()
    {
        var store = new FileProviderIntegrationManifestStore(testRoot);
        var manifest = CreateManifest();
        var connection = CreateConnection(manifest);
        await store.SaveManifestAsync(manifest);
        await store.SaveConnectionAsync(connection);
        var service = new ProviderIntegrationDryRunService(store);

        var result = await service.RunManualCsvDryRunAsync(
            CreateRequest(
                manifest,
                connection,
                """
                account_id,cusip,quantity,as_of,source_id
                A-100, 9128285M8 ,"1,234.50",06/16/2026,POS-1
                """));

        result.RecordsReceived.Should().Be(1);
        result.RecordsAccepted.Should().Be(1);
        result.RecordsQuarantined.Should().Be(0);
        result.Status.Should().Be(ProviderIntegrationProcessingStatusDto.Validated);
        result.Issues.Should().BeEmpty();

        var syncRun = await store.GetSyncRunAsync(result.SyncRunId);
        syncRun.Should().NotBeNull();
        syncRun!.RecordsReceived.Should().Be(1);
        syncRun.RecordsAccepted.Should().Be(1);
        syncRun.RecordsQuarantined.Should().Be(0);
        syncRun.RawPayloadId.Should().Be(result.RawPayloadId);
        syncRun.EndpointKey.Should().Be("manual-csv-upload");

        var rawPayload = await store.GetRawPayloadAsync(result.SyncRunId, result.RawPayloadId);
        rawPayload.Should().NotBeNull();
        rawPayload!.RawPayload.GetProperty("recordCount").GetInt32().Should().Be(1);

        var staged = await store.ListStagingRecordsAsync(result.SyncRunId);
        staged.Should().ContainSingle();
        var mapped = staged.Single().MappedRecord;
        mapped.GetProperty("providerAccountId").GetString().Should().Be("A-100");
        mapped.GetProperty("security").GetProperty("cusip").GetString().Should().Be("9128285M8");
        mapped.GetProperty("quantity").GetDecimal().Should().Be(1234.50m);
        mapped.GetProperty("asOf").GetString().Should().Be("2026-06-16");
        staged.Single().SourceRecordId.Should().Be("POS-1");
        staged.Single().DedupeKey.Should().Contain("POS-1");
    }

    [Fact]
    public async Task RunManualCsvDryRunAsync_QuarantinesRowsMissingRequiredCanonicalFields()
    {
        var store = new FileProviderIntegrationManifestStore(testRoot);
        var manifest = CreateManifest();
        var connection = CreateConnection(manifest);
        await store.SaveManifestAsync(manifest);
        await store.SaveConnectionAsync(connection);
        var service = new ProviderIntegrationDryRunService(store);

        var result = await service.RunManualCsvDryRunAsync(
            CreateRequest(
                manifest,
                connection,
                """
                account_id,cusip,quantity,as_of,source_id
                A-100,,100,2026-06-16,POS-1
                """));

        result.RecordsReceived.Should().Be(1);
        result.RecordsAccepted.Should().Be(0);
        result.RecordsQuarantined.Should().Be(1);
        result.Status.Should().Be(ProviderIntegrationProcessingStatusDto.Quarantined);
        result.Issues.Should().Contain(issue =>
            issue.Code == "required.missing" &&
            issue.TargetField == "security.cusip");

        (await store.ListStagingRecordsAsync(result.SyncRunId)).Should().BeEmpty();
        var syncRun = await store.GetSyncRunAsync(result.SyncRunId);
        syncRun.Should().NotBeNull();
        syncRun!.Status.Should().Be(ProviderIntegrationProcessingStatusDto.Quarantined);
        syncRun.RecordsQuarantined.Should().Be(1);
        syncRun.Issues.Should().Contain(issue => issue.TargetField == "security.cusip");
        var quarantine = await store.ListQuarantinedRecordsAsync(result.SyncRunId);
        quarantine.Should().ContainSingle();
        quarantine.Single().ValidationErrors.Should().Contain(error => error.TargetField == "security.cusip");
        quarantine.Single().MappedRecord.Should().NotBeNull();
    }

    [Fact]
    public async Task RunManualCsvDryRunAsync_MapsTransactionEnumsAndSignedAmounts()
    {
        var store = new FileProviderIntegrationManifestStore(testRoot);
        var manifest = new ProviderIntegrationTemplateCatalog().GetManifest("template-manual-csv-upload-v1")!;
        var connection = CreateConnection(manifest, ProviderCapabilityKindDto.Transactions);
        await store.SaveManifestAsync(manifest);
        await store.SaveConnectionAsync(connection);
        var service = new ProviderIntegrationDryRunService(store);

        var result = await service.RunManualCsvDryRunAsync(
            CreateRequest(
                manifest,
                connection,
                """
                transaction_id,account_id,transaction_type,posting_date,amount,currency,cusip
                TX-1,A-100,FEE,2026-06-16,25.75,usd,9128285M8
                """,
                ProviderCapabilityKindDto.Transactions));

        result.RecordsAccepted.Should().Be(1);
        result.RecordsQuarantined.Should().Be(0);
        var staged = await store.ListStagingRecordsAsync(result.SyncRunId);
        staged.Should().ContainSingle();
        var mapped = staged.Single().MappedRecord;
        mapped.GetProperty("transactionType").GetString().Should().Be("fee");
        mapped.GetProperty("amount").GetProperty("amount").GetDecimal().Should().Be(-25.75m);
        mapped.GetProperty("amount").GetProperty("currency").GetString().Should().Be("USD");
        staged.Single().SourceRecordId.Should().Be("TX-1");
        staged.Single().DedupeKey.Should().Contain("TX-1");
    }

    [Fact]
    public async Task RunManualCsvDryRunAsync_StagesFixedIncomeSecurityMasterWithIdentifierLineage()
    {
        var store = new FileProviderIntegrationManifestStore(testRoot);
        var manifest = new ProviderIntegrationTemplateCatalog().GetManifest("template-fixed-income-security-master-v1")!;
        var connection = CreateConnection(manifest, ProviderCapabilityKindDto.SecurityReferenceData);
        await store.SaveManifestAsync(manifest);
        await store.SaveConnectionAsync(connection);
        var service = new ProviderIntegrationDryRunService(store);

        var result = await service.RunManualCsvDryRunAsync(
            CreateRequest(
                manifest,
                connection,
                """
                cusip,isin,ticker,issuer,description,coupon,maturity_date,currency,rating,sector,naic_designation
                9128285M8,US9128285M81,UST,US Treasury,US Treasury Note,4.25,2031-06-16,usd,AA+,Government,1
                """,
                ProviderCapabilityKindDto.SecurityReferenceData));

        result.RecordsAccepted.Should().Be(1);
        result.RecordsQuarantined.Should().Be(0);
        var staged = await store.ListStagingRecordsAsync(result.SyncRunId);
        staged.Should().ContainSingle();
        var mapped = staged.Single().MappedRecord;
        mapped.GetProperty("security").GetProperty("cusip").GetString().Should().Be("9128285M8");
        mapped.GetProperty("security").GetProperty("coupon").GetDecimal().Should().Be(4.25m);
        mapped.GetProperty("security").GetProperty("maturityDate").GetString().Should().Be("2031-06-16");
        mapped.GetProperty("security").GetProperty("currency").GetString().Should().Be("USD");
        staged.Single().SourceRecordId.Should().Be("9128285M8");
        staged.Single().DedupeKey.Should().Contain("9128285M8");
    }

    [Fact]
    public async Task RunManualCsvDryRunAsync_QuarantinesDuplicateTransactionIdentity()
    {
        var store = new FileProviderIntegrationManifestStore(testRoot);
        var manifest = new ProviderIntegrationTemplateCatalog().GetManifest("template-manual-csv-upload-v1")!;
        var connection = CreateConnection(manifest, ProviderCapabilityKindDto.Transactions);
        await store.SaveManifestAsync(manifest);
        await store.SaveConnectionAsync(connection);
        var service = new ProviderIntegrationDryRunService(store);

        var result = await service.RunManualCsvDryRunAsync(
            CreateRequest(
                manifest,
                connection,
                """
                transaction_id,account_id,transaction_type,posting_date,amount,currency,cusip
                TX-DUP,A-100,BUY,2026-06-16,25.75,usd,9128285M8
                TX-DUP,A-100,BUY,2026-06-16,25.75,usd,9128285M8
                """,
                ProviderCapabilityKindDto.Transactions));

        result.RecordsReceived.Should().Be(2);
        result.RecordsAccepted.Should().Be(1);
        result.RecordsQuarantined.Should().Be(1);
        result.Status.Should().Be(ProviderIntegrationProcessingStatusDto.Quarantined);
        result.Issues.Should().Contain(issue =>
            issue.Code == "duplicate.dedupe-key" &&
            issue.TargetField == "providerTransactionId");
        (await store.ListStagingRecordsAsync(result.SyncRunId)).Should().ContainSingle()
            .Which.SourceRecordId.Should().Be("TX-DUP");
        (await store.ListQuarantinedRecordsAsync(result.SyncRunId)).Should().ContainSingle()
            .Which.ValidationErrors.Should().Contain(error => error.Code == "duplicate.dedupe-key");
    }

    [Fact]
    public async Task RunManualCsvDryRunAsync_QuarantinesMoneyAmountWithoutCurrency()
    {
        var store = new FileProviderIntegrationManifestStore(testRoot);
        var manifest = CreateMoneyValidationManifest();
        var connection = CreateConnection(manifest);
        await store.SaveManifestAsync(manifest);
        await store.SaveConnectionAsync(connection);
        var service = new ProviderIntegrationDryRunService(store);

        var result = await service.RunManualCsvDryRunAsync(
            CreateRequest(
                manifest,
                connection,
                """
                account_id,cusip,quantity,as_of,mkt_val,source_id
                A-100,9128285M8,100,2026-06-16,1000.00,POS-1
                """));

        result.RecordsAccepted.Should().Be(0);
        result.RecordsQuarantined.Should().Be(1);
        result.Issues.Should().Contain(issue =>
            issue.Code == "money.currency.missing" &&
            issue.TargetField == "marketValue.currency");
        (await store.ListStagingRecordsAsync(result.SyncRunId)).Should().BeEmpty();
        (await store.ListQuarantinedRecordsAsync(result.SyncRunId)).Should().ContainSingle()
            .Which.ValidationErrors.Should().Contain(error => error.Code == "money.currency.missing");
    }

    [Fact]
    public async Task RunManualCsvDryRunAsync_ObservesCancellationBeforeWritingEvidence()
    {
        var store = new FileProviderIntegrationManifestStore(testRoot);
        var manifest = CreateManifest();
        var connection = CreateConnection(manifest);
        await store.SaveManifestAsync(manifest);
        await store.SaveConnectionAsync(connection);
        var service = new ProviderIntegrationDryRunService(store);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => service.RunManualCsvDryRunAsync(
            CreateRequest(
                manifest,
                connection,
                """
                account_id,cusip,quantity,as_of
                A-100,9128285M8,100,2026-06-16
                """),
            cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        (await store.GetSyncRunAsync("sync-run-1")).Should().BeNull();
        (await store.ListStagingRecordsAsync("sync-run-1")).Should().BeEmpty();
        (await store.ListQuarantinedRecordsAsync("sync-run-1")).Should().BeEmpty();
    }

    private static ManualCsvProviderIntegrationDryRunRequestDto CreateRequest(
        ProviderIntegrationManifestDto manifest,
        ProviderConnectionDto connection,
        string csvContent,
        ProviderCapabilityKindDto capability = ProviderCapabilityKindDto.Positions)
        => new(
            "sync-run-1",
            manifest.ManifestId,
            connection.ConnectionId,
            capability,
            "positions.csv",
            csvContent,
            "operator@example.com",
            DateTimeOffset.Parse("2026-06-16T12:00:00Z"));

    private static ProviderIntegrationManifestDto CreateManifest()
        => new(
            "manifest-manual-positions-v1",
            1,
            "manual-upload",
            "Manual CSV Upload",
            IntegrationTypeDto.ManualUpload,
            "test",
            new ProviderIntegrationAuthConfigDto(
                ProviderIntegrationAuthTypeDto.None,
                TokenUrl: null,
                Scopes: [],
                Metadata: new Dictionary<string, string>()),
            [
                new ProviderCapabilityDto(
                    ProviderCapabilityKindDto.Positions,
                    Enabled: true,
                    RequiresCertifiedAdapter: false,
                    RequiredCanonicalFields:
                    [
                        "providerAccountId",
                        "security.cusip",
                        "quantity",
                        "asOf"
                    ])
            ],
            Endpoints: [],
            FieldMappings:
            [
                new FieldMappingDto(
                    ProviderCapabilityKindDto.Positions,
                    "$.account_id",
                    "providerAccountId",
                    new TransformRuleDto("trim", new Dictionary<string, string>()),
                    Required: true,
                    ProviderMappingConfidenceDto.Approved,
                    DefaultValue: null,
                    ConstantValue: null),
                new FieldMappingDto(
                    ProviderCapabilityKindDto.Positions,
                    "$.cusip",
                    "security.cusip",
                    new TransformRuleDto("uppercase", new Dictionary<string, string>()),
                    Required: true,
                    ProviderMappingConfidenceDto.Approved,
                    DefaultValue: null,
                    ConstantValue: null),
                new FieldMappingDto(
                    ProviderCapabilityKindDto.Positions,
                    "$.quantity",
                    "quantity",
                    new TransformRuleDto("decimal", new Dictionary<string, string>()),
                    Required: true,
                    ProviderMappingConfidenceDto.Approved,
                    DefaultValue: null,
                    ConstantValue: null),
                new FieldMappingDto(
                    ProviderCapabilityKindDto.Positions,
                    "$.as_of",
                    "asOf",
                    new TransformRuleDto("date", new Dictionary<string, string>()),
                    Required: true,
                    ProviderMappingConfidenceDto.Approved,
                    DefaultValue: null,
                    ConstantValue: null),
                new FieldMappingDto(
                    ProviderCapabilityKindDto.Positions,
                    "$.source_id",
                    "sourceRecordId",
                    Transform: null,
                    Required: false,
                    ProviderMappingConfidenceDto.Approved,
                    DefaultValue: null,
                    ConstantValue: null)
            ],
            new SyncScheduleDto(
                "manual",
                "manual",
                Time: null,
                "America/New_York",
                ProviderIntegrationCursorTypeDto.None,
                CursorField: null,
                FullRefreshFrequency: null),
            ValidationRules: [],
            new ProviderIntegrationActivationPolicyDto(
                RequiresAuthenticationTest: false,
                RequiresEndpointTest: false,
                RequiresDryRun: true,
                RequiresApproval: true,
                ProductionWriteCapabilitiesAllowed: false,
                RequiredIssueCodes: []),
            ProviderIntegrationActivationStateDto.Draft,
            "operator@example.com",
            DateTimeOffset.Parse("2026-06-16T12:00:00Z"),
            ApprovedBy: null,
            ApprovedAt: null,
            ChangeReason: "Initial manual CSV dry-run template");

    private static ProviderIntegrationManifestDto CreateMoneyValidationManifest()
        => CreateManifest() with
        {
            ManifestId = "manifest-manual-money-validation-v1",
            FieldMappings =
            [
                new FieldMappingDto(
                    ProviderCapabilityKindDto.Positions,
                    "$.account_id",
                    "providerAccountId",
                    new TransformRuleDto("trim", new Dictionary<string, string>()),
                    Required: true,
                    ProviderMappingConfidenceDto.Approved,
                    DefaultValue: null,
                    ConstantValue: null),
                new FieldMappingDto(
                    ProviderCapabilityKindDto.Positions,
                    "$.cusip",
                    "security.cusip",
                    new TransformRuleDto("uppercase", new Dictionary<string, string>()),
                    Required: true,
                    ProviderMappingConfidenceDto.Approved,
                    DefaultValue: null,
                    ConstantValue: null),
                new FieldMappingDto(
                    ProviderCapabilityKindDto.Positions,
                    "$.quantity",
                    "quantity",
                    new TransformRuleDto("decimal", new Dictionary<string, string>()),
                    Required: true,
                    ProviderMappingConfidenceDto.Approved,
                    DefaultValue: null,
                    ConstantValue: null),
                new FieldMappingDto(
                    ProviderCapabilityKindDto.Positions,
                    "$.as_of",
                    "asOf",
                    new TransformRuleDto("date", new Dictionary<string, string>()),
                    Required: true,
                    ProviderMappingConfidenceDto.Approved,
                    DefaultValue: null,
                    ConstantValue: null),
                new FieldMappingDto(
                    ProviderCapabilityKindDto.Positions,
                    "$.mkt_val",
                    "marketValue.amount",
                    new TransformRuleDto("decimal", new Dictionary<string, string>()),
                    Required: false,
                    ProviderMappingConfidenceDto.Approved,
                    DefaultValue: null,
                    ConstantValue: null),
                new FieldMappingDto(
                    ProviderCapabilityKindDto.Positions,
                    "$.source_id",
                    "sourceRecordId",
                    Transform: null,
                    Required: false,
                    ProviderMappingConfidenceDto.Approved,
                    DefaultValue: null,
                    ConstantValue: null)
            ]
        };

    private static ProviderConnectionDto CreateConnection(
        ProviderIntegrationManifestDto manifest,
        params ProviderCapabilityKindDto[] enabledCapabilities)
        => new(
            "connection-manual-upload",
            manifest.ProviderId,
            manifest.ManifestId,
            "Manual Upload Test",
            "test",
            ProviderIntegrationActivationStateDto.Draft,
            "none",
            enabledCapabilities.Length == 0 ? [ProviderCapabilityKindDto.Positions] : enabledCapabilities,
            "operator@example.com",
            DateTimeOffset.Parse("2026-06-16T12:00:00Z"),
            DateTimeOffset.Parse("2026-06-16T12:00:00Z"),
            ApprovalEvidenceId: null);
}
