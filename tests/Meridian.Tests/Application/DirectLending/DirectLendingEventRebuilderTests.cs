using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Meridian.Application.DirectLending;
using Meridian.Contracts.DirectLending;

namespace Meridian.Tests.Application.DirectLending;

/// <summary>
/// Direct unit coverage for <see cref="DirectLendingEventRebuilder"/> event replay,
/// including retained restructuring economics, terms and servicing evidence, and the PIK lifecycle.
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
        RecordedAt: new DateTimeOffset(2026, 3, 22, 12, 0, 0, TimeSpan.Zero).AddMinutes(version),
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

    [Fact]
    public void Rebuild_RestructuringAndLaterPayment_PreserveRecordedEconomicsAndEvidenceAcrossReplay()
    {
        var loanId = Guid.NewGuid();
        var history = DrawnLoanHistory(loanId);
        var restructure = RecordedRestructuring(loanId);
        history.Add(restructure);
        var rebuilder = new DirectLendingEventRebuilder();

        var restructured = rebuilder.Rebuild(loanId, history)!;

        restructured.Contract.Status.Should().Be(LoanStatus.Workout);
        restructured.Contract.CurrentTermsVersion.Should().Be(2);
        restructured.Contract.CurrentTerms.FixedAnnualRate.Should().Be(0.04m);
        restructured.Contract.CurrentTerms.CommitmentAmount.Should().Be(1_250_000m);
        restructured.Contract.TermsVersions.Select(item => item.VersionNumber).Should().Equal(1, 2);
        restructured.Contract.TermsVersions[1].SourceAction.Should().Be("loan.restructured");
        restructured.Contract.TermsVersions[1].RecordedAt.Should().Be(RestructuringRecordedAt,
            "the retained projection timestamp precedes the event-store receipt timestamp");
        restructured.Servicing.Status.Should().Be(LoanStatus.Workout);
        restructured.Servicing.Balances.PrincipalOutstanding.Should().Be(150m);
        restructured.Servicing.DrawdownLots.Select(item => item.RemainingPrincipal).Should().Equal(0m, 150m);
        restructured.Servicing.CurrentCommitment.Should().Be(1_000_000m);
        restructured.Servicing.TotalDrawn.Should().Be(300m);
        restructured.Servicing.AvailableToDraw.Should().Be(999_700m);
        restructured.Servicing.ServicingRevision.Should().Be(3);
        restructured.Servicing.RevisionHistory[0].Should().Be(new ServicingRevisionDto(
            3, "Restructuring", RestructuringDate, RestructuringRecordedAt, "Agreed workout"));

        history.Add(Event(6, "loan.principal-payment-applied", new DateOnly(2026, 4, 2), new
        {
            loanId,
            appliedAmount = 50m,
            effectiveDate = new DateOnly(2026, 4, 2)
        }));
        var retainedInput = JsonSerializer.Serialize(history, Json);
        var rebuilt = rebuilder.Rebuild(loanId, history)!;
        var replayed = rebuilder.Rebuild(loanId, history.AsEnumerable().Reverse().Append(restructure).ToArray())!;

        rebuilt.AggregateVersion.Should().Be(6);
        rebuilt.Servicing.Balances.PrincipalOutstanding.Should().Be(100m);
        rebuilt.Servicing.DrawdownLots.Select(item => item.RemainingPrincipal).Should().Equal(0m, 100m);
        rebuilt.Servicing.TotalDrawn.Should().Be(250m, "the later payment still consumes draw history once");
        rebuilt.Servicing.ServicingRevision.Should().Be(4);
        rebuilt.Servicing.LastPaymentDate.Should().Be(new DateOnly(2026, 4, 2));
        JsonSerializer.Serialize(replayed, Json).Should().Be(JsonSerializer.Serialize(rebuilt, Json),
            "input ordering and an identical redelivery cannot apply the haircut or revisions twice");
        JsonSerializer.Serialize(history, Json).Should().Be(retainedInput, "rebuild must preserve retained input bytes");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Rebuild_LegacyInMemoryRestructuringAlias_AppliesNullableHaircutOnce(bool hasHaircut)
    {
        var loanId = Guid.NewGuid();
        var history = DrawnLoanHistory(loanId);
        var newTerms = BuildTerms() with { FixedAnnualRate = 0.04m };
        var restructure = Event(5, "LoanRestructured", RestructuringDate, new RestructureLoanRequest(
            RestructuringType.Full, "Legacy workout", RestructuringDate, newTerms, hasHaircut ? 75m : null));
        history.Add(restructure);
        history.Add(restructure);

        var state = new DirectLendingEventRebuilder().Rebuild(loanId, history.AsEnumerable().Reverse().ToArray())!;

        state.Contract.CurrentTerms.Should().BeEquivalentTo(newTerms);
        state.Contract.CurrentTermsVersion.Should().Be(2);
        state.Contract.TermsVersions[1].SourceAction.Should().Be("Restructuring");
        state.Contract.TermsVersions[1].RecordedAt.Should().Be(restructure.RecordedAt);
        state.Servicing.Balances.PrincipalOutstanding.Should().Be(hasHaircut ? 225m : 300m);
        state.Servicing.DrawdownLots.Select(item => item.RemainingPrincipal).Should().Equal(hasHaircut ? 25m : 100m, 200m);
        state.Servicing.ServicingRevision.Should().Be(3);
        state.Servicing.RevisionHistory.Count(item => item.RevisionSourceType == "Restructuring").Should().Be(1);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Rebuild_LegacyRestructuring_RestoresOptionalTermsAndHaircut(bool replaceTerms, bool hasHaircut)
    {
        var loanId = Guid.NewGuid();
        var history = DrawnLoanHistory(loanId);
        var amendedTerms = BuildTerms() with { FixedAnnualRate = 0.06m };
        history.Add(Event(5, "loan.terms-amended", RestructuringDate, new
        {
            terms = amendedTerms,
            versionNumber = 2,
            amendmentReason = "Earlier amendment"
        }));
        var newTerms = BuildTerms() with { FixedAnnualRate = 0.04m };
        var payload = new Dictionary<string, object?>
        {
            ["loanId"] = loanId,
            ["reason"] = "Legacy workout",
            ["effectiveDate"] = RestructuringDate
        };
        if (replaceTerms)
            payload["newTerms"] = newTerms;
        if (hasHaircut)
        {
            payload["haircutAmount"] = 300m;
            payload["principalHaircut"] = 300m;
        }
        var restructure = Event(6, "loan.restructured", RestructuringDate, payload) with
        {
            RecordedAt = RestructuringRecordedAt
        };
        history.Add(restructure);

        var state = new DirectLendingEventRebuilder().Rebuild(loanId, history)!;

        state.Contract.Status.Should().Be(LoanStatus.Workout);
        state.Contract.CurrentTermsVersion.Should().Be(replaceTerms ? 3 : 2);
        state.Contract.CurrentTerms.Should().BeEquivalentTo(replaceTerms ? newTerms : amendedTerms);
        state.Contract.TermsVersions.Select(item => item.VersionNumber).Should().Equal(
            replaceTerms ? new[] { 1, 2, 3 } : new[] { 1, 2 });
        if (replaceTerms)
        {
            var version = state.Contract.TermsVersions[2];
            version.RecordedAt.Should().Be(restructure.RecordedAt);
            version.SourceAction.Should().Be("loan.restructured");
            version.AmendmentReason.Should().Be("Legacy workout");
        }
        state.Servicing.Status.Should().Be(LoanStatus.Workout);
        state.Servicing.Balances.PrincipalOutstanding.Should().Be(hasHaircut ? 0m : 300m);
        state.Servicing.DrawdownLots.Sum(item => item.RemainingPrincipal).Should().Be(hasHaircut ? 0m : 300m);
        state.Servicing.TotalDrawn.Should().Be(300m);
        state.Servicing.ServicingRevision.Should().Be(3);
        state.Servicing.RevisionHistory[0].Should().Be(new ServicingRevisionDto(
            3, "Restructuring", RestructuringDate, restructure.RecordedAt, "Legacy workout"));
    }

    [Theory]
    [InlineData("\"principalHaircut\":-0.01")]
    [InlineData("\"principalHaircut\":300.01")]
    [InlineData("\"principalHaircut\":\"25\"")]
    [InlineData("\"principalHaircut\":true")]
    [InlineData("\"principalHaircut\":null")]
    [InlineData("\"principalHaircut\":1e50")]
    [InlineData("\"principalHaircut\":25,\"haircutAmount\":30")]
    [InlineData("\"principalHaircut\":25,\"PrincipalHaircut\":30")]
    [InlineData("\"appliedPrincipalHaircut\":null")]
    [InlineData("\"principalHaircut\":25,\"newTerms\":42")]
    [InlineData("\"principalHaircut\":25,\"newTerms\":{}")]
    public void Rebuild_RestructuringWithMalformedAmountsOrTerms_RefusesInsteadOfClamping(string financialFields)
    {
        var loanId = Guid.NewGuid();
        var history = DrawnLoanHistory(loanId);
        history.Add(Event(5, "loan.restructured", RestructuringDate, new { }) with
        {
            PayloadJson = "{\"reason\":\"Workout\",\"effectiveDate\":\"2026-04-01\"," + financialFields + "}"
        });

        var rebuild = () => new DirectLendingEventRebuilder().Rebuild(loanId, history);

        rebuild.Should().Throw<InvalidDataException>().WithMessage("*loan.restructured*");
    }

    [Theory]
    [InlineData("termsVersion")]
    [InlineData("termsHash")]
    [InlineData("termsContent")]
    [InlineData("termsTimestamp")]
    [InlineData("revisionNumber")]
    [InlineData("revisionDate")]
    [InlineData("revisionNotes")]
    [InlineData("missingRevision")]
    [InlineData("missingAppliedHaircut")]
    [InlineData("missingJournalHaircut")]
    [InlineData("differentAppliedHaircut")]
    [InlineData("differentLoan")]
    [InlineData("differentEffectiveDate")]
    public void Rebuild_RestructuringWithConflictingRetainedEvidence_Refuses(string conflict)
    {
        var loanId = Guid.NewGuid();
        var history = DrawnLoanHistory(loanId);
        var restructure = RecordedRestructuring(loanId);
        var payload = JsonNode.Parse(restructure.PayloadJson)!.AsObject();
        switch (conflict)
        {
            case "termsVersion": payload["newTermsVersion"]!["versionNumber"] = 9; break;
            case "termsHash": payload["newTermsVersion"]!["termsHash"] = "altered"; break;
            case "termsContent": payload["newTermsVersion"]!["terms"]!["fixedAnnualRate"] = 0.09m; break;
            case "termsTimestamp": payload["newTermsVersion"]!["recordedAt"] = RestructuringRecordedAt.AddSeconds(1); break;
            case "revisionNumber": payload["servicingRevision"]!["revisionNumber"] = 9; break;
            case "revisionDate": payload["servicingRevision"]!["effectiveAsOfDate"] = "2026-04-02"; break;
            case "revisionNotes": payload["servicingRevision"]!["notes"] = "Other workout"; break;
            case "missingRevision": payload.Remove("servicingRevision"); break;
            case "missingAppliedHaircut": payload.Remove("appliedPrincipalHaircut"); break;
            case "missingJournalHaircut": payload.Remove("principalHaircut"); payload.Remove("haircutAmount"); break;
            case "differentAppliedHaircut": payload["appliedPrincipalHaircut"] = 160m; break;
            case "differentLoan": payload["loanId"] = Guid.NewGuid(); break;
            case "differentEffectiveDate": payload["effectiveDate"] = "2026-04-02"; break;
            default: throw new InvalidOperationException($"Unknown fixture conflict '{conflict}'.");
        }
        history.Add(restructure with { PayloadJson = payload.ToJsonString() });

        var rebuild = () => new DirectLendingEventRebuilder().Rebuild(loanId, history);

        rebuild.Should().Throw<InvalidDataException>().WithMessage("*loan.restructured*");
    }

    [Theory]
    [InlineData("eventIdentity")]
    [InlineData("aggregateVersion")]
    [InlineData("effectiveDate")]
    [InlineData("payload")]
    public void Rebuild_RestructuringWithConflictingDuplicate_Refuses(string conflict)
    {
        var loanId = Guid.NewGuid();
        var history = DrawnLoanHistory(loanId);
        var restructure = RecordedRestructuring(loanId);
        history.Add(restructure);
        var duplicate = conflict switch
        {
            "eventIdentity" => restructure with { AggregateVersion = 6 },
            "aggregateVersion" => restructure with { EventId = Guid.NewGuid() },
            "effectiveDate" => restructure with { EffectiveDate = RestructuringDate.AddDays(1) },
            "payload" => restructure with { PayloadJson = restructure.PayloadJson.Replace("Agreed workout", "Changed workout", StringComparison.Ordinal) },
            _ => throw new InvalidOperationException($"Unknown fixture conflict '{conflict}'.")
        };
        history.Add(duplicate);

        var rebuild = () => new DirectLendingEventRebuilder().Rebuild(loanId, history.AsEnumerable().Reverse().ToArray());

        rebuild.Should().Throw<InvalidDataException>().WithMessage("*loan.restructured*");
    }

    private static readonly DateOnly RestructuringDate = new(2026, 4, 1);
    private static readonly DateTimeOffset RestructuringRecordedAt = new(2026, 4, 1, 8, 0, 0, TimeSpan.Zero);

    private static List<LoanEventLineageDto> DrawnLoanHistory(Guid loanId) =>
    [
        Event(1, "loan.created", new DateOnly(2026, 3, 22), new
        {
            loanId,
            facilityName = "Restructuring replay facility",
            borrower = new BorrowerInfoDto(Guid.NewGuid(), "Borrower", Guid.NewGuid()),
            effectiveDate = new DateOnly(2026, 3, 22),
            terms = BuildTerms()
        }),
        Event(2, "loan.activated", new DateOnly(2026, 3, 22), new { activationDate = new DateOnly(2026, 3, 22) }),
        Event(3, "loan.drawdown-booked", new DateOnly(2026, 3, 23), new
        {
            lotId = Guid.NewGuid(),
            amount = 100m,
            tradeDate = new DateOnly(2026, 3, 22),
            settleDate = new DateOnly(2026, 3, 23)
        }),
        Event(4, "loan.drawdown-booked", new DateOnly(2026, 3, 25), new
        {
            lotId = Guid.NewGuid(),
            amount = 200m,
            tradeDate = new DateOnly(2026, 3, 24),
            settleDate = new DateOnly(2026, 3, 25)
        })
    ];

    private static LoanEventLineageDto RecordedRestructuring(Guid loanId)
    {
        var newTerms = BuildTerms() with { FixedAnnualRate = 0.04m, CommitmentAmount = 1_250_000m };
        return Event(5, "loan.restructured", RestructuringDate, new
        {
            loanId,
            reason = "Agreed workout",
            effectiveDate = RestructuringDate,
            newTerms,
            haircutAmount = 150m,
            principalHaircut = 150m,
            appliedPrincipalHaircut = 150m,
            newTermsVersion = DirectLendingServiceSupport.CreateTermsVersion(
                2, newTerms, "loan.restructured", "Agreed workout", RestructuringRecordedAt),
            servicingRevision = new ServicingRevisionDto(
                3, "Restructuring", RestructuringDate, RestructuringRecordedAt, "Agreed workout")
        }) with { RecordedAt = RestructuringRecordedAt.AddSeconds(1) };
    }
}
