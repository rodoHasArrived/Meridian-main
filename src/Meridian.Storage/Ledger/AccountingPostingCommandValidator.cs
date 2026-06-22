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

        if (write.LedgerBookId.HasValue && command.LedgerBookId.HasValue && write.LedgerBookId.Value != command.LedgerBookId.Value)
        {
            throw new LedgerValidationException("Ledger write ledger book id conflicts with the accounting posting command ledger book id.");
        }

        if (!write.LedgerBookId.HasValue && !command.LedgerBookId.HasValue)
        {
            throw new LedgerValidationException("Accounting posting command ledger book id is required.");
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
            LedgerBookId = command.LedgerBookId ?? write.LedgerBookId,
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
            LedgerBook = FirstText(metadata.LedgerBook, command.LedgerBookId?.ToString("D")),
            IdempotencyKey = FirstText(metadata.IdempotencyKey, treasury?.IdempotencyKey, command.IdempotencyKey),
            FundEventId = FirstText(metadata.FundEventId, treasury?.FundEventId),
            FundEventType = FirstText(metadata.FundEventType, treasury?.FundEventType, command.SourceEventType),
            CapitalAccountId = FirstText(metadata.CapitalAccountId, treasury?.CapitalAccountId),
            InvestorId = FirstText(metadata.InvestorId, treasury?.InvestorId),
            PaymentIntentId = FirstText(metadata.PaymentIntentId, treasury?.PaymentIntentId),
            SettlementReference = FirstText(metadata.SettlementReference, treasury?.SettlementReference),
            EvidenceReferences = MergeEvidence(metadata.EvidenceReferences, evidence)
        };

        var lines = NormalizeLineDimensions(entry.Lines, normalizedMetadata.Tags);
        return new JournalEntry(
            entry.JournalEntryId,
            entry.Timestamp,
            entry.Description,
            lines,
            normalizedMetadata);
    }

    private static IReadOnlyList<LedgerEntry> NormalizeLineDimensions(
        IReadOnlyList<LedgerEntry> lines,
        IReadOnlyDictionary<string, string>? tags)
    {
        if (tags is null || tags.Count == 0)
        {
            return lines;
        }

        LedgerEntry[]? normalized = null;
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            if (line.Dimensions is not null)
            {
                continue;
            }

            var dimensions = BuildLineDimensions(line.EntryId, tags);
            if (dimensions is null)
            {
                continue;
            }

            normalized ??= [.. lines];
            normalized[index] = new LedgerEntry(
                line.EntryId,
                line.JournalEntryId,
                line.Timestamp,
                line.Account,
                line.Debit,
                line.Credit,
                line.Description,
                dimensions);
        }

        return normalized ?? lines;
    }

    private static LedgerLineDimensionSet? BuildLineDimensions(
        Guid lineEntryId,
        IReadOnlyDictionary<string, string> tags)
    {
        var prefix = $"lineDimensions.{lineEntryId:N}.";
        var externalGlDimensions = tags
            .Where(pair => pair.Key.StartsWith(prefix + "externalGl.", StringComparison.OrdinalIgnoreCase))
            .Select(pair => new
            {
                Key = NormalizeOptional(pair.Key[(prefix.Length + "externalGl.".Length)..]),
                Value = NormalizeOptional(pair.Value)
            })
            .Where(pair => pair.Key is not null && pair.Value is not null)
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(pair => pair.Key!, pair => pair.Value!, StringComparer.OrdinalIgnoreCase);

        var dimensions = new LedgerLineDimensionSet(
            FundId: GetLineDimensionTag(tags, prefix, "fundId"),
            EntityId: GetLineDimensionTag(tags, prefix, "entityId"),
            SleeveId: GetLineDimensionTag(tags, prefix, "sleeveId"),
            StrategyId: GetLineDimensionTag(tags, prefix, "strategyId"),
            InvestorId: GetLineDimensionTag(tags, prefix, "investorId"),
            CapitalAccountId: GetLineDimensionTag(tags, prefix, "capitalAccountId"),
            InstrumentId: GetLineDimensionGuidTag(tags, prefix, "instrumentId"),
            TaxLotId: GetLineDimensionTag(tags, prefix, "taxLotId"),
            CostCenterId: GetLineDimensionTag(tags, prefix, "costCenterId"),
            CounterpartyId: GetLineDimensionTag(tags, prefix, "counterpartyId"),
            ExternalGlDimensions: externalGlDimensions,
            OrganizationId: GetLineDimensionTag(tags, prefix, "organizationId"),
            PortfolioId: GetLineDimensionTag(tags, prefix, "portfolioId"),
            BookId: GetLineDimensionTag(tags, prefix, "bookId"),
            AccountId: GetLineDimensionTag(tags, prefix, "accountId"),
            CustomerId: GetLineDimensionTag(tags, prefix, "customerId"),
            VendorId: GetLineDimensionTag(tags, prefix, "vendorId"),
            ProjectId: GetLineDimensionTag(tags, prefix, "projectId"));

        return HasAnyLineDimension(dimensions) ? dimensions : null;
    }

    private static string? GetLineDimensionTag(
        IReadOnlyDictionary<string, string> tags,
        string prefix,
        string field)
        => tags.TryGetValue(prefix + field, out var value) ? NormalizeOptional(value) : null;

    private static Guid? GetLineDimensionGuidTag(
        IReadOnlyDictionary<string, string> tags,
        string prefix,
        string field)
    {
        var value = GetLineDimensionTag(tags, prefix, field);
        if (value is null)
        {
            return null;
        }

        if (Guid.TryParse(value, out var parsed))
        {
            return parsed;
        }

        throw new LedgerValidationException($"Line dimension '{field}' must be a valid GUID.");
    }

    private static bool HasAnyLineDimension(LedgerLineDimensionSet dimensions)
        => !string.IsNullOrWhiteSpace(dimensions.FundId)
           || !string.IsNullOrWhiteSpace(dimensions.EntityId)
           || !string.IsNullOrWhiteSpace(dimensions.SleeveId)
           || !string.IsNullOrWhiteSpace(dimensions.StrategyId)
           || !string.IsNullOrWhiteSpace(dimensions.InvestorId)
           || !string.IsNullOrWhiteSpace(dimensions.CapitalAccountId)
           || dimensions.InstrumentId.HasValue
           || !string.IsNullOrWhiteSpace(dimensions.TaxLotId)
           || !string.IsNullOrWhiteSpace(dimensions.CostCenterId)
           || !string.IsNullOrWhiteSpace(dimensions.CounterpartyId)
           || dimensions.ExternalGlDimensions.Count > 0
           || !string.IsNullOrWhiteSpace(dimensions.OrganizationId)
           || !string.IsNullOrWhiteSpace(dimensions.PortfolioId)
           || !string.IsNullOrWhiteSpace(dimensions.BookId)
           || !string.IsNullOrWhiteSpace(dimensions.AccountId)
           || !string.IsNullOrWhiteSpace(dimensions.CustomerId)
           || !string.IsNullOrWhiteSpace(dimensions.VendorId)
           || !string.IsNullOrWhiteSpace(dimensions.ProjectId);

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

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
