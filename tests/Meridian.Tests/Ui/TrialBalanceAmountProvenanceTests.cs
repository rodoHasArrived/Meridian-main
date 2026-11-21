using System.Text;
using FluentAssertions;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Tenancy;
using Meridian.Contracts.Workstation;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Meridian.Ui.Shared.Evidence;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Meridian.Tests.Ui;

public sealed class TrialBalanceAmountProvenanceTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Aggregate_RetainsPreparerClassificationEvenWhenSourceProofIsBlocked(bool retainSources)
    {
        using var fixture = new Fixture();
        await fixture.AddJournalAsync(125m, retainSources);
        fixture.Records[0] = AccountingPreparerAttestationTestData.Attach(fixture.Records[0], fixture.Scope);
        var receipt = fixture.Records[0].Entry.Metadata.PreparerAttestation!.Receipt;
        var row = (await fixture.BindAsync()).Single(value => value.AccountName == "Cash");

        var packet = await fixture.Service.GetPacketAsync(row.BalanceProofSubjectId!, fixture.Scope);

        var amount = packet!.LedgerAmount!;
        amount.Status.Should().Be(retainSources ? EvidenceStatusDto.Ready : EvidenceStatusDto.Blocked);
        var disclosure = amount.AccountingPreparerAttestations.Should().ContainSingle().Which;
        disclosure.Classification.Should().Be(LedgerAmountApprovalClassification.PreparerAttestation);
        disclosure.AttestationId.Should().Be(receipt.AttestationId);
        disclosure.SourceScope.Should().Be(fixture.Scope);
        amount.OrdinaryRecordedApprovalSatisfied.Should().BeFalse();
        LedgerAmountApprovalClassification.MayCountAsOrdinaryApproval(amount, true).Should().BeFalse();
        System.Text.Json.JsonSerializer.Serialize(packet).Should().NotContain("private-retained-operands");
    }

    [Fact]
    public async Task Checkpoint_ProvesExactContributorsAndSurvivesLaterAppends()
    {
        using var fixture = new Fixture();
        await fixture.AddJournalAsync(125m, retainSources: true);
        var binding = (await fixture.BindAsync()).Single(row => row.AccountName == "Cash");
        binding.BalanceProofSubjectId.Should().StartWith("trial-balance:");
        binding.LedgerEntryIds.Should().ContainSingle();
        var original = await fixture.Service.GetPacketAsync(binding.BalanceProofSubjectId!, fixture.Scope);
        original!.LedgerAmount!.Status.Should().Be(EvidenceStatusDto.Ready);
        original.LedgerAmount.Amount.Should().Be(125m);
        original.LedgerAmount.Evidence.Where(item => item.Kind != "ledger-record")
            .Should().OnlyContain(item => item.SourceSubjectId != null && item.SourceScope == fixture.Scope);

        await fixture.AddJournalAsync(50m, retainSources: false);
        var historical = await fixture.Service.GetPacketAsync(binding.BalanceProofSubjectId!, fixture.Scope);
        historical!.LedgerAmount!.Amount.Should().Be(125m);
        historical.LedgerAmount.Status.Should().Be(EvidenceStatusDto.Ready);
        var latest = (await fixture.BindAsync()).Single(row => row.AccountName == "Cash");
        latest.Balance.Should().Be(175m);
        latest.BalanceProofSubjectId.Should().NotBe(binding.BalanceProofSubjectId);
        (await fixture.Service.GetPacketAsync(latest.BalanceProofSubjectId!, fixture.Scope))!.LedgerAmount!.Status
            .Should().Be(EvidenceStatusDto.Blocked, "every contributor needs verified retained support");
    }

    [Fact]
    public async Task ScopeAndPopulationTampering_CannotBorrowAnotherAmountProof()
    {
        using var fixture = new Fixture();
        await fixture.AddJournalAsync(9007199254740993.125m, retainSources: false);
        var row = (await fixture.BindAsync()).Single(candidate => candidate.AccountName == "Cash");
        row.BalanceExact.Should().Be("9007199254740993.125");
        (await fixture.Service.GetPacketAsync(row.BalanceProofSubjectId!, fixture.Scope with { TenantId = "other" })).Should().BeNull();
        (await fixture.Service.GetPacketAsync(row.BalanceProofSubjectId!, fixture.Scope with { CompanyId = "other" })).Should().BeNull();
        var first = fixture.Records[0];
        fixture.Records[0] = first with { CreatedAt = first.CreatedAt.AddSeconds(1) };
        (await fixture.Service.GetPacketAsync(row.BalanceProofSubjectId!, fixture.Scope)).Should().BeNull();
    }

    [Fact]
    public async Task SummaryMismatchAndAmbiguousPopulation_DoNotPublishBindings()
    {
        using var fixture = new Fixture();
        await fixture.AddJournalAsync(125m, retainSources: false);
        var summary = fixture.Summary();
        var changed = summary with { TrialBalance = summary.TrialBalance.Select(row => row with { Balance = row.Balance + 1m }).ToArray() };
        (await fixture.Service.BindAsync(changed, fixture.Scope.TenantId, fixture.Scope.CompanyId))
            .Should().OnlyContain(row => row.BalanceProofSubjectId == null);
        fixture.Records.Add(fixture.Records[0]);
        (await fixture.Service.BindAsync(summary, fixture.Scope.TenantId, fixture.Scope.CompanyId))
            .Should().OnlyContain(row => row.BalanceProofSubjectId == null);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "trial-balance-proof-" + Guid.NewGuid().ToString("N"));
        private readonly Mock<ILedgerJournalStore> _journals = new();
        private readonly Mock<ILedgerBookService> _books = new();
        private readonly Mock<IFundProfileTenancyRegistry> _tenancy = new();
        private readonly FileEvidenceArtifactStore _artifacts;
        public LedgerAmountScopeDto Scope { get; } = new("tenant", "company", "fund", Guid.NewGuid(), Guid.NewGuid());
        public List<LedgerJournalEntryRecord> Records { get; } = [];
        public TrialBalanceAmountProvenanceService Service { get; }

        public Fixture()
        {
            _artifacts = new FileEvidenceArtifactStore(_root, NullLogger<FileEvidenceArtifactStore>.Instance);
            var now = DateTimeOffset.UtcNow;
            _tenancy.Setup(service => service.ResolveAsync(Scope.FundProfileId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new FundProfileOwnership(Scope.FundProfileId, Scope.TenantId, Scope.CompanyId));
            _books.Setup(service => service.GetBookAsync(Scope.LedgerBookId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new LedgerBookDto(Scope.LedgerBookId, Scope.FundProfileId, Guid.NewGuid(), FundStructureNodeKindDto.Fund, "Fund", "USD", now, now));
            _journals.Setup(store => store.GetPeriodAsync(Scope.PeriodId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new LedgerAccountingPeriod(Scope.PeriodId, Scope.LedgerBookId, 2026, 10, "October", new(2026, 10, 1), new(2026, 10, 31), "HardClosed", now, now, 1));
            _journals.Setup(store => store.QueryAsync(It.Is<LedgerJournalEntryQuery>(query => query.LedgerBookId == Scope.LedgerBookId && query.PeriodId == Scope.PeriodId), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => Records);
            var posted = new PostedLedgerAmountProvenanceService(_journals.Object, _books.Object, _tenancy.Object, _artifacts);
            Service = new TrialBalanceAmountProvenanceService(_journals.Object, _books.Object, _tenancy.Object, posted);
        }

        public LedgerPeriodSummaryDto Summary() => new(Scope.PeriodId, Scope.LedgerBookId, 2026, 10, "October",
            PostgresLedgerBookService.ProjectTrialBalanceCheckpoint(Records), 0m, 0m, 0m, null, 0, LedgerPeriodSignoffStatusDto.Pending, DateTimeOffset.UtcNow);

        public Task<IReadOnlyList<LedgerPeriodTrialBalanceLineDto>> BindAsync()
            => Service.BindAsync(Summary(), Scope.TenantId, Scope.CompanyId);

        public async Task AddJournalAsync(decimal amount, bool retainSources)
        {
            var journalId = Guid.NewGuid();
            var debitId = Guid.NewGuid();
            var creditId = Guid.NewGuid();
            var references = new List<JournalEvidenceReference>();
            if (retainSources)
            {
                foreach (var (id, side) in new[] { (debitId, "debit"), (creditId, "credit") })
                {
                    var source = $"{journalId:D}:{id:D}:{side}";
                    var retainedId = PostedLedgerAmountProvenanceService.BuildRetainedSubjectId(source, Scope);
                    var intake = await _artifacts.WriteIntakeArtifactAsync(new EvidenceVaultIntakeRequestDto("ledger-amount", retainedId,
                        "upload", id + ".txt", Convert.ToBase64String(Encoding.UTF8.GetBytes(id.ToString())), "text/plain", "bank")
                    { TenantId = Scope.TenantId, Scope = Scope.CompanyId, Actor = "preparer" });
                    var review = await _artifacts.ReviewDocumentAsync(intake.VaultIdentity.VaultId, intake.Document!.DocumentId,
                        Scope.TenantId, Scope.CompanyId, new EvidenceVaultDocumentReviewRequestDto(EvidenceDocumentReviewStatusDto.Accepted, "reviewer",
                            ExtractionStatus: EvidenceExtractionStatusDto.Accepted)
                        { ConfirmedFields = [new("amount", amount.ToString(System.Globalization.CultureInfo.InvariantCulture), "reviewer", DateTimeOffset.UtcNow)] });
                    var identity = await _artifacts.TryGetVaultIdentityAsync(intake.VaultIdentity.VaultId, Scope.TenantId, Scope.CompanyId);
                    references.Add(new JournalEvidenceReference(id.ToString(), "vault:" + identity!.VaultId, "source-document", "bank", identity.RetainedAt,
                        "preparer", retainedId, identity.ContentHashSha256, "Reviewed source", ReviewStatus: "Accepted", ReviewedBy: "reviewer",
                        ReviewedAtUtc: review!.AuditEvent.RecordedAt, SubjectType: "ledger-amount"));
                }
            }
            var now = DateTimeOffset.UtcNow;
            var entry = new JournalEntry(journalId, now, "Funding",
                [new(debitId, journalId, now, new("Cash", LedgerAccountType.Asset), amount, 0m, "Funding"),
                 new(creditId, journalId, now, new("Equity", LedgerAccountType.Equity), 0m, amount, "Funding")],
                new JournalEntryMetadata(EvidenceReferences: references));
            Records.Add(new(entry, Guid.NewGuid(), Scope.PeriodId, null, null, Records.Count + 1, now));
        }

        public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
    }
}
