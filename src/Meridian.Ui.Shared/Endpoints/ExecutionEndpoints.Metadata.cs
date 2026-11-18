using System.Text.Json;
using Meridian.Execution.Sdk;
using Microsoft.AspNetCore.Http;

namespace Meridian.Ui.Shared.Endpoints;

public static partial class ExecutionEndpoints
{
    /// <summary>
    /// The account grant for an operator whose account-scoped access the endpoint has just
    /// verified. Only call after that check succeeds.
    /// </summary>
    private static ExecutionAccountGrant HttpPrincipalAccountGrant(Guid fundAccountId, string actor) =>
        new(fundAccountId, actor, ExecutionAccountGrantSources.HttpPrincipal, DateTimeOffset.UtcNow);

    /// <summary>
    /// Routes an operator order through typed authority when the request names an account the
    /// operator was verified for, and through the untyped path otherwise. Accountless orders are
    /// paper-only in practice: live routing refuses them.
    /// </summary>
    private static Task<OrderResult> SubmitOperatorOrderAsync(
        IOrderManager oms,
        OrderRequest request,
        OrderSubmissionAuthority? authority,
        CancellationToken ct) =>
        authority is null
            ? oms.PlaceOrderAsync(request, ct)
            : oms.SubmitAsync(new OrderSubmitCommand(request, authority), ct);

    private static IResult? TryRejectClientControlledExecutionMetadata(
        OrderRequest request,
        JsonSerializerOptions jsonOptions)
    {
        if (!ContainsServerOwnedExecutionMetadata(request.Metadata))
        {
            return null;
        }

        var blocked = new OrderResult
        {
            Success = false,
            OrderId = request.ClientOrderId ?? "blocked",
            ErrorMessage = "Broker account routing and execution-control metadata must be resolved server-side; server-owned execution metadata is not accepted from client order requests."
        };
        return Results.Json(blocked, jsonOptions, statusCode: StatusCodes.Status403Forbidden);
    }

    private static bool ContainsServerOwnedExecutionMetadata(IReadOnlyDictionary<string, string>? metadata)
    {
        if (metadata is null)
        {
            return false;
        }

        return ExecutionOrderMetadataPolicy.ContainsClientRejectedServerOwnedKey(metadata);
    }

    private static IReadOnlyDictionary<string, string>? RemoveServerOwnedExecutionMetadata(
        IReadOnlyDictionary<string, string>? metadata)
    {
        if (metadata is null)
        {
            return null;
        }

        return ExecutionOrderMetadataPolicy.RemoveClientRejectedServerOwnedKeys(metadata);
    }

    private static bool ContainsRestrictedBrokerRoutingMetadata(IReadOnlyDictionary<string, string>? metadata)
    {
        if (metadata is null || metadata.Count == 0)
        {
            return false;
        }

        return metadata.Keys.Any(static key => key.Equals("asset_class", StringComparison.OrdinalIgnoreCase)
            || key.Equals("assetClass", StringComparison.OrdinalIgnoreCase)
            || key.Equals("alpaca:asset_class", StringComparison.OrdinalIgnoreCase)
            || key.Equals("broker_account_id", StringComparison.OrdinalIgnoreCase)
            || key.Equals("brokerAccountId", StringComparison.OrdinalIgnoreCase)
            || key.Equals("account_id", StringComparison.OrdinalIgnoreCase)
            || key.Equals("accountId", StringComparison.OrdinalIgnoreCase)
            || key.Equals("alpaca:broker_account_id", StringComparison.OrdinalIgnoreCase));
    }

    private static Dictionary<string, string> MergeMetadata(
        IReadOnlyDictionary<string, string>? metadata,
        params (string Key, string? Value)[] additions)
    {
        var merged = metadata is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(metadata, StringComparer.OrdinalIgnoreCase);

        foreach (var (key, value) in additions)
        {
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            merged[key] = value;
        }

        return merged;
    }
}
