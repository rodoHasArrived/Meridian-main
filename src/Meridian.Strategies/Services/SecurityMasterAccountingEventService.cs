using System.Security.Cryptography;
using System.Text;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.Instruments.AssetOperations;

namespace Meridian.Strategies.Services;

public interface ISecurityMasterAccountingEventService
{
    SecurityMasterAccountingEventResult Generate(SecurityMasterAccountingEventRequest request);
}

public interface ISecurityMasterAccountingEventSourceAdapter
{
    Task<SecurityMasterAccountingEventRequest?> BuildRequestAsync(
        StrategyRunDetail detail,
        ReconciliationRunRequest request,
        CancellationToken ct = default);
}

public sealed record SecurityMasterAccountingEventRequest(
    string RunId,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    IReadOnlyList<SecurityMasterAccountingSecurity> Securities,
    IReadOnlyList<SecurityMasterAccountingPosition> Positions,
    IReadOnlyList<SecurityFactorScheduleEntry>? FactorSchedule = null,
    IReadOnlyList<SecurityActualCashActivity>? ActualActivity = null,
    decimal AmountTolerance = 0.01m);

public sealed record SecurityMasterAccountingSecurity(
    Guid SecurityId,
    string Symbol,
    string AssetClass,
    string Currency,
    SecurityFixedIncomeTerms? FixedIncomeTerms = null,
    SecurityAccountingRule? AccountingRule = null);

public sealed record SecurityFixedIncomeTerms(
    decimal? CouponRate,
    string? CouponType,
    string? DayCountConvention,
    int? PaymentFrequencyPerYear,
    DateOnly? IssueDate = null,
    DateOnly? DatedDate = null,
    DateOnly? FirstCouponDate = null,
    DateOnly? NextCouponDate = null,
    DateOnly? MaturityDate = null,
    DateOnly? AccrualStartDate = null,
    decimal? CurrentFactor = null,
    decimal? OriginalFace = null,
    decimal? CurrentFace = null,
    bool RequiresFactorSchedule = false);

public sealed record SecurityAccountingRule(
    string AccountingClassification,
    string ReportingBasis,
    string AccruedInterestReceivableAccount = "Accrued Interest Receivable",
    string CouponIncomeAccount = "Coupon Income",
    string CashAccount = "Cash",
    string PrincipalAccount = "Securities");

public sealed record SecurityMasterAccountingPosition(
    string Symbol,
    Guid? SecurityId,
    string AccountId,
    decimal ParAmount,
    decimal? CarryingPrice = null,
    Guid? PositionId = null,
    long PositionVersion = 1);

public sealed record SecurityFactorScheduleEntry(
    Guid SecurityId,
    DateOnly AsOfDate,
    decimal PriorFactor,
    decimal CurrentFactor,
    string Source,
    string? EvidenceLink = null,
    string? SourceContentHash = null);

internal sealed record FactorScheduleCoverageIssue(string Code, Func<string, string> Message)
{
    public static FactorScheduleCoverageIssue Missing { get; } = new(
        "FACTOR_SCHEDULE_MISSING",
        symbol => $"Security '{symbol}' is factor-based and requires a factor schedule before expected principal paydowns can be generated.");

    public static FactorScheduleCoverageIssue Stale { get; } = new(
        "FACTOR_STALE",
        symbol => $"Security '{symbol}' factor schedule does not include a current-period factor for expected principal paydown generation.");
}

public sealed record SecurityActualCashActivity(
    string SourceName,
    string? ExternalTransactionId,
    string AccountId,
    Guid? SecurityId,
    string? Symbol,
    decimal CashAmount,
    decimal PrincipalAmount,
    decimal IncomeAmount,
    DateOnly PayDate,
    string Classification,
    string? EvidenceLink = null);

public sealed record SecurityMasterAccountingEventResult(
    IReadOnlyList<ExpectedAccountingEventDto> ExpectedEvents,
    IReadOnlyList<AccrualCalculationResultDto> AccrualCalculations,
    IReadOnlyList<ExpectedJournalPreviewDto> JournalPreviews,
    IReadOnlyList<SecurityMasterAccountingIssueDto> Issues);

public sealed class NullSecurityMasterAccountingEventSourceAdapter : ISecurityMasterAccountingEventSourceAdapter
{
    public Task<SecurityMasterAccountingEventRequest?> BuildRequestAsync(
        StrategyRunDetail detail,
        ReconciliationRunRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(detail);
        ArgumentNullException.ThrowIfNull(request);
        return Task.FromResult<SecurityMasterAccountingEventRequest?>(null);
    }
}

public sealed class SecurityMasterAccountingEventService : ISecurityMasterAccountingEventService
{
    private readonly IFactorPaydownProjectionService _factorPaydownProjector;

    public SecurityMasterAccountingEventService(IFactorPaydownProjectionService? factorPaydownProjector = null)
    {
        _factorPaydownProjector = factorPaydownProjector ?? new FactorPaydownProjectionService();
    }

    public SecurityMasterAccountingEventResult Generate(SecurityMasterAccountingEventRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.PeriodEnd < request.PeriodStart)
        {
            throw new ArgumentException("Period end must be on or after period start.", nameof(request));
        }

        var securities = request.Securities.ToDictionary(static security => security.SecurityId);
        var securitiesBySymbol = request.Securities
            .Where(static security => !string.IsNullOrWhiteSpace(security.Symbol))
            .GroupBy(static security => security.Symbol.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.OrdinalIgnoreCase);

        var events = new List<ExpectedAccountingEventDto>();
        var accruals = new List<AccrualCalculationResultDto>();
        var previews = new List<ExpectedJournalPreviewDto>();
        var issues = new List<SecurityMasterAccountingIssueDto>();

        foreach (var position in request.Positions)
        {
            var security = ResolveSecurity(position, securities, securitiesBySymbol);
            if (security is null)
            {
                issues.Add(CreateIssue(
                    "SECURITY_ACCOUNTING_RULE_MISSING",
                    "security-master",
                    position.Symbol,
                    position.AccountId,
                    $"Position '{position.Symbol}' cannot generate expected accounting events because no Security Master accounting record was provided.",
                    ReconciliationBreakSeverity.High));
                continue;
            }

            if (!IsFixedIncome(security.AssetClass))
            {
                issues.Add(CreateIssue(
                    "SM_UNSUPPORTED_ACCOUNTING_INSTRUMENT",
                    "security-master",
                    security.Symbol,
                    position.AccountId,
                    $"Security '{security.Symbol}' asset class '{security.AssetClass}' is not supported by the first Security Master accounting-event slice.",
                    ReconciliationBreakSeverity.Info));
                continue;
            }

            if (security.AccountingRule is null)
            {
                issues.Add(CreateIssue(
                    "SECURITY_ACCOUNTING_RULE_MISSING",
                    "security-master",
                    security.Symbol,
                    position.AccountId,
                    $"Security '{security.Symbol}' is missing an accounting rule for journal preview generation.",
                    ReconciliationBreakSeverity.High));
            }

            // Coupon-coverage completeness gates COUPON accrual generation only. Factor paydowns
            // are principal events driven by the factor schedule alone: a canonical
            // StructuredCredit legitimately carries no coupon rate, day count, or payment
            // frequency in its economic terms, and skipping its paydowns for missing accrual
            // inputs would silently drop principal events (and posting candidates) the record's
            // retained factor evidence fully supports. The coverage issues still surface.
            var coverageIssues = ValidateFixedIncomeCoverage(security, position.AccountId);
            issues.AddRange(coverageIssues);
            if (!coverageIssues.Any(static issue => issue.Severity >= ReconciliationBreakSeverity.High))
            {
                GenerateFixedCouponEvents(request, security, position, events, accruals, previews);
            }

            if (RequiresFactorSchedule(security) &&
                GetFactorScheduleCoverageStatus(request, security.SecurityId) is { } factorScheduleStatus)
            {
                issues.Add(CreateIssue(
                    factorScheduleStatus.Code,
                    "security-master",
                    security.Symbol,
                    position.AccountId,
                    factorScheduleStatus.Message(security.Symbol),
                    ReconciliationBreakSeverity.High));
                continue;
            }

            GenerateFactorPaydownEvents(request, security, position, events, previews, issues);
        }

        ReconcileActualActivity(request, events, issues);

        return new SecurityMasterAccountingEventResult(
            events,
            accruals,
            previews,
            issues
                .DistinctBy(static issue => $"{issue.Code}|{issue.Source}|{issue.Symbol}|{issue.AccountId}|{issue.Reason}", StringComparer.OrdinalIgnoreCase)
                .ToArray());
    }

    private static SecurityMasterAccountingSecurity? ResolveSecurity(
        SecurityMasterAccountingPosition position,
        IReadOnlyDictionary<Guid, SecurityMasterAccountingSecurity> securities,
        IReadOnlyDictionary<string, SecurityMasterAccountingSecurity> securitiesBySymbol)
    {
        if (position.SecurityId is Guid securityId && securities.TryGetValue(securityId, out var securityById))
        {
            return securityById;
        }

        return securitiesBySymbol.TryGetValue(position.Symbol, out var securityBySymbol) ? securityBySymbol : null;
    }

    private static bool IsFixedIncome(string assetClass) =>
        assetClass.Equals("Bond", StringComparison.OrdinalIgnoreCase) ||
        assetClass.Equals("CertificateOfDeposit", StringComparison.OrdinalIgnoreCase) ||
        assetClass.Equals("CommercialPaper", StringComparison.OrdinalIgnoreCase) ||
        assetClass.Equals("TreasuryBill", StringComparison.OrdinalIgnoreCase) ||
        assetClass.Equals("MortgageBacked", StringComparison.OrdinalIgnoreCase) ||
        assetClass.Equals("MortgageBackedSecurity", StringComparison.OrdinalIgnoreCase) ||
        assetClass.Equals("Mbs", StringComparison.OrdinalIgnoreCase) ||
        assetClass.Equals("AssetBacked", StringComparison.OrdinalIgnoreCase) ||
        assetClass.Equals("AssetBackedSecurity", StringComparison.OrdinalIgnoreCase) ||
        assetClass.Equals("Abs", StringComparison.OrdinalIgnoreCase) ||
        assetClass.Equals("Loan", StringComparison.OrdinalIgnoreCase) ||
        assetClass.Equals("AmortizingLoan", StringComparison.OrdinalIgnoreCase);

    private static bool RequiresFactorSchedule(SecurityMasterAccountingSecurity security)
    {
        var terms = security.FixedIncomeTerms;
        if (terms is null)
        {
            return false;
        }

        if (terms.RequiresFactorSchedule)
        {
            return true;
        }

        if (terms.CurrentFactor is decimal currentFactor && currentFactor < 1m)
        {
            return true;
        }

        return terms.OriginalFace is decimal originalFace &&
            terms.CurrentFace is decimal currentFace &&
            originalFace > 0m &&
            currentFace < originalFace;
    }

    private static FactorScheduleCoverageIssue? GetFactorScheduleCoverageStatus(
        SecurityMasterAccountingEventRequest request,
        Guid securityId)
    {
        var entries = (request.FactorSchedule ?? Array.Empty<SecurityFactorScheduleEntry>())
            .Where(entry => entry.SecurityId == securityId)
            .ToArray();
        if (entries.Length == 0)
        {
            return FactorScheduleCoverageIssue.Missing;
        }

        var hasPeriodFactor = entries.Any(entry =>
            entry.AsOfDate >= request.PeriodStart &&
            entry.AsOfDate <= request.PeriodEnd);
        if (hasPeriodFactor)
        {
            return null;
        }

        var latestFactor = entries.Max(static entry => entry.AsOfDate);
        return latestFactor < request.PeriodStart
            ? FactorScheduleCoverageIssue.Stale
            : FactorScheduleCoverageIssue.Missing;
    }

    private static IReadOnlyList<SecurityMasterAccountingIssueDto> ValidateFixedIncomeCoverage(
        SecurityMasterAccountingSecurity security,
        string accountId)
    {
        var issues = new List<SecurityMasterAccountingIssueDto>();
        var terms = security.FixedIncomeTerms;
        if (terms is null)
        {
            issues.Add(CreateIssue(
                "SM_ACCOUNTING_TERMS_INCOMPLETE",
                "security-master",
                security.Symbol,
                accountId,
                $"Security '{security.Symbol}' is missing fixed-income accounting terms.",
                ReconciliationBreakSeverity.High));
            return issues;
        }

        if (string.IsNullOrWhiteSpace(terms.CouponType))
        {
            issues.Add(CreateIssue("SM_COUPON_TERMS_MISSING", "security-master", security.Symbol, accountId, $"Security '{security.Symbol}' is missing coupon type.", ReconciliationBreakSeverity.High));
        }

        if (terms.CouponType?.Equals("Floating", StringComparison.OrdinalIgnoreCase) == true)
        {
            issues.Add(CreateIssue("SM_RATE_RESET_TERMS_MISSING", "security-master", security.Symbol, accountId, $"Security '{security.Symbol}' is floating-rate and requires reset terms before expected accruals can be generated.", ReconciliationBreakSeverity.High));
        }

        if (terms.CouponRate is null && terms.CouponType?.Equals("ZeroCoupon", StringComparison.OrdinalIgnoreCase) != true)
        {
            issues.Add(CreateIssue("SM_COUPON_TERMS_MISSING", "security-master", security.Symbol, accountId, $"Security '{security.Symbol}' is missing coupon rate.", ReconciliationBreakSeverity.High));
        }

        if (string.IsNullOrWhiteSpace(terms.DayCountConvention))
        {
            issues.Add(CreateIssue("SM_DAY_COUNT_MISSING", "security-master", security.Symbol, accountId, $"Security '{security.Symbol}' is missing day-count convention.", ReconciliationBreakSeverity.High));
        }

        if (terms.PaymentFrequencyPerYear is null or <= 0)
        {
            issues.Add(CreateIssue("SM_PAYMENT_FREQUENCY_MISSING", "security-master", security.Symbol, accountId, $"Security '{security.Symbol}' is missing payment frequency.", ReconciliationBreakSeverity.High));
        }

        if (string.IsNullOrWhiteSpace(security.AccountingRule?.AccountingClassification))
        {
            issues.Add(CreateIssue("SM_ACCOUNTING_CLASSIFICATION_MISSING", "security-master", security.Symbol, accountId, $"Security '{security.Symbol}' is missing accounting classification.", ReconciliationBreakSeverity.High));
        }

        return issues;
    }

    private static void GenerateFixedCouponEvents(
        SecurityMasterAccountingEventRequest request,
        SecurityMasterAccountingSecurity security,
        SecurityMasterAccountingPosition position,
        List<ExpectedAccountingEventDto> events,
        List<AccrualCalculationResultDto> accruals,
        List<ExpectedJournalPreviewDto> previews)
    {
        var terms = security.FixedIncomeTerms!;
        if (terms.CouponType?.Equals("Fixed", StringComparison.OrdinalIgnoreCase) != true)
        {
            return;
        }

        var start = Max(request.PeriodStart, terms.AccrualStartDate ?? terms.DatedDate ?? terms.IssueDate ?? request.PeriodStart);
        var end = terms.MaturityDate is DateOnly maturity ? Min(request.PeriodEnd, maturity) : request.PeriodEnd;
        if (end <= start)
        {
            return;
        }

        var convention = DayCountConventions.Parse(terms.DayCountConvention);
        var accrualDays = DayCountConventions.Days(convention, start, end);
        var fraction = DayCountConventions.Fraction(convention, start, end);
        var accruedAmount = RoundMoney(position.ParAmount * NormalizeRate(terms.CouponRate!.Value) * fraction);
        if (accruedAmount <= 0m)
        {
            return;
        }

        var snapshot = CreateSnapshot(request, security, position, priorFactor: null, currentFactor: terms.CurrentFactor);
        var eventId = BuildDeterministicId("accrual", snapshot.SourceHash);
        var accrual = new AccrualCalculationResultDto(
            eventId,
            security.SecurityId,
            security.Symbol,
            position.AccountId,
            start,
            end,
            accrualDays,
            fraction,
            accruedAmount,
            security.Currency,
            snapshot);
        accruals.Add(accrual);

        var accrualEvent = CreateExpectedEvent(
            eventId,
            ExpectedAccountingEventKindDto.AccrueInterestIncome,
            security,
            position,
            end,
            accruedAmount,
            principalAmount: 0m,
            incomeAmount: accruedAmount,
            snapshot);
        events.Add(accrualEvent);
        previews.Add(CreateInterestAccrualPreview(security, position, accrualEvent));

        if (terms.NextCouponDate is DateOnly couponDate &&
            couponDate >= request.PeriodStart &&
            couponDate <= request.PeriodEnd)
        {
            var couponAmount = RoundMoney(position.ParAmount * NormalizeRate(terms.CouponRate.Value) / terms.PaymentFrequencyPerYear!.Value);
            if (couponAmount > 0m)
            {
                var couponSnapshot = CreateSnapshot(request, security, position, priorFactor: null, currentFactor: terms.CurrentFactor);
                var couponEventId = BuildDeterministicId("coupon", couponSnapshot.SourceHash, couponDate.ToString("yyyy-MM-dd"));
                var couponEvent = CreateExpectedEvent(
                    couponEventId,
                    ExpectedAccountingEventKindDto.ReceiveCashInterest,
                    security,
                    position,
                    couponDate,
                    couponAmount,
                    principalAmount: 0m,
                    incomeAmount: couponAmount,
                    couponSnapshot);
                events.Add(couponEvent);
                previews.Add(CreateCouponCashPreview(security, position, couponEvent));
            }
        }
    }

    private void GenerateFactorPaydownEvents(
        SecurityMasterAccountingEventRequest request,
        SecurityMasterAccountingSecurity security,
        SecurityMasterAccountingPosition position,
        List<ExpectedAccountingEventDto> events,
        List<ExpectedJournalPreviewDto> previews,
        List<SecurityMasterAccountingIssueDto> issues)
    {
        var factors = (request.FactorSchedule ?? Array.Empty<SecurityFactorScheduleEntry>())
            .Where(entry => entry.SecurityId == security.SecurityId && entry.AsOfDate >= request.PeriodStart && entry.AsOfDate <= request.PeriodEnd)
            .OrderBy(static entry => entry.AsOfDate)
            .ToArray();
        var positionId = position.PositionId.GetValueOrDefault();
        if (factors.Length > 0 && positionId == Guid.Empty)
        {
            issues.Add(CreateIssue(
                "FACTOR_PAYDOWN_POSITION_REQUIRED",
                "security-master",
                security.Symbol,
                position.AccountId,
                "Factor-paydown events require a durable Asset Operations book-position identity.",
                ReconciliationBreakSeverity.High));
            return;
        }

        var projectedPositionVersion = position.PositionVersion;
        foreach (var factor in factors)
        {
            var projection = _factorPaydownProjector.Project(new FactorPaydownProjectionRequest(
                security.SecurityId,
                positionId,
                projectedPositionVersion,
                projectedPositionVersion,
                position.ParAmount,
                factor.PriorFactor,
                factor.CurrentFactor,
                security.Currency,
                factor.AsOfDate,
                new DateTimeOffset(factor.AsOfDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
                "SecurityMaster",
                $"{security.SecurityId:N}:{factor.AsOfDate:yyyy-MM-dd}",
                factor.SourceContentHash ?? string.Empty,
                string.IsNullOrWhiteSpace(factor.EvidenceLink) ? [] : [factor.EvidenceLink]));
            if (!projection.ProducesPostingCandidate)
            {
                foreach (var projectionIssue in projection.Issues)
                {
                    issues.Add(CreateIssue(
                        projectionIssue.Code.Replace('-', '_').Replace('.', '_').ToUpperInvariant(),
                        "security-master",
                        security.Symbol,
                        position.AccountId,
                        projectionIssue.Message,
                        ReconciliationBreakSeverity.High));
                }

                continue;
            }

            projectedPositionVersion = projection.EconomicState!.Version;

            var snapshot = CreateSnapshot(request, security, position, factor.PriorFactor, factor.CurrentFactor);
            var economicEvent = projection.EconomicEvent!;
            var expectedPrincipal = projection.PrincipalPaydown!.Value;
            var expectedEvent = CreateExpectedEvent(
                economicEvent.EventId.ToString("N"),
                ExpectedAccountingEventKindDto.RecognizePrincipalPaydown,
                security,
                position,
                factor.AsOfDate,
                expectedPrincipal,
                principalAmount: expectedPrincipal,
                incomeAmount: 0m,
                snapshot,
                provenanceSuffix: $"factor-source:{factor.Source};factor-evidence:{factor.EvidenceLink}") with
            {
                IdempotencyKey = $"{security.SecurityId:N}:{positionId:N}:{economicEvent.EventId:N}",
                EconomicEvent = economicEvent,
                ProjectionLineage = projection.Lineage,
                EvidenceLinks = economicEvent.EvidenceLinks
            };
            events.Add(expectedEvent);
            // The journal preview needs the security's accounting rule for its ledger accounts;
            // without one the EXPECTED paydown event still surfaces (the retained factor evidence
            // fully supports the principal event itself) alongside the already-recorded
            // SECURITY_ACCOUNTING_RULE_MISSING issue, but no preview is fabricated from a missing
            // classification — previously this dereferenced the absent rule and crashed the run.
            if (security.AccountingRule is not null)
            {
                previews.Add(CreatePrincipalPaydownPreview(security, position, expectedEvent));
            }
        }
    }

    private static void ReconcileActualActivity(
        SecurityMasterAccountingEventRequest request,
        IReadOnlyList<ExpectedAccountingEventDto> expectedEvents,
        List<SecurityMasterAccountingIssueDto> issues)
    {
        var actualRows = request.ActualActivity ?? Array.Empty<SecurityActualCashActivity>();
        var expectedCashEvents = expectedEvents
            .Where(static item => item.EventKind is ExpectedAccountingEventKindDto.ReceiveCashInterest or ExpectedAccountingEventKindDto.RecognizePrincipalPaydown)
            .ToArray();

        foreach (var expected in expectedCashEvents)
        {
            var candidates = actualRows
                .Where(actual => IsSameCashEvent(expected, actual))
                .ToArray();
            if (candidates.Length == 0)
            {
                issues.Add(CreateIssue(
                    expected.EventKind == ExpectedAccountingEventKindDto.RecognizePrincipalPaydown ? "FACTOR_PAYDOWN_EXTERNAL_CASH_MISSING" : "ACCRUAL_ACTUAL_EVENT_MISSING",
                    "actual-activity",
                    expected.Symbol,
                    expected.AccountId,
                    $"Expected {expected.EventKind} for '{expected.Symbol}' has no matching provider, custodian, bank, statement, or ledger activity.",
                    ReconciliationBreakSeverity.High,
                    expected.ExpectedAmount,
                    actualAmount: null));
                continue;
            }

            var sameKindCandidates = candidates
                .Where(actual => MatchesExpectedKind(expected, actual))
                .ToArray();
            var oppositeKindCandidates = candidates
                .Where(actual => !MatchesExpectedKind(expected, actual))
                .ToArray();
            var expectedAmount = expected.PrincipalAmount > 0m ? expected.PrincipalAmount : expected.IncomeAmount;

            if (sameKindCandidates.Length == 0 && oppositeKindCandidates.Length > 0)
            {
                issues.Add(CreateIssue(
                    expected.PrincipalAmount > 0m ? "FACTOR_PAYDOWN_CLASSIFICATION_MISMATCH" : "ACCRUAL_CLASSIFICATION_MISMATCH",
                    "actual-activity",
                    expected.Symbol,
                    expected.AccountId,
                    expected.PrincipalAmount > 0m
                        ? $"Factor paydown for '{expected.Symbol}' is expected as principal at par, but actual activity classifies cash as income."
                        : $"Coupon cash for '{expected.Symbol}' is expected as income, but actual activity classifies cash as principal.",
                    ReconciliationBreakSeverity.High,
                    expectedAmount,
                    candidates.Sum(static actual => actual.CashAmount)));
                continue;
            }

            var actualAmount = sameKindCandidates.Sum(actual => ResolveComparableAmount(actual, expected));
            if (Math.Abs(actualAmount - expectedAmount) > request.AmountTolerance)
            {
                issues.Add(CreateIssue(
                    expected.PrincipalAmount > 0m ? "FACTOR_PAYDOWN_AMOUNT_MISMATCH" : "ACCRUAL_AMOUNT_MISMATCH",
                    "actual-activity",
                    expected.Symbol,
                    expected.AccountId,
                    $"Actual activity for '{expected.Symbol}' differs from the Security Master expected amount.",
                    ReconciliationBreakSeverity.High,
                    expectedAmount,
                    actualAmount));
            }
        }

        foreach (var actual in actualRows)
        {
            var hasExpected = expectedCashEvents.Any(expected => IsSameCashEvent(expected, actual));
            if (hasExpected)
            {
                continue;
            }

            var code = actual.PrincipalAmount > 0m
                ? "FACTOR_REDUCTION_UNRECONCILED"
                : "ACCRUAL_EXPECTED_EVENT_MISSING";
            issues.Add(CreateIssue(
                code,
                actual.SourceName,
                actual.Symbol ?? actual.SecurityId?.ToString("N") ?? "unknown",
                actual.AccountId,
                $"Actual activity '{actual.ExternalTransactionId ?? actual.SourceName}' has no Security Master expected accounting event.",
                ReconciliationBreakSeverity.Medium,
                expectedAmount: null,
                actual.CashAmount));
        }
    }

    private static bool IsSameSecurity(ExpectedAccountingEventDto expected, SecurityActualCashActivity actual)
    {
        if (actual.SecurityId is Guid securityId && securityId == expected.SecurityId)
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(actual.Symbol) &&
            string.Equals(actual.Symbol, expected.Symbol, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSameCashEvent(ExpectedAccountingEventDto expected, SecurityActualCashActivity actual)
        => IsSameSecurity(expected, actual)
           && string.Equals(expected.AccountId, actual.AccountId, StringComparison.OrdinalIgnoreCase)
           && actual.PayDate == expected.EventDate;

    private static bool MatchesExpectedKind(ExpectedAccountingEventDto expected, SecurityActualCashActivity actual)
        => expected.PrincipalAmount > 0m
            ? IsPrincipalActivity(actual)
            : IsIncomeActivity(actual);

    private static bool IsPrincipalActivity(SecurityActualCashActivity actual)
        => actual.PrincipalAmount > 0m ||
           string.Equals(actual.Classification, "Principal", StringComparison.OrdinalIgnoreCase);

    private static bool IsIncomeActivity(SecurityActualCashActivity actual)
        => actual.IncomeAmount > 0m ||
           string.Equals(actual.Classification, "Income", StringComparison.OrdinalIgnoreCase);

    private static decimal ResolveComparableAmount(SecurityActualCashActivity actual, ExpectedAccountingEventDto expected)
        => expected.PrincipalAmount > 0m
            ? actual.PrincipalAmount != 0m ? actual.PrincipalAmount : actual.CashAmount
            : actual.IncomeAmount != 0m ? actual.IncomeAmount : actual.CashAmount;

    private static ExpectedAccountingEventDto CreateExpectedEvent(
        string eventId,
        ExpectedAccountingEventKindDto kind,
        SecurityMasterAccountingSecurity security,
        SecurityMasterAccountingPosition position,
        DateOnly eventDate,
        decimal expectedAmount,
        decimal principalAmount,
        decimal incomeAmount,
        AccrualInputSnapshotDto snapshot,
        string? provenanceSuffix = null)
    {
        var idempotencyKey = $"{security.SecurityId:N}:{position.AccountId}:{eventDate:yyyyMMdd}:{kind}:{snapshot.SourceHash}";
        var provenance = string.IsNullOrWhiteSpace(provenanceSuffix)
            ? $"security-master:{security.SecurityId:N};snapshot:{snapshot.SourceHash}"
            : $"security-master:{security.SecurityId:N};snapshot:{snapshot.SourceHash};{provenanceSuffix}";

        return new ExpectedAccountingEventDto(
            eventId,
            kind,
            security.SecurityId,
            security.Symbol,
            position.AccountId,
            eventDate,
            expectedAmount,
            principalAmount,
            incomeAmount,
            security.Currency,
            idempotencyKey,
            provenance,
            snapshot);
    }

    private static ExpectedJournalPreviewDto CreateInterestAccrualPreview(
        SecurityMasterAccountingSecurity security,
        SecurityMasterAccountingPosition position,
        ExpectedAccountingEventDto expectedEvent)
    {
        var rule = security.AccountingRule!;
        return CreatePreview(
            expectedEvent,
            $"Accrue interest income for {security.Symbol}",
            [
                new ExpectedJournalPreviewLineDto(rule.AccruedInterestReceivableAccount, "Asset", security.Symbol, expectedEvent.IncomeAmount, 0m),
                new ExpectedJournalPreviewLineDto(rule.CouponIncomeAccount, "Revenue", security.Symbol, 0m, expectedEvent.IncomeAmount)
            ]);
    }

    private static ExpectedJournalPreviewDto CreateCouponCashPreview(
        SecurityMasterAccountingSecurity security,
        SecurityMasterAccountingPosition position,
        ExpectedAccountingEventDto expectedEvent)
    {
        var rule = security.AccountingRule!;
        return CreatePreview(
            expectedEvent,
            $"Receive coupon cash for {security.Symbol}",
            [
                new ExpectedJournalPreviewLineDto(rule.CashAccount, "Asset", null, expectedEvent.IncomeAmount, 0m),
                new ExpectedJournalPreviewLineDto(rule.AccruedInterestReceivableAccount, "Asset", security.Symbol, 0m, expectedEvent.IncomeAmount)
            ]);
    }

    private static ExpectedJournalPreviewDto CreatePrincipalPaydownPreview(
        SecurityMasterAccountingSecurity security,
        SecurityMasterAccountingPosition position,
        ExpectedAccountingEventDto expectedEvent)
    {
        var rule = security.AccountingRule!;
        return CreatePreview(
            expectedEvent,
            $"Recognize factor principal paydown for {security.Symbol}",
            [
                new ExpectedJournalPreviewLineDto(rule.CashAccount, "Asset", null, expectedEvent.PrincipalAmount, 0m),
                new ExpectedJournalPreviewLineDto(rule.PrincipalAccount, "Asset", security.Symbol, 0m, expectedEvent.PrincipalAmount)
            ]);
    }

    private static ExpectedJournalPreviewDto CreatePreview(
        ExpectedAccountingEventDto expectedEvent,
        string description,
        IReadOnlyList<ExpectedJournalPreviewLineDto> lines)
    {
        var totalDebits = lines.Sum(static line => line.Debit);
        var totalCredits = lines.Sum(static line => line.Credit);
        return new ExpectedJournalPreviewDto(
            BuildDeterministicId("journal-preview", expectedEvent.IdempotencyKey),
            expectedEvent.EventId,
            description,
            expectedEvent.EventDate,
            Math.Abs(totalDebits - totalCredits) <= LedgerToleranceConstants.Balance,
            RequiresOperatorApproval: true,
            expectedEvent.IdempotencyKey,
            lines);
    }

    private static AccrualInputSnapshotDto CreateSnapshot(
        SecurityMasterAccountingEventRequest request,
        SecurityMasterAccountingSecurity security,
        SecurityMasterAccountingPosition position,
        decimal? priorFactor,
        decimal? currentFactor)
    {
        var terms = security.FixedIncomeTerms;
        var source = string.Join(
            "|",
            request.RunId,
            request.PeriodStart.ToString("yyyy-MM-dd"),
            request.PeriodEnd.ToString("yyyy-MM-dd"),
            security.SecurityId.ToString("N"),
            security.Symbol,
            position.AccountId,
            position.ParAmount.ToString("0.##########"),
            terms?.CouponRate?.ToString("0.##########") ?? "",
            terms?.CouponType ?? "",
            terms?.DayCountConvention ?? "",
            terms?.PaymentFrequencyPerYear?.ToString() ?? "",
            priorFactor?.ToString("0.##########") ?? "",
            currentFactor?.ToString("0.##########") ?? "");

        return new AccrualInputSnapshotDto(
            security.SecurityId,
            security.Symbol,
            position.AccountId,
            request.PeriodStart,
            request.PeriodEnd,
            position.ParAmount,
            terms?.CouponRate,
            terms?.CouponType,
            terms?.DayCountConvention,
            terms?.PaymentFrequencyPerYear,
            priorFactor,
            currentFactor,
            Hash(source));
    }

    private static SecurityMasterAccountingIssueDto CreateIssue(
        string code,
        string source,
        string symbol,
        string? accountId,
        string reason,
        ReconciliationBreakSeverity severity,
        decimal? expectedAmount = null,
        decimal? actualAmount = null) =>
        new(code, source, symbol, accountId, reason, severity, "/workstation/data/security-master", expectedAmount, actualAmount);

    private static decimal NormalizeRate(decimal rate) => rate > 1m ? rate / 100m : rate;

    private static decimal RoundMoney(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);

    private static DateOnly Min(DateOnly left, DateOnly right) => left <= right ? left : right;

    private static DateOnly Max(DateOnly left, DateOnly right) => left >= right ? left : right;

    private static string BuildDeterministicId(params string[] parts) => Hash(string.Join("|", parts))[..32].ToLowerInvariant();

    private static string Hash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
