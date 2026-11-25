using Meridian.Contracts.Ledger;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Ledger;

namespace Meridian.Storage.Ledger;

/// <summary>
/// Pure (pre-lock) rules for an <see cref="AtomicTaxLotMutationKind.AccountRekey"/> batch: the
/// governed reclass that moves a legacy ticker-keyed per-security account's balance and every one of
/// its open tax lots onto the SecurityId-keyed account (Security Master review F2). The store checks
/// the locked lots, the complete open-lot population and the legacy balance under lock.
/// </summary>
public static class LedgerAccountRekeyRules
{
    /// <summary>Journal metadata tag carrying the reviewed legacy account, for reconciliation readers.</summary>
    public const string JournalTag = "ledger.securityKeyRekey";

    /// <summary>Retained-evidence subject type for the mapping-report row that authorizes a re-key.</summary>
    public const string EvidenceSubjectType = "LedgerSecurityKeyMapping";

    /// <summary>Validates the instruction, governance and exact two-line reclass journal of <paramref name="command"/>.</summary>
    public static void Validate(AtomicTaxLotJournalCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        var instruction = command.AccountRekey
            ?? throw new LedgerValidationException("Account re-key posting requires the reviewed re-key instruction.");
        ValidateInstruction(instruction, command.Direction);

        if (command.AcquisitionLot is not null || command.DisposalSelections is { Count: > 0 }
            || command.Amortization is not null || command.CorporateAction is not null || command.CapitalReturn is not null
            || command.FactorPaydown is not null || command.ReliefMethod is not null || command.PolicyRevision is not null
            || command.DisposalSalePrice is not null || command.ReferenceVersions is not null
            || command.CorrectsMutationBatchId is not null || command.Journal.SourceJournalEntryId is not null
            || command.Journal.PostingCommand?.SourceJournalEntryId is not null)
            throw new LedgerValidationException("Account re-key cannot combine other lot mutations or correction instructions.");

        ValidateGovernance(command.Journal);

        var metadata = command.Journal.Entry.Metadata;
        if (metadata.SecurityId != instruction.TargetAccount.SecurityId)
            throw new LedgerValidationException("Account re-key journal metadata must identify the target SecurityId.");
        if (metadata.Tags?.GetValueOrDefault(JournalTag) != instruction.LegacyAccount.ToString())
            throw new LedgerValidationException("Account re-key journal must tag the exact legacy account it reclasses.");
        if (!command.RetainedEvidence.Any(static evidence => evidence.SubjectType == EvidenceSubjectType))
            throw new LedgerValidationException("Account re-key requires the retained Security Master mapping evidence.");

        ValidateJournalShape(command.Journal.Entry, instruction);
    }

    /// <summary>The legacy/target identity, direction and lot-population rules.</summary>
    public static void ValidateInstruction(LedgerAccountRekeyInstruction instruction, OpenLotDirection direction)
    {
        ArgumentNullException.ThrowIfNull(instruction);
        ArgumentNullException.ThrowIfNull(instruction.LegacyAccount);
        ArgumentNullException.ThrowIfNull(instruction.TargetAccount);
        ArgumentNullException.ThrowIfNull(instruction.Lots);
        var legacy = instruction.LegacyAccount;
        var target = instruction.TargetAccount;
        if (legacy.SecurityId is not null || target.SecurityId is not { } securityId || securityId == Guid.Empty
            || !LedgerAccounts.IsSecurityKeyedAccount(legacy)
            || !LedgerSecurityKeyMigration.IsRetainedEquivalent(target, legacy))
            throw new LedgerValidationException(
                "Account re-key must move a legacy ticker-keyed per-security account onto the same account keyed by SecurityId.");
        if (!OpenLotCarryingAccounts.Matches(legacy, direction))
            throw new LedgerValidationException("Account re-key direction must match the carrying account (long: asset; short: liability).");
        if (instruction.LegacyBalance <= 0m || decimal.Round(instruction.LegacyBalance, 10) != instruction.LegacyBalance)
            throw new LedgerValidationException("Account re-key moves a positive legacy balance at durable precision.");
        if (instruction.Lots.Count == 0
            || instruction.Lots.Select(static lot => lot.TaxLotRecordId).Distinct().Count() != instruction.Lots.Count
            || instruction.Lots.Any(static lot => lot.TaxLotRecordId == Guid.Empty || string.IsNullOrWhiteSpace(lot.LotId)
                || lot.ExpectedVersion < 0 || lot.ExpectedOpenQuantity <= 0m || lot.ExpectedCostBasis < 0m
                || lot.BookPositionId == Guid.Empty))
            throw new LedgerValidationException("Account re-key must list each open lot once with its reviewed version, quantity, basis and book position.");
        if (instruction.Lots.Sum(static lot => lot.ExpectedCostBasis) != instruction.LegacyBalance)
            throw new LedgerValidationException(
                "Account re-key lot basis must equal the legacy balance; a balance without lots, or lots without balance, needs its own correction first.");
    }

    /// <summary>
    /// Requires an approved Adjustment posting with complete approval metadata. A re-key is never
    /// auto-approved: the approval must be supplied by the governed review that accepted the mapping.
    /// </summary>
    public static void ValidateGovernance(LedgerJournalEntryWrite journal)
    {
        ArgumentNullException.ThrowIfNull(journal);
        var approval = journal.AdjustmentApproval;
        if (journal.PostingKind != LedgerPostingKindDto.Adjustment
            || (journal.PostingCommand is { } posting && posting.ApprovalState != AccountingPostingApprovalStateDto.Approved)
            || approval is null || approval.Status != LedgerAdjustmentApprovalStatusDto.Approved
            || string.IsNullOrWhiteSpace(approval.ApprovalId) || string.IsNullOrWhiteSpace(approval.ApprovedBy)
            || string.IsNullOrWhiteSpace(approval.ReasonCode) || approval.ApprovedAt == default
            || approval.ApprovedAt.Offset != TimeSpan.Zero || approval.ApprovedAt > journal.Entry.Timestamp
            || (string.IsNullOrWhiteSpace(approval.GovernanceCaseId) && string.IsNullOrWhiteSpace(approval.EvidenceLink)))
            throw new LedgerValidationException("Account re-key requires complete approved adjustment metadata before posting.");
    }

    /// <summary>
    /// The open basis each book position moves: the reclass journal carries one debit and one credit
    /// per position with non-zero basis, so position-level ledger views stay whole after the re-key.
    /// </summary>
    public static IReadOnlyList<(Guid BookPositionId, decimal Amount)> PositionAmounts(LedgerAccountRekeyInstruction instruction)
    {
        ArgumentNullException.ThrowIfNull(instruction);
        return instruction.Lots
            .GroupBy(static lot => lot.BookPositionId)
            .Select(static group => (group.Key, group.Sum(static lot => lot.ExpectedCostBasis)))
            .Where(static position => position.Item2 != 0m)
            .OrderBy(static position => position.Key)
            .ToArray();
    }

    /// <summary>
    /// The journal is exactly the balanced reclass of the legacy balance onto the target: for every
    /// book position, one debit and one credit of that position's lot basis, dimensioned by the target
    /// SecurityId and the position. A single-position account is one debit and one credit.
    /// </summary>
    public static void ValidateJournalShape(JournalEntry entry, LedgerAccountRekeyInstruction instruction)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(instruction);
        var securityId = instruction.TargetAccount.SecurityId;
        var debitNormal = instruction.LegacyAccount.AccountType is LedgerAccountType.Asset or LedgerAccountType.Expense;
        var (debited, credited) = debitNormal
            ? (instruction.TargetAccount, instruction.LegacyAccount)
            : (instruction.LegacyAccount, instruction.TargetAccount);
        var lines = entry.Lines;
        var positions = PositionAmounts(instruction);
        bool InPosition(LedgerEntry line, Guid positionId)
            => line.Dimensions?.InstrumentId == securityId && line.Dimensions?.PositionId == positionId;
        if (lines.Count != positions.Count * 2
            || positions.Sum(static position => position.Amount) != instruction.LegacyBalance
            || positions.Any(position =>
                lines.Count(line => line.Account == debited && line.Debit == position.Amount && line.Credit == 0m
                    && InPosition(line, position.BookPositionId)) != 1
                || lines.Count(line => line.Account == credited && line.Credit == position.Amount && line.Debit == 0m
                    && InPosition(line, position.BookPositionId)) != 1))
            throw new LedgerValidationException(
                "Account re-key journal must be exactly one debit and one credit per book position, together moving the whole legacy balance onto the SecurityId-keyed account.");
        if (lines.Any(line => line.Currency is { } currency
                && (!currency.IsFunctionalCurrency || currency.TransactionDebit != line.Debit || currency.TransactionCredit != line.Credit)))
            throw new LedgerValidationException("Account re-key moves functional-currency carrying basis only.");
    }

    /// <summary>The open functional basis a lot carries, as the re-key moves it.</summary>
    public static decimal OpenBasis(LedgerTaxLotRecord lot)
    {
        ArgumentNullException.ThrowIfNull(lot);
        return lot.Acquisition is null ? lot.OpenQuantity * lot.UnitCost : lot.ToOpenLot().OpenFunctionalCostBasis;
    }
}
