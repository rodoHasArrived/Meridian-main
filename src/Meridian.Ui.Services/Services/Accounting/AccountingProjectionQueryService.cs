using System.Collections.Immutable;
using Meridian.Contracts.Ledger;
using Meridian.FinancialOperations.AccountingClose;

namespace Meridian.Ui.Services.Services.Accounting;

public interface IAccountingProjectionQueryService
{
    ImmutableArray<TrialBalanceLine> GetTrialBalance(string ledgerId, LedgerDimensionSetDto? dimensions = null);
    ImmutableArray<RollForwardLine> GetRollForward(string ledgerId, LedgerDimensionSetDto? dimensions = null);
    ImmutableArray<SourceLinkedAuditLine> GetAuditLines(string ledgerId);
    SourceLinkedAuditLine? GetAuditLine(string ledgerId, Guid journalEntryId);
    AccountingCloseProjection GetCloseProjection(
        string ledgerId,
        DateOnly period,
        CloseEvidence evidence,
        LedgerDimensionSetDto? dimensions = null);
}

public sealed class AccountingProjectionQueryService : IAccountingProjectionQueryService
{
    private readonly AccountingPostingService _postingService;
    private readonly TrialBalanceProjectionService _projectionService;
    private readonly MonthEndCloseStateMachine _closeStateMachine;
    private readonly CloseEvidencePackageService _evidencePackageService;

    public AccountingProjectionQueryService(
        AccountingPostingService postingService,
        TrialBalanceProjectionService projectionService,
        MonthEndCloseStateMachine? closeStateMachine = null,
        CloseEvidencePackageService? evidencePackageService = null)
    {
        _postingService = postingService;
        _projectionService = projectionService;
        _closeStateMachine = closeStateMachine ?? new MonthEndCloseStateMachine();
        _evidencePackageService = evidencePackageService ?? new CloseEvidencePackageService();
    }

    public ImmutableArray<TrialBalanceLine> GetTrialBalance(string ledgerId, LedgerDimensionSetDto? dimensions = null)
        => _projectionService.BuildTrialBalance(_postingService.Replay(ledgerId), dimensions);

    public ImmutableArray<RollForwardLine> GetRollForward(string ledgerId, LedgerDimensionSetDto? dimensions = null)
    {
        var trial = GetTrialBalance(ledgerId, dimensions);
        return _projectionService.BuildRollForward([], trial, []);
    }

    public ImmutableArray<SourceLinkedAuditLine> GetAuditLines(string ledgerId)
        => _postingService.Audit(ledgerId);

    public SourceLinkedAuditLine? GetAuditLine(string ledgerId, Guid journalEntryId)
        => GetAuditLines(ledgerId).FirstOrDefault(line => line.JournalEntryId == journalEntryId);

    public AccountingCloseProjection GetCloseProjection(
        string ledgerId,
        DateOnly period,
        CloseEvidence evidence,
        LedgerDimensionSetDto? dimensions = null)
    {
        var trialBalance = GetTrialBalance(ledgerId, dimensions);
        var rollForward = GetRollForward(ledgerId, dimensions);
        var audit = GetAuditLines(ledgerId);
        var balanced = _projectionService.IsBalanced(trialBalance);
        var closePeriod = new ClosePeriod(ledgerId, period, ClosePeriodState.Validating, evidence, []);
        var state = _closeStateMachine.Transition(closePeriod, evidence, balanced);
        var evidencePackage = _evidencePackageService.Build(state, balanced, audit);
        return new AccountingCloseProjection(
            ledgerId,
            period,
            state,
            balanced,
            trialBalance,
            rollForward,
            audit,
            evidencePackage,
            evidencePackage.ApprovalHistory);
    }
}

public sealed record AccountingCloseProjection(
    string LedgerId,
    DateOnly Period,
    ClosePeriod ClosePeriod,
    bool TrialBalanceBalanced,
    ImmutableArray<TrialBalanceLine> TrialBalance,
    ImmutableArray<RollForwardLine> RollForward,
    ImmutableArray<SourceLinkedAuditLine> AuditTrail,
    CloseEvidencePackage EvidencePackage,
    ImmutableArray<CloseApprovalHistoryEntry> ApprovalHistory);
