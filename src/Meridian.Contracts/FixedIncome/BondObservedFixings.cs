using System.Text.Json.Serialization;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Contracts.FixedIncome;

/// <summary>A source observation deliberately bound to one bond's contractual reset, separate from forecasts.</summary>
public sealed record BondObservedFixingCandidate(
    Guid SecurityId, long SecurityVersion, string IndexName, DateOnly ResetDate, decimal RateFraction,
    SecurityRateObservationDto Observation, string RecordedBy, string Justification);

/// <summary>Immutable observed-reset evidence. Calculations must explicitly select ReceiptId.</summary>
public sealed record BondObservedFixingReceipt(
    Guid ReceiptId, BondObservedFixingCandidate Fixing, DateTimeOffset RecordedAt, string ContentSha256);

public sealed record RetainBondObservedFixingRequest(
    long ExpectedSecurityVersion, DateOnly ResetDate, SecurityRateObservationDto Observation, string Justification);

public interface IBondObservedFixingStore
{
    Task<BondObservedFixingReceipt> RetainAsync(BondObservedFixingCandidate fixing, CancellationToken ct = default);
    Task<BondObservedFixingReceipt?> GetAsync(Guid securityId, Guid receiptId, CancellationToken ct = default);
    Task<IReadOnlyList<BondObservedFixingReceipt>> ListAsync(Guid securityId, CancellationToken ct = default);
}

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(BondObservedFixingCandidate))]
[JsonSerializable(typeof(BondObservedFixingReceipt))]
public sealed partial class BondObservedFixingJsonContext : JsonSerializerContext;
