using System.Text;
using System.Text.Json;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Workstation;
using Meridian.Strategies.Services;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

public static partial class WorkstationEndpoints
{
    private static readonly JsonSerializerOptions ReconciliationAuditJsonOptions =
        CreateReconciliationAuditJsonOptions();

    private static async Task<ReconciliationBreakQueueTransitionResult> ResolveBreakAsync(
        IServiceProvider services,
        ReconciliationBreakQueueScope scope,
        ResolveReconciliationBreakRequest request,
        CancellationToken ct)
    {
        var repository = services.GetService<IReconciliationBreakQueueRepository>();
        if (repository is null)
        {
            return new ReconciliationBreakQueueTransitionResult(
                ReconciliationBreakQueueTransitionStatus.NotFound,
                Item: null,
                Error: "Reconciliation break queue repository is not registered.");
        }

        var item = await repository.GetByIdAsync(scope, request.BreakId, ct).ConfigureAwait(false);
        if (item is null || !string.Equals(item.SourceType, "statement", StringComparison.OrdinalIgnoreCase))
        {
            return await ResolveBreakAsync(repository, scope, request, ct).ConfigureAwait(false);
        }

        var handoff = services.GetService<IStatementReconciliationCaseworkHandoffService>()
            ?? throw new StatementReconciliationCaseworkHandoffException(
                "STATEMENT_CASEWORK_AUTHORITY_REQUIRED",
                "Authoritative statement reconciliation casework handoff is not registered.");
        var command = await BuildLegacyStatementResolveCommandAsync(
                repository,
                scope,
                item,
                request,
                ct)
            .ConfigureAwait(false);
        return await handoff.ApplyAsync(scope, command, ct).ConfigureAwait(false);
    }

    private static async Task<ReconciliationCaseworkCommand> BuildLegacyStatementResolveCommandAsync(
        IReconciliationBreakQueueRepository repository,
        ReconciliationBreakQueueScope scope,
        ReconciliationBreakQueueItem current,
        ResolveReconciliationBreakRequest request,
        CancellationToken ct)
    {
        var material = string.Join(
            '\n',
            "meridian.workstation.statement-legacy-resolve.v1",
            request.BreakId.Trim(),
            request.Status.ToString(),
            request.ResolvedBy.Trim(),
            (request.ResolutionNote ?? string.Empty).Trim(),
            (request.OperatorRationale ?? string.Empty).Trim(),
            // The caller's origin, re-derived at the route handler, rather than a constant. For a
            // human session this is still "HumanOperator", so existing command ids are unchanged;
            // only a non-human origin -- which the gate downstream refuses anyway -- differs (#2673).
            request.ActionOrigin.ToString());
        var inputHash = Sha256Digest.ComputeUtf8(material);
        var commandId = $"statement-legacy-resolve:{inputHash}";
        var commandBase = current;
        if (StatementCaseworkHandoffObligation.HasPending(current, commandId)
            || StatementCaseworkHandoffObligation.HasCompleted(current, commandId))
        {
            var retainedAudit = (await repository.GetAuditHistoryAsync(scope, current.BreakId, ct).ConfigureAwait(false))
                .LastOrDefault(audit =>
                    string.Equals(audit.CommandId, commandId, StringComparison.Ordinal)
                    && !string.IsNullOrWhiteSpace(audit.BeforePayload));
            if (retainedAudit is null)
            {
                throw new StatementReconciliationCaseworkHandoffException(
                    "STATEMENT_CASEWORK_RECEIPT_MISSING",
                    $"Statement case '{current.BreakId}' retains a handoff marker without its originating casework audit.");
            }

            try
            {
                commandBase = JsonSerializer.Deserialize<ReconciliationBreakQueueItem>(
                                  retainedAudit.BeforePayload!,
                                  ReconciliationAuditJsonOptions)
                              ?? throw new JsonException("The retained before-payload was empty.");
            }
            catch (JsonException exception)
            {
                throw new StatementReconciliationCaseworkHandoffException(
                    "STATEMENT_CASEWORK_RECEIPT_INVALID",
                    $"The retained statement casework receipt for '{current.BreakId}' cannot be reconstructed.",
                    exception);
            }
        }

        var dismissed = request.Status == ReconciliationBreakQueueStatus.Dismissed;
        return new ReconciliationCaseworkCommand(
            BreakId: request.BreakId,
            Action: ReconciliationCaseworkAction.Resolve,
            Actor: request.ResolvedBy,
            CommandId: commandId,
            CorrelationId: $"statement-legacy-resolve:{inputHash[..16]}",
            Source: "workstation-statement-legacy-resolve-adapter",
            ExpectedVersion: commandBase.Version,
            Reason: request.OperatorRationale,
            Note: request.ResolutionNote,
            RootCauseCode: dismissed ? "DismissedFalsePositive" : commandBase.RootCauseCode,
            ResolutionCode: dismissed
                ? "DismissedFalsePositive"
                : commandBase.ResolutionCode ?? "LegacyResolved",
            EvidenceLinks: (commandBase.EvidenceLinks ?? [])
                .Where(static evidence => !StatementCaseworkHandoffObligation.IsControlMarker(evidence))
                .ToArray(),
            ActionOrigin: request.ActionOrigin);
    }

    private static JsonSerializerOptions CreateReconciliationAuditJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };
        options.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
        return options;
    }

    private static async Task<ReconciliationBreakQueueTransitionResult> ResolveBreakAsync(
        IReconciliationBreakQueueRepository? repository,
        ReconciliationBreakQueueScope scope,
        ResolveReconciliationBreakRequest request,
        CancellationToken ct)
    {
        if (repository is null)
        {
            return new ReconciliationBreakQueueTransitionResult(
                ReconciliationBreakQueueTransitionStatus.NotFound,
                Item: null,
                Error: "Reconciliation break queue repository is not registered.");
        }

        return await repository.ResolveAsync(scope, request, ct).ConfigureAwait(false);
    }
}
