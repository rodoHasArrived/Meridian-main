using Meridian.Application.FundStructure;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Services;
using Meridian.FinancialOperations.Intercompany;
using Meridian.Storage.Ledger;
using Moq;

namespace Meridian.Tests.FinancialOperations.Intercompany;

public sealed class IntercompanyObligationExpiryChronologyTests
{
    [Theory]
    [InlineData(10)]
    [InlineData(20)]
    public async Task ApprovedContinuousExtensionSupersedesOriginalExpiry(int amendmentDay)
    {
        var fixture = new Fixture(20);
        fixture.Amend(amendmentDay, 100);

        var result = await fixture.RunAsync();

        Assert.DoesNotContain(result.Findings, IsExpiring);
        Assert.Equal("Current", result.Status);
        var row = Assert.Single(result.BalanceSchedule!.Rows);
        Assert.Equal(1, row.IntercompanyAgreementVersion);
        Assert.Equal(fixture.Revisions[0].PayloadHash, row.IntercompanyAgreementPayloadHash);
        Assert.True(row.MappingVerified);
        Assert.Equal(100m, row.TransactionSignedBalance);
    }

    [Fact]
    public async Task MultipleContinuousCutoversWarnAtResultingExpiryWithEffectiveRevision()
    {
        var fixture = new Fixture(10);
        fixture.Amend(5, 20);
        fixture.Amend(15, 25);

        var finding = Assert.Single((await fixture.RunAsync()).Findings, IsExpiring);

        Assert.Contains(Fixture.AsOf.AddDays(25).ToString("yyyy-MM-dd"), finding.Message);
        Assert.Equal(3, finding.AgreementVersion);
    }

    [Fact]
    public async Task ShorterFutureAmendmentBringsCoverageEndForward()
    {
        var fixture = new Fixture(100);
        fixture.Amend(5, 12);

        var finding = Assert.Single((await fixture.RunAsync()).Findings, IsExpiring);

        Assert.Contains(Fixture.AsOf.AddDays(12).ToString("yyyy-MM-dd"), finding.Message);
        Assert.Equal(2, finding.AgreementVersion);
    }

    [Fact]
    public async Task GapStillWarnsWhenAgreementReactivatesBeforeHorizon()
    {
        var fixture = new Fixture(10);
        fixture.Amend(11, 100);
        Assert.Equal(IntercompanyAgreementStatusDto.Active,
            IntercompanyAgreementTimeline.Resolve(fixture.Revisions, Fixture.AsOf.AddDays(30)).Status);

        var finding = Assert.Single((await fixture.RunAsync()).Findings, IsExpiring);

        Assert.Contains(Fixture.AsOf.AddDays(10).ToString("yyyy-MM-dd"), finding.Message);
        Assert.Equal(1, finding.AgreementVersion);
        fixture.Agreements.Verify(x => x.GetAsync(It.IsAny<IntercompanyAgreementCallContext>(), fixture.AgreementId,
            Fixture.AsOf.AddDays(11), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(IntercompanyAgreementProposalStateDto.Draft)]
    [InlineData(IntercompanyAgreementProposalStateDto.Submitted)]
    [InlineData(IntercompanyAgreementProposalStateDto.Rejected)]
    public async Task UnapprovedExtensionDoesNotExtendCoverage(IntercompanyAgreementProposalStateDto state)
    {
        var fixture = new Fixture(20);
        var now = DateTimeOffset.Parse("2026-11-13T00:00:00Z");
        fixture.Proposals = [new(Guid.NewGuid(), IntercompanyAgreementActionDto.Amend, state,
            fixture.Revisions[0].Terms with
            {
                Action = IntercompanyAgreementActionDto.Amend,
                EffectiveFrom = Fixture.AsOf.AddDays(10),
                EffectiveTo = Fixture.AsOf.AddDays(100)
            }, 1, ["author"], "author", now, Fixture.Hash("pending"), now, now, "Pending extension")];

        var finding = Assert.Single((await fixture.RunAsync()).Findings, IsExpiring);

        Assert.Contains(Fixture.AsOf.AddDays(20).ToString("yyyy-MM-dd"), finding.Message);
        Assert.Equal(1, finding.AgreementVersion);
    }

    [Fact]
    public async Task FutureTerminationEndsOpenEndedCoverageBeforeLaterAmendment()
    {
        var fixture = new Fixture(null);
        fixture.Amend(15, 100);
        fixture.Terminate(10);

        var finding = Assert.Single((await fixture.RunAsync()).Findings, IsExpiring);

        Assert.Contains("legal coverage ends on", finding.Message);
        Assert.Contains(Fixture.AsOf.AddDays(10).ToString("yyyy-MM-dd"), finding.Message);
        Assert.DoesNotContain("expires", finding.Message);
        Assert.Equal(1, finding.AgreementVersion);
    }

    [Fact]
    public async Task TerminationAtAmendmentCutoverEndsCoverage()
    {
        var fixture = new Fixture(10);
        fixture.Amend(10, 100);
        fixture.Terminate(10);

        var finding = Assert.Single((await fixture.RunAsync()).Findings, IsExpiring);

        Assert.Contains(Fixture.AsOf.AddDays(10).ToString("yyyy-MM-dd"), finding.Message);
    }

    [Fact]
    public async Task SameDateCorrectionUsesHighestApprovedVersion()
    {
        var fixture = new Fixture(20);
        fixture.Amend(10, 15);
        fixture.Amend(10, 100);

        Assert.DoesNotContain((await fixture.RunAsync()).Findings, IsExpiring);
    }

    [Theory]
    [InlineData(30, true)]
    [InlineData(31, false)]
    public async Task ExpiryHorizonIncludesItsExactEndDate(int expiryDay, bool expected)
    {
        var fixture = new Fixture(expiryDay);

        Assert.Equal(expected, (await fixture.RunAsync()).Findings.Any(IsExpiring));
    }

    [Fact]
    public async Task ExactAsOfExpiryRetainsExpiredExposureInsteadOfApproachingFinding()
    {
        var fixture = new Fixture(0);

        var result = await fixture.RunAsync();

        Assert.DoesNotContain(result.Findings, IsExpiring);
        Assert.Contains(result.Findings, x => x.Code == "intercompany.agreement.expired-exposure");
        Assert.True(Assert.Single(result.BalanceSchedule!.Rows).MappingVerified);
    }

    [Fact]
    public async Task ZeroHorizonDoesNotEvaluateFutureBoundaries()
    {
        var fixture = new Fixture(1);

        Assert.DoesNotContain((await fixture.RunAsync(0)).Findings, IsExpiring);
        fixture.Agreements.Verify(x => x.GetAsync(It.IsAny<IntercompanyAgreementCallContext>(), It.IsAny<Guid>(),
            It.IsAny<DateOnly?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("identity")]
    [InlineData("date")]
    [InlineData("version")]
    public async Task InconsistentBoundaryAuthorityIsUnavailable(string discrepancy)
    {
        var fixture = new Fixture(20);
        fixture.BoundaryProjection = date => discrepancy switch
        {
            "missing" => null,
            "identity" => fixture.Detail(date) with { AgreementId = Guid.NewGuid() },
            "date" => fixture.Detail(date) with { AsOf = Fixture.AsOf },
            _ => fixture.Detail(date) with { AggregateVersion = 99 }
        };

        var result = await fixture.RunAsync();

        Assert.Equal("Unavailable", result.Status);
        Assert.Null(result.BalanceSchedule);
        Assert.DoesNotContain(result.Findings, IsExpiring);
    }

    private static bool IsExpiring(IntercompanyObligationFindingDto finding) =>
        finding.Code == "intercompany.agreement.expiring";

    private sealed class Fixture
    {
        public static readonly DateOnly AsOf = new(2026, 11, 13);
        public static string Hash(string value) => Sha256Digest.ComputeUtf8(value);
        public Guid AgreementId { get; } = Guid.NewGuid();
        public List<IntercompanyAgreementRevisionDto> Revisions { get; } = [];
        public IReadOnlyList<IntercompanyAgreementProposalDto> Proposals { get; set; } = [];
        public Func<DateOnly, IntercompanyAgreementDetailDto?>? BoundaryProjection { get; set; }
        public Mock<IIntercompanyAgreementService> Agreements { get; } = new();
        private readonly IntercompanyObligationMonitorService _service;
        private readonly IntercompanyAgreementCallContext _context = new("synthetic-tenant", "reader");
        private readonly Guid _entity = Guid.NewGuid();
        private readonly Guid _counterparty = Guid.NewGuid();
        private readonly LedgerBookRecord _book;

        public Fixture(int? expiryDay)
        {
            var now = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
            _book = new(Guid.NewGuid(), "synthetic-fund", _entity, FundStructureNodeKindDto.Entity,
                "Synthetic source", "USD", now, now);
            var terms = new IntercompanyAgreementDraftDto("SYN-LOAN", "Synthetic agreement", _entity, "Lender",
                _counterparty, "Borrower", IntercompanyAgreementTypeDto.Loan, "USD", new(2026, 1, 1),
                expiryDay is { } day ? AsOf.AddDays(day) : null);
            Revisions.Add(new(AgreementId, 1, null, null, IntercompanyAgreementActionDto.Activate, terms,
                Guid.NewGuid(), Hash("reviewed-v1"), ["author"], "author", "reviewer", now, "Synthetic review"));
            var schedules = new Mock<IIntercompanyBalanceScheduleSource>();
            var row = new IntercompanyBalanceScheduleRowDto(_book.LedgerBookId, _entity, _counterparty,
                Guid.NewGuid(), Guid.NewGuid(), "Loan", "receivable", IntercompanyAccountRoleDto.Receivable,
                "USD", "USD", 100m, 100m, true, [Guid.NewGuid()], ["synthetic-line"])
            {
                IntercompanyAgreementId = AgreementId,
                IntercompanyAgreementVersion = 1,
                IntercompanyAgreementPayloadHash = Revisions[0].PayloadHash
            };
            schedules.Setup(x => x.GetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<Guid?>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new IntercompanyBalanceScheduleDto(_context.TenantId, "synthetic-company",
                    _book.FundProfileId, _book.LedgerBookId, AsOf, true, [row], [], Hash("posted-schedule")));
            Agreements.Setup(x => x.ListAsync(It.IsAny<IntercompanyAgreementCallContext>(),
                It.IsAny<IntercompanyAgreementQueryDto>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => new IntercompanyAgreementListDto([Detail(AsOf)], null));
            Agreements.Setup(x => x.GetAsync(It.IsAny<IntercompanyAgreementCallContext>(), It.IsAny<Guid>(),
                It.IsAny<DateOnly?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((IntercompanyAgreementCallContext _, Guid _, DateOnly? date, DateTimeOffset? _, CancellationToken _) =>
                    BoundaryProjection is { } projection ? projection(date ?? AsOf) : Detail(date ?? AsOf));
            var journals = new Mock<ILedgerJournalStore>();
            journals.Setup(x => x.GetLedgerBookAsync(_book.LedgerBookId, It.IsAny<CancellationToken>())).ReturnsAsync(_book);
            var access = new Mock<IIntercompanyAgreementAccessAuthority>();
            access.Setup(x => x.CanAccessEntitiesAsync(It.IsAny<IntercompanyAgreementCallContext>(),
                It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
            var lifecycle = new Mock<IEntityLifecycleService>();
            lifecycle.Setup(x => x.GetAsync(It.IsAny<Guid>(), It.IsAny<long?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid entity, long? _, CancellationToken _) => EntityLifecycleRevisionIntegrity.Seal(
                    new(_context.TenantId, entity, 1, now, "operator", "Synthetic baseline", "synthetic:evidence", "", "",
                        [new(entity, now, null, LegalEntityLifecycleStatusDto.Active, LegalEntityLifecycleEventKindDto.Other)])));
            _service = new(schedules.Object, Agreements.Object, lifecycle.Object, journals.Object, access.Object);
        }

        public void Amend(int startDay, int? endDay)
        {
            var previous = Revisions[^1];
            var version = previous.AgreementVersion + 1;
            Revisions.Add(previous with
            {
                AgreementVersion = version,
                PredecessorAgreementVersion = previous.AgreementVersion,
                BaseAgreementVersion = previous.AgreementVersion,
                Action = IntercompanyAgreementActionDto.Amend,
                PayloadHash = Hash($"reviewed-v{version}"),
                Terms = previous.Terms with
                {
                    Action = IntercompanyAgreementActionDto.Amend,
                    EffectiveFrom = AsOf.AddDays(startDay),
                    EffectiveTo = endDay is { } end ? AsOf.AddDays(end) : null
                }
            });
        }

        public void Terminate(int day)
        {
            var previous = Revisions[^1];
            Revisions.Add(previous with
            {
                AgreementVersion = previous.AgreementVersion + 1,
                Action = IntercompanyAgreementActionDto.Terminate,
                Terms = previous.Terms with
                { Action = IntercompanyAgreementActionDto.Terminate, TerminationEffectiveDate = AsOf.AddDays(day) }
            });
        }

        public IntercompanyAgreementDetailDto Detail(DateOnly date)
        {
            var initial = Revisions[0].Terms;
            var timeline = IntercompanyAgreementTimeline.Resolve(Revisions, date);
            return new(AgreementId, 4, initial.Code, initial.Name, _entity, initial.PartyOneRole, _counterparty,
                initial.PartyTwoRole, initial.AgreementType, initial.Currency, date, timeline.Status,
                Revisions.Max(x => x.AgreementVersion), timeline.Revision, Proposals, Revisions, [],
                new(false, false, false, false, false, false, false));
        }

        public Task<IntercompanyObligationMonitorDto> RunAsync(int horizon = 30) =>
            _service.EvaluateAsync(_context, "synthetic-company", new(_book.FundProfileId, _book.LedgerBookId, AsOf, horizon));
    }
}
