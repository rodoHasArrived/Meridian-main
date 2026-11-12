using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Reconciliation;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.Domain.Reconciliation;
using Meridian.FinancialOperations.Reconciliation;
using Meridian.Infrastructure.Reconciliation;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Meridian.Strategies.Services;
using Meridian.Ui.Shared.Evidence;
using Meridian.Ui.Shared.Services;
using NSubstitute;

namespace Meridian.Tests.Ui;

public sealed class CorporateActionCashDividendReconciliationSupportTests
{
    [Fact]
    public async Task PreviousMonthStatementSupportsMidMonthCutoffOnlyWithUnchangedCanonicalLotsAndExactSidedProof()
    {
        var fixture = new Fixture();
        var first = await fixture.ReadAsync();
        var second = await fixture.ReadAsync();
        first.Should().Be(second, "no wall clock or mutable preparer-stage state belongs in the source frame");
        first.EvidenceHash.Should().Be(Sha256Digest.ComputeUtf8(first.EvidenceJson));
        first.EvidenceJson.Should().Contain("2026-10-31").And.Contain("2026-11-11")
            .And.Contain(Sha256Digest.ComputeUtf8("source-row-sha")).And.Contain("native-position-evidence");
        await fixture.Vault.Received(4).VerifyRetainedContentAsync("vault-source", "tenant-a", "company-a", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MissingCompositionFailsClosedWithoutConstructingOrdinaryHostsDifferently()
    {
        var service = new CorporateActionCashDividendReconciliationSupport();
        var fixture = new Fixture();
        await Assert.ThrowsAsync<CorporateActionValidationException>(() => service.GetSupportAsync(fixture.Scope,
            Fixture.SecurityId, Fixture.Cutoff, fixture.Accounts));
    }

    [Theory]
    [InlineData("checkpoint-hash")]
    [InlineData("checkpoint-running")]
    [InlineData("legacy-groups")]
    [InlineData("incomplete-population")]
    [InlineData("wrong-mapping")]
    [InlineData("unmatched-source")]
    [InlineData("duplicate-source-member")]
    [InlineData("wrong-internal-member")]
    [InlineData("quantity-tolerance")]
    [InlineData("unretained-source-bytes")]
    [InlineData("missing-account-authority")]
    [InlineData("unknown-security")]
    [InlineData("wrong-security")]
    [InlineData("changed-lot-identity")]
    [InlineData("missing-book-account")]
    [InlineData("open-book-break")]
    [InlineData("drifting-queue")]
    public async Task CompletionBadgeCannotReplaceExactRetainedAuthority(string mutation)
    {
        var fixture = new Fixture();
        switch (mutation)
        {
            case "checkpoint-hash": fixture.Checkpoint = fixture.Checkpoint with { MatchArtifact = new(new string('f', 64), 1) }; break;
            case "checkpoint-running": fixture.Checkpoint = fixture.Checkpoint with { Status = StatementRunRecoveryStatus.Running }; break;
            case "legacy-groups": fixture.Artifact = fixture.Artifact with { MatchGroups = null }; fixture.Rehash(); break;
            case "incomplete-population": fixture.Artifact = fixture.Artifact with { SourceComparisonComplete = false }; fixture.Rehash(); break;
            case "wrong-mapping": fixture.Artifact = fixture.Artifact with { SourceComparisonMappingFingerprint = new string('b', 64) }; fixture.Rehash(); break;
            case "unmatched-source": fixture.Artifact = fixture.Artifact with { MatchGroups = [], MatchCount = 0 }; fixture.Rehash(); break;
            case "duplicate-source-member": fixture.Artifact = fixture.Artifact with
                { MatchGroups = [fixture.Artifact.MatchGroups![0] with { StatementEvidenceReferences = ["run-source:1", "run-source:1"] }] }; fixture.Rehash(); break;
            case "wrong-internal-member": fixture.Artifact = fixture.Artifact with
                { MatchGroups = [fixture.Artifact.MatchGroups![0] with { InternalEvidenceReferences = ["unbound-position"] }] }; fixture.Rehash(); break;
            case "quantity-tolerance": fixture.Artifact = fixture.Artifact with
                { MatchGroups = [fixture.Artifact.MatchGroups![0] with { QuantityVariance = 0.01m }] }; fixture.Rehash(); break;
            case "unretained-source-bytes": fixture.Vault.VerifyRetainedContentAsync("vault-source", "tenant-a", "company-a", Arg.Any<CancellationToken>()).Returns(false); break;
            case "missing-account-authority": fixture.Api.GetAuthorizedFundAccountAsync(Fixture.AccountId, Arg.Any<ReconciliationBreakQueueScope>(), Arg.Any<CancellationToken>()).Returns((ReconciliationFundAccountAuthorization?)null); break;
            case "unknown-security": fixture.Security.GetRecordedByIdentifierAsOfAsync(Arg.Any<SecurityIdentifierKind>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>()).Returns((SecurityDetailDto?)null); break;
            case "wrong-security": fixture.Security.GetRecordedByIdentifierAsOfAsync(Arg.Any<SecurityIdentifierKind>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>()).Returns(Fixture.SecurityDetail(Guid.NewGuid())); break;
            case "changed-lot-identity": fixture.PastLot = fixture.PastLot with { TaxLotRecordId = Guid.NewGuid() }; break;
            case "missing-book-account": fixture.Accounts = [new(Fixture.AccountId.ToString("D"), 9m)]; break;
            case "open-book-break": fixture.QueueItems = [fixture.Break(1)]; break;
            case "drifting-queue":
                fixture.Queue.GetAllAsync(Arg.Any<ReconciliationBreakQueueScope>(), null, Arg.Any<CancellationToken>())
                    .Returns(Task.FromResult<IReadOnlyList<ReconciliationBreakQueueItem>>([]), Task.FromResult<IReadOnlyList<ReconciliationBreakQueueItem>>([fixture.Break(2) with { Status = ReconciliationBreakQueueStatus.Resolved }]));
                break;
        }
        await Assert.ThrowsAsync<CorporateActionValidationException>(() => fixture.ReadAsync());
    }

    private sealed class Fixture
    {
        internal static readonly Guid SecurityId = Guid.Parse("c2000000-0000-0000-0000-000000000001");
        internal static readonly Guid BookId = Guid.Parse("c2000000-0000-0000-0000-000000000002");
        internal static readonly Guid AccountId = Guid.Parse("c2000000-0000-0000-0000-000000000003");
        private static readonly Guid PeriodId = Guid.Parse("c2000000-0000-0000-0000-000000000004");
        private static readonly DateOnly AsOf = new(2026, 10, 31);
        internal static readonly DateOnly Cutoff = new(2026, 11, 11);
        private static readonly DateTimeOffset At = new(2026, 11, 1, 8, 0, 0, TimeSpan.Zero);
        internal readonly IReconciliationApiService Api = Substitute.For<IReconciliationApiService>();
        internal readonly ICanonicalStatementStore Canonical = Substitute.For<ICanonicalStatementStore>();
        internal readonly IStatementRunMatchArtifactStore Artifacts = Substitute.For<IStatementRunMatchArtifactStore>();
        internal readonly IStatementRunRecoveryRepository Recovery = Substitute.For<IStatementRunRecoveryRepository>();
        internal readonly IInternalReconciliationPopulationProvider Populations = Substitute.For<IInternalReconciliationPopulationProvider>();
        internal readonly ILedgerSecurityEntitlementInventory Inventory = Substitute.For<ILedgerSecurityEntitlementInventory>();
        internal readonly IEvidenceArtifactStore Vault = Substitute.For<IEvidenceArtifactStore>();
        internal readonly IReconciliationBreakQueueRepository Queue = Substitute.For<IReconciliationBreakQueueRepository>();
        internal readonly ISecurityMasterQueryService Security = Substitute.For<ISecurityMasterQueryService>();
        internal readonly CorporateActionCaseScopeDto Scope = new("tenant-a", "company-a", FundProfileId: "fund-a", LedgerBookId: BookId.ToString("D"));
        internal IReadOnlyList<CashDividendReconciliationAccountDto> Accounts = [new(AccountId.ToString("D"), 10m)];
        internal StatementRunMatchArtifact Artifact;
        internal StatementRunRecoveryCheckpoint Checkpoint;
        internal readonly BrokerStatementImportResult Import;
        internal LedgerTaxLotRecord PastLot;
        internal readonly LedgerTaxLotRecord CutoffLot;
        internal IReadOnlyList<ReconciliationBreakQueueItem> QueueItems = [];

        internal Fixture()
        {
            var hash = Sha256Digest.ComputeUtf8("retained-source-bytes");
            var mapping = Sha256Digest.ComputeUtf8("executed-mapping");
            var accounting = new StatementAccountingScope("fund-a", BookId, PeriodId, AsOf);
            Import = new(new("run-source", "synthetic-custodian", AsOf, At, "retained-source", hash, 1, 1)
            {
                FundAccountId = AccountId.ToString("D"), ExternalAccountId = "custody-a", AccountingScope = accounting,
                StatementPeriodStart = new(2026, 10, 1), StatementPeriodEnd = AsOf, ExecutedMappingFingerprint = mapping
            }, [new("run-source", 1, "custody-a", "XYZ", 10m, 20m, 0m, "position", AsOf, Sha256Digest.ComputeUtf8("source-row-sha"))]);
            Artifact = new("run-source", "run-source", [], [], 1)
            {
                SourceComparisonComplete = true, SourceComparisonMappingFingerprint = mapping,
                SourceComparisonPolicyFingerprint = Sha256Digest.ComputeUtf8("executed-policy"), SourceComparisonPopulationKinds = ["position"],
                MatchGroups = [new("group-1", "Position", "Exact", ["quantity"], ["run-source:1"], ["native-position-evidence"], 1m) { QuantityVariance = 0m }]
            };
            Checkpoint = new(1, "run-source", "run-source", hash, hash, StatementRunRecoveryStage.Completed,
                StatementRunRecoveryStatus.Completed, new(StatementDurabilityHashing.Hash(Import), 1),
                new(StatementDurabilityHashing.Hash(Artifact), 1), null, null, 1, null, null, null, At, At);
            var acquisition = new OpenLotAcquisitionDto(LotQuantityBasis.Units, "USD", "USD", 1m, 200m, 200m,
                new(2026, 9, 1), null, []);
            CutoffLot = new(Guid.NewGuid(), BookId, new("Investment", LedgerAccountType.Asset, "XYZ", AccountId.ToString("D")),
                "lot-1", new(2026, 9, 1), 10m, 10m, 20m, "USD", At, At, Guid.NewGuid(), Version: 1,
                SecurityId: SecurityId, BookPositionId: Guid.NewGuid(), Acquisition: acquisition);
            PastLot = CutoffLot;
            Api.GetAuthorizedFundAccountAsync(AccountId, Arg.Any<ReconciliationBreakQueueScope>(), Arg.Any<CancellationToken>())
                .Returns(new ReconciliationFundAccountAuthorization(AccountId, "fund-a"));
            Api.ListStatementRunsAsync(Arg.Any<ReconciliationBreakQueueScope>(), Arg.Any<CancellationToken>())
                .Returns([new("run-source", "run-source", StatementRunStatus.Completed, At, At, 99, 99, 99, 0)]);
            Api.GetStatementRunAuthorizationAsync("run-source", Arg.Any<ReconciliationBreakQueueScope>(), Arg.Any<CancellationToken>())
                .Returns(new StatementReconciliationRunAuthorization("run-source", AccountId, "fund-a", BookId, PeriodId, AsOf, new(2026, 10, 1), AsOf));
            Canonical.GetImportAsync("run-source", Arg.Any<CancellationToken>()).Returns(_ => Import);
            Artifacts.GetAsync("run-source", Arg.Any<CancellationToken>()).Returns(_ => Artifact);
            Recovery.GetAsync("run-source", Arg.Any<CancellationToken>()).Returns(_ => Checkpoint);
            Populations.GetPopulationsAsync(Arg.Any<InternalReconciliationPopulationContext>(), Arg.Any<CancellationToken>())
                .Returns(new InternalReconciliationPopulations([new("position-1", "custody-a", "XYZ", AsOf, 10m, null, "native-position-evidence")], [], []));
            Inventory.ReadSecurityEntitlementInventoryAsync("tenant-a", "company-a", BookId, SecurityId, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
                .Returns(call => Snapshot(call.ArgAt<DateOnly>(4)));
            Queue.GetAllAsync(Arg.Any<ReconciliationBreakQueueScope>(), null, Arg.Any<CancellationToken>()).Returns(_ => QueueItems);
            Vault.FindByLinkageAsync(Arg.Any<EvidenceVaultLookupRequestDto>(), Arg.Any<CancellationToken>()).Returns([
                new EvidenceVaultIdentityDto("vault-source", "statement-run", "run-source", "manifest.json", "/evidence/source", At, hash, 1, "file")
                {
                    TenantId = "tenant-a", Scope = "company-a", Artifacts = [new("source", "source", "source.bin", hash, 5, At, null, null, "statement-run", "run-source")]
                }]);
            Vault.VerifyRetainedContentAsync("vault-source", "tenant-a", "company-a", Arg.Any<CancellationToken>()).Returns(true);
            Security.GetRecordedByIdentifierAsOfAsync(SecurityIdentifierKind.Ticker, "XYZ", null, At, Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
                .Returns(SecurityDetail(SecurityId));
        }

        internal Task<CashDividendReconciliationSupportDto> ReadAsync() => new CorporateActionCashDividendReconciliationSupport(
            Api, Canonical, Artifacts, Recovery, Populations, Inventory, Vault, Queue, Security)
            .GetSupportAsync(Scope, SecurityId, Cutoff, Accounts);

        internal void Rehash() => Checkpoint = Checkpoint with
        { MatchArtifact = new(StatementDurabilityHashing.Hash(Artifact), Artifact.MatchCount + Artifact.Breaks.Count), MatchCount = Artifact.MatchCount };

        internal ReconciliationBreakQueueItem Break(long version) => new("book-break", "run-source", "synthetic", ReconciliationBreakCategory.AmountMismatch,
            ReconciliationBreakQueueStatus.Open, 10m, "quantity", null, At, At, ToleranceBand: 0m,
            FundAccountId: AccountId.ToString("D"), Version: version, LedgerBookId: BookId)
        { TenantId = "tenant-a", CompanyId = "company-a", FundProfileId = "fund-a" };

        private LedgerSecurityEntitlementInventorySnapshot Snapshot(DateOnly date)
        {
            var lot = date == AsOf ? PastLot : CutoffLot;
            var json = JsonSerializer.Serialize(new { Date = date, Lot = lot });
            return new("tenant-a", "company-a", new(BookId, "fund-a", Guid.Parse("c2000000-0000-0000-0000-000000000009"),
                FundStructureNodeKindDto.Fund, "USD book", "USD", At, At), SecurityId, date, 1, [lot], json, Sha256Digest.ComputeUtf8(json));
        }

        internal static SecurityDetailDto SecurityDetail(Guid id) => new(id, "Equity", SecurityStatusDto.Active, "XYZ", "USD",
            JsonSerializer.SerializeToElement(new { }), JsonSerializer.SerializeToElement(new { }), [], [], 1, At.AddMonths(-1), null);
    }
}
