using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Npgsql;

namespace Meridian.FinancialOperations.Ledger;

public interface IAccountingPostingCandidatePostService
{
    Task<PostedPostingRuleJournalCandidateResultDto> PostCandidateAsync(
        PostPostingRuleJournalCandidateRequestDto request,
        CancellationToken ct = default);
}

public sealed class AccountingPostingCandidatePostService : IAccountingPostingCandidatePostService
{
    private readonly IAccountingPostingCandidateWriteBuilder _candidateBuilder;
    private readonly ILedgerJournalStore? _journalStore;

    public AccountingPostingCandidatePostService(
        IAccountingPostingCandidateWriteBuilder candidateBuilder,
        ILedgerJournalStore? journalStore = null)
    {
        _candidateBuilder = candidateBuilder ?? throw new ArgumentNullException(nameof(candidateBuilder));
        _journalStore = journalStore;
    }

    public async Task<PostedPostingRuleJournalCandidateResultDto> PostCandidateAsync(
        PostPostingRuleJournalCandidateRequestDto request,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Candidate);

        if (request.ActionOrigin != OperationsActionOriginDto.HumanOperator)
        {
            throw new InvalidOperationException("Generated accounting posting candidates require a human-operator action origin before append.");
        }

        var journalStore = _journalStore
            ?? throw new InvalidOperationException("Generated accounting posting candidates cannot be posted because no Postgres-backed ledger journal store is configured.");
        var actor = RequireText(request.Actor, nameof(request.Actor));
        var preparer = RequireText(request.Candidate.Actor, nameof(request.Candidate.Actor));
        if (string.Equals(actor, preparer, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Generated accounting posting candidates require approval by an operator independent from the candidate preparer.");
        }

        var approvalId = RequireText(request.ApprovalId, nameof(request.ApprovalId));
        var ledgerBookId = request.Candidate.LedgerBookId
            ?? throw new ArgumentException("Generated accounting posting candidates require a ledger book id.", nameof(request));
        if (ledgerBookId == Guid.Empty)
        {
            throw new ArgumentException("Generated accounting posting candidates require a ledger book id.", nameof(request));
        }

        if (request.Candidate.AggregateId == Guid.Empty)
        {
            throw new ArgumentException("Generated accounting posting candidates require an aggregate id.", nameof(request));
        }

        if (request.Candidate.AggregateId != ledgerBookId)
        {
            throw new InvalidOperationException("Generated accounting posting candidate aggregate id must equal the target ledger book id.");
        }

        var sourceEventId = request.Candidate.SourceEventId
            ?? throw new ArgumentException("Generated accounting posting candidates require a source economic event id.", nameof(request));
        if (sourceEventId == Guid.Empty)
        {
            throw new ArgumentException("Generated accounting posting candidates require a source economic event id.", nameof(request));
        }
        EnsureApprovalEvidenceScope(request, ledgerBookId, sourceEventId);

        var ledgerBook = await journalStore.GetLedgerBookAsync(ledgerBookId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Ledger book '{ledgerBookId:D}' was not found.");
        if (ledgerBook.AccountingBasis != request.Candidate.AccountingBasis)
        {
            throw new InvalidOperationException(
                $"Generated accounting posting candidate basis '{request.Candidate.AccountingBasis}' does not match ledger book '{ledgerBook.LedgerBookId:D}' basis '{ledgerBook.AccountingBasis}'.");
        }

        var existing = await FindExistingPostingAsync(journalStore, ledgerBookId, sourceEventId, ct).ConfigureAwait(false);
        if (existing is not null)
        {
            var candidateForReplay = await _candidateBuilder
                .BuildCandidateWriteAsync(WithPostingContext(request, actor, ledgerBookId, sourceEventId), ct)
                .ConfigureAwait(false);
            return new PostedPostingRuleJournalCandidateResultDto(
                candidateForReplay.Candidate,
                BuildPostedResult(existing, ledgerBookId),
                WasReplay: true);
        }

        var candidateWrite = await _candidateBuilder
            .BuildCandidateWriteAsync(WithPostingContext(request, actor, ledgerBookId, sourceEventId), ct)
            .ConfigureAwait(false);
        var candidate = candidateWrite.Candidate;
        if (candidate.HasBlockingIssues)
        {
            throw new InvalidOperationException("Generated accounting posting candidate is blocked and cannot be posted.");
        }

        var write = candidateWrite.Write
            ?? throw new InvalidOperationException("Generated accounting posting candidate did not produce a durable ledger write.");
        var command = write.PostingCommand
            ?? throw new InvalidOperationException("Generated accounting posting candidate did not produce an accounting posting command.");
        if (command.ApprovalState != AccountingPostingApprovalStateDto.Pending)
        {
            throw new InvalidOperationException(
                $"Generated accounting posting candidates require a pending approval command before append; current approval state is '{command.ApprovalState}'.");
        }

        EnsureWriteLedgerBookScope(write, ledgerBookId);

        var period = await journalStore.GetPeriodAsync(write.PeriodId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Ledger period '{write.PeriodId:D}' was not found.");
        if (period.LedgerBookId.HasValue && period.LedgerBookId.Value != ledgerBookId)
        {
            throw new InvalidOperationException(
                $"Ledger period '{period.PeriodId:D}' does not belong to ledger book '{ledgerBookId:D}'.");
        }

        var recordedAtUtc = DateTimeOffset.UtcNow;
        var approvedCommand = command with
        {
            AggregateId = ledgerBookId,
            LedgerBookId = ledgerBookId,
            SourceEventId = sourceEventId,
            CorrelationId = ResolveCorrelationId(request.CorrelationId, command.CorrelationId, request.Candidate.CorrelationId),
            CausationId = sourceEventId,
            ApprovalState = AccountingPostingApprovalStateDto.Approved,
            ApprovalId = approvalId,
            ActionOrigin = request.ActionOrigin,
            OperatorRationale = string.IsNullOrWhiteSpace(request.ApprovalNotes)
                ? command.OperatorRationale
                : request.ApprovalNotes.Trim(),
            Evidence = MergeEvidence(command.Evidence, request.EvidenceLinks, approvalId, actor, request.ApprovalNotes, recordedAtUtc)
        };
        var approvedWrite = write with
        {
            AggregateId = ledgerBookId,
            CommandId = approvedCommand.CommandId,
            CorrelationId = approvedCommand.CorrelationId,
            SourceEventId = sourceEventId,
            LedgerBookId = ledgerBookId,
            PostingCommand = approvedCommand
        };

        EnsureAccountingPeriodAcceptsPosting(approvedWrite, period);
        try
        {
            await journalStore.AppendAsync(approvedWrite, ct).ConfigureAwait(false);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            // Cross-instance race: another horizontally-scaled writer committed the same posting
            // identity between the pre-append replay check and this append. Resolve it as an
            // idempotent replay — mirroring the single-instance check above and
            // DurableLedgerPostingTarget.PostAsync — instead of surfacing the raw unique violation.
            var raced = await FindExistingPostingAsync(journalStore, ledgerBookId, sourceEventId, ct).ConfigureAwait(false);
            if (raced is null)
            {
                throw;
            }

            return new PostedPostingRuleJournalCandidateResultDto(
                candidate with { PostingCommand = approvedCommand },
                BuildPostedResult(raced, ledgerBookId),
                WasReplay: true);
        }

        var posted = await FindExistingPostingAsync(journalStore, ledgerBookId, sourceEventId, ct).ConfigureAwait(false);
        return new PostedPostingRuleJournalCandidateResultDto(
            candidate with { PostingCommand = approvedCommand },
            posted is null
                ? BuildPostedResult(approvedWrite, ledgerBookId, recordedAtUtc)
                : BuildPostedResult(posted, ledgerBookId),
            WasReplay: false);
    }

    private static void EnsureAccountingPeriodAcceptsPosting(
        LedgerJournalEntryWrite write,
        LedgerAccountingPeriod period)
    {
        if (string.Equals(period.Status, "Open", StringComparison.Ordinal))
        {
            return;
        }

        if (string.Equals(period.Status, "SoftClosed", StringComparison.Ordinal))
        {
            if (write.PostingKind != LedgerPostingKindDto.Adjustment)
            {
                throw new LedgerValidationException(
                    $"Accounting period '{period.Label}' is soft-closed; only Adjustment postings are accepted.");
            }

            if (write.AdjustmentApproval?.Status != LedgerAdjustmentApprovalStatusDto.Approved)
            {
                throw new LedgerValidationException(
                    $"Accounting period '{period.Label}' is soft-closed; Adjustment postings require approved governance metadata.");
            }

            return;
        }

        if (string.Equals(period.Status, "HardClosed", StringComparison.Ordinal))
        {
            throw new LedgerValidationException(
                $"Accounting period '{period.Label}' is hard-closed; no postings are permitted.");
        }

        throw new LedgerValidationException(
            $"Accounting period '{period.Label}' has unsupported status '{period.Status}'.");
    }

    private static void EnsureApprovalEvidenceScope(
        PostPostingRuleJournalCandidateRequestDto request,
        Guid ledgerBookId,
        Guid sourceEventId)
    {
        var tenantId = NormalizeOptional(request.TenantId) ?? NormalizeOptional(request.Candidate.TenantId);
        var companyId = NormalizeOptional(request.CompanyId) ?? NormalizeOptional(request.Candidate.CompanyId);
        var fundProfileId = RequireText(request.Candidate.FundProfileId, nameof(request.Candidate.FundProfileId));

        if (request.EvidenceLinks.Any(link =>
                ReferencesApproval(link) &&
                ReferencesText(link, fundProfileId) &&
                ReferencesLedgerBook(link, ledgerBookId) &&
                ReferencesGuid(link, "source-event", sourceEventId) &&
                (tenantId is null || ReferencesText(link, tenantId)) &&
                (companyId is null || ReferencesText(link, companyId))))
        {
            return;
        }

        var tenantScope = tenantId is null ? string.Empty : $", tenant '{tenantId}'";
        var companyScope = companyId is null ? string.Empty : $", company '{companyId}'";
        throw new InvalidOperationException(
            $"Generated accounting posting candidate approval evidence must name approval intent, fund '{fundProfileId}'{tenantScope}{companyScope}, ledger book '{ledgerBookId:D}', and source event '{sourceEventId:D}' on the same retained artifact.");
    }

    private static void EnsureWriteLedgerBookScope(
        LedgerJournalEntryWrite write,
        Guid ledgerBookId)
    {
        if (write.LedgerBookId.HasValue && write.LedgerBookId.Value != ledgerBookId)
        {
            throw new LedgerValidationException(
                $"Generated accounting posting candidate write targets ledger book '{write.LedgerBookId.Value:D}', not approved ledger book '{ledgerBookId:D}'.");
        }

        if (!string.Equals(write.Entry.Metadata.LedgerBook, ledgerBookId.ToString("D"), StringComparison.OrdinalIgnoreCase))
        {
            throw new LedgerValidationException(
                $"Generated accounting posting candidate journal metadata ledger book '{write.Entry.Metadata.LedgerBook ?? "missing"}' does not match approved ledger book '{ledgerBookId:D}'.");
        }

        foreach (var line in write.Entry.Lines)
        {
            if (!string.Equals(line.Dimensions?.BookId, ledgerBookId.ToString("D"), StringComparison.OrdinalIgnoreCase))
            {
                throw new LedgerValidationException(
                    $"Generated accounting posting candidate line '{line.EntryId:D}' dimension book '{line.Dimensions?.BookId ?? "missing"}' does not match approved ledger book '{ledgerBookId:D}'.");
            }
        }
    }

    private static PostingRuleJournalCandidateRequestDto WithPostingContext(
        PostPostingRuleJournalCandidateRequestDto request,
        string actor,
        Guid ledgerBookId,
        Guid sourceEventId)
        => request.Candidate with
        {
            Actor = actor,
            AggregateId = ledgerBookId,
            LedgerBookId = ledgerBookId,
            SourceEventId = sourceEventId,
            CorrelationId = ResolveCorrelationId(request.CorrelationId, request.Candidate.CorrelationId),
            TenantId = NormalizeOptional(request.TenantId) ?? request.Candidate.TenantId,
            CompanyId = NormalizeOptional(request.CompanyId) ?? request.Candidate.CompanyId
        };

    private static async Task<LedgerJournalEntryRecord?> FindExistingPostingAsync(
        ILedgerJournalStore journalStore,
        Guid ledgerBookId,
        Guid sourceEventId,
        CancellationToken ct)
    {
        var records = await journalStore.GetByAggregateAsync(ledgerBookId, ct).ConfigureAwait(false);
        return records
            .Where(record => record.SourceEventId == sourceEventId)
            .OrderBy(static record => record.GlobalSequence)
            .FirstOrDefault(record => record.Entry.Metadata.LedgerBook is null ||
                                      string.Equals(record.Entry.Metadata.LedgerBook, ledgerBookId.ToString("D"), StringComparison.OrdinalIgnoreCase));
    }

    private static PostedLedgerJournalEntryResultDto BuildPostedResult(
        LedgerJournalEntryRecord record,
        Guid ledgerBookId)
        => new(
            record.Entry.JournalEntryId,
            ledgerBookId,
            record.AccountingBasis,
            record.PeriodId,
            record.AggregateId,
            record.CommandId,
            record.SourceEventId,
            record.CorrelationId,
            record.GlobalSequence,
            record.CreatedAt,
            record.Entry.Metadata.IdempotencyKey);

    private static PostedLedgerJournalEntryResultDto BuildPostedResult(
        LedgerJournalEntryWrite write,
        Guid ledgerBookId,
        DateTimeOffset postedAtUtc)
        => new(
            write.Entry.JournalEntryId,
            ledgerBookId,
            write.AccountingBasis,
            write.PeriodId,
            write.AggregateId,
            write.CommandId,
            write.SourceEventId,
            write.CorrelationId,
            PostedAtUtc: postedAtUtc,
            IdempotencyKey: write.PostingCommand?.IdempotencyKey ?? write.Entry.Metadata.IdempotencyKey);

    private static IReadOnlyList<AccountingPostingEvidenceReferenceDto> MergeEvidence(
        IReadOnlyList<AccountingPostingEvidenceReferenceDto> existing,
        IReadOnlyList<string> approvalEvidenceLinks,
        string approvalId,
        string actor,
        string? approvalNotes,
        DateTimeOffset recordedAtUtc)
    {
        var evidence = new List<AccountingPostingEvidenceReferenceDto>(existing);
        foreach (var link in approvalEvidenceLinks.Select(NormalizeOptional).Where(static link => link is not null).Cast<string>())
        {
            evidence.Add(new AccountingPostingEvidenceReferenceDto(
                EvidenceId: link,
                Uri: link,
                Kind: AccountingPostingEvidenceKindDto.Approval,
                SourceSystem: "FinancialOperations",
                RetainedAtUtc: recordedAtUtc,
                RetainedBy: actor));
        }

        evidence.Add(new AccountingPostingEvidenceReferenceDto(
            EvidenceId: approvalId,
            Uri: $"approval://accounting-posting/{Uri.EscapeDataString(approvalId)}",
            Kind: AccountingPostingEvidenceKindDto.Approval,
            SourceSystem: "FinancialOperations",
            RetainedAtUtc: recordedAtUtc,
            RetainedBy: actor,
            Description: NormalizeOptional(approvalNotes)));
        return evidence
            .GroupBy(static item => item.EvidenceId, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.Last())
            .ToArray();
    }

    private static Guid? ResolveCorrelationId(string? requested, params Guid?[] fallback)
    {
        if (!string.IsNullOrWhiteSpace(requested) && Guid.TryParse(requested, out var parsed) && parsed != Guid.Empty)
        {
            return parsed;
        }

        return fallback.FirstOrDefault(static item => item.HasValue && item.Value != Guid.Empty);
    }

    private static string RequireText(string? value, string parameterName)
        => string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException($"{parameterName} is required.", parameterName)
            : value.Trim();

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool ReferencesApproval(string? reference)
        => !string.IsNullOrWhiteSpace(reference) &&
           (reference.Contains("approval", StringComparison.OrdinalIgnoreCase) ||
            reference.Contains("approved", StringComparison.OrdinalIgnoreCase) ||
            reference.Contains("sign-off", StringComparison.OrdinalIgnoreCase) ||
            reference.Contains("signoff", StringComparison.OrdinalIgnoreCase) ||
            reference.Contains("review", StringComparison.OrdinalIgnoreCase));

    private static bool ReferencesText(string? reference, string value)
        => !string.IsNullOrWhiteSpace(reference) &&
           !string.IsNullOrWhiteSpace(value) &&
           reference.Contains(value.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool ReferencesLedgerBook(string? reference, Guid ledgerBookId)
        => ReferencesGuid(reference, "ledger-book", ledgerBookId) ||
           ReferencesGuid(reference, "book", ledgerBookId) ||
           ReferencesGuid(reference, "ledgerBookId", ledgerBookId);

    private static bool ReferencesGuid(string? reference, string label, Guid value)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return false;
        }

        var dashed = value.ToString("D");
        var compact = value.ToString("N");
        return ReferencesScopedValue(reference, $"{label}:", dashed) ||
               ReferencesScopedValue(reference, $"{label}/", dashed) ||
               ReferencesScopedValue(reference, $"{label}=", dashed) ||
               ReferencesScopedValue(reference, $"{label}:", compact) ||
               ReferencesScopedValue(reference, $"{label}/", compact) ||
               ReferencesScopedValue(reference, $"{label}=", compact);
    }

    private static bool ReferencesScopedValue(string reference, string prefix, string value)
    {
        var searchIndex = 0;
        while (searchIndex < reference.Length)
        {
            var prefixIndex = reference.IndexOf(prefix, searchIndex, StringComparison.OrdinalIgnoreCase);
            if (prefixIndex < 0)
            {
                return false;
            }

            var valueIndex = prefixIndex + prefix.Length;
            if (reference.Length >= valueIndex + value.Length &&
                string.Compare(reference, valueIndex, value, 0, value.Length, StringComparison.OrdinalIgnoreCase) == 0 &&
                IsEvidenceTokenBoundary(reference, valueIndex + value.Length))
            {
                return true;
            }

            searchIndex = valueIndex;
        }

        return false;
    }

    private static bool IsEvidenceTokenBoundary(string reference, int index)
        => index >= reference.Length ||
           reference[index] is '/' or '?' or '&' or '#' or ';' or ',' or ')' or ']' or '}' or ' ' or '\t' or '\r' or '\n';
}
