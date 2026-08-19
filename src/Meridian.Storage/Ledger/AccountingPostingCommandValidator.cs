using System.Text.Json;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Operations;
using Meridian.Contracts.Workstation;
using Meridian.Ledger;
using static Meridian.Contracts.Text.TextPrimitives;
using Meridian.Contracts.Integrity;

namespace Meridian.Storage.Ledger;

public static class AccountingPostingCommandValidator
{
    internal const string PostingCommandFingerprintTag = "postingCommandFingerprint";

    public static LedgerJournalEntryWrite NormalizeAndValidate(
        LedgerJournalEntryWrite write,
        bool requirePostingCommand = false,
        bool requireExpectedVersion = false)
    {
        ArgumentNullException.ThrowIfNull(write);
        ArgumentNullException.ThrowIfNull(write.Entry);

        if (write.PostingCommand is not { } command)
        {
            if (requirePostingCommand)
            {
                throw new LedgerValidationException(
                    "A typed accounting posting command is required at the production ledger append boundary.");
            }

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

        if (requireExpectedVersion && !command.ExpectedVersion.HasValue)
        {
            throw new LedgerValidationException(
                "Accounting posting command expected version is required at the production ledger append boundary.");
        }

        if (!OperationsOriginGuard.IsHumanOperator(command.ActionOrigin))
        {
            throw new LedgerValidationException(
                "Material accounting posting commands require a human-operator action origin.",
                OperationsOriginGuard.Refusal("append material accounting posting commands"));
        }

        if (string.IsNullOrWhiteSpace(command.IdempotencyKey))
        {
            throw new LedgerValidationException("Accounting posting command idempotency key is required.");
        }

        if (AssetAccountingEventTypeNames.TryParse(command.SourceEventType, out _))
        {
            ValidateAssetAccountingEvidence(command);
        }
        else if (command.Evidence.Count == 0 && string.IsNullOrWhiteSpace(command.OperatorRationale))
        {
            throw new LedgerValidationException("Accounting posting command requires retained evidence or an operator rationale.");
        }

        ValidateDataProvenance(command);

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

        ValidateBookContext(write, command);
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

    /// <summary>
    /// Data-provenance evidence gate (fail closed): a figure whose evidence declares a simulated,
    /// seeded, or sample origin may only cross the authoritative append boundary when the posting
    /// carries the retained simulated mark (<see cref="AccountingPostingCommandDto.Provenance"/> set
    /// to a non-real value). An unmarked simulated figure is refused rather than silently posted as
    /// if it were real; a marked one is allowed and the mark is retained onto the journal metadata.
    /// </summary>
    private static void ValidateDataProvenance(AccountingPostingCommandDto command)
    {
        var declaredProvenance = command.Provenance;
        if (!Enum.IsDefined(declaredProvenance))
        {
            throw new LedgerValidationException(
                $"Accounting posting command provenance '{declaredProvenance}' is not a defined DataProvenance value.");
        }

        if (declaredProvenance.IsNonReal())
        {
            // The mark is retained via the "dataProvenance" journal tag in BuildCommandTags.
            return;
        }

        if (DeclaresSimulatedOrigin(command))
        {
            throw new LedgerValidationException(
                "Accounting posting command evidence declares a simulated, seeded, or sample origin but the " +
                "posting is marked as real. Refusing to post an unmarked simulated figure; set the command's " +
                "DataProvenance mark so the simulated origin is retained on the journal.");
        }
    }

    // Unambiguous, structured simulated-origin markers. Matched by exact (trimmed, case-insensitive)
    // equality against the source system / source domain — never by substring — so legitimate values
    // like "Sample Custodian" or "fixture-bank" are not mistaken for a simulated origin. The explicit
    // command Provenance mark is the primary gate; this is a narrow safety net for a source that is
    // labeled simulated while the posting forgot to carry the mark.
    private static readonly string[] SimulatedOriginTokens =
    [
        "simulated", "simulation", "synthetic", "backtest",
        "seeded", "seed", "demo", "fixture",
        "sample", "placeholder"
    ];

    private static bool DeclaresSimulatedOrigin(AccountingPostingCommandDto command)
    {
        foreach (var evidence in command.Evidence)
        {
            if (IsSimulatedOriginToken(evidence.SourceSystem))
            {
                return true;
            }
        }

        return IsSimulatedOriginToken(command.EconomicEvent?.SourceDomain);
    }

    private static bool IsSimulatedOriginToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        foreach (var token in SimulatedOriginTokens)
        {
            if (string.Equals(trimmed, token, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static void ValidateAssetAccountingEvidence(AccountingPostingCommandDto command)
    {
        if (command.EconomicEvent is null ||
            command.ProjectionLineage is null ||
            command.BookPositionId is null ||
            command.BookContext is null ||
            command.RulePackReference is null)
        {
            throw new LedgerValidationException(
                "Asset accounting postings require authoritative book, position, economic-event, projection-lineage, and rule-pack context.");
        }

        if (command.Evidence.Count == 0)
        {
            throw new LedgerValidationException(
                "Asset accounting postings require verified retained evidence; operator rationale cannot substitute for evidence.");
        }

        var invalidEvidence = new List<string>();
        foreach (var item in command.Evidence)
        {
            var evidence = new RetainedEvidenceIdentityDto(
                item.EvidenceId,
                item.Uri,
                item.ContentHash ?? string.Empty,
                item.SourceSystem,
                item.SourceReference ?? string.Empty,
                item.ReviewStatus ?? string.Empty,
                item.Reviewer ?? string.Empty,
                item.ReviewedAtUtc ?? default,
                item.EffectiveDate ?? default,
                item.EvidenceVersion ?? 0,
                item.RetainedAtUtc,
                item.RetainedBy,
                item.SubjectType ?? string.Empty,
                item.SubjectId ?? string.Empty);
            invalidEvidence.AddRange(RetainedEvidenceIdentityValidator.Validate(evidence));
        }

        if (invalidEvidence.Count > 0)
        {
            throw new LedgerValidationException(
                $"Asset accounting posting evidence is incomplete: {string.Join(" ", invalidEvidence.Distinct(StringComparer.Ordinal))}");
        }

        var eventHash = command.EconomicEvent.SourceContentHash?.Trim();
        if (string.IsNullOrWhiteSpace(eventHash) ||
            !command.Evidence.Any(item => HashesMatch(item.ContentHash, eventHash)))
        {
            throw new LedgerValidationException(
                "Asset accounting posting evidence must include the economic event source content hash.");
        }

        if (command.Evidence.Any(item => item.EffectiveDate != command.EffectiveDate))
        {
            throw new LedgerValidationException(
                "Asset accounting posting evidence effective date must match the economic event effective date.");
        }
    }

    private static bool HashesMatch(string? left, string? right)
        => !string.IsNullOrWhiteSpace(left) &&
           !string.IsNullOrWhiteSpace(right) &&
           string.Equals(
               NormalizeSha256(left),
               NormalizeSha256(right),
               StringComparison.OrdinalIgnoreCase);

    private static string NormalizeSha256(string value)
        => value.Trim().StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
            ? value.Trim()["sha256:".Length..]
            : value.Trim();

    private static void ValidateBookContext(
        LedgerJournalEntryWrite write,
        AccountingPostingCommandDto command)
    {
        if (command.BookContext is not { } context)
        {
            if (RequiresAuthoritativeBookContext(command))
            {
                throw new LedgerValidationException(
                    "Typed accounting posting commands require authoritative book context before append.");
            }

            return;
        }

        var writeBookId = command.LedgerBookId ?? write.LedgerBookId;
        if (writeBookId != context.LedgerBookId)
        {
            throw new LedgerValidationException(
                "Accounting posting command book context ledger book must match the ledger write book.");
        }

        if (context.PeriodId != write.PeriodId)
        {
            throw new LedgerValidationException(
                "Accounting posting command book context period must match the ledger write period.");
        }

        if (context.AccountingBasis != write.AccountingBasis)
        {
            throw new LedgerValidationException(
                "Accounting posting command book context basis must match the ledger write accounting basis.");
        }

        if (!string.Equals(
                context.AccountingPolicyId?.Trim(),
                write.AccountingPolicyId?.Trim(),
                StringComparison.Ordinal) ||
            !string.Equals(
                context.AccountingPolicyVersion?.Trim(),
                write.AccountingPolicyVersion?.Trim(),
                StringComparison.Ordinal))
        {
            throw new LedgerValidationException(
                "Accounting posting command book context policy must match the ledger write accounting policy and version.");
        }
    }

    private static bool RequiresAuthoritativeBookContext(AccountingPostingCommandDto command)
        => command.BookPositionId.HasValue ||
           command.EconomicEvent is not null ||
           command.ProjectionLineage is not null ||
           command.RulePackReference is not null;

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
                item.Description,
                item.SourceReference,
                item.ReviewStatus,
                item.Reviewer,
                item.ReviewedAtUtc,
                item.EffectiveDate,
                item.EvidenceVersion,
                item.SubjectType))
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
            FundEventType = FirstText(metadata.FundEventType, treasury?.FundEventType),
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
            ["approvalState"] = command.ApprovalState.ToString(),
            [PostingCommandFingerprintTag] = ComputePostingCommandFingerprint(command)
        };

        AddTag(tags, "approvalId", command.ApprovalId);
        AddTag(tags, "dataProvenance", command.Provenance.Label());
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

    internal static string ComputePostingCommandFingerprint(AccountingPostingCommandDto command)
    {
        ArgumentNullException.ThrowIfNull(command);
        var element = JsonSerializer.SerializeToElement(
            command,
            AccountingPostingCommandFingerprintJsonContext.Default.AccountingPostingCommandDto);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            WriteCanonicalJson(writer, element);
        }

        return $"sha256:{Sha256Digest.Compute(stream.ToArray())}";
    }

    private static void WriteCanonicalJson(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element
                             .EnumerateObject()
                             .OrderBy(static property => property.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonicalJson(writer, property.Value);
                }

                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteCanonicalJson(writer, item);
                }

                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(element.GetString());
                break;
            case JsonValueKind.Number:
                writer.WriteRawValue(element.GetRawText(), skipInputValidation: true);
                break;
            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;
            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;
            case JsonValueKind.Null:
                writer.WriteNullValue();
                break;
            default:
                throw new LedgerValidationException(
                    $"Accounting posting command contains unsupported JSON value kind '{element.ValueKind}'.");
        }
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
                normalizedDimensions,
                // Carried, not defaulted. This rebuild exists to normalize dimensions; dropping
                // the leg's currency here discarded the transaction currency, both
                // transaction-side amounts, and the FX rate on the way to the store, because
                // every posting that reaches this path has dimensions and therefore gets rebuilt.
                line.Currency);
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
}
