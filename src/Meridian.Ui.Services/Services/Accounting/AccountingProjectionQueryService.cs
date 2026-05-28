using System.Collections.Immutable;
using Meridian.Application.AccountingClose;

namespace Meridian.Ui.Services.Services.Accounting;

public interface IAccountingProjectionQueryService
{
    ImmutableArray<TrialBalanceLine> GetTrialBalance(string ledgerId);
    ImmutableArray<RollForwardLine> GetRollForward(string ledgerId);
    ImmutableArray<SourceLinkedAuditLine> GetAuditLines(string ledgerId);
    SourceLinkedAuditLine? GetAuditLine(string ledgerId, Guid journalEntryId);
    AccountingCloseProjection GetCloseProjection(string ledgerId, DateOnly period, CloseEvidence evidence);
}

public sealed class AccountingProjectionQueryService : IAccountingProjectionQueryService
{
    private readonly AccountingPostingService _postingService;
    private readonly TrialBalanceProjectionService _projectionService;
    private readonly MonthEndCloseStateMachine _closeStateMachine;

    public AccountingProjectionQueryService(
        AccountingPostingService postingService,
        TrialBalanceProjectionService projectionService,
        MonthEndCloseStateMachine? closeStateMachine = null)
    {
        _postingService = postingService;
        _projectionService = projectionService;
        _closeStateMachine = closeStateMachine ?? new MonthEndCloseStateMachine();
    }

    public ImmutableArray<TrialBalanceLine> GetTrialBalance(string ledgerId)
        => _projectionService.BuildTrialBalance(_postingService.Replay(ledgerId));

    public ImmutableArray<RollForwardLine> GetRollForward(string ledgerId)
    {
        var trial = GetTrialBalance(ledgerId);
        return _projectionService.BuildRollForward([], trial, []);
    }

    public ImmutableArray<SourceLinkedAuditLine> GetAuditLines(string ledgerId)
        => _postingService.Audit(ledgerId);

    public SourceLinkedAuditLine? GetAuditLine(string ledgerId, Guid journalEntryId)
        => GetAuditLines(ledgerId).FirstOrDefault(line => line.JournalEntryId == journalEntryId);

    public AccountingCloseProjection GetCloseProjection(string ledgerId, DateOnly period, CloseEvidence evidence)
    {
        var trialBalance = GetTrialBalance(ledgerId);
        var rollForward = GetRollForward(ledgerId);
        var audit = GetAuditLines(ledgerId);
        var balanced = _projectionService.IsBalanced(trialBalance);
        var closePeriod = new ClosePeriod(ledgerId, period, ClosePeriodState.Validating, evidence, []);
        var state = _closeStateMachine.Transition(closePeriod, evidence, balanced);
        return new AccountingCloseProjection(ledgerId, period, state, balanced, trialBalance, rollForward, audit);
    }
}

public sealed record AccountingCloseProjection(
    string LedgerId,
    DateOnly Period,
    ClosePeriod ClosePeriod,
    bool TrialBalanceBalanced,
    ImmutableArray<TrialBalanceLine> TrialBalance,
    ImmutableArray<RollForwardLine> RollForward,
    ImmutableArray<SourceLinkedAuditLine> AuditTrail);
