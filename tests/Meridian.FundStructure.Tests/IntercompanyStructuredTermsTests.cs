using System.Text.Json;
using Meridian.Application.FundStructure;
using Meridian.Contracts.Calendars;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.SecurityMaster.Schedules;
using Xunit;

namespace Meridian.FundStructure.Tests;

public sealed class IntercompanyStructuredTermsTests
{
    private static readonly Guid First = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Second = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void LegacyTermsSerializeWithoutNewPropertiesAndRetainedFingerprintsStayUnchanged()
    {
        const string json = """{"fixedAnnualRate":"0.1234567890123456789012345678","notes":"Retained"}""";
        var terms = JsonSerializer.Deserialize(json, FundStructureContractsJsonContext.Default.IntercompanyAgreementTermsDto)!;
        Assert.Null(terms.StructuredTerms);
        Assert.Equal(json, JsonSerializer.Serialize(terms, FundStructureContractsJsonContext.Default.IntercompanyAgreementTermsDto));
        // Exercises the retained exact command/review SHA-256 fixtures; no historical rehash or approval rewrite.
        new IntercompanyAgreementRulesTests().RetainedCommandAndReviewKeepTheirCanonicalFingerprints();
    }

    [Fact]
    public void NewStructuredRatesRoundTripAsExactDecimalStringsAndEnterNativeReviewHash()
    {
        var draft = Loan();
        var terms = draft.Terms! with { StructuredTerms = draft.Terms!.StructuredTerms! with { FixedAnnualRate = 0.1234567890123456789012345678m } };
        var json = JsonSerializer.Serialize(terms, FundStructureContractsJsonContext.Default.IntercompanyAgreementTermsDto);
        using var document = JsonDocument.Parse(json);
        Assert.Equal("0.1234567890123456789012345678", document.RootElement.GetProperty("structuredTerms").GetProperty("fixedAnnualRate").GetString());
        Assert.Equal(terms.StructuredTerms.FixedAnnualRate,
            JsonSerializer.Deserialize(json, FundStructureContractsJsonContext.Default.IntercompanyAgreementTermsDto)!.StructuredTerms!.FixedAnnualRate);
        var payload = new IntercompanyAgreementReviewPayloadDto(draft, null, null, "Review");
        Assert.NotEqual(IntercompanyAgreementRules.ReviewedHash(payload),
            IntercompanyAgreementRules.ReviewedHash(payload with { Draft = draft with { Terms = terms } }));
    }

    [Fact]
    public void TypedTermsNormalizeOnlyTheirDeclaredFieldsAndDoNotInferProse()
    {
        var draft = Loan();
        var normalized = IntercompanyAgreementRules.Normalize(draft with
        {
            Terms = draft.Terms! with
            {
                StructuredTerms = draft.Terms!.StructuredTerms! with
                {
                    Schedule = draft.Terms.StructuredTerms.Schedule! with { CalendarId = " WEEKENDS " },
                    FixedAnnualRate = 0.0500m
                }
            }
        });
        Assert.Equal("WEEKENDS", normalized.Terms!.StructuredTerms!.Schedule!.CalendarId);
        Assert.Equal(0.05m, normalized.Terms.StructuredTerms.FixedAnnualRate);
        Assert.Equal("0.05", normalized.Terms.StructuredTerms.FixedAnnualRate!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var old = IntercompanyAgreementRules.Normalize(draft with { Terms = new(RateDescription: "5 percent fixed, monthly") });
        Assert.Null(old.Terms!.StructuredTerms);
    }

    [Fact]
    public void PartialStructuredDraftCanBeSavedButCannotBeSubmittedAsComplete()
    {
        var draft = Loan() with { Terms = new() { StructuredTerms = new() } };
        IntercompanyAgreementRules.ValidateShape(draft);
        Assert.Throws<IntercompanyAgreementValidationException>(() => IntercompanyAgreementRules.ValidateComplete(draft));
    }

    [Fact]
    public void FixedAndFloatingLoansRequireExplicitCompatibleRateFieldsAndUnits()
    {
        var draft = Loan();
        IntercompanyAgreementRules.ValidateComplete(draft);
        var floating = draft.Terms!.StructuredTerms! with
        { RateBasis = IntercompanyRateBasisDto.Floating, FixedAnnualRate = null, FloatingIndexReference = "SOFR", SpreadBps = 175m };
        IntercompanyAgreementRules.ValidateComplete(draft with { Terms = draft.Terms with { StructuredTerms = floating } });
        foreach (var invalid in new[]
        {
            draft.Terms.StructuredTerms with { FixedAnnualRate = 5m },
            draft.Terms.StructuredTerms with { FloatingIndexReference = "SOFR" },
            draft.Terms.StructuredTerms with { DayCount = (IntercompanyDayCountDto)999 },
            draft.Terms.StructuredTerms with { Compounding = null },
            floating with { SpreadBps = null },
            floating with { FixedAnnualRate = 0.05m }
        })
            Assert.Throws<IntercompanyAgreementValidationException>(() => IntercompanyAgreementRules.ValidateComplete(
            draft with { Terms = draft.Terms with { StructuredTerms = invalid } }));
    }

    [Fact]
    public void LegacyAndStructuredRateOrSettlementConflictsCannotReachLegalApproval()
    {
        var draft = Loan();
        IntercompanyAgreementRules.ValidateComplete(draft with { Terms = draft.Terms! with { FixedAnnualRate = 0.05m, SettlementDays = 1 } });
        Assert.Throws<IntercompanyAgreementValidationException>(() => IntercompanyAgreementRules.ValidateComplete(draft with
        { Terms = draft.Terms! with { FixedAnnualRate = 0.06m } }));
        Assert.Throws<IntercompanyAgreementValidationException>(() => IntercompanyAgreementRules.ValidateComplete(draft with
        { Terms = draft.Terms! with { SettlementDays = 2 } }));
    }

    [Fact]
    public void PercentageFeesRequireCalculationBaseAndCannotMixAmountOrLoanFields()
    {
        var draft = Loan() with
        {
            AgreementType = IntercompanyAgreementTypeDto.ManagementFee,
            PartyOneRole = "Service provider",
            PartyTwoRole = "Recipient",
            Terms = new() { StructuredTerms = new(Schedule(), AnnualFeeRate: 0.015m, CalculationBaseReference: "month-end NAV") }
        };
        IntercompanyAgreementRules.ValidateComplete(draft);
        foreach (var invalid in new[]
        {
            draft.Terms!.StructuredTerms! with { CalculationBaseReference = null },
            draft.Terms!.StructuredTerms! with { FixedFeeAmount = 100m },
            draft.Terms!.StructuredTerms! with { AnnualFeeRate = 1.5m },
            draft.Terms!.StructuredTerms! with { RateBasis = IntercompanyRateBasisDto.Fixed }
        })
            Assert.Throws<IntercompanyAgreementValidationException>(() => IntercompanyAgreementRules.ValidateComplete(
            draft with { Terms = draft.Terms! with { StructuredTerms = invalid } }));
    }

    [Fact]
    public void CostSharesUseExactPercentagesOfScopedPartiesOrExplicitDriver()
    {
        var draft = Loan() with
        {
            AgreementType = IntercompanyAgreementTypeDto.CostShare,
            PartyOneRole = "Participant",
            PartyTwoRole = "Participant",
            Terms = new() { StructuredTerms = new(Schedule(), Allocations: [new(First, 40m), new(Second, 60m)]) }
        };
        IntercompanyAgreementRules.ValidateComplete(draft);
        IntercompanyAgreementRules.ValidateComplete(draft with { Terms = new() { StructuredTerms = new(Schedule(), AllocationDriverReference: "headcount:v1") } });
        foreach (var invalid in new[]
        {
            draft.Terms!.StructuredTerms! with { Allocations = [new(First, 99.99m)] },
            draft.Terms!.StructuredTerms! with { Allocations = [new(First, 50m), new(First, 50m)] },
            draft.Terms!.StructuredTerms! with { Allocations = [new(Guid.NewGuid(), 100m)] },
            draft.Terms!.StructuredTerms! with { AllocationDriverReference = "headcount:v1" }
        })
            Assert.Throws<IntercompanyAgreementValidationException>(() => IntercompanyAgreementRules.ValidateComplete(
            draft with { Terms = draft.Terms! with { StructuredTerms = invalid } }));
    }

    [Fact]
    public void ScheduleValidationRejectsUnknownConventionsAndDatesOutsideLegalTerm()
    {
        var draft = Loan();
        foreach (var invalid in new[]
        {
            Schedule() with { StartDate = new(2025, 12, 31) },
            Schedule() with { EndDate = new(2027, 1, 2) },
            Schedule() with { SettlementBusinessDays = -1 },
            Schedule() with { Frequency = (IntercompanyFrequencyDto)999 },
            Schedule() with { BusinessDayConvention = (BusinessDayConvention)999 },
            Schedule() with { CalendarId = "" }
        })
            Assert.Throws<IntercompanyAgreementValidationException>(() => IntercompanyAgreementRules.ValidateComplete(draft with
            { Terms = draft.Terms! with { StructuredTerms = draft.Terms!.StructuredTerms! with { Schedule = invalid } } }));
    }

    [Fact]
    public void DatesUseDeclaredCalendarAndSharedKernelWithNoChargeAmounts()
    {
        var preview = Service(BusinessDayCalendars.WeekendsOnly).Preview(Loan(), "reviewed-payload-hash");
        Assert.Equal("Ready", preview.Status);
        Assert.Equal("StructuredV1", preview.TermsFormat);
        Assert.Equal("reviewed-payload-hash", preview.SubjectHash);
        Assert.Equal(ScheduleGenerator.KernelVersion, preview.KernelVersion);
        Assert.Equal(BusinessDayCalendars.WeekendsOnly.ContentHash, preview.CalendarHash);
        Assert.Equal(12, preview.Periods.Count);
        // Settlement lag starts at the unadjusted accrual end, Sunday, per the shared kernel contract.
        Assert.Equal(new DateOnly(2026, 2, 2), preview.Periods[0].PaymentDate);
    }

    [Fact]
    public void MissingCalendarNeverFallsBackAndReplacementChangesPreviewEvidenceOnly()
    {
        var draft = Loan();
        var payload = new IntercompanyAgreementReviewPayloadDto(draft, null, null, "Review");
        var hash = IntercompanyAgreementRules.ReviewedHash(payload);
        var missing = Service().Preview(draft, hash);
        Assert.Equal("Unavailable", missing.Status);
        Assert.Null(missing.CalendarHash);
        Assert.Empty(missing.Periods);
        var first = Service(BusinessDayCalendars.WeekendsOnly).Preview(draft, hash);
        var replacement = new HolidayBusinessDayCalendar("WEEKENDS", 2, [new DateOnly(2026, 2, 2)]);
        var next = Service(replacement).Preview(draft, hash);
        Assert.NotEqual(first.CalendarHash, next.CalendarHash);
        Assert.Equal(new DateOnly(2026, 2, 3), next.Periods[0].PaymentDate);
        Assert.Equal(hash, next.SubjectHash);
        Assert.Equal(hash, IntercompanyAgreementRules.ReviewedHash(payload));
    }

    [Fact]
    public void IncompleteCalendarCoverageIsUnavailableRatherThanFalsePrecision()
    {
        var partial = new HolidayBusinessDayCalendar("WEEKENDS", 1,
            coverage: new(new DateOnly(2026, 6, 1), new DateOnly(2026, 12, 31)));
        var preview = Service(partial).Preview(Loan(), "reviewed-hash");
        Assert.Equal("Unavailable", preview.Status);
        Assert.Empty(preview.Periods);
        Assert.NotEmpty(preview.Diagnostics);
    }

    [Fact]
    public void GuaranteesRetainBeneficiaryAndLimitWithoutProducingObligationDates()
    {
        var draft = Loan() with
        {
            AgreementType = IntercompanyAgreementTypeDto.Guarantee,
            PartyOneRole = "Guarantor",
            PartyTwoRole = "Supported entity",
            Terms = new() { StructuredTerms = new(Guarantee: new("External beneficiary", 1000000m)) }
        };
        IntercompanyAgreementRules.ValidateComplete(draft);
        var preview = Service(BusinessDayCalendars.WeekendsOnly).Preview(draft, "guarantee-hash");
        Assert.Equal("Unavailable", preview.Status);
        Assert.Empty(preview.Periods);
        Assert.Throws<IntercompanyAgreementValidationException>(() => IntercompanyAgreementRules.ValidateComplete(draft with
        { Terms = new() { StructuredTerms = new(Guarantee: new("External beneficiary", -1m)) } }));
        Assert.Throws<IntercompanyAgreementValidationException>(() => IntercompanyAgreementRules.ValidateComplete(draft with
        { Terms = new() { StructuredTerms = new(Schedule(), Guarantee: new("External beneficiary", 1000000m)) } }));
        Assert.Throws<IntercompanyAgreementValidationException>(() => IntercompanyAgreementRules.ValidateComplete(draft with
        { Terms = draft.Terms! with { FixedAnnualRate = 0.05m } }));
    }

    [Fact]
    public void UnstructuredRevisionsRemainReadableButNeverAcquireDatePreviews()
    {
        var draft = Loan() with { Terms = new(RateDescription: "Five percent, monthly") };
        IntercompanyAgreementRules.ValidateComplete(draft);
        var preview = Service(BusinessDayCalendars.WeekendsOnly).Preview(draft, "legacy-hash");
        Assert.Equal("Unstructured", preview.TermsFormat);
        Assert.Equal("Unavailable", preview.Status);
        Assert.Empty(preview.Periods);
    }

    private static IntercompanyAgreementDraftDto Loan() => new("IC-TEST", "Synthetic loan", First, "Lender", Second, "Borrower",
        IntercompanyAgreementTypeDto.Loan, "USD", new(2026, 1, 1), new(2027, 1, 1),
        new()
        {
            StructuredTerms = new(Schedule(), IntercompanyRateBasisDto.Fixed, 0.05m,
            DayCount: IntercompanyDayCountDto.Actual360, Compounding: IntercompanyCompoundingDto.Simple)
        });
    private static IntercompanyScheduleDto Schedule() => new(new(2026, 1, 1), new(2027, 1, 1),
        IntercompanyFrequencyDto.Monthly, "WEEKENDS", BusinessDayConvention.Following, 1);
    private static IntercompanyStructuredTermsPreviewService Service(params IBusinessDayCalendar[] calendars)
        => new(new StaticBusinessDayCalendarSource(calendars));
}
