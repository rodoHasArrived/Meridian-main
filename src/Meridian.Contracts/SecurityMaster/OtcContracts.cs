using System.Text.Json.Serialization;
using static Meridian.Contracts.Text.TextPrimitives;

namespace Meridian.Contracts.SecurityMaster;

/// <summary>The form of master agreement a counterparty relationship is documented under.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<OtcMasterAgreementType>))]
public enum OtcMasterAgreementType
{
    /// <summary>ISDA 1992 Master Agreement.</summary>
    Isda1992,

    /// <summary>ISDA 2002 Master Agreement.</summary>
    Isda2002,

    /// <summary>Global Master Repurchase Agreement (repo).</summary>
    Gmra,

    /// <summary>Global Master Securities Lending Agreement.</summary>
    Gmsla,

    /// <summary>Any other bilateral master or long-form confirmation.</summary>
    Other
}

/// <summary>Which side of the contract the fund took: it bought (received, paid fixed, bought protection) or sold.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<OtcContractDirection>))]
public enum OtcContractDirection
{
    Buy,
    Sell
}

/// <summary>Where a bilateral contract is in its life. Terminated, novated and matured contracts are closed and no longer amendable.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<OtcContractStatus>))]
public enum OtcContractStatus
{
    /// <summary>Traded, confirmation outstanding.</summary>
    Pending,

    /// <summary>Confirmed with the counterparty and live.</summary>
    Confirmed,

    /// <summary>Terminated early by agreement or event of default.</summary>
    Terminated,

    /// <summary>Novated to another counterparty; the successor is a new contract.</summary>
    Novated,

    /// <summary>Reached its scheduled termination date.</summary>
    Matured
}

/// <summary>
/// A master agreement (ISDA, GMRA, GMSLA) between one of the fund's legal entities and a
/// counterparty. Every bilateral contract hangs off one through its netting set, so close-out
/// netting and collateral terms are facts about the relationship, not copied onto each trade.
/// </summary>
public sealed record OtcMasterAgreementDto(
    Guid AgreementId,
    string Counterparty,
    string? CounterpartyLei,
    string OurEntity,
    Guid? OurEntityId,
    OtcMasterAgreementType AgreementType,
    DateOnly AgreementDate,
    string? GoverningLaw,
    string? Notes,
    long Version,
    string UpdatedBy,
    DateTimeOffset UpdatedAt);

/// <summary>A collateral type a credit support annex accepts, with the haircut applied to its value.</summary>
public sealed record OtcEligibleCollateralDto(string Description, string? AssetClass, decimal HaircutPercent);

/// <summary>
/// A credit support annex (or equivalent) under a master agreement: the thresholds, minimum transfer
/// amount, independent amount, rounding, valuation cadence and eligible collateral that govern
/// margin calls between the two parties.
/// </summary>
public sealed record OtcCollateralAgreementDto(
    Guid CollateralAgreementId,
    Guid MasterAgreementId,
    string Currency,
    decimal ThresholdOurs,
    decimal ThresholdTheirs,
    decimal MinimumTransferAmount,
    decimal? IndependentAmount,
    decimal? RoundingAmount,
    string ValuationFrequency,
    IReadOnlyList<OtcEligibleCollateralDto> EligibleCollateral,
    DateOnly EffectiveDate,
    DateOnly? TerminationDate,
    string? Notes,
    long Version,
    string UpdatedBy,
    DateTimeOffset UpdatedAt);

/// <summary>
/// The set of contracts under one master agreement whose exposures net against each other on
/// close-out, optionally margined under one collateral agreement.
/// </summary>
public sealed record OtcNettingSetDto(
    Guid NettingSetId,
    Guid MasterAgreementId,
    Guid? CollateralAgreementId,
    string Name,
    string? Notes,
    long Version,
    string UpdatedBy,
    DateTimeOffset UpdatedAt);

/// <summary>
/// A bilateral contract: the trade the fund did against a counterparty on an instrument whose
/// economic terms the Security Master holds. The instrument says what the contract is (a 5y
/// payer swap on SOFR); the contract says who it is with, how much, which way, under which
/// netting set, and where it is in its life. Many contracts can share one instrument.
/// </summary>
public sealed record OtcContractDto(
    Guid ContractId,
    Guid SecurityId,
    Guid NettingSetId,
    string? ExternalTradeId,
    OtcContractDirection Direction,
    decimal Notional,
    string NotionalCurrency,
    DateOnly TradeDate,
    DateOnly EffectiveDate,
    DateOnly? TerminationDate,
    OtcContractStatus Status,
    string? ConfirmationReference,
    string? Notes,
    long Version,
    string UpdatedBy,
    DateTimeOffset UpdatedAt);

/// <summary>A netting set with the agreements it sits under and the contracts it holds.</summary>
public sealed record OtcNettingSetViewDto(
    OtcNettingSetDto NettingSet,
    OtcMasterAgreementDto MasterAgreement,
    OtcCollateralAgreementDto? CollateralAgreement,
    IReadOnlyList<OtcContractDto> Contracts);

/// <summary>Creates (<see cref="AgreementId"/> null) or amends a master agreement.</summary>
public sealed record UpsertOtcMasterAgreementRequest(
    Guid? AgreementId,
    string Counterparty,
    string? CounterpartyLei,
    string OurEntity,
    Guid? OurEntityId,
    OtcMasterAgreementType AgreementType,
    DateOnly AgreementDate,
    string? GoverningLaw = null,
    string? Notes = null,
    string? UpdatedBy = null);

/// <summary>Creates (<see cref="CollateralAgreementId"/> null) or amends a collateral agreement under a master agreement.</summary>
public sealed record UpsertOtcCollateralAgreementRequest(
    Guid? CollateralAgreementId,
    Guid MasterAgreementId,
    string Currency,
    decimal ThresholdOurs,
    decimal ThresholdTheirs,
    decimal MinimumTransferAmount,
    decimal? IndependentAmount,
    decimal? RoundingAmount,
    string ValuationFrequency,
    IReadOnlyList<OtcEligibleCollateralDto> EligibleCollateral,
    DateOnly EffectiveDate,
    DateOnly? TerminationDate = null,
    string? Notes = null,
    string? UpdatedBy = null);

/// <summary>Creates (<see cref="NettingSetId"/> null) or amends a netting set.</summary>
public sealed record UpsertOtcNettingSetRequest(
    Guid? NettingSetId,
    Guid MasterAgreementId,
    Guid? CollateralAgreementId,
    string Name,
    string? Notes = null,
    string? UpdatedBy = null);

/// <summary>Creates (<see cref="ContractId"/> null) or amends a bilateral contract.</summary>
public sealed record UpsertOtcContractRequest(
    Guid? ContractId,
    Guid SecurityId,
    Guid NettingSetId,
    string? ExternalTradeId,
    OtcContractDirection Direction,
    decimal Notional,
    string NotionalCurrency,
    DateOnly TradeDate,
    DateOnly EffectiveDate,
    DateOnly? TerminationDate,
    OtcContractStatus Status,
    string? ConfirmationReference = null,
    string? Notes = null,
    string? UpdatedBy = null);

/// <summary>Persistence for master agreements, collateral agreements, netting sets and bilateral contracts.</summary>
public interface IOtcContractStore
{
    Task<OtcMasterAgreementDto> UpsertMasterAgreementAsync(UpsertOtcMasterAgreementRequest request, CancellationToken ct = default);
    Task<OtcMasterAgreementDto?> GetMasterAgreementAsync(Guid agreementId, CancellationToken ct = default);
    Task<IReadOnlyList<OtcMasterAgreementDto>> ListMasterAgreementsAsync(string? counterparty = null, CancellationToken ct = default);

    Task<OtcCollateralAgreementDto> UpsertCollateralAgreementAsync(UpsertOtcCollateralAgreementRequest request, CancellationToken ct = default);
    Task<OtcCollateralAgreementDto?> GetCollateralAgreementAsync(Guid collateralAgreementId, CancellationToken ct = default);

    Task<OtcNettingSetDto> UpsertNettingSetAsync(UpsertOtcNettingSetRequest request, CancellationToken ct = default);
    Task<OtcNettingSetViewDto?> GetNettingSetAsync(Guid nettingSetId, CancellationToken ct = default);

    Task<OtcContractDto> UpsertContractAsync(UpsertOtcContractRequest request, CancellationToken ct = default);
    Task<OtcContractDto?> GetContractAsync(Guid contractId, CancellationToken ct = default);
    Task<IReadOnlyList<OtcContractDto>> ListContractsForSecurityAsync(Guid securityId, CancellationToken ct = default);
}

/// <summary>
/// The rules every write to the contract model satisfies, kept pure so they are testable without a
/// database and identical in every store. Each returns the normalized request or throws
/// <see cref="ArgumentException"/> naming the rule.
/// </summary>
public static class OtcContractRules
{
    public static UpsertOtcMasterAgreementRequest Normalize(UpsertOtcMasterAgreementRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var counterparty = NormalizeOptional(request.Counterparty) ?? throw new ArgumentException("A master agreement must name the counterparty.");
        var ourEntity = NormalizeOptional(request.OurEntity) ?? throw new ArgumentException("A master agreement must name the fund's legal entity.");
        var lei = NormalizeOptional(request.CounterpartyLei);
        if (lei is not null && lei.Length != 20)
            throw new ArgumentException("A Legal Entity Identifier is 20 characters.", nameof(request));
        return request with
        {
            Counterparty = counterparty,
            CounterpartyLei = lei?.ToUpperInvariant(),
            OurEntity = ourEntity,
            GoverningLaw = NormalizeOptional(request.GoverningLaw),
            Notes = NormalizeOptional(request.Notes),
            UpdatedBy = Actor(request.UpdatedBy)
        };
    }

    public static UpsertOtcCollateralAgreementRequest Normalize(UpsertOtcCollateralAgreementRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.MasterAgreementId == Guid.Empty)
            throw new ArgumentException("A collateral agreement sits under a master agreement.", nameof(request));
        var currency = RequireCurrency(request.Currency);
        if (request.ThresholdOurs < 0m || request.ThresholdTheirs < 0m)
            throw new ArgumentException("A threshold cannot be negative.", nameof(request));
        if (request.MinimumTransferAmount < 0m)
            throw new ArgumentException("The minimum transfer amount cannot be negative.", nameof(request));
        if (request.IndependentAmount is < 0m)
            throw new ArgumentException("An independent amount cannot be negative.", nameof(request));
        if (request.RoundingAmount is <= 0m)
            throw new ArgumentException("A rounding amount must be positive.", nameof(request));
        if (request.TerminationDate is { } termination && termination < request.EffectiveDate)
            throw new ArgumentException("A collateral agreement cannot terminate before it takes effect.", nameof(request));
        var frequency = NormalizeOptional(request.ValuationFrequency) ?? throw new ArgumentException("A collateral agreement states how often exposure is valued.");

        var eligible = new List<OtcEligibleCollateralDto>();
        foreach (var item in request.EligibleCollateral ?? [])
        {
            var description = NormalizeOptional(item.Description) ?? throw new ArgumentException("Eligible collateral must be described.");
            if (item.HaircutPercent is < 0m or > 100m)
                throw new ArgumentException($"The haircut on '{description}' must be between 0 and 100 percent.", nameof(request));
            eligible.Add(new OtcEligibleCollateralDto(description, NormalizeOptional(item.AssetClass), item.HaircutPercent));
        }

        return request with
        {
            Currency = currency,
            ValuationFrequency = frequency,
            EligibleCollateral = eligible,
            Notes = NormalizeOptional(request.Notes),
            UpdatedBy = Actor(request.UpdatedBy)
        };
    }

    public static UpsertOtcNettingSetRequest Normalize(UpsertOtcNettingSetRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.MasterAgreementId == Guid.Empty)
            throw new ArgumentException("A netting set sits under a master agreement.", nameof(request));
        return request with
        {
            Name = NormalizeOptional(request.Name) ?? throw new ArgumentException("A netting set must be named."),
            Notes = NormalizeOptional(request.Notes),
            UpdatedBy = Actor(request.UpdatedBy)
        };
    }

    public static UpsertOtcContractRequest Normalize(UpsertOtcContractRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.SecurityId == Guid.Empty)
            throw new ArgumentException("A contract must name the instrument it trades.", nameof(request));
        if (request.NettingSetId == Guid.Empty)
            throw new ArgumentException("A contract must belong to a netting set.", nameof(request));
        if (request.Notional <= 0m)
            throw new ArgumentException("A contract's notional must be positive.", nameof(request));
        if (request.EffectiveDate < request.TradeDate)
            throw new ArgumentException("A contract cannot take effect before it is traded.", nameof(request));
        if (request.TerminationDate is { } termination && termination < request.EffectiveDate)
            throw new ArgumentException("A contract cannot terminate before it takes effect.", nameof(request));
        return request with
        {
            ExternalTradeId = NormalizeOptional(request.ExternalTradeId),
            NotionalCurrency = RequireCurrency(request.NotionalCurrency),
            ConfirmationReference = NormalizeOptional(request.ConfirmationReference),
            Notes = NormalizeOptional(request.Notes),
            UpdatedBy = Actor(request.UpdatedBy)
        };
    }

    /// <summary>Whether the asset class the instrument belongs to is traded as a bilateral contract.</summary>
    public static void RequireBilateralAssetClass(string assetClass)
    {
        if (!SecurityAssetClassCatalog.GetOrDefault(assetClass).TradesAsBilateralContract)
            throw new ArgumentException($"Asset class '{assetClass}' is not traded as a bilateral contract; only instruments the catalog marks as such carry contracts.", nameof(assetClass));
    }

    /// <summary>Terminated, novated and matured contracts are closed: they are history, not state to amend.</summary>
    public static bool IsClosed(OtcContractStatus status)
        => status is OtcContractStatus.Terminated or OtcContractStatus.Novated or OtcContractStatus.Matured;

    /// <summary>A three-letter ISO currency code, upper-cased; shared with the collateral rules so both spell currency the same way.</summary>
    internal static string RequireCurrency(string? value)
    {
        var currency = NormalizeOptional(value);
        if (currency is null || currency.Length != 3 || !currency.All(char.IsAsciiLetter))
            throw new ArgumentException("A currency is a three-letter ISO code.");
        return currency.ToUpperInvariant();
    }

    private static string Actor(string? value) => string.IsNullOrWhiteSpace(value) ? "unknown" : value.Trim();
}

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(OtcEligibleCollateralDto[]))]
public sealed partial class OtcContractsJsonContext : JsonSerializerContext;
