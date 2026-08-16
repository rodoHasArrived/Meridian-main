using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Tenancy;
using Meridian.Contracts.Workstation;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using static Meridian.Contracts.Text.TextPrimitives;

namespace Meridian.FinancialOperations.PrivateCapital;

/// <summary>
/// Exact server-owned scope used to resolve the capital-account tie-out that supports one
/// fee-accrual execution. The evaluation timestamp is the actual execution/review time, not
/// the schedule's original due timestamp.
/// </summary>
public sealed record AutomatedJournalCapitalAccountReconciliationScope(
    string TenantId,
    string CompanyId,
    string FundProfileId,
    Guid LedgerBookId,
    string EntityId,
    string PeriodId,
    string Currency,
    DateTimeOffset EvaluatedAtUtc);

/// <summary>
/// Server-side source for reviewed capital-account balances, confidence, source version, and
/// retained evidence. Implementations must resolve from authoritative state; request payloads
/// and persisted schedule assertions are never an implementation fallback.
/// </summary>
public interface IAutomatedJournalCapitalAccountReconciliationResolver
{
    Task<AutomatedJournalCapitalAccountReconciliationDto?> ResolveAsync(
        AutomatedJournalCapitalAccountReconciliationScope scope,
        CancellationToken ct = default);
}

/// <summary>
/// Reconstructs fee-basis NAV, capital-account equity, and high-water history from immutable,
/// posted ledger journals. Tenant/company ownership, ledger-book identity, accounting period,
/// entity dimensions, book currency, retained evidence, durable posting-command or adjustment
/// approval provenance, and the evaluation-time boundary are all resolved from server-owned state
/// before a reconciliation can be returned.
/// </summary>
public sealed class LedgerCapitalAccountReconciliationResolver
    : IAutomatedJournalCapitalAccountReconciliationResolver
{
    private const decimal TieOutTolerance = 0.01m;
    private const decimal GovernedDerivedConfidence = 0.95m;
    private readonly ILedgerJournalStore? _journalStore;
    private readonly IFundProfileTenancyRegistry? _tenancyRegistry;

    public LedgerCapitalAccountReconciliationResolver(
        ILedgerJournalStore? journalStore,
        IFundProfileTenancyRegistry? tenancyRegistry)
    {
        _journalStore = journalStore;
        _tenancyRegistry = tenancyRegistry;
    }

    public async Task<AutomatedJournalCapitalAccountReconciliationDto?> ResolveAsync(
        AutomatedJournalCapitalAccountReconciliationScope scope,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ct.ThrowIfCancellationRequested();

        if (_journalStore is null ||
            _tenancyRegistry is null ||
            !TryNormalizeScope(scope, out var normalized))
        {
            return null;
        }

        var owner = await _tenancyRegistry
            .ResolveAsync(normalized.FundProfileId, ct)
            .ConfigureAwait(false);
        if (!MatchesOwner(owner, normalized))
        {
            return null;
        }

        var book = await _journalStore
            .GetLedgerBookAsync(normalized.LedgerBookId, ct)
            .ConfigureAwait(false);
        if (book is null ||
            !EqualsText(book.FundProfileId, normalized.FundProfileId) ||
            !EqualsText(book.BaseCurrency, normalized.Currency))
        {
            return null;
        }

        var periods = await _journalStore
            .ListPeriodsAsync(
                normalized.LedgerBookId,
                status: null,
                normalized.FundProfileId,
                fundStructureNodeId: null,
                ct)
            .ConfigureAwait(false);
        var bookPeriods = periods
            .Where(period => period.LedgerBookId == normalized.LedgerBookId)
            .OrderBy(static period => period.StartDate)
            .ThenBy(static period => period.PeriodNo)
            .ThenBy(static period => period.PeriodId)
            .ToArray();
        var matchingPeriods = bookPeriods
            .Where(period => MatchesPeriod(period, normalized.PeriodId))
            .ToArray();
        if (matchingPeriods.Length != 1)
        {
            return null;
        }

        var currentPeriod = matchingPeriods[0];
        var evaluatedAtUtc = normalized.EvaluatedAtUtc.ToUniversalTime();
        var periodStartUtc = StartOfDayUtc(currentPeriod.StartDate);
        var periodEndUtc = EndOfDayUtc(currentPeriod.EndDate);
        if (evaluatedAtUtc < periodStartUtc)
        {
            return null;
        }

        var currentCutoffUtc = evaluatedAtUtc < periodEndUtc ? evaluatedAtUtc : periodEndUtc;
        var dimensions = new LedgerLineDimensionSet(
            FundId: normalized.FundProfileId,
            EntityId: normalized.EntityId,
            BookId: normalized.LedgerBookId.ToString("D"));
        var queriedRecords = await _journalStore
            .QueryAsync(
                new LedgerJournalEntryQuery(
                    LedgerBookId: normalized.LedgerBookId,
                    LineDimensions: dimensions,
                    OccurredTo: currentCutoffUtc),
                ct)
            .ConfigureAwait(false);

        var sourcePeriods = bookPeriods
            .Where(period => period.StartDate <= currentPeriod.EndDate)
            .ToArray();
        var eligiblePeriodIds = sourcePeriods
            .Select(static period => period.PeriodId)
            .ToHashSet();
        var records = queriedRecords
            .Where(record => eligiblePeriodIds.Contains(record.PeriodId))
            .Where(record => record.CreatedAt != default && record.CreatedAt.ToUniversalTime() <= evaluatedAtUtc)
            .Where(record => record.Entry.Timestamp.ToUniversalTime() <= currentCutoffUtc)
            .Where(record => HasMatchingLine(record.Entry, dimensions))
            .OrderBy(static record => record.Entry.Timestamp)
            .ThenBy(static record => record.GlobalSequence)
            .ThenBy(static record => record.Entry.JournalEntryId)
            .ToArray();
        if (records.Length == 0 ||
            records.GroupBy(static record => record.Entry.JournalEntryId).Any(static group => group.Count() != 1) ||
            records.Any(record => IsCurrentPeriodFeePosting(record, currentPeriod)))
        {
            return null;
        }

        var evidenceByRecord = new Dictionary<Guid, IReadOnlyList<JournalEvidenceReference>>();
        var governanceByRecord = new Dictionary<Guid, GovernedReviewProvenance>();
        foreach (var record in records)
        {
            var evidence = record.Entry.Metadata.EvidenceReferences
                .Where(item => item.RetainedAtUtc.ToUniversalTime() <= evaluatedAtUtc)
                .Where(static item =>
                    !string.IsNullOrWhiteSpace(item.EvidenceId) &&
                    !string.IsNullOrWhiteSpace(item.Uri) &&
                    !string.IsNullOrWhiteSpace(item.SourceSystem) &&
                    !string.IsNullOrWhiteSpace(item.RetainedBy))
                .OrderBy(static item => item.RetainedAtUtc)
                .ThenBy(static item => item.EvidenceId, StringComparer.Ordinal)
                .ToArray();
            if (evidence.Length == 0)
            {
                return null;
            }

            if (!TryResolveGovernedReview(record, evidence, evaluatedAtUtc, out var governance))
            {
                return null;
            }

            evidenceByRecord[record.Entry.JournalEntryId] = evidence;
            governanceByRecord[record.Entry.JournalEntryId] = governance;
        }

        var capitalAccountIds = records
            .SelectMany(static record => record.Entry.Lines)
            .Where(line => line.Account.AccountType == LedgerAccountType.Equity &&
                           MatchesDimensions(line.Dimensions, dimensions))
            .Select(static line => NormalizeOptional(line.Dimensions?.CapitalAccountId))
            .Where(static value => value is not null)
            .Select(static value => value!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (capitalAccountIds.Length == 0)
        {
            return null;
        }

        var ledger = Hydrate(records);
        var openingCutoffUtc = periodStartUtc.AddTicks(-1);
        var beginningNav = CalculateNav(ledger, openingCutoffUtc, dimensions);
        var endingNavBeforeFees = CalculateNav(ledger, currentCutoffUtc, dimensions);
        var capitalOpening = CalculateCapitalAccountBalance(
            ledger,
            openingCutoffUtc,
            dimensions,
            capitalAccountIds);
        var capitalEnding = CalculateCapitalAccountBalance(
            ledger,
            currentCutoffUtc,
            dimensions,
            capitalAccountIds);

        var priorPeriods = bookPeriods
            .Where(period => period.EndDate < currentPeriod.StartDate)
            .OrderBy(static period => period.EndDate)
            .ThenBy(static period => period.PeriodId)
            .ToArray();
        if (priorPeriods.Any(static period => string.Equals(period.Status, "Open", StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        var priorPeriodEndNavs = priorPeriods
            .Select(period => CalculateNav(ledger, EndOfDayUtc(period.EndDate), dimensions))
            .ToArray();
        var highWaterMark = priorPeriodEndNavs.Length == 0
            ? beginningNav
            : Math.Max(beginningNav, priorPeriodEndNavs.Max());
        var priorPeriodEndCapitalBalances = priorPeriods
            .Select(period => CalculateCapitalAccountBalance(
                ledger,
                EndOfDayUtc(period.EndDate),
                dimensions,
                capitalAccountIds))
            .ToArray();
        var capitalHighWaterMark = priorPeriodEndCapitalBalances.Length == 0
            ? capitalOpening
            : Math.Max(capitalOpening, priorPeriodEndCapitalBalances.Max());
        if (beginningNav < 0m ||
            endingNavBeforeFees < 0m ||
            highWaterMark < 0m ||
            capitalOpening < 0m ||
            capitalEnding < 0m ||
            capitalHighWaterMark < 0m)
        {
            return null;
        }

        var maximumVariance = new[]
        {
            decimal.Abs(beginningNav - capitalOpening),
            decimal.Abs(endingNavBeforeFees - capitalEnding),
            decimal.Abs(highWaterMark - capitalHighWaterMark)
        }.Max();
        var isReconciled = maximumVariance <= TieOutTolerance;
        var evidenceReferences = evidenceByRecord.Values
            .SelectMany(static items => items)
            .DistinctBy(static item => $"{item.EvidenceId}\u001f{item.Uri}", StringComparer.OrdinalIgnoreCase)
            .OrderBy(static item => item.RetainedAtUtc)
            .ThenBy(static item => item.EvidenceId, StringComparer.Ordinal)
            .ToArray();
        var reviewers = governanceByRecord.Values
            .Select(static item => item.ReviewedBy)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var reviewedAtUtc = governanceByRecord.Values
            .Max(static item => item.ReviewedAtUtc)
            .ToUniversalTime();
        var sourceVersion = BuildSourceVersion(
            normalized,
            book,
            currentPeriod,
            sourcePeriods,
            records,
            evidenceByRecord,
            governanceByRecord);
        var evidenceLinks = evidenceReferences
            .Select(item => new OperationsEvidenceLinkDto(
                item.EvidenceId,
                item.Description ?? $"{item.Kind} retained by {item.RetainedBy}",
                item.Uri,
                item.SourceSystem,
                item.RetainedAtUtc))
            .ToArray();

        return new AutomatedJournalCapitalAccountReconciliationDto(
            ReconciliationId: $"ledger-capital-reconciliation:{currentPeriod.PeriodId:D}:{sourceVersion[..16]}",
            PeriodId: normalized.PeriodId,
            Currency: normalized.Currency,
            ReconciledBeginningNav: beginningNav,
            ReconciledEndingNavBeforeFees: endingNavBeforeFees,
            ReconciledHighWaterMark: highWaterMark,
            CapitalAccountOpeningBalance: capitalOpening,
            CapitalAccountEndingBalanceBeforeFees: capitalEnding,
            CapitalAccountHighWaterMark: capitalHighWaterMark,
            MaximumVarianceTolerance: TieOutTolerance,
            // The tie-out is deterministically derived from governed ledger facts, but it is not
            // itself a separately certified reconciliation. Do not present arithmetic equality as
            // 100% human-reviewed confidence.
            ConfidenceScore: isReconciled ? GovernedDerivedConfidence : 0.50m,
            IsReconciled: isReconciled,
            SourceVersion: sourceVersion,
            ReviewedBy: string.Join(", ", reviewers),
            ReviewedAtUtc: reviewedAtUtc,
            EvidenceLinks: evidenceLinks);
    }

    private static bool TryResolveGovernedReview(
        LedgerJournalEntryRecord record,
        IReadOnlyList<JournalEvidenceReference> evidence,
        DateTimeOffset evaluatedAtUtc,
        out GovernedReviewProvenance governance)
    {
        if (TryResolveAdjustmentApproval(record, evaluatedAtUtc, out governance))
        {
            return true;
        }

        governance = default!;
        if (record.CommandId is not { } commandId || commandId == Guid.Empty)
        {
            return false;
        }

        var tags = record.Entry.Metadata.Tags;
        var postingCommandId = GetTag(tags, "postingCommandId");
        var approvalState = GetTag(tags, "approvalState");
        var approvalId = GetTag(tags, "approvalId");
        var fingerprint = GetTag(tags, "postingCommandFingerprint");
        if (!Guid.TryParse(postingCommandId, out var retainedCommandId) ||
            retainedCommandId != commandId ||
            !string.Equals(approvalState, AccountingPostingApprovalStateDto.Approved.ToString(), StringComparison.OrdinalIgnoreCase) ||
            approvalId is null ||
            !IsSha256Fingerprint(fingerprint))
        {
            return false;
        }

        var journalEntryId = record.Entry.JournalEntryId.ToString("D");
        var approvalEvidence = evidence
            .Where(IsApprovalOrCertificationEvidence)
            .Where(item =>
                EqualsText(item.EvidenceId, approvalId) ||
                EqualsText(item.SubjectId, journalEntryId))
            .OrderBy(static item => item.RetainedAtUtc)
            .ThenBy(static item => item.EvidenceId, StringComparer.Ordinal)
            .LastOrDefault();
        if (approvalEvidence is null ||
            string.IsNullOrWhiteSpace(approvalEvidence.RetainedBy) ||
            approvalEvidence.RetainedAtUtc == default ||
            approvalEvidence.RetainedAtUtc.ToUniversalTime() > evaluatedAtUtc)
        {
            return false;
        }

        governance = new GovernedReviewProvenance(
            "PostingCommandApproval",
            approvalId,
            approvalEvidence.RetainedBy.Trim(),
            approvalEvidence.RetainedAtUtc.ToUniversalTime(),
            fingerprint!);
        return true;
    }

    private static bool TryResolveAdjustmentApproval(
        LedgerJournalEntryRecord record,
        DateTimeOffset evaluatedAtUtc,
        out GovernedReviewProvenance governance)
    {
        governance = default!;
        var approval = record.AdjustmentApproval;
        if (record.PostingKind != LedgerPostingKindDto.Adjustment ||
            approval is null ||
            approval.Status != LedgerAdjustmentApprovalStatusDto.Approved ||
            string.IsNullOrWhiteSpace(approval.ApprovalId) ||
            string.IsNullOrWhiteSpace(approval.ApprovedBy) ||
            string.IsNullOrWhiteSpace(approval.ReasonCode) ||
            approval.ApprovedAt == default ||
            approval.ApprovedAt.ToUniversalTime() > evaluatedAtUtc ||
            approval.ApprovedAt.ToUniversalTime() > record.CreatedAt.ToUniversalTime() ||
            (string.IsNullOrWhiteSpace(approval.GovernanceCaseId) &&
             string.IsNullOrWhiteSpace(approval.EvidenceLink)))
        {
            return false;
        }

        var governanceVersion = string.Join(
            '\u001e',
            approval.ApprovalId.Trim(),
            approval.ApprovedBy.Trim(),
            approval.ApprovedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            approval.ReasonCode.Trim(),
            NormalizeOptional(approval.GovernanceCaseId) ?? "<null>",
            NormalizeOptional(approval.EvidenceLink) ?? "<null>");
        governance = new GovernedReviewProvenance(
            "AdjustmentApproval",
            approval.ApprovalId.Trim(),
            approval.ApprovedBy.Trim(),
            approval.ApprovedAt.ToUniversalTime(),
            governanceVersion);
        return true;
    }

    private static bool IsApprovalOrCertificationEvidence(JournalEvidenceReference evidence)
        => string.Equals(evidence.Kind?.Trim(), AccountingPostingEvidenceKindDto.Approval.ToString(), StringComparison.OrdinalIgnoreCase) ||
           string.Equals(evidence.Kind?.Trim(), "Certification", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(evidence.Kind?.Trim(), "Certified", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(evidence.Kind?.Trim(), "SignOff", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(evidence.Kind?.Trim(), "Sign-Off", StringComparison.OrdinalIgnoreCase);

    private static string? GetTag(IReadOnlyDictionary<string, string>? tags, string key)
        => tags is not null && tags.TryGetValue(key, out var value)
            ? NormalizeOptional(value)
            : null;

    private static bool IsSha256Fingerprint(string? value)
    {
        var normalized = NormalizeOptional(value);
        if (normalized is null ||
            normalized.Length != 71 ||
            !normalized.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        for (var index = 7; index < normalized.Length; index++)
        {
            var character = normalized[index];
            if (!((character >= '0' && character <= '9') ||
                  (character >= 'a' && character <= 'f') ||
                  (character >= 'A' && character <= 'F')))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryNormalizeScope(
        AutomatedJournalCapitalAccountReconciliationScope scope,
        out AutomatedJournalCapitalAccountReconciliationScope normalized)
    {
        normalized = scope;
        var tenantId = NormalizeOptional(scope.TenantId);
        var companyId = NormalizeOptional(scope.CompanyId);
        var fundProfileId = NormalizeOptional(scope.FundProfileId);
        var entityId = NormalizeOptional(scope.EntityId);
        var periodId = NormalizeOptional(scope.PeriodId);
        var currency = NormalizeOptional(scope.Currency);
        if (tenantId is null ||
            companyId is null ||
            fundProfileId is null ||
            entityId is null ||
            periodId is null ||
            currency is null ||
            scope.LedgerBookId == Guid.Empty ||
            scope.EvaluatedAtUtc == default)
        {
            return false;
        }

        normalized = scope with
        {
            TenantId = tenantId,
            CompanyId = companyId,
            FundProfileId = fundProfileId,
            EntityId = entityId,
            PeriodId = periodId,
            Currency = currency.ToUpperInvariant(),
            EvaluatedAtUtc = scope.EvaluatedAtUtc.ToUniversalTime()
        };
        return true;
    }

    private static bool MatchesOwner(
        Meridian.Contracts.Tenancy.FundProfileOwnership? owner,
        AutomatedJournalCapitalAccountReconciliationScope scope)
        => owner is not null &&
           EqualsText(owner.FundProfileId, scope.FundProfileId) &&
           EqualsText(owner.TenantId, scope.TenantId) &&
           EqualsText(owner.CompanyId, scope.CompanyId);

    private static bool MatchesPeriod(LedgerAccountingPeriod period, string periodId)
        => EqualsText(period.Label, periodId) ||
           EqualsText(FormattableString.Invariant($"{period.FiscalYear:D4}-{period.PeriodNo:D2}"), periodId) ||
           EqualsText(period.PeriodId.ToString("D"), periodId);

    private static bool IsCurrentPeriodFeePosting(
        LedgerJournalEntryRecord record,
        LedgerAccountingPeriod currentPeriod)
    {
        if (record.PeriodId != currentPeriod.PeriodId)
        {
            return false;
        }

        var idempotencyKey = NormalizeOptional(record.Entry.Metadata.IdempotencyKey);
        return idempotencyKey?.StartsWith("mgmt-fee|", StringComparison.OrdinalIgnoreCase) == true ||
               idempotencyKey?.StartsWith("perf-fee|", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static bool HasMatchingLine(JournalEntry entry, LedgerLineDimensionSet expected)
        => entry.Lines.Any(line => MatchesDimensions(line.Dimensions, expected));

    private static bool MatchesDimensions(
        LedgerLineDimensionSet? actual,
        LedgerLineDimensionSet expected)
        => actual is not null &&
           EqualsText(actual.FundId, expected.FundId) &&
           EqualsText(actual.EntityId, expected.EntityId) &&
           EqualsText(actual.BookId, expected.BookId);

    private static Meridian.Ledger.Ledger Hydrate(IReadOnlyList<LedgerJournalEntryRecord> records)
    {
        var ledger = new Meridian.Ledger.Ledger();
        foreach (var record in records)
        {
            ledger.Post(record.Entry);
        }

        return ledger;
    }

    private static decimal CalculateNav(
        IReadOnlyLedger ledger,
        DateTimeOffset asOfUtc,
        LedgerLineDimensionSet dimensions)
    {
        var statements = LedgerFinancialStatementBuilder.BuildAsOf(
            ledger,
            asOfUtc,
            lineDimensions: dimensions);
        return statements.TotalAssets - statements.TotalLiabilities;
    }

    private static decimal CalculateCapitalAccountBalance(
        IReadOnlyLedger ledger,
        DateTimeOffset asOfUtc,
        LedgerLineDimensionSet dimensions,
        IReadOnlyList<string> capitalAccountIds)
    {
        var total = 0m;
        foreach (var capitalAccountId in capitalAccountIds)
        {
            var scopedDimensions = dimensions with { CapitalAccountId = capitalAccountId };
            total += ledger
                .TrialBalanceAsOf(asOfUtc, lineDimensions: scopedDimensions)
                .Where(static pair => pair.Key.AccountType == LedgerAccountType.Equity)
                .Sum(static pair => pair.Value);
        }

        return total;
    }

    private static string BuildSourceVersion(
        AutomatedJournalCapitalAccountReconciliationScope scope,
        LedgerBookRecord book,
        LedgerAccountingPeriod currentPeriod,
        IReadOnlyList<LedgerAccountingPeriod> periods,
        IReadOnlyList<LedgerJournalEntryRecord> records,
        IReadOnlyDictionary<Guid, IReadOnlyList<JournalEvidenceReference>> evidenceByRecord,
        IReadOnlyDictionary<Guid, GovernedReviewProvenance> governanceByRecord)
    {
        var canonical = new StringBuilder();
        Append(canonical, scope.TenantId.ToLowerInvariant());
        Append(canonical, scope.CompanyId.ToLowerInvariant());
        Append(canonical, scope.FundProfileId.ToLowerInvariant());
        Append(canonical, scope.LedgerBookId.ToString("D"));
        Append(canonical, scope.EntityId.ToLowerInvariant());
        Append(canonical, currentPeriod.PeriodId.ToString("D"));
        Append(canonical, scope.Currency.ToUpperInvariant());
        Append(canonical, book.UpdatedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        Append(canonical, book.AccountingBasis.ToString());
        Append(canonical, book.AccountingPolicyId);
        Append(canonical, book.AccountingPolicyVersion);

        foreach (var period in periods)
        {
            Append(canonical, period.PeriodId.ToString("D"));
            Append(canonical, period.StartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            Append(canonical, period.EndDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            Append(canonical, period.Status);
            Append(canonical, period.Version.ToString(CultureInfo.InvariantCulture));
        }

        foreach (var record in records)
        {
            Append(canonical, record.Entry.JournalEntryId.ToString("D"));
            Append(canonical, record.PeriodId.ToString("D"));
            Append(canonical, record.GlobalSequence.ToString(CultureInfo.InvariantCulture));
            Append(canonical, record.CreatedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
            Append(canonical, record.Entry.Timestamp.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
            Append(canonical, record.AccountingBasis.ToString());
            Append(canonical, record.AccountingPolicyId);
            Append(canonical, record.AccountingPolicyVersion);
            Append(canonical, record.RuleId);
            Append(canonical, record.RuleVersion);
            Append(canonical, record.SourceEventId?.ToString("D"));
            Append(canonical, record.Entry.Metadata.IdempotencyKey);
            var governance = governanceByRecord[record.Entry.JournalEntryId];
            Append(canonical, governance.Kind);
            Append(canonical, governance.GovernanceId);
            Append(canonical, governance.ReviewedBy);
            Append(canonical, governance.ReviewedAtUtc.ToString("O", CultureInfo.InvariantCulture));
            Append(canonical, governance.GovernanceVersion);
            foreach (var line in record.Entry.Lines.OrderBy(static line => line.EntryId))
            {
                Append(canonical, line.EntryId.ToString("D"));
                Append(canonical, line.Account.Name);
                Append(canonical, line.Account.AccountType.ToString());
                Append(canonical, line.Account.Symbol);
                Append(canonical, line.Account.FinancialAccountId);
                Append(canonical, line.Debit.ToString(CultureInfo.InvariantCulture));
                Append(canonical, line.Credit.ToString(CultureInfo.InvariantCulture));
                Append(canonical, line.Dimensions?.FundId);
                Append(canonical, line.Dimensions?.EntityId);
                Append(canonical, line.Dimensions?.BookId);
                Append(canonical, line.Dimensions?.InvestorId);
                Append(canonical, line.Dimensions?.CapitalAccountId);
            }

            foreach (var evidence in evidenceByRecord[record.Entry.JournalEntryId])
            {
                Append(canonical, evidence.EvidenceId);
                Append(canonical, evidence.Uri);
                Append(canonical, evidence.Kind);
                Append(canonical, evidence.SourceSystem);
                Append(canonical, evidence.RetainedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
                Append(canonical, evidence.RetainedBy);
                Append(canonical, evidence.ContentHash);
            }
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())))
            .ToLowerInvariant();
    }

    private static void Append(StringBuilder builder, string? value)
        => builder.Append(value?.Trim() ?? "<null>").Append('\u001f');

    private static DateTimeOffset StartOfDayUtc(DateOnly date)
        => new(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

    private static DateTimeOffset EndOfDayUtc(DateOnly date)
        => new(date.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);

    private static bool EqualsText(string? left, string? right)
        => string.Equals(
            NormalizeOptional(left),
            NormalizeOptional(right),
            StringComparison.OrdinalIgnoreCase);

    private sealed record GovernedReviewProvenance(
        string Kind,
        string GovernanceId,
        string ReviewedBy,
        DateTimeOffset ReviewedAtUtc,
        string GovernanceVersion);
}
