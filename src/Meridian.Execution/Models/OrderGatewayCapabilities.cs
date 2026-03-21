using Meridian.Execution.Sdk;
using OrderType = Meridian.Execution.Sdk.OrderType;
using TimeInForce = Meridian.Execution.Sdk.TimeInForce;

namespace Meridian.Execution.Models;

/// <summary>
/// Describes the provider-independent capabilities exposed by an order gateway.
/// Concrete brokers can publish additional implementation-specific hints through
/// <see cref="ProviderExtensions"/> without leaking provider types into strategies.
/// </summary>
public sealed record OrderGatewayCapabilities(
    IReadOnlySet<OrderType> SupportedOrderTypes,
    IReadOnlySet<TimeInForce> SupportedTimeInForce,
    IReadOnlySet<ExecutionMode> SupportedExecutionModes,
    bool SupportsOrderModification,
    bool SupportsPartialFills,
    IReadOnlyDictionary<string, string>? ProviderExtensions = null);

/// <summary>
/// Result of validating an order request against a gateway's capabilities and rules.
/// </summary>
public sealed record OrderValidationResult(
    bool IsValid,
    string? Reason = null,
    IReadOnlyDictionary<string, string>? ProviderHints = null);
