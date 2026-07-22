using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.PortfolioRecords.FundAccounts;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.Api;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Operations;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Services;
using Meridian.Contracts.Workstation;
using Meridian.FSharp.Ledger;
using Meridian.ProviderSdk;
using Meridian.Storage.Archival;
using Meridian.Storage.SecurityMaster;
using Meridian.Strategies.Services;
using Microsoft.Extensions.Logging;

namespace Meridian.Ui.Shared.Services;

public sealed partial class ProviderLedgerReconciliationService
{
    private VerifiedOperationOutcome BuildTerminalOutcome(
        ProviderLedgerReconciliationRunIntent intent,
        string inputHash,
        ProviderLedgerReconciliationSummaryDto summary,
        IReadOnlyList<string> warnings,
        ProviderCaseworkPersistenceResult casework)
    {
        var completedAt = DateTimeOffset.UtcNow;
        var runEvidenceId = "provider-reconciliation-run";
        var inputEvidenceId = "provider-reconciliation-input";
        var caseworkEvidenceId = "provider-reconciliation-casework";
        var route = UiApiRoutes.FundAccountBrokerageSyncReconciliationLatest.Replace(
            "{accountId}",
            summary.AccountId.ToString("D"),
            StringComparison.Ordinal);
        var caseworkHash = ComputeSha256(string.Join("\n", casework.CaseIds.Order(StringComparer.Ordinal)));
        var evidence = new List<OperationEvidenceReference>
        {
            new(
                inputEvidenceId,
                "canonical-input",
                "Canonical provider, internal-ledger, request, ledger-book, accounting-period, and as-of input snapshot.",
                Uri: $"urn:sha256:{inputHash}",
                ContentHashSha256: inputHash,
                CapturedAtUtc: intent.UpdatedAtUtc),
            new(
                runEvidenceId,
                "retained-run-detail",
                "Durable provider-ledger reconciliation run detail.",
                Uri: ToFileUri(BuildRunDetailPath(summary.AccountId, summary.ReconciliationRunId)),
                CapturedAtUtc: completedAt),
            new(
                caseworkEvidenceId,
                "reconciliation-casework",
                casework.RequiredCount == 0
                    ? "The evaluated run produced no reconciliation cases requiring retention."
                    : $"Retained {casework.RetainedCount} of {casework.RequiredCount} required reconciliation cases.",
                Uri: route,
                ContentHashSha256: caseworkHash,
                CapturedAtUtc: completedAt)
        };

        var prerequisitesSatisfied = summary.Status != ProviderLedgerReconciliationStatusDto.Blocked;
        var state = !prerequisitesSatisfied || casework.IsBlocked
            ? OperationTerminalState.Blocked
            : !casework.IsSatisfied
                ? OperationTerminalState.Failed
                : summary.Status == ProviderLedgerReconciliationStatusDto.Breaks || warnings.Count > 0
                    ? OperationTerminalState.CompletedWithWarnings
                    : OperationTerminalState.Succeeded;
        var postconditions = new List<OperationPostcondition>
        {
            new(
                "reconciliation-evaluated",
                "Provider and internal-ledger evidence was evaluated using the retained canonical input.",
                OperationPostconditionState.Satisfied,
                Required: true,
                EvidenceIds: [inputEvidenceId]),
            new(
                "accounting-prerequisites-ready",
                "Required provider, internal-ledger, ledger-book, accounting-period, and as-of prerequisites are available.",
                prerequisitesSatisfied ? OperationPostconditionState.Satisfied : OperationPostconditionState.NotSatisfied,
                Required: true,
                EvidenceIds: [inputEvidenceId]),
            new(
                "reconciliation-casework-retained",
                "Every generated reconciliation case is durably retained or the run produced no cases.",
                casework.IsSatisfied ? OperationPostconditionState.Satisfied : OperationPostconditionState.NotSatisfied,
                Required: true,
                EvidenceIds: [caseworkEvidenceId]),
            new(
                "terminal-run-detail-retained",
                "The terminal reconciliation detail is retained under the stable run id.",
                OperationPostconditionState.Satisfied,
                Required: true,
                EvidenceIds: [runEvidenceId])
        };

        IReadOnlyList<OperationIssue> issues;
        IReadOnlyList<OperationRecoveryAction> recovery;
        if (state == OperationTerminalState.Blocked)
        {
            var message = casework.Error
                ?? warnings.FirstOrDefault()
                ?? "Provider-ledger reconciliation is blocked by an unmet prerequisite.";
            issues =
            [
                new OperationIssue(
                    "PROVIDER_RECONCILIATION_BLOCKED",
                    message,
                    OperationIssueSeverity.Error,
                    EvidenceId: casework.IsBlocked ? caseworkEvidenceId : inputEvidenceId)
                {
                    IsBlocking = true
                }
            ];
            recovery =
            [
                new OperationRecoveryAction(
                    "resolve-prerequisite-and-retry",
                    "Resolve prerequisite and retry",
                    casework.Error
                        ?? "Restore the missing source or exact accounting scope, then start a new reconciliation operation.",
                    Retryable: true,
                    RequiresHumanAction: true,
                    Route: route)
                {
                    EvidenceIds = [inputEvidenceId, caseworkEvidenceId]
                }
            ];
        }
        else if (state == OperationTerminalState.Failed)
        {
            issues =
            [
                new OperationIssue(
                    "RECONCILIATION_CASEWORK_NOT_RETAINED",
                    casework.Error ?? "Required reconciliation casework was not fully retained.",
                    OperationIssueSeverity.Error,
                    EvidenceId: caseworkEvidenceId)
            ];
            recovery =
            [
                new OperationRecoveryAction(
                    "retry-same-operation",
                    "Retry same operation",
                    "Repair durable reconciliation case storage, then retry with the same operation id and exact input.",
                    Retryable: true,
                    RequiresHumanAction: true,
                    Route: route)
                {
                    EvidenceIds = [inputEvidenceId, caseworkEvidenceId]
                }
            ];
        }
        else if (state == OperationTerminalState.CompletedWithWarnings)
        {
            issues =
            [
                new OperationIssue(
                    "RECONCILIATION_BREAKS_REQUIRE_REVIEW",
                    summary.BreakCount > 0
                        ? $"Reconciliation completed and retained {summary.BreakCount} break(s) requiring governed review."
                        : warnings[0],
                    OperationIssueSeverity.Warning,
                    EvidenceId: runEvidenceId)
            ];
            recovery =
            [
                new OperationRecoveryAction(
                    "review-reconciliation-casework",
                    "Review reconciliation casework",
                    "Assign, resolve, waive, or supersede retained breaks through governed reconciliation casework before close or certified reporting.",
                    Retryable: false,
                    RequiresHumanAction: true,
                    Route: route)
                {
                    EvidenceIds = [runEvidenceId, caseworkEvidenceId]
                }
            ];
        }
        else
        {
            issues = [];
            recovery = [];
        }

        return VerifiedOperationOutcomeValidator.ValidateAndThrow(new VerifiedOperationOutcome(
            OperationId: intent.OperationId,
            OperationKind: OperationKind,
            State: state,
            StartedAtUtc: intent.StartedAtUtc,
            CompletedAtUtc: completedAt,
            AttemptNumber: intent.AttemptNumber,
            CorrelationId: summary.AccountId.ToString("D"),
            InputHashSha256: inputHash,
            Postconditions: postconditions,
            Evidence: evidence,
            Artifacts: [],
            Issues: issues,
            Recovery: recovery));
    }

    private VerifiedOperationOutcome BuildPersistenceFailureOutcome(
        ProviderLedgerReconciliationRunIntent intent,
        string inputHash,
        ProviderLedgerReconciliationSummaryDto summary,
        string issueCode,
        string issueMessage,
        string? exceptionType,
        bool caseworkRetained,
        bool runRecordRetained)
    {
        var completedAt = DateTimeOffset.UtcNow;
        var inputEvidenceId = "provider-reconciliation-input";
        var intentEvidenceId = "provider-reconciliation-intent";
        var caseworkEvidenceId = "provider-reconciliation-casework";
        var route = UiApiRoutes.FundAccountBrokerageSyncReconciliationLatest.Replace(
            "{accountId}",
            summary.AccountId.ToString("D"),
            StringComparison.Ordinal);
        return VerifiedOperationOutcomeValidator.ValidateAndThrow(new VerifiedOperationOutcome(
            OperationId: intent.OperationId,
            OperationKind: OperationKind,
            State: OperationTerminalState.Failed,
            StartedAtUtc: intent.StartedAtUtc,
            CompletedAtUtc: completedAt,
            AttemptNumber: intent.AttemptNumber,
            CorrelationId: summary.AccountId.ToString("D"),
            InputHashSha256: inputHash,
            Postconditions:
            [
                new OperationPostcondition(
                    "reconciliation-evaluated",
                    "Provider and internal-ledger evidence was evaluated using the retained canonical input.",
                    OperationPostconditionState.Satisfied,
                    Required: true,
                    EvidenceIds: [inputEvidenceId]),
                new OperationPostcondition(
                    "reconciliation-casework-retained",
                    "Required reconciliation casework is durably retained.",
                    caseworkRetained ? OperationPostconditionState.Satisfied : OperationPostconditionState.NotSatisfied,
                    Required: true,
                    EvidenceIds: [caseworkEvidenceId]),
                new OperationPostcondition(
                    "terminal-run-detail-retained",
                    "The terminal reconciliation detail is retained under the stable run id.",
                    runRecordRetained ? OperationPostconditionState.Satisfied : OperationPostconditionState.NotSatisfied,
                    Required: true,
                    EvidenceIds: [intentEvidenceId])
            ],
            Evidence:
            [
                new OperationEvidenceReference(
                    inputEvidenceId,
                    "canonical-input",
                    "Canonical provider, internal-ledger, request, ledger-book, accounting-period, and as-of input snapshot.",
                    Uri: $"urn:sha256:{inputHash}",
                    ContentHashSha256: inputHash,
                    CapturedAtUtc: intent.UpdatedAtUtc),
                new OperationEvidenceReference(
                    intentEvidenceId,
                    "durable-run-intent",
                    "Pre-casework run intent retained before any reconciliation case mutation.",
                    Uri: ToFileUri(BuildOperationIntentPath(summary.AccountId, intent.OperationId)),
                    ContentHashSha256: intent.RequestHashSha256,
                    CapturedAtUtc: intent.StartedAtUtc),
                new OperationEvidenceReference(
                    caseworkEvidenceId,
                    "reconciliation-casework",
                    caseworkRetained
                        ? "All casework created before the terminal persistence failure remains durably retained."
                        : "One or more required reconciliation cases were not durably retained.",
                    Uri: route,
                    CapturedAtUtc: completedAt)
            ],
            Artifacts: [],
            Issues:
            [
                new OperationIssue(
                    issueCode,
                    issueMessage,
                    OperationIssueSeverity.Error,
                    exceptionType,
                    EvidenceId: runRecordRetained ? caseworkEvidenceId : intentEvidenceId)
            ],
            Recovery:
            [
                new OperationRecoveryAction(
                    "repair-storage-and-retry-same-operation",
                    "Repair storage and retry",
                    "Inspect the durable run intent and retained casework, repair storage, then retry with the same operation id and exact input.",
                    Retryable: true,
                    RequiresHumanAction: true,
                    Route: route)
                {
                    EvidenceIds = [intentEvidenceId, caseworkEvidenceId]
                }
            ]));
    }

    private ProviderLedgerReconciliationDetailDto BuildUnexpectedFailureDetail(
        Guid accountId,
        ProviderLedgerReconciliationRunIntent intent,
        string inputHash,
        string issueCode,
        string issueMessage,
        string? exceptionType)
    {
        var completedAt = DateTimeOffset.UtcNow;
        var inputEvidenceId = "provider-reconciliation-input";
        var intentEvidenceId = "provider-reconciliation-intent";
        var outcome = VerifiedOperationOutcomeValidator.ValidateAndThrow(new VerifiedOperationOutcome(
            OperationId: intent.OperationId,
            OperationKind: OperationKind,
            State: OperationTerminalState.Failed,
            StartedAtUtc: intent.StartedAtUtc,
            CompletedAtUtc: completedAt,
            AttemptNumber: intent.AttemptNumber,
            CorrelationId: accountId.ToString("D"),
            InputHashSha256: inputHash,
            Postconditions:
            [
                new OperationPostcondition(
                    "reconciliation-evaluated",
                    "Provider and internal-ledger evidence was evaluated to a verified terminal result.",
                    OperationPostconditionState.NotSatisfied,
                    Required: true,
                    EvidenceIds: [inputEvidenceId]),
                new OperationPostcondition(
                    "pre-casework-run-intent-retained",
                    "A recovery anchor exists before any reconciliation case can be written.",
                    OperationPostconditionState.Satisfied,
                    Required: true,
                    EvidenceIds: [intentEvidenceId])
            ],
            Evidence:
            [
                new OperationEvidenceReference(
                    inputEvidenceId,
                    "request-input",
                    "Canonical request input available when the operation failed.",
                    Uri: $"urn:sha256:{inputHash}",
                    ContentHashSha256: inputHash,
                    CapturedAtUtc: intent.StartedAtUtc),
                new OperationEvidenceReference(
                    intentEvidenceId,
                    "durable-run-intent",
                    "Pre-casework run intent retained before reconciliation case mutation.",
                    Uri: ToFileUri(BuildOperationIntentPath(accountId, intent.OperationId)),
                    ContentHashSha256: intent.RequestHashSha256,
                    CapturedAtUtc: intent.StartedAtUtc)
            ],
            Artifacts: [],
            Issues:
            [
                new OperationIssue(
                    issueCode,
                    issueMessage,
                    OperationIssueSeverity.Error,
                    exceptionType,
                    EvidenceId: inputEvidenceId)
            ],
            Recovery:
            [
                new OperationRecoveryAction(
                    "inspect-intent-and-retry",
                    "Inspect intent and retry",
                    "Inspect the durable intent and operation logs, correct the failure, then retry with the same operation id and exact request.",
                    Retryable: true,
                    RequiresHumanAction: true)
                {
                    EvidenceIds = [inputEvidenceId, intentEvidenceId]
                }
            ]));
        var summary = BuildFailureSummary(accountId, intent.RunId, intent.StartedAtUtc);
        return new ProviderLedgerReconciliationDetailDto(
            summary,
            Checks: [],
            Breaks: [],
            Warnings: [issueMessage],
            EvidenceLinks: [ToFileUri(BuildOperationIntentPath(accountId, intent.OperationId))],
            Outcome: outcome);
    }

    private ProviderLedgerReconciliationDetailDto BuildIdempotencyConflictDetail(
        Guid accountId,
        string operationId,
        string requestHash,
        ProviderLedgerReconciliationRunIntent existingIntent)
        => BuildConflictDetail(
            accountId,
            operationId,
            existingIntent.RunId,
            existingIntent.AttemptNumber + 1,
            existingIntent.StartedAtUtc,
            requestHash,
            existingIntent.RequestHashSha256,
            "OPERATION_ID_REQUEST_CONFLICT",
            "The supplied operation id is already bound to a different reconciliation request. Use a new operation id for changed request input.");

    private ProviderLedgerReconciliationDetailDto BuildInputConflictDetail(
        Guid accountId,
        ProviderLedgerReconciliationRunIntent intent,
        string inputHash,
        string retainedInputHash)
        => BuildConflictDetail(
            accountId,
            intent.OperationId,
            intent.RunId,
            intent.AttemptNumber,
            intent.StartedAtUtc,
            inputHash,
            retainedInputHash,
            "OPERATION_ID_INPUT_CONFLICT",
            "The supplied operation id is already bound to different provider or accounting-scope evidence. Use a new operation id after the source snapshot or exact scope changes.");

    private ProviderLedgerReconciliationDetailDto BuildConflictDetail(
        Guid accountId,
        string operationId,
        Guid runId,
        int attemptNumber,
        DateTimeOffset startedAt,
        string inputHash,
        string retainedHash,
        string issueCode,
        string message)
    {
        var completedAt = DateTimeOffset.UtcNow;
        var suppliedEvidenceId = "supplied-operation-input";
        var retainedEvidenceId = "retained-operation-binding";
        var outcome = VerifiedOperationOutcomeValidator.ValidateAndThrow(new VerifiedOperationOutcome(
            OperationId: operationId,
            OperationKind: OperationKind,
            State: OperationTerminalState.Blocked,
            StartedAtUtc: startedAt,
            CompletedAtUtc: completedAt,
            AttemptNumber: Math.Max(1, attemptNumber),
            CorrelationId: accountId.ToString("D"),
            InputHashSha256: inputHash,
            Postconditions:
            [
                new OperationPostcondition(
                    "idempotency-binding-matched",
                    "The operation id is bound to the exact retained request and source input.",
                    OperationPostconditionState.NotSatisfied,
                    Required: true,
                    EvidenceIds: [suppliedEvidenceId, retainedEvidenceId])
            ],
            Evidence:
            [
                new OperationEvidenceReference(
                    suppliedEvidenceId,
                    "canonical-input",
                    "Canonical input supplied by this attempt.",
                    Uri: $"urn:sha256:{inputHash}",
                    ContentHashSha256: inputHash,
                    CapturedAtUtc: completedAt),
                new OperationEvidenceReference(
                    retainedEvidenceId,
                    "idempotency-binding",
                    "Canonical input already retained for this operation id.",
                    Uri: $"urn:sha256:{retainedHash}",
                    ContentHashSha256: retainedHash,
                    CapturedAtUtc: startedAt)
            ],
            Artifacts: [],
            Issues:
            [
                new OperationIssue(
                    issueCode,
                    message,
                    OperationIssueSeverity.Error,
                    EvidenceId: retainedEvidenceId)
                {
                    IsBlocking = true
                }
            ],
            Recovery:
            [
                new OperationRecoveryAction(
                    "use-new-operation-id",
                    "Use a new operation id",
                    "Keep the existing operation immutable and submit the changed input under a new operation id.",
                    Retryable: true,
                    RequiresHumanAction: false)
                {
                    EvidenceIds = [suppliedEvidenceId, retainedEvidenceId]
                }
            ]));
        return new ProviderLedgerReconciliationDetailDto(
            BuildFailureSummary(accountId, runId, startedAt),
            Checks: [],
            Breaks: [],
            Warnings: [message],
            EvidenceLinks: [$"urn:sha256:{retainedHash}"],
            Outcome: outcome);
    }

    private static ProviderLedgerReconciliationSummaryDto BuildFailureSummary(
        Guid accountId,
        Guid runId,
        DateTimeOffset createdAt)
        => new(
            ReconciliationRunId: runId,
            AccountId: accountId,
            CreatedAt: createdAt,
            Status: ProviderLedgerReconciliationStatusDto.Blocked,
            TotalChecks: 0,
            MatchedChecks: 0,
            BreakCount: 0,
            SecurityIssueCount: 0,
            AmountTolerance: 0m,
            ProviderStaleAfterMinutes: 0,
            ProviderId: null,
            ExternalAccountId: null,
            ProviderSyncedAt: null,
            InternalAsOfDate: null);

    private static string NormalizeOperationId(string? operationId)
    {
        var normalized = string.IsNullOrWhiteSpace(operationId)
            ? Guid.NewGuid().ToString("N")
            : operationId.Trim();
        if (normalized.Length > 200)
        {
            throw new ArgumentException("Provider-ledger reconciliation operation id cannot exceed 200 characters.", nameof(operationId));
        }
        return normalized;
    }

    private static string ComputeRequestHash(Guid accountId, ProviderLedgerReconciliationRequestDto request)
    {
        var builder = new StringBuilder();
        AppendCanonical(builder, "schema", "provider-ledger-request.v1");
        AppendCanonical(builder, "accountId", accountId);
        AppendCanonical(builder, "amountTolerance", Math.Abs(request.AmountTolerance));
        AppendCanonical(builder, "providerStaleAfterMinutes", Math.Max(1, request.ProviderStaleAfterMinutes));
        AppendCanonical(builder, "requestedBy", NormalizeOwner(request.RequestedBy) ?? DefaultActor);
        AppendCanonical(builder, "defaultBreakOwner", NormalizeOwner(request.DefaultBreakOwner) ?? "fund-accounting");
        AppendCanonical(builder, "signedOffBreakCount", request.SignedOffBreakKeys?.Count ?? 0);
        AppendCanonical(builder, "signedOffBy", NormalizeOwner(request.SignedOffBy));
        return ComputeSha256(builder.ToString());
    }

    private static string ComputeOperationInputHash(
        Guid accountId,
        ProviderLedgerReconciliationRequestDto request,
        FundAccountBrokerageSyncActivityDto? provider,
        AccountBalanceSnapshotDto? ledger,
        ProviderLedgerScope? scope,
        string? scopeError,
        IReadOnlyList<CustodianPositionLineDto> custodianPositions,
        IReadOnlyList<BankStatementLineDto> bankStatementLines,
        IReadOnlyList<ProviderLedgerReconciliationCheckDto> checks,
        IReadOnlyList<ProviderSecurityMasterPassportDto> securityMasterPassports)
    {
        var builder = new StringBuilder();
        AppendCanonical(builder, "schema", "provider-ledger-input.v1");
        AppendCanonical(builder, "requestHash", ComputeRequestHash(accountId, request));
        AppendCanonical(builder, "provider.present", provider is not null);
        if (provider is not null)
        {
            AppendCanonical(builder, "provider.fundAccountId", provider.FundAccountId);
            AppendCanonical(builder, "provider.id", provider.Link.ProviderId);
            AppendCanonical(builder, "provider.externalAccountId", provider.Link.ExternalAccountId);
            AppendCanonical(builder, "provider.accountKind", provider.Link.AccountKind);
            AppendCanonical(builder, "provider.syncedAt", provider.SyncedAt);
            AppendCanonical(builder, "provider.rawSnapshotPath", provider.RawSnapshotPath);
            AppendCanonical(builder, "provider.projectionPath", provider.ProjectionPath);
            AppendCanonical(builder, "provider.health", provider.Status.Health);
            AppendCanonical(builder, "provider.isStale", provider.Status.IsStale);
            AppendCanonical(builder, "provider.lastSuccessfulSyncAt", provider.Status.LastSuccessfulSyncAt);
            AppendCanonical(builder, "provider.balance.cash", provider.Balance?.Cash);
            AppendCanonical(builder, "provider.balance.equity", provider.Balance?.Equity);
            AppendCanonical(builder, "provider.balance.buyingPower", provider.Balance?.BuyingPower);
            AppendCanonical(builder, "provider.balance.currency", provider.Balance?.Currency);
            AppendCanonical(builder, "provider.balance.margin", provider.Balance?.MarginBalance);
            var index = 0;
            foreach (var position in provider.Positions
                         .OrderBy(static item => item.Symbol, StringComparer.Ordinal)
                         .ThenBy(static item => item.PositionId, StringComparer.Ordinal))
            {
                var prefix = $"provider.position[{index++}]";
                AppendCanonical(builder, $"{prefix}.symbol", position.Symbol);
                AppendCanonical(builder, $"{prefix}.positionId", position.PositionId);
                AppendCanonical(builder, $"{prefix}.quantity", position.Quantity);
                AppendCanonical(builder, $"{prefix}.averageEntryPrice", position.AverageEntryPrice);
                AppendCanonical(builder, $"{prefix}.marketPrice", position.MarketPrice);
                AppendCanonical(builder, $"{prefix}.marketValue", position.MarketValue);
                AppendCanonical(builder, $"{prefix}.unrealizedPnl", position.UnrealizedPnl);
                AppendCanonical(builder, $"{prefix}.assetClass", position.AssetClass);
                AppendCanonical(builder, $"{prefix}.currency", position.Currency);
                AppendCanonical(builder, $"{prefix}.securityId", position.Security?.SecurityId);
            }
            index = 0;
            foreach (var fill in provider.Fills.OrderBy(static item => item.FillId, StringComparer.Ordinal))
            {
                var prefix = $"provider.fill[{index++}]";
                AppendCanonical(builder, $"{prefix}.id", fill.FillId);
                AppendCanonical(builder, $"{prefix}.orderId", fill.OrderId);
                AppendCanonical(builder, $"{prefix}.symbol", fill.Symbol);
                AppendCanonical(builder, $"{prefix}.side", fill.Side);
                AppendCanonical(builder, $"{prefix}.quantity", fill.Quantity);
                AppendCanonical(builder, $"{prefix}.price", fill.Price);
                AppendCanonical(builder, $"{prefix}.filledAt", fill.FilledAt);
                AppendCanonical(builder, $"{prefix}.commission", fill.Commission);
                AppendCanonical(builder, $"{prefix}.realizedPnl", fill.RealizedPnl);
            }
            index = 0;
            foreach (var transaction in provider.CashTransactions.OrderBy(static item => item.TransactionId, StringComparer.Ordinal))
            {
                var prefix = $"provider.cash[{index++}]";
                AppendCanonical(builder, $"{prefix}.id", transaction.TransactionId);
                AppendCanonical(builder, $"{prefix}.type", transaction.TransactionType);
                AppendCanonical(builder, $"{prefix}.amount", transaction.Amount);
                AppendCanonical(builder, $"{prefix}.currency", transaction.Currency);
                AppendCanonical(builder, $"{prefix}.postedAt", transaction.PostedAt);
                AppendCanonical(builder, $"{prefix}.symbol", transaction.Symbol);
            }
            index = 0;
            foreach (var action in (provider.CorporateActions ?? [])
                         .OrderBy(static item => item.EventId, StringComparer.Ordinal))
            {
                var prefix = $"provider.corporateAction[{index++}]";
                AppendCanonical(builder, $"{prefix}.id", action.EventId);
                AppendCanonical(builder, $"{prefix}.type", action.EventType);
                AppendCanonical(builder, $"{prefix}.symbol", action.Symbol);
                AppendCanonical(builder, $"{prefix}.effectiveDate", action.EffectiveDate);
                AppendCanonical(builder, $"{prefix}.exDate", action.ExDate);
                AppendCanonical(builder, $"{prefix}.amount", action.Amount);
                AppendCanonical(builder, $"{prefix}.quantity", action.Quantity);
                AppendCanonical(builder, $"{prefix}.factor", action.Factor);
                AppendCanonical(builder, $"{prefix}.currency", action.Currency);
            }
        }

        AppendCanonical(builder, "ledger.present", ledger is not null);
        if (ledger is not null)
        {
            AppendCanonical(builder, "ledger.snapshotId", ledger.SnapshotId);
            AppendCanonical(builder, "ledger.accountId", ledger.AccountId);
            AppendCanonical(builder, "ledger.fundId", ledger.FundId);
            AppendCanonical(builder, "ledger.asOfDate", ledger.AsOfDate);
            AppendCanonical(builder, "ledger.currency", ledger.Currency);
            AppendCanonical(builder, "ledger.cash", ledger.CashBalance);
            AppendCanonical(builder, "ledger.securitiesMarketValue", ledger.SecuritiesMarketValue);
            AppendCanonical(builder, "ledger.accruedInterest", ledger.AccruedInterest);
            AppendCanonical(builder, "ledger.pendingSettlement", ledger.PendingSettlement);
            AppendCanonical(builder, "ledger.unrealizedPnl", ledger.UnrealizedPnl);
            AppendCanonical(builder, "ledger.realizedPnl", ledger.RealizedPnl);
            AppendCanonical(builder, "ledger.source", ledger.Source);
            AppendCanonical(builder, "ledger.recordedAt", ledger.RecordedAt);
            AppendCanonical(builder, "ledger.externalReference", ledger.ExternalReference);
        }

        var sourceIndex = 0;
        foreach (var position in custodianPositions
                     .OrderBy(static item => item.Identifier, StringComparer.Ordinal)
                     .ThenBy(static item => item.LineId))
        {
            var prefix = $"custodian.position[{sourceIndex++}]";
            AppendCanonical(builder, $"{prefix}.lineId", position.LineId);
            AppendCanonical(builder, $"{prefix}.batchId", position.BatchId);
            AppendCanonical(builder, $"{prefix}.asOfDate", position.AsOfDate);
            AppendCanonical(builder, $"{prefix}.identifier", position.Identifier);
            AppendCanonical(builder, $"{prefix}.identifierType", position.IdentifierType);
            AppendCanonical(builder, $"{prefix}.quantity", position.Quantity);
            AppendCanonical(builder, $"{prefix}.marketValue", position.MarketValue);
            AppendCanonical(builder, $"{prefix}.costBasis", position.CostBasis);
            AppendCanonical(builder, $"{prefix}.currency", position.Currency);
            AppendCanonical(builder, $"{prefix}.isShort", position.IsShort);
        }

        sourceIndex = 0;
        foreach (var line in bankStatementLines
                     .OrderBy(static item => item.ValueDate)
                     .ThenBy(static item => item.LineId))
        {
            var prefix = $"bank.line[{sourceIndex++}]";
            AppendCanonical(builder, $"{prefix}.lineId", line.LineId);
            AppendCanonical(builder, $"{prefix}.batchId", line.BatchId);
            AppendCanonical(builder, $"{prefix}.transactionDate", line.TransactionDate);
            AppendCanonical(builder, $"{prefix}.valueDate", line.ValueDate);
            AppendCanonical(builder, $"{prefix}.amount", line.Amount);
            AppendCanonical(builder, $"{prefix}.currency", line.Currency);
            AppendCanonical(builder, $"{prefix}.transactionType", line.TransactionType);
            AppendCanonical(builder, $"{prefix}.reference", line.Reference);
            AppendCanonical(builder, $"{prefix}.closingBalance", line.ClosingBalance);
        }

        sourceIndex = 0;
        foreach (var check in checks.OrderBy(static item => item.CheckId, StringComparer.Ordinal))
        {
            var prefix = $"evaluation.check[{sourceIndex++}]";
            AppendCanonical(builder, $"{prefix}.id", check.CheckId);
            AppendCanonical(builder, $"{prefix}.status", check.Status);
            AppendCanonical(builder, $"{prefix}.expectedSource", check.ExpectedSource);
            AppendCanonical(builder, $"{prefix}.actualSource", check.ActualSource);
            AppendCanonical(builder, $"{prefix}.expected", check.ExpectedAmount);
            AppendCanonical(builder, $"{prefix}.actual", check.ActualAmount);
            AppendCanonical(builder, $"{prefix}.variance", check.Variance);
            AppendCanonical(builder, $"{prefix}.reason", check.Reason);
        }

        sourceIndex = 0;
        foreach (var passport in securityMasterPassports.OrderBy(static item => item.Symbol, StringComparer.Ordinal))
        {
            var prefix = $"securityMaster.passport[{sourceIndex++}]";
            AppendCanonical(builder, $"{prefix}.symbol", passport.Symbol);
            AppendCanonical(builder, $"{prefix}.securityId", passport.SecurityId);
            AppendCanonical(builder, $"{prefix}.status", passport.Status);
            AppendCanonical(builder, $"{prefix}.securityStatus", passport.SecurityStatus);
            AppendCanonical(builder, $"{prefix}.confidence", passport.ConfidenceScore);
            AppendCanonical(builder, $"{prefix}.resolutionSource", passport.ResolutionSource);
            AppendCanonical(builder, $"{prefix}.freshnessMinutes", passport.FreshnessMinutes);
            foreach (var conflict in passport.IdentifierConflicts.Order(StringComparer.Ordinal))
            {
                AppendCanonical(builder, $"{prefix}.identifierConflict", conflict);
            }
            foreach (var issue in passport.ValidationIssueCodes.Order(StringComparer.Ordinal))
            {
                AppendCanonical(builder, $"{prefix}.validationIssue", issue);
            }
        }

        AppendCanonical(builder, "scope.error", scopeError);
        AppendCanonical(builder, "scope.bookId", scope?.Book.LedgerBookId);
        AppendCanonical(builder, "scope.fundProfileId", scope?.Book.FundProfileId);
        AppendCanonical(builder, "scope.accountingBasis", scope?.Book.AccountingBasis);
        AppendCanonical(builder, "scope.accountingPolicyId", scope?.Book.AccountingPolicyId);
        AppendCanonical(builder, "scope.accountingPolicyVersion", scope?.Book.AccountingPolicyVersion);
        AppendCanonical(builder, "scope.baseCurrency", scope?.Book.BaseCurrency);
        AppendCanonical(builder, "scope.periodId", scope?.Period?.PeriodId);
        AppendCanonical(builder, "scope.periodStart", scope?.Period?.StartDate);
        AppendCanonical(builder, "scope.periodEnd", scope?.Period?.EndDate);
        AppendCanonical(builder, "scope.asOfDate", scope?.AsOfDate);
        return ComputeSha256(builder.ToString());
    }

    private static void AppendCanonical(StringBuilder builder, string key, object? value)
    {
        var normalizedKey = key.Trim();
        var text = CanonicalScalar(value);
        builder.Append(normalizedKey.Length.ToString(CultureInfo.InvariantCulture))
            .Append(':')
            .Append(normalizedKey)
            .Append('=')
            .Append(text.Length.ToString(CultureInfo.InvariantCulture))
            .Append(':')
            .Append(text)
            .Append('|');
    }

    private static string CanonicalScalar(object? value)
        => value switch
        {
            null => "<null>",
            string text => text.Trim(),
            DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            DateTimeOffset timestamp => timestamp.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            DateTime timestamp => timestamp.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            Guid id => id.ToString("D"),
            bool flag => flag ? "true" : "false",
            Enum enumValue => enumValue.ToString(),
            decimal number => number.ToString("G29", CultureInfo.InvariantCulture),
            double number => number.ToString("R", CultureInfo.InvariantCulture),
            float number => number.ToString("R", CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? "<null>",
            _ => value.ToString()?.Trim() ?? "<null>"
        };

    private static string ComputeSha256(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private async Task<ProviderLedgerReconciliationRunIntent?> ReadRunIntentAsync(
        Guid accountId,
        string operationId,
        CancellationToken ct)
    {
        var path = BuildOperationIntentPath(accountId, operationId);
        if (!File.Exists(path))
        {
            return null;
        }

        await using var stream = File.OpenRead(path);
        var intent = await JsonSerializer.DeserializeAsync<ProviderLedgerReconciliationRunIntent>(stream, JsonOptions, ct)
            .ConfigureAwait(false);
        if (intent is null || !string.Equals(intent.SchemaVersion, RunIntentSchemaVersion, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Provider-ledger reconciliation run intent '{path}' is missing or has an unsupported schema.");
        }
        if (intent.AccountId != accountId || intent.RunId == Guid.Empty || intent.AttemptNumber <= 0)
        {
            throw new InvalidDataException($"Provider-ledger reconciliation run intent '{path}' has invalid account, run, or attempt identity.");
        }
        return intent;
    }

    private async Task PersistRunIntentAsync(ProviderLedgerReconciliationRunIntent intent, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(intent, JsonOptions);
        await AtomicFileWriter.WriteAsync(BuildOperationAttemptPath(intent), json, ct).ConfigureAwait(false);
        await AtomicFileWriter.WriteAsync(BuildOperationIntentPath(intent.AccountId, intent.OperationId), json, ct).ConfigureAwait(false);
    }

    private async Task TryPersistRunIntentAsync(ProviderLedgerReconciliationRunIntent intent, CancellationToken ct)
    {
        try
        {
            await PersistRunIntentAsync(intent, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(
                ex,
                "Failed to update provider-ledger reconciliation run intent {OperationId} for account {AccountId}",
                intent.OperationId,
                intent.AccountId);
        }
    }

    private async Task<bool> TryPersistTerminalFailureAsync(
        ProviderLedgerReconciliationRunIntent intent,
        ProviderLedgerReconciliationDetailDto detail,
        CancellationToken ct)
    {
        try
        {
            await PersistAsync(intent.AccountId, detail, ct).ConfigureAwait(false);
            await TryPersistRunIntentAsync(
                    intent with
                    {
                        InputHashSha256 = detail.Outcome?.InputHashSha256 ?? intent.InputHashSha256,
                        UpdatedAtUtc = detail.Outcome?.CompletedAtUtc ?? DateTimeOffset.UtcNow,
                        State = detail.Outcome?.State.ToString() ?? "Failed",
                        TerminalState = detail.Outcome?.State ?? OperationTerminalState.Failed,
                        FailureReason = detail.Outcome?.Issues.FirstOrDefault()?.Message
                    },
                    ct)
                .ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(
                ex,
                "Failed to retain terminal provider-ledger reconciliation detail for operation {OperationId}",
                intent.OperationId);
            return false;
        }
    }

    private async Task<ProviderLedgerReconciliationDetailDto?> GetRunDetailAsync(
        Guid accountId,
        Guid runId,
        CancellationToken ct)
    {
        var path = BuildRunDetailPath(accountId, runId);
        if (!File.Exists(path))
        {
            return null;
        }
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<ProviderLedgerReconciliationDetailDto>(stream, JsonOptions, ct)
            .ConfigureAwait(false);
    }

    private async Task PersistAsync(
        Guid accountId,
        ProviderLedgerReconciliationDetailDto detail,
        CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(detail, JsonOptions);
        await AtomicFileWriter.WriteAsync(BuildRunAttemptDetailPath(accountId, detail), json, ct)
            .ConfigureAwait(false);
        await AtomicFileWriter.WriteAsync(BuildRunDetailPath(accountId, detail.Summary.ReconciliationRunId), json, ct)
            .ConfigureAwait(false);
        await AtomicFileWriter.WriteAsync(BuildLatestDetailPath(accountId), json, ct)
            .ConfigureAwait(false);
    }

    private string BuildRunAttemptDetailPath(Guid accountId, ProviderLedgerReconciliationDetailDto detail)
        => Path.Combine(
            BuildAccountDirectory(accountId),
            "runs",
            $"{detail.Summary.ReconciliationRunId:N}.attempt-{Math.Max(1, detail.Outcome?.AttemptNumber ?? 1):D4}.json");

    private string BuildRunDetailPath(Guid accountId, Guid runId)
        => Path.Combine(BuildAccountDirectory(accountId), "runs", $"{runId:N}.json");

    private string BuildLatestDetailPath(Guid accountId)
        => Path.Combine(BuildAccountDirectory(accountId), "latest.json");

    private string BuildOperationIntentPath(Guid accountId, string operationId)
        => Path.Combine(BuildOperationDirectory(accountId, operationId), "intent.json");

    private string BuildOperationAttemptPath(ProviderLedgerReconciliationRunIntent intent)
        => Path.Combine(
            BuildOperationDirectory(intent.AccountId, intent.OperationId),
            "attempts",
            $"{intent.AttemptNumber:D4}.json");

    private string BuildOperationDirectory(Guid accountId, string operationId)
        => Path.Combine(BuildAccountDirectory(accountId), "operations", ComputeSha256(operationId));

    private static string ToFileUri(string path)
        => new Uri(Path.GetFullPath(path)).AbsoluteUri;

    private string BuildAccountDirectory(Guid accountId)
        => Path.Combine(_options.RootDirectory, "reconciliation", accountId.ToString("N"));
}
