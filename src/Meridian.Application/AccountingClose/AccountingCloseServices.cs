using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;

namespace Meridian.Application.AccountingClose;

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
    public ImmutableArray<TrialBalanceLine> BuildTrialBalance(IEnumerable<JournalEntry> entries)
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
                if (!buckets.TryGetValue(line.AccountCode, out var accumulator))
                {
                    accumulator = new TrialBalanceAccumulator(line.AccountCode);
                    buckets[line.AccountCode] = accumulator;
                }

                accumulator.Add(entry, line);
            }
        }

        return buckets.Values
            .OrderBy(static accumulator => accumulator.AccountCode, StringComparer.OrdinalIgnoreCase)
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
        var accountCodes = openingRows.Select(static row => row.AccountCode)
            .Concat(activityRows.Select(static row => row.AccountCode))
            .Concat(fxRows.Select(static row => row.AccountCode))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase);

        return accountCodes.Select(accountCode =>
        {
            var openingBalance = openingRows.Where(row => string.Equals(row.AccountCode, accountCode, StringComparison.OrdinalIgnoreCase)).Sum(static row => row.Net);
            var activityBalance = activityRows.Where(row => string.Equals(row.AccountCode, accountCode, StringComparison.OrdinalIgnoreCase)).Sum(static row => row.Net);
            var adjustment = fxRows.Where(row => string.Equals(row.AccountCode, accountCode, StringComparison.OrdinalIgnoreCase)).Sum(static row => row.AdjustmentAmount);
            var sourceEventIds = activityRows.Where(row => string.Equals(row.AccountCode, accountCode, StringComparison.OrdinalIgnoreCase))
                .SelectMany(static row => row.SourceEventIds.IsDefault ? ImmutableArray<string>.Empty : row.SourceEventIds)
                .Concat(fxRows.Where(row => string.Equals(row.AccountCode, accountCode, StringComparison.OrdinalIgnoreCase)).Select(static row => row.SourceEventId))
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToImmutableArray();
            var approvalIds = activityRows.Where(row => string.Equals(row.AccountCode, accountCode, StringComparison.OrdinalIgnoreCase))
                .SelectMany(static row => row.ApprovalIds.IsDefault ? ImmutableArray<string>.Empty : row.ApprovalIds)
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToImmutableArray();
            return new RollForwardLine(accountCode, openingBalance, activityBalance, adjustment, openingBalance + activityBalance + adjustment, sourceEventIds, approvalIds);
        }).ToImmutableArray();
    }
}
internal sealed class TrialBalanceAccumulator
{
    private readonly HashSet<string> _sourceEventIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _approvalIds = new(StringComparer.OrdinalIgnoreCase);

    public TrialBalanceAccumulator(string accountCode)
    {
        AccountCode = accountCode;
    }

    public string AccountCode { get; }
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
            _approvalIds.Order(StringComparer.OrdinalIgnoreCase).ToImmutableArray());

    private static void AddIfPresent(string value, HashSet<string> target)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            target.Add(value.Trim());
        }
    }
}
