using System.Globalization;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Ledger;
using Meridian.FinancialOperations.Intercompany;
using Meridian.Storage.Ledger;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Pre-fills an ordinary journal-workbench draft that corrects one side of a reciprocal balance after its dispute was
/// resolved (DEC-IC-CONFIRM-003). The intercompany leg carries the approved profile's exact agreement, mapping and
/// transaction identities so the posting authority accepts it and the balance schedule attributes it to the pair; the
/// offset is an operator-chosen non-intercompany account. Nothing is submitted, approved or posted here: the draft
/// enters the side's ordinary maker-checker review, and once it posts the balances are proposed again.
/// </summary>
public sealed class IntercompanyConfirmationAdjustmentDraftService(IntercompanyBalanceConfirmationService confirmations,
    ILedgerJournalStore journals, IIntercompanyAccountBindingStore bindings, IAccountingConfigurationService configuration,
    IManualJournalEntryDraftStore drafts, ManualJournalEntryWorkbenchService workbench, TimeProvider? time = null)
{
    /// <summary>The retry-stable idempotency prefix that marks a draft as prepared from a resolved confirmation dispute.</summary>
    public const string SourcePrefix = "intercompany-confirmation-adjustment:";

    private readonly TimeProvider _clock = time ?? TimeProvider.System;

    public async Task<ManualJournalEntryDraftDto> CreateDraftAsync(string tenantId, string? companyId, string actor, Guid confirmationId,
        CreateIntercompanyConfirmationAdjustmentDraftRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(actor))
            throw new UnauthorizedAccessException("An authenticated preparer is required.");
        if (request.RequestId == Guid.Empty)
            throw new ArgumentException("An adjustment request identity is required.");
        var current = await confirmations.GetAsync(tenantId, companyId, confirmationId, ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("The balance confirmation is unavailable in this scope.");
        var side = request.Party == IntercompanyBalanceConfirmationPartyDto.PartyOne ? current.PartyOne : current.PartyTwo;
        var counterparty = request.Party == IntercompanyBalanceConfirmationPartyDto.PartyOne ? current.PartyTwo.EntityId : current.PartyOne.EntityId;
        // Only the side's own company prepares a correction of that side's book.
        var book = await confirmations.RequireBookAsync(tenantId, companyId, side.LedgerBookId, ct).ConfigureAwait(false);
        if (current.Status != IntercompanyBalanceConfirmationStatusDto.Resolved)
            throw new InvalidOperationException("An adjustment is drafted only after the dispute is resolved.");
        if (request.ExpectedVersion != current.Version
            || !string.Equals(request.ExpectedBalanceDigest, current.BalanceDigest, StringComparison.Ordinal))
            throw new InvalidOperationException("The confirmation changed since it was shown. Reload it before drafting an adjustment.");
        if (request.AdjustmentAmount == 0m)
            throw new ArgumentException("State a nonzero adjustment amount.");
        if (request.Role is not (IntercompanyAccountRoleDto.Receivable or IntercompanyAccountRoleDto.Payable))
            throw new ArgumentException("An adjustment corrects the side's intercompany receivable or payable.");
        if (string.IsNullOrWhiteSpace(request.OffsetAccountNodeId))
            throw new ArgumentException("Choose the offset account.");

        var resolution = current.Events.Last(item => item.Action == IntercompanyBalanceConfirmationActionDto.Resolve);
        var dispute = current.Events.Last(item => item.Action == IntercompanyBalanceConfirmationActionDto.Dispute);
        var draftId = DraftId(tenantId, current.ConfirmationId, request.Party, resolution.EventId);
        var existing = await drafts.GetAsync(book.FundProfileId, draftId, ct, tenantId, companyId).ConfigureAwait(false);
        if (existing is not null)
        {
            if (existing.LedgerBookId != book.LedgerBookId || existing.TreasuryContext?.IdempotencyKey?.StartsWith(SourcePrefix, StringComparison.Ordinal) != true)
                throw new InvalidOperationException("The deterministic adjustment identity already retains a different journal.");
            // One adjustment per side per resolution; reviewer edits, decisions and posting are never overwritten.
            return existing;
        }

        var profile = await confirmations.EffectiveProfileAsync(tenantId, current.AccountingArrangementId, request.AccountingDate, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"No approved accounting profile revision is in effect on {request.AccountingDate:yyyy-MM-dd}.");
        if (profile.Currency != book.BaseCurrency)
            throw new InvalidOperationException("A cross-currency adjustment needs the governed FX authority; prepare it in the journal workbench.");
        var mapping = profile.PartyMappings.SingleOrDefault(item => item.EntityId == side.EntityId && item.LedgerBookId == book.LedgerBookId)
            ?? throw new InvalidOperationException("The approved profile does not map this side's book.");
        var periods = (await journals.ListPeriodsAsync(book.LedgerBookId, ct: ct).ConfigureAwait(false))
            .Where(item => item.Status == "Open" && item.StartDate <= request.AccountingDate && request.AccountingDate <= item.EndDate).ToArray();
        if (periods.Length != 1)
            throw new InvalidOperationException("Exactly one open period of this side's book must contain the accounting date; no date is moved automatically.");

        var workspace = await configuration.GetWorkspaceAsync(book.FundProfileId, book.LedgerBookId, ct, tenantId, companyId).ConfigureAwait(false);
        var node = request.Role == IntercompanyAccountRoleDto.Receivable ? mapping.ReceivableAccountNodeId : mapping.PayableAccountNodeId;
        var account = workspace.ChartOfAccounts.SingleOrDefault(item => item.NodeId == node && !item.IsArchived)
            ?? throw new InvalidOperationException("The mapped intercompany account is missing or archived.");
        var offset = workspace.ChartOfAccounts.SingleOrDefault(item => item.NodeId == request.OffsetAccountNodeId && !item.IsArchived)
            ?? throw new InvalidOperationException("The offset account is missing or archived.");
        if (offset.IntercompanyRole is not null || offset.NodeId == account.NodeId)
            throw new InvalidOperationException("The offset must be a non-intercompany account; the counterpart side corrects its own book.");
        var binding = (await bindings.ListAsync(book.LedgerBookId, request.AccountingDate, ct: ct).ConfigureAwait(false))
            .SingleOrDefault(item => item.ChartAccountNodeId == account.NodeId && item.Role == request.Role && !item.IsArchived)
            ?? throw new InvalidOperationException("An exact account-role binding for the mapped intercompany account is required.");

        var amount = Math.Abs(request.AdjustmentAmount);
        var debitIntercompany = request.AdjustmentAmount > 0m;
        var transaction = TransactionId(current.ConfirmationId, request.Party, resolution.EventId);
        var original = new ManualJournalLineCurrencyDto(profile.Currency, amount, 1m);
        var lines = new[]
        {
            new ManualJournalEntryLineDto("intercompany", debitIntercompany ? AccountingTemplateLineSideDto.Debit : AccountingTemplateLineSideDto.Credit,
                amount, book.BaseCurrency, account.Path, EntityId: side.EntityId.ToString("D"),
                Dimensions: new(EntityId: side.EntityId.ToString("D"), CounterpartyId: counterparty.ToString("D"),
                    FundId: book.FundProfileId, BookId: book.LedgerBookId.ToString("D"))
                {
                    ChartAccountNodeId = account.NodeId,
                    IntercompanyAgreementId = profile.CanonicalAgreementId,
                    IntercompanyAgreementVersion = profile.CanonicalAgreementVersion,
                    AccountingArrangementId = profile.ArrangementId.ToString("D"),
                    AccountingArrangementRevisionId = profile.RevisionId.ToString("D"),
                    AccountBindingRevisionId = binding.RevisionId.ToString("D"),
                    IntercompanyTransactionId = transaction
                }) { TransactionCurrency = original },
            new ManualJournalEntryLineDto("offset", debitIntercompany ? AccountingTemplateLineSideDto.Credit : AccountingTemplateLineSideDto.Debit,
                amount, book.BaseCurrency, offset.Path, EntityId: side.EntityId.ToString("D"),
                Dimensions: new(EntityId: side.EntityId.ToString("D"), FundId: book.FundProfileId, BookId: book.LedgerBookId.ToString("D"))
                { ChartAccountNodeId = offset.NodeId })
        };
        var memo = string.IsNullOrWhiteSpace(request.Memo)
            ? FormattableString.Invariant($"Intercompany balance adjustment after resolved dispute, as of {current.AsOf:yyyy-MM-dd}: {resolution.Reason}")
            : request.Memo.Trim();
        var now = _clock.GetUtcNow();
        var links = new[]
        {
            $"intercompany-balance-confirmation:{current.ConfirmationId:D}/version:{current.Version.ToString(CultureInfo.InvariantCulture)}",
            $"intercompany-balance-confirmation:{current.ConfirmationId:D}/dispute:{dispute.EventId:D}",
            $"intercompany-balance-confirmation:{current.ConfirmationId:D}/resolution:{resolution.EventId:D}",
            $"accounting-arrangement:{profile.ArrangementId:D}/revision:{profile.RevisionId:D}",
            $"intercompany-agreement:{profile.CanonicalAgreementId:D}/version:{profile.CanonicalAgreementVersion}"
        };
        var draft = new ManualJournalEntryDraftDto(draftId, ManualJournalEntryStatusDto.Draft, book.FundProfileId, book.LedgerBookId,
            book.AccountingBasis, request.AccountingDate, periods[0].PeriodId.ToString("D"), side.EntityId.ToString("D"), null,
            book.BaseCurrency, memo, actor, now, now, 1, lines, links, [],
            TreasuryContext: new(request.AccountingDate, SourcePrefix + draftId.ToString("D")),
            TenantId: tenantId, CompanyId: companyId);
        return await workbench.SaveAutomatedDraftAsync(new(draft, actor, SourcePrefix + draftId.ToString("D"),
            LedgerBookId: book.LedgerBookId, TenantId: tenantId, CompanyId: companyId), ct).ConfigureAwait(false);
    }

    /// <summary>One adjustment identity per confirmation, side and resolution.</summary>
    public static Guid DraftId(string tenantId, Guid confirmationId, IntercompanyBalanceConfirmationPartyDto party, Guid resolutionEventId)
        => new(Sha256Digest.ComputeBytesUtf8(string.Join(':', "intercompany-confirmation-adjustment-v1", tenantId,
            confirmationId.ToString("D"), party.ToString(), resolutionEventId.ToString("D"))).AsSpan(0, 16));

    /// <summary>The intercompany transaction identity the adjustment's intercompany leg carries.</summary>
    public static string TransactionId(Guid confirmationId, IntercompanyBalanceConfirmationPartyDto party, Guid resolutionEventId)
        => FormattableString.Invariant($"{SourcePrefix}{confirmationId:N}:{(party == IntercompanyBalanceConfirmationPartyDto.PartyOne ? 1 : 2)}:{resolutionEventId:N}");
}
