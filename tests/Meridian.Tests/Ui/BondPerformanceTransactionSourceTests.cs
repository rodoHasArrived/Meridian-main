using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.Storage.FixedIncome;
using Meridian.Ui.Shared.Evidence;
using Meridian.Ui.Shared.Services;
using NSubstitute;

namespace Meridian.Tests.Ui;

[Trait("Category", "Unit")]
public sealed class BondPerformanceTransactionSourceTests
{
    private static readonly AccountCashForecastScopeDto Scope = new("tenant-a", "company-a",
        "d091eddd-4c8e-4051-80a3-a06859fbebcc", "a8877cda-0218-475b-886e-7f5e6e5d8104", "USD");
    private static readonly DateOnly Start = new(2026, 1, 1), End = new(2026, 7, 1);
    private static readonly DateTimeOffset Known = new(2026, 11, 12, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid OpeningBatch = Guid.Parse("62a5c5f8-16ef-48cb-8558-dc0ec519db09"), ClosingBatch = Guid.Parse("51e266cf-9ba5-4bd5-88b0-932c412c2222");
    private const string VaultId = "ev-0123456789abcdef01234567";
    private const string DocumentId = "transactions";
    private const string Uri = "evidence://evidence-vault/" + VaultId;

    [Fact]
    public async Task AcceptedCompletePopulation_PersistsSignedTradesAndExplicitZeroReceipts_Idempotently()
    {
        using var fixture = new Fixture();
        fixture.Set(Source());
        var first = await fixture.Source.ImportAsync(Scope, Uri, DocumentId, "operator-a");
        var retry = await fixture.Source.ImportAsync(Scope, Uri, DocumentId, "operator-b");
        retry.Should().BeEquivalentTo(first);
        var coverage = await fixture.Source.GetAsync(Scope, Start, End, Known);
        coverage.Complete.Should().BeTrue();
        coverage.CashPopulationComplete.Should().BeTrue();
        coverage.PaymentVersionIds.Should().BeEmpty();
        coverage.TradeCash.Select(row => row.Amount).Should().Equal(-1000m, 1020m);
        coverage.Inputs.Should().ContainSingle().Which.ContentSha256.Should().Be(first.Evidence.ContentHashSha256);
        (await fixture.Store.ReadAsync(Scope)).Should().ContainSingle();
        (await fixture.Store.ReadAsync(Scope with { AccountId = Guid.NewGuid().ToString("D") })).Should().BeEmpty();
    }

    [Theory]
    [InlineData("foreign-account")]
    [InlineData("unreviewed")]
    [InlineData("same-reviewer")]
    [InlineData("unconfirmed-hash")]
    [InlineData("content-hash")]
    public async Task UnverifiedOrForeignSource_CannotBecomeReturnCoverage(string failure)
    {
        using var fixture = new Fixture();
        fixture.Set(Source(), failure);
        Func<Task> import = () => fixture.Source.ImportAsync(Scope, Uri, DocumentId, "operator");
        var exception = await import.Should().ThrowAsync<Exception>();
        exception.Which.GetType().Should().Be(failure == "content-hash" ? typeof(InvalidDataException) : typeof(InvalidOperationException));
        (await fixture.Store.ReadAsync(Scope)).Should().BeEmpty();
    }

    [Fact]
    public async Task MissingRetainedReceiptIds_BlockCompletenessRatherThanInferringZeroCash()
    {
        using var fixture = new Fixture();
        fixture.Set(Source() with { PaymentVersionIds = ["receipt-not-imported"] });
        await fixture.Source.ImportAsync(Scope, Uri, DocumentId, "operator");
        var coverage = await fixture.Source.GetAsync(Scope, Start, End, Known);
        coverage.Complete.Should().BeFalse();
        coverage.CoverageGaps.Should().Contain(gap => gap.Contains("payment observations", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ReviewRevocation_AfterImport_IsRecheckedBeforeAnyReturnClaim()
    {
        using var fixture = new Fixture();
        fixture.Set(Source());
        await fixture.Source.ImportAsync(Scope, Uri, DocumentId, "operator");
        fixture.Set(Source(), "unreviewed");
        Func<Task> read = () => fixture.Source.GetAsync(Scope, Start, End, Known);
        await read.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task OmittedTradeCashAmount_IsNotDefaultedIntoCertifiedZeroCash()
    {
        using var fixture = new Fixture();
        var json = JsonNode.Parse(JsonSerializer.Serialize(Source(), BondPerformanceTransactionsJsonContext.Default.BondPerformanceTransactionsDocument))!.AsObject();
        json["tradeCash"]!.AsArray()[0]!.AsObject().Remove("amount");
        fixture.Set(Source(), bytes: System.Text.Encoding.UTF8.GetBytes(json.ToJsonString()));
        Func<Task> import = () => fixture.Source.ImportAsync(Scope, Uri, DocumentId, "operator");
        await import.Should().ThrowAsync<JsonException>();
        (await fixture.Store.ReadAsync(Scope)).Should().BeEmpty();
    }

    private static BondPerformanceTransactionsDocument Source() => new(1, "bondPerformanceTransactions", Scope.AccountId,
        Scope.BookId, Start, End, 1, "SyntheticCustodian", "statement-2026-H1", true, true,
        [new("buy", Guid.Parse("b4eead86-3a52-40dd-b6b8-8a51fdeab2c5"), new(2026, 2, 1), -1000m, "USD") { Kind = "Buy", OriginalFaceDelta = 1000m },
         new("sell", Guid.Parse("b4eead86-3a52-40dd-b6b8-8a51fdeab2c5"), new(2026, 3, 1), 1020m, "USD") { Kind = "Sell", OriginalFaceDelta = -1000m }], [])
    { HoldingBasis = "Settled", OpeningHoldingsBatchId = OpeningBatch, ClosingHoldingsBatchId = ClosingBatch };

    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-performance-trades-" + Guid.NewGuid().ToString("N"));
        private readonly IEvidenceArtifactStore _vault = Substitute.For<IEvidenceArtifactStore>();
        private EvidenceVaultIdentityDto? _identity;
        private byte[] _bytes = [];
        public FileBondPerformanceTransactionStore Store { get; }
        public BondPerformanceTransactionSource Source { get; }

        public Fixture()
        {
            Store = new(_root);
            var monitoring = Substitute.For<IBondMonitoringService>();
            monitoring.GetHistoryAsync(Scope, Arg.Any<CancellationToken>()).Returns(Array.Empty<BondMonitoringSnapshotDto>());
            var securities = Substitute.For<ISecurityMasterQueryService>();
            securities.GetRecordedByIdAsOfAsync(Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
                .Returns(call => Task.FromResult<SecurityDetailDto?>(new(call.Arg<Guid>(), "Bond", SecurityStatusDto.Active,
                    "Synthetic bond", "USD", JsonSerializer.SerializeToElement(new { }), JsonSerializer.SerializeToElement(new { }),
                    [], [], 1, Known.AddYears(-1), null)));
            var holdings = Substitute.For<IAccountBondHoldingsSource>();
            holdings.GetHoldingsAsync(Guid.Parse(Scope.AccountId), Start, Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
                .Returns(new AccountBondHoldingsSnapshotDto(Guid.Parse(Scope.AccountId), Start, OpeningBatch, new string('a', 64), "synthetic:start", []));
            holdings.GetHoldingsAsync(Guid.Parse(Scope.AccountId), End, Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
                .Returns(new AccountBondHoldingsSnapshotDto(Guid.Parse(Scope.AccountId), End, ClosingBatch, new string('b', 64), "synthetic:end", []));
            var verifier = Substitute.For<IBondPerformanceSourceVerifier>();
            Source = new(_vault, Store, monitoring, securities, holdings, verifier, new Clock());
            _vault.TryGetVaultIdentityAsync(VaultId, Scope.TenantId, Scope.CompanyId, Arg.Any<CancellationToken>()).Returns(_ => _identity);
            _vault.TryOpenDocumentContentAsync(VaultId, DocumentId, Scope.TenantId, Scope.CompanyId, Arg.Any<CancellationToken>())
                .Returns(_ => new EvidenceManifestFile(new MemoryStream(_bytes), "application/json", "transactions.json", Known.AddHours(-2)));
        }

        public void Set(BondPerformanceTransactionsDocument source, string? failure = null, byte[]? bytes = null)
        {
            _bytes = bytes ?? JsonSerializer.SerializeToUtf8Bytes(source, BondPerformanceTransactionsJsonContext.Default.BondPerformanceTransactionsDocument);
            var hash = failure == "content-hash" ? new string('0', 64) : Sha256Digest.Compute(_bytes);
            var reviewer = failure == "same-reviewer" ? "uploader" : "reviewer";
            var reviewedAt = Known.AddHours(-1);
            var review = new EvidenceDocumentReviewStateDto(failure == "unreviewed" ? EvidenceDocumentReviewStatusDto.Unreviewed
                : EvidenceDocumentReviewStatusDto.Accepted, reviewer, reviewedAt)
            {
                ConfirmedFields = failure == "unconfirmed-hash" ? []
                    : [new("sourceContentHashSha256", hash, reviewer, reviewedAt)]
            };
            var document = new EvidenceDocumentDto(DocumentId, "transactions.json", EvidenceDocumentClassificationDto.BankEvidence,
                hash, Known.AddHours(-2), "ManualUpload", "uploader", Scope.TenantId, Scope.CompanyId,
                EvidenceExtractionStatusDto.Accepted, [], review,
                [new(reviewedAt, reviewer, "DocumentReviewRecorded", "Accepted source bytes")])
            { SourceSystem = source.SourceDomain, SourceReference = source.SourceEntityId, ContentType = "application/json" };
            _identity = new(VaultId, "account", failure == "foreign-account" ? Guid.NewGuid().ToString("D") : Scope.AccountId,
                "manifest.json", "/api/evidence", Known.AddHours(-2), hash, 1, "file-bundle")
            { TenantId = Scope.TenantId, Scope = Scope.CompanyId, Documents = [document] };
        }

        public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
    }

    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => Known; }
}
