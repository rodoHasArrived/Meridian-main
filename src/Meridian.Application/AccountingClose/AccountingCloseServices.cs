using System.Collections.Concurrent;
using System.Collections.Immutable;

namespace Meridian.Application.AccountingClose;

public sealed class AccountingPostingService
{
    private readonly ConcurrentDictionary<string, ImmutableArray<JournalEntry>> _journals = new();

    public ImmutableArray<JournalEntry> Post(string ledgerId, IEnumerable<JournalEntry> entries)
    {
        var ordered = entries.OrderBy(static e => e.TradeDate).ThenBy(static e => e.JournalEntryId).ToImmutableArray();
        _journals[ledgerId] = ordered;
        return ordered;
    }

    public ImmutableArray<JournalEntry> Replay(string ledgerId) => _journals.TryGetValue(ledgerId, out var entries) ? entries : ImmutableArray<JournalEntry>.Empty;

    public ImmutableArray<SourceLinkedAuditLine> Audit(string ledgerId) => Replay(ledgerId)
        .Select(static e => new SourceLinkedAuditLine(e.JournalEntryId, e.SourceEventId, $"approval-{e.JournalEntryId:N}"))
        .ToImmutableArray();
}

public sealed class FxTranslationService
{
    public TranslationAdjustment Translate(string ledgerId, DateOnly period, string accountCode, decimal functionalAmount, FxRate rate)
    {
        var reportingAmount = decimal.Round(functionalAmount * rate.Rate, 2, MidpointRounding.AwayFromZero);
        var adjustment = reportingAmount - functionalAmount;
        return new TranslationAdjustment(Guid.NewGuid(), ledgerId, period, accountCode, functionalAmount, reportingAmount, adjustment, rate.SourceEventId);
    }
}

public sealed class MonthEndCloseStateMachine
{
    public ClosePeriod Transition(ClosePeriod current, CloseEvidence evidence, bool isTrialBalanceBalanced)
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

        var nextState = blockers.Count switch
        {
            0 when current.State is ClosePeriodState.Validating or ClosePeriodState.Open => ClosePeriodState.Closed,
            0 => current.State,
            _ when current.State == ClosePeriodState.Open => ClosePeriodState.Validating,
            _ => ClosePeriodState.Blocked
        };

        return current with { State = nextState, Evidence = evidence, Blockers = blockers.ToImmutable() };
    }
}

public sealed class TrialBalanceProjectionService
{
    public ImmutableArray<TrialBalanceLine> BuildTrialBalance(IEnumerable<JournalEntry> entries)
    {
        return entries
            .SelectMany(static entry => entry.Lines)
            .GroupBy(static line => line.AccountCode)
            .Select(group =>
            {
                var debit = group.Where(static line => line.IsDebit).Sum(static line => line.Amount);
                var credit = group.Where(static line => !line.IsDebit).Sum(static line => line.Amount);
                return new TrialBalanceLine(group.Key, debit, credit, debit - credit);
            })
            .OrderBy(static line => line.AccountCode)
            .ToImmutableArray();
    }

    public bool IsBalanced(IEnumerable<TrialBalanceLine> lines)
    {
        var totalDebit = lines.Sum(static line => line.Debit);
        var totalCredit = lines.Sum(static line => line.Credit);
        return decimal.Round(totalDebit - totalCredit, 2, MidpointRounding.AwayFromZero) == 0m;
    }

    public ImmutableArray<RollForwardLine> BuildRollForward(
        IEnumerable<TrialBalanceLine> opening,
        IEnumerable<TrialBalanceLine> activity,
        IEnumerable<TranslationAdjustment> fx)
    {
        var map = opening.ToDictionary(static x => x.AccountCode, static x => x.Net);
        foreach (var line in activity)
        {
            map.TryGetValue(line.AccountCode, out var current);
            map[line.AccountCode] = current + line.Net;
        }

        var fxMap = fx.GroupBy(static x => x.AccountCode).ToDictionary(static g => g.Key, static g => g.Sum(static x => x.AdjustmentAmount));
        return map.Select(kvp =>
        {
            fxMap.TryGetValue(kvp.Key, out var adj);
            var openingBalance = opening.FirstOrDefault(x => x.AccountCode == kvp.Key)?.Net ?? 0m;
            var closing = kvp.Value + adj;
            return new RollForwardLine(kvp.Key, openingBalance, kvp.Value - openingBalance, adj, closing);
        }).OrderBy(static x => x.AccountCode).ToImmutableArray();
    }
}
