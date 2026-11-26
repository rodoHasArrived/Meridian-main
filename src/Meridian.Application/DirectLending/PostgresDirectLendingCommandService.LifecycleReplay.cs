using System.Text.Json;
using Meridian.Contracts.DirectLending;
using Meridian.Storage.DirectLending;
using Npgsql;

namespace Meridian.Application.DirectLending;

public sealed partial class PostgresDirectLendingCommandService
{
    private static readonly JsonSerializerOptions LifecycleCommandJsonOptions = new(JsonSerializerDefaults.Web);

    private async Task<DirectLendingCommandResult<LoanServicingStateDto>?> TryReplayCommittedLifecycleCommandAsync(
        Guid loanId,
        string eventType,
        object request,
        DirectLendingCommandMetadataDto? metadata,
        CancellationToken ct)
    {
        if (metadata?.CommandId is not Guid commandId || commandId == Guid.Empty)
            return null;

        var history = await _queryService.GetHistoryAsync(loanId, ct).ConfigureAwait(false);
        var retained = history.Where(entry => entry.CommandId == commandId).ToArray();
        if (retained.Length == 0)
            return null;
        if (retained.Any(entry => entry.EventType != eventType || !LifecycleRequestMatches(loanId, entry, request)))
            return DirectLendingCommandResult<LoanServicingStateDto>.Failure(DirectLendingErrorCode.Conflict,
                $"Direct lending command id {commandId} was already used for a different mutation on loan {loanId}.");

        var committed = await _queryService.LoadAggregateAsync(loanId, ct).ConfigureAwait(false);
        return committed is not null
            ? DirectLendingCommandResult<LoanServicingStateDto>.Success(committed.Servicing)
            : DirectLendingCommandResult<LoanServicingStateDto>.Failure(DirectLendingErrorCode.Conflict,
                "The committed lifecycle state is unavailable; retry after recovery.");
    }

    private async Task<DirectLendingCommandResult<LoanServicingStateDto>> SaveLifecycleCommandAsync(
        Guid loanId,
        PersistedDirectLendingState previous,
        LoanContractDetailDto contract,
        LoanServicingStateDto servicing,
        string eventType,
        DateOnly? effectiveDate,
        JsonDocument payload,
        object request,
        DirectLendingCommandMetadataDto? metadata,
        CancellationToken ct,
        DirectLendingPersistenceBatch? persistenceBatch = null)
    {
        try
        {
            await SaveAsync(loanId, previous.AggregateVersion, previous.AggregateVersion + 1,
                contract, servicing, eventType, effectiveDate, payload, metadata, ct, persistenceBatch).ConfigureAwait(false);
            return DirectLendingCommandResult<LoanServicingStateDto>.Success(servicing);
        }
        catch (Exception ex) when (IsRetainedCommandPersistenceRace(ex))
        {
            // A concurrent retry can have computed a different revision receipt before the first
            // request committed. Resolve only the identical retained request; never relax the
            // state store's full-payload comparison or alter the already committed evidence.
            var replay = await TryReplayCommittedLifecycleCommandAsync(loanId, eventType, request, metadata, ct).ConfigureAwait(false);
            if (replay is not null)
                return replay;
            throw;
        }
    }

    private static bool IsRetainedCommandPersistenceRace(Exception exception) => exception switch
    {
        DirectLendingCommandException { Error.Code: DirectLendingErrorCode.Conflict or DirectLendingErrorCode.ConcurrencyConflict } => true,
        PostgresException { SqlState: PostgresErrorCodes.SerializationFailure } => true,
        // Migration 007 owns this specific per-loan command-id index. A different uniqueness
        // violation can indicate unrelated corrupt or conflicting data and must propagate.
        PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "ux_loan_event_command_id" } => true,
        _ => false
    };

    private static bool LifecycleRequestMatches(Guid loanId, LoanEventLineageDto entry, object request)
    {
        try
        {
            using var document = JsonDocument.Parse(entry.PayloadJson);
            var fields = document.RootElement.EnumerateObject()
                .ToDictionary(static property => property.Name, static property => property.Value, StringComparer.OrdinalIgnoreCase);
            if (fields.TryGetValue("loanId", out var retainedLoanId) && retainedLoanId.GetGuid() != loanId)
                return false;

            IReadOnlyDictionary<string, JsonElement> requestFields = fields;
            if (entry.EventType == "loan.collateral-added")
            {
                if (!fields.TryGetValue("collateral", out var collateral))
                    return false;
                requestFields = collateral.EnumerateObject()
                    .ToDictionary(static property => property.Name, static property => property.Value, StringComparer.OrdinalIgnoreCase);
            }

            var expected = JsonSerializer.SerializeToElement(request, request.GetType(), LifecycleCommandJsonOptions);
            foreach (var property in expected.EnumerateObject())
            {
                // The durable write-off command retains the original request separately from its
                // capped applied amount. An old applied-only event cannot prove a larger request.
                var name = entry.EventType == "loan.write-off-applied" && property.Name == "amount"
                    ? "requestedAmount" : property.Name;
                if (!requestFields.TryGetValue(name, out var actual))
                {
                    if (property.Value.ValueKind == JsonValueKind.Null)
                        continue;
                    return false;
                }
                if (!JsonElement.DeepEquals(actual, property.Value))
                    return false;
            }
            return true;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException or ArgumentException)
        {
            return false;
        }
    }
}
