using System.Collections.Immutable;
using Meridian.Application.AccountingClose;

namespace Meridian.Ui.Services.Services.Accounting;

public interface IAccountingProjectionQueryService
{
    ImmutableArray<TrialBalanceLine> GetTrialBalance(string ledgerId);
    ImmutableArray<RollForwardLine> GetRollForward(string ledgerId);
    ImmutableArray<SourceLinkedAuditLine> GetAuditLines(string ledgerId);
}

public sealed class AccountingProjectionQueryService : IAccountingProjectionQueryService
{
    private readonly AccountingPostingService _postingService;
    private readonly TrialBalanceProjectionService _projectionService;

    public AccountingProjectionQueryService(AccountingPostingService postingService, TrialBalanceProjectionService projectionService)
    {
        _postingService = postingService;
        _projectionService = projectionService;
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
}
