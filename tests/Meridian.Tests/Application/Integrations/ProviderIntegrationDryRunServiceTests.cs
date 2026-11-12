using System.Text.Json;
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
        var expectedManifest = ProviderIntegrationManifestIdentity.Create(manifest);
        syncRun.ManifestReference.Should().Be(expectedManifest);
        syncRun.OriginalManifestReference.Should().Be(expectedManifest);

        var rawPayload = await store.GetRawPayloadAsync(result.SyncRunId, result.RawPayloadId);
        rawPayload.Should().NotBeNull();
        rawPayload!.RawPayload.GetProperty("recordCount").GetInt32().Should().Be(1);
        rawPayload.ManifestReference.Should().Be(expectedManifest);
        rawPayload.OriginalManifestReference.Should().Be(expectedManifest);

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
                cusip,isin,ticker,issuer,description,coupon,coupon_units,coupon_cash_basis,payment_frequency,day_count,issue_date,maturity_date,currency,currency_review,payment_calendars,business_day_convention,accrual_adjusted,settlement_days,settlement_convention,settlement_calendars,call_schedule,factor_history,rating,sector,naic_designation
                9128285M8,US9128285M81,UST,US Treasury,US Treasury Note,4.25,Percent,Level,SemiAnnual,30/360,2026-06-16,2031-06-16,usd,Reviewed,"[""WEEKENDS""]",Following,false,1,Following,"[""WEEKENDS""]",[],[],AA+,Government,1
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
        mapped.GetProperty("security").GetProperty("currencyReview").GetString().Should().Be("Reviewed");
        mapped.GetProperty("security").GetProperty("paymentCalendars").EnumerateArray()
            .Select(calendar => calendar.GetString()).Should().Equal("WEEKENDS");
        decimal.Parse(mapped.GetProperty("fixedIncomePaymentPreview").GetProperty("sampleCouponPayment").GetString()!,
            System.Globalization.CultureInfo.InvariantCulture).Should().Be(2125m);
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
    public async Task RunManualCsvDryRunAsync_RetainsManifestReferenceWhenMissingMappingBlocksRun()
    {
        var store = new FileProviderIntegrationManifestStore(testRoot);
        var manifest = CreateManifest() with { FieldMappings = [] };
        var connection = CreateConnection(manifest);
        await store.SaveManifestAsync(manifest);
        await store.SaveConnectionAsync(connection);
        var service = new ProviderIntegrationDryRunService(store);

        var result = await service.RunManualCsvDryRunAsync(CreateRequest(
            manifest, connection, "account_id,quantity\nA-100,100"));

        result.Status.Should().Be(ProviderIntegrationProcessingStatusDto.Blocked);
        var expected = ProviderIntegrationManifestIdentity.Create(manifest);
        var retainedRun = await store.GetSyncRunAsync(result.SyncRunId);
        retainedRun!.ManifestReference.Should().Be(expected);
        retainedRun.OriginalManifestReference.Should().Be(expected);
        var retainedPayload = await store.GetRawPayloadAsync(result.SyncRunId, result.RawPayloadId);
        retainedPayload!.ManifestReference.Should().Be(expected);
        retainedPayload.OriginalManifestReference.Should().Be(expected);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RunManualCsvDryRunAsync_RejectsReusedRunIdBeforeAppendingEvidence(bool replayOwnsRunId)
    {
        var store = new FileProviderIntegrationManifestStore(testRoot);
        var manifest = CreateManifest();
        var connection = CreateConnection(manifest);
        await store.SaveManifestAsync(manifest);
        await store.SaveConnectionAsync(connection);
        var service = new ProviderIntegrationDryRunService(store);
        var request = CreateRequest(manifest, connection,
            "account_id,cusip,quantity,as_of,source_id\nA-100,,100,2026-06-16,POS-1");
        var source = await service.RunManualCsvDryRunAsync(request);
        var retainedRunId = source.SyncRunId;
        if (replayOwnsRunId)
        {
            var sourceRecord = (await store.ListQuarantinedRecordsAsync(source.SyncRunId)).Single();
            var replay = await new ProviderIntegrationQuarantineReplayService(store).ReplayAsync(
                new ProviderIntegrationQuarantineReplayRequestDto(
                    "replay-owned-run", source.SyncRunId, manifest.ManifestId, connection.ConnectionId,
                    request.Capability, [sourceRecord.QuarantineRecordId], request.RequestedBy, request.RequestedAt));
            retainedRunId = replay.ReplaySyncRunId;
        }

        var retainedRun = await store.GetSyncRunAsync(retainedRunId);
        var retainedPayload = await store.GetRawPayloadAsync(retainedRunId, retainedRun!.RawPayloadId!);
        var retainedQuarantine = await store.ListQuarantinedRecordsAsync(retainedRunId);
        var evidenceFiles = Directory.GetFiles(testRoot, "*.json", SearchOption.AllDirectories);
        var act = () => service.RunManualCsvDryRunAsync(request with
        {
            SyncRunId = retainedRunId,
            FileName = "different-payload.csv"
        });

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*unused sync run id*");
        (await store.GetSyncRunAsync(retainedRunId)).Should().BeEquivalentTo(retainedRun);
        JsonSerializer.Serialize(await store.GetRawPayloadAsync(retainedRunId, retainedRun.RawPayloadId!))
            .Should().Be(JsonSerializer.Serialize(retainedPayload));
        JsonSerializer.Serialize(await store.ListQuarantinedRecordsAsync(retainedRunId))
            .Should().Be(JsonSerializer.Serialize(retainedQuarantine));
        (await store.ListStagingRecordsAsync(retainedRunId)).Should().BeEmpty();
        Directory.GetFiles(testRoot, "*.json", SearchOption.AllDirectories).Should().BeEquivalentTo(evidenceFiles);
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

    [Theory]
    [InlineData("Clean", "Clean", "1", "98500", "98500", "98620")]
    [InlineData("Clean", "Dirty", "0.8", "78920", "78800", "78920")]
    [InlineData("Dirty", "Clean", "0.8", "78680", "78680", "78800")]
    [InlineData("Dirty", "Dirty", "0.8", "78800", "78680", "78800")]
    public async Task RunManualCsvDryRunAsync_ReconcilesBondFaceFactorAndConsiderationBasis(
        string priceBasis, string valueBasis, string factor, string providerValue, string clean, string dirty)
    {
        var store = new FileProviderIntegrationManifestStore(testRoot);
        var manifest = CreateBondValueManifest();
        var connection = CreateConnection(manifest);
        await store.SaveManifestAsync(manifest);
        await store.SaveConnectionAsync(connection);
        var service = new ProviderIntegrationDryRunService(store);

        var result = await service.RunManualCsvDryRunAsync(CreateRequest(manifest, connection,
            "account_id,cusip,quantity,as_of,source_id,face,factor,quote,provider_value,interest,price_basis,value_basis,currency,interest_currency\n" +
            $"A-100,9128285M8,1,2026-06-16,POS-1,100000,{factor},98.50,{providerValue},120,{priceBasis},{valueBasis},USD,USD"));

        result.RecordsAccepted.Should().Be(1);
        result.Issues.Should().BeEmpty();
        var preview = result.MappedRecordPreviews.Should().ContainSingle().Subject.GetProperty("bondValueReconciliation");
        preview.GetProperty("providerValue").ValueKind.Should().Be(JsonValueKind.String);
        decimal.Parse(preview.GetProperty("normalizedValue").GetString()!, System.Globalization.CultureInfo.InvariantCulture)
            .Should().Be(decimal.Parse(providerValue, System.Globalization.CultureInfo.InvariantCulture));
        decimal.Parse(preview.GetProperty("cleanConsideration").GetString()!, System.Globalization.CultureInfo.InvariantCulture)
            .Should().Be(decimal.Parse(clean, System.Globalization.CultureInfo.InvariantCulture));
        decimal.Parse(preview.GetProperty("dirtyConsideration").GetString()!, System.Globalization.CultureInfo.InvariantCulture)
            .Should().Be(decimal.Parse(dirty, System.Globalization.CultureInfo.InvariantCulture));
        decimal.Parse(preview.GetProperty("variance").GetString()!, System.Globalization.CultureInfo.InvariantCulture).Should().Be(0);
        var staged = (await store.ListStagingRecordsAsync(result.SyncRunId)).Should().ContainSingle().Subject;
        staged.MappedRecord.GetProperty("bondValueReconciliation").GetProperty("status").GetString().Should().Be("Matched");
        var raw = await store.GetRawPayloadAsync(result.SyncRunId, result.RawPayloadId);
        raw!.RawPayload.ToString().Should().NotContain("bondValueReconciliation");
    }

    [Fact]
    public async Task RunManualCsvDryRunAsync_RetainsBondProviderNormalizedAndVarianceWhenQuarantined()
    {
        var store = new FileProviderIntegrationManifestStore(testRoot);
        var manifest = CreateBondValueManifest();
        var connection = CreateConnection(manifest);
        await store.SaveManifestAsync(manifest);
        await store.SaveConnectionAsync(connection);
        var result = await new ProviderIntegrationDryRunService(store).RunManualCsvDryRunAsync(CreateRequest(manifest, connection,
            "account_id,cusip,quantity,as_of,source_id,face,factor,quote,provider_value,interest,price_basis,value_basis,currency,interest_currency\n" +
            "A-100,9128285M8,100000,2026-06-16,POS-1,100000,1,98.50,98525,0,Clean,Clean,USD,USD"));

        result.RecordsQuarantined.Should().Be(1);
        result.Issues.Should().Contain(issue => issue.Code == "market-value.tolerance.exceeded");
        var evidence = (await store.ListQuarantinedRecordsAsync(result.SyncRunId)).Single().MappedRecord!.Value
            .GetProperty("bondValueReconciliation");
        evidence.GetProperty("providerValue").GetString().Should().Be("98525");
        decimal.Parse(evidence.GetProperty("normalizedValue").GetString()!, System.Globalization.CultureInfo.InvariantCulture).Should().Be(98500);
        decimal.Parse(evidence.GetProperty("variance").GetString()!, System.Globalization.CultureInfo.InvariantCulture).Should().Be(25);
    }

    [Theory]
    [InlineData("", "1", "120", "USD")]
    [InlineData("100000", "", "120", "USD")]
    [InlineData("100000", "1", "", "USD")]
    [InlineData("100000", "1", "120", "EUR")]
    public async Task RunManualCsvDryRunAsync_BlocksAmbiguousBondUnitsWithoutUsingQuantityFallback(
        string face, string factor, string interest, string interestCurrency)
    {
        var store = new FileProviderIntegrationManifestStore(testRoot);
        var manifest = CreateBondValueManifest();
        var connection = CreateConnection(manifest);
        await store.SaveManifestAsync(manifest);
        await store.SaveConnectionAsync(connection);
        var result = await new ProviderIntegrationDryRunService(store).RunManualCsvDryRunAsync(CreateRequest(manifest, connection,
            "account_id,cusip,quantity,as_of,source_id,face,factor,quote,provider_value,interest,price_basis,value_basis,currency,interest_currency\n" +
            $"A-100,9128285M8,100000,2026-06-16,POS-1,{face},{factor},98.50,98500,{interest},Clean,Clean,USD,{interestCurrency}"));

        result.RecordsQuarantined.Should().Be(1);
        result.Issues.Should().Contain(issue => issue.Code == "bond-value.conventions.incomplete");
        result.MappedRecordPreviews.Single().GetProperty("bondValueReconciliation")
            .GetProperty("normalizedValue").ValueKind.Should().Be(JsonValueKind.Null);
    }

    private static ProviderIntegrationManifestDto CreateBondValueManifest()
    {
        var manifest = CreateManifest();
        return manifest with
        {
            FieldMappings =
            [
                .. manifest.FieldMappings,
                new FieldMappingDto(ProviderCapabilityKindDto.Positions, "$.asset_class", "security.assetClass", null,
                    false, ProviderMappingConfidenceDto.Approved, null, "Bond"),
                BondValueMapping("face", "bond.originalFace", "decimal"),
                BondValueMapping("factor", "bond.currentFactor", "decimal"),
                BondValueMapping("quote", "price.amount", "decimal"),
                BondValueMapping("provider_value", "marketValue.amount", "decimal"),
                BondValueMapping("currency", "marketValue.currency"),
                BondValueMapping("interest", "bond.accruedInterest.amount", "decimal"),
                BondValueMapping("interest_currency", "bond.accruedInterest.currency"),
                BondValueMapping("price_basis", "bond.priceBasis"),
                BondValueMapping("value_basis", "bond.marketValueBasis"),
                new FieldMappingDto(ProviderCapabilityKindDto.Positions, "$.quotation", "bond.quotationConvention", null,
                    false, ProviderMappingConfidenceDto.Approved, null, "Per100")
            ]
        };
    }

    private static FieldMappingDto BondValueMapping(string source, string target, string? transform = null)
        => new(ProviderCapabilityKindDto.Positions, $"$.{source}", target,
            transform is null ? null : new TransformRuleDto(transform, new Dictionary<string, string>()),
            false, ProviderMappingConfidenceDto.Approved, null, null);

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
