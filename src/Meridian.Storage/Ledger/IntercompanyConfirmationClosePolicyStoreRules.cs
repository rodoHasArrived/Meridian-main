using System.Text.Json;
using Meridian.Contracts.Ledger;

namespace Meridian.Storage.Ledger;

/// <summary>Append-only checks shared by the file and PostgreSQL confirmation close-policy stores.</summary>
public static class IntercompanyConfirmationClosePolicyStoreRules
{
    public static string Tenant(string tenantId) => string.IsNullOrWhiteSpace(tenantId) || tenantId.Trim().Equals("all", StringComparison.OrdinalIgnoreCase)
        ? throw new UnauthorizedAccessException("A single tenant scope is required.")
        : tenantId.Trim().ToLowerInvariant();

    /// <summary>True for an identical retry of the current version; otherwise <paramref name="next"/> must append exactly one step.</summary>
    public static bool IsIdenticalRetry(IntercompanyConfirmationClosePolicyDto? current, IntercompanyConfirmationClosePolicyDto next, long expectedVersion)
    {
        ArgumentNullException.ThrowIfNull(next);
        if (current is not null && current.Version == next.Version && Serialize(current) == Serialize(next))
            return true;
        if ((current?.Version ?? 0) != expectedVersion)
            throw new InvalidOperationException("The close policy changed since it was read. Reload it before acting.");
        if (next.Version != expectedVersion + 1 || next.Events.Count != (current?.Events.Count ?? 0) + 1
            || (current is not null && (current.PolicyId != next.PolicyId || current.LedgerBookId != next.LedgerBookId
                || current.CompanyId != next.CompanyId || !current.BlockingCodes.SequenceEqual(next.BlockingCodes)
                || !current.Events.SequenceEqual(next.Events.Take(current.Events.Count)))))
            throw new InvalidOperationException("A close-policy version must append exactly one step and keep its retained identity, codes and history.");
        return false;
    }

    public static void RequireTenant(string tenant, IntercompanyConfirmationClosePolicyDto policy)
    {
        if (!string.Equals(Tenant(policy.TenantId), tenant, StringComparison.Ordinal))
            throw new UnauthorizedAccessException("The close policy belongs to a different tenant.");
    }

    public static string Serialize(IntercompanyConfirmationClosePolicyDto policy)
        => JsonSerializer.Serialize(policy, IntercompanyConfirmationClosePolicyJsonContext.Default.IntercompanyConfirmationClosePolicyDto);

    public static IntercompanyConfirmationClosePolicyDto Deserialize(string json)
        => JsonSerializer.Deserialize(json, IntercompanyConfirmationClosePolicyJsonContext.Default.IntercompanyConfirmationClosePolicyDto)
            ?? throw new InvalidDataException("A retained close-policy payload is empty.");
}
