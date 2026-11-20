using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Tenancy;
using Meridian.Storage.AssetOperations;
using Meridian.Storage.Ledger;
using Meridian.Ui.Shared.Services;
using NSubstitute;

namespace Meridian.Tests.AssetOperations;

public sealed class BookReceiptSourceTests
{
    [Fact]
    public async Task AcceptedScheduleUsesHistoricalHoldingAndKeepsMissingProviderEvidenceIncomplete()
    {
        var fixture = new Fixture();
        fixture.Snapshot = fixture.Snapshot with
        {
            PositionEconomicStates = [fixture.State, fixture.State with
        {
            EconomicStateId = Guid.NewGuid(), AsOfDate = new DateOnly(2026, 6, 20), ParAmount = 1000m, Version = 2
        }]
        };

        var result = await fixture.LoadAsync();

        var expectation = result.Expectations.Should().ContainSingle().Which;
        expectation.ExpectedAmount.Should().Be(25m);
        expectation.Quantity.Should().Be(500m);
        expectation.ProjectionRunId.Should().Be(fixture.Run.ProjectionRunId);
        expectation.TermsVersionId.Should().Be(fixture.Terms.TermsVersionId);
        expectation.EvidenceLinks.Should().Contain($"/api/workstation/assets/{fixture.SecurityId:D}/operations");
        result.Coverage.Single(row => row.Area == "Holdings").IsComplete.Should().BeTrue();
        result.Coverage.Single(row => row.Area == "Projections").IsComplete.Should().BeTrue();
        result.Coverage.Single(row => row.Area == "Receipts").IsComplete.Should().BeFalse();
        result.Coverage.Single(row => row.Area == "Postings").IsComplete.Should().BeFalse();
    }

    [Theory]
    [InlineData("foreign-tenant", "company")]
    [InlineData("tenant", "foreign-company")]
    public async Task ForeignOwnershipRefusesBeforeReadingHoldings(string tenant, string company)
    {
        var fixture = new Fixture { Owner = new FundProfileOwnership("fund", tenant, company) };
        await FluentActions.Awaiting(() => fixture.LoadAsync()).Should().ThrowAsync<UnauthorizedAccessException>();
        await fixture.Positions.DidNotReceive().GetBookAsync(Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>());
        await fixture.Projections.DidNotReceive().GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OwnershipRecordForAnotherFundCannotAuthorizeTheBook()
    {
        var fixture = new Fixture { Owner = new FundProfileOwnership("other-fund", "tenant", "company") };
        await FluentActions.Awaiting(() => fixture.LoadAsync()).Should().ThrowAsync<UnauthorizedAccessException>();
        await fixture.Positions.DidNotReceive().GetBookAsync(Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MissingOwnershipAuthorityIsIncompleteAndDoesNotReadHoldings()
    {
        var fixture = new Fixture();
        var result = await new BookReceiptSource(fixture.Positions, fixture.Projections).LoadAsync(fixture.Query);
        result.Expectations.Should().BeEmpty();
        result.Coverage.Should().OnlyContain(row => !row.IsComplete);
        await fixture.Positions.DidNotReceive().GetBookAsync(Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UnpublishedSecurityDoesNotBecomeAnExpectedSchedule()
    {
        var fixture = new Fixture { Detail = null };
        var result = await fixture.LoadAsync();
        result.Expectations.Should().BeEmpty();
        result.Coverage.Single(row => row.Area == "Projections").IsComplete.Should().BeFalse();
    }

    [Theory]
    [InlineData("Pending", "CashFlowProjectionRun", false)]
    [InlineData("Accepted", "Invoice", false)]
    [InlineData("Accepted", "CashFlowProjectionRun", true)]
    public async Task AcceptanceMustBindTheExactTypedSchedule(string status, string type, bool foreignSubject)
    {
        var fixture = new Fixture();
        fixture.Detail = fixture.Detail! with
        {
            CashFlowProjectionRuns = [fixture.Run with
        {
            RetainedEvidence = [fixture.Run.RetainedEvidence[0] with
            {
                ReviewStatus = status, SubjectType = type,
                SubjectId = foreignSubject ? Guid.NewGuid().ToString("D") : fixture.Run.ProjectionRunId.ToString("D")
            }]
        }]
        };
        var result = await fixture.LoadAsync();
        result.Expectations.Should().BeEmpty();
        result.Coverage.Single(row => row.Area == "Projections").IsComplete.Should().BeFalse();
    }

    [Fact]
    public async Task GenericAcceptedSecurityEvidenceDoesNotApproveScheduleEconomics()
    {
        var fixture = new Fixture();
        fixture.Detail = fixture.Detail! with
        {
            CashFlowProjectionRuns = [fixture.Run with
        {
            RetainedEvidence = [fixture.Run.RetainedEvidence[0] with { SubjectType = "Security", SubjectId = fixture.SecurityId.ToString("D") }]
        }]
        };
        (await fixture.LoadAsync()).Expectations.Should().BeEmpty();
    }

    [Fact]
    public async Task ForeignPositionOwnerAndMissingSizingBasisCannotSupplyExpectedAmounts()
    {
        var fixture = new Fixture();
        fixture.Snapshot = fixture.Snapshot with
        {
            BookPositions = [fixture.Position with
        {
            BookContext = fixture.Position.BookContext with { FundProfileId = "other-fund" }
        }]
        };
        var foreign = await fixture.LoadAsync();
        foreign.Expectations.Should().BeEmpty();
        foreign.Coverage.Single(row => row.Area == "Holdings").IsComplete.Should().BeFalse();
        await fixture.Projections.DidNotReceive().GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());

        var unsized = new Fixture();
        unsized.Detail = unsized.Detail! with { ProjectedCashFlows = [unsized.Flow with { PrincipalBasis = null }] };
        var result = await unsized.LoadAsync();
        result.Expectations.Should().BeEmpty();
        result.Coverage.Single(row => row.Area == "Projections").IsComplete.Should().BeFalse();
    }

    [Fact]
    public async Task FutureApprovalAndGeneratedRunsCannotEnterEarlierObservation()
    {
        var fixture = new Fixture();
        fixture.Detail = fixture.Detail! with
        {
            CashFlowProjectionRuns = [fixture.Run with
        {
            RetainedEvidence = [fixture.Run.RetainedEvidence[0] with
            {
                ReviewedAtUtc = Fixture.Timestamp.AddMonths(2), RetainedAtUtc = Fixture.Timestamp.AddMonths(2)
            }]
        }]
        };
        (await fixture.LoadAsync()).Expectations.Should().BeEmpty();
        fixture.Detail = fixture.Detail with { CashFlowProjectionRuns = [fixture.Run with { GeneratedAt = Fixture.Timestamp.AddMonths(2) }] };
        (await fixture.LoadAsync()).Expectations.Should().BeEmpty();
    }

    [Fact]
    public async Task FutureTermsAmendmentDoesNotRewriteEarlierObligation()
    {
        var fixture = new Fixture();
        var terms = fixture.Terms with
        {
            TermsVersionId = Guid.NewGuid(),
            TermsHash = "terms-amended",
            VersionNumber = 2,
            EffectiveDate = new DateOnly(2026, 6, 20),
            RecordedAt = Fixture.Timestamp.AddDays(19)
        };
        var amended = fixture.AcceptedRun(new DateOnly(2026, 6, 20)) with
        {
            ExtensionPayload = JsonSerializer.SerializeToElement(new { termsVersionId = terms.TermsVersionId })
        };
        fixture.Detail = fixture.Detail! with
        {
            TermsHistory = [fixture.Terms, terms],
            CashFlowProjectionRuns = [fixture.Run, amended],
            ProjectedCashFlows = [fixture.Flow, fixture.Flow with
            {
                ProjectionRunId = amended.ProjectionRunId, ProjectedCashFlowId = Guid.NewGuid(), Amount = 100m
            }]
        };
        var result = await fixture.LoadAsync();
        result.Expectations.Should().ContainSingle().Which.ExpectedAmount.Should().Be(25m);
        result.Expectations[0].TermsVersionId.Should().Be(fixture.Terms.TermsVersionId);
    }

    [Fact]
    public async Task AmbiguousTermsRequireExplicitProjectionLineage()
    {
        var fixture = new Fixture();
        fixture.Detail = fixture.Detail! with
        {
            TermsHistory = [fixture.Terms, fixture.Terms with
        {
            TermsVersionId = Guid.NewGuid(), VersionNumber = 2, TermsHash = "different-terms"
        }]
        };
        var result = await fixture.LoadAsync();
        result.Expectations.Should().BeEmpty();
        result.Coverage.Single(row => row.Area == "Projections").IsComplete.Should().BeFalse();
    }

    [Fact]
    public async Task AmendedPaymentDateAndSequencePreserveAccrualObligationIdentity()
    {
        var fixture = new Fixture();
        var original = fixture.Flow with { AccrualStartDate = new DateOnly(2026, 6, 1), AccrualEndDate = new DateOnly(2026, 6, 14) };
        fixture.Detail = fixture.Detail! with { ProjectedCashFlows = [original] };
        var first = await fixture.LoadAsync();
        var amended = fixture.AcceptedRun(new DateOnly(2026, 6, 20));
        fixture.Detail = fixture.Detail with
        {
            CashFlowProjectionRuns = [fixture.Run, amended],
            ProjectedCashFlows = [original, original with
            {
                ProjectionRunId = amended.ProjectionRunId, ProjectedCashFlowId = Guid.NewGuid(),
                DueDate = new DateOnly(2026, 6, 18), SequenceNumber = 10, Amount = 60m
            }]
        };
        var result = await fixture.LoadAsync();
        result.Expectations.Should().ContainSingle().Which.ObligationKey.Should().Be(first.Expectations[0].ObligationKey);
        result.Expectations[0].ExpectedAmount.Should().Be(30m);
        result.Expectations[0].DueDate.Should().Be(new DateOnly(2026, 6, 18));
        first.Expectations[0].ExpectedAmount.Should().Be(25m);
    }

    [Fact]
    public void StableSourceAndTerminalPrincipalKeysSurvivePaymentDateChanges()
    {
        var position = Guid.NewGuid();
        var original = new DateOnly(2026, 6, 15);
        var amended = new DateOnly(2026, 7, 15);
        BookReceiptSource.ObligationKey(position, "Principal", original, sourceObligationId: "repayment-1")
            .Should().Be(BookReceiptSource.ObligationKey(position, "Principal", amended, sourceObligationId: "repayment-1"));
        BookReceiptSource.ObligationKey(position, "Principal", original, isFinalPrincipal: true)
            .Should().Be(BookReceiptSource.ObligationKey(position, "Principal", amended, isFinalPrincipal: true));
    }

    [Fact]
    public async Task FutureAmortizationKeepsCurrentOwnershipShareForLaterCoupons()
    {
        var fixture = new Fixture();
        fixture.Detail = fixture.Detail! with
        {
            ProjectedCashFlows =
        [
            fixture.Flow with { DueDate = new DateOnly(2026, 7, 15) },
            fixture.Flow with { ProjectedCashFlowId = Guid.NewGuid(), SequenceNumber = 2,
                DueDate = new DateOnly(2026, 8, 15), PrincipalBasis = 800m, Amount = 40m }
        ]
        };
        var result = await fixture.LoadAsync();
        result.Expectations.Single(row => row.DueDate == new DateOnly(2026, 8, 15)).ExpectedAmount.Should().Be(20m);
    }

    [Fact]
    public async Task UnacceptedFutureSizingAnchorCannotInflateAcceptedLaterCoupon()
    {
        var fixture = new Fixture();
        var laterId = Guid.NewGuid();
        var later = fixture.Flow with
        {
            ProjectedCashFlowId = laterId,
            DueDate = new DateOnly(2026, 8, 15),
            PrincipalBasis = 800m,
            Amount = 40m,
            RetainedEvidence = [fixture.Run.RetainedEvidence[0] with
            {
                SubjectType = "ProjectedCashFlow", SubjectId = laterId.ToString("D")
            }]
        };
        fixture.Detail = fixture.Detail! with
        {
            CashFlowProjectionRuns = [fixture.Run with { RetainedEvidence = [] }],
            ProjectedCashFlows = [fixture.Flow with { DueDate = new DateOnly(2026, 7, 15) }, later]
        };
        var result = await fixture.LoadAsync();
        result.Expectations.Should().BeEmpty();
        result.Coverage.Single(row => row.Area == "Projections").IsComplete.Should().BeFalse();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FutureRecordedLineageOrTriggerCannotSelectTermsOrBypassHoldingSizing(bool futureRecorded)
    {
        var fixture = new Fixture();
        var trigger = fixture.State.SourceEvent! with
        {
            BookPositionId = fixture.Position.PositionId,
            SecurityId = fixture.SecurityId,
            OccurredAtUtc = futureRecorded ? Fixture.Timestamp : Fixture.Timestamp.AddMonths(2)
        };
        var lineage = new ProjectionLineageDto(fixture.Run.ProjectionRunId, null, "model", "1", "engine", "Base",
            fixture.Run.ProjectionAsOf, futureRecorded ? Fixture.Timestamp.AddMonths(2) : Fixture.Timestamp,
            "Servicer", "schedule", trigger, TermsHash: "future-terms")
        { BookPositionId = fixture.Position.PositionId };
        fixture.Snapshot = fixture.Snapshot with { ProjectionLineages = [lineage] };

        var result = await fixture.LoadAsync();
        result.Expectations.Should().BeEmpty();
        result.Coverage.Single(row => row.Area == "Projections").IsComplete.Should().BeFalse();
    }

    [Fact]
    public async Task AnotherPositionsAcceptedScheduleCannotBeScaledIntoThisBook()
    {
        var fixture = new Fixture();
        var foreignPositionId = Guid.NewGuid();
        var trigger = fixture.State.SourceEvent! with { BookPositionId = foreignPositionId, SecurityId = fixture.SecurityId };
        var lineage = new ProjectionLineageDto(fixture.Run.ProjectionRunId, null, "model", "1", "engine", "Base",
            fixture.Run.ProjectionAsOf, Fixture.Timestamp, "Servicer", "schedule", trigger, TermsHash: fixture.Terms.TermsHash)
        {
            BookPositionId = foreignPositionId
        };
        // The other book's binding is retained in the security history, outside this book snapshot.
        fixture.Detail = fixture.Detail! with { ProjectionLineages = [lineage] };
        (await fixture.LoadAsync()).Expectations.Should().BeEmpty();
    }

    [Fact]
    public async Task AcceptedExplicitPositionScheduleIsNotScaledTwice()
    {
        var fixture = new Fixture();
        fixture.Detail = fixture.Detail! with
        {
            ProjectedCashFlows = [fixture.Flow with
        {
            ExtensionPayload = JsonSerializer.SerializeToElement(new { bookPositionId = fixture.Position.PositionId })
        }]
        };
        (await fixture.LoadAsync()).Expectations.Should().ContainSingle().Which.ExpectedAmount.Should().Be(50m);
    }

    [Fact]
    public async Task FirstObservationOfMaturedPositionKeepsPrePaydownPrincipalDue()
    {
        var fixture = new Fixture();
        fixture.Snapshot = fixture.Snapshot with
        {
            BookPositions = [fixture.Position with { Status = "Matured", EffectiveTo = fixture.Flow.DueDate }],
            PositionEconomicStates = [fixture.State, fixture.State with
            {
                EconomicStateId = Guid.NewGuid(), AsOfDate = fixture.Flow.DueDate, ParAmount = 0m, Version = 2
            }]
        };
        fixture.Detail = fixture.Detail! with { ProjectedCashFlows = [fixture.Flow with { FlowType = "Maturity", Amount = 1000m }] };
        (await fixture.LoadAsync()).Expectations.Should().ContainSingle().Which.ExpectedAmount.Should().Be(500m);
    }

    [Fact]
    public async Task SameRunDuplicateSemanticObligationsAreIncompleteRatherThanCountedTwice()
    {
        var fixture = new Fixture();
        fixture.Detail = fixture.Detail! with
        {
            ProjectedCashFlows = [fixture.Flow, fixture.Flow with
        {
            ProjectedCashFlowId = Guid.NewGuid(), SequenceNumber = 2
        }]
        };
        var result = await fixture.LoadAsync();
        result.Expectations.Should().BeEmpty();
        result.Coverage.Single(row => row.Area == "Projections").IsComplete.Should().BeFalse();
    }

    private sealed class Fixture
    {
        public static readonly DateTimeOffset Timestamp = DateTimeOffset.Parse("2026-06-01T12:00:00Z");
        public Guid SecurityId { get; } = Guid.NewGuid();
        public Guid BookId { get; } = Guid.NewGuid();
        public PositionEconomicStateDto State { get; }
        public BookPositionDto Position { get; }
        public InstrumentPositionProjectionSnapshot Snapshot { get; set; }
        public AssetCashFlowProjectionRunDto Run { get; }
        public AssetProjectedCashFlowDto Flow { get; }
        public AssetTermsVersionDto Terms { get; }
        public AssetOperationsDetailDto? Detail { get; set; }
        public FundProfileOwnership Owner { get; set; } = new("fund", "tenant", "company");
        public IInstrumentPositionProjectionStore Positions { get; } = Substitute.For<IInstrumentPositionProjectionStore>();
        public IAssetOperationsProjectionStore Projections { get; } = Substitute.For<IAssetOperationsProjectionStore>();
        public BookReceiptQueryDto Query => new(new("tenant", "company", BookId), new DateOnly(2026, 6, 30));

        public Fixture()
        {
            var positionId = Guid.NewGuid();
            var roleId = Guid.NewGuid();
            var origin = new EconomicEventReferenceDto(Guid.NewGuid(), "Holding", 1,
                new DateOnly(2026, 6, 1), Timestamp, "Custody", "holding-1");
            State = new PositionEconomicStateDto(Guid.NewGuid(), positionId, new DateOnly(2026, 6, 1),
                "USD", 1, ParAmount: 500m, SourceEvent: origin, EvidenceLinks: ["evidence://holding/1"]);
            var book = new AccountingBookContextDto(BookId, "fund", Guid.NewGuid(), FundStructureNodeKindDto.Fund,
                "Book", "USD", AccountingBasisKindDto.Gaap, "policy", "1");
            Position = new BookPositionDto(positionId, SecurityId, roleId, book, "Long", "Active", new DateOnly(2026, 6, 1),
                CurrentEconomicState: State, OriginEvent: origin, EvidenceLinks: ["evidence://holding/1"]);
            var role = new InstrumentRoleDto(roleId, SecurityId, "fund", "Fund", "Holder", "Debit", "Asset", new DateOnly(2026, 6, 1));
            Snapshot = new InstrumentPositionProjectionSnapshot(SecurityId, [role], [Position], [State], [])
            {
                LedgerBookId = BookId,
                AsOfDate = Query.AsOfDate
            };
            var runId = Guid.NewGuid();
            var evidence = new RetainedEvidenceIdentityDto("schedule-1", "evidence://schedule/1", new string('a', 64),
                "Servicer", "schedule-1", "Accepted", "reviewer", Timestamp, new DateOnly(2026, 6, 1),
                1, Timestamp, "retainer", "CashFlowProjectionRun", runId.ToString("D"));
            Run = new AssetCashFlowProjectionRunDto(runId, SecurityId, new DateOnly(2026, 6, 1), "engine-v1",
                "Completed", Timestamp, "Servicer", "schedule-1")
            { RetainedEvidence = [evidence] };
            Flow = new AssetProjectedCashFlowDto(Guid.NewGuid(), runId, SecurityId, 1, "Coupon",
                new DateOnly(2026, 6, 15), 50m, "USD", "Expected", PrincipalBasis: 1000m);
            Terms = new AssetTermsVersionDto(Guid.NewGuid(), SecurityId, 1, "terms-v1", new DateOnly(2026, 6, 1),
                Timestamp, "Servicer", "terms-1", "Original terms");
            var subject = new AssetOperationSubjectDto(SecurityId, "Bond", "Synthetic bond", "TEST", []);
            var readiness = new AssetOperationsReadinessDto(SecurityId, "Ready", [], [], [], [], Timestamp, "Servicer", "schedule-1");
            Detail = new AssetOperationsDetailDto(subject, [Terms], [], [Run], [Flow], [], [], [], [], readiness, []);
        }

        public AssetCashFlowProjectionRunDto AcceptedRun(DateOnly asOf)
        {
            var id = Guid.NewGuid();
            var timestamp = new DateTimeOffset(asOf.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);
            return Run with
            {
                ProjectionRunId = id,
                ProjectionAsOf = asOf,
                GeneratedAt = timestamp,
                RetainedEvidence = [Run.RetainedEvidence[0] with { EvidenceId = $"run-{id:D}", SubjectId = id.ToString("D"),
                    ReviewedAtUtc = timestamp, RetainedAtUtc = timestamp }]
            };
        }

        public async Task<BookReceiptSourceSnapshotDto> LoadAsync()
        {
            Positions.GetBookAsync(BookId, Query.AsOfDate, Arg.Any<CancellationToken>()).Returns(new[] { Snapshot });
            Projections.GetAsync(SecurityId, Arg.Any<CancellationToken>()).Returns(Detail);
            var journals = Substitute.For<ILedgerJournalStore>();
            journals.GetLedgerBookAsync(BookId, Arg.Any<CancellationToken>()).Returns(new LedgerBookRecord(BookId, "fund",
                Position.BookContext.FundStructureNodeId, FundStructureNodeKindDto.Fund, "Book", "USD", Timestamp, Timestamp));
            var ownership = Substitute.For<IFundProfileTenancyRegistry>();
            ownership.ResolveAsync("fund", Arg.Any<CancellationToken>()).Returns(Owner);
            return await new BookReceiptSource(Positions, Projections, journals: journals, ownership: ownership).LoadAsync(Query);
        }
    }
}
