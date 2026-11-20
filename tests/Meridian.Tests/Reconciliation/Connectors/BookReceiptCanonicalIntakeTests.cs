using System.Text;
using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Integrity;
using Meridian.DataIntegration.Reconciliation;
using Meridian.Domain.Reconciliation;
using Meridian.FinancialOperations.Reconciliation;
using Meridian.FinancialOperations.Reconciliation.Connectors;
using Meridian.Storage.Archival;
using Xunit;

namespace Meridian.Tests.Reconciliation.Connectors;

public sealed class BookReceiptCanonicalIntakeTests : IDisposable
{
    private readonly string _root = StatementConnectorTestData.CreateTempRoot("book-receipt-intake");
    private readonly Guid _securityId = Guid.Parse("ab80653a-92f8-4697-b62b-f96c588932c5");
    private const string SourceHeader = "account,symbol,quantity,price,cashAmount,activityType,tradeDate,currency,externalTransactionId,relatedTransactionId,securityId,receiptAllocations,obligationKey,projectedCashFlowId";
    private const string CanonicalHeader = "account,symbol,quantity,price,cashAmount,activityType,tradeDate,settlementDate,currency,feesCommission,externalTransactionId,activityCategory,activitySubtype,providerActivityCode,relatedTransactionId,orderId,description,marketValue,accruedInterest,securityId,receiptAllocations,obligationKey,projectedCashFlowId";

    [Fact]
    public async Task Connector_to_retained_canonical_import_preserves_split_coupon_and_linked_reversal()
    {
        const string allocations = "[{\"obligationKey\":\"coupon-a\",\"amount\":40.25},{\"obligationKey\":\"coupon-b\",\"amount\":59.75}]";
        var source = SourceHeader + "\n" + SourceRow("payment", 100m, "coupon", allocations) + "\n"
            + SourceRow("reversal", -100m, "coupon", allocations, related: "payment") + "\n";
        var parsed = await Connector().ParseAsync(new("source.csv", Encoding.UTF8.GetBytes(source)));
        parsed.HasErrors.Should().BeFalse();
        parsed.Records.Should().HaveCount(2);
        parsed.Records.Should().OnlyContain(r => r.ActivityType == "coupon" && r.SecurityId == _securityId);
        var artifact = StatementImportService.RenderCanonicalArtifact(parsed.Records);
        artifact.Split('\n')[0].Should().Be(CanonicalHeader);
        var sourcePath = Path.Combine(_root, "source.csv");
        var artifactPath = Path.Combine(_root, "canonical.csv");
        await File.WriteAllTextAsync(sourcePath, source);
        await File.WriteAllTextAsync(artifactPath, artifact);
        var request = new BrokerStatementImportRequest("samplebroker", sourcePath, new(2026, 11, 20))
        {
            ExternalAccountId = "A1",
            CanonicalSourcePath = artifactPath,
            ExecutedMappingFingerprint = parsed.ExecutedMappingFingerprint
        };
        var store = Store();
        var imported = await new CsvBrokerStatementService(store).ImportAsync(request);
        imported.Rows.Should().HaveCount(2);
        imported.Rows.Should().OnlyContain(r => r.ActivityType == "coupon" && r.SecurityId == _securityId
            && r.ReceiptAllocations == allocations);
        imported.Rows[1].RelatedTransactionId.Should().Be("payment");
        imported.Rows[1].CashAmount.Should().Be(-100m);
        var originalHash = StatementDurabilityHashing.Hash(imported);
        File.Delete(sourcePath);
        File.Delete(artifactPath);
        var retained = (await Store().GetImportAsync(imported.Import.ImportId))!;
        StatementDurabilityHashing.Hash(retained).Should().Be(originalHash);
        var evidence = await store.ReadSourceEvidenceAsync(imported.Import.ImportId);
        evidence.Source.Should().Equal(Encoding.UTF8.GetBytes(source));
        evidence.ParserInput.Should().Equal(Encoding.UTF8.GetBytes(artifact));
        StatementDurabilityHashing.Hash(retained with
        {
            Rows = retained.Rows.Select(r => r with { RelatedTransactionId = "different-payment" }).ToArray()
        }).Should().NotBe(originalHash, "reversal linkage is part of completed canonical source authority");
    }

    [Theory]
    [InlineData("[{\"obligationKey\":\"coupon-a\",\"amount\":100,\"amount\":100}]", null)]
    [InlineData("[{\"obligationKey\":\"coupon-a\",\"amount\":99}]", null)]
    [InlineData("[{\"obligationKey\":\"coupon-a\",\"amount\":\"100\"}]", null)]
    [InlineData("[{\"obligationKey\":\"coupon-a\",\"unknown\":true,\"amount\":100}]", null)]
    [InlineData("[{\"obligationKey\":\"coupon-a\",\"amount\":50},{\"obligationKey\":\" coupon-a \",\"amount\":50}]", null)]
    [InlineData(null, "00000000-0000-0000-0000-000000000000")]
    public async Task Preview_and_direct_canonical_parser_reject_malformed_allocation_evidence(string? allocations, string? security)
    {
        var source = SourceHeader + "\n" + SourceRow("payment", 100m, "coupon", allocations, security: security) + "\n";
        var parsed = await Connector().ParseAsync(new("source.csv", Encoding.UTF8.GetBytes(source)));
        parsed.HasErrors.Should().BeTrue();
        parsed.Issues.Should().Contain(i => i.Code == "ROW_INVALID_RECEIPT_METADATA");
        parsed.Records.Should().BeEmpty();

        // Exercise the canonical parser independently; source imports can bypass connector preview.
        var fields = new[] { "A1", "BOND", "0", "0", "100", "coupon", "2026-11-20", "", "USD", "", "payment",
            "", "", "", "", "", "", "", "", security ?? _securityId.ToString("D"), allocations ?? "", "", "" };
        var path = Path.Combine(_root, "invalid.csv");
        await File.WriteAllTextAsync(path, CanonicalHeader + "\n" + string.Join(',', fields.Select(Escape)) + "\n");
        var validation = await new CsvBrokerStatementService(Store()).ValidateAsync(
            new BrokerStatementImportRequest("samplebroker", path, new(2026, 11, 20)) { ExternalAccountId = "A1" });
        validation.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Older_prefix_artifacts_and_absent_metadata_keep_their_exact_serialization()
    {
        const string legacyHeader = "account,symbol,quantity,price,cashAmount,activityType,tradeDate,settlementDate,currency,feesCommission,externalTransactionId,activityCategory,activitySubtype,providerActivityCode,relatedTransactionId,orderId,description";
        const string legacyArtifact = legacyHeader + "\nA1,BOND,1,100,-100,trade,2026-11-20,,USD,,trade-1,,,,,,\n";
        var parsed = await Connector().ParseAsync(new("legacy.csv", Encoding.UTF8.GetBytes(legacyArtifact)));
        parsed.HasErrors.Should().BeFalse();
        StatementImportService.RenderCanonicalArtifact(parsed.Records).Should().Be(legacyArtifact);

        // This shape predates receipt fields. Null omission must preserve its content hash exactly.
        const string legacyRow = "{\"ImportId\":\"legacy\",\"SourceRowNumber\":2,\"Account\":\"A1\",\"Symbol\":\"BOND\",\"Quantity\":1,\"Price\":100,\"CashAmount\":-100,\"ActivityType\":\"trade\",\"TradeDate\":\"2026-11-20\",\"RawChecksum\":\"original\",\"Currency\":\"USD\",\"SettlementDate\":null,\"FeesCommission\":null,\"ExternalTransactionId\":\"trade-1\"}";
        var row = JsonSerializer.Deserialize<CanonicalStatementRow>(legacyRow)!;
        var serialized = JsonSerializer.Serialize(row);
        serialized.Should().Be(legacyRow);
        Sha256Digest.ComputeUtf8(serialized).Should().Be(Sha256Digest.ComputeUtf8(legacyRow));
    }

    private CsvStatementConnector Connector() => new(new StatementMappingProfileCatalog(new FileStatementMappingProfileStore(_root)));
    private JsonCanonicalStatementStore Store() => new(_root, new AtomicFileWriterAdapter());
    private string SourceRow(string transaction, decimal amount, string kind, string? allocations,
        string? related = null, string? security = null)
        => string.Join(',', new[] { "A1", "BOND", "0", "0", amount.ToString(System.Globalization.CultureInfo.InvariantCulture), kind,
            "2026-11-20", "USD", transaction, related ?? "", security ?? _securityId.ToString("D"), allocations ?? "", "", "" }.Select(Escape));
    private static string Escape(string value) => value.Contains(',') || value.Contains('"')
        ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : value;
    public void Dispose() => Directory.Delete(_root, recursive: true);
}
