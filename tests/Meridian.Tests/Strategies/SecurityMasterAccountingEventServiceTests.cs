using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.Instruments.AssetOperations;
using Meridian.Strategies.Services;

namespace Meridian.Tests.Strategies;

public sealed class SecurityMasterAccountingEventServiceTests
{
    private static readonly Guid BondSecurityId = Guid.Parse("aaaaaaaa-1111-2222-3333-bbbbbbbbbbbb");
    private static readonly Guid BondPositionId = Guid.Parse("bbbbbbbb-2222-4222-8222-cccccccccccc");

    [Fact]
    public void Generate_FixedCouponBond_ShouldCreateAccrualAndBalancedJournalPreview()
    {
        var service = new SecurityMasterAccountingEventService();

        var result = service.Generate(CreateRequest(
            actualActivity:
            [
                new SecurityActualCashActivity(
                    SourceName: "custodian",
                    ExternalTransactionId: "custodian-coupon-1",
                    AccountId: "acct-1",
                    SecurityId: BondSecurityId,
                    Symbol: "BOND1",
                    CashAmount: 3_000m,
                    PrincipalAmount: 0m,
                    IncomeAmount: 3_000m,
                    PayDate: new DateOnly(2026, 1, 31),
                    Classification: "Income")
            ]));

        result.AccrualCalculations.Should().ContainSingle();
        var accrual = result.AccrualCalculations.Single();
        accrual.AccrualDays.Should().Be(31);
        accrual.AccruedAmount.Should().Be(509.59m);

        result.ExpectedEvents.Should().Contain(item =>
            item.EventKind == ExpectedAccountingEventKindDto.AccrueInterestIncome &&
            item.IncomeAmount == 509.59m &&
            item.Provenance.Contains(BondSecurityId.ToString("N"), StringComparison.Ordinal));
        result.JournalPreviews.Should().Contain(preview =>
            preview.ExpectedEventId == accrual.EventId &&
            preview.IsBalanced &&
            preview.RequiresOperatorApproval);
        result.Issues.Should().BeEmpty();
    }

    [Fact]
    public void Generate_FactorPaydown_ShouldRecognizePrincipalAtParNotCarryingPrice()
    {
        var service = new SecurityMasterAccountingEventService();
        var request = CreateRequest(
            factorSchedule:
            [
                new SecurityFactorScheduleEntry(
                    BondSecurityId,
                    new DateOnly(2026, 1, 20),
                    PriorFactor: 1.00m,
                    CurrentFactor: 0.97m,
                    Source: "custodian-factor-file",
                    EvidenceLink: "evidence://factor/bond-2026-01",
                    SourceContentHash: "sha256:bond-factor-2026-01")
            ]);

        var result = service.Generate(request);

        var factorEvent = result.ExpectedEvents.Single(item =>
            item.EventKind == ExpectedAccountingEventKindDto.RecognizePrincipalPaydown);
        factorEvent.PrincipalAmount.Should().Be(3_000m, "factor paydowns are expected at par, not at the bond carrying price");
        result.JournalPreviews.Single(preview => preview.ExpectedEventId == factorEvent.EventId)
            .Lines.Should().Contain(line => line.AccountName == "Cash" && line.Debit == 3_000m);
    }

    [Fact]
    public void Generate_MortgageBackedFactorPaydown_ShouldUseSecurityMasterFactorSchedule()
    {
        var service = new SecurityMasterAccountingEventService();
        var request = CreateRequest(
            security: new SecurityMasterAccountingSecurity(
                BondSecurityId,
                "MBS1",
                "MortgageBackedSecurity",
                "USD",
                new SecurityFixedIncomeTerms(
                    CouponRate: 0m,
                    CouponType: "Fixed",
                    DayCountConvention: "ACT/365",
                    PaymentFrequencyPerYear: 12,
                    IssueDate: new DateOnly(2025, 1, 1),
                    MaturityDate: new DateOnly(2035, 1, 1),
                    AccrualStartDate: new DateOnly(2026, 1, 1),
                    CurrentFactor: 0.9625m,
                    OriginalFace: 100_000m,
                    CurrentFace: 96_250m,
                    RequiresFactorSchedule: true),
                new SecurityAccountingRule("AvailableForSale", "GAAP")),
            position: new SecurityMasterAccountingPosition(
                Symbol: "MBS1",
                SecurityId: BondSecurityId,
                AccountId: "acct-1",
                ParAmount: 100_000m,
                CarryingPrice: 0.91m,
                PositionId: BondPositionId),
            factorSchedule:
            [
                new SecurityFactorScheduleEntry(
                    BondSecurityId,
                    new DateOnly(2026, 1, 20),
                    PriorFactor: 0.98m,
                    CurrentFactor: 0.9625m,
                    Source: "custodian-factor-file",
                    EvidenceLink: "factor-evidence-1",
                    SourceContentHash: "sha256:mbs-factor-2026-01")
            ]);

        var result = service.Generate(request);

        result.Issues.Should().NotContain(issue => issue.Code == "SM_UNSUPPORTED_ACCOUNTING_INSTRUMENT");
        var factorEvent = result.ExpectedEvents.Single(item =>
            item.EventKind == ExpectedAccountingEventKindDto.RecognizePrincipalPaydown);
        factorEvent.Symbol.Should().Be("MBS1");
        factorEvent.PrincipalAmount.Should().Be(1_750m);
        factorEvent.Provenance.Should().Contain("factor-source:custodian-factor-file");
        factorEvent.EconomicEvent.Should().NotBeNull();
        factorEvent.EconomicEvent!.EvidenceLinks.Should().Equal("factor-evidence-1");
        factorEvent.ProjectionLineage!.ModelKey.Should().Be("mbs-factor-paydown");
        factorEvent.EvidenceLinks.Should().Equal("factor-evidence-1");
    }

    [Fact]
    public void Generate_FactorBasedSecurityWithoutSchedule_ShouldReturnMissingFactorScheduleIssue()
    {
        var service = new SecurityMasterAccountingEventService();
        var request = CreateRequest(security: new SecurityMasterAccountingSecurity(
            BondSecurityId,
            "BOND1",
            "Bond",
            "USD",
            new SecurityFixedIncomeTerms(
                CouponRate: 0.06m,
                CouponType: "Fixed",
                DayCountConvention: "ACT/365",
                PaymentFrequencyPerYear: 2,
                IssueDate: new DateOnly(2025, 1, 1),
                NextCouponDate: new DateOnly(2026, 1, 31),
                MaturityDate: new DateOnly(2030, 1, 1),
                AccrualStartDate: new DateOnly(2026, 1, 1),
                CurrentFactor: 0.97m,
                OriginalFace: 100_000m,
                CurrentFace: 97_000m,
                RequiresFactorSchedule: true),
            new SecurityAccountingRule("AvailableForSale", "GAAP")));

        var result = service.Generate(request);

        result.Issues.Should().ContainSingle(issue =>
            issue.Code == "FACTOR_SCHEDULE_MISSING" &&
            issue.Severity == ReconciliationBreakSeverity.High);
        result.ExpectedEvents.Should().NotContain(item =>
            item.EventKind == ExpectedAccountingEventKindDto.RecognizePrincipalPaydown);
    }

    [Fact]
    public void Generate_FactorBasedSecurityWithPriorOnlySchedule_ShouldReturnStaleFactorIssue()
    {
        var service = new SecurityMasterAccountingEventService();
        var request = CreateRequest(
            security: new SecurityMasterAccountingSecurity(
                BondSecurityId,
                "MBS1",
                "MortgageBackedSecurity",
                "USD",
                new SecurityFixedIncomeTerms(
                    CouponRate: 0.03m,
                    CouponType: "Fixed",
                    DayCountConvention: "ACT/365",
                    PaymentFrequencyPerYear: 12,
                    IssueDate: new DateOnly(2025, 1, 1),
                    MaturityDate: new DateOnly(2035, 1, 1),
                    AccrualStartDate: new DateOnly(2026, 1, 1),
                    CurrentFactor: 0.97m,
                    OriginalFace: 100_000m,
                    CurrentFace: 97_000m,
                    RequiresFactorSchedule: true),
                new SecurityAccountingRule("AvailableForSale", "GAAP")),
            factorSchedule:
            [
                new SecurityFactorScheduleEntry(
                    BondSecurityId,
                    new DateOnly(2025, 12, 15),
                    PriorFactor: 1.00m,
                    CurrentFactor: 0.98m,
                    Source: "prior-month-factor-file",
                    EvidenceLink: "evidence://factor/prior-month",
                    SourceContentHash: "sha256:prior-month")
            ]);

        var result = service.Generate(request);

        result.Issues.Should().ContainSingle(issue =>
            issue.Code == "FACTOR_STALE" &&
            issue.Severity == ReconciliationBreakSeverity.High);
        result.ExpectedEvents.Should().NotContain(item =>
            item.EventKind == ExpectedAccountingEventKindDto.RecognizePrincipalPaydown);
    }

    [Fact]
    public void Generate_CouponCoverageGap_ShouldStillGenerateFactorPaydowns()
    {
        // A canonical StructuredCredit legitimately carries no coupon rate, day count, or payment
        // frequency: those coverage gaps gate COUPON accrual generation only. Factor paydowns are
        // principal events driven by the retained factor evidence alone — skipping them for
        // missing accrual inputs would silently drop principal events the schedule fully supports.
        var service = new SecurityMasterAccountingEventService();
        var request = CreateRequest(
            security: new SecurityMasterAccountingSecurity(
                BondSecurityId,
                "SCIO1",
                "MortgageBackedSecurity",
                "USD",
                new SecurityFixedIncomeTerms(
                    CouponRate: null,
                    CouponType: null,
                    DayCountConvention: null,
                    PaymentFrequencyPerYear: null,
                    CurrentFactor: 0.97m,
                    OriginalFace: 100_000m,
                    CurrentFace: 97_000m),
                new SecurityAccountingRule("AvailableForSale", "GAAP")),
            factorSchedule:
            [
                new SecurityFactorScheduleEntry(
                    BondSecurityId,
                    new DateOnly(2026, 1, 20),
                    PriorFactor: 1.00m,
                    CurrentFactor: 0.97m,
                    Source: "trustee-report",
                    EvidenceLink: "evidence://factor/scio-2026-01",
                    SourceContentHash: "sha256:scio-factor-2026-01")
            ]);

        var result = service.Generate(request);

        result.Issues.Should().Contain(issue => issue.Code == "SM_COUPON_TERMS_MISSING");
        result.ExpectedEvents.Should().Contain(item =>
            item.EventKind == ExpectedAccountingEventKindDto.RecognizePrincipalPaydown &&
            item.PrincipalAmount == 3_000m);
        result.ExpectedEvents.Should().NotContain(item =>
            item.EventKind == ExpectedAccountingEventKindDto.AccrueInterestIncome);
    }

    [Fact]
    public void Generate_MissingAccountingRule_ShouldKeepFactorPaydownEventWithoutPreview()
    {
        // Valid factor evidence with NO accounting rule: the expected principal event still
        // surfaces (the retained evidence supports it) alongside the high-severity missing-rule
        // issue, but no journal preview is fabricated from an absent classification — previously
        // this dereferenced the null rule and crashed the reconciliation run.
        var service = new SecurityMasterAccountingEventService();
        var request = CreateRequest(
            security: new SecurityMasterAccountingSecurity(
                BondSecurityId,
                "BOND1",
                "Bond",
                "USD",
                new SecurityFixedIncomeTerms(
                    CouponRate: 0.06m,
                    CouponType: "Fixed",
                    DayCountConvention: "ACT/365",
                    PaymentFrequencyPerYear: 2,
                    IssueDate: new DateOnly(2025, 1, 1),
                    NextCouponDate: new DateOnly(2026, 1, 31),
                    MaturityDate: new DateOnly(2030, 1, 1),
                    AccrualStartDate: new DateOnly(2026, 1, 1),
                    CurrentFactor: 0.97m,
                    OriginalFace: 100_000m,
                    CurrentFace: 97_000m),
                AccountingRule: null),
            factorSchedule:
            [
                new SecurityFactorScheduleEntry(
                    BondSecurityId,
                    new DateOnly(2026, 1, 20),
                    PriorFactor: 1.00m,
                    CurrentFactor: 0.97m,
                    Source: "custodian-factor-file",
                    EvidenceLink: "evidence://factor/bond-2026-01",
                    SourceContentHash: "sha256:bond-factor-2026-01")
            ]);

        var result = service.Generate(request);

        result.Issues.Should().Contain(issue => issue.Code == "SECURITY_ACCOUNTING_RULE_MISSING");
        var factorEvent = result.ExpectedEvents.Should().ContainSingle(item =>
            item.EventKind == ExpectedAccountingEventKindDto.RecognizePrincipalPaydown).Subject;
        factorEvent.PrincipalAmount.Should().Be(3_000m);
        result.JournalPreviews.Should().NotContain(preview => preview.ExpectedEventId == factorEvent.EventId,
            "a journal preview needs the accounting rule's ledger classification");
    }

    [Fact]
    public void Generate_MissingTerms_ShouldReturnStructuredPostureIssues()
    {
        var service = new SecurityMasterAccountingEventService();
        var request = CreateRequest(security: new SecurityMasterAccountingSecurity(
            BondSecurityId,
            "BOND1",
            "Bond",
            "USD",
            FixedIncomeTerms: new SecurityFixedIncomeTerms(
                CouponRate: null,
                CouponType: "Fixed",
                DayCountConvention: null,
                PaymentFrequencyPerYear: null),
            AccountingRule: null));

        var result = service.Generate(request);

        result.ExpectedEvents.Should().BeEmpty();
        result.Issues.Select(issue => issue.Code).Should().Contain([
            "SECURITY_ACCOUNTING_RULE_MISSING",
            "SM_COUPON_TERMS_MISSING",
            "SM_DAY_COUNT_MISSING",
            "SM_PAYMENT_FREQUENCY_MISSING",
            "SM_ACCOUNTING_CLASSIFICATION_MISSING"
        ]);
    }

    [Fact]
    public void Generate_ExpectedCouponWithoutActualActivity_ShouldCreateMissingActualIssue()
    {
        var service = new SecurityMasterAccountingEventService();

        var result = service.Generate(CreateRequest());

        result.ExpectedEvents.Should().Contain(item => item.EventKind == ExpectedAccountingEventKindDto.ReceiveCashInterest);
        result.Issues.Should().Contain(issue =>
            issue.Code == "ACCRUAL_ACTUAL_EVENT_MISSING" &&
            issue.ExpectedAmount == 3_000m);
    }

    [Fact]
    public void Generate_ActualPrincipalForCoupon_ShouldCreateClassificationMismatch()
    {
        var service = new SecurityMasterAccountingEventService();
        var request = CreateRequest(
            actualActivity:
            [
                new SecurityActualCashActivity(
                    SourceName: "custodian",
                    ExternalTransactionId: "custodian-row-1",
                    AccountId: "acct-1",
                    SecurityId: BondSecurityId,
                    Symbol: "BOND1",
                    CashAmount: 3_000m,
                    PrincipalAmount: 3_000m,
                    IncomeAmount: 0m,
                    PayDate: new DateOnly(2026, 1, 31),
                    Classification: "Principal")
            ]);

        var result = service.Generate(request);

        result.Issues.Should().ContainSingle(issue =>
            issue.Code == "ACCRUAL_CLASSIFICATION_MISMATCH" &&
            issue.ExpectedAmount == 3_000m &&
            issue.ActualAmount == 3_000m);
    }

    [Fact]
    public void Generate_CouponAndPrincipalOnDifferentDates_ShouldNotCrossMatchActualRows()
    {
        var service = new SecurityMasterAccountingEventService();
        var request = CreateRequest(
            factorSchedule:
            [
                new SecurityFactorScheduleEntry(
                    BondSecurityId,
                    new DateOnly(2026, 1, 20),
                    PriorFactor: 1.00m,
                    CurrentFactor: 0.97m,
                    Source: "custodian-factor-file",
                    EvidenceLink: "evidence://factor/bond-2026-01",
                    SourceContentHash: "sha256:bond-factor-2026-01")
            ],
            actualActivity:
            [
                new SecurityActualCashActivity(
                    SourceName: "custodian",
                    ExternalTransactionId: "custodian-principal-1",
                    AccountId: "acct-1",
                    SecurityId: BondSecurityId,
                    Symbol: "BOND1",
                    CashAmount: 3_000m,
                    PrincipalAmount: 3_000m,
                    IncomeAmount: 0m,
                    PayDate: new DateOnly(2026, 1, 20),
                    Classification: "Principal"),
                new SecurityActualCashActivity(
                    SourceName: "custodian",
                    ExternalTransactionId: "custodian-coupon-1",
                    AccountId: "acct-1",
                    SecurityId: BondSecurityId,
                    Symbol: "BOND1",
                    CashAmount: 3_000m,
                    PrincipalAmount: 0m,
                    IncomeAmount: 3_000m,
                    PayDate: new DateOnly(2026, 1, 31),
                    Classification: "Income")
            ]);

        var result = service.Generate(request);

        result.ExpectedEvents.Should().Contain(item => item.EventKind == ExpectedAccountingEventKindDto.ReceiveCashInterest);
        result.ExpectedEvents.Should().Contain(item => item.EventKind == ExpectedAccountingEventKindDto.RecognizePrincipalPaydown);
        result.Issues.Select(static issue => issue.Code).Should().NotContain([
            "ACCRUAL_CLASSIFICATION_MISMATCH",
            "FACTOR_PAYDOWN_CLASSIFICATION_MISMATCH",
            "ACCRUAL_AMOUNT_MISMATCH",
            "FACTOR_PAYDOWN_AMOUNT_MISMATCH"
        ]);
    }

    [Fact]
    public void Generate_FactorPaydown_ShouldKeepEventIdentityStableAcrossRunIds()
    {
        var service = new SecurityMasterAccountingEventService();
        var schedule = new SecurityFactorScheduleEntry(
            BondSecurityId,
            new DateOnly(2026, 1, 20),
            0.98m,
            0.9625m,
            "custodian-factor-file",
            "evidence://factor/mbs-2026-01",
            "sha256:stable-factor-row");
        var firstRequest = CreateRequest(factorSchedule: [schedule]);
        var secondRequest = firstRequest with { RunId = "different-reconciliation-run" };

        var first = service.Generate(firstRequest).ExpectedEvents.Single(item =>
            item.EventKind == ExpectedAccountingEventKindDto.RecognizePrincipalPaydown);
        var second = service.Generate(secondRequest).ExpectedEvents.Single(item =>
            item.EventKind == ExpectedAccountingEventKindDto.RecognizePrincipalPaydown);

        second.EventId.Should().Be(first.EventId);
        second.IdempotencyKey.Should().Be(first.IdempotencyKey);
        second.EconomicEvent!.EventId.Should().Be(first.EconomicEvent!.EventId);
    }

    [Fact]
    public void Generate_CouponOnExclusivePeriodEnd_BelongsToTheNextPeriod()
    {
        // The period end is EXCLUSIVE (the adapter supplies the next month's first day): a coupon
        // dated exactly on the boundary is the NEXT period's event, where the same date is that
        // run's PeriodStart. Emitting it here too would duplicate the expected cash event and
        // journal preview for every security paying on the first of a month.
        var service = new SecurityMasterAccountingEventService();
        var result = service.Generate(CreateRequest(security: new SecurityMasterAccountingSecurity(
            BondSecurityId,
            "BOND1",
            "Bond",
            "USD",
            new SecurityFixedIncomeTerms(
                CouponRate: 0.06m,
                CouponType: "Fixed",
                DayCountConvention: "ACT/365",
                PaymentFrequencyPerYear: 2,
                IssueDate: new DateOnly(2025, 1, 1),
                NextCouponDate: new DateOnly(2026, 2, 1),
                MaturityDate: new DateOnly(2030, 1, 1),
                AccrualStartDate: new DateOnly(2026, 1, 1),
                CurrentFactor: 1m),
            new SecurityAccountingRule("AvailableForSale", "GAAP"))));

        result.ExpectedEvents.Should().Contain(item => item.EventKind == ExpectedAccountingEventKindDto.AccrueInterestIncome);
        result.ExpectedEvents.Should().NotContain(
            item => item.EventKind == ExpectedAccountingEventKindDto.ReceiveCashInterest,
            "a coupon dated on the exclusive period end is emitted by the next period's run, not this one");
    }

    [Fact]
    public void Generate_UnchangedFactorObservation_IsCoverageNotAPaydown()
    {
        // An observation whose factor equals its prior is the period's factor COVERAGE — no
        // principal moved. It must neither project a paydown nor fail closed on the evidence
        // requirement (this entry carries no evidence link, which for a REAL paydown is a
        // high-severity issue).
        var result = new SecurityMasterAccountingEventService().Generate(CreateRequest(
            factorSchedule:
            [
                new SecurityFactorScheduleEntry(
                    BondSecurityId,
                    new DateOnly(2026, 1, 20),
                    PriorFactor: 0.80m,
                    CurrentFactor: 0.80m,
                    Source: "trustee-report",
                    SourceContentHash: "sha256:unchanged-factor")
            ]));

        result.ExpectedEvents.Should().NotContain(item =>
            item.EventKind == ExpectedAccountingEventKindDto.RecognizePrincipalPaydown);
        result.Issues.Should().NotContain(issue => issue.Code == "FACTOR_PAYDOWN_EVIDENCE_REQUIRED",
            "a no-change observation has nothing to evidence");
        result.Issues.Should().NotContain(issue => issue.Code == "FACTOR_SCHEDULE_MISSING",
            "the unchanged observation still counts as the period's factor coverage");
    }

    [Fact]
    public void Generate_FactorPaydownWithoutEvidence_ShouldFailClosed()
    {
        var result = new SecurityMasterAccountingEventService().Generate(CreateRequest(
            factorSchedule:
            [
                new SecurityFactorScheduleEntry(
                    BondSecurityId,
                    new DateOnly(2026, 1, 20),
                    1m,
                    0.97m,
                    "custodian-factor-file",
                    SourceContentHash: "sha256:factor-row")
            ]));

        result.ExpectedEvents.Should().NotContain(item =>
            item.EventKind == ExpectedAccountingEventKindDto.RecognizePrincipalPaydown);
        result.Issues.Should().Contain(issue => issue.Code == "FACTOR_PAYDOWN_EVIDENCE_REQUIRED");
    }

    [Fact]
    public void Generate_FactorPaydownWithoutDurablePosition_ShouldFailClosed()
    {
        var result = new SecurityMasterAccountingEventService().Generate(CreateRequest(
            position: new SecurityMasterAccountingPosition(
                "BOND1",
                BondSecurityId,
                "acct-1",
                100_000m,
                0.94m),
            factorSchedule:
            [
                new SecurityFactorScheduleEntry(
                    BondSecurityId,
                    new DateOnly(2026, 1, 20),
                    1m,
                    0.97m,
                    "custodian-factor-file",
                    "evidence://factor/bond-2026-01",
                    "sha256:factor-row")
            ]));

        result.ExpectedEvents.Should().NotContain(item =>
            item.EventKind == ExpectedAccountingEventKindDto.RecognizePrincipalPaydown);
        result.Issues.Should().ContainSingle(issue => issue.Code == "FACTOR_PAYDOWN_POSITION_REQUIRED");
    }

    [Fact]
    public void Generate_MultipleFactorRows_ShouldAdvanceExpectedPositionVersionSequentially()
    {
        var projector = new RecordingFactorPaydownProjector();
        var result = new SecurityMasterAccountingEventService(projector).Generate(CreateRequest(
            position: new SecurityMasterAccountingPosition(
                "BOND1",
                BondSecurityId,
                "acct-1",
                100_000m,
                0.94m,
                BondPositionId,
                PositionVersion: 7),
            factorSchedule:
            [
                new SecurityFactorScheduleEntry(
                    BondSecurityId,
                    new DateOnly(2026, 1, 15),
                    1m,
                    0.98m,
                    "custodian-factor-file",
                    "evidence://factor/bond-2026-01-a",
                    "sha256:factor-row-a"),
                new SecurityFactorScheduleEntry(
                    BondSecurityId,
                    new DateOnly(2026, 1, 20),
                    0.98m,
                    0.97m,
                    "custodian-factor-file",
                    "evidence://factor/bond-2026-01-b",
                    "sha256:factor-row-b")
            ]));

        result.ExpectedEvents.Count(item =>
            item.EventKind == ExpectedAccountingEventKindDto.RecognizePrincipalPaydown).Should().Be(2);
        projector.Requests.Select(static request => request.PositionVersion).Should().Equal(7, 8);
        projector.Requests.Select(static request => request.ExpectedPositionVersion).Should().Equal(7, 8);
    }

    private static SecurityMasterAccountingEventRequest CreateRequest(
        SecurityMasterAccountingSecurity? security = null,
        SecurityMasterAccountingPosition? position = null,
        IReadOnlyList<SecurityFactorScheduleEntry>? factorSchedule = null,
        IReadOnlyList<SecurityActualCashActivity>? actualActivity = null)
    {
        security ??= new SecurityMasterAccountingSecurity(
            BondSecurityId,
            "BOND1",
            "Bond",
            "USD",
            new SecurityFixedIncomeTerms(
                CouponRate: 0.06m,
                CouponType: "Fixed",
                DayCountConvention: "ACT/365",
                PaymentFrequencyPerYear: 2,
                IssueDate: new DateOnly(2025, 1, 1),
                NextCouponDate: new DateOnly(2026, 1, 31),
                MaturityDate: new DateOnly(2030, 1, 1),
                AccrualStartDate: new DateOnly(2026, 1, 1),
                CurrentFactor: 1m),
            new SecurityAccountingRule("AvailableForSale", "GAAP"));

        return new SecurityMasterAccountingEventRequest(
            RunId: "run-accounting",
            PeriodStart: new DateOnly(2026, 1, 1),
            PeriodEnd: new DateOnly(2026, 2, 1),
            Securities: [security],
            Positions:
            [
                position ?? new SecurityMasterAccountingPosition(
                    Symbol: "BOND1",
                    SecurityId: BondSecurityId,
                    AccountId: "acct-1",
                    ParAmount: 100_000m,
                    CarryingPrice: 0.94m,
                    PositionId: BondPositionId)
            ],
            FactorSchedule: factorSchedule,
            ActualActivity: actualActivity);
    }

    private sealed class RecordingFactorPaydownProjector : IFactorPaydownProjectionService
    {
        private readonly FactorPaydownProjectionService _inner = new();

        public List<FactorPaydownProjectionRequest> Requests { get; } = [];

        public FactorPaydownProjectionResult Project(FactorPaydownProjectionRequest request)
        {
            Requests.Add(request);
            return _inner.Project(request);
        }
    }
}
