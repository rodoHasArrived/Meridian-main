using System.Text.Json;
using System.Text.Json.Serialization;
using static Meridian.Contracts.Text.TextPrimitives;

namespace Meridian.Contracts.SecurityMaster;

/// <summary>A retained servicing fact on one bilateral contract, separate from its shared instrument.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<OtcContractLifecycleKind>))]
public enum OtcContractLifecycleKind { RateReset, Compression, Novation, CreditEvent, AuctionSettlement }

/// <summary>The contractual credit trigger determined by the external evidence.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<OtcCreditEventKind>))]
public enum OtcCreditEventKind
{
    Bankruptcy, FailureToPay, Restructuring, ObligationAcceleration, ObligationDefault,
    RepudiationMoratorium, GovernmentalIntervention
}

/// <summary>Version-one lifecycle data. Unknown discriminators fail deserialization rather than becoming another event.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(OtcRateResetData), "rateReset")]
[JsonDerivedType(typeof(OtcCompressionData), "compression")]
[JsonDerivedType(typeof(OtcNovationData), "novation")]
[JsonDerivedType(typeof(OtcCreditEventData), "creditEvent")]
[JsonDerivedType(typeof(OtcAuctionSettlementData), "auctionSettlement")]
public abstract record OtcContractLifecycleData;

/// <summary>A fixing for one contract leg. IndexRate is an annual fraction, including an explicit zero or negative rate.</summary>
public sealed record OtcRateResetData([property: JsonRequired] string LegId, [property: JsonRequired] string IndexName, [property: JsonRequired] DateOnly FixingDate, [property: JsonRequired] decimal IndexRate) : OtcContractLifecycleData;

/// <summary>One contract's externally agreed compression result; zero remaining notional closes it. No other contract is modified.</summary>
public sealed record OtcCompressionData([property: JsonRequired] string CompressionReference, [property: JsonRequired] decimal RemainingNotional) : OtcContractLifecycleData;

/// <summary>A complete novation to a separately booked successor contract; no successor is fabricated.</summary>
public sealed record OtcNovationData([property: JsonRequired] Guid SuccessorContractId) : OtcContractLifecycleData;

/// <summary>An externally determined credit trigger. Recording it does not invent an auction price or settle the contract.</summary>
public sealed record OtcCreditEventData([property: JsonRequired] OtcCreditEventKind CreditEvent, [property: JsonRequired] string ReferenceEntity, [property: JsonRequired] string DeterminationReference) : OtcContractLifecycleData;

/// <summary>Full CDS auction settlement linked to its retained credit trigger. Amount is signed from the fund's side.</summary>
public sealed record OtcAuctionSettlementData([property: JsonRequired] Guid CreditEventId, [property: JsonRequired] DateOnly AuctionDate, [property: JsonRequired] decimal FinalPricePercent,
    [property: JsonRequired] decimal SettlementAmount, [property: JsonRequired] string Currency, [property: JsonRequired] int SettlementScale, [property: JsonRequired] string AuctionReference) : OtcContractLifecycleData;

/// <summary>An idempotent lifecycle write fenced on the current contract version. Endpoints replace Actor with the session identity.</summary>
public sealed record AppendOtcContractLifecycleRequest([property: JsonRequired] Guid EventId, [property: JsonRequired] Guid ContractId, [property: JsonRequired] long ExpectedVersion,
    [property: JsonRequired] DateOnly EffectiveDate, [property: JsonRequired] string SourceReference, [property: JsonRequired] OtcContractLifecycleData Data, string? Actor = null);

/// <summary>Immutable lifecycle evidence, including the exact predecessor and deterministic successor snapshots.</summary>
public sealed record OtcContractLifecycleEventDto([property: JsonRequired] Guid EventId, [property: JsonRequired] int PayloadVersion, [property: JsonRequired] OtcContractLifecycleKind Kind,
    [property: JsonRequired] DateOnly EffectiveDate, [property: JsonRequired] string SourceReference, [property: JsonRequired] string Actor, [property: JsonRequired] DateTimeOffset RecordedAt,
    [property: JsonRequired] OtcContractDto Before, [property: JsonRequired] OtcContractDto After, [property: JsonRequired] OtcContractLifecycleData Data, OtcContractDto? NovationSuccessor = null, OtcContractLifecycleTarget? Target = null);

/// <summary>The exact instrument revision and contractual target validated when a servicing fact was recorded.</summary>
public sealed record OtcContractLifecycleTarget(
    [property: JsonRequired] Guid SecurityId, [property: JsonRequired] long SecurityVersion,
    [property: JsonRequired] string AssetClass, string? LegId = null, string? IndexName = null, string? ReferenceEntity = null);

/// <summary>Pure write and replay rules. Lifecycle records do not themselves authorize a ledger posting or transfer of cash.</summary>
public static class OtcContractLifecycleRules
{
    public const int CurrentPayloadVersion = 1;

    private static readonly JsonSerializerOptions PayloadOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static AppendOtcContractLifecycleRequest Normalize(AppendOtcContractLifecycleRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.EventId == Guid.Empty || request.ContractId == Guid.Empty || request.ExpectedVersion < 1 || request.EffectiveDate == default)
            throw new ArgumentException("A lifecycle event requires its id, contract, expected version and effective date.");
        var actor = NormalizeOptional(request.Actor) ?? throw new ArgumentException("A lifecycle event requires the authenticated actor.");
        if (actor.Equals("unknown", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("A lifecycle event requires an identified actor.");
        var source = Require(request.SourceReference, "source reference");
        OtcContractLifecycleData data = request.Data switch
        {
            OtcRateResetData reset when reset.FixingDate != default && reset.FixingDate <= request.EffectiveDate
                => reset with { LegId = Require(reset.LegId, "reset leg"), IndexName = Require(reset.IndexName, "reset index") },
            OtcCompressionData compression when compression.RemainingNotional >= 0m
                => compression with { CompressionReference = Require(compression.CompressionReference, "compression reference") },
            OtcNovationData novation when novation.SuccessorContractId != Guid.Empty && novation.SuccessorContractId != request.ContractId
                => novation,
            OtcCreditEventData credit when Enum.IsDefined(credit.CreditEvent)
                => credit with { ReferenceEntity = Require(credit.ReferenceEntity, "reference entity"), DeterminationReference = Require(credit.DeterminationReference, "credit determination") },
            OtcAuctionSettlementData auction when auction.CreditEventId != Guid.Empty && auction.AuctionDate != default
                && auction.AuctionDate <= request.EffectiveDate && auction.FinalPricePercent is >= 0m and <= 100m
                && auction.SettlementScale is >= 0 and <= 8
                => auction with { Currency = OtcContractRules.RequireCurrency(auction.Currency), AuctionReference = Require(auction.AuctionReference, "auction reference") },
            _ => throw new ArgumentException("Lifecycle data is missing, unknown or invalid for its event type.")
        };
        return request with { Actor = actor, SourceReference = source, Data = data };
    }

    public static OtcContractLifecycleKind KindOf(OtcContractLifecycleData data) => data switch
    {
        OtcRateResetData => OtcContractLifecycleKind.RateReset,
        OtcCompressionData => OtcContractLifecycleKind.Compression,
        OtcNovationData => OtcContractLifecycleKind.Novation,
        OtcCreditEventData => OtcContractLifecycleKind.CreditEvent,
        OtcAuctionSettlementData => OtcContractLifecycleKind.AuctionSettlement,
        _ => throw new ArgumentException("Unknown lifecycle data.", nameof(data))
    };

    /// <summary>Builds a retained event from a confirmed predecessor, validated prior history and any novation successor.</summary>
    public static OtcContractLifecycleEventDto Create(OtcContractDto current, AppendOtcContractLifecycleRequest request,
        DateTimeOffset recordedAt, IReadOnlyList<OtcContractLifecycleEventDto>? prior = null, OtcContractDto? successor = null, OtcContractLifecycleTarget? target = null)
    {
        ArgumentNullException.ThrowIfNull(current);
        if (recordedAt == default || !Enum.IsDefined(current.Direction) || current.Notional <= 0m)
            throw new ArgumentException("Lifecycle evidence requires a recording time and valid predecessor economics.");
        var normalized = Normalize(request);
        if (normalized.EffectiveDate > DateOnly.FromDateTime(recordedAt.UtcDateTime))
            throw new ArgumentException("A retained lifecycle event cannot take effect after its recording date.");
        if (current.ContractId != normalized.ContractId || current.Version != normalized.ExpectedVersion)
            throw new InvalidOperationException("The contract version changed; reload before recording its lifecycle event.");
        if (current.Status != OtcContractStatus.Confirmed)
            throw new ArgumentException("Lifecycle events require a confirmed, live contract.");
        if (normalized.EffectiveDate < current.EffectiveDate || normalized.Data is not OtcAuctionSettlementData
            && current.TerminationDate is { } termination && normalized.EffectiveDate > termination)
            throw new ArgumentException("A lifecycle event must fall within the contract's effective window.");
        var history = prior ?? [];
        for (var index = 0; index < history.Count; index++)
        {
            EnsureShape(history[index]);
            if (history[index].Before.ContractId != current.ContractId
                || index > 0 && history[index - 1].After != history[index].Before)
                throw new ArgumentException("Lifecycle history must be contiguous and belong to this contract.");
        }
        if (history.Count > 0 && history[^1].After != current)
            throw new ArgumentException("Lifecycle history does not end at the current contract snapshot.");
        if (history.Any(item => item.EventId == normalized.EventId))
            throw new ArgumentException("The lifecycle event id already appears in its history.");
        if (history.Any(item => item.Data is OtcCreditEventData) && normalized.Data is not OtcAuctionSettlementData)
            throw new ArgumentException("A credit-triggered contract requires its auction settlement before any further lifecycle change.");
        if (history.Any(item => item.EffectiveDate > normalized.EffectiveDate))
            throw new ArgumentException("A lifecycle event cannot precede already recorded effective history.");
        ValidateTarget(current, normalized.Data, target);
        if (normalized.Data is OtcAuctionSettlementData auctionTarget)
        {
            var creditTarget = history.SingleOrDefault(item => item.EventId == auctionTarget.CreditEventId)?.Target;
            if (creditTarget is not null && !string.Equals(creditTarget.ReferenceEntity, target?.ReferenceEntity, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Auction reference entity contradicts the retained credit determination.");
        }
        var after = Apply(current, normalized, recordedAt, history, successor);
        return new(normalized.EventId, CurrentPayloadVersion, KindOf(normalized.Data), normalized.EffectiveDate,
            normalized.SourceReference, normalized.Actor!, recordedAt, current, after, normalized.Data,
            normalized.Data is OtcNovationData ? successor : null, target);
    }

    /// <summary>Replays a contiguous lifecycle sequence. CRUD gaps, unknown versions and altered before/after snapshots refuse replay.</summary>
    public static OtcContractDto Replay(OtcContractDto initial, IReadOnlyList<OtcContractLifecycleEventDto> events)
    {
        ArgumentNullException.ThrowIfNull(initial);
        ArgumentNullException.ThrowIfNull(events);
        var current = initial;
        var prior = new List<OtcContractLifecycleEventDto>();
        foreach (var item in events)
        {
            EnsureShape(item);
            if (item.Before != current)
                throw new InvalidOperationException("Lifecycle replay does not match the retained predecessor snapshot.");
            var after = Create(current, RequestOf(item), item.RecordedAt, prior, item.NovationSuccessor, item.Target).After;
            if (after != item.After)
                throw new InvalidOperationException("Lifecycle replay does not match the retained successor snapshot.");
            current = after;
            prior.Add(item);
        }
        return current;
    }

    public static string Serialize(OtcContractLifecycleEventDto item)
    {
        EnsureShape(item);
        return JsonSerializer.Serialize(item, PayloadOptions);
    }

    public static OtcContractLifecycleEventDto Deserialize(string json)
    {
        var item = JsonSerializer.Deserialize<OtcContractLifecycleEventDto>(json, PayloadOptions)
            ?? throw new JsonException("Missing lifecycle event payload.");
        EnsureShape(item);
        return item;
    }

    public static bool IsRetry(OtcContractLifecycleEventDto item, AppendOtcContractLifecycleRequest request)
        => RequestOf(item) == Normalize(request);

    private static AppendOtcContractLifecycleRequest RequestOf(OtcContractLifecycleEventDto item)
        => new(item.EventId, item.Before.ContractId, item.Before.Version, item.EffectiveDate, item.SourceReference, item.Data, item.Actor);

    private static void EnsureShape(OtcContractLifecycleEventDto item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.PayloadVersion != CurrentPayloadVersion)
            throw new NotSupportedException($"OTC lifecycle payload version {item.PayloadVersion} is unsupported.");
        if (item.Before is null || item.After is null || item.RecordedAt == default || item.Kind != KindOf(item.Data))
            throw new InvalidOperationException("Invalid lifecycle event envelope.");
        if ((item.Data is OtcNovationData) != (item.NovationSuccessor is not null))
            throw new InvalidOperationException("Novation evidence must retain exactly its validated successor snapshot.");
        _ = Normalize(RequestOf(item));
        ValidateSnapshot(item.Before);
        ValidateSnapshot(item.After);
        ValidateTarget(item.Before, item.Data, item.Target);
        if (item.After.ContractId != item.Before.ContractId || item.After.Version != checked(item.Before.Version + 1))
            throw new InvalidOperationException("Invalid lifecycle event version or contract identity.");
    }

    private static OtcContractDto Apply(OtcContractDto current, AppendOtcContractLifecycleRequest request,
        DateTimeOffset recordedAt, IReadOnlyList<OtcContractLifecycleEventDto> prior, OtcContractDto? successor)
    {
        var after = current with { Version = checked(current.Version + 1), UpdatedBy = request.Actor!, UpdatedAt = recordedAt };
        switch (request.Data)
        {
            case OtcRateResetData reset:
                if (prior.Any(item => item.Data is OtcRateResetData earlier && earlier.LegId == reset.LegId
                    && string.Equals(earlier.IndexName, reset.IndexName, StringComparison.OrdinalIgnoreCase) && earlier.FixingDate == reset.FixingDate))
                    throw new ArgumentException("A fixing for that leg, index and date is already retained; an explicit correction workflow is required.");
                return after;
            case OtcCompressionData compression:
                if (compression.RemainingNotional >= current.Notional)
                    throw new ArgumentException("Compression must reduce the contract's notional.");
                return compression.RemainingNotional == 0m
                    ? after with { Status = OtcContractStatus.Terminated, TerminationDate = request.EffectiveDate }
                    : after with { Notional = compression.RemainingNotional };
            case OtcNovationData:
                {
                    if (successor is null || successor.ContractId != ((OtcNovationData)request.Data).SuccessorContractId
                        || successor.Status != OtcContractStatus.Confirmed || successor.SecurityId != current.SecurityId
                        || successor.Direction != current.Direction || successor.Notional != current.Notional
                        || successor.NotionalCurrency != current.NotionalCurrency || successor.EffectiveDate != request.EffectiveDate
                        || successor.NettingSetId == current.NettingSetId)
                        throw new ArgumentException("Novation requires a separately confirmed successor with matching instrument, direction, notional and currency, effective on the novation date in another netting set.");
                }
                return after with { Status = OtcContractStatus.Novated, TerminationDate = request.EffectiveDate };
            case OtcCreditEventData:
                if (prior.Any(item => item.Data is OtcCreditEventData))
                    throw new ArgumentException("A credit determination is already retained; an explicit correction workflow is required.");
                return after;
            case OtcAuctionSettlementData auction:
                var credit = prior.SingleOrDefault(item => item.EventId == auction.CreditEventId && item.Data is OtcCreditEventData)
                    ?? throw new ArgumentException("Auction settlement must link a retained credit event on this contract.");
                if (auction.AuctionDate < credit.EffectiveDate || auction.Currency != current.NotionalCurrency)
                    throw new ArgumentException("Auction date or settlement currency contradicts the credit event or contract.");
                var amount = decimal.Round(current.Notional * (1m - auction.FinalPricePercent / 100m)
                    * (current.Direction == OtcContractDirection.Buy ? 1m : -1m), auction.SettlementScale, MidpointRounding.ToEven);
                if (auction.SettlementAmount != amount)
                    throw new ArgumentException("Auction settlement amount must equal the signed notional loss at the final auction price, rounded to the explicitly declared settlement scale.");
                return after with { Status = OtcContractStatus.Terminated, TerminationDate = request.EffectiveDate };
            default:
                throw new ArgumentException("Unknown lifecycle data.");
        }
    }

    /// <summary>Validates the target against the authoritative instrument revision, never a caller-supplied fixing target.</summary>
    public static OtcContractLifecycleTarget? TargetFor(OtcContractDto contract, OtcContractLifecycleData data, SecurityDetailDto instrument)
    {
        if (instrument.SecurityId != contract.SecurityId || instrument.Version < 1)
            throw new ArgumentException("The lifecycle instrument revision does not match the contract.");
        if (data is not (OtcRateResetData or OtcCreditEventData or OtcAuctionSettlementData))
            return null;
        var sources = StructuredCashFlowTermsResolver.EnumerateTermSources(instrument).ToArray();
        var reference = SecurityTermReader.ReadString(sources, ["referenceEntity"]);
        OtcContractLifecycleTarget target;
        if (data is OtcRateResetData reset)
        {
            var declared = new HashSet<string>(StringComparer.Ordinal);
            var foundContainer = false;
            foreach (var source in sources)
            {
                foreach (var key in new[] { "legs", "swapLegs", "cashFlowLegs" })
                {
                    if (!SecurityTermReader.TryGetProperty(source, key, out var rows) || rows.ValueKind != JsonValueKind.Array)
                        continue;
                    var objects = rows.EnumerateArray().Where(row => row.ValueKind == JsonValueKind.Object).ToArray();
                    if (objects.Length == 0)
                        continue;
                    foreach (var row in objects)
                        if (SecurityTermReader.ReadString(row, "legId", "id", "name") is { } id)
                            declared.Add(id);
                    foundContainer = true;
                    break;
                }
                if (foundContainer)
                    break;
            }
            var matches = (StructuredCashFlowTermsResolver.Resolve(instrument).Legs ?? [])
                .Where(leg => leg.LegId == reset.LegId && declared.Contains(leg.LegId)).ToArray();
            if (matches.Length != 1 || matches[0].RateKind != CashFlowLegRateKind.Floating
                || !string.Equals(matches[0].IndexName, reset.IndexName, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("A reset must identify one declared floating leg and its contractual index.");
            target = new(instrument.SecurityId, instrument.Version, instrument.AssetClass, matches[0].LegId, matches[0].IndexName);
        }
        else
            target = new(instrument.SecurityId, instrument.Version, instrument.AssetClass, ReferenceEntity: reference);
        ValidateTarget(contract, data, target);
        return target;
    }

    private static void ValidateTarget(OtcContractDto contract, OtcContractLifecycleData data, OtcContractLifecycleTarget? target)
    {
        if (data is not (OtcRateResetData or OtcCreditEventData or OtcAuctionSettlementData))
            return;
        if (target is null || target.SecurityId != contract.SecurityId || target.SecurityVersion < 1
            || !SecurityAssetClassCatalog.TryGet(target.AssetClass, out var descriptor) || !descriptor.TradesAsBilateralContract)
            throw new ArgumentException("The lifecycle event requires a matching retained instrument target.");
        if (data is OtcRateResetData reset && (target.LegId != reset.LegId
            || string.IsNullOrWhiteSpace(target.IndexName) || !string.Equals(target.IndexName, reset.IndexName, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Reset target does not match its contractual floating leg and index.");
        if (data is OtcCreditEventData or OtcAuctionSettlementData)
        {
            if (descriptor.AssetClass != "CreditDefaultSwap" || string.IsNullOrWhiteSpace(target.ReferenceEntity))
                throw new ArgumentException("Credit determination and auction settlement require a CreditDefaultSwap reference entity.");
            if (data is OtcCreditEventData credit && !string.Equals(credit.ReferenceEntity, target.ReferenceEntity, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Credit determination reference entity does not match the instrument.");
        }
    }

    private static void ValidateSnapshot(OtcContractDto snapshot)
    {
        if (snapshot.ContractId == Guid.Empty || snapshot.SecurityId == Guid.Empty || snapshot.NettingSetId == Guid.Empty
            || snapshot.Notional <= 0m || snapshot.Version < 1 || !Enum.IsDefined(snapshot.Direction) || !Enum.IsDefined(snapshot.Status)
            || snapshot.TradeDate == default || snapshot.EffectiveDate == default || snapshot.EffectiveDate < snapshot.TradeDate
            || snapshot.TerminationDate < snapshot.EffectiveDate || snapshot.UpdatedAt == default
            || string.IsNullOrWhiteSpace(snapshot.UpdatedBy) || snapshot.NotionalCurrency != OtcContractRules.RequireCurrency(snapshot.NotionalCurrency))
            throw new InvalidOperationException("Lifecycle evidence contains an invalid contract snapshot.");
    }

    private static string Require(string? value, string noun)
        => NormalizeOptional(value) ?? throw new ArgumentException($"A lifecycle event requires its {noun}.");
}
