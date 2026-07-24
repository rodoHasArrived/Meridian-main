using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.Ledger;
using Meridian.Ledger;
using Meridian.Storage.Ledger;

namespace Meridian.FinancialOperations.OperationsContinuity;

/// <summary>
/// Ledger-posting concern extracted from OperationsContinuityWorkflowService: validates ledger post
/// requests and journal candidates, resolves authoritative security-master statuses, builds the ledger
/// journal write, and commits/appends it to the transactional-commit or journal store. Owns the three
/// optional ledger/security dependencies. Behavior-preserving; the facade constructs and delegates to
/// it, keeping repository/audit/status-derivation/projection in the facade.
/// </summary>
internal sealed class OperationsLedgerPostingService
{
    private readonly ILedgerJournalStore? _ledgerJournalStore;
    private readonly IOperationsContinuityTransactionalCommitStore? _transactionalCommitStore;
    private readonly ISecurityMasterQueryService? _securityMasterQueryService;

    public OperationsLedgerPostingService(
        ILedgerJournalStore? ledgerJournalStore,
        IOperationsContinuityTransactionalCommitStore? transactionalCommitStore,
        ISecurityMasterQueryService? securityMasterQueryService)
    {
        _ledgerJournalStore = ledgerJournalStore;
        _transactionalCommitStore = transactionalCommitStore;
        _securityMasterQueryService = securityMasterQueryService;
    }

    public bool IsLedgerStoreAvailable => _transactionalCommitStore is not null || _ledgerJournalStore is not null;

    public async Task<LedgerCommitOutcome> CommitOrAppendAsync(
        OperationsContinuityWorkflow workflowForCommit,
        OperationsWorkflowAuditDraft auditDraft,
        LedgerJournalEntryWrite? journalWrite,
        IReadOnlyList<OperationsEvidenceLinkDto> evidence,
        CancellationToken ct)
    {
        if (journalWrite is not null && _transactionalCommitStore is not null)
        {
            OperationsContinuityTransactionalCommitResult commitResult;
            try
            {
                commitResult = await _transactionalCommitStore
                    .CommitLedgerPostingAsync(workflowForCommit, auditDraft, journalWrite, ct)
                    .ConfigureAwait(false);
            }
            catch (LedgerValidationException ex)
            {
                return LedgerCommitOutcome.Rejected(new OperationsWorkflowBlockerDto(
                    "LEDGER_JOURNAL_APPEND_REJECTED",
                    ex.Message,
                    OperationsGateKeyDto.LedgerPosting,
                    "Critical",
                    evidence));
            }

            return LedgerCommitOutcome.Committed(commitResult.Workflow);
        }

        if (journalWrite is not null)
        {
            try
            {
                await _ledgerJournalStore!.AppendAsync(journalWrite, ct).ConfigureAwait(false);
            }
            catch (LedgerValidationException ex)
            {
                return LedgerCommitOutcome.Rejected(new OperationsWorkflowBlockerDto(
                    "LEDGER_JOURNAL_APPEND_REJECTED",
                    ex.Message,
                    OperationsGateKeyDto.LedgerPosting,
                    "Critical",
                    evidence));
            }
        }

        return LedgerCommitOutcome.Proceed();
    }

    public IReadOnlyList<OperationsWorkflowBlockerDto> ValidateLedgerPostRequest(
        OperationsLedgerPostRequestDto request,
        IReadOnlyList<OperationsEvidenceLinkDto> evidenceLinks)
    {
        var blockers = new List<OperationsWorkflowBlockerDto>();
        if (string.IsNullOrWhiteSpace(request.LedgerBatchId))
        {
            blockers.Add(new OperationsWorkflowBlockerDto(
                "LEDGER_BATCH_ID_REQUIRED",
                "Ledger posting must return a durable ledger batch id.",
                OperationsGateKeyDto.LedgerPosting,
                "Critical",
                evidenceLinks));
        }

        if (string.IsNullOrWhiteSpace(request.PostingKind))
        {
            blockers.Add(new OperationsWorkflowBlockerDto(
                "LEDGER_POSTING_KIND_REQUIRED",
                "Ledger posting kind is required.",
                OperationsGateKeyDto.LedgerPosting,
                "Error",
                evidenceLinks));
        }

        if (!request.HasValidatedJournal)
        {
            blockers.Add(new OperationsWorkflowBlockerDto(
                "LEDGER_VALIDATED_JOURNAL_REQUIRED",
                "Ledger posting requires a validated journal draft.",
                OperationsGateKeyDto.LedgerPosting,
                "Critical",
                evidenceLinks));
        }

        if (!request.PeriodOpen)
        {
            blockers.Add(new OperationsWorkflowBlockerDto(
                "LEDGER_PERIOD_CLOSED",
                "Ledger posting into a closed or hard-closed period requires a governed reopen path before adjustment posting.",
                OperationsGateKeyDto.LedgerPosting,
                "Critical",
                evidenceLinks));
        }

        if (request.HasDuplicatePostingCandidate)
        {
            blockers.Add(new OperationsWorkflowBlockerDto(
                "LEDGER_DUPLICATE_POSTING_CANDIDATE",
                "Duplicate posting candidate detected for this source activity or generated accounting event.",
                OperationsGateKeyDto.LedgerPosting,
                "Critical",
                evidenceLinks));
        }

        return blockers;
    }

    public bool TryBuildJournalWrite(
        OperationsContinuityWorkflow workflow,
        OperationsLedgerPostRequestDto request,
        IReadOnlyDictionary<Guid, SecurityStatusDto> authoritativeSecurityStatuses,
        out LedgerJournalEntryWrite journalWrite,
        out IReadOnlyList<OperationsWorkflowBlockerDto> blockers)
    {
        journalWrite = default!;
        var candidate = request.JournalCandidate;
        if (candidate is null)
        {
            blockers =
            [
                new OperationsWorkflowBlockerDto(
                    "LEDGER_JOURNAL_CANDIDATE_REQUIRED",
                    "Ledger posting requires a journal candidate that can be appended to the durable ledger journal store.",
                    OperationsGateKeyDto.LedgerPosting,
                    "Critical",
                    OperationsContinuityWorkflowText.NormalizeEvidence(request.EvidenceLinks))
            ];
            return false;
        }

        var validationBlockers = ValidateJournalCandidate(workflow, request, candidate, authoritativeSecurityStatuses, request.EvidenceLinks);
        if (validationBlockers.Count > 0)
        {
            blockers = validationBlockers;
            return false;
        }

        try
        {
            var journalEntryId = candidate.JournalEntryId.GetValueOrDefault();
            if (journalEntryId == Guid.Empty)
            {
                journalEntryId = Guid.NewGuid();
            }

            var description = candidate.Description.Trim();
            var lines = candidate.Lines
                .Select(line =>
                {
                    _ = Enum.TryParse<LedgerAccountType>(line.AccountType, ignoreCase: true, out var accountType);
                    return new LedgerEntry(
                        line.EntryId.GetValueOrDefault() == Guid.Empty ? Guid.NewGuid() : line.EntryId.GetValueOrDefault(),
                        journalEntryId,
                        candidate.Timestamp,
                        new LedgerAccount(
                            line.AccountName.Trim(),
                            accountType,
                            NormalizeOptional(line.Symbol),
                            NormalizeOptional(line.FinancialAccountId)),
                        line.Debit,
                        line.Credit,
                        description,
                        ToLedgerLineDimensions(line.Dimensions));
                })
                .ToArray();

            var entry = new JournalEntry(
                journalEntryId,
                candidate.Timestamp,
                description,
                lines,
                ToJournalEntryMetadata(candidate, authoritativeSecurityStatuses));

            journalWrite = new LedgerJournalEntryWrite(
                entry,
                candidate.AggregateId,
                candidate.PeriodId,
                candidate.CommandId,
                candidate.CorrelationId,
                candidate.AccountingBasis,
                NormalizePolicy(candidate.AccountingPolicyId),
                NormalizePolicy(candidate.AccountingPolicyVersion),
                NormalizeOptional(candidate.RuleId),
                NormalizeOptional(candidate.RuleVersion),
                candidate.SourceEventId,
                candidate.SourceJournalEntryId,
                candidate.PostingKind,
                candidate.AdjustmentApproval,
                PostingCommand: BuildPostingCommand(workflow, request, candidate),
                LedgerBookId: workflow.LedgerBookId);
            blockers = [];
            return true;
        }
        catch (LedgerValidationException ex)
        {
            blockers =
            [
                new OperationsWorkflowBlockerDto(
                    "LEDGER_JOURNAL_CANDIDATE_INVALID",
                    ex.Message,
                    OperationsGateKeyDto.LedgerPosting,
                    "Critical",
                    OperationsContinuityWorkflowText.NormalizeEvidence(request.EvidenceLinks))
            ];
            return false;
        }
    }

    private static IReadOnlyList<OperationsWorkflowBlockerDto> ValidateJournalCandidate(
        OperationsContinuityWorkflow workflow,
        OperationsLedgerPostRequestDto request,
        OperationsLedgerJournalCandidateDto candidate,
        IReadOnlyDictionary<Guid, SecurityStatusDto> authoritativeSecurityStatuses,
        IReadOnlyList<OperationsEvidenceLinkDto>? evidenceLinks)
    {
        var evidence = OperationsContinuityWorkflowText.NormalizeEvidence(evidenceLinks);
        var blockers = new List<OperationsWorkflowBlockerDto>();
        if (candidate.AggregateId == Guid.Empty)
        {
            blockers.Add(CreateJournalCandidateBlocker("LEDGER_JOURNAL_AGGREGATE_ID_REQUIRED", "Ledger journal candidate aggregate id is required.", evidence));
        }

        if (candidate.PeriodId == Guid.Empty)
        {
            blockers.Add(CreateJournalCandidateBlocker("LEDGER_JOURNAL_PERIOD_ID_REQUIRED", "Ledger journal candidate period id is required.", evidence));
        }
        else if (TryResolveWorkflowPeriodGuid(workflow.PeriodId, out var workflowPeriodId) && candidate.PeriodId != workflowPeriodId)
        {
            blockers.Add(CreateJournalCandidateBlocker("LEDGER_JOURNAL_PERIOD_ID_MISMATCH", "Ledger journal candidate period id must match the workflow period.", evidence));
        }

        if (candidate.AggregateId != Guid.Empty && candidate.AggregateId != workflow.FundAccountId)
        {
            blockers.Add(CreateJournalCandidateBlocker("LEDGER_JOURNAL_AGGREGATE_ID_MISMATCH", "Ledger journal candidate aggregate id must match the workflow fund account.", evidence));
        }

        if (candidate.CommandId.GetValueOrDefault() == Guid.Empty ||
            string.IsNullOrWhiteSpace(candidate.IdempotencyKey))
        {
            blockers.Add(CreateJournalCandidateBlocker(
                "LEDGER_IDEMPOTENCY_KEY_MISSING",
                "Ledger journal candidate requires a durable command id and idempotency key before posting.",
                evidence));
        }

        if (!candidate.ExpectedLedgerVersion.HasValue || candidate.ExpectedLedgerVersion.Value < 0)
        {
            blockers.Add(CreateJournalCandidateBlocker(
                "LEDGER_EXPECTED_VERSION_REQUIRED",
                "Ledger journal candidate requires the authoritative accounting-period version before posting.",
                evidence));
        }

        if (candidate.Metadata?.SecurityId is null ||
            string.IsNullOrWhiteSpace(candidate.SecurityMasterProvenance))
        {
            blockers.Add(CreateJournalCandidateBlocker(
                "LEDGER_JOURNAL_PROVENANCE_MISSING",
                "Ledger journal candidate requires Security Master security id and provenance before posting.",
                evidence));
        }
        else if (!SecurityMasterProvenanceReferences(candidate.SecurityMasterProvenance, candidate.Metadata.SecurityId.Value))
        {
            blockers.Add(CreateJournalCandidateBlocker(
                "LEDGER_JOURNAL_SECURITY_MASTER_PROVENANCE_MISMATCH",
                "Ledger journal candidate Security Master provenance must reference the candidate security id before posting.",
                evidence));
        }

        if (candidate.Timestamp == default)
        {
            blockers.Add(CreateJournalCandidateBlocker("LEDGER_JOURNAL_TIMESTAMP_REQUIRED", "Ledger journal candidate timestamp is required.", evidence));
        }

        if (string.IsNullOrWhiteSpace(candidate.Description))
        {
            blockers.Add(CreateJournalCandidateBlocker("LEDGER_JOURNAL_DESCRIPTION_REQUIRED", "Ledger journal candidate description is required.", evidence));
        }

        if (candidate.Lines is null || candidate.Lines.Count == 0)
        {
            blockers.Add(CreateJournalCandidateBlocker("LEDGER_JOURNAL_LINES_REQUIRED", "Ledger journal candidate requires at least one debit or credit line.", evidence));
        }

        foreach (var line in candidate.Lines ?? [])
        {
            if (string.IsNullOrWhiteSpace(line.AccountName))
            {
                blockers.Add(CreateJournalCandidateBlocker("LEDGER_JOURNAL_ACCOUNT_NAME_REQUIRED", "Every ledger journal candidate line requires an account name.", evidence));
            }

            if (!Enum.TryParse<LedgerAccountType>(line.AccountType, ignoreCase: true, out _))
            {
                blockers.Add(CreateJournalCandidateBlocker(
                    "LEDGER_JOURNAL_ACCOUNT_TYPE_INVALID",
                    $"Ledger journal candidate account type '{line.AccountType}' is invalid.",
                    evidence));
            }

            if (IsInstrumentBearingJournalLine(line))
            {
                var lineLabel = string.IsNullOrWhiteSpace(line.Symbol)
                    ? string.IsNullOrWhiteSpace(line.AccountName)
                        ? "instrument-bearing line"
                        : $"account '{line.AccountName.Trim()}'"
                    : $"symbol '{line.Symbol.Trim()}'";
                if (string.IsNullOrWhiteSpace(line.Symbol))
                {
                    blockers.Add(CreateJournalCandidateBlocker(
                        "LEDGER_LINE_SECURITY_MASTER_SYMBOL_MISSING",
                        $"Ledger journal candidate line for {lineLabel} requires an explicit Security Master symbol before posting.",
                        evidence));
                }
                else if (!string.IsNullOrWhiteSpace(candidate.Metadata?.Symbol) &&
                    !string.Equals(line.Symbol.Trim(), candidate.Metadata.Symbol.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    blockers.Add(CreateJournalCandidateBlocker(
                        "LEDGER_LINE_SECURITY_MASTER_SYMBOL_MISMATCH",
                        $"Ledger journal candidate line for {lineLabel} must use the same Security Master symbol as the journal candidate metadata.",
                        evidence));
                }

                if (line.SecurityId.GetValueOrDefault() == Guid.Empty)
                {
                    blockers.Add(CreateJournalCandidateBlocker(
                        "LEDGER_LINE_SECURITY_MASTER_ID_MISSING",
                        $"Ledger journal candidate line for {lineLabel} requires a resolved Security Master security id before posting.",
                        evidence));
                }
                else
                {
                    var lineSecurityId = line.SecurityId.GetValueOrDefault();
                    if (candidate.Metadata?.SecurityId is Guid candidateSecurityId && candidateSecurityId != lineSecurityId)
                    {
                        blockers.Add(CreateJournalCandidateBlocker(
                            "LEDGER_LINE_SECURITY_MASTER_ID_MISMATCH",
                            $"Ledger journal candidate line for {lineLabel} must use the same Security Master security id as the journal candidate metadata.",
                            evidence));
                    }

                    if (!string.IsNullOrWhiteSpace(line.SecurityMasterProvenance) &&
                        !SecurityMasterProvenanceReferences(line.SecurityMasterProvenance, lineSecurityId))
                    {
                        blockers.Add(CreateJournalCandidateBlocker(
                            "LEDGER_LINE_SECURITY_MASTER_PROVENANCE_MISMATCH",
                            $"Ledger journal candidate line for {lineLabel} must carry Security Master provenance that references the resolved security id.",
                            evidence));
                    }
                }

                if (!line.SecurityMasterApproved)
                {
                    blockers.Add(CreateJournalCandidateBlocker(
                        "LEDGER_LINE_SECURITY_MASTER_APPROVAL_REQUIRED",
                        $"Ledger journal candidate line for {lineLabel} requires approved Security Master identity before posting.",
                        evidence));
                }

                if (!TryGetAuthoritativeActiveSecurityStatus(line.SecurityId, authoritativeSecurityStatuses))
                {
                    blockers.Add(CreateJournalCandidateBlocker(
                        "LEDGER_LINE_SECURITY_MASTER_ACTIVE_STATUS_REQUIRED",
                        $"Ledger journal candidate line for {lineLabel} requires active Security Master status from the authoritative Security Master before posting.",
                        evidence));
                }

                if (string.IsNullOrWhiteSpace(line.SecurityMasterApprovalReference))
                {
                    blockers.Add(CreateJournalCandidateBlocker(
                        "LEDGER_LINE_SECURITY_MASTER_APPROVAL_EVIDENCE_MISSING",
                        $"Ledger journal candidate line for {lineLabel} requires Security Master approval evidence before posting.",
                        evidence));
                }

                if (string.IsNullOrWhiteSpace(line.SecurityMasterProvenance))
                {
                    blockers.Add(CreateJournalCandidateBlocker(
                        "LEDGER_LINE_SECURITY_MASTER_PROVENANCE_MISSING",
                        $"Ledger journal candidate line for {lineLabel} requires Security Master provenance before posting.",
                        evidence));
                }

                if (string.IsNullOrWhiteSpace(line.LedgerMappingReference))
                {
                    blockers.Add(CreateJournalCandidateBlocker(
                        "LEDGER_LINE_SECURITY_MASTER_MAPPING_MISSING",
                        $"Ledger journal candidate line for {lineLabel} requires a Security Master ledger mapping reference before posting.",
                        evidence));
                }
                else if (!LedgerMappingReferencesInstrument(line.LedgerMappingReference, line.Symbol, line.SecurityId))
                {
                    blockers.Add(CreateJournalCandidateBlocker(
                        "LEDGER_LINE_SECURITY_MASTER_MAPPING_MISMATCH",
                        $"Ledger journal candidate line for {lineLabel} requires a Security Master ledger mapping reference tied to the resolved symbol or security id.",
                        evidence));
                }
            }
        }

        var totalDebits = candidate.Lines?.Sum(static line => line.Debit) ?? 0m;
        var totalCredits = candidate.Lines?.Sum(static line => line.Credit) ?? 0m;
        if (!LedgerJournalConstruction.IsBalanced(totalDebits, totalCredits))
        {
            blockers.Add(CreateJournalCandidateBlocker(
                "LEDGER_DRAFT_IMBALANCED",
                "Ledger journal candidate debit and credit totals must balance before posting.",
                evidence));
        }

        return blockers;
    }

    private static AccountingPostingCommandDto BuildPostingCommand(
        OperationsContinuityWorkflow workflow,
        OperationsLedgerPostRequestDto request,
        OperationsLedgerJournalCandidateDto candidate)
    {
        var commandId = candidate.CommandId.GetValueOrDefault();
        var retainedAt = candidate.Timestamp;
        var evidence = OperationsContinuityWorkflowText.NormalizeEvidence(request.EvidenceLinks)
            .Select(link => new AccountingPostingEvidenceReferenceDto(
                link.EvidenceId,
                link.Route ?? $"evidence://operations-continuity/{link.EvidenceId}",
                AccountingPostingEvidenceKindDto.Reconciliation,
                string.IsNullOrWhiteSpace(link.Source) ? "OperationsContinuity" : link.Source.Trim(),
                link.CapturedAtUtc ?? retainedAt,
                request.Actor,
                SubjectId: workflow.WorkflowId.ToString("D"),
                Description: link.Label))
            .ToArray();
        var intent = candidate.PostingKind switch
        {
            LedgerPostingKindDto.Adjustment => AccountingPostingIntentDto.Adjustment,
            _ => AccountingPostingIntentDto.Originating
        };

        return new AccountingPostingCommandDto(
            commandId,
            candidate.AggregateId,
            candidate.PeriodId,
            DateOnly.FromDateTime(candidate.Timestamp.UtcDateTime),
            candidate.Timestamp,
            candidate.IdempotencyKey!,
            intent,
            SourceEventId: candidate.SourceEventId,
            CorrelationId: candidate.CorrelationId,
            CausationId: commandId,
            SourceJournalEntryId: candidate.SourceJournalEntryId,
            ExpectedVersion: candidate.ExpectedLedgerVersion,
            SourceEventType: candidate.Metadata?.ActivityType,
            ApprovalState: AccountingPostingApprovalStateDto.Approved,
            ApprovalId: candidate.AdjustmentApproval?.ApprovalId ?? request.LedgerBatchId,
            OperatorRationale: request.Rationale,
            Evidence: evidence,
            ActionOrigin: request.ActionOrigin,
            LedgerBookId: workflow.LedgerBookId);
    }


    public async Task<IReadOnlyDictionary<Guid, SecurityStatusDto>> ResolveAuthoritativeSecurityStatusesAsync(
        IReadOnlyList<OperationsLedgerJournalLineDto>? lines,
        CancellationToken ct)
    {
        var securityIds = (lines ?? [])
            .Where(static line => IsInstrumentBearingJournalLine(line))
            .Select(static line => line.SecurityId.GetValueOrDefault())
            .Where(static securityId => securityId != Guid.Empty)
            .Distinct()
            .ToArray();

        if (securityIds.Length == 0 || _securityMasterQueryService is null)
        {
            return new Dictionary<Guid, SecurityStatusDto>();
        }

        var statuses = new Dictionary<Guid, SecurityStatusDto>();
        foreach (var securityId in securityIds)
        {
            var security = await _securityMasterQueryService.GetByIdAsync(securityId, ct).ConfigureAwait(false);
            if (security is not null)
            {
                statuses[securityId] = security.Status;
            }
        }

        return statuses;
    }

    private static bool TryResolveWorkflowPeriodGuid(string workflowPeriodId, out Guid resolvedPeriodId) =>
        Guid.TryParse(workflowPeriodId, out resolvedPeriodId);

    private static OperationsWorkflowBlockerDto CreateJournalCandidateBlocker(
        string code,
        string message,
        IReadOnlyList<OperationsEvidenceLinkDto> evidenceLinks) =>
        new(code, message, OperationsGateKeyDto.LedgerPosting, "Critical", evidenceLinks);

    private static bool SecurityMasterProvenanceReferences(string? provenance, Guid securityId)
    {
        if (string.IsNullOrWhiteSpace(provenance) || securityId == Guid.Empty)
        {
            return false;
        }

        return provenance.Contains(securityId.ToString("D"), StringComparison.OrdinalIgnoreCase) ||
            provenance.Contains(securityId.ToString("N"), StringComparison.OrdinalIgnoreCase);
    }

    private static bool LedgerMappingReferencesInstrument(string? mappingReference, string? symbol, Guid? securityId)
    {
        if (string.IsNullOrWhiteSpace(mappingReference))
        {
            return false;
        }

        var mapping = mappingReference.Trim();
        var resolvedSecurityId = securityId.GetValueOrDefault();
        if (resolvedSecurityId != Guid.Empty &&
            (mapping.Contains(resolvedSecurityId.ToString("D"), StringComparison.OrdinalIgnoreCase) ||
             mapping.Contains(resolvedSecurityId.ToString("N"), StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(symbol) &&
            mapping.Contains(symbol.Trim(), StringComparison.OrdinalIgnoreCase);
    }


    private static bool TryGetAuthoritativeActiveSecurityStatus(
        Guid? securityId,
        IReadOnlyDictionary<Guid, SecurityStatusDto> authoritativeSecurityStatuses) =>
        TryGetAuthoritativeSecurityStatus(securityId, authoritativeSecurityStatuses, out var status) &&
        status == SecurityStatusDto.Active;

    private static bool TryGetAuthoritativeSecurityStatus(
        Guid? securityId,
        IReadOnlyDictionary<Guid, SecurityStatusDto> authoritativeSecurityStatuses,
        out SecurityStatusDto status)
    {
        status = default;
        var resolvedSecurityId = securityId.GetValueOrDefault();
        return resolvedSecurityId != Guid.Empty &&
            authoritativeSecurityStatuses.TryGetValue(resolvedSecurityId, out status);
    }

    private static bool IsInstrumentBearingJournalLine(OperationsLedgerJournalLineDto line)
        => !string.IsNullOrWhiteSpace(line.Symbol) ||
           line.SecurityId.GetValueOrDefault() != Guid.Empty ||
           !string.IsNullOrWhiteSpace(line.SecurityMasterProvenance) ||
           !string.IsNullOrWhiteSpace(line.LedgerMappingReference) ||
           IsInstrumentAccountName(line.AccountName);

    private static bool IsInstrumentAccountName(string? accountName)
    {
        if (string.IsNullOrWhiteSpace(accountName))
        {
            return false;
        }

        var normalized = accountName.Trim();
        return normalized.Equals("Securities", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("Dividend Receivable", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("Accrued Interest Receivable", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("Corporate Action Distribution", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("Short Securities Payable", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("Option Premium Asset", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("Option Premium Liability", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("Futures MTM Settlement", StringComparison.OrdinalIgnoreCase);
    }

    private static JournalEntryMetadata? ToJournalEntryMetadata(
        OperationsLedgerJournalCandidateDto candidate,
        IReadOnlyDictionary<Guid, SecurityStatusDto> authoritativeSecurityStatuses)
    {
        var metadata = candidate.Metadata;
        if (metadata is null)
        {
            return null;
        }

        var tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (metadata.Tags is not null)
        {
            foreach (var pair in metadata.Tags)
            {
                tags[pair.Key] = pair.Value;
            }
        }

        if (!string.IsNullOrWhiteSpace(candidate.IdempotencyKey))
        {
            tags["operationsContinuityIdempotencyKey"] = candidate.IdempotencyKey.Trim();
        }

        if (!string.IsNullOrWhiteSpace(candidate.SecurityMasterProvenance))
        {
            tags["securityMasterProvenance"] = candidate.SecurityMasterProvenance.Trim();
        }

        var securityMasterLineage = BuildSecurityMasterLineageTag(candidate.Lines, authoritativeSecurityStatuses);
        if (!string.IsNullOrWhiteSpace(securityMasterLineage))
        {
            tags["securityMasterLineage"] = securityMasterLineage;
        }

        return new JournalEntryMetadata(
            ActivityType: NormalizeOptional(metadata.ActivityType),
            Symbol: NormalizeOptional(metadata.Symbol),
            SecurityId: metadata.SecurityId,
            OrderId: metadata.OrderId,
            FillId: metadata.FillId,
            ProjectId: NormalizeOptional(metadata.ProjectId),
            LedgerBook: NormalizeOptional(metadata.LedgerBook),
            LedgerView: null,
            ScenarioId: NormalizeOptional(metadata.ScenarioId),
            StrategyId: NormalizeOptional(metadata.StrategyId),
            FinancialAccountId: NormalizeOptional(metadata.FinancialAccountId),
            CounterpartyAccountId: NormalizeOptional(metadata.CounterpartyAccountId),
            Institution: NormalizeOptional(metadata.Institution),
            Tags: tags.Count == 0 ? null : tags);
    }

    private static LedgerLineDimensionSet? ToLedgerLineDimensions(LedgerDimensionSetDto? dimensions)
        => LedgerJournalConstruction.ToLedgerLineDimensions(dimensions);

    private static string? BuildSecurityMasterLineageTag(
        IReadOnlyList<OperationsLedgerJournalLineDto>? lines,
        IReadOnlyDictionary<Guid, SecurityStatusDto> authoritativeSecurityStatuses)
    {
        var mappedLines = (lines ?? [])
            .Where(static line => !string.IsNullOrWhiteSpace(line.Symbol))
            .Select(line =>
            {
                var symbol = line.Symbol!.Trim();
                var securityIdValue = line.SecurityId.GetValueOrDefault();
                var securityId = securityIdValue == Guid.Empty
                    ? "missing"
                    : securityIdValue.ToString("N");
                var provenance = string.IsNullOrWhiteSpace(line.SecurityMasterProvenance)
                    ? "missing"
                    : line.SecurityMasterProvenance.Trim();
                var mapping = string.IsNullOrWhiteSpace(line.LedgerMappingReference)
                    ? "missing"
                    : line.LedgerMappingReference.Trim();
                var approval = string.IsNullOrWhiteSpace(line.SecurityMasterApprovalReference)
                    ? "missing"
                    : line.SecurityMasterApprovalReference.Trim();
                var status = TryGetAuthoritativeSecurityStatus(line.SecurityId, authoritativeSecurityStatuses, out var authoritativeStatus)
                    ? $"security-status:{authoritativeStatus}"
                    : "missing";
                return $"{symbol}:{securityId}:{mapping}:{approval}:{status}:{provenance}";
            })
            .ToArray();

        return mappedLines.Length == 0 ? null : string.Join("|", mappedLines);
    }

    private static string NormalizePolicy(string value) =>
        string.IsNullOrWhiteSpace(value) ? "legacy-v1" : value.Trim();

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

internal readonly struct LedgerCommitOutcome
{
    public enum LedgerCommitDisposition { Committed, Rejected, Proceed }

    private LedgerCommitOutcome(LedgerCommitDisposition disposition, OperationsContinuityWorkflow? workflow, OperationsWorkflowBlockerDto? blocker)
    {
        Disposition = disposition;
        Workflow = workflow;
        Blocker = blocker;
    }

    public LedgerCommitDisposition Disposition { get; }
    public OperationsContinuityWorkflow? Workflow { get; }
    public OperationsWorkflowBlockerDto? Blocker { get; }

    public static LedgerCommitOutcome Committed(OperationsContinuityWorkflow workflow) => new(LedgerCommitDisposition.Committed, workflow, null);
    public static LedgerCommitOutcome Rejected(OperationsWorkflowBlockerDto blocker) => new(LedgerCommitDisposition.Rejected, null, blocker);
    public static LedgerCommitOutcome Proceed() => new(LedgerCommitDisposition.Proceed, null, null);
}
