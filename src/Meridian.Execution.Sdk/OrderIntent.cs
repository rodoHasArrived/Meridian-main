namespace Meridian.Execution.Sdk;

/// <summary>Server-resolved scope and terms for submitting one order to the OMS.</summary>
public sealed record OrderIntent(
    Guid IntentId,
    string TenantId,
    string CompanyId,
    Guid FundAccountId,
    string Actor,
    OrderRequest Terms,
    StrategyOrderAuthority? StrategyAuthority = null);

/// <summary>Authority carried by an active, reviewed strategy paper session.</summary>
public sealed record StrategyOrderAuthority(
    string StrategyId,
    string RunId,
    string StrategyVersionSha256,
    string ApprovalReference,
    string PaperSessionId)
{
    /// <summary>The exact executable artifact, when the reviewed run carries one.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? StrategyArtifactId { get; init; }
}

public static class OrderIntentStatus
{
    public const string Retained = "Retained";
    public const string Conflict = "Conflict";
    public const string ReconciliationRequired = "ReconciliationRequired";
    public const string Blocked = "Blocked";
}

/// <summary>Versioned submissions use the same OMS instance as existing order requests.</summary>
public interface IOrderIntentSubmitter
{
    Task<OrderResult> SubmitIntentAsync(OrderIntent intent, CancellationToken ct = default);
}
