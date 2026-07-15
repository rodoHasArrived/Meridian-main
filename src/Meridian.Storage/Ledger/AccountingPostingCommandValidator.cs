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

        ValidateTypedAssertions(command);

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

    private static void ValidateTypedAssertions(AccountingPostingCommandDto command)
    {
        if (command.EconomicEvent is { } economicEvent)
        {
            if (command.SourceEventId != economicEvent.EventId ||
                !string.Equals(command.SourceEventType?.Trim(), economicEvent.EventType.Trim(), StringComparison.OrdinalIgnoreCase) ||
                command.EffectiveDate != economicEvent.EffectiveDate)
            {
                throw new LedgerValidationException("Typed economic-event identity must match the accounting posting command source event.");
            }

            if (command.BookPositionId.HasValue && economicEvent.BookPositionId != command.BookPositionId)
            {
                throw new LedgerValidationException("Typed economic-event book position must match the accounting posting command book position.");
            }
        }

        if (command.ProjectionLineage is { } lineage)
        {
            if (command.EconomicEvent is null || lineage.TriggerEvent.EventId != command.EconomicEvent.EventId)
            {
                throw new LedgerValidationException("Projection lineage trigger must match the typed economic event.");
            }

            if (command.BookPositionId.HasValue &&
                (lineage.BookPositionId != command.BookPositionId ||
                 lineage.TriggerEvent.BookPositionId != command.BookPositionId))
            {
                throw new LedgerValidationException("Projection lineage book position must match the accounting posting command book position.");
            }
        }
    }

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
        var typedSecurityId = command.EconomicEvent?.SecurityId;
        if (metadata.SecurityId.HasValue && typedSecurityId.HasValue && metadata.SecurityId != typedSecurityId)
        {
            throw new LedgerValidationException("Journal metadata Security Master identity conflicts with the accounting posting command economic event.");
        }

        var tags = MergeCommandTags(metadata.Tags, BuildCommandTags(command));

        var normalizedMetadata = metadata with
        {
            SecurityId = metadata.SecurityId ?? typedSecurityId,
            EffectiveDate = metadata.EffectiveDate ?? treasury?.EffectiveDate ?? command.EffectiveDate,
            LedgerBook = FirstText(metadata.LedgerBook, command.LedgerBookId?.ToString("D")),
            IdempotencyKey = FirstText(metadata.IdempotencyKey, treasury?.IdempotencyKey, command.IdempotencyKey),
            FundEventId = FirstText(metadata.FundEventId, treasury?.FundEventId),
            FundEventType = FirstText(metadata.FundEventType, treasury?.FundEventType, command.SourceEventType),
            CapitalAccountId = FirstText(metadata.CapitalAccountId, treasury?.CapitalAccountId),
            InvestorId = FirstText(metadata.InvestorId, treasury?.InvestorId),
            PaymentIntentId = FirstText(metadata.PaymentIntentId, treasury?.PaymentIntentId),
            SettlementReference = FirstText(metadata.SettlementReference, treasury?.SettlementReference),
            EvidenceReferences = MergeEvidence(metadata.EvidenceReferences, evidence),
            Tags = tags
        };

        var lines = NormalizeLineDimensions(
            entry.Lines,
            normalizedMetadata.Tags,
            typedSecurityId,
            command.BookPositionId ?? command.EconomicEvent?.BookPositionId);
        return new JournalEntry(
            entry.JournalEntryId,
            entry.Timestamp,
            entry.Description,
            lines,
            normalizedMetadata);
    }

    private static IReadOnlyDictionary<string, string> BuildCommandTags(AccountingPostingCommandDto command)
    {
        var tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["postingCommandId"] = command.CommandId.ToString("D"),
            ["approvalState"] = command.ApprovalState.ToString()
        };

        AddTag(tags, "approvalId", command.ApprovalId);
        AddTag(tags, "sourceEventId", (command.EconomicEvent?.EventId ?? command.SourceEventId)?.ToString("D"));
        AddTag(tags, "sourceEventType", command.EconomicEvent?.EventType ?? command.SourceEventType);
        AddTag(tags, "sourceEventVersion", command.EconomicEvent?.EventVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
        AddTag(tags, "sourceEventDomain", command.EconomicEvent?.SourceDomain);
        AddTag(tags, "sourceEventEntityId", command.EconomicEvent?.SourceEntityId);
        AddTag(tags, "sourceEventContentHash", command.EconomicEvent?.SourceContentHash);
        AddTag(tags, "securityId", command.EconomicEvent?.SecurityId?.ToString("D"));
        AddTag(tags, "bookPositionId", (command.BookPositionId ?? command.EconomicEvent?.BookPositionId)?.ToString("D"));
        AddTag(tags, "projectionRunId", command.ProjectionLineage?.ProjectionRunId.ToString("D"));
        AddTag(tags, "projectionEventId", command.ProjectionLineage?.ProjectionEventId?.ToString("D"));
        AddTag(tags, "projectionModelKey", command.ProjectionLineage?.ModelKey);
        AddTag(tags, "projectionModelVersion", command.ProjectionLineage?.ModelVersion);
        AddTag(tags, "projectionEngineVersion", command.ProjectionLineage?.EngineVersion);
        AddTag(tags, "projectionScenario", command.ProjectionLineage?.Scenario);
        AddTag(tags, "rulePackId", command.RulePackReference?.RulePackId);
        AddTag(tags, "rulePackVersion", command.RulePackReference?.RulePackVersion);
        AddTag(tags, "selectedRuleId", command.RulePackReference?.SelectedRuleId);
        AddTag(tags, "selectedRuleVersion", command.RulePackReference?.SelectedRuleVersion);
        return tags;
    }

    private static void AddTag(IDictionary<string, string> tags, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            tags[key] = value.Trim();
        }
    }

    private static IReadOnlyDictionary<string, string> MergeCommandTags(
        IReadOnlyDictionary<string, string>? existing,
        IReadOnlyDictionary<string, string> commandOwned)
    {
        var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (existing is not null)
        {
            foreach (var pair in existing)
            {
                merged[pair.Key] = pair.Value;
            }
        }

        foreach (var pair in commandOwned)
        {
            if (merged.TryGetValue(pair.Key, out var current) &&
                !string.Equals(current?.Trim(), pair.Value, StringComparison.OrdinalIgnoreCase))
            {
                throw new LedgerValidationException(
                    $"Journal metadata tag '{pair.Key}' conflicts with the accounting posting command.");
            }

            merged[pair.Key] = pair.Value;
        }

        return merged;
    }

    private static IReadOnlyList<LedgerEntry> NormalizeLineDimensions(
        IReadOnlyList<LedgerEntry> lines,
        IReadOnlyDictionary<string, string>? tags,
        Guid? typedInstrumentId,
        Guid? typedPositionId)
    {
        if ((tags is null || tags.Count == 0) &&
            !typedInstrumentId.HasValue &&
            !typedPositionId.HasValue)
        {
            return lines;
        }

        LedgerEntry[]? normalized = null;
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            var dimensions = line.Dimensions ??
                (tags is null ? null : BuildLineDimensions(line.EntryId, tags));

            if (dimensions?.InstrumentId is Guid instrumentId &&
                typedInstrumentId.HasValue &&
                instrumentId != typedInstrumentId.Value)
            {
                throw new LedgerValidationException(
                    $"Ledger line '{line.EntryId:D}' instrument dimension conflicts with the typed economic event.");
            }

            if (dimensions?.PositionId is Guid positionId &&
                typedPositionId.HasValue &&
                positionId != typedPositionId.Value)
            {
                throw new LedgerValidationException(
                    $"Ledger line '{line.EntryId:D}' position dimension conflicts with the typed book position.");
            }

            var normalizedDimensions = dimensions;
            if (typedInstrumentId.HasValue || typedPositionId.HasValue)
            {
                normalizedDimensions = (dimensions ?? new LedgerLineDimensionSet()) with
                {
                    InstrumentId = dimensions?.InstrumentId ?? typedInstrumentId,
                    PositionId = dimensions?.PositionId ?? typedPositionId
                };
            }

            if (normalizedDimensions is null)
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
                normalizedDimensions);
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
            ProjectId: GetLineDimensionTag(tags, prefix, "projectId"))
        {
            PositionId = GetLineDimensionGuidTag(tags, prefix, "positionId")
        };

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
           || dimensions.PositionId.HasValue
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
