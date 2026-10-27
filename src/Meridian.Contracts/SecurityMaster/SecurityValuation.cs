using System.Text.Json.Serialization;

namespace Meridian.Contracts.SecurityMaster;

/// <summary>The model a security is valued with.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SecurityValuationModelKind>))]
public enum SecurityValuationModelKind
{
    /// <summary>The golden-copy price selected through the pricing hierarchy.</summary>
    QuotedPrice,

    /// <summary>The projected cash flows discounted on a stored discount curve.</summary>
    DiscountedCashFlow
}

/// <summary>
/// How observable the inputs behind a value are. The mark lane maps this to the ledger's
/// <c>FairValueLevel</c>; Contracts cannot reference Ledger.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<SecurityValuationObservability>))]
public enum SecurityValuationObservability
{
    /// <summary>A quoted price selected from retained observations.</summary>
    QuotedPrice,

    /// <summary>A model on stored market inputs (curves) only.</summary>
    ObservableInputs,

    /// <summary>A model that fell back to an unobservable input, such as a flat forward at the last fixing.</summary>
    UnobservableInputs
}

/// <summary>A curve the caller pins for a role; <paramref name="Role"/> is <c>Discount</c> in this increment.</summary>
public sealed record SecurityValuationCurveInput(string Role, Guid CurveId, Guid? CurveVersionId = null);

/// <summary>
/// Value one security as of a date with these market inputs. <paramref name="AsOf"/> is the
/// economic cutoff (curves and schedules use its UTC date); <paramref name="KnownAt"/> the knowledge
/// cutoff, as the golden-copy as-of read takes it. <paramref name="Quantity"/> is in the class's lot
/// unit and enables <see cref="SecurityValuationResult.MarketValue"/>. <paramref name="Model"/>
/// overrides the catalog's <c>DefaultValuationModel</c>.
/// </summary>
public sealed record SecurityValuationRequest(
    Guid SecurityId,
    DateTimeOffset AsOf,
    DateTimeOffset? KnownAt = null,
    string? AccountId = null,
    decimal? Quantity = null,
    IReadOnlyList<SecurityValuationCurveInput>? Curves = null,
    SecurityValuationModelKind? Model = null,
    IReadOnlyDictionary<string, string>? Parameters = null);

/// <summary>One retained input a value was built from, so the value can be rebuilt from the same evidence.</summary>
public sealed record SecurityValuationInputDto(
    string Kind,
    string Reference,
    Guid? Id = null,
    long? Version = null,
    string? ContentSha256 = null,
    DateTimeOffset? AsOf = null,
    DateTimeOffset? RecordedAt = null);

/// <summary>Why no value could be produced. When any blocker is present no number is substituted.</summary>
public sealed record SecurityValuationBlockerDto(string Code, string Reason);

/// <summary>
/// The outcome of valuing a security. <paramref name="Price"/> is the per-unit quote in
/// <paramref name="PriceUnit"/> when one exists; <paramref name="PresentValue"/> the model's PV on
/// the projected principal or notional basis; <paramref name="ObservedAsOf"/> the oldest market
/// input used (the selected price's as-of for a quote; the earlier of the discount curve's as-of and
/// every forward curve's as-of the projection read for discounted cash flow). When <see cref="IsBlocked"/>, <paramref name="Price"/>, <paramref name="PresentValue"/>
/// and <paramref name="MarketValue"/> are null; zero is never substituted.
/// </summary>
public sealed record SecurityValuationResult(
    Guid SecurityId,
    SecurityValuationModelKind Model,
    string ModelKey,
    string ModelVersion,
    DateTimeOffset AsOf,
    DateTimeOffset KnowledgeAsOf,
    string Currency,
    decimal? Price,
    SecurityPriceUnit PriceUnit,
    decimal? PresentValue,
    decimal? Quantity,
    decimal? MarketValue,
    DateTimeOffset? ObservedAsOf,
    SecurityValuationObservability Observability,
    IReadOnlyList<SecurityValuationInputDto> Inputs,
    IReadOnlyList<string> Diagnostics,
    IReadOnlyList<SecurityValuationBlockerDto> Blockers)
{
    public bool IsBlocked => Blockers.Count > 0;

    /// <summary>A blocked result for <paramref name="model"/>: no price, value or market value, and the blockers given.</summary>
    public static SecurityValuationResult Blocked(
        SecurityValuationRequest request,
        SecurityDetailDto security,
        SecurityValuationModelKind model,
        string modelKey,
        string modelVersion,
        DateTimeOffset knowledgeAsOf,
        IReadOnlyList<SecurityValuationBlockerDto> blockers,
        IReadOnlyList<SecurityValuationInputDto>? inputs = null,
        IReadOnlyList<string>? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(security);
        ArgumentNullException.ThrowIfNull(blockers);
        if (blockers.Count == 0)
            throw new ArgumentException("A blocked result names at least one blocker.", nameof(blockers));

        return new SecurityValuationResult(
            security.SecurityId, model, modelKey, modelVersion, request.AsOf, knowledgeAsOf, security.Currency,
            Price: null, SecurityPriceUnit.Unspecified, PresentValue: null, request.Quantity, MarketValue: null,
            ObservedAsOf: null, SecurityValuationObservability.UnobservableInputs,
            inputs ?? [], diagnostics ?? [], blockers);
    }
}

/// <summary>One valuation model. The service dispatches to the registered model whose <see cref="Kind"/> the request or catalog names.</summary>
public interface ISecurityValuationModel
{
    SecurityValuationModelKind Kind { get; }

    /// <summary>A stable key such as <c>quoted-price</c> or <c>discounted-cash-flow</c>.</summary>
    string ModelKey { get; }

    /// <summary>The model's version, such as <c>v1</c>; changes when the arithmetic changes.</summary>
    string ModelVersion { get; }

    Task<SecurityValuationResult> ValueAsync(SecurityValuationRequest request, SecurityDetailDto security, CancellationToken ct);
}

/// <summary>Values a security with the model its request or asset class names.</summary>
public interface ISecurityValuationService
{
    /// <summary>Null when the security is unknown; a blocked result otherwise, never an exception for missing inputs.</summary>
    Task<SecurityValuationResult?> ValueAsync(SecurityValuationRequest request, CancellationToken ct = default);
}
