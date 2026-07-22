using System.Text.Json;
using FluentAssertions;
using Meridian.Application.DirectLending;
using Meridian.Contracts.DirectLending;

namespace Meridian.Tests.Application.DirectLending;

/// <summary>
/// Direct unit coverage for <see cref="DirectLendingEventRebuilder"/> event replay,
/// focused on the PIK lifecycle: the toggle event must replay (instead of throwing
/// "Unsupported direct-lending event type") and PIK accrual events must capitalize
/// interest into principal.
/// </summary>
public sealed class DirectLendingEventRebuilderTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static LoanEventLineageDto Event(
        long version,
        string eventType,
        DateOnly effectiveDate,
        object payload) => new(
        EventId: Guid.NewGuid(),
        AggregateVersion: version,
        EventType: eventType,
        EventSchemaVersion: 1,
        EffectiveDate: effectiveDate,
        RecordedAt: DateTimeOffset.UtcNow,
        PayloadJson: JsonSerializer.Serialize(payload, Json),
        CausationId: null,
        CorrelationId: null,
        CommandId: null,
        SourceSystem: "unit-test",
        ReplayFlag: false);

    private static DirectLendingTermsDto BuildTerms() => new(
        OriginationDate: new DateOnly(2026, 3, 22),
        MaturityDate: new DateOnly(2029, 3, 22),
        CommitmentAmount: 1_000_000m,
        BaseCurrency: CurrencyCode.USD,
        RateTypeKind: RateTypeKind.Fixed,
        FixedAnnualRate: 0.08m,
        InterestIndexName: null,
        SpreadBps: null,
        FloorRate: null,
        CapRate: null,
        DayCountBasis: DayCountBasis.Act360,
        PaymentFrequency: PaymentFrequency.Quarterly,
        AmortizationType: AmortizationType.InterestOnly,
        CommitmentFeeRate: null,
        DefaultRateSpreadBps: null,
        PrepaymentAllowed: true,
        CovenantsJson: null);

    [Fact]
    public void Rebuild_WithPikToggleAndPikAccrual_RestoresCapitalizedState()
    {
        var loanId = Guid.NewGuid();
        var rebuilder = new DirectLendingEventRebuilder();

        var history = new List<LoanEventLineageDto>
        {
            Event(1, "loan.created", new DateOnly(2026, 3, 22), new
            {
                loanId,
                facilityName = "PIK Replay Facility",
                borrower = new BorrowerInfoDto(Guid.NewGuid(), "Borrower", Guid.NewGuid()),
                effectiveDate = new DateOnly(2026, 3, 22),
                terms = BuildTerms()
            }),
            Event(2, "loan.pik-toggled", new DateOnly(2026, 3, 23), new
            {
                loanId,
                enablePik = true,
                effectiveDate = new DateOnly(2026, 3, 23),
                reason = "Borrower elected PIK"
            }),
            Event(3, "loan.daily-accrual-posted", new DateOnly(2026, 3, 24), new
            {
                loanId,
                accrualEntryId = Guid.NewGuid(),
                accrualDate = new DateOnly(2026, 3, 24),
                interestAmount = 0m,
                commitmentFeeAmount = 0m,
                penaltyAmount = 0m,
                annualRateApplied = 0.08m,
                pikInterestAmount = 55.56m
            })
        };

        var state = rebuilder.Rebuild(loanId, history);

        state.Should().NotBeNull();
        state!.Servicing.IsPikToggled.Should().BeTrue();
        state.Servicing.Balances.PrincipalOutstanding.Should().Be(55.56m,
            "PIK interest capitalizes into principal on replay");
        state.Servicing.Balances.InterestAccruedUnpaid.Should().Be(0m);
        state.Servicing.RevisionHistory.Should().Contain(revision =>
            revision.RevisionSourceType == "PikToggle" &&
            revision.Notes == "Borrower elected PIK");
    }

    [Fact]
    public void Rebuild_PikToggleWithoutReason_UsesDefaultNote()
    {
        var loanId = Guid.NewGuid();
        var rebuilder = new DirectLendingEventRebuilder();

        var history = new List<LoanEventLineageDto>
        {
            Event(1, "loan.created", new DateOnly(2026, 3, 22), new
            {
                loanId,
                facilityName = "PIK Replay Facility",
                borrower = new BorrowerInfoDto(Guid.NewGuid(), "Borrower", Guid.NewGuid()),
                effectiveDate = new DateOnly(2026, 3, 22),
                terms = BuildTerms()
            }),
            Event(2, "loan.pik-toggled", new DateOnly(2026, 3, 23), new
            {
                loanId,
                enablePik = false,
                effectiveDate = new DateOnly(2026, 3, 23),
                reason = (string?)null
            })
        };

        var state = rebuilder.Rebuild(loanId, history);

        state.Should().NotBeNull();
        state!.Servicing.IsPikToggled.Should().BeFalse();
        state.Servicing.RevisionHistory.Should().Contain(revision =>
            revision.RevisionSourceType == "PikToggle" &&
            revision.Notes == "PIK disabled");
    }
}
