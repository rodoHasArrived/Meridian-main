using System.Globalization;
using System.Text.Json;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.Integrity;

namespace Meridian.Ledger;

/// <summary>
/// Converts a reviewed <see cref="ShortCarryInstruction"/> into a governed
/// <see cref="AutomatedJournalDraft"/> (kind <see cref="AutomatedJournalEventKind.ShortCarryCharged"/>), so a
/// borrow fee or payment in lieu reaches the ledger only through workbench review, approval and posting.
/// The lines are <see cref="ShortCarryProjector.Lines"/> against the lot's liability carrying account; the
/// caller proves the instruction against the authoritative open short lot first.
/// </summary>
public static class ShortCarryDraftBuilder
{
    public const string ActivityType = "short-carry";
    public const string IdempotencyPrefix = "short-carry|";

    public static AutomatedJournalDraft BuildDraft(
        ShortCarryInstruction instruction,
        LedgerAccount carryingAccount,
        Guid securityId,
        DateTimeOffset preparedAtUtc,
        string preparedBy)
    {
        ArgumentNullException.ThrowIfNull(instruction);
        ArgumentException.ThrowIfNullOrWhiteSpace(preparedBy);
        var lines = ShortCarryProjector.Lines(instruction, carryingAccount);
        var amount = lines.Sum(static line => line.debit);
        // A payment in lieu is paid on the pay date; a borrow fee accrues through the last day of [start, end).
        var effectiveDate = instruction.Kind == ShortCarryKind.PaymentInLieu
            ? instruction.AccrualEnd
            : instruction.AccrualEnd.AddDays(-1);
        var fingerprint = Fingerprint(instruction);
        var idempotencyKey = string.Create(CultureInfo.InvariantCulture,
            $"{IdempotencyPrefix}{instruction.TaxLotRecordId:D}|{instruction.Kind}|{instruction.AccrualStart:yyyy-MM-dd}|{instruction.AccrualEnd:yyyy-MM-dd}|{fingerprint}");
        var kindLabel = instruction.Kind == ShortCarryKind.BorrowFee ? "Stock borrow fee" : "Payment in lieu of dividend";
        var description = string.Create(CultureInfo.InvariantCulture,
            $"{kindLabel} on short lot {instruction.LotId} for {instruction.Quantity} units, {instruction.AccrualStart:yyyy-MM-dd} to {instruction.AccrualEnd:yyyy-MM-dd}");

        var journalEvent = new AutomatedJournalEvent(
            AutomatedJournalEventKind.ShortCarryCharged,
            carryingAccount.Symbol ?? instruction.LotId,
            amount,
            preparedAtUtc,
            FinancialAccountId: carryingAccount.FinancialAccountId,
            Description: description,
            SecurityId: securityId == Guid.Empty ? null : securityId,
            SourceEventId: instruction.SourceEvidenceId.Trim(),
            EffectiveDate: effectiveDate,
            IdempotencyKey: idempotencyKey);

        var tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["shortCarry.taxLotRecordId"] = instruction.TaxLotRecordId.ToString("D"),
            ["shortCarry.lotId"] = instruction.LotId,
            ["shortCarry.kind"] = instruction.Kind.ToString(),
            ["shortCarry.accrualStart"] = instruction.AccrualStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["shortCarry.accrualEnd"] = instruction.AccrualEnd.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["shortCarry.quantity"] = instruction.Quantity.ToString(CultureInfo.InvariantCulture),
            ["shortCarry.sourceEvidenceId"] = instruction.SourceEvidenceId.Trim(),
            ["shortCarry.modelVersion"] = instruction.ModelVersion,
            ["shortCarry.instructionFingerprint"] = fingerprint,
            ["shortCarry.preparedBy"] = preparedBy.Trim()
        };

        var metadata = new JournalEntryMetadata(
            ActivityType: ActivityType,
            EffectiveDate: effectiveDate,
            IdempotencyKey: idempotencyKey,
            Tags: tags);

        return new AutomatedJournalDraft(
            journalEvent,
            description,
            lines.Select(static line => (line.account, line.debit, line.credit, (LedgerLineDimensionSet?)null)).ToArray(),
            metadata);
    }

    /// <summary>Digest of the retained instruction inputs, so a changed input is a different charge identity.</summary>
    public static string Fingerprint(ShortCarryInstruction instruction)
    {
        ArgumentNullException.ThrowIfNull(instruction);
        return Sha256Digest.Compute(JsonSerializer.SerializeToUtf8Bytes(instruction))[..16];
    }
}
