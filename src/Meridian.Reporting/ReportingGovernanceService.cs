using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using Meridian.Contracts.Integrity;

namespace Meridian.Reporting;

/// <summary>
/// Owns the canonical reporting lifecycle. Rendering/execution and governance are intentionally
/// separate state machines, and every governed change is optimistic, transactional, and audited.
/// </summary>
public sealed class ReportingGovernanceService
{
    private readonly IReportingGovernanceRepository _repository;
    private readonly TimeProvider _timeProvider;
    private readonly Func<string, string> _idFactory;

    public ReportingGovernanceService(
        IReportingGovernanceRepository repository,
        TimeProvider? timeProvider = null,
        Func<string, string>? idFactory = null)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _idFactory = idFactory ?? (prefix => $"{prefix}_{Guid.NewGuid():N}");
    }

    public ValueTask<GovernedReportingRun> CreateRunAsync(
        ReportingRunCreationRequest request,
        ReportingAuthorityScope authority,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(authority);

        ValidateCreationRequest(request);
        EnsureAuthority(authority, request.Scope, ReportingGovernancePermission.CreateRun);
        EnsureAccess(request.Access, authority);

        return _repository.ExecuteTransactionAsync(async (transaction, ct) =>
        {
            var existingById = await transaction.GetRunAsync(request.Scope.TenantId, request.RunId, ct);
            if (existingById is not null)
            {
                throw new ReportingGovernanceException($"Reporting run '{request.RunId}' already exists.");
            }

            var revisions = await transaction.ListRunsBySeriesAsync(
                request.Scope.TenantId,
                request.SeriesId,
                ct);

            // A series is one immutable lineage. Ordinary generation can create only its first
            // revision; later revisions require the independently approved restatement command.
            if (revisions.Count != 0)
            {
                throw new ReportingGovernanceException(
                    $"Reporting series '{request.SeriesId}' already exists; a new revision requires approved restatement governance.");
            }

            var now = _timeProvider.GetUtcNow();
            var run = new GovernedReportingRun(
                request.RunId,
                request.SeriesId,
                Revision: 1,
                request.TemplateId,
                request.TemplateVersion,
                request.Scope,
                request.Access,
                request.Snapshot,
                authority,
                now,
                RestatementOfRunId: null,
                GovernedReportingExecutionState.Queued,
                GovernedReportingState.Draft,
                Version: 1,
                Readiness: null,
                Approval: null,
                Release: null,
                AuditTrail: []);

            run = run with
            {
                AuditTrail =
                [
                    CreateAuditEntry(
                        ReportingGovernanceAuditAggregateKind.Run,
                        run.RunId,
                        aggregateVersion: 1,
                        now,
                        ReportingGovernanceAuditAction.RunCreated,
                        authority,
                        ReportingGovernancePermission.CreateRun,
                        fromExecution: null,
                        toExecution: GovernedReportingExecutionState.Queued,
                        fromGovernance: null,
                        toGovernance: GovernedReportingState.Draft,
                        fromRestatement: null,
                        toRestatement: null,
                        note: ReportingGovernanceCanonicalValidation.BuildInitialRunAuditNote(run),
                        previousHash: null)
                ]
            };

            await transaction.AddRunAsync(run, ct);
            return run;
        }, cancellationToken);
    }

    public ValueTask<GovernedReportingRun> BeginExecutionAsync(
        string runId,
        long expectedVersion,
        ReportingAuthorityScope authority,
        CancellationToken cancellationToken = default) =>
        MutateRunAsync(
            runId,
            expectedVersion,
            authority,
            ReportingGovernancePermission.ExecuteRun,
            humanRequired: false,
            cancellationToken,
            run =>
            {
                EnsureMutableDraft(run, "start execution");
                if (run.ExecutionState is not (GovernedReportingExecutionState.Queued or GovernedReportingExecutionState.Failed))
                {
                    throw InvalidState(run, "start execution from Queued or Failed");
                }

                return Mutation.ForRun(
                    run with { ExecutionState = GovernedReportingExecutionState.Running },
                    ReportingGovernanceAuditAction.ExecutionStarted);
            });

    public ValueTask<GovernedReportingRun> CompleteExecutionAsync(
        string runId,
        long expectedVersion,
        ReportingAuthorityScope authority,
        CancellationToken cancellationToken = default) =>
        MutateRunAsync(
            runId,
            expectedVersion,
            authority,
            ReportingGovernancePermission.ExecuteRun,
            humanRequired: false,
            cancellationToken,
            run =>
            {
                EnsureMutableDraft(run, "complete execution");
                if (run.ExecutionState != GovernedReportingExecutionState.Running)
                {
                    throw InvalidState(run, "complete execution from Running");
                }

                return Mutation.ForRun(
                    run with { ExecutionState = GovernedReportingExecutionState.Succeeded },
                    ReportingGovernanceAuditAction.ExecutionSucceeded);
            });

    public ValueTask<GovernedReportingRun> FailExecutionAsync(
        string runId,
        long expectedVersion,
        string failureReason,
        ReportingAuthorityScope authority,
        CancellationToken cancellationToken = default)
    {
        RequireText(failureReason, nameof(failureReason));
        return MutateRunAsync(
            runId,
            expectedVersion,
            authority,
            ReportingGovernancePermission.ExecuteRun,
            humanRequired: false,
            cancellationToken,
            run =>
            {
                EnsureMutableDraft(run, "fail execution");
                if (run.ExecutionState != GovernedReportingExecutionState.Running)
                {
                    throw InvalidState(run, "fail execution from Running");
                }

                return Mutation.ForRun(
                    run with { ExecutionState = GovernedReportingExecutionState.Failed },
                    ReportingGovernanceAuditAction.ExecutionFailed,
                    failureReason);
            });
    }

    public ValueTask<GovernedReportingRun> CancelExecutionAsync(
        string runId,
        long expectedVersion,
        string reason,
        ReportingAuthorityScope authority,
        CancellationToken cancellationToken = default)
    {
        RequireText(reason, nameof(reason));
        return MutateRunAsync(
            runId,
            expectedVersion,
            authority,
            ReportingGovernancePermission.ExecuteRun,
            humanRequired: false,
            cancellationToken,
            run =>
            {
                EnsureMutableDraft(run, "cancel execution");
                if (run.ExecutionState is not (GovernedReportingExecutionState.Queued or GovernedReportingExecutionState.Running))
                {
                    throw InvalidState(run, "cancel execution from Queued or Running");
                }

                return Mutation.ForRun(
                    run with { ExecutionState = GovernedReportingExecutionState.Cancelled },
                    ReportingGovernanceAuditAction.ExecutionCancelled,
                    reason);
            });
    }

    public ValueTask<GovernedReportingRun> ValidateAsync(
        string runId,
        long expectedVersion,
        ReportingReadinessReceipt readiness,
        ReportingAuthorityScope authority,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(readiness);
        return MutateRunAsync(
            runId,
            expectedVersion,
            authority,
            ReportingGovernancePermission.ValidateRun,
            humanRequired: false,
            cancellationToken,
            run =>
            {
                EnsureMutable(run, "validate");
                if (run.ExecutionState != GovernedReportingExecutionState.Succeeded ||
                    run.GovernanceState != GovernedReportingState.Draft)
                {
                    throw InvalidState(run, "validate only a successfully executed Draft");
                }

                ValidateReadiness(readiness, run);
                return Mutation.ForRun(
                    run with
                    {
                        GovernanceState = GovernedReportingState.Validated,
                        Readiness = readiness
                    },
                    ReportingGovernanceAuditAction.RunValidated,
                    $"readiness={readiness.ReceiptId};hash={readiness.ReceiptHash}");
            });
    }

    public ValueTask<GovernedReportingRun> SubmitAsync(
        string runId,
        long expectedVersion,
        ReportingAuthorityScope authority,
        CancellationToken cancellationToken = default) =>
        MutateRunAsync(
            runId,
            expectedVersion,
            authority,
            ReportingGovernancePermission.SubmitRun,
            humanRequired: false,
            cancellationToken,
            run =>
            {
                EnsureMutable(run, "submit for review");
                if (run.ExecutionState != GovernedReportingExecutionState.Succeeded ||
                    run.GovernanceState != GovernedReportingState.Validated ||
                    run.Readiness is null)
                {
                    throw InvalidState(run, "submit only a successfully executed and validated run");
                }

                return Mutation.ForRun(
                    run with { GovernanceState = GovernedReportingState.InReview },
                    ReportingGovernanceAuditAction.RunSubmitted);
            });

    public ValueTask<GovernedReportingRun> ApproveAsync(
        string runId,
        long expectedVersion,
        string decisionNote,
        ReportingAuthorityScope authority,
        CancellationToken cancellationToken = default)
    {
        RequireText(decisionNote, nameof(decisionNote));
        return MutateRunAsync(
            runId,
            expectedVersion,
            authority,
            ReportingGovernancePermission.ApproveRun,
            humanRequired: true,
            cancellationToken,
            run =>
            {
                EnsureMutable(run, "approve");
                if (run.GovernanceState != GovernedReportingState.InReview)
                {
                    throw InvalidState(run, "approve only an InReview run");
                }

                if (StringComparer.OrdinalIgnoreCase.Equals(run.CreationAuthority.ActorId, authority.ActorId))
                {
                    throw new ReportingGovernanceAuthorizationException(
                        "The run creator cannot approve the same run.");
                }

                if (run.Access.Mode == ReportingGovernanceAccessMode.Private
                    && StringComparer.OrdinalIgnoreCase.Equals(run.Access.OwnerPrincipalId, authority.ActorId))
                {
                    throw new ReportingGovernanceAuthorizationException(
                        "The private report owner cannot approve the same run.");
                }

                var now = _timeProvider.GetUtcNow();
                return Mutation.ForRun(
                    run with
                    {
                        GovernanceState = GovernedReportingState.Approved,
                        Approval = new ReportingApprovalReceipt(authority, now, decisionNote)
                    },
                    ReportingGovernanceAuditAction.RunApproved,
                    decisionNote,
                    occurredAtUtc: now);
            });
    }

    public ValueTask<GovernedReportingRun> ReleaseAsync(
        string runId,
        long expectedVersion,
        ReportingReleaseEvidence evidence,
        ReportingAuthorityScope authority,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        ValidateReleaseEvidence(evidence);

        return MutateRunAsync(
            runId,
            expectedVersion,
            authority,
            ReportingGovernancePermission.ReleaseRun,
            humanRequired: true,
            cancellationToken,
            run =>
            {
                EnsureMutable(run, "release");
                if (run.ExecutionState != GovernedReportingExecutionState.Succeeded ||
                    run.GovernanceState != GovernedReportingState.Approved ||
                    run.Approval is null)
                {
                    throw InvalidState(run, "release only a successfully executed and approved run");
                }

                ReportingGovernanceCanonicalValidation.ValidateFinalReleaseSnapshot(run.Snapshot);
                ReportingGovernanceCanonicalValidation.ValidateCompleteClientPackageRelease(
                    run.RunId,
                    run.Snapshot,
                    evidence.Artifacts);

                if (StringComparer.OrdinalIgnoreCase.Equals(run.Approval.Authority.ActorId, authority.ActorId))
                {
                    throw new ReportingGovernanceAuthorizationException(
                        "The run approver cannot release the same run.");
                }

                var now = _timeProvider.GetUtcNow();
                var release = new ReportingReleaseReceipt(
                    authority,
                    now,
                    evidence.ManifestId,
                    evidence.ManifestHash,
                    evidence.Artifacts,
                    evidence.EvidenceIds);
                return Mutation.ForRun(
                    run with
                    {
                        GovernanceState = GovernedReportingState.Released,
                        Release = release
                    },
                    ReportingGovernanceAuditAction.RunReleased,
                    ReportingGovernanceCanonicalValidation.BuildReleaseAuditNote(release),
                    occurredAtUtc: now);
            });
    }

    public ValueTask<ReportingRestatementRequest> RequestRestatementAsync(
        ReportingRestatementRequestCommand command,
        ReportingAuthorityScope authority,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(authority);
        RequireText(command.PredecessorRunId, nameof(command.PredecessorRunId));
        RequireText(command.Reason, nameof(command.Reason));
        ValidateChangedLines(command.ChangedLines);

        return _repository.ExecuteTransactionAsync(async (transaction, ct) =>
        {
            var predecessor = await GetRequiredRunAsync(
                transaction,
                authority.TenantId,
                command.PredecessorRunId,
                ct);

            EnsureExpectedVersion(predecessor.Version, command.ExpectedPredecessorVersion, predecessor.RunId);
            EnsureAuthority(
                authority,
                predecessor.Scope,
                ReportingGovernancePermission.RequestRestatement,
                humanRequired: true);
            EnsureAccess(predecessor.Access, authority);

            if (predecessor.GovernanceState != GovernedReportingState.Released || predecessor.Release is null)
            {
                throw InvalidState(predecessor, "request restatement only for a Released run");
            }

            var now = _timeProvider.GetUtcNow();
            var requestId = RequireGeneratedId(_idFactory("restatement"));
            var request = new ReportingRestatementRequest(
                requestId,
                predecessor.RunId,
                predecessor.SeriesId,
                predecessor.Revision,
                predecessor.Version,
                command.Reason,
                command.ChangedLines,
                authority,
                now,
                ReportingRestatementRequestState.PendingApproval,
                Version: 1,
                ApprovedBy: null,
                ApprovedAtUtc: null,
                DraftRunId: null,
                AuditTrail: [],
                RequestedChangedLines: command.ChangedLines);

            request = request with
            {
                AuditTrail =
                [
                    CreateAuditEntry(
                        ReportingGovernanceAuditAggregateKind.RestatementRequest,
                        request.RequestId,
                        aggregateVersion: 1,
                        now,
                        ReportingGovernanceAuditAction.RestatementRequested,
                        authority,
                        ReportingGovernancePermission.RequestRestatement,
                        fromExecution: null,
                        toExecution: null,
                        fromGovernance: null,
                        toGovernance: null,
                        fromRestatement: null,
                        toRestatement: ReportingRestatementRequestState.PendingApproval,
                        ReportingGovernanceCanonicalValidation.BuildRestatementRequestAuditNote(request),
                        previousHash: null)
                ]
            };

            await transaction.AddRestatementRequestAsync(authority.TenantId, request, ct);
            return request;
        }, cancellationToken);
    }

    public ValueTask<ReportingRestatementApprovalResult> ApproveRestatementAsync(
        ReportingRestatementApprovalCommand command,
        ReportingAuthorityScope authority,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(authority);
        RequireText(command.RequestId, nameof(command.RequestId));

        return _repository.ExecuteTransactionAsync(async (transaction, ct) =>
        {
            var request = await transaction.GetRestatementRequestAsync(
                authority.TenantId,
                command.RequestId,
                ct) ?? throw new ReportingGovernanceNotFoundException(
                    $"Restatement request '{command.RequestId}' was not found in the authority tenant.");

            EnsureExpectedVersion(request.Version, command.ExpectedRequestVersion, request.RequestId);
            if (request.State != ReportingRestatementRequestState.PendingApproval)
            {
                throw new ReportingGovernanceException(
                    $"Restatement request '{request.RequestId}' is already {request.State}.");
            }

            var predecessor = await GetRequiredRunAsync(
                transaction,
                authority.TenantId,
                request.PredecessorRunId,
                ct);

            EnsureAuthority(
                authority,
                predecessor.Scope,
                ReportingGovernancePermission.ApproveRestatement,
                humanRequired: true);
            EnsureAccess(predecessor.Access, authority);

            if (StringComparer.OrdinalIgnoreCase.Equals(request.RequestedBy.ActorId, authority.ActorId))
            {
                throw new ReportingGovernanceAuthorizationException(
                    "The restatement requester cannot approve the same request.");
            }

            if (predecessor.GovernanceState != GovernedReportingState.Released ||
                predecessor.Release is null ||
                predecessor.Version != request.PredecessorVersion ||
                predecessor.Revision != request.PredecessorRevision ||
                !StringComparer.Ordinal.Equals(predecessor.SeriesId, request.SeriesId))
            {
                throw new ReportingGovernanceConcurrencyException(
                    "The released predecessor no longer matches the restatement request.");
            }

            ReportingGovernanceCanonicalValidation.ValidateSnapshot(
                command.ReplacementSnapshot,
                predecessor.Scope);
            RequireText(command.ReplacementRunId, nameof(command.ReplacementRunId));
            ValidateChangedLines(command.ChangedLines);

            var revisions = await transaction.ListRunsBySeriesAsync(
                predecessor.Scope.TenantId,
                predecessor.SeriesId,
                ct);
            if (revisions.Count == 0 ||
                revisions.Any(run => !Equals(run.Scope, predecessor.Scope)) ||
                revisions.Max(static run => run.Revision) != predecessor.Revision)
            {
                throw new ReportingGovernanceConcurrencyException(
                    "The reporting series advanced or its immutable scope is inconsistent.");
            }

            var now = _timeProvider.GetUtcNow();
            var draftRunId = command.ReplacementRunId.Trim();
            if (await transaction.GetRunAsync(authority.TenantId, draftRunId, ct) is not null)
            {
                throw new ReportingGovernanceConcurrencyException(
                    $"Replacement reporting run '{draftRunId}' already exists.");
            }
            var draftRun = new GovernedReportingRun(
                draftRunId,
                predecessor.SeriesId,
                checked(predecessor.Revision + 1),
                predecessor.TemplateId,
                predecessor.TemplateVersion,
                predecessor.Scope,
                predecessor.Access,
                command.ReplacementSnapshot,
                authority,
                now,
                predecessor.RunId,
                GovernedReportingExecutionState.Queued,
                GovernedReportingState.Draft,
                Version: 1,
                Readiness: null,
                Approval: null,
                Release: null,
                AuditTrail: [],
                RestatementRequestId: request.RequestId);

            draftRun = draftRun with
            {
                AuditTrail =
                [
                    CreateAuditEntry(
                        ReportingGovernanceAuditAggregateKind.Run,
                        draftRun.RunId,
                        aggregateVersion: 1,
                        now,
                        ReportingGovernanceAuditAction.RestatementDraftCreated,
                        authority,
                        ReportingGovernancePermission.ApproveRestatement,
                        fromExecution: null,
                        toExecution: GovernedReportingExecutionState.Queued,
                        fromGovernance: null,
                        toGovernance: GovernedReportingState.Draft,
                        fromRestatement: null,
                        toRestatement: null,
                        note: ReportingGovernanceCanonicalValidation.BuildRestatementDraftAuditNote(draftRun),
                        previousHash: null)
                ]
            };

            var requestVersion = checked(request.Version + 1);
            var approvedRequest = request with
            {
                State = ReportingRestatementRequestState.Approved,
                Version = requestVersion,
                ApprovedBy = authority,
                ApprovedAtUtc = now,
                DraftRunId = draftRun.RunId,
                ChangedLines = command.ChangedLines
            };
            var requestAudit = CreateAuditEntry(
                ReportingGovernanceAuditAggregateKind.RestatementRequest,
                request.RequestId,
                requestVersion,
                now,
                ReportingGovernanceAuditAction.RestatementApproved,
                authority,
                ReportingGovernancePermission.ApproveRestatement,
                fromExecution: null,
                toExecution: null,
                fromGovernance: null,
                toGovernance: null,
                fromRestatement: request.State,
                toRestatement: ReportingRestatementRequestState.Approved,
                note: ReportingGovernanceCanonicalValidation.BuildRestatementApprovalAuditNote(approvedRequest),
                previousHash: LastHash(request.AuditTrail));

            approvedRequest = approvedRequest with
            {
                AuditTrail = SafeAudit(request.AuditTrail).Add(requestAudit)
            };

            ReportingGovernanceCanonicalValidation.ValidateRestatementBinding(
                approvedRequest,
                predecessor,
                draftRun);

            await transaction.ReplaceRestatementRequestAsync(
                authority.TenantId,
                approvedRequest,
                command.ExpectedRequestVersion,
                ct);
            await transaction.AddRunAsync(draftRun, ct);

            return new ReportingRestatementApprovalResult(approvedRequest, draftRun);
        }, cancellationToken);
    }

    private ValueTask<GovernedReportingRun> MutateRunAsync(
        string runId,
        long expectedVersion,
        ReportingAuthorityScope authority,
        ReportingGovernancePermission permission,
        bool humanRequired,
        CancellationToken cancellationToken,
        Func<GovernedReportingRun, Mutation> mutation)
    {
        RequireText(runId, nameof(runId));
        ArgumentNullException.ThrowIfNull(authority);
        ArgumentNullException.ThrowIfNull(mutation);

        return _repository.ExecuteTransactionAsync(async (transaction, ct) =>
        {
            var run = await GetRequiredRunAsync(transaction, authority.TenantId, runId, ct);
            EnsureExpectedVersion(run.Version, expectedVersion, run.RunId);
            EnsureAuthority(authority, run.Scope, permission, humanRequired);
            EnsureAccess(run.Access, authority);

            var result = mutation(run);
            var now = result.OccurredAtUtc ?? _timeProvider.GetUtcNow();
            var version = checked(run.Version + 1);
            var audit = CreateAuditEntry(
                ReportingGovernanceAuditAggregateKind.Run,
                run.RunId,
                version,
                now,
                result.Action,
                authority,
                permission,
                run.ExecutionState == result.Run.ExecutionState ? null : run.ExecutionState,
                run.ExecutionState == result.Run.ExecutionState ? null : result.Run.ExecutionState,
                run.GovernanceState == result.Run.GovernanceState ? null : run.GovernanceState,
                run.GovernanceState == result.Run.GovernanceState ? null : result.Run.GovernanceState,
                fromRestatement: null,
                toRestatement: null,
                result.Note,
                LastHash(run.AuditTrail));

            var updated = result.Run with
            {
                Version = version,
                AuditTrail = SafeAudit(run.AuditTrail).Add(audit)
            };

            await transaction.ReplaceRunAsync(updated, expectedVersion, ct);
            return updated;
        }, cancellationToken);
    }

    private static async ValueTask<GovernedReportingRun> GetRequiredRunAsync(
        IReportingGovernanceTransaction transaction,
        string tenantId,
        string runId,
        CancellationToken cancellationToken) =>
        await transaction.GetRunAsync(tenantId, runId, cancellationToken) ??
        throw new ReportingGovernanceNotFoundException(
            $"Reporting run '{runId}' was not found in the authority tenant.");

    private ReportingGovernanceAuditEntry CreateAuditEntry(
        ReportingGovernanceAuditAggregateKind aggregateKind,
        string aggregateId,
        long aggregateVersion,
        DateTimeOffset occurredAtUtc,
        ReportingGovernanceAuditAction action,
        ReportingAuthorityScope authority,
        ReportingGovernancePermission permission,
        GovernedReportingExecutionState? fromExecution,
        GovernedReportingExecutionState? toExecution,
        GovernedReportingState? fromGovernance,
        GovernedReportingState? toGovernance,
        ReportingRestatementRequestState? fromRestatement,
        ReportingRestatementRequestState? toRestatement,
        string? note,
        string? previousHash)
    {
        var entry = new ReportingGovernanceAuditEntry(
            RequireGeneratedId(_idFactory("report-audit")),
            aggregateKind,
            aggregateId,
            aggregateVersion,
            occurredAtUtc,
            action,
            authority,
            permission,
            fromExecution,
            toExecution,
            fromGovernance,
            toGovernance,
            fromRestatement,
            toRestatement,
            note,
            previousHash,
            Hash: string.Empty);

        return ReportingGovernanceAuditChain.Seal(entry);
    }

    private static void ValidateCreationRequest(ReportingRunCreationRequest request)
    {
        ReportingGovernanceCanonicalValidation.ValidateCreationRequest(request);
    }

    private static void ValidateReadiness(ReportingReadinessReceipt readiness, GovernedReportingRun run)
        => ReportingGovernanceCanonicalValidation.ValidateReadiness(readiness, run);

    private static void ValidateReleaseEvidence(ReportingReleaseEvidence evidence)
        => ReportingGovernanceCanonicalValidation.ValidateReleaseEvidence(evidence);

    private static void ValidateChangedLines(ImmutableArray<ReportingRestatementChangedLine> changedLines)
        => ReportingGovernanceCanonicalValidation.ValidateChangedLines(changedLines);

    private static void EnsureAuthority(
        ReportingAuthorityScope authority,
        ReportingOperationalScope scope,
        ReportingGovernancePermission permission,
        bool humanRequired = false)
    {
        RequireText(authority.ActorId, nameof(authority.ActorId));
        RequireText(authority.CorrelationId, nameof(authority.CorrelationId));

        if (!StringComparer.Ordinal.Equals(authority.TenantId, scope.TenantId) ||
            !StringComparer.Ordinal.Equals(authority.OrganizationId, scope.OrganizationId) ||
            !StringComparer.Ordinal.Equals(authority.CompanyId, scope.CompanyId))
        {
            throw new ReportingGovernanceAuthorizationException(
                "The command authority does not match the immutable tenant, organization, and company scope.");
        }

        if (!authority.HasPermission(permission))
        {
            throw new ReportingGovernanceAuthorizationException(
                $"The command authority lacks explicit '{permission}' permission.");
        }

        if (humanRequired && authority.Origin != ReportingCommandOrigin.HumanOperator)
        {
            throw new ReportingGovernanceAuthorizationException(
                $"'{permission}' requires a human operator authority snapshot.");
        }
    }

    private static void EnsureAccess(ReportingAccessScope access, ReportingAuthorityScope authority)
    {
        var allowed = access.Mode switch
        {
            ReportingGovernanceAccessMode.CompanyWide => true,
            ReportingGovernanceAccessMode.Private =>
                (access.AllowOwnerAccess
                    && StringComparer.OrdinalIgnoreCase.Equals(access.OwnerPrincipalId, authority.ActorId))
                || (!access.Principals.IsDefaultOrEmpty
                    && access.Principals.Any(principal =>
                        principal.Kind == ReportingAccessPrincipalKind.User
                        && authority.Matches(principal))),
            ReportingGovernanceAccessMode.Restricted =>
                access.AllowOwnerAccess
                && !string.IsNullOrWhiteSpace(access.OwnerPrincipalId)
                && StringComparer.OrdinalIgnoreCase.Equals(access.OwnerPrincipalId, authority.ActorId)
                || (!access.Principals.IsDefaultOrEmpty && access.Principals.Any(authority.Matches)),
            _ => false
        };

        if (!allowed)
        {
            throw new ReportingGovernanceAuthorizationException(
                "The command authority is not included in the immutable reporting access scope.");
        }
    }

    private static void EnsureExpectedVersion(long actual, long expected, string aggregateId)
    {
        if (expected <= 0 || actual != expected)
        {
            throw new ReportingGovernanceConcurrencyException(
                $"Aggregate '{aggregateId}' version conflict: expected {expected}, actual {actual}.");
        }
    }

    private static void EnsureMutable(GovernedReportingRun run, string operation)
    {
        if (run.GovernanceState == GovernedReportingState.Released || run.Release is not null)
        {
            throw new ReportingGovernanceException(
                $"Released run '{run.RunId}' is immutable and cannot {operation}.");
        }
    }

    private static void EnsureMutableDraft(GovernedReportingRun run, string operation)
    {
        EnsureMutable(run, operation);
        if (run.GovernanceState != GovernedReportingState.Draft)
        {
            throw InvalidState(run, $"{operation} only while governance remains Draft");
        }
    }

    private static ReportingGovernanceException InvalidState(GovernedReportingRun run, string operation) =>
        new(
            $"Cannot {operation} for run '{run.RunId}' in execution state {run.ExecutionState} " +
            $"and governance state {run.GovernanceState}.");

    private static ImmutableArray<ReportingGovernanceAuditEntry> SafeAudit(
        ImmutableArray<ReportingGovernanceAuditEntry> audit) =>
        audit.IsDefault ? [] : audit;

    private static string? LastHash(ImmutableArray<ReportingGovernanceAuditEntry> audit) =>
        audit.IsDefaultOrEmpty ? null : audit[^1].Hash;

    private static string RequireGeneratedId(string value)
    {
        RequireText(value, "generatedId");
        return value;
    }

    private static void RequireText(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ReportingGovernanceException($"'{name}' is required.");
        }
    }

    private sealed record Mutation(
        GovernedReportingRun Run,
        ReportingGovernanceAuditAction Action,
        string? Note,
        DateTimeOffset? OccurredAtUtc)
    {
        public static Mutation ForRun(
            GovernedReportingRun run,
            ReportingGovernanceAuditAction action,
            string? note = null,
            DateTimeOffset? occurredAtUtc = null) =>
            new(run, action, note, occurredAtUtc);
    }
}

/// <summary>Deterministic SHA-256 chaining for reporting lifecycle audit evidence.</summary>
public static class ReportingGovernanceAuditChain
{
    public static bool Verify(IReadOnlyList<ReportingGovernanceAuditEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (entries.Count == 0)
        {
            return false;
        }

        var aggregateKind = entries[0].AggregateKind;
        var aggregateId = entries[0].AggregateId;
        string? previousHash = null;

        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            if (entry.AggregateKind != aggregateKind ||
                !StringComparer.Ordinal.Equals(entry.AggregateId, aggregateId) ||
                entry.AggregateVersion != index + 1L ||
                !StringComparer.Ordinal.Equals(entry.PreviousHash, previousHash) ||
                string.IsNullOrWhiteSpace(entry.Hash) ||
                !StringComparer.Ordinal.Equals(entry.Hash, ComputeHash(entry)))
            {
                return false;
            }

            previousHash = entry.Hash;
        }

        return true;
    }

    internal static ReportingGovernanceAuditEntry Seal(ReportingGovernanceAuditEntry entry) =>
        entry with { Hash = ComputeHash(entry) };

    private static string ComputeHash(ReportingGovernanceAuditEntry entry)
    {
        var canonical = new StringBuilder();
        Append(canonical, entry.EventId);
        Append(canonical, (int)entry.AggregateKind);
        Append(canonical, entry.AggregateId);
        Append(canonical, entry.AggregateVersion);
        Append(canonical, entry.OccurredAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        Append(canonical, (int)entry.Action);
        Append(canonical, entry.Authority.ActorId);
        Append(canonical, entry.Authority.TenantId);
        Append(canonical, entry.Authority.OrganizationId);
        Append(canonical, entry.Authority.CompanyId);
        Append(canonical, entry.Authority.Permissions.Length);
        foreach (var permission in entry.Authority.Permissions.OrderBy(static permission => permission))
        {
            Append(canonical, (int)permission);
        }

        Append(canonical, (int)entry.Authority.Origin);
        Append(canonical, entry.Authority.CorrelationId);
        Append(canonical, entry.Authority.PrincipalIds.IsDefault
            ? 0
            : entry.Authority.PrincipalIds.Length);
        if (!entry.Authority.PrincipalIds.IsDefaultOrEmpty)
        {
            foreach (var principalId in entry.Authority.PrincipalIds.Order(StringComparer.Ordinal))
            {
                Append(canonical, principalId);
            }
        }
        Append(canonical, (int)entry.PermissionUsed);
        Append(canonical, entry.FromExecutionState is null ? null : (int)entry.FromExecutionState.Value);
        Append(canonical, entry.ToExecutionState is null ? null : (int)entry.ToExecutionState.Value);
        Append(canonical, entry.FromGovernanceState is null ? null : (int)entry.FromGovernanceState.Value);
        Append(canonical, entry.ToGovernanceState is null ? null : (int)entry.ToGovernanceState.Value);
        Append(canonical, entry.FromRestatementState is null ? null : (int)entry.FromRestatementState.Value);
        Append(canonical, entry.ToRestatementState is null ? null : (int)entry.ToRestatementState.Value);
        Append(canonical, entry.Note);
        Append(canonical, entry.PreviousHash);

        return Sha256Digest.ComputeUtf8(canonical.ToString());
    }

    private static void Append(StringBuilder target, object? value)
    {
        var text = value switch
        {
            null => null,
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString()
        };

        if (text is null)
        {
            target.Append("-1:");
            return;
        }

        target.Append(Encoding.UTF8.GetByteCount(text).ToString(CultureInfo.InvariantCulture));
        target.Append(':');
        target.Append(text);
    }
}
