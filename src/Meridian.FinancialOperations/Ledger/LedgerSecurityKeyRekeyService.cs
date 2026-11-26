using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.SecurityMaster;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using static Meridian.Contracts.Text.TextPrimitives;

namespace Meridian.FinancialOperations.Ledger;

/// <summary>
/// One reviewable re-key of a legacy ticker-keyed per-security account (Security Master review F2):
/// the balance reclass and every open tax lot of the account, moved together onto the
/// SecurityId-keyed account by one atomic batch once approved.
/// </summary>
public sealed record LedgerSecurityKeyRekeyProposal(
    Guid LedgerBookId,
    Guid PeriodId,
    long ExpectedPeriodVersion,
    DateTimeOffset EffectiveAt,
    IReadOnlyList<Guid> BookPositionIds,
    LedgerSecurityKeyMappingRow Mapping,
    LedgerAccountRekeyInstruction Instruction,
    Guid MutationBatchId,
    Guid JournalEntryId,
    Guid SourceEventId,
    string IdempotencyKey);

/// <summary>A legacy account the re-key cannot move automatically, with the reason.</summary>
public sealed record LedgerSecurityKeyRekeyBlocked(LedgerSecurityKeyMappingRow Mapping, string Reason);

/// <summary>
/// A legacy account whose open lots predate canonical lot identity: they carry no Security Master or
/// book-position identity (the two are recorded together), so nothing proves they belong to the
/// resolved security. Attribute each listed lot through the governed open-lot backfill (retained
/// acquisition evidence, independent review, human-operator apply), then plan again; the account then
/// re-keys like any other, across however many book positions the backfill assigns.
/// </summary>
public sealed record LedgerSecurityKeyRekeyBackfillRequirement(
    LedgerSecurityKeyMappingRow Mapping,
    IReadOnlyList<Guid> TaxLotRecordIds,
    string Reason);

/// <summary>
/// The re-key plan for one ledger book. <see cref="BalanceOnly"/> rows carry no tax lots (for example
/// a dividend receivable) and move with an ordinary governed reclass from
/// <see cref="LedgerSecurityKeyMigration.ProposeReclass"/>.
/// </summary>
public sealed record LedgerSecurityKeyRekeyPlan(
    LedgerSecurityKeyMappingReport Report,
    IReadOnlyList<LedgerSecurityKeyRekeyProposal> Proposals,
    IReadOnlyList<LedgerSecurityKeyMappingRow> BalanceOnly,
    IReadOnlyList<LedgerSecurityKeyRekeyBlocked> Blocked,
    IReadOnlyList<LedgerSecurityKeyRekeyBackfillRequirement> AwaitingBackfill);

/// <summary>
/// The approval and Security Master lineage a reviewer attaches to a re-key proposal. Nothing is ever
/// auto-approved: <see cref="Approval"/> must be the approved adjustment the governed review issued.
/// </summary>
/// <param name="Approval">Approved adjustment metadata (status Approved, approver, time, reason, case or evidence).</param>
/// <param name="SecurityMasterProvenance">Security Master provenance tag for the target security (active, approved).</param>
/// <param name="SecurityMasterLineage">Security Master lineage tag, including its ledger mapping for the ticker and SecurityId.</param>
/// <param name="MappingEvidence">Retained evidence identity of the reviewed mapping-report row.</param>
/// <param name="PostingCommand">The governed, approved posting command when the store requires one.</param>
public sealed record LedgerSecurityKeyRekeyApproval(
    LedgerAdjustmentApprovalMetadataDto Approval,
    string SecurityMasterProvenance,
    string SecurityMasterLineage,
    RetainedEvidenceIdentityDto MappingEvidence,
    AccountingPostingCommandDto? PostingCommand = null);

/// <summary>
/// Plans and executes the Security Master review F2 re-key of legacy ticker-keyed per-security
/// accounts: the mapping report comes from the canonical (provider-less) resolver; each lot-carrying
/// legacy account becomes one <see cref="AtomicTaxLotMutationKind.AccountRekey"/> proposal that a
/// reviewer approves before <see cref="ExecuteAsync"/> posts it atomically.
/// </summary>
public sealed class LedgerSecurityKeyRekeyService
{
    /// <summary>Journal activity type of a re-key reclass.</summary>
    public const string ActivityType = "security-key-rekey";

    private readonly ILedgerJournalStore _store;
    private readonly IAtomicTaxLotJournalStore _atomicStore;
    private readonly ISecurityResolver _resolver;

    public LedgerSecurityKeyRekeyService(ILedgerJournalStore store, IAtomicTaxLotJournalStore atomicStore, ISecurityResolver resolver)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _atomicStore = atomicStore ?? throw new ArgumentNullException(nameof(atomicStore));
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
    }

    /// <summary>Builds the mapping report and one re-key proposal per lot-carrying legacy account.</summary>
    public async Task<LedgerSecurityKeyRekeyPlan> PlanAsync(
        Guid ledgerBookId,
        Guid periodId,
        DateTimeOffset effectiveAt,
        CancellationToken ct = default)
    {
        var period = await _store.GetPeriodAsync(periodId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The re-key accounting period does not exist.");
        if (period.LedgerBookId != ledgerBookId)
            throw new InvalidOperationException("The re-key accounting period belongs to another ledger book.");
        var effectiveDate = DateOnly.FromDateTime(effectiveAt.UtcDateTime);
        if (effectiveDate < period.StartDate || effectiveDate > period.EndDate)
            throw new InvalidOperationException("The re-key effective instant must fall inside the selected accounting period.");

        var balances = await _store.ReadTrialBalanceAsOfAsync(ledgerBookId, effectiveAt, ct: ct).ConfigureAwait(false);
        var report = await LedgerSecurityKeyMigration.BuildReportAsync(balances, _resolver, effectiveAt, ct).ConfigureAwait(false);

        var proposals = new List<LedgerSecurityKeyRekeyProposal>();
        var balanceOnly = new List<LedgerSecurityKeyMappingRow>();
        var blocked = new List<LedgerSecurityKeyRekeyBlocked>();
        var awaitingBackfill = new List<LedgerSecurityKeyRekeyBackfillRequirement>();
        foreach (var row in report.ReclassCandidates)
        {
            var lots = await _store.ListOpenTaxLotsAsync(ledgerBookId, row.LegacyAccount, ct).ConfigureAwait(false);
            if (lots.Count == 0)
            {
                balanceOnly.Add(row);
                continue;
            }

            if (FindLotsAwaitingBackfill(row, lots) is { } requirement)
            {
                awaitingBackfill.Add(requirement);
                continue;
            }

            // Labels are unique per account key across open and closed lots. Read which of the lots'
            // labels, and their relabel candidates, are already taken on the SecurityId-keyed account.
            IReadOnlySet<string> occupied = new HashSet<string>(StringComparer.Ordinal);
            if (row.TargetAccount is { } target)
            {
                var candidates = lots.Select(static lot => lot.LotId)
                    .Concat(lots.SelectMany(static lot => Enumerable.Range(1, LedgerAccountRekeyRules.MaxRelabelAttempts)
                        .Select(attempt => LedgerAccountRekeyRules.RelabelCandidate(lot.LotId, attempt))))
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                occupied = (await _store.GetTaxLotsByLotIdsAsync(ledgerBookId, target, candidates, ct).ConfigureAwait(false))
                    .Select(static lot => lot.LotId)
                    .ToHashSet(StringComparer.Ordinal);
            }

            var proposal = TryPropose(ledgerBookId, period, effectiveAt, row, lots, out var reason, occupied);
            if (proposal is null)
                blocked.Add(new LedgerSecurityKeyRekeyBlocked(row, reason!));
            else
                proposals.Add(proposal);
        }

        return new LedgerSecurityKeyRekeyPlan(report, proposals, balanceOnly, blocked, awaitingBackfill);
    }

    /// <summary>
    /// Returns the backfill requirement for a resolved legacy account whose open lots lack canonical
    /// Security Master and book-position identity, or <see langword="null"/> when every open lot has it.
    /// </summary>
    public static LedgerSecurityKeyRekeyBackfillRequirement? FindLotsAwaitingBackfill(
        LedgerSecurityKeyMappingRow row,
        IReadOnlyList<LedgerTaxLotRecord> openLots)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(openLots);
        if (row.TargetAccount?.SecurityId is null)
            return null;

        var unattributed = openLots
            .Where(static lot => lot.OpenQuantity > 0m && (lot.SecurityId == Guid.Empty || lot.BookPositionId == Guid.Empty))
            .Select(static lot => lot.TaxLotRecordId)
            .Order()
            .ToArray();
        return unattributed.Length == 0
            ? null
            : new LedgerSecurityKeyRekeyBackfillRequirement(row, unattributed,
                $"{unattributed.Length} open lot(s) on {row.LegacyAccount} predate canonical lot identity; attribute them to Security Master {row.TargetAccount.SecurityId:D} and a book position through the governed open-lot backfill, then plan the re-key again.");
    }

    /// <summary>
    /// Builds the per-account proposal for one mapping row, or <see langword="null"/> with the reason
    /// the store would refuse it.
    /// </summary>
    public static LedgerSecurityKeyRekeyProposal? TryPropose(
        Guid ledgerBookId,
        LedgerAccountingPeriod period,
        DateTimeOffset effectiveAt,
        LedgerSecurityKeyMappingRow row,
        IReadOnlyList<LedgerTaxLotRecord> openLots,
        out string? reason,
        IReadOnlySet<string>? occupiedTargetLabels = null)
    {
        ArgumentNullException.ThrowIfNull(period);
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(openLots);
        reason = null;
        if (row.TargetAccount?.SecurityId is not { } securityId)
        {
            reason = $"Ticker '{row.LegacyAccount.Symbol}' is {row.Resolution.Status}; it is never re-keyed automatically.";
            return null;
        }

        var direction = row.LegacyAccount.AccountType == LedgerAccountType.Liability ? OpenLotDirection.Short : OpenLotDirection.Long;
        var open = openLots.Where(static lot => lot.OpenQuantity > 0m).OrderBy(static lot => lot.TaxLotRecordId).ToArray();
        if (open.Any(lot => lot.Account != row.LegacyAccount || lot.LedgerBookId != ledgerBookId))
        {
            reason = "Open lots do not all belong to the legacy account in this ledger book.";
            return null;
        }

        // Lots without canonical identity are routed to the governed open-lot backfill first.
        if (FindLotsAwaitingBackfill(row, open) is { } requirement)
        {
            reason = requirement.Reason;
            return null;
        }

        if (open.Any(lot => lot.SecurityId != securityId))
        {
            reason = "Open lots on the legacy account carry a different instrument than the resolved SecurityId.";
            return null;
        }

        // A holding may sit in several book positions of the same security; the re-key moves them
        // all in one batch (one reclass pair per position).

        var positions = open.Select(static lot => lot.BookPositionId).Distinct().Order().ToArray();

        // A label already taken on the SecurityId-keyed account moves to its first free relabel form
        // ({label}~rekey, {label}~rekey2, ...), recorded in the instruction for review.
        var used = new HashSet<string>(occupiedTargetLabels ?? new HashSet<string>(), StringComparer.Ordinal);
        var targetLabels = new string?[open.Length];
        for (var index = 0; index < open.Length; index++)
        {
            var label = open[index].LotId;
            if (used.Add(label))
                continue;
            var relabel = Enumerable.Range(1, LedgerAccountRekeyRules.MaxRelabelAttempts)
                .Select(attempt => LedgerAccountRekeyRules.RelabelCandidate(label, attempt))
                .FirstOrDefault(candidate => !used.Contains(candidate));
            if (relabel is null)
            {
                reason = $"Lot label '{label}' and its {LedgerAccountRekeyRules.MaxRelabelAttempts} relabel forms are all taken on the SecurityId-keyed account; relabel it first.";
                return null;
            }

            used.Add(relabel);
            targetLabels[index] = relabel;
        }

        LedgerAccountRekeyLot[] lots;
        try
        {
            lots = open.Select((lot, index) => new LedgerAccountRekeyLot(lot.TaxLotRecordId, lot.LotId, lot.Version, lot.OpenQuantity,
                LedgerAccountRekeyRules.OpenBasis(lot), lot.BookPositionId, targetLabels[index])).ToArray();
            var instruction = new LedgerAccountRekeyInstruction(row.LegacyAccount, row.TargetAccount, row.Balance, lots);
            LedgerAccountRekeyRules.ValidateInstruction(instruction, direction);
            var key = $"security-key-rekey:{ledgerBookId:D}:{row.LegacyAccount}:{securityId:D}";
            return new LedgerSecurityKeyRekeyProposal(
                ledgerBookId,
                period.PeriodId,
                period.Version,
                effectiveAt,
                positions,
                row,
                instruction,
                MutationBatchId: DeterministicGuid(key, "batch"),
                JournalEntryId: DeterministicGuid(key, "journal"),
                SourceEventId: DeterministicGuid(key, "source"),
                IdempotencyKey: key);
        }
        catch (LedgerValidationException ex)
        {
            reason = ex.Message;
            return null;
        }
    }

    /// <summary>Builds the approved atomic command for a proposal (pure; no store access).</summary>
    public static AtomicTaxLotJournalCommand BuildCommand(
        LedgerSecurityKeyRekeyProposal proposal,
        LedgerSecurityKeyRekeyApproval approval,
        LedgerBookRecord book)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        ArgumentNullException.ThrowIfNull(approval);
        ArgumentNullException.ThrowIfNull(approval.Approval);
        ArgumentNullException.ThrowIfNull(approval.MappingEvidence);
        ArgumentNullException.ThrowIfNull(book);
        if (book.LedgerBookId != proposal.LedgerBookId)
            throw new InvalidOperationException("The re-key proposal belongs to another ledger book.");
        if (approval.Approval.Status != LedgerAdjustmentApprovalStatusDto.Approved)
            throw new InvalidOperationException("A re-key posts only with an approved adjustment; it is never auto-approved.");
        if (!string.Equals(approval.MappingEvidence.SubjectType, LedgerAccountRekeyRules.EvidenceSubjectType, StringComparison.Ordinal))
            throw new InvalidOperationException($"Re-key evidence must be a retained '{LedgerAccountRekeyRules.EvidenceSubjectType}' record.");

        var instruction = proposal.Instruction;
        var securityId = instruction.TargetAccount.SecurityId!.Value;
        var debitNormal = instruction.LegacyAccount.AccountType is LedgerAccountType.Asset or LedgerAccountType.Expense;
        var (debited, credited) = debitNormal
            ? (instruction.TargetAccount, instruction.LegacyAccount)
            : (instruction.LegacyAccount, instruction.TargetAccount);
        var description = $"Re-key {instruction.LegacyAccount} to Security Master {securityId:D} (Security Master review F2)";
        // One debit and one credit per book position, each moving that position's lot basis, so
        // position-dimensioned views stay whole. The single-position line identities are unchanged.
        var positionAmounts = LedgerAccountRekeyRules.PositionAmounts(instruction);
        var lines = new List<LedgerEntry>(positionAmounts.Count * 2);
        for (var index = 0; index < positionAmounts.Count; index++)
        {
            var (positionId, positionAmount) = positionAmounts[index];
            var dimensions = new LedgerLineDimensionSet(InstrumentId: securityId, BookId: book.LedgerBookId.ToString("D"),
                AccountId: instruction.LegacyAccount.FinancialAccountId)
            { PositionId = positionId };
            var suffix = positionAmounts.Count == 1 ? string.Empty : $":{positionId:N}";
            lines.Add(new LedgerEntry(DeterministicGuid(proposal.IdempotencyKey, $"line:0{suffix}"), proposal.JournalEntryId,
                proposal.EffectiveAt, debited, positionAmount, 0m, description, dimensions,
                new LedgerEntryCurrency(book.BaseCurrency, book.BaseCurrency, positionAmount, 0m, 1m)));
            lines.Add(new LedgerEntry(DeterministicGuid(proposal.IdempotencyKey, $"line:1{suffix}"), proposal.JournalEntryId,
                proposal.EffectiveAt, credited, 0m, positionAmount, description, dimensions,
                new LedgerEntryCurrency(book.BaseCurrency, book.BaseCurrency, 0m, positionAmount, 1m)));
        }
        var tags = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [LedgerAccountRekeyRules.JournalTag] = instruction.LegacyAccount.ToString(),
            ["securityMasterProvenance"] = RequireText(approval.SecurityMasterProvenance, nameof(approval.SecurityMasterProvenance)),
            ["securityMasterLineage"] = RequireText(approval.SecurityMasterLineage, nameof(approval.SecurityMasterLineage))
        };
        var metadata = new JournalEntryMetadata(
            ActivityType: ActivityType,
            Symbol: instruction.LegacyAccount.Symbol,
            SecurityId: securityId,
            FinancialAccountId: instruction.LegacyAccount.FinancialAccountId,
            EffectiveDate: DateOnly.FromDateTime(proposal.EffectiveAt.UtcDateTime),
            IdempotencyKey: proposal.IdempotencyKey,
            Tags: tags);
        var entry = new JournalEntry(proposal.JournalEntryId, proposal.EffectiveAt, description, lines, metadata);
        var write = new LedgerJournalEntryWrite(
            entry,
            AggregateId: book.LedgerBookId,
            PeriodId: proposal.PeriodId,
            CommandId: approval.PostingCommand?.CommandId ?? DeterministicGuid(proposal.IdempotencyKey, "command"),
            AccountingBasis: book.AccountingBasis,
            AccountingPolicyId: book.AccountingPolicyId,
            AccountingPolicyVersion: book.AccountingPolicyVersion,
            SourceEventId: proposal.SourceEventId,
            PostingKind: LedgerPostingKindDto.Adjustment,
            AdjustmentApproval: approval.Approval,
            PostingCommand: approval.PostingCommand,
            LedgerBookId: book.LedgerBookId);

        return AtomicTaxLotJournalCommand.Create(
            proposal.MutationBatchId,
            book.LedgerBookId,
            write,
            proposal.SourceEventId,
            proposal.IdempotencyKey,
            proposal.ExpectedPeriodVersion,
            AtomicTaxLotMutationKind.AccountRekey,
            [approval.MappingEvidence],
            direction: instruction.LegacyAccount.AccountType == LedgerAccountType.Liability ? OpenLotDirection.Short : OpenLotDirection.Long,
            accountRekey: instruction);
    }

    /// <summary>Posts an approved proposal: the reclass journal and the lot re-key commit atomically.</summary>
    public async Task<AtomicTaxLotJournalResult> ExecuteAsync(
        LedgerSecurityKeyRekeyProposal proposal,
        LedgerSecurityKeyRekeyApproval approval,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        var book = await _store.GetLedgerBookAsync(proposal.LedgerBookId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The re-key ledger book does not exist.");
        var command = BuildCommand(proposal, approval, book);
        return await _atomicStore.AppendAssetPostingAsync(command, ct).ConfigureAwait(false);
    }

    private static Guid DeterministicGuid(string key, string purpose)
    {
        var hash = Sha256Digest.ComputeBytesUtf8($"{key}|{purpose}");
        hash[6] = (byte)((hash[6] & 0x0F) | 0x50);
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);
        return new Guid(hash.AsSpan(0, 16));
    }
}
