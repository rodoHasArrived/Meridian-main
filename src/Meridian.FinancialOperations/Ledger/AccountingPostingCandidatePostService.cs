using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.Ledger;
using Meridian.Storage.Ledger;

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
        if (command.ApprovalState == AccountingPostingApprovalStateDto.Rejected)
        {
            throw new InvalidOperationException("Rejected accounting posting candidates cannot be appended.");
        }

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

        await journalStore.AppendAsync(approvedWrite, ct).ConfigureAwait(false);
        var posted = await FindExistingPostingAsync(journalStore, ledgerBookId, sourceEventId, ct).ConfigureAwait(false);
        return new PostedPostingRuleJournalCandidateResultDto(
            candidate with { PostingCommand = approvedCommand },
            posted is null
                ? BuildPostedResult(approvedWrite, ledgerBookId, recordedAtUtc)
                : BuildPostedResult(posted, ledgerBookId),
            WasReplay: false);
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
}
