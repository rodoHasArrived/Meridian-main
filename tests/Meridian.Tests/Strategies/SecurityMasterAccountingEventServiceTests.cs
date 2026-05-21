using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.Strategies.Services;

namespace Meridian.Tests.Strategies;

public sealed class SecurityMasterAccountingEventServiceTests
{
    private static readonly Guid BondSecurityId = Guid.Parse("aaaaaaaa-1111-2222-3333-bbbbbbbbbbbb");

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
                    Source: "custodian-factor-file")
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
                CarryingPrice: 0.91m),
            factorSchedule:
            [
                new SecurityFactorScheduleEntry(
                    BondSecurityId,
                    new DateOnly(2026, 1, 20),
                    PriorFactor: 0.98m,
                    CurrentFactor: 0.9625m,
                    Source: "custodian-factor-file",
                    EvidenceLink: "factor-evidence-1")
            ]);

        var result = service.Generate(request);

        result.Issues.Should().NotContain(issue => issue.Code == "SM_UNSUPPORTED_ACCOUNTING_INSTRUMENT");
        var factorEvent = result.ExpectedEvents.Single(item =>
            item.EventKind == ExpectedAccountingEventKindDto.RecognizePrincipalPaydown);
        factorEvent.Symbol.Should().Be("MBS1");
        factorEvent.PrincipalAmount.Should().Be(1_750m);
        factorEvent.Provenance.Should().Contain("factor-source:custodian-factor-file");
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
                    Source: "custodian-factor-file")
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
                    CarryingPrice: 0.94m)
            ],
            FactorSchedule: factorSchedule,
            ActualActivity: actualActivity);
    }
}
