using Meridian.Contracts.Workstation;
using Meridian.Domain.Reconciliation;
using Meridian.Infrastructure.Reconciliation;

namespace Meridian.FinancialOperations.Reconciliation;

/// <summary>
/// Commits one versioned decision for a statement break and its paired case, then projects the
/// retained after-images through the existing source stores. The committed authority can always
/// resume an interrupted projection.
/// </summary>
public sealed class StatementBreakDispositionService : IStatementBreakDispositionService
{
    private const int MaximumIdentityLength = 256;
    private const int MaximumRationaleLength = 4096;
    private const int MaximumEvidenceLinkLength = 2048;
    private const int MaximumEvidenceLinks = 100;

    private readonly IReconciliationBreakStore _breakStore;
    private readonly IReconciliationCaseStore _caseStore;
    private readonly IStatementBreakDispositionTransactionStore _transactionStore;
    private readonly TimeProvider _timeProvider;

    public StatementBreakDispositionService(
        IReconciliationBreakStore breakStore,
        IReconciliationCaseStore caseStore,
        IStatementBreakDispositionTransactionStore transactionStore,
        TimeProvider? timeProvider = null)
    {
        _breakStore = breakStore ?? throw new ArgumentNullException(nameof(breakStore));
        _caseStore = caseStore ?? throw new ArgumentNullException(nameof(caseStore));
        _transactionStore = transactionStore ?? throw new ArgumentNullException(nameof(transactionStore));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public Task<StatementBreakDispositionResult> DispositionAsync(
        StatementBreakDispositionCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var normalized = Normalize(command, out var validationError);
        if (normalized is null)
        {
            return Task.FromResult(CreateRejectedResult(command, validationError!));
        }

        var inputHash = StatementBreakDispositionHashing.HashCanonical(new CanonicalCommandInput(
            normalized.BreakId,
            normalized.ExpectedVersion,
            normalized.CommandId,
            normalized.Disposition,
            normalized.Actor,
            normalized.Rationale,
            normalized.EvidenceLinks,
            normalized.SupersedingBreakId));

        return _transactionStore.ExecuteExclusiveAsync(
            async (session, sessionCancellationToken) =>
            {
                var receipt = session.Snapshot.CommandReceipts.FirstOrDefault(item =>
                    string.Equals(item.CommandId, normalized.CommandId, StringComparison.Ordinal));
                if (receipt is not null)
                {
                    var retainedTransaction = GetTransaction(session.Snapshot, receipt.TransactionId);
                    if (!string.Equals(receipt.InputHashSha256, inputHash, StringComparison.Ordinal))
                    {
                        return CreateResult(
                            session.Snapshot,
                            retainedTransaction,
                            StatementBreakDispositionOutcome.CommandConflict,
                            $"Command id '{normalized.CommandId}' is already bound to different disposition input.");
                    }

                    if (retainedTransaction.State == StatementBreakDispositionTransactionState.Completed)
                    {
                        return CreateResult(
                            session.Snapshot,
                            retainedTransaction,
                            StatementBreakDispositionOutcome.IdempotentReplay);
                    }

                    return await MaterializeAndCompleteAsync(
                        session,
                        retainedTransaction,
                        StatementBreakDispositionOutcome.Resumed)
                        .ConfigureAwait(false);
                }

                var latestTransaction = session.Snapshot.Transactions
                    .Where(item => string.Equals(item.BreakId, normalized.BreakId, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(static item => item.Version)
                    .FirstOrDefault();
                if (latestTransaction is not null)
                {
                    return CreateResult(
                        session.Snapshot,
                        latestTransaction,
                        StatementBreakDispositionOutcome.VersionConflict,
                        $"Statement break '{normalized.BreakId}' is at version {latestTransaction.Version}; expected version {normalized.ExpectedVersion}.");
                }

                var breakRecord = await _breakStore
                    .GetAsync(normalized.BreakId, sessionCancellationToken)
                    .ConfigureAwait(false);
                if (breakRecord is null)
                {
                    return CreateFailureResult(
                        normalized,
                        StatementBreakDispositionOutcome.NotFound,
                        normalized.ExpectedVersion,
                        $"Statement break '{normalized.BreakId}' was not found.");
                }

                var expectedCaseId = $"case:{breakRecord.BreakId}";
                var reconciliationCase = await _caseStore
                    .GetAsync(expectedCaseId, sessionCancellationToken)
                    .ConfigureAwait(false);
                if (reconciliationCase is null)
                {
                    return CreateFailureResult(
                        normalized,
                        StatementBreakDispositionOutcome.NotFound,
                        breakRecord.Version,
                        $"Paired reconciliation case '{expectedCaseId}' was not found.",
                        breakRecord: breakRecord);
                }

                var pairError = ValidatePair(normalized, breakRecord, reconciliationCase);
                if (pairError is not null)
                {
                    var outcome = breakRecord.Version != normalized.ExpectedVersion ||
                                  reconciliationCase.Version != normalized.ExpectedVersion
                        ? StatementBreakDispositionOutcome.VersionConflict
                        : StatementBreakDispositionOutcome.Rejected;
                    return CreateFailureResult(
                        normalized,
                        outcome,
                        Math.Max(breakRecord.Version, reconciliationCase.Version),
                        pairError,
                        reconciliationCase.CaseId,
                        breakRecord,
                        reconciliationCase);
                }

                var now = _timeProvider.GetUtcNow();
                var transactionId = Guid.NewGuid().ToString("N");
                var version = normalized.ExpectedVersion + 1;
                var evidenceHash = StatementBreakDispositionHashing.HashCanonical(normalized.EvidenceLinks);
                var audit = CreateAuditEntry(
                    session.Snapshot,
                    transactionId,
                    normalized,
                    reconciliationCase.CaseId,
                    version,
                    now);
                var breakAfter = BuildBreakAfter(
                    breakRecord,
                    normalized,
                    transactionId,
                    version,
                    evidenceHash,
                    now);
                var caseAfter = BuildCaseAfter(
                    reconciliationCase,
                    normalized,
                    transactionId,
                    version,
                    audit,
                    now);
                var transaction = new StatementBreakDispositionTransaction(
                    transactionId,
                    normalized.CommandId,
                    inputHash,
                    normalized.BreakId,
                    reconciliationCase.CaseId,
                    normalized.ExpectedVersion,
                    version,
                    normalized.Disposition,
                    normalized.Actor,
                    normalized.Rationale,
                    normalized.EvidenceLinks,
                    evidenceHash,
                    normalized.SupersedingBreakId,
                    breakAfter,
                    caseAfter,
                    StatementBreakDispositionTransactionState.Prepared,
                    now,
                    now,
                    CompletedAtUtc: null,
                    ProjectionAttemptCount: 0,
                    LastProjectionAttemptAtUtc: null,
                    LastError: null);
                var receiptToRetain = new StatementBreakDispositionCommandReceipt(
                    normalized.CommandId,
                    normalized.BreakId,
                    transactionId,
                    inputHash,
                    normalized.ExpectedVersion,
                    now);
                var preparedSnapshot = session.Snapshot with
                {
                    Transactions = session.Snapshot.Transactions.Concat([transaction]).ToArray(),
                    CommandReceipts = session.Snapshot.CommandReceipts.Concat([receiptToRetain]).ToArray(),
                    AuditHistory = session.Snapshot.AuditHistory.Concat([audit]).ToArray(),
                    ContentHashSha256 = null
                };

                // This atomic replacement is the disposition commit. Caller cancellation remains
                // valid until this point; all work after it is driven from retained after-images.
                await session.SaveAsync(preparedSnapshot, sessionCancellationToken).ConfigureAwait(false);
                return await MaterializeAndCompleteAsync(
                    session,
                    transaction,
                    StatementBreakDispositionOutcome.Applied)
                    .ConfigureAwait(false);
            },
            cancellationToken);
    }

    public Task<int> ResumePendingAsync(CancellationToken cancellationToken = default)
        => _transactionStore.ExecuteExclusiveAsync(
            async (session, sessionCancellationToken) =>
            {
                var pendingTransactionIds = session.Snapshot.Transactions
                    .Where(static item => item.State != StatementBreakDispositionTransactionState.Completed)
                    .OrderBy(static item => item.PreparedAtUtc)
                    .Select(static item => item.TransactionId)
                    .ToArray();
                var completedCount = 0;

                foreach (var transactionId in pendingTransactionIds)
                {
                    sessionCancellationToken.ThrowIfCancellationRequested();
                    var transaction = GetTransaction(session.Snapshot, transactionId);
                    var result = await MaterializeAndCompleteAsync(
                        session,
                        transaction,
                        StatementBreakDispositionOutcome.Resumed)
                        .ConfigureAwait(false);
                    if (result.Outcome == StatementBreakDispositionOutcome.Resumed)
                    {
                        completedCount++;
                    }
                }

                return completedCount;
            },
            cancellationToken);

    public async Task<IReadOnlyList<StatementBreakDispositionAuditEntry>> GetAuditHistoryAsync(
        string breakId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(breakId);
        var snapshot = await _transactionStore.ReadAsync(cancellationToken).ConfigureAwait(false);
        return GetAuditHistory(snapshot, breakId.Trim());
    }

    private async Task<StatementBreakDispositionResult> MaterializeAndCompleteAsync(
        IStatementBreakDispositionTransactionSession session,
        StatementBreakDispositionTransaction transaction,
        StatementBreakDispositionOutcome successOutcome)
    {
        // The authority is already committed. Projection and checkpoint persistence deliberately
        // ignore request cancellation so cancellation cannot turn a committed decision into an
        // ambiguous client-visible failure.
        var now = _timeProvider.GetUtcNow();
        try
        {
            transaction = GetTransaction(session.Snapshot, transaction.TransactionId) with
            {
                ProjectionAttemptCount = transaction.ProjectionAttemptCount + 1,
                LastProjectionAttemptAtUtc = now,
                UpdatedAtUtc = now,
                LastError = null
            };
            await SaveTransactionAsync(session, transaction).ConfigureAwait(false);

            if (transaction.State == StatementBreakDispositionTransactionState.Prepared)
            {
                await MaterializeBreakAsync(transaction).ConfigureAwait(false);
                transaction = transaction with
                {
                    State = StatementBreakDispositionTransactionState.BreakApplied,
                    UpdatedAtUtc = _timeProvider.GetUtcNow(),
                    LastError = null
                };
                await SaveTransactionAsync(session, transaction).ConfigureAwait(false);
            }

            if (transaction.State == StatementBreakDispositionTransactionState.BreakApplied)
            {
                await MaterializeCaseAsync(transaction).ConfigureAwait(false);
                transaction = transaction with
                {
                    State = StatementBreakDispositionTransactionState.CaseApplied,
                    UpdatedAtUtc = _timeProvider.GetUtcNow(),
                    LastError = null
                };
                await SaveTransactionAsync(session, transaction).ConfigureAwait(false);
            }

            if (transaction.State == StatementBreakDispositionTransactionState.CaseApplied)
            {
                var completedAtUtc = _timeProvider.GetUtcNow();
                transaction = transaction with
                {
                    State = StatementBreakDispositionTransactionState.Completed,
                    UpdatedAtUtc = completedAtUtc,
                    CompletedAtUtc = completedAtUtc,
                    LastError = null
                };
                await SaveTransactionAsync(session, transaction).ConfigureAwait(false);
            }

            return CreateResult(session.Snapshot, transaction, successOutcome);
        }
        catch (Exception ex) when (IsRecoverableProjectionFailure(ex))
        {
            var error = $"Disposition committed, but paired-record projection is pending recovery: {ex.Message}";
            try
            {
                var retained = GetTransaction(session.Snapshot, transaction.TransactionId);
                retained = retained with
                {
                    UpdatedAtUtc = _timeProvider.GetUtcNow(),
                    LastError = error
                };
                await SaveTransactionAsync(session, retained).ConfigureAwait(false);
                transaction = retained;
            }
            catch (Exception checkpointException) when (IsRecoverableProjectionFailure(checkpointException))
            {
                error = $"{error} Recovery checkpoint could not be updated: {checkpointException.Message}";
            }

            return CreateResult(
                session.Snapshot,
                transaction,
                StatementBreakDispositionOutcome.RecoveryPending,
                error);
        }
    }

    private async Task MaterializeBreakAsync(StatementBreakDispositionTransaction transaction)
    {
        var current = await _breakStore
            .GetAsync(transaction.BreakId, CancellationToken.None)
            .ConfigureAwait(false);
        if (IsApplied(current, transaction))
        {
            return;
        }
        if (current is not null && current.Version >= transaction.Version)
        {
            throw new InvalidDataException(
                $"Statement break '{transaction.BreakId}' has incompatible version {current.Version} while transaction '{transaction.TransactionId}' requires version {transaction.Version}.");
        }

        await _breakStore
            .WriteAsync([transaction.BreakAfter], CancellationToken.None)
            .ConfigureAwait(false);
    }

    private async Task MaterializeCaseAsync(StatementBreakDispositionTransaction transaction)
    {
        var current = await _caseStore
            .GetAsync(transaction.CaseId, CancellationToken.None)
            .ConfigureAwait(false);
        if (IsApplied(current, transaction))
        {
            return;
        }
        if (current is not null && current.Version >= transaction.Version)
        {
            throw new InvalidDataException(
                $"Reconciliation case '{transaction.CaseId}' has incompatible version {current.Version} while transaction '{transaction.TransactionId}' requires version {transaction.Version}.");
        }

        await _caseStore.SaveAsync(transaction.CaseAfter, CancellationToken.None).ConfigureAwait(false);
    }

    private static bool IsApplied(
        ReconciliationBreakRecord? current,
        StatementBreakDispositionTransaction transaction)
        => current is not null &&
           current.Version == transaction.Version &&
           string.Equals(current.DispositionTransactionId, transaction.TransactionId, StringComparison.Ordinal);

    private static bool IsApplied(
        ReconciliationCase? current,
        StatementBreakDispositionTransaction transaction)
        => current is not null &&
           current.Version == transaction.Version &&
           string.Equals(current.DispositionTransactionId, transaction.TransactionId, StringComparison.Ordinal);

    private static Task SaveTransactionAsync(
        IStatementBreakDispositionTransactionSession session,
        StatementBreakDispositionTransaction transaction)
    {
        var found = false;
        var transactions = session.Snapshot.Transactions.Select(item =>
        {
            if (!string.Equals(item.TransactionId, transaction.TransactionId, StringComparison.Ordinal))
            {
                return item;
            }

            found = true;
            return transaction;
        }).ToArray();
        if (!found)
        {
            throw new InvalidDataException(
                $"Disposition transaction '{transaction.TransactionId}' is missing from the authority snapshot.");
        }

        return session.SaveAsync(
            session.Snapshot with
            {
                Transactions = transactions,
                ContentHashSha256 = null
            },
            CancellationToken.None);
    }

    private static StatementBreakDispositionAuditEntry CreateAuditEntry(
        StatementBreakDispositionTransactionSnapshot snapshot,
        string transactionId,
        NormalizedCommand command,
        string caseId,
        long version,
        DateTimeOffset occurredAtUtc)
    {
        var previous = snapshot.AuditHistory.LastOrDefault();
        var entry = new StatementBreakDispositionAuditEntry(
            Guid.NewGuid().ToString("N"),
            (previous?.Sequence ?? 0) + 1,
            transactionId,
            command.CommandId,
            command.BreakId,
            caseId,
            version,
            command.Disposition,
            command.Actor,
            command.Rationale,
            command.EvidenceLinks,
            occurredAtUtc,
            previous?.EntryHash,
            EntryHash: string.Empty);
        return entry with
        {
            EntryHash = StatementBreakDispositionHashing.ComputeAuditEntryHash(entry)
        };
    }

    private static ReconciliationBreakRecord BuildBreakAfter(
        ReconciliationBreakRecord current,
        NormalizedCommand command,
        string transactionId,
        long version,
        string evidenceHash,
        DateTimeOffset disposedAtUtc)
        => current with
        {
            Status = ToTerminalStatus(command.Disposition),
            Version = version,
            Disposition = command.Disposition.ToString(),
            DispositionActor = command.Actor,
            DispositionRationale = command.Rationale,
            DispositionEvidenceLinks = command.EvidenceLinks,
            DispositionEvidenceHash = evidenceHash,
            DisposedAtUtc = disposedAtUtc,
            DispositionTransactionId = transactionId,
            SupersedingBreakId = command.SupersedingBreakId
        };

    private static ReconciliationCase BuildCaseAfter(
        ReconciliationCase current,
        NormalizedCommand command,
        string transactionId,
        long version,
        StatementBreakDispositionAuditEntry audit,
        DateTimeOffset disposedAtUtc)
    {
        var terminalStatus = ToTerminalStatus(command.Disposition);
        var evidenceReferences = current.EvidenceReferences
            .Concat(command.EvidenceLinks)
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var historyEntry = new ReconciliationCaseHistoryEntry(
            disposedAtUtc,
            current.Status,
            terminalStatus,
            command.Rationale)
        {
            Actor = command.Actor,
            EvidenceId = command.EvidenceLinks[0]
        };
        var auditEvent = new ReconciliationCaseAuditEvent(
            audit.AuditId,
            "StatementBreakDispositionCommitted",
            disposedAtUtc,
            command.Actor,
            $"Statement break and paired case disposition committed as {terminalStatus}.",
            command.EvidenceLinks,
            command.Rationale,
            transactionId,
            version,
            audit.PreviousHash,
            audit.EntryHash);
        var decisionNote = new ReconciliationCaseDecisionNote(
            $"decision:{transactionId}",
            command.Actor,
            disposedAtUtc,
            command.Rationale,
            command.EvidenceLinks);

        return current with
        {
            BreakId = command.BreakId,
            Status = terminalStatus,
            Rationale = command.Rationale,
            Version = version,
            Disposition = command.Disposition.ToString(),
            DispositionTransactionId = transactionId,
            LastUpdatedAtUtc = disposedAtUtc,
            LastUpdatedBy = command.Actor,
            EvidenceReferences = evidenceReferences,
            History = current.History.Concat([historyEntry]).ToArray(),
            AuditEvents = current.AuditEvents.Concat([auditEvent]).ToArray(),
            DecisionNotes = current.DecisionNotes.Concat([decisionNote]).ToArray(),
            Resolution = new ReconciliationResolutionMetadata(
                command.Disposition.ToString().ToLowerInvariant(),
                command.Rationale,
                command.Actor,
                disposedAtUtc)
        };
    }

    private static string? ValidatePair(
        NormalizedCommand command,
        ReconciliationBreakRecord breakRecord,
        ReconciliationCase reconciliationCase)
    {
        if (!string.Equals(breakRecord.ImportId, reconciliationCase.ImportId, StringComparison.OrdinalIgnoreCase))
        {
            return "The statement break and reconciliation case do not share the same import identity.";
        }
        if (!string.IsNullOrWhiteSpace(reconciliationCase.BreakId) &&
            !string.Equals(reconciliationCase.BreakId, breakRecord.BreakId, StringComparison.OrdinalIgnoreCase))
        {
            return "The reconciliation case is paired to a different statement break.";
        }
        if (breakRecord.Version != command.ExpectedVersion || reconciliationCase.Version != command.ExpectedVersion)
        {
            return $"The paired break/case version is {Math.Max(breakRecord.Version, reconciliationCase.Version)}; expected version {command.ExpectedVersion}.";
        }
        if (!string.Equals(breakRecord.Status, "Open", StringComparison.OrdinalIgnoreCase))
        {
            return $"Statement break '{breakRecord.BreakId}' is already in terminal or non-open status '{breakRecord.Status}'.";
        }
        if (IsTerminalCaseStatus(reconciliationCase.Status))
        {
            return $"Reconciliation case '{reconciliationCase.CaseId}' is already in terminal status '{reconciliationCase.Status}'.";
        }

        return null;
    }

    private static bool IsTerminalCaseStatus(string status)
        => string.Equals(status.Trim(), "Resolved", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(status.Trim(), "Waived", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(status.Trim(), "Superseded", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(status.Trim(), "Closed", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(status.Trim(), "Dismissed", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(status.Trim(), "SignedOff", StringComparison.OrdinalIgnoreCase);

    private static string ToTerminalStatus(ReconciliationBreakDispositionDto disposition)
        => disposition switch
        {
            ReconciliationBreakDispositionDto.Resolved => "Resolved",
            ReconciliationBreakDispositionDto.Waived => "Waived",
            ReconciliationBreakDispositionDto.Superseded => "Superseded",
            _ => throw new ArgumentOutOfRangeException(nameof(disposition), disposition, "Unsupported statement break disposition.")
        };

    private static NormalizedCommand? Normalize(
        StatementBreakDispositionCommand command,
        out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(command.BreakId) || command.BreakId.Trim().Length > MaximumIdentityLength)
        {
            error = $"Break id is required and must not exceed {MaximumIdentityLength} characters.";
            return null;
        }
        if (command.ExpectedVersion < 0 || command.ExpectedVersion == long.MaxValue)
        {
            error = "Expected version must be non-negative and leave room for the next version.";
            return null;
        }
        if (string.IsNullOrWhiteSpace(command.CommandId) || command.CommandId.Trim().Length > MaximumIdentityLength)
        {
            error = $"Command id is required and must not exceed {MaximumIdentityLength} characters.";
            return null;
        }
        if (!Enum.IsDefined(command.Disposition))
        {
            error = $"Unsupported statement break disposition '{command.Disposition}'.";
            return null;
        }
        if (string.IsNullOrWhiteSpace(command.Actor) || command.Actor.Trim().Length > MaximumIdentityLength)
        {
            error = $"Authenticated actor is required and must not exceed {MaximumIdentityLength} characters.";
            return null;
        }
        if (string.IsNullOrWhiteSpace(command.Rationale) || command.Rationale.Trim().Length > MaximumRationaleLength)
        {
            error = $"Rationale is required and must not exceed {MaximumRationaleLength} characters.";
            return null;
        }
        if (command.EvidenceLinks is null)
        {
            error = "At least one evidence link is required.";
            return null;
        }

        var evidenceLinks = command.EvidenceLinks
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Select(static item => item.Trim())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static item => item, StringComparer.Ordinal)
            .ToArray();
        if (evidenceLinks.Length == 0)
        {
            error = "At least one evidence link is required.";
            return null;
        }
        if (evidenceLinks.Length > MaximumEvidenceLinks ||
            evidenceLinks.Any(static item => item.Length > MaximumEvidenceLinkLength))
        {
            error = $"Evidence is limited to {MaximumEvidenceLinks} links of at most {MaximumEvidenceLinkLength} characters each.";
            return null;
        }

        var supersedingBreakId = string.IsNullOrWhiteSpace(command.SupersedingBreakId)
            ? null
            : command.SupersedingBreakId.Trim();
        if (supersedingBreakId is { Length: > MaximumIdentityLength })
        {
            error = $"Superseding break id must not exceed {MaximumIdentityLength} characters.";
            return null;
        }
        if (command.Disposition == ReconciliationBreakDispositionDto.Superseded)
        {
            if (supersedingBreakId is null)
            {
                error = "A superseding break id is required for a Superseded disposition.";
                return null;
            }
            if (string.Equals(supersedingBreakId, command.BreakId.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                error = "A statement break cannot supersede itself.";
                return null;
            }
        }
        else if (supersedingBreakId is not null)
        {
            error = "A superseding break id is only valid for a Superseded disposition.";
            return null;
        }

        return new NormalizedCommand(
            command.BreakId.Trim(),
            command.ExpectedVersion,
            command.CommandId.Trim(),
            command.Disposition,
            command.Actor.Trim(),
            command.Rationale.Trim(),
            evidenceLinks,
            supersedingBreakId);
    }

    private static StatementBreakDispositionResult CreateRejectedResult(
        StatementBreakDispositionCommand command,
        string error)
        => new(
            StatementBreakDispositionOutcome.Rejected,
            command.BreakId?.Trim() ?? string.Empty,
            CaseId: null,
            TransactionId: null,
            command.CommandId?.Trim() ?? string.Empty,
            Math.Max(0, command.ExpectedVersion),
            Enum.IsDefined(command.Disposition) ? command.Disposition : null,
            string.IsNullOrWhiteSpace(command.Actor) ? null : command.Actor.Trim(),
            string.IsNullOrWhiteSpace(command.Rationale) ? null : command.Rationale.Trim(),
            command.EvidenceLinks,
            DisposedAtUtc: null,
            Break: null,
            Case: null,
            AuditHistory: null,
            error);

    private static StatementBreakDispositionResult CreateFailureResult(
        NormalizedCommand command,
        StatementBreakDispositionOutcome outcome,
        long version,
        string error,
        string? caseId = null,
        ReconciliationBreakRecord? breakRecord = null,
        ReconciliationCase? reconciliationCase = null)
        => new(
            outcome,
            command.BreakId,
            caseId,
            TransactionId: null,
            command.CommandId,
            version,
            command.Disposition,
            command.Actor,
            command.Rationale,
            command.EvidenceLinks,
            DisposedAtUtc: null,
            breakRecord,
            reconciliationCase,
            AuditHistory: null,
            error);

    private static StatementBreakDispositionResult CreateResult(
        StatementBreakDispositionTransactionSnapshot snapshot,
        StatementBreakDispositionTransaction transaction,
        StatementBreakDispositionOutcome outcome,
        string? error = null)
        => new(
            outcome,
            transaction.BreakId,
            transaction.CaseId,
            transaction.TransactionId,
            transaction.CommandId,
            transaction.Version,
            transaction.Disposition,
            transaction.Actor,
            transaction.Rationale,
            transaction.EvidenceLinks,
            transaction.BreakAfter.DisposedAtUtc,
            transaction.BreakAfter,
            transaction.CaseAfter,
            GetAuditHistory(snapshot, transaction.BreakId),
            error);

    private static IReadOnlyList<StatementBreakDispositionAuditEntry> GetAuditHistory(
        StatementBreakDispositionTransactionSnapshot snapshot,
        string breakId)
        => snapshot.AuditHistory
            .Where(item => string.Equals(item.BreakId, breakId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(static item => item.Sequence)
            .ToArray();

    private static StatementBreakDispositionTransaction GetTransaction(
        StatementBreakDispositionTransactionSnapshot snapshot,
        string transactionId)
        => snapshot.Transactions.SingleOrDefault(item =>
               string.Equals(item.TransactionId, transactionId, StringComparison.Ordinal))
           ?? throw new InvalidDataException(
               $"Disposition transaction '{transactionId}' is missing from the authority snapshot.");

    private static bool IsRecoverableProjectionFailure(Exception exception)
        => exception is not OutOfMemoryException and
           not StackOverflowException and
           not AccessViolationException;

    private sealed record NormalizedCommand(
        string BreakId,
        long ExpectedVersion,
        string CommandId,
        ReconciliationBreakDispositionDto Disposition,
        string Actor,
        string Rationale,
        IReadOnlyList<string> EvidenceLinks,
        string? SupersedingBreakId);

    private sealed record CanonicalCommandInput(
        string BreakId,
        long ExpectedVersion,
        string CommandId,
        ReconciliationBreakDispositionDto Disposition,
        string Actor,
        string Rationale,
        IReadOnlyList<string> EvidenceLinks,
        string? SupersedingBreakId);
}
