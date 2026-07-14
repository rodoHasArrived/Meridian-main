using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.AccountingClose;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Projects the Financial Operations close gate onto the shared manual-journal workbench.
/// It may create drafts or governed reversal drafts, but approval and posting stay human actions.
/// </summary>
public sealed class AccountingClosePostingWorkbenchBridge : IAccountingClosePostingWorkbench
{
    private const string GateLabel = "Post closing entries";
    private readonly AutomatedJournalIntakeRunner _runner;
    private readonly IManualJournalEntryWorkbenchService _workbench;
    private readonly IManualJournalEntryLifecycleService _lifecycle;
    private readonly ILedgerBookService _ledgerBookService;

    public AccountingClosePostingWorkbenchBridge(
        AutomatedJournalIntakeRunner runner,
        IManualJournalEntryWorkbenchService workbench,
        IManualJournalEntryLifecycleService lifecycle,
        ILedgerBookService ledgerBookService)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _workbench = workbench ?? throw new ArgumentNullException(nameof(workbench));
        _lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
        _ledgerBookService = ledgerBookService ?? throw new ArgumentNullException(nameof(ledgerBookService));
    }

    public async Task<ClosePostingGateDto> EvaluateAsync(
        AccountingClosePostingContext context,
        CancellationToken ct = default)
    {
        ValidateContext(context);
        try
        {
            var period = await ResolvePeriodAsync(context, ct).ConfigureAwait(false);
            var preview = await _runner
                .PreviewPeriodCloseAsync(ToIntakeRequest(context, period.PeriodId, "close-gate-preview"), ct)
                .ConfigureAwait(false);
            var workbench = await _workbench
                .GetWorkbenchAsync(context.FundProfileId, context.LedgerBookId, ct)
                .ConfigureAwait(false);
            return BuildGate(context, period.PeriodId, preview, workbench.Drafts);
        }
        catch (InvalidOperationException ex)
        {
            return Blocked(context, ex.Message);
        }
    }

    public async Task<ClosePostingGateDto> EnsureClosingDraftQueuedAsync(
        AccountingClosePostingContext context,
        AccountingClosePostingCommand command,
        CancellationToken ct = default)
    {
        ValidateContext(context);
        ValidateHumanCommand(command, requireController: false);

        var before = await EvaluateAsync(context, ct).ConfigureAwait(false);
        if (before.IsReadyForLock || before.State is ClosePostingGateStateDto.DraftQueued
            or ClosePostingGateStateDto.Submitted or ClosePostingGateStateDto.Approved)
        {
            return before;
        }

        if (before.State == ClosePostingGateStateDto.Blocked)
        {
            return before;
        }

        var period = await ResolvePeriodAsync(context, ct).ConfigureAwait(false);
        await _runner.RunPeriodCloseIntakeAsync(
                ToIntakeRequest(context, period.PeriodId, command.Actor),
                ct)
            .ConfigureAwait(false);
        return await EvaluateAsync(context, ct).ConfigureAwait(false);
    }

    public async Task<LedgerPeriodDto> FinalizeHardCloseAsync(
        AccountingClosePostingContext context,
        AccountingClosePostingCommand command,
        CancellationToken ct = default)
    {
        ValidateContext(context);
        ValidateHumanCommand(command, requireController: false);
        var period = await ResolvePeriodAsync(context, ct).ConfigureAwait(false);
        if (period.Status == LedgerPeriodStatusDto.HardClosed)
        {
            return period;
        }

        if (period.Status != LedgerPeriodStatusDto.SoftClosed)
        {
            throw new InvalidOperationException(
                $"Ledger period '{period.Label}' must be soft-closed before the governed hard-close gate can finalize it.");
        }

        // Re-evaluate at the mutation boundary. The management service normally evaluates the
        // gate first, but a direct caller or concurrent adjustment/reversal must not bypass the
        // governed approval state merely because temporary-account balances happen to be zero.
        var gate = await EvaluateAsync(context, ct).ConfigureAwait(false);
        if (!gate.IsReadyForLock)
        {
            throw new InvalidOperationException(
                $"Ledger period '{period.Label}' cannot be hard-closed while the closing-entry gate is {gate.State}: {gate.Detail}");
        }

        var closed = await _ledgerBookService.ClosePeriodAsync(
                period.PeriodId,
                new CloseLedgerPeriodRequest(
                    LedgerPeriodCloseKindDto.HardClose,
                    command.Actor,
                    command.Reason,
                    RequiredSignoffRole: command.Role ?? "Fund Controller",
                    ActionOrigin: command.ActionOrigin),
                ct)
            .ConfigureAwait(false);
        return closed.Period;
    }

    public async Task<ClosePostingGateDto> ReopenAndQueueClosingReversalsAsync(
        AccountingClosePostingContext context,
        AccountingClosePostingCommand command,
        CancellationToken ct = default)
    {
        ValidateContext(context);
        ValidateHumanCommand(command, requireController: true);
        var period = await ResolvePeriodAsync(context, ct).ConfigureAwait(false);
        if (period.Status is not LedgerPeriodStatusDto.HardClosed and not LedgerPeriodStatusDto.SoftClosed)
        {
            throw new InvalidOperationException(
                $"Ledger period '{period.Label}' must be hard-closed, or soft-closed by an idempotent reopen retry, before closing batches can be reversed.");
        }

        var workbench = await _workbench
            .GetWorkbenchAsync(context.FundProfileId, context.LedgerBookId, ct)
            .ConfigureAwait(false);
        var closingBatches = workbench.Drafts
            .Where(draft =>
                draft.EntryType == ManualJournalEntryTypeDto.ClosingEntry &&
                draft.LedgerBookId == context.LedgerBookId &&
                string.Equals(draft.PeriodId, period.PeriodId.ToString("D"), StringComparison.OrdinalIgnoreCase) &&
                draft.Status is ManualJournalEntryStatusDto.Posted or ManualJournalEntryStatusDto.Reversed)
            .OrderByDescending(static draft => draft.PostedAtUtc)
            .ThenByDescending(static draft => draft.JournalEntryId)
            .ToArray();
        if (closingBatches.Length == 0)
        {
            throw new InvalidOperationException(
                $"Period '{context.PeriodId}' has no retained posted closing batch to reverse.");
        }

        var reversalDrafts = workbench.Drafts
            .Where(draft => draft.ReversalOfJournalEntryId.HasValue &&
                            closingBatches.Any(batch => batch.JournalEntryId == draft.ReversalOfJournalEntryId.Value))
            .GroupBy(static draft => draft.ReversalOfJournalEntryId!.Value)
            .ToDictionary(
                static group => group.Key,
                static group => group
                    .OrderByDescending(static draft => draft.UpdatedAtUtc)
                    .ThenByDescending(static draft => draft.JournalEntryId)
                    .First());
        foreach (var batch in closingBatches)
        {
            if (reversalDrafts.ContainsKey(batch.JournalEntryId))
            {
                continue;
            }

            if (batch.Status == ManualJournalEntryStatusDto.Reversed)
            {
                throw new InvalidOperationException(
                    $"Closing batch '{batch.JournalEntryId:D}' is marked Reversed without its retained reversal draft; reopen fails closed.");
            }

            var reversed = await _lifecycle.ApplyLifecycleActionAsync(
                    new JournalEntryLifecycleActionRequestDto(
                        batch.JournalEntryId,
                        context.FundProfileId,
                        JournalEntryLifecycleActionDto.Reverse,
                        command.Actor,
                        batch.Version,
                        command.Reason,
                        command.CorrelationId,
                        command.EvidenceLinks,
                        command.ActionOrigin,
                        PeriodIsLocked: false,
                        LedgerBookId: context.LedgerBookId),
                    ct)
                .ConfigureAwait(false);
            var generated = reversed.GeneratedJournalEntries.SingleOrDefault(draft =>
                draft.ReversalOfJournalEntryId == batch.JournalEntryId)
                ?? throw new InvalidOperationException(
                    $"Reversing closing batch '{batch.JournalEntryId:D}' did not retain a source-linked reversal draft.");
            reversalDrafts[batch.JournalEntryId] = generated;
        }

        if (reversalDrafts.Count != closingBatches.Length)
        {
            throw new InvalidOperationException("Not every retained closing batch has a source-linked reversal draft.");
        }

        if (period.Status == LedgerPeriodStatusDto.SoftClosed)
        {
            if (reversalDrafts.Values.Any(draft => !IsSameReopenReplay(draft, command)))
            {
                throw new InvalidOperationException(
                    "The ledger period is already soft-closed, but retained reversal drafts do not match this reopen correlation/evidence; retry is rejected.");
            }
        }
        else
        {
            await _ledgerBookService.ReopenPeriodAsync(
                    period.PeriodId,
                    new ReopenLedgerPeriodRequest(
                        command.Actor,
                        command.Role!,
                        command.Reason,
                        command.ApprovalReference!,
                        command.EvidenceLinks,
                        command.ActionOrigin),
                    ct)
                .ConfigureAwait(false);
        }

        var evaluated = await EvaluateAsync(context, ct).ConfigureAwait(false);
        var reversalRows = reversalDrafts.Values.ToArray();
        var pendingCount = reversalRows.Count(static draft =>
            draft.Status is not ManualJournalEntryStatusDto.Posted and not ManualJournalEntryStatusDto.CloseLocked);
        return evaluated with
        {
            State = pendingCount > 0 ? ClosePostingGateStateDto.ReversalQueued : evaluated.State,
            IsReadyForLock = false,
            Detail = pendingCount > 0
                ? $"{pendingCount} governed closing-entry reversal draft(s) await independent approval and posting before restatement can be reclosed."
                : "All retained closing-entry reversals are posted; apply the restatement and rerun the closing-entry delta before reclose.",
            EvidenceLinks = command.EvidenceLinks,
            ClosingBatchJournalEntryIds = closingBatches.Select(static draft => draft.JournalEntryId).ToArray(),
            ReversalDraftJournalEntryIds = reversalRows.Select(static draft => draft.JournalEntryId).ToArray()
        };
    }

    private static bool IsSameReopenReplay(
        ManualJournalEntryDraftDto reversalDraft,
        AccountingClosePostingCommand command)
    {
        var hasEvidenceMatch = command.EvidenceLinks.Any(requested =>
            reversalDraft.EvidenceLinks.Any(retained =>
                string.Equals(requested, retained, StringComparison.OrdinalIgnoreCase)));
        if (!hasEvidenceMatch)
        {
            return false;
        }

        return string.IsNullOrWhiteSpace(command.CorrelationId) ||
               reversalDraft.LifecycleTransitions.Any(transition =>
                   string.Equals(transition.CorrelationId, command.CorrelationId, StringComparison.OrdinalIgnoreCase));
    }

    private static ClosePostingGateDto BuildGate(
        AccountingClosePostingContext context,
        Guid ledgerPeriodId,
        PeriodCloseDraftPreview preview,
        IReadOnlyList<ManualJournalEntryDraftDto> drafts)
    {
        var periodId = ledgerPeriodId.ToString("D");
        var periodDrafts = drafts
            .Where(draft =>
                draft.LedgerBookId == context.LedgerBookId &&
                string.Equals(draft.PeriodId, periodId, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var closingBatches = periodDrafts
            .Where(static draft =>
                draft.EntryType == ManualJournalEntryTypeDto.ClosingEntry &&
                draft.Status is ManualJournalEntryStatusDto.Posted or ManualJournalEntryStatusDto.CloseLocked or ManualJournalEntryStatusDto.Reversed)
            .Select(static draft => draft.JournalEntryId)
            .Distinct()
            .ToArray();
        var pendingReversals = periodDrafts
            .Where(static draft =>
                draft.ReversalOfJournalEntryId.HasValue &&
                draft.Status is not ManualJournalEntryStatusDto.Posted and not ManualJournalEntryStatusDto.CloseLocked)
            .ToArray();
        if (pendingReversals.Length > 0)
        {
            return new ClosePostingGateDto(
                GateId(context, ledgerPeriodId),
                GateLabel,
                ClosePostingGateStateDto.ReversalQueued,
                false,
                preview.Projection.NetIncome,
                preview.Projection.Lines.Count,
                $"{pendingReversals.Length} closing-entry reversal draft(s) await approval and posting.",
                Balances: ToBalances(preview),
                EvidenceLinks: pendingReversals.SelectMany(static draft => draft.EvidenceLinks).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                ClosingBatchJournalEntryIds: closingBatches,
                ReversalDraftJournalEntryIds: pendingReversals.Select(static draft => draft.JournalEntryId).ToArray());
        }

        if (preview.Draft is null)
        {
            var posted = closingBatches.Length > 0;
            return new ClosePostingGateDto(
                GateId(context, ledgerPeriodId),
                GateLabel,
                posted ? ClosePostingGateStateDto.Posted : ClosePostingGateStateDto.NotRequired,
                true,
                0m,
                0,
                posted
                    ? "All revenue and expense balances are zero; retained closing entries are posted."
                    : "All revenue and expense balances are zero; no closing entry is required.",
                ClosingBatchJournalEntryIds: closingBatches);
        }

        var key = preview.Draft.Event.IdempotencyKey;
        var matching = periodDrafts.FirstOrDefault(draft =>
            string.Equals(draft.TreasuryContext?.IdempotencyKey, key, StringComparison.OrdinalIgnoreCase));
        if (matching is null)
        {
            return new ClosePostingGateDto(
                GateId(context, ledgerPeriodId),
                GateLabel,
                ClosePostingGateStateDto.Required,
                false,
                preview.Projection.NetIncome,
                preview.Projection.Lines.Count,
                "Non-zero revenue or expense balances remain. Queue and post the projected closing-entry delta before period lock.",
                IdempotencyKey: key,
                Balances: ToBalances(preview),
                ClosingBatchJournalEntryIds: closingBatches);
        }

        var state = matching.Status switch
        {
            ManualJournalEntryStatusDto.Draft or ManualJournalEntryStatusDto.NeedsFix => ClosePostingGateStateDto.DraftQueued,
            ManualJournalEntryStatusDto.Submitted => ClosePostingGateStateDto.Submitted,
            ManualJournalEntryStatusDto.Approved => ClosePostingGateStateDto.Approved,
            ManualJournalEntryStatusDto.Posted or ManualJournalEntryStatusDto.CloseLocked => ClosePostingGateStateDto.Blocked,
            _ => ClosePostingGateStateDto.Blocked
        };
        return new ClosePostingGateDto(
            GateId(context, ledgerPeriodId),
            GateLabel,
            state,
            false,
            preview.Projection.NetIncome,
            preview.Projection.Lines.Count,
            state == ClosePostingGateStateDto.Blocked
                ? "A matching closing batch is retained, but temporary balances remain non-zero; investigate ledger replay before lock."
                : $"Closing-entry draft is {matching.Status}; independent approval and posting are required before lock.",
            matching.JournalEntryId,
            matching.Status,
            key,
            ToBalances(preview),
            matching.EvidenceLinks,
            closingBatches);
    }

    private static IReadOnlyList<ClosePostingBalanceDto> ToBalances(PeriodCloseDraftPreview preview)
        => preview.Projection.Lines
            .Select(static line => new ClosePostingBalanceDto(
                line.Account.Name,
                line.Account.AccountType.ToString(),
                line.PeriodBalance,
                line.Account.Symbol,
                line.Account.FinancialAccountId,
                LedgerDimensionMapper.ToDto(line.Dimensions)))
            .ToArray();

    private static RunPeriodCloseDraftIntakeRequest ToIntakeRequest(
        AccountingClosePostingContext context,
        Guid ledgerPeriodId,
        string actor)
        => new(
            context.FundProfileId,
            context.Currency,
            actor,
            ledgerPeriodId,
            context.LedgerBookId);

    private static ClosePostingGateDto Blocked(AccountingClosePostingContext context, string detail)
        => new(
            GateId(context, null),
            GateLabel,
            ClosePostingGateStateDto.Blocked,
            false,
            0m,
            0,
            detail);

    private static string GateId(AccountingClosePostingContext context, Guid? ledgerPeriodId)
        => $"period-close-posting:{context.LedgerBookId:N}:{ledgerPeriodId?.ToString("N") ?? context.PeriodId.Trim()}";

    private async Task<LedgerPeriodDto> ResolvePeriodAsync(
        AccountingClosePostingContext context,
        CancellationToken ct)
    {
        var periods = await _ledgerBookService
            .ListPeriodsAsync(new LedgerPeriodQuery(LedgerBookId: context.LedgerBookId), ct)
            .ConfigureAwait(false);
        var hasGuid = Guid.TryParse(context.PeriodId, out var requestedId);
        return periods.FirstOrDefault(period =>
                   (hasGuid && period.PeriodId == requestedId) ||
                   string.Equals(period.Label, context.PeriodId, StringComparison.OrdinalIgnoreCase))
               ?? throw new InvalidOperationException(
                   $"Ledger period '{context.PeriodId}' was not found in book '{context.LedgerBookId:D}'.");
    }

    private static void ValidateContext(AccountingClosePostingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(context.FundProfileId);
        ArgumentException.ThrowIfNullOrWhiteSpace(context.Currency);
        if (context.LedgerBookId == Guid.Empty || string.IsNullOrWhiteSpace(context.PeriodId))
        {
            throw new ArgumentException("Period-close posting context requires ledger book and period ids.", nameof(context));
        }
    }

    private static void ValidateHumanCommand(
        AccountingClosePostingCommand command,
        bool requireController)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.ActionOrigin != OperationsActionOriginDto.HumanOperator)
        {
            throw new InvalidOperationException(
                "Reviewed automation cannot queue closing entries or reversals; a human operator must perform the close action.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(command.Actor);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.Reason);
        if (command.EvidenceLinks.Count == 0)
        {
            throw new InvalidOperationException("Closing-entry commands require retained evidence.");
        }

        if (requireController)
        {
            if (!string.Equals(command.Role, "Controller", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(command.Role, "Fund Controller", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Closing-entry reversal requires Controller or Fund Controller authority.");
            }

            ArgumentException.ThrowIfNullOrWhiteSpace(command.ApprovalReference);
        }
    }
}
