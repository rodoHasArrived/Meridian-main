namespace Meridian.Execution.Sdk;

/// <summary>The observed outcome of one cancel-all operation, separate from broker order status.</summary>
public enum CancelAllOrderDisposition
{
    Unknown,
    ConfirmedCancelled,
    FilledDuringCancel,
    AlreadyTerminal,
    StillWorking,
    WithdrawnBeforeDispatch
}

/// <summary>
/// Retained observations for one local order or broker-only order. These are evidence as of
/// <see cref="ObservedAt"/>, not a current position valuation or a new accounting posting.
/// </summary>
public sealed record CancelAllOrderOutcome
{
    /// <summary>Distinguishes local and broker identities even when their text happens to collide.</summary>
    public required string ObservationKey { get; init; }
    public required string OrderId { get; init; }
    public string? BrokerOrderId { get; init; }
    public Guid? FundAccountId { get; init; }
    public string? BrokerAccountId { get; init; }
    public string? Symbol { get; init; }
    public OrderSide? Side { get; init; }
    public string Operation { get; init; } = "Cancel";
    public CancelAllOrderDisposition Disposition { get; init; }
    public OrderStatus? InitialStatus { get; init; }
    public OrderStatus? Status { get; init; }
    public decimal? InitialQuantity { get; init; }
    public decimal? InitialFilledQuantity { get; init; }
    public decimal? Quantity { get; init; }
    public decimal? FilledQuantity { get; init; }
    /// <summary>Possible remaining order quantity; null means it could not be established.</summary>
    public decimal? RemainingQuantity { get; init; }
    public bool CancellationAttempted { get; init; }
    public bool RequiresReconciliation { get; init; }
    public bool HasUnconfirmedReplacement { get; init; }
    public string? CandidateBrokerOrderId { get; init; }
    public string? CandidateClientOrderId { get; init; }
    public decimal? ResidualNotional { get; init; }
    public string? Currency { get; init; }
    public string ValuationCoverage { get; init; } = "Unavailable";
    public OrderMarketReference? ValuationReference { get; init; }
    public required DateTimeOffset ObservedAt { get; init; }
    public string? Detail { get; init; }
}

/// <summary>
/// Complete or interrupted cancel-all evidence reconstructed from the execution audit WAL.
/// Account projections contain only orders attributed to the requested account. Broker-wide
/// coverage is observational and never establishes that an omitted account has no exposure.
/// </summary>
public sealed record CancelAllIncident
{
    public int Version { get; init; } = 1;
    public required string IncidentId { get; init; }
    public required DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public string? Actor { get; init; }
    public string? CorrelationId { get; init; }
    public OrderSubmissionAuthorityRecord? Authority { get; init; }
    public required string GatewayId { get; init; }
    public required ExecutionMode ExecutionMode { get; init; }
    public string? BrokerScopeIdentity { get; init; }
    public string? TenantId { get; init; }
    public string? CompanyId { get; init; }
    public Guid? FundAccountId { get; init; }
    public bool IsAccountProjection { get; init; }
    /// <summary>InProgress, Complete or Interrupted. Complete describes evidence, not cancellation success.</summary>
    public string EvidenceStatus { get; init; } = "InProgress";
    public string? EvidenceError { get; init; }
    public bool BrokerViewUnavailable { get; init; } = true;
    public string? BrokerViewError { get; init; } = "Broker convergence has not been observed.";
    public IReadOnlyList<CancelAllOrderOutcome> Orders { get; init; } = [];
}
