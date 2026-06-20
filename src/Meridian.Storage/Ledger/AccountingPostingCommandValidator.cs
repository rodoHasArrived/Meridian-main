using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.Ledger;

namespace Meridian.Storage.Ledger;

public static class AccountingPostingCommandValidator
{
    public static LedgerJournalEntryWrite NormalizeAndValidate(LedgerJournalEntryWrite write)
    {
        ArgumentNullException.ThrowIfNull(write);
        ArgumentNullException.ThrowIfNull(write.Entry);

        if (write.PostingCommand is not { } command)
        {
            return write;
        }

        if (command.CommandId == Guid.Empty)
        {
            throw new LedgerValidationException("Accounting posting command id is required.");
        }

        if (command.AggregateId != write.AggregateId)
        {
            throw new LedgerValidationException("Accounting posting command aggregate id must match the ledger write aggregate id.");
        }

        if (command.PeriodId != write.PeriodId)
        {
            throw new LedgerValidationException("Accounting posting command period id must match the ledger write period id.");
        }

        if (write.CommandId.HasValue && write.CommandId.Value != command.CommandId)
        {
            throw new LedgerValidationException("Ledger write command id conflicts with the accounting posting command id.");
        }

        if (write.SourceEventId.HasValue && command.SourceEventId.HasValue && write.SourceEventId.Value != command.SourceEventId.Value)
        {
            throw new LedgerValidationException("Ledger write source event id conflicts with the accounting posting command source event id.");
        }

        if (write.SourceJournalEntryId.HasValue && command.SourceJournalEntryId.HasValue && write.SourceJournalEntryId.Value != command.SourceJournalEntryId.Value)
        {
            throw new LedgerValidationException("Ledger write source journal entry id conflicts with the accounting posting command source journal entry id.");
        }

        if (command.CorrelationId == Guid.Empty || command.CausationId == Guid.Empty)
        {
            throw new LedgerValidationException("Accounting posting command correlation and causation ids must be non-empty when supplied.");
        }

        if (command.ExpectedVersion is < 0)
        {
            throw new LedgerValidationException("Accounting posting command expected version cannot be negative.");
        }

        if (command.ActionOrigin != OperationsActionOriginDto.HumanOperator)
        {
            throw new LedgerValidationException("Material accounting posting commands require a human-operator action origin.");
        }

        if (string.IsNullOrWhiteSpace(command.IdempotencyKey))
        {
            throw new LedgerValidationException("Accounting posting command idempotency key is required.");
        }

        if (command.Evidence.Count == 0 && string.IsNullOrWhiteSpace(command.OperatorRationale))
        {
            throw new LedgerValidationException("Accounting posting command requires retained evidence or an operator rationale.");
        }

        if (command.Intent is AccountingPostingIntentDto.Reversal or AccountingPostingIntentDto.Rebook or AccountingPostingIntentDto.Restatement)
        {
            if (!command.SourceJournalEntryId.HasValue && !write.SourceJournalEntryId.HasValue)
            {
                throw new LedgerValidationException("Correction posting commands require source journal entry lineage.");
            }
        }

        if (RequiresApproval(command.Intent) && command.ApprovalState is not AccountingPostingApprovalStateDto.Approved and not AccountingPostingApprovalStateDto.NotRequired)
        {
            throw new LedgerValidationException("Accounting posting command requires approved or not-required reviewer state before append.");
        }

        var entry = NormalizeEntryMetadata(write.Entry, command);
        return write with
        {
            Entry = entry,
            CommandId = command.CommandId,
            CorrelationId = command.CorrelationId ?? write.CorrelationId,
            SourceEventId = command.SourceEventId ?? write.SourceEventId,
            SourceJournalEntryId = command.SourceJournalEntryId ?? write.SourceJournalEntryId,
            PostingKind = command.Intent == AccountingPostingIntentDto.Adjustment
                ? LedgerPostingKindDto.Adjustment
                : write.PostingKind
        };
    }

    private static bool RequiresApproval(AccountingPostingIntentDto intent)
        => intent is not AccountingPostingIntentDto.AutomatedDraft;

    private static JournalEntry NormalizeEntryMetadata(JournalEntry entry, AccountingPostingCommandDto command)
    {
        var metadata = entry.Metadata;
        var treasury = command.TreasuryContext;
        var evidence = command.Evidence
            .Select(static item => new JournalEvidenceReference(
                item.EvidenceId,
                item.Uri,
                item.Kind.ToString(),
                item.SourceSystem,
                item.RetainedAtUtc,
                item.RetainedBy,
                item.SubjectId,
                item.ContentHash,
                item.Description))
            .ToArray();

        var normalizedMetadata = metadata with
        {
            EffectiveDate = metadata.EffectiveDate ?? treasury?.EffectiveDate ?? command.EffectiveDate,
            IdempotencyKey = FirstText(metadata.IdempotencyKey, treasury?.IdempotencyKey, command.IdempotencyKey),
            FundEventId = FirstText(metadata.FundEventId, treasury?.FundEventId),
            FundEventType = FirstText(metadata.FundEventType, treasury?.FundEventType, command.SourceEventType),
            CapitalAccountId = FirstText(metadata.CapitalAccountId, treasury?.CapitalAccountId),
            InvestorId = FirstText(metadata.InvestorId, treasury?.InvestorId),
            PaymentIntentId = FirstText(metadata.PaymentIntentId, treasury?.PaymentIntentId),
            SettlementReference = FirstText(metadata.SettlementReference, treasury?.SettlementReference),
            EvidenceReferences = MergeEvidence(metadata.EvidenceReferences, evidence)
        };

        return new JournalEntry(
            entry.JournalEntryId,
            entry.Timestamp,
            entry.Description,
            entry.Lines,
            normalizedMetadata);
    }

    private static IReadOnlyList<JournalEvidenceReference> MergeEvidence(
        IReadOnlyList<JournalEvidenceReference> existing,
        IReadOnlyList<JournalEvidenceReference> additional)
    {
        if (additional.Count == 0)
        {
            return existing;
        }

        return existing
            .Concat(additional)
            .GroupBy(static item => item.EvidenceId, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .ToArray();
    }

    private static string? FirstText(params string?[] values)
        => values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value))?.Trim();
}