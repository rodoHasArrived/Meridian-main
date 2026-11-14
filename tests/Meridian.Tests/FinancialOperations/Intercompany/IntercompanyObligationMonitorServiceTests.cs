using Meridian.Application.FundStructure;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Services;
using Meridian.FinancialOperations.Intercompany;
using Meridian.Storage.Ledger;
using Moq;

namespace Meridian.Tests.FinancialOperations.Intercompany;

public sealed class IntercompanyObligationMonitorServiceTests
{
    [Fact]
    public async Task ReadsNativeReviewedScheduleWithoutCreatingAttributionOrPosting()
    {
        var fixture = new Fixture();
        var result = await fixture.RunAsync();
        Assert.Equal("Current", result.Status);
        Assert.True(result.IsComplete);
        Assert.Empty(result.Findings);
        Assert.Equal(fixture.Row.IntercompanyAgreementPayloadHash,
            Assert.Single(result.BalanceSchedule!.Rows).IntercompanyAgreementPayloadHash);
        Assert.Contains(result.SourceVersions, x => x.SourceKind == "balance-schedule");
        Assert.Contains(result.SourceVersions, x => x.SourceKind == "legal-agreement");
        Assert.Contains(result.SourceVersions, x => x.SourceKind == "entity-lifecycle");
        fixture.Journals.Verify(x => x.AppendAsync(It.IsAny<LedgerJournalEntryWrite>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ApproachingExpiryAndExpiredExposureRemainAdvisory()
    {
        var fixture = new Fixture();
        var expiring = fixture.Revision with
        { Terms = fixture.Revision.Terms with { EffectiveTo = Fixture.AsOf.AddDays(20) } };
        fixture.Detail = fixture.Detail with
        {
            ApplicableRevision = expiring,
            Revisions = [expiring]
        };
        Assert.Contains((await fixture.RunAsync()).Findings, x => x.Code == "intercompany.agreement.expiring");
        fixture.Detail = fixture.Detail with { EffectiveStatus = IntercompanyAgreementStatusDto.Expired };
        var expired = await fixture.RunAsync();
        Assert.Contains(expired.Findings, x => x.Code == "intercompany.agreement.expired-exposure");
        Assert.True(expired.BalanceSchedule!.Rows[0].MappingVerified);
        Assert.Equal(100m, expired.BalanceSchedule.Rows[0].TransactionSignedBalance);
    }

    [Fact]
    public async Task MissingNativeReferenceIsUnknownRatherThanInventingCoverage()
    {
        var fixture = new Fixture();
        fixture.Row = fixture.Row with
        {
            MappingVerified = false,
            IntercompanyAgreementId = null,
            IntercompanyAgreementVersion = null,
            IntercompanyAgreementPayloadHash = null
        };
        var result = await fixture.RunAsync();
        Assert.Equal("Partial", result.Status);
        Assert.Contains(result.Findings, x => x.Code == "intercompany.coverage.unmapped");
        Assert.Contains(result.Findings, x => x.Code == "intercompany.agreement.reference-unknown");
        Assert.False(result.IsComplete);
    }

    [Fact]
    public async Task ChangedCanonicalPayloadHashCannotBeTreatedAsApprovedLegalCoverage()
    {
        var fixture = new Fixture();
        fixture.Row = fixture.Row with { IntercompanyAgreementPayloadHash = Fixture.Hash("unrelated-legal") };
        var result = await fixture.RunAsync();
        Assert.Contains(result.Findings, x => x.Code == "intercompany.agreement.reference-invalid");
        Assert.False(result.IsComplete);
    }

    [Theory]
    [InlineData(LegalEntityLifecycleStatusDto.WindingDown)]
    [InlineData(LegalEntityLifecycleStatusDto.Dissolved)]
    [InlineData(LegalEntityLifecycleStatusDto.Merged)]
    public async Task InactiveLifecycleDoesNotRemoveResidualPostedBalances(LegalEntityLifecycleStatusDto status)
    {
        var fixture = new Fixture { LifecycleStatus = status };
        var result = await fixture.RunAsync();
        Assert.Contains(result.Findings, x => x.Code == "intercompany.lifecycle.exposure" && x.EntityId == fixture.EntityId);
        Assert.Single(result.BalanceSchedule!.Rows);
    }

    [Fact]
    public async Task MissingLifecycleAndPreBaselineDatesStayUnknown()
    {
        var fixture = new Fixture { MissingLifecycle = true };
        Assert.Contains((await fixture.RunAsync()).Findings, x => x.Code == "intercompany.lifecycle.unknown");
        fixture.MissingLifecycle = false;
        fixture.LifecycleFrom = new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero);
        var prior = await fixture.RunAsync();
        Assert.Contains(prior.Findings, x => x.Code == "intercompany.lifecycle.unknown");
        Assert.False(prior.IsComplete);
    }

    [Fact]
    public async Task GuaranteesAreDisclosedButCannotSatisfyOrdinaryBalanceCoverage()
    {
        var fixture = new Fixture();
        fixture.Detail = fixture.Detail with { AgreementType = IntercompanyAgreementTypeDto.Guarantee };
        var result = await fixture.RunAsync();
        Assert.Single(result.Guarantees);
        Assert.Contains(result.Findings, x => x.Code == "intercompany.agreement.reference-invalid");
    }

    [Fact]
    public async Task AgreementPagesAreExhaustedSoGuaranteesBeyondFirstPageRemainVisible()
    {
        var fixture = new Fixture();
        var guarantee = fixture.Detail with { AgreementId = Guid.NewGuid(), AgreementType = IntercompanyAgreementTypeDto.Guarantee };
        fixture.Agreements.Setup(x => x.ListAsync(It.IsAny<IntercompanyAgreementCallContext>(),
            It.IsAny<IntercompanyAgreementQueryDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IntercompanyAgreementCallContext _, IntercompanyAgreementQueryDto query, CancellationToken _) =>
                query.Cursor is null ? new IntercompanyAgreementListDto([fixture.Detail], "second-page") : new([guarantee], null));
        Assert.Single((await fixture.RunAsync()).Guarantees);
    }

    [Fact]
    public async Task ChangedNativeScheduleDuringEvaluationIsStale()
    {
        var fixture = new Fixture();
        fixture.Schedules.SetupSequence(x => x.GetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<Guid?>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(fixture.Schedule()).ReturnsAsync(fixture.Schedule() with { ContentHash = Fixture.Hash("new-source") });
        var result = await fixture.RunAsync();
        Assert.Equal("Stale", result.Status);
        Assert.False(result.IsComplete);
        Assert.Contains(result.Findings, x => x.Code == "intercompany.monitor.stale");
    }

    [Fact]
    public async Task ChangedLegalAggregateDuringEvaluationIsStale()
    {
        var fixture = new Fixture();
        var calls = 0;
        fixture.Agreements.Setup(x => x.ListAsync(It.IsAny<IntercompanyAgreementCallContext>(),
            It.IsAny<IntercompanyAgreementQueryDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new IntercompanyAgreementListDto([fixture.Detail with { AggregateVersion = ++calls > 2 ? 5 : 4 }], null));
        Assert.Equal("Stale", (await fixture.RunAsync()).Status);
    }

    [Fact]
    public async Task AccessFailureDoesNotDiscloseScheduleLegalPartiesOrLifecycle()
    {
        var fixture = new Fixture();
        fixture.Access.Setup(x => x.CanAccessEntitiesAsync(It.IsAny<IntercompanyAgreementCallContext>(),
            It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        var result = await fixture.RunAsync();
        Assert.Equal("Unavailable", result.Status);
        Assert.Null(result.BalanceSchedule);
        Assert.Empty(result.Guarantees);
        Assert.DoesNotContain(fixture.Detail.Name, Assert.Single(result.Findings).Message);
        fixture.Lifecycle.Verify(x => x.GetAsync(It.IsAny<Guid>(), It.IsAny<long?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UnavailableAuthorityDoesNotReportEveryBalanceUncovered()
    {
        var fixture = new Fixture();
        fixture.Schedules.Setup(x => x.GetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<Guid?>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>())).ThrowsAsync(new IOException("Provider unavailable"));
        var result = await fixture.RunAsync();
        Assert.Equal("Unavailable", result.Status);
        Assert.False(result.IsComplete);
        Assert.DoesNotContain(result.Findings, x => x.Code == "intercompany.coverage.unmapped");
    }

    [Fact]
    public async Task EntityAssertionPreservesNativeCounterpartEvidenceWithoutSilentFiltering()
    {
        var fixture = new Fixture();
        var reciprocal = fixture.Row with
        {
            EntityId = fixture.CounterpartyId,
            CounterpartyEntityId = fixture.EntityId,
            Role = IntercompanyAccountRoleDto.Payable,
            TransactionSignedBalance = -100m,
            FunctionalSignedBalance = -100m
        };
        fixture.Schedules.Setup(x => x.GetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<Guid?>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(fixture.Schedule() with { Rows = [fixture.Row, reciprocal] });
        var result = await fixture.Service.EvaluateAsync(fixture.Context, fixture.Company,
            fixture.Request with { EntityId = fixture.EntityId });
        Assert.Equal(2, result.BalanceSchedule!.Rows.Count);
        Assert.Contains(result.BalanceSchedule.Rows, row => row.EntityId == fixture.CounterpartyId);
    }

    [Fact]
    public async Task DifferentCompanyOrUnrelatedEntityCannotBroadenObservationScope()
    {
        var fixture = new Fixture();
        var unrelated = await fixture.Service.EvaluateAsync(fixture.Context, fixture.Company,
            fixture.Request with { EntityId = Guid.NewGuid() });
        Assert.Equal("Unavailable", unrelated.Status);
        Assert.Null(unrelated.BalanceSchedule);
        fixture.Schedules.Setup(x => x.GetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<Guid?>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(fixture.Schedule() with { CompanyId = "another-company" });
        Assert.Equal("Unavailable", (await fixture.RunAsync()).Status);
    }

    [Fact]
    public async Task ExplicitDateAndConcreteScopeAreRequired()
    {
        var fixture = new Fixture();
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Service.EvaluateAsync(fixture.Context, fixture.Company,
            new(fixture.Fund, fixture.Book.LedgerBookId, default)));
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Service.EvaluateAsync(fixture.Context, "all", fixture.Request));
    }

    private sealed class Fixture
    {
        public static readonly DateOnly AsOf = new(2026, 11, 13);
        public string Fund { get; } = "synthetic-fund";
        public string Company { get; } = "company-a";
        public IntercompanyAgreementCallContext Context { get; } = new("tenant-a", "reader");
        public Guid EntityId { get; } = Guid.NewGuid();
        public Guid CounterpartyId { get; } = Guid.NewGuid();
        public LedgerBookRecord Book { get; }
        public IntercompanyAgreementRevisionDto Revision { get; }
        public IntercompanyAgreementDetailDto Detail { get; set; }
        public IntercompanyBalanceScheduleRowDto Row { get; set; }
        public LegalEntityLifecycleStatusDto LifecycleStatus { get; set; } = LegalEntityLifecycleStatusDto.Active;
        public bool MissingLifecycle { get; set; }
        public DateTimeOffset LifecycleFrom { get; set; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public Mock<IIntercompanyBalanceScheduleSource> Schedules { get; } = new();
        public Mock<IIntercompanyAgreementService> Agreements { get; } = new();
        public Mock<IEntityLifecycleService> Lifecycle { get; } = new();
        public Mock<ILedgerJournalStore> Journals { get; } = new();
        public Mock<IIntercompanyAgreementAccessAuthority> Access { get; } = new();
        public IntercompanyObligationMonitorService Service { get; }
        public IntercompanyObligationMonitorRequestDto Request => new(Fund, Book.LedgerBookId, AsOf);
        public Fixture()
        {
            var now = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
            Book = new(Guid.NewGuid(), Fund, EntityId, FundStructureNodeKindDto.Entity, "Synthetic source", "USD", now, now);
            var legalId = Guid.NewGuid();
            var draft = new IntercompanyAgreementDraftDto("SYN-LOAN", "Synthetic intercompany loan", EntityId, "Lender",
                CounterpartyId, "Borrower", IntercompanyAgreementTypeDto.Loan, "USD", new(2026, 1, 1), new(2027, 1, 1));
            Revision = new(legalId, 1, null, null, IntercompanyAgreementActionDto.Activate, draft, Guid.NewGuid(),
                Hash("legal-v1"), ["author"], "submitter", "reviewer", now, "Synthetic reviewed source");
            Detail = new(legalId, 4, draft.Code, draft.Name, EntityId, draft.PartyOneRole, CounterpartyId, draft.PartyTwoRole,
                draft.AgreementType, "USD", AsOf, IntercompanyAgreementStatusDto.Active, 1, Revision, [], [Revision], [],
                new(false, false, false, false, false, false, false));
            Row = new(Book.LedgerBookId, EntityId, CounterpartyId, Guid.NewGuid(), Guid.NewGuid(), "Loan", "receivable-node",
                IntercompanyAccountRoleDto.Receivable, "USD", "USD", 100m, 100m, true, [Guid.NewGuid()], [Guid.NewGuid().ToString("D")])
            { IntercompanyAgreementId = legalId, IntercompanyAgreementVersion = 1, IntercompanyAgreementPayloadHash = Revision.PayloadHash };
            Schedules.Setup(x => x.GetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid?>(),
                It.IsAny<DateOnly>(), It.IsAny<CancellationToken>())).ReturnsAsync(() => Schedule());
            Agreements.Setup(x => x.ListAsync(It.IsAny<IntercompanyAgreementCallContext>(), It.IsAny<IntercompanyAgreementQueryDto>(),
                It.IsAny<CancellationToken>())).ReturnsAsync(() => new IntercompanyAgreementListDto([Detail], null));
            Agreements.Setup(x => x.GetAsync(It.IsAny<IntercompanyAgreementCallContext>(), It.IsAny<Guid>(), It.IsAny<DateOnly?>(),
                It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((IntercompanyAgreementCallContext _, Guid _, DateOnly? asOf, DateTimeOffset? knownAt, CancellationToken _) =>
                {
                    var date = asOf ?? AsOf;
                    var timeline = IntercompanyAgreementTimeline.Resolve(Detail.Revisions, date, knownAt);
                    return Detail with { AsOf = date, EffectiveStatus = timeline.Status, ApplicableRevision = timeline.Revision };
                });
            Journals.Setup(x => x.GetLedgerBookAsync(Book.LedgerBookId, It.IsAny<CancellationToken>())).ReturnsAsync(() => Book);
            Access.Setup(x => x.CanAccessEntitiesAsync(It.IsAny<IntercompanyAgreementCallContext>(), It.IsAny<IReadOnlyList<Guid>>(),
                It.IsAny<CancellationToken>())).ReturnsAsync(true);
            Lifecycle.Setup(x => x.GetAsync(It.IsAny<Guid>(), It.IsAny<long?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid entity, long? _, CancellationToken _) => MissingLifecycle ? null : EntityLifecycleRevisionIntegrity.Seal(
                    new(Context.TenantId, entity, 1, now, "operator", "Synthetic baseline", "synthetic:evidence", "", "",
                        [new(entity, LifecycleFrom, null, LifecycleStatus, LegalEntityLifecycleEventKindDto.Other,
                            LifecycleStatus == LegalEntityLifecycleStatusDto.Merged ? (entity == EntityId ? CounterpartyId : EntityId) : null)])));
            Service = new(Schedules.Object, Agreements.Object, Lifecycle.Object, Journals.Object, Access.Object);
        }
        public static string Hash(string value) => Sha256Digest.ComputeUtf8(value);
        public IntercompanyBalanceScheduleDto Schedule() => new(Context.TenantId, Company, Fund, Book.LedgerBookId,
            AsOf, true, [Row], [], Hash("schedule-v1"));
        public Task<IntercompanyObligationMonitorDto> RunAsync() => Service.EvaluateAsync(Context, Company, Request);
    }
}
