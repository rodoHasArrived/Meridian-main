using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using Meridian.Contracts.Ledger;

namespace Meridian.FinancialOperations.AccountingClose;

public sealed class AccountingPostingService
{
    private readonly ConcurrentDictionary<string, ImmutableArray<JournalEntry>> _journals = new(StringComparer.OrdinalIgnoreCase);

    public ImmutableArray<JournalEntry> Post(string ledgerId, IEnumerable<JournalEntry> entries)
    {
        var result = PostWithResult(ledgerId, entries);
        if (!result.Accepted)
        {
            throw new InvalidOperationException(string.Join(" ", result.RejectedReasons));
        }

        return result.PostedEntries;
    }

    public JournalPostingResult PostWithResult(
        string ledgerId,
        IEnumerable<JournalEntry> entries,
        ClosePeriod? closePeriod = null)
    {
        if (string.IsNullOrWhiteSpace(ledgerId))
        {
            throw new ArgumentException("Ledger id is required.", nameof(ledgerId));
        }

        ArgumentNullException.ThrowIfNull(entries);

        var rejected = ImmutableArray.CreateBuilder<string>();
        if (closePeriod?.IsLocked == true)
        {
            rejected.Add($"Accounting period {closePeriod.Period:yyyy-MM-dd} for ledger {closePeriod.LedgerId} is locked.");
        }

        var normalized = entries
            .Select(entry => NormalizeEntry(ledgerId, entry))
            .OrderBy(static entry => entry.AccountingPeriod ?? entry.TradeDate)
            .ThenBy(static entry => entry.TradeDate)
            .ThenBy(static entry => entry.JournalEntryId)
            .ToImmutableArray();

        foreach (var entry in normalized)
        {
            if (entry.Lines.IsDefaultOrEmpty)
            {
                rejected.Add($"Journal entry {entry.JournalEntryId:D} has no lines.");
                continue;
            }

            foreach (var currencyGroup in entry.Lines.GroupBy(static line => NormalizeCurrency(line.Currency)))
            {
                var debit = currencyGroup.Where(static line => line.IsDebit).Sum(static line => line.Amount);
                var credit = currencyGroup.Where(static line => !line.IsDebit).Sum(static line => line.Amount);
                if (decimal.Round(debit - credit, 2, MidpointRounding.AwayFromZero) != 0m)
                {
                    rejected.Add($"Journal entry {entry.JournalEntryId:D} is out of balance for {currencyGroup.Key}: debits {debit:0.00}, credits {credit:0.00}.");
                }
            }
        }

        if (rejected.Count > 0)
        {
            return new JournalPostingResult(ledgerId, ImmutableArray<JournalEntry>.Empty, ImmutableArray<SourceLinkedAuditLine>.Empty, rejected.ToImmutable());
        }

        _journals.AddOrUpdate(
            ledgerId,
            normalized,
            (_, current) => current.Concat(normalized)
                .OrderBy(static entry => entry.AccountingPeriod ?? entry.TradeDate)
                .ThenBy(static entry => entry.TradeDate)
                .ThenBy(static entry => entry.JournalEntryId)
                .ToImmutableArray());

        var posted = Replay(ledgerId);
        return new JournalPostingResult(ledgerId, posted, BuildAudit(posted), ImmutableArray<string>.Empty);
    }

    public ImmutableArray<JournalEntry> Replay(string ledgerId)
        => _journals.TryGetValue(ledgerId, out var entries) ? entries : ImmutableArray<JournalEntry>.Empty;

    public ImmutableArray<SourceLinkedAuditLine> Audit(string ledgerId) => BuildAudit(Replay(ledgerId));

    private static JournalEntry NormalizeEntry(string ledgerId, JournalEntry entry)
        => entry with
        {
            LedgerId = ledgerId,
            AccountingPeriod = entry.AccountingPeriod ?? new DateOnly(entry.TradeDate.Year, entry.TradeDate.Month, 1),
            Lines = entry.Lines.IsDefault ? ImmutableArray<JournalLine>.Empty : entry.Lines
        };

    private static ImmutableArray<SourceLinkedAuditLine> BuildAudit(IEnumerable<JournalEntry> entries)
        => entries
            .Select(static entry => new SourceLinkedAuditLine(
                entry.JournalEntryId,
                entry.SourceEventId,
                ResolveApprovalId(entry),
                entry.AccountingPeriod,
                entry.Description,
                entry.Lines.IsDefault
                    ? ImmutableArray<string>.Empty
                    : entry.Lines.Select(static line => line.AccountCode).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToImmutableArray()))
            .ToImmutableArray();

    private static string ResolveApprovalId(JournalEntry entry)
        => entry.Lines.IsDefault
            ? $"approval-{entry.JournalEntryId:N}"
            : entry.Lines.Select(static line => line.ApprovalId).FirstOrDefault(static approval => !string.IsNullOrWhiteSpace(approval))
                ?? $"approval-{entry.JournalEntryId:N}";

    private static string NormalizeCurrency(string currency)
        => string.IsNullOrWhiteSpace(currency) ? "UNSPECIFIED" : currency.Trim().ToUpperInvariant();
}

public sealed class FxTranslationService
{
    public TranslationAdjustment Translate(string ledgerId, DateOnly period, string accountCode, decimal functionalAmount, FxRate rate)
    {
        ArgumentNullException.ThrowIfNull(rate);

        var reportingAmount = decimal.Round(functionalAmount * rate.Rate, 2, MidpointRounding.AwayFromZero);
        var adjustment = reportingAmount - functionalAmount;
        var adjustmentId = CreateDeterministicId(ledgerId, period, accountCode, functionalAmount, reportingAmount, rate);
        return new TranslationAdjustment(
            adjustmentId,
            ledgerId,
            period,
            accountCode,
            functionalAmount,
            reportingAmount,
            adjustment,
            rate.SourceEventId,
            rate.FromCurrency,
            rate.ToCurrency,
            string.IsNullOrWhiteSpace(rate.RateId) ? rate.SourceEventId : rate.RateId);
    }

    public ImmutableArray<TranslationAdjustment> TranslateTrialBalance(
        string ledgerId,
        DateOnly period,
        IEnumerable<TrialBalanceLine> lines,
        FxRate rate)
    {
        ArgumentNullException.ThrowIfNull(lines);
        return lines
            .OrderBy(static line => line.AccountCode, StringComparer.OrdinalIgnoreCase)
            .Select(line => Translate(ledgerId, period, line.AccountCode, line.Net, rate))
            .ToImmutableArray();
    }

    private static Guid CreateDeterministicId(
        string ledgerId,
        DateOnly period,
        string accountCode,
        decimal functionalAmount,
        decimal reportingAmount,
        FxRate rate)
    {
        var input = string.Join('|', ledgerId, period.ToString("yyyy-MM-dd"), accountCode, functionalAmount, reportingAmount, rate.FromCurrency, rate.ToCurrency, rate.RateDate.ToString("yyyy-MM-dd"), rate.Rate, rate.SourceEventId, rate.RateId);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return new Guid(hash[..16]);
    }
}

public sealed class MonthEndCloseStateMachine
{
    public ClosePeriod Transition(ClosePeriod current, CloseEvidence evidence, bool isTrialBalanceBalanced)
    {
        ArgumentNullException.ThrowIfNull(evidence);

        if (current.State == ClosePeriodState.Closed)
        {
            return current;
        }

        var blockers = BuildBlockers(evidence, isTrialBalanceBalanced);
        var nextState = current.State switch
        {
            ClosePeriodState.Open => ClosePeriodState.Validating,
            ClosePeriodState.Validating when blockers.Count == 0 => ClosePeriodState.Closed,
            ClosePeriodState.Validating => ClosePeriodState.Blocked,
            ClosePeriodState.Blocked when blockers.Count == 0 => ClosePeriodState.Closed,
            ClosePeriodState.Blocked => ClosePeriodState.Blocked,
            _ => current.State
        };

        return current with
        {
            State = nextState,
            Evidence = evidence,
            Blockers = blockers.ToImmutable(),
            LockedAt = nextState == ClosePeriodState.Closed ? DateTimeOffset.UtcNow : current.LockedAt
        };
    }

    private static ImmutableArray<string>.Builder BuildBlockers(CloseEvidence evidence, bool isTrialBalanceBalanced)
    {
        var blockers = ImmutableArray.CreateBuilder<string>();
        if (!isTrialBalanceBalanced)
        {
            blockers.Add("Trial balance is out of balance.");
        }

        if (!evidence.TrialBalanceSignedOff)
        {
            blockers.Add("Trial balance sign-off missing.");
        }

        if (!evidence.ReconciliationSignedOff)
        {
            blockers.Add("Reconciliation sign-off missing.");
        }

        if (!evidence.ApprovalsCompleted)
        {
            blockers.Add("Approval evidence missing.");
        }

        foreach (var check in evidence.NormalizedChecks.Where(static check => check.Required && !check.Passed))
        {
            blockers.Add($"{check.Label} evidence check failed: {check.Detail}");
        }

        return blockers;
    }
}

public sealed class TrialBalanceProjectionService
{
    public ImmutableArray<TrialBalanceLine> BuildTrialBalance(
        IEnumerable<JournalEntry> entries,
        LedgerDimensionSetDto? dimensions = null)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var buckets = new Dictionary<string, TrialBalanceAccumulator>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            if (entry.Lines.IsDefaultOrEmpty)
            {
                continue;
            }

            foreach (var line in entry.Lines)
            {
                if (!MatchesDimensions(dimensions, line.Dimensions))
                {
                    continue;
                }

                var bucketKey = BuildBucketKey(line.AccountCode, line.Dimensions);
                if (!buckets.TryGetValue(bucketKey, out var accumulator))
                {
                    accumulator = new TrialBalanceAccumulator(line.AccountCode, line.Dimensions);
                    buckets[bucketKey] = accumulator;
                }

                accumulator.Add(entry, line);
            }
        }

        return buckets.Values
            .OrderBy(static accumulator => accumulator.AccountCode, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static accumulator => BuildDimensionSignature(accumulator.Dimensions), StringComparer.OrdinalIgnoreCase)
            .Select(static accumulator => accumulator.ToLine())
            .ToImmutableArray();
    }

    public bool IsBalanced(IEnumerable<TrialBalanceLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var materialized = lines.ToArray();
        var totalDebit = materialized.Sum(static line => line.Debit);
        var totalCredit = materialized.Sum(static line => line.Credit);
        return decimal.Round(totalDebit - totalCredit, 2, MidpointRounding.AwayFromZero) == 0m;
    }

    public ImmutableArray<RollForwardLine> BuildRollForward(
        IEnumerable<TrialBalanceLine> opening,
        IEnumerable<TrialBalanceLine> activity,
        IEnumerable<TranslationAdjustment> fx)
    {
        ArgumentNullException.ThrowIfNull(opening);
        ArgumentNullException.ThrowIfNull(activity);
        ArgumentNullException.ThrowIfNull(fx);

        var openingRows = opening.ToArray();
        var activityRows = activity.ToArray();
        var fxRows = fx.ToArray();
        var buckets = openingRows
            .Select(static row => (row.AccountCode, row.Dimensions))
            .Concat(activityRows.Select(static row => (row.AccountCode, row.Dimensions)))
            .Concat(fxRows.Select(static row => (row.AccountCode, Dimensions: (LedgerDimensionSetDto?)null)))
            .GroupBy(static row => BuildBucketKey(row.AccountCode, row.Dimensions), StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .OrderBy(static row => row.AccountCode, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static row => BuildDimensionSignature(row.Dimensions), StringComparer.OrdinalIgnoreCase);

        return buckets.Select(bucket =>
        {
            var openingBalance = openingRows
                .Where(row => SameAccountAndDimensions(row.AccountCode, row.Dimensions, bucket.AccountCode, bucket.Dimensions))
                .Sum(static row => row.Net);
            var activityBalance = activityRows
                .Where(row => SameAccountAndDimensions(row.AccountCode, row.Dimensions, bucket.AccountCode, bucket.Dimensions))
                .Sum(static row => row.Net);
            var adjustment = bucket.Dimensions is null
                ? fxRows.Where(row => string.Equals(row.AccountCode, bucket.AccountCode, StringComparison.OrdinalIgnoreCase)).Sum(static row => row.AdjustmentAmount)
                : 0m;
            var sourceEventIds = activityRows.Where(row => SameAccountAndDimensions(row.AccountCode, row.Dimensions, bucket.AccountCode, bucket.Dimensions))
                .SelectMany(static row => row.SourceEventIds.IsDefault ? ImmutableArray<string>.Empty : row.SourceEventIds)
                .Concat(bucket.Dimensions is null
                    ? fxRows.Where(row => string.Equals(row.AccountCode, bucket.AccountCode, StringComparison.OrdinalIgnoreCase)).Select(static row => row.SourceEventId)
                    : [])
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToImmutableArray();
            var approvalIds = activityRows.Where(row => SameAccountAndDimensions(row.AccountCode, row.Dimensions, bucket.AccountCode, bucket.Dimensions))
                .SelectMany(static row => row.ApprovalIds.IsDefault ? ImmutableArray<string>.Empty : row.ApprovalIds)
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToImmutableArray();
            return new RollForwardLine(
                bucket.AccountCode,
                openingBalance,
                activityBalance,
                adjustment,
                openingBalance + activityBalance + adjustment,
                sourceEventIds,
                approvalIds,
                bucket.Dimensions);
        }).ToImmutableArray();
    }

    private static string BuildBucketKey(string accountCode, LedgerDimensionSetDto? dimensions)
        => string.Concat(NormalizeToken(accountCode), "|", BuildDimensionSignature(dimensions));

    private static bool SameAccountAndDimensions(
        string leftAccountCode,
        LedgerDimensionSetDto? leftDimensions,
        string rightAccountCode,
        LedgerDimensionSetDto? rightDimensions)
        => string.Equals(leftAccountCode, rightAccountCode, StringComparison.OrdinalIgnoreCase) &&
           string.Equals(BuildDimensionSignature(leftDimensions), BuildDimensionSignature(rightDimensions), StringComparison.OrdinalIgnoreCase);

    private static bool MatchesDimensions(LedgerDimensionSetDto? expected, LedgerDimensionSetDto? actual)
    {
        if (expected is null)
        {
            return true;
        }

        if (actual is null)
        {
            return !HasAnyDimension(expected);
        }

        return Matches(expected.FundId, actual.FundId) &&
            Matches(expected.EntityId, actual.EntityId) &&
            Matches(expected.SleeveId, actual.SleeveId) &&
            Matches(expected.StrategyId, actual.StrategyId) &&
            Matches(expected.InvestorId, actual.InvestorId) &&
            Matches(expected.CapitalAccountId, actual.CapitalAccountId) &&
            Matches(expected.InstrumentId?.ToString("D"), actual.InstrumentId?.ToString("D")) &&
            Matches(expected.TaxLotId, actual.TaxLotId) &&
            Matches(expected.CostCenterId, actual.CostCenterId) &&
            Matches(expected.CounterpartyId, actual.CounterpartyId) &&
            Matches(expected.OrganizationId, actual.OrganizationId) &&
            Matches(expected.PortfolioId, actual.PortfolioId) &&
            Matches(expected.BookId, actual.BookId) &&
            Matches(expected.AccountId, actual.AccountId) &&
            Matches(expected.CustomerId, actual.CustomerId) &&
            Matches(expected.VendorId, actual.VendorId) &&
            Matches(expected.ProjectId, actual.ProjectId) &&
            ExternalDimensionsMatch(expected.ExternalGlDimensions, actual.ExternalGlDimensions);
    }

    private static bool Matches(string? expected, string? actual)
        => string.IsNullOrWhiteSpace(expected) || string.Equals(expected.Trim(), actual?.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool ExternalDimensionsMatch(
        IReadOnlyDictionary<string, string>? expected,
        IReadOnlyDictionary<string, string>? actual)
    {
        if (expected is null || expected.Count == 0)
        {
            return true;
        }

        if (actual is null)
        {
            return false;
        }

        foreach (var (key, value) in expected)
        {
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            if (!actual.TryGetValue(key.Trim(), out var actualValue) ||
                !string.Equals(value.Trim(), actualValue?.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static string BuildDimensionSignature(LedgerDimensionSetDto? dimensions)
    {
        if (dimensions is null || !HasAnyDimension(dimensions))
        {
            return "dimension:none";
        }

        var externalGl = dimensions.ExternalGlDimensions
            .Where(static pair => !string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
            .OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(static pair => $"{NormalizeToken(pair.Key)}={NormalizeToken(pair.Value)}");
        return string.Join(
            "|",
            NormalizeToken(dimensions.FundId),
            NormalizeToken(dimensions.EntityId),
            NormalizeToken(dimensions.SleeveId),
            NormalizeToken(dimensions.StrategyId),
            NormalizeToken(dimensions.InvestorId),
            NormalizeToken(dimensions.CapitalAccountId),
            dimensions.InstrumentId?.ToString("D") ?? string.Empty,
            NormalizeToken(dimensions.TaxLotId),
            NormalizeToken(dimensions.CostCenterId),
            NormalizeToken(dimensions.CounterpartyId),
            NormalizeToken(dimensions.OrganizationId),
            NormalizeToken(dimensions.PortfolioId),
            NormalizeToken(dimensions.BookId),
            NormalizeToken(dimensions.AccountId),
            NormalizeToken(dimensions.CustomerId),
            NormalizeToken(dimensions.VendorId),
            NormalizeToken(dimensions.ProjectId),
            string.Join(",", externalGl));
    }

    private static bool HasAnyDimension(LedgerDimensionSetDto dimensions)
        => !string.IsNullOrWhiteSpace(dimensions.FundId) ||
            !string.IsNullOrWhiteSpace(dimensions.EntityId) ||
            !string.IsNullOrWhiteSpace(dimensions.SleeveId) ||
            !string.IsNullOrWhiteSpace(dimensions.StrategyId) ||
            !string.IsNullOrWhiteSpace(dimensions.InvestorId) ||
            !string.IsNullOrWhiteSpace(dimensions.CapitalAccountId) ||
            dimensions.InstrumentId.HasValue ||
            !string.IsNullOrWhiteSpace(dimensions.TaxLotId) ||
            !string.IsNullOrWhiteSpace(dimensions.CostCenterId) ||
            !string.IsNullOrWhiteSpace(dimensions.CounterpartyId) ||
            !string.IsNullOrWhiteSpace(dimensions.OrganizationId) ||
            !string.IsNullOrWhiteSpace(dimensions.PortfolioId) ||
            !string.IsNullOrWhiteSpace(dimensions.BookId) ||
            !string.IsNullOrWhiteSpace(dimensions.AccountId) ||
            !string.IsNullOrWhiteSpace(dimensions.CustomerId) ||
            !string.IsNullOrWhiteSpace(dimensions.VendorId) ||
            !string.IsNullOrWhiteSpace(dimensions.ProjectId) ||
            dimensions.ExternalGlDimensions.Any(static pair => !string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value));

    private static string NormalizeToken(string? value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToUpperInvariant();
}
internal sealed class TrialBalanceAccumulator
{
    private readonly HashSet<string> _sourceEventIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _approvalIds = new(StringComparer.OrdinalIgnoreCase);

    public TrialBalanceAccumulator(string accountCode, LedgerDimensionSetDto? dimensions)
    {
        AccountCode = accountCode;
        Dimensions = dimensions;
    }

    public string AccountCode { get; }
    public LedgerDimensionSetDto? Dimensions { get; }
    public decimal Debit { get; private set; }
    public decimal Credit { get; private set; }

    public void Add(JournalEntry entry, JournalLine line)
    {
        if (line.IsDebit)
        {
            Debit += line.Amount;
        }
        else
        {
            Credit += line.Amount;
        }

        AddIfPresent(entry.SourceEventId, _sourceEventIds);
        AddIfPresent(line.SourceEventId, _sourceEventIds);
        AddIfPresent(line.ApprovalId, _approvalIds);
    }

    public TrialBalanceLine ToLine()
        => new(
            AccountCode,
            Debit,
            Credit,
            Debit - Credit,
            _sourceEventIds.Order(StringComparer.OrdinalIgnoreCase).ToImmutableArray(),
            _approvalIds.Order(StringComparer.OrdinalIgnoreCase).ToImmutableArray(),
            Dimensions);

    private static void AddIfPresent(string value, HashSet<string> target)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            target.Add(value.Trim());
        }
    }
}
