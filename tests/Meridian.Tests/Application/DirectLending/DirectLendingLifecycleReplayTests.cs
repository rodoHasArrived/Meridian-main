using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Meridian.Application.DirectLending;
using Meridian.Contracts.DirectLending;
using Meridian.Storage.DirectLending;

namespace Meridian.Tests.Application.DirectLending;

public sealed partial class DirectLendingLifecycleReplayTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly Guid LoanId = Guid.Parse("81111111-1111-4111-8111-111111111111");
    private static readonly Guid CollateralId = Guid.Parse("82222222-2222-4222-8222-222222222222");
    private static readonly DateOnly Start = new(2026, 3, 22);
    private static readonly DateTimeOffset Recorded = new(2026, 3, 22, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Rebuild_CreditLifecycle_ReconstructsBalancesLotsCollateralAndStatusDeterministically()
    {
        var history = FundedHistory();
        Add(history, "loan.fee-assessed", new { amount = 25m, effectiveDate = Start });
        Add(history, "loan.daily-accrual-posted", new
        {
            accrualEntryId = Guid.Parse("83333333-3333-4333-8333-333333333333"), accrualDate = Start,
            interestAmount = 4m, commitmentFeeAmount = 0m, penaltyAmount = 0m, annualRateApplied = 0.08m
        });
        Add(history, "loan.collateral-added", new { collateral = Collateral() });
        Add(history, "loan.collateral-value-updated", new { collateralId = CollateralId, newEstimatedValue = 175m, newAppraisalDate = Start });
        Add(history, "loan.status-transitioned", new { newStatus = LoanStatus.Defaulted, reason = "Retained default", effectiveDate = Start });
        Add(history, "loan.write-off-applied", new { requestedAmount = 25m, appliedAmount = 25m, reason = "Partial loss", effectiveDate = Start });
        Add(history, "loan.servicer-position-imported", new
        {
            batchId = Guid.Parse("84444444-4444-4444-8444-444444444444"),
            servicerReportLineId = Guid.Parse("85555555-5555-4555-8555-555555555555"), reportAsOfDate = Start,
            principalOutstanding = 125m, interestAccruedUnpaid = (decimal?)null,
            feesAccruedUnpaid = (decimal?)null, penaltyAccruedUnpaid = 1m, commitmentAvailable = (decimal?)null
        });
        Add(history, "loan.discount-premium-amortized", BasisReceipt(10m, 2m, 100m, 20m));
        Add(history, "loan.status-transitioned", new { newStatus = LoanStatus.Active, reason = "Retained cure", effectiveDate = Start });
        Add(history, "loan.collateral-removed", new { collateralId = CollateralId, reason = "Released" });
        var originalBytes = JsonSerializer.Serialize(history, Json);

        var first = Rebuild(history);
        var second = Rebuild(history.AsEnumerable().Reverse().Concat(history).ToArray());

        first.Contract.Status.Should().Be(LoanStatus.Active);
        first.Servicing.Status.Should().Be(LoanStatus.Active);
        first.Servicing.Balances.Should().Be(new OutstandingBalancesDto(125m, 4m, 0m, 25m, 1m));
        first.Servicing.DrawdownLots.Select(lot => lot.RemainingPrincipal).Should().Equal(75m, 50m);
        first.Servicing.TotalDrawn.Should().Be(125m);
        first.Servicing.AvailableToDraw.Should().Be(875m);
        first.Servicing.LastPaymentDate.Should().BeNull("write-off is not a cash payment");
        first.Servicing.Collateral.Should().BeEmpty();
        first.Servicing.UnamortizedDiscount.Should().Be(90m);
        first.Servicing.UnamortizedPremium.Should().Be(18m);
        first.Servicing.ServicingRevision.Should().Be(12);
        first.Servicing.RevisionHistory.Should().HaveCount(12);
        first.AggregateVersion.Should().Be(history.Count);
        JsonSerializer.Serialize(second, Json).Should().Be(JsonSerializer.Serialize(first, Json));
        JsonSerializer.Serialize(history, Json).Should().Be(originalBytes, "replay must not rewrite retained evidence");
    }

    [Theory]
    [InlineData(LoanStatus.NonPerforming)]
    [InlineData(LoanStatus.Defaulted)]
    [InlineData(LoanStatus.Workout)]
    [InlineData(LoanStatus.Active)]
    [InlineData(LoanStatus.Closed)]
    public void Rebuild_StatusEvents_RestoreBothStatusViewsWithoutChangingEconomics(LoanStatus status)
    {
        var history = FundedHistory();
        var before = Rebuild(history);
        Add(history, "LoanStatusTransitioned", new { newStatus = status, effectiveDate = Start, reason = "Retained decision" });

        var rebuilt = Rebuild(history);

        rebuilt.Contract.Status.Should().Be(status);
        rebuilt.Servicing.Status.Should().Be(status);
        rebuilt.Servicing.Balances.Should().Be(before.Servicing.Balances);
        rebuilt.Servicing.DrawdownLots.Should().BeEquivalentTo(before.Servicing.DrawdownLots);
        rebuilt.Servicing.RevisionHistory[0].Notes.Should().Be("Retained decision");
    }

    [Theory]
    [InlineData(25, 25, 125)]
    [InlineData(150, 150, 0)]
    [InlineData(250, 150, 0)]
    public void Rebuild_WriteOff_UsesAppliedAmountAndPreservesOtherBalances(int requested, int applied, int remaining)
    {
        var history = FundedHistory();
        Add(history, "loan.fee-assessed", new { amount = 7m });
        Add(history, "loan.write-off-applied", new { requestedAmount = requested, appliedAmount = applied, reason = "Recorded loss" });

        var rebuilt = Rebuild(history);

        rebuilt.Servicing.Balances.PrincipalOutstanding.Should().Be(remaining);
        rebuilt.Servicing.TotalDrawn.Should().Be(remaining);
        rebuilt.Servicing.DrawdownLots.Sum(lot => lot.RemainingPrincipal).Should().Be(remaining);
        rebuilt.Servicing.Balances.FeesAccruedUnpaid.Should().Be(7m);
        rebuilt.Servicing.LastPaymentDate.Should().BeNull();
    }

    [Fact]
    public void Rebuild_LegacyWriteOff_RetainsItsAlreadyAppliedAmount()
    {
        var history = FundedHistory();
        Add(history, "loan.write-off-applied", new { amount = 50m, reason = "Historical applied amount" });

        var rebuilt = Rebuild(history);

        rebuilt.Servicing.Balances.PrincipalOutstanding.Should().Be(100m);
        rebuilt.Servicing.DrawdownLots.Select(lot => lot.RemainingPrincipal).Should().Equal(50m, 50m);
    }

    [Theory]
    [InlineData("{\"requestedAmount\":200,\"appliedAmount\":200,\"reason\":\"bad\"}")]
    [InlineData("{\"requestedAmount\":25,\"appliedAmount\":20,\"reason\":\"bad\"}")]
    [InlineData("{\"appliedAmount\":25,\"amount\":20,\"reason\":\"bad\"}")]
    [InlineData("{\"appliedAmount\":-1,\"reason\":\"bad\"}")]
    [InlineData("{\"appliedAmount\":\"25\",\"reason\":\"bad\"}")]
    public void Rebuild_WriteOff_RejectsMalformedOrContradictoryAmounts(string payload)
    {
        var history = FundedHistory();
        history.Add(Event(5, "loan.write-off-applied", new { }) with { PayloadJson = payload });

        FluentActions.Invoking(() => Rebuild(history)).Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void Rebuild_LegacyCollateralAndPikAliases_PreserveOriginalIdentityAndDates()
    {
        var history = FundedHistory();
        Add(history, "CollateralAdded", Collateral());
        Add(history, "CollateralValueUpdated", new { collateralId = CollateralId, newEstimatedValue = 123m, newAppraisalDate = Start });
        Add(history, "PikToggled", new { enablePik = true, effectiveDate = Start, reason = "PIK elected" });

        var updated = Rebuild(history);
        updated.Servicing.Collateral.Should().ContainSingle().Which.Should().Be(Collateral() with { EstimatedValue = 123m });
        updated.Servicing.IsPikToggled.Should().BeTrue();
        Add(history, "CollateralRemoved", new { collateralId = CollateralId, reason = "Released" });

        Rebuild(history).Servicing.Collateral.Should().BeEmpty();
    }

    [Fact]
    public void Rebuild_ServicerObservation_DistinguishesZeroFromMissingAndNull()
    {
        var history = FundedHistory();
        Add(history, "loan.fee-assessed", new { amount = 25m });
        Add(history, "loan.servicer-position-imported", new
        {
            batchId = Guid.Parse("84444444-4444-4444-8444-444444444444"),
            servicerReportLineId = Guid.Parse("85555555-5555-4555-8555-555555555555"), reportAsOfDate = Start,
            principalOutstanding = (decimal?)null, feesAccruedUnpaid = 0m, commitmentAvailable = (decimal?)null
        });

        var rebuilt = Rebuild(history);

        rebuilt.Servicing.Balances.PrincipalOutstanding.Should().Be(150m);
        rebuilt.Servicing.Balances.FeesAccruedUnpaid.Should().Be(0m);
        rebuilt.Servicing.AvailableToDraw.Should().Be(850m);
        rebuilt.Servicing.RevisionHistory[0].Notes.Should().Contain("84444444-4444-4444-8444-444444444444");
    }

    [Fact]
    public void Rebuild_RetainsExactServicingRevisionInsteadOfEventReceiptClock()
    {
        var history = FundedHistory();
        var revision = new ServicingRevisionDto(3, "InternalEvent", Start, Recorded.AddSeconds(17), "Retained fee note");
        Add(history, "loan.fee-assessed", new { amount = 12m, servicingRevision = revision });

        var rebuilt = Rebuild(history);

        rebuilt.Servicing.RevisionHistory[0].Should().Be(revision);
        rebuilt.Servicing.RevisionHistory[0].CreatedAt.Should().NotBe(history[^1].RecordedAt);
    }

    [Theory]
    [InlineData("revisionNumber")]
    [InlineData("revisionSourceType")]
    [InlineData("effectiveAsOfDate")]
    [InlineData("createdAt")]
    public void Rebuild_RejectsConflictingRetainedRevision(string field)
    {
        var history = FundedHistory();
        var revision = new ServicingRevisionDto(3, "InternalEvent", Start, Recorded.AddSeconds(17), "Retained fee note");
        var item = Event(5, "loan.fee-assessed", new { amount = 12m, servicingRevision = revision });
        var payload = JsonNode.Parse(item.PayloadJson)!.AsObject();
        payload["servicingRevision"]![field] = field switch
        {
            "revisionNumber" => JsonValue.Create(4),
            "revisionSourceType" => JsonValue.Create("Other"),
            "effectiveAsOfDate" => JsonValue.Create("2026-03-23"),
            "createdAt" => JsonValue.Create("0001-01-01T00:00:00+00:00"),
            _ => throw new InvalidOperationException()
        };
        history.Add(item with { PayloadJson = payload.ToJsonString() });

        FluentActions.Invoking(() => Rebuild(history)).Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData("loan.discount-premium-amortized")]
    [InlineData("DiscountPremiumAmortized")]
    public void Rebuild_Amortization_UsesRetainedBasisAndAmountsAcrossLaterTermsChanges(string eventType)
    {
        var history = FundedHistory();
        Add(history, eventType, BasisReceipt(10m, 2m, 100m, 20m));
        Add(history, "loan.terms-amended", new { terms = Terms() with { MaturityDate = new DateOnly(2040, 1, 1) }, versionNumber = 2, amendmentReason = "Later maturity" });
        Add(history, eventType, new { discountAmort = 20m, premiumAmort = 3m });

        var rebuilt = Rebuild(history);

        rebuilt.Servicing.UnamortizedDiscount.Should().Be(70m);
        rebuilt.Servicing.UnamortizedPremium.Should().Be(15m);
        rebuilt.Servicing.Balances.PrincipalOutstanding.Should().Be(150m);
    }

    [Fact]
    public void Rebuild_HistoricalNonzeroAmortizationWithoutBasis_FailsExplicitly()
    {
        var history = FundedHistory();
        Add(history, "DiscountPremiumAmortized", new { discountAmort = 10m, premiumAmort = 0m });

        FluentActions.Invoking(() => Rebuild(history)).Should().Throw<InvalidDataException>()
            .WithMessage("*no retained opening basis*");
    }

    [Fact]
    public void Rebuild_HistoricalZeroAmortization_IsStillReplayable()
    {
        var history = FundedHistory();
        Add(history, "DiscountPremiumAmortized", new { discountAmort = 0m, premiumAmort = 0m });

        Rebuild(history).Servicing.UnamortizedDiscount.Should().Be(0m);
    }

    [Theory]
    [InlineData("unamortizedDiscountBefore", 101)]
    [InlineData("unamortizedDiscountAfter", 89)]
    [InlineData("discountAmortization", 11)]
    [InlineData("premiumAmortization", -1)]
    public void Rebuild_Amortization_RejectsContradictoryBasisOrAliases(string field, int value)
    {
        var history = FundedHistory();
        var item = Event(5, "loan.discount-premium-amortized", BasisReceipt(10m, 2m, 100m, 20m));
        var payload = JsonNode.Parse(item.PayloadJson)!.AsObject();
        payload[field] = value;
        history.Add(item with { PayloadJson = payload.ToJsonString() });

        FluentActions.Invoking(() => Rebuild(history)).Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void Rebuild_Amortization_RejectsASecondUnexplainedOpeningBasis()
    {
        var history = FundedHistory();
        Add(history, "loan.discount-premium-amortized", BasisReceipt(10m, 2m, 100m, 20m));
        Add(history, "loan.discount-premium-amortized", BasisReceipt(10m, 2m, 100m, 20m));

        FluentActions.Invoking(() => Rebuild(history)).Should().Throw<InvalidDataException>()
            .WithMessage("*conflicts with earlier retained basis*");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Rebuild_Amortization_RejectsAnAmountAboveItsRetainedBasis(bool completeReceipt)
    {
        var history = FundedHistory();
        if (completeReceipt)
        {
            Add(history, "loan.discount-premium-amortized", new
            {
                discountAmort = 20m, premiumAmort = 0m,
                unamortizedDiscountBefore = 10m, unamortizedDiscountAfter = 0m,
                unamortizedPremiumBefore = 0m, unamortizedPremiumAfter = 0m
            });
        }
        else
        {
            Add(history, "loan.discount-premium-amortized", BasisReceipt(10m, 0m, 20m, 0m));
            Add(history, "loan.discount-premium-amortized", new { discountAmort = 20m, premiumAmort = 0m });
        }

        FluentActions.Invoking(() => Rebuild(history)).Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void Rebuild_MissingAggregateVersion_FailsInsteadOfReturningAnIncompleteState()
    {
        var history = FundedHistory();
        history.RemoveAt(2);

        FluentActions.Invoking(() => Rebuild(history)).Should().Throw<InvalidDataException>()
            .WithMessage("*history is incomplete*expected aggregate version '3'*");
    }

    [Fact]
    public void Rebuild_PreservesAcceptedNumericClassificationsAndExplicitDefaultDates()
    {
        var history = FundedHistory();
        // Recovery must not add validation that the accepting command did not enforce.
        var collateral = Collateral() with
        {
            CollateralType = (CollateralType)255,
            Currency = (CurrencyCode)255,
            AppraisalDate = default
        };
        history.Add(Event(5, "loan.collateral-added", new { collateral }) with { EffectiveDate = default(DateOnly) });
        history.Add(Event(6, "loan.status-transitioned", new
        {
            newStatus = (LoanStatus)255, effectiveDate = default(DateOnly), reason = "Retained classification"
        }) with { EffectiveDate = default(DateOnly) });

        var rebuilt = Rebuild(history);

        rebuilt.Servicing.Collateral.Should().ContainSingle().Which.Should().Be(collateral);
        rebuilt.Contract.Status.Should().Be((LoanStatus)255);
        rebuilt.Servicing.Status.Should().Be((LoanStatus)255);
    }

    [Fact]
    public void Rebuild_DailyAccrual_PreservesExistingPenaltyAsTheLiveAggregateDoes()
    {
        var history = FundedHistory();
        Add(history, "loan.prepayment-penalty-charged", new { penaltyAmount = 7m });
        Add(history, "loan.daily-accrual-posted", new
        {
            accrualEntryId = Guid.Parse("8bbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb"), accrualDate = Start,
            interestAmount = 1m, commitmentFeeAmount = 0m, penaltyAmount = 0m, annualRateApplied = 0.08m
        });

        var rebuilt = Rebuild(history);

        rebuilt.Servicing.Balances.PenaltyAccruedUnpaid.Should().Be(7m);
        rebuilt.Servicing.AccrualEntries.Should().ContainSingle().Which.PenaltyAmount.Should().Be(0m);
    }

    [Theory]
    [InlineData("eventIdentity")]
    [InlineData("aggregateVersion")]
    [InlineData("payload")]
    [InlineData("recordedAt")]
    public void Rebuild_ConflictingHistoryIdentityOrVersion_FailsBeforeApplyingAnotherEffect(string conflict)
    {
        var history = FundedHistory();
        Add(history, "loan.fee-assessed", new { amount = 25m });
        var original = history[^1];
        history.Add(conflict switch
        {
            "eventIdentity" => original with { AggregateVersion = 6 },
            "aggregateVersion" => original with { EventId = Guid.NewGuid() },
            "payload" => original with { PayloadJson = "{\"amount\":26}" },
            "recordedAt" => original with { RecordedAt = original.RecordedAt.AddSeconds(1) },
            _ => throw new InvalidOperationException()
        });

        FluentActions.Invoking(() => Rebuild(history)).Should().Throw<InvalidDataException>().WithMessage("*Conflicting retained*");
    }

    [Theory]
    [InlineData("{\"amount\":25,\"Amount\":25}")]
    [InlineData("{\"amount\":25,\"loanId\":\"99999999-9999-4999-8999-999999999999\"}")]
    [InlineData("{\"amount\":25,\"effectiveDate\":\"2026-03-23\"}")]
    public void Rebuild_RejectsAmbiguousFieldsWrongLoanAndConflictingDate(string payload)
    {
        var history = FundedHistory();
        history.Add(Event(5, "loan.fee-assessed", new { }) with { PayloadJson = payload });

        FluentActions.Invoking(() => Rebuild(history)).Should().Throw<InvalidDataException>();
    }

    private static object BasisReceipt(decimal discount, decimal premium, decimal discountBefore, decimal premiumBefore) => new
    {
        accrualDate = Start, discountAmortization = discount, discountAmort = discount,
        premiumAmortization = premium, premiumAmort = premium,
        unamortizedDiscountBefore = discountBefore, unamortizedDiscountAfter = discountBefore - discount,
        unamortizedPremiumBefore = premiumBefore, unamortizedPremiumAfter = premiumBefore - premium
    };

    private static CollateralDto Collateral() => new(CollateralId, CollateralType.Equipment, "Retained equipment", 200m, CurrencyCode.USD, Start);

    private static PersistedDirectLendingState Rebuild(IReadOnlyList<LoanEventLineageDto> history)
        => new DirectLendingEventRebuilder().Rebuild(LoanId, history)!;

    private static void Add(List<LoanEventLineageDto> history, string eventType, object payload)
        => history.Add(Event(history.Count + 1, eventType, payload));

    private static LoanEventLineageDto Event(long version, string eventType, object payload) => new(
        Guid.Parse($"80000000-0000-4000-8000-{version:D12}"), version, eventType, 1, Start,
        Recorded.AddMinutes(version), JsonSerializer.Serialize(payload, Json), null, null, null, "synthetic-replay", false);

    private static List<LoanEventLineageDto> FundedHistory(DirectLendingTermsDto? terms = null) =>
    [
        Event(1, "loan.created", new
        {
            loanId = LoanId, facilityName = "Synthetic lifecycle facility",
            borrower = new BorrowerInfoDto(Guid.Parse("86666666-6666-4666-8666-666666666666"), "Synthetic borrower", null),
            effectiveDate = Start, terms = terms ?? Terms()
        }),
        Event(2, "loan.activated", new { activationDate = Start }),
        Event(3, "loan.drawdown-booked", new { lotId = Guid.Parse("87777777-7777-4777-8777-777777777777"), amount = 100m, tradeDate = Start, settleDate = Start }),
        Event(4, "loan.drawdown-booked", new { lotId = Guid.Parse("88888888-8888-4888-8888-888888888888"), amount = 50m, tradeDate = Start.AddDays(1), settleDate = Start.AddDays(1) })
            with { EffectiveDate = Start.AddDays(1) }
    ];

    private static DirectLendingTermsDto Terms() => new(
        Start, Start.AddYears(3), 1_000m, CurrencyCode.USD, RateTypeKind.Fixed, 0.08m, null, null, null, null,
        DayCountBasis.Act360, PaymentFrequency.Quarterly, AmortizationType.InterestOnly, null, null, true, null);
}
