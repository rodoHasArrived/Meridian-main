using FluentAssertions;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Tenancy;
using Meridian.FinancialOperations.Intercompany;
using Meridian.Ledger;
using Meridian.Storage.FundStructure;
using Meridian.Storage.Ledger;
using Meridian.Tests.FinancialOperations.Intercompany;
using Meridian.Tests.TestSupport;
using Meridian.Ui.Shared.Services;
using Moq;

namespace Meridian.Tests.Ui;

/// <summary>
/// Entity A posted an intercompany loan receivable and entity B booked the payable. A reviewer in
/// tenant A links the manual counterpart, reprices the generated draft or reads the aged breaks.
/// Guards against any of those acting on a destination or source book whose retained owner is
/// another tenant, another company where the path compares company, or no registered owner at all:
/// nothing may be linked, saved, repriced or reported as reconciled across that boundary.
/// </summary>
public sealed class IntercompanyProposalDestinationAuthorityTests
{
    private static readonly DateOnly AsOf = new(2026, 7, 3);

    [Fact]
    public async Task Link_PostedCounterpartInADestinationOwnedByTheSourceTenantAndCompany_IsRetained()
    {
        var fixture = new AuthorityFixture();
        var request = fixture.LinkRequest();

        await fixture.Service.LinkAsync("tenant-a", fixture.ProposalId, request, "reviewer");

        fixture.Proposals.Verify(store => store.LinkAsync("tenant-a", fixture.ProposalId, request, "reviewer", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("foreign-tenant")]
    [InlineData("foreign-company")]
    [InlineData("unbound")]
    public async Task Link_DestinationBookOutsideTheSourceTenantOrCompany_IsRefusedAndNothingIsLinked(string ownership)
    {
        var fixture = new AuthorityFixture();
        fixture.Owners["target-fund"] = Owner("target-fund", ownership);

        var link = () => fixture.Service.LinkAsync("tenant-a", fixture.ProposalId, fixture.LinkRequest(), "reviewer");

        await link.Should().ThrowAsync<UnauthorizedAccessException>().WithMessage("*belongs to another tenant or company*");
        fixture.VerifyNothingLinked();
        (await fixture.Drafts.ListAsync("target-fund")).Should().BeEmpty();
    }

    [Theory]
    [InlineData("foreign-tenant")]
    [InlineData("foreign-company")]
    [InlineData("unbound")]
    public async Task Link_SourceBookOutsideTheSourceTenantOrCompany_IsRefusedAndNothingIsLinked(string ownership)
    {
        var fixture = new AuthorityFixture();
        fixture.Owners["source-fund"] = Owner("source-fund", ownership);

        var link = () => fixture.Service.LinkAsync("tenant-a", fixture.ProposalId, fixture.LinkRequest(), "reviewer");

        await link.Should().ThrowAsync<InvalidOperationException>().WithMessage("*book owner differs from the approved parties*");
        fixture.VerifyNothingLinked();
    }

    [Fact]
    public async Task Refresh_DestinationOwnedByTheCallingTenant_PassesTheOwnershipGateAndReachesTheRepricingRules()
    {
        var fixture = new AuthorityFixture();
        var delivered = await fixture.DeliverGeneratedDraftAsync();

        var preview = () => fixture.Service.RefreshPreviewAsync("tenant-a", fixture.ProposalId);
        var refresh = () => fixture.Service.RefreshAsync("tenant-a", fixture.ProposalId,
            new(Guid.NewGuid(), delivered.Version, "reviewed-preview-fingerprint", "Reprice at the approved rate"), "reviewer");

        // A same-currency loan needs no repricing; reaching this rule shows the caller was accepted as the owner.
        await preview.Should().ThrowAsync<InvalidOperationException>().WithMessage("*requires no foreign-currency repricing*");
        await refresh.Should().ThrowAsync<InvalidOperationException>().WithMessage("*requires no foreign-currency repricing*");
        fixture.Fx.Verify(authority => authority.AcquireAuthorityLeaseAsync(It.IsAny<CancellationToken>()), Times.Once);
        (await fixture.StoredDraftsAsync()).Should().ContainSingle().Which.Should().BeEquivalentTo(delivered);
    }

    [Theory]
    [InlineData("foreign-tenant")]
    [InlineData("unbound")]
    public async Task Refresh_DestinationBookOutsideTheCallingTenant_IsRefusedBeforeFxAuthorityAndTheDraftIsUntouched(string ownership)
    {
        var fixture = new AuthorityFixture();
        var delivered = await fixture.DeliverGeneratedDraftAsync();
        fixture.Owners["target-fund"] = Owner("target-fund", ownership);

        var preview = () => fixture.Service.RefreshPreviewAsync("tenant-a", fixture.ProposalId);
        var refresh = () => fixture.Service.RefreshAsync("tenant-a", fixture.ProposalId,
            new(Guid.NewGuid(), delivered.Version, "reviewed-preview-fingerprint", "Reprice at the approved rate"), "reviewer");

        await preview.Should().ThrowAsync<UnauthorizedAccessException>().WithMessage("*destination is outside the current tenant*");
        await refresh.Should().ThrowAsync<UnauthorizedAccessException>().WithMessage("*destination book is unavailable*");
        fixture.Fx.Verify(authority => authority.AcquireAuthorityLeaseAsync(It.IsAny<CancellationToken>()), Times.Never);
        fixture.Fx.Verify(authority => authority.ResolveAsync(It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Never);
        (await fixture.StoredDraftsAsync()).Should().ContainSingle().Which.Should().BeEquivalentTo(delivered);
    }

    [Theory]
    [InlineData("foreign-tenant")]
    [InlineData("unbound")]
    public async Task Breaks_SourceBookOutsideTheCallingTenant_AreRefused(string ownership)
    {
        var fixture = new AuthorityFixture();
        fixture.DeliverPostedCounterpart();
        fixture.Owners["source-fund"] = Owner("source-fund", ownership);

        var breaks = () => fixture.Service.BreaksAsync("tenant-a", IntercompanyProposalTestData.BookA, AsOf);

        await breaks.Should().ThrowAsync<UnauthorizedAccessException>().WithMessage("*source book is outside the current tenant*");
    }

    [Theory]
    [InlineData("owned", null)]
    [InlineData("foreign-tenant", "AmountOrIdentityMismatch")]
    [InlineData("foreign-company", "AmountOrIdentityMismatch")]
    [InlineData("unbound", "AmountOrIdentityMismatch")]
    public async Task Breaks_ReciprocalCounterpartInABookOutsideTheSourceTenantOrCompany_IsNotAcceptedAsReconciled(
        string ownership, string? expectedClassification)
    {
        var fixture = new AuthorityFixture();
        var counterpart = fixture.DeliverPostedCounterpart();
        if (ownership != "owned")
            fixture.Owners["target-fund"] = Owner("target-fund", ownership);

        var breaks = await fixture.Service.BreaksAsync("tenant-a", IntercompanyProposalTestData.BookA, AsOf);

        if (expectedClassification is null)
        {
            // Same amounts, same agreement and transaction identity, same tenant and company: reconciled.
            breaks.Should().BeEmpty();
            return;
        }
        var reported = breaks.Should().ContainSingle().Subject;
        reported.ProposalId.Should().Be(fixture.ProposalId);
        reported.Classification.Should().Be(expectedClassification);
        reported.Detail.Should().Contain("book owner differs from the approved parties");
        reported.TargetJournalId.Should().Be(counterpart);
    }

    private static FundProfileOwnership? Owner(string fund, string ownership) => ownership switch
    {
        "foreign-tenant" => new(fund, "tenant-b", "company-a"),
        "foreign-company" => new(fund, "tenant-a", "company-b"),
        _ => null
    };

    private sealed class AuthorityFixture
    {
        private readonly List<LedgerJournalEntryRecord> _source = [];
        private readonly List<LedgerJournalEntryRecord> _target = [];
        private readonly ResolvedIntercompanyAccountingArrangementDto _agreement = IntercompanyProposalTestData.Agreement();
        private readonly LedgerJournalEntryRecord _manualCounterpart;
        private IntercompanyProposalDto _proposal;

        internal AuthorityFixture()
        {
            var source = Record(IntercompanyProposalTestData.BookA, 100m);
            _source.Add(source);
            _manualCounterpart = Record(IntercompanyProposalTestData.BookB, -100m);
            _target.Add(_manualCounterpart);
            var proposal = IntercompanyProposalTestData.Proposal();
            _proposal = proposal with { Source = proposal.Source with { SourceJournalId = source.Entry.JournalEntryId } };

            Proposals.Setup(store => store.GetAsync("tenant-a", ProposalId, It.IsAny<CancellationToken>())).ReturnsAsync(() => _proposal);
            Proposals.Setup(store => store.ListAsync("tenant-a", IntercompanyProposalTestData.BookA, It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => [_proposal]);
            var sourceBook = new LedgerBookRecord(IntercompanyProposalTestData.BookA, "source-fund", IntercompanyProposalTestData.EntityA,
                FundStructureNodeKindDto.Entity, "Source", "USD", IntercompanyProposalTestData.Now, IntercompanyProposalTestData.Now);
            var journals = new Mock<ILedgerJournalStore>();
            journals.Setup(store => store.GetLedgerBookAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid book, CancellationToken _) => book == IntercompanyProposalTestData.BookA ? sourceBook
                    : book == IntercompanyProposalTestData.BookB ? IntercompanyProposalTestData.TargetBook() : null);
            journals.Setup(store => store.QueryAsync(It.IsAny<LedgerJournalEntryQuery>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((LedgerJournalEntryQuery query, CancellationToken _) =>
                    (query.LedgerBookId == IntercompanyProposalTestData.BookA ? _source : _target).ToArray());
            var arrangements = new Mock<IIntercompanyAccountingArrangementStore>();
            arrangements.Setup(store => store.GetAsync("tenant-a", IntercompanyProposalTestData.AccountingArrangementId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new IntercompanyAccountingArrangementHistoryDto([new(Guid.NewGuid(), "synthetic", IntercompanyAccountingArrangementActionDto.Approve,
                    "reviewer", IntercompanyProposalTestData.Now, "Approved synthetic source", _agreement.Arrangement)]));
            var owners = new Mock<IFundProfileTenancyRegistry>();
            owners.Setup(registry => registry.ResolveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string fund, CancellationToken _) => Owners.GetValueOrDefault(fund));
            Fx.Setup(authority => authority.AcquireAuthorityLeaseAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(EmptyLedgerAuthorityLease.Instance);
            var configuration = Mock.Of<IAccountingConfigurationService>();
            var workbench = new ManualJournalEntryWorkbenchService(Drafts, configuration, new InMemoryAccountingActionAuditStore(),
                journalStore: journals.Object, mutationRecovery: new InMemoryManualJournalMutationRecoveryStore());
            Service = new IntercompanyProposalDeliveryService(Proposals.Object,
                IntercompanyArrangementTestData.Service(arrangements.Object, _agreement), journals.Object,
                Mock.Of<IIntercompanyAccountBindingStore>(), configuration, Drafts, workbench, owners.Object, Fx.Object, new FixtureClock());
        }

        internal IntercompanyProposalDeliveryService Service { get; }
        internal Mock<IIntercompanyProposalStore> Proposals { get; } = new();
        internal Mock<IConsolidationFxAuthority> Fx { get; } = new();
        internal InMemoryManualJournalEntryDraftStore Drafts { get; } = new();
        internal Guid ProposalId => _proposal.Source.ProposalId;
        /// <summary>The retained owner of each fund profile; a null value is a book with no registered owner.</summary>
        internal Dictionary<string, FundProfileOwnership?> Owners { get; } = new(StringComparer.Ordinal)
        {
            ["source-fund"] = new("source-fund", "tenant-a", "company-a"),
            ["target-fund"] = new("target-fund", "tenant-a", "company-a")
        };

        internal LinkIntercompanyCounterpartRequest LinkRequest() => new(Guid.NewGuid(), IntercompanyProposalTestData.BookB,
            _manualCounterpart.Entry.JournalEntryId, "Reviewed the manually booked payable");

        /// <summary>The worker delivered its generated draft into the destination review queue.</summary>
        internal async Task<ManualJournalEntryDraftDto> DeliverGeneratedDraftAsync()
        {
            var draft = IntercompanyProposalDraftBuilder.Build(_proposal, _agreement, IntercompanyProposalTestData.TargetBook(),
                IntercompanyProposalTestData.Period(), [], IntercompanyProposalTestData.TargetBindings(), IntercompanyProposalTestData.Chart(),
                null, IntercompanyProposalTestData.Now) with
            { TenantId = "tenant-a", CompanyId = "company-a" };
            await Drafts.SaveAsync(draft);
            _proposal = _proposal with
            {
                Status = IntercompanyProposalStatusDto.Drafted,
                TargetBookId = IntercompanyProposalTestData.BookB,
                TargetJournalId = draft.JournalEntryId
            };
            return (await StoredDraftsAsync()).Should().ContainSingle().Subject;
        }

        /// <summary>The delivered counterpart was reviewed and posted in the destination book.</summary>
        internal Guid DeliverPostedCounterpart()
        {
            _proposal = _proposal with
            {
                Status = IntercompanyProposalStatusDto.Drafted,
                TargetBookId = IntercompanyProposalTestData.BookB,
                TargetJournalId = _manualCounterpart.Entry.JournalEntryId
            };
            return _manualCounterpart.Entry.JournalEntryId;
        }

        internal Task<IReadOnlyList<ManualJournalEntryDraftDto>> StoredDraftsAsync()
            => Drafts.ListAsync("target-fund", tenantId: "tenant-a", companyId: "company-a");

        internal void VerifyNothingLinked()
        {
            Proposals.Verify(store => store.LinkAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<LinkIntercompanyCounterpartRequest>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
            Proposals.Verify(store => store.LinkRejectedDraftAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<LinkIntercompanyCounterpartRequest>(),
                It.IsAny<string>(), It.IsAny<RejectedIntercompanyDraftEvidenceDto>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        private LedgerJournalEntryRecord Record(Guid book, decimal signed)
        {
            var journal = Guid.NewGuid();
            var source = book == IntercompanyProposalTestData.BookA;
            var timestamp = new DateTimeOffset(IntercompanyProposalTestData.Date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
            var dimensions = new LedgerLineDimensionSet(
                EntityId: (source ? IntercompanyProposalTestData.EntityA : IntercompanyProposalTestData.EntityB).ToString("D"),
                CounterpartyId: (source ? IntercompanyProposalTestData.EntityB : IntercompanyProposalTestData.EntityA).ToString("D"))
            {
                IntercompanyAgreementId = _agreement.CanonicalAgreementId,
                IntercompanyAgreementVersion = _agreement.CanonicalAgreementVersion,
                AccountingArrangementId = IntercompanyProposalTestData.AccountingArrangementId.ToString("D"),
                AccountingArrangementRevisionId = IntercompanyProposalTestData.RevisionId.ToString("D"),
                ChartAccountNodeId = source ? "a-receivable" : "b-payable",
                IntercompanyTransactionId = "invoice-100"
            };
            var entry = new JournalEntry(journal, timestamp, "Posted loan drawdown", [
                new(Guid.NewGuid(), journal, timestamp,
                    new(source ? "Assets:Due from B" : "Liabilities:Due to A", source ? LedgerAccountType.Asset : LedgerAccountType.Liability),
                    Math.Max(0m, signed), Math.Max(0m, -signed), "Posted loan drawdown", dimensions),
                new(Guid.NewGuid(), journal, timestamp, new("Assets:Cash", LedgerAccountType.Asset),
                    Math.Max(0m, -signed), Math.Max(0m, signed), "Posted loan drawdown")],
                new JournalEntryMetadata(EffectiveDate: IntercompanyProposalTestData.Date));
            return new(entry, book, Guid.NewGuid(), null, null, 1, timestamp);
        }
    }

    private sealed class FixtureClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 7, 3, 0, 0, 0, TimeSpan.Zero);
    }
}
