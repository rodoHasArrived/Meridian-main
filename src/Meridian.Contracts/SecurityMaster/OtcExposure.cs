using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Operations;

namespace Meridian.Contracts.SecurityMaster;

/// <summary>
/// Values one bilateral contract as of a date in a stated currency, with provenance, or says why
/// it cannot. The mark is signed from the fund's side with the contract's direction applied:
/// positive means the counterparty owes the fund. An unvalued result carries no mark and at least
/// one blocker, never zero.
/// </summary>
public interface IOtcContractValuationSource
{
    Task<OtcContractValuationDto> ValueAsync(OtcContractValuationRequest request, CancellationToken ct = default);
}

/// <summary>What to value: the contract, the date and the currency the exposure is computed in.</summary>
public sealed record OtcContractValuationRequest(
    OtcContractDto Contract,
    DateOnly AsOfDate,
    string Currency,
    DateTimeOffset? KnowledgeAsOf = null);

/// <summary>Whether a contract could be valued.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<OtcValuationStatus>))]
public enum OtcValuationStatus
{
    Valued,
    Unvalued
}

/// <summary>One contract's mark on one date, or the reasons it has none.</summary>
public sealed record OtcContractValuationDto(
    Guid ContractId,
    Guid SecurityId,
    DateOnly AsOfDate,
    OtcValuationStatus Status,
    decimal? MarkToMarket,
    string Currency,
    string Method,
    IReadOnlyList<OtcValuationInputDto> Inputs,
    IReadOnlyList<OtcValuationBlockerDto> Blockers,
    DataProvenance Provenance);

/// <summary>An observation a valuation used, with where it came from and when it was observed.</summary>
public sealed record OtcValuationInputDto(
    string Kind,
    string Source,
    DateTimeOffset AsOf,
    string? Reference,
    DataProvenance Provenance);

/// <summary>Why a contract, or a snapshot, could not be completed.</summary>
public sealed record OtcValuationBlockerDto(string Code, string Reason);

/// <summary>The blocker codes the exposure rules and the first valuation source emit.</summary>
public static class OtcValuationBlockerCodes
{
    /// <summary>No price or mark is recorded for the instrument as of the date.</summary>
    public const string MissingInput = "MissingInput";

    /// <summary>The recorded price is not quoted as a percent of notional.</summary>
    public const string UnitMismatch = "UnitMismatch";

    /// <summary>The pricing hierarchy fell back to an observation older than its staleness limit.</summary>
    public const string StaleInput = "StaleInput";

    /// <summary>The contract is denominated in a currency other than the computation currency.</summary>
    public const string CurrencyMismatch = "CurrencyMismatch";

    /// <summary>The valuation source returned nothing for a contract the snapshot considers.</summary>
    public const string MissingValuation = "MissingValuation";

    /// <summary>An open contract whose scheduled termination date has passed; nothing matures contracts automatically.</summary>
    public const string PastTermination = "PastTermination";

    /// <summary>The netting set has no collateral agreement, so no credit support can be computed.</summary>
    public const string NoCollateralAgreement = "NoCollateralAgreement";

    /// <summary>The collateral agreement terminated before the as-of date.</summary>
    public const string CollateralAgreementTerminated = "CollateralAgreementTerminated";

    /// <summary>The collateral agreement takes effect after the as-of date, so its thresholds and independent amount do not yet apply.</summary>
    public const string CollateralAgreementNotYetEffective = "CollateralAgreementNotYetEffective";

    /// <summary>
    /// The collateral agreement is also referenced by another netting set; its balances are not
    /// allocated across netting sets, so crediting them to each would count the same collateral twice.
    /// </summary>
    public const string SharedCollateralAgreement = "SharedCollateralAgreement";

    /// <summary>
    /// A positive collateral balance is valued after the as-of date. Balances are current-state
    /// records, so what was held on the earlier date is unknown.
    /// </summary>
    public const string CollateralValuationAfterAsOf = "CollateralValuationAfterAsOf";

    /// <summary>A collateral balance's description is no longer an eligible-collateral line of the agreement, so no haircut applies to it.</summary>
    public const string IneligibleCollateral = "IneligibleCollateral";

    /// <summary>
    /// A positive collateral balance is held in a currency other than the computation currency (for
    /// example after the agreement's currency was amended); this increment does no FX.
    /// </summary>
    public const string CollateralCurrencyMismatch = "CollateralCurrencyMismatch";
}

/// <summary>How the contracts in a netting set are combined into one exposure.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<OtcNettingBasis>))]
public enum OtcNettingBasis
{
    /// <summary>The master agreement permits close-out netting: exposure is the signed sum of marks.</summary>
    CloseOutNet,

    /// <summary>Netting is not relied on: each side's exposure is the sum of the marks in its favour.</summary>
    Gross
}

/// <summary>Whether every considered contract was valued and no gate blocked the credit support computation.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<OtcExposureSnapshotStatus>))]
public enum OtcExposureSnapshotStatus
{
    Complete,
    Incomplete
}

/// <summary>A collateral transfer the credit support terms imply: who moves it and whether it is a delivery or a return.</summary>
public sealed record OtcCollateralMovementDto(
    OtcMovementKind Kind,
    OtcMovementParty Mover,
    decimal Amount,
    string Currency);

/// <summary>
/// One collateral balance as the snapshot saw it, with the haircut applied: the haircut of the
/// agreement's eligible-collateral line at computation time, not the one copied onto the balance.
/// </summary>
public sealed record OtcCollateralHeldLineDto(
    Guid BalanceId,
    OtcCollateralDirection Direction,
    OtcCollateralKind Kind,
    string Description,
    decimal ValuedAmount,
    decimal HaircutPercent,
    decimal HaircutAdjusted,
    DateOnly ValuedAsOf);

/// <summary>
/// The credit support arithmetic from one party's perspective as Secured Party, with the other
/// party as Pledgor: exposure, the independent amounts and threshold that apply, the resulting
/// credit support amount, what the Secured Party already holds, the raw delivery and return, and
/// the movement after the minimum transfer amount and rounding.
/// </summary>
public sealed record OtcCreditSupportSideDto(
    OtcMovementParty SecuredParty,
    decimal Exposure,
    decimal IndependentAmountPledgor,
    decimal IndependentAmountSecuredParty,
    decimal ThresholdPledgor,
    decimal CreditSupportAmount,
    decimal Held,
    decimal DeliveryRaw,
    decimal ReturnRaw,
    OtcCollateralMovementDto? Movement);

/// <summary>
/// The append-only valuation of one netting set on one date: the contracts valued, the netting
/// basis, gross and net exposure, the collateral held on each side, the credit support each side's
/// terms imply and the movements that follow. Recomputing a date appends the next version. With no
/// collateral agreement the snapshot reports exposures only and both credit support sides are null.
/// </summary>
public sealed record OtcExposureSnapshotDto(
    Guid SnapshotId,
    Guid NettingSetId,
    Guid MasterAgreementId,
    Guid? CollateralAgreementId,
    DateOnly AsOfDate,
    int Version,
    string RuleVersion,
    string Currency,
    OtcNettingBasis NettingBasis,
    OtcExposureSnapshotStatus Status,
    DataProvenance Provenance,
    long MasterAgreementVersion,
    long? CollateralAgreementVersion,
    IReadOnlyList<OtcContractValuationDto> Contracts,
    decimal GrossPositive,
    decimal GrossNegative,
    decimal NetExposure,
    OtcCreditSupportSideDto? FundAsSecuredParty,
    OtcCreditSupportSideDto? CounterpartyAsSecuredParty,
    IReadOnlyList<OtcCollateralHeldLineDto> CollateralHeld,
    IReadOnlyList<OtcCollateralMovementDto> Movements,
    IReadOnlyList<OtcValuationBlockerDto> Blockers,
    string PayloadSha256,
    string ComputedBy,
    DateTimeOffset ComputedAt);

/// <summary>Asks for a netting set's exposure to be computed and recorded as of a date.</summary>
public sealed record ComputeOtcExposureSnapshotRequest(DateOnly AsOfDate, DateTimeOffset? KnowledgeAsOf = null);

/// <summary>Computes and records exposure snapshots for a netting set.</summary>
public interface IOtcExposureService
{
    /// <summary>
    /// Values the netting set's open contracts as of <paramref name="asOfDate"/>, applies its
    /// credit support terms and appends the result as the next snapshot version for that date.
    /// Every contract is valued at one knowledge time: <paramref name="knowledgeAsOf"/>, else the
    /// instant the snapshot is stamped as computed. Returns null when the netting set does not
    /// exist; an empty netting set id, a default as-of date or a blank actor is an
    /// <see cref="ArgumentException"/>.
    /// </summary>
    Task<OtcExposureSnapshotDto?> ComputeAsync(Guid nettingSetId, DateOnly asOfDate, string actor, DateTimeOffset? knowledgeAsOf = null, CancellationToken ct = default);
}

/// <summary>
/// Exposure and credit support as pure functions, in the way <see cref="OtcContractRules"/> is
/// pure: no clock, no I/O, <see cref="ArgumentException"/> naming the rule. Every amount is in the
/// collateral agreement currency and every mark is signed from the fund's side.
/// </summary>
public static class OtcExposureRules
{
    /// <summary>Stamped on every snapshot so a reader knows which arithmetic produced it.</summary>
    public const string RuleVersion = "otc-exposure-rules-v1";

    /// <summary>
    /// Whether the master agreement form permits close-out netting. <c>Other</c> is treated as not
    /// netting: the model records no netting opinion, so it fails closed to gross exposure.
    /// </summary>
    public static bool PermitsCloseOutNetting(OtcMasterAgreementType agreementType)
        => agreementType is OtcMasterAgreementType.Isda1992 or OtcMasterAgreementType.Isda2002 or OtcMasterAgreementType.Gmra or OtcMasterAgreementType.Gmsla;

    /// <summary>Whether a contract enters the exposure as of a date: open and already traded.</summary>
    public static bool EntersExposure(OtcContractDto contract, DateOnly asOfDate)
    {
        ArgumentNullException.ThrowIfNull(contract);
        return !OtcContractRules.IsClosed(contract.Status) && contract.TradeDate <= asOfDate;
    }

    /// <summary>The contracts that enter the exposure as of a date, in their given order.</summary>
    public static IReadOnlyList<OtcContractDto> SelectContracts(IEnumerable<OtcContractDto> contracts, DateOnly asOfDate)
    {
        ArgumentNullException.ThrowIfNull(contracts);
        return contracts.Where(contract => EntersExposure(contract, asOfDate)).ToArray();
    }

    /// <summary>
    /// The currency the exposure is computed in: the collateral agreement's, or, with no collateral
    /// agreement, the notional currency of the first contract that enters the exposure as of the
    /// date (exposures only), so a closed or not-yet-traded contract cannot set it. When no contract
    /// enters, the first contract's currency is used; nothing is valued in it.
    /// </summary>
    public static string ResolveCurrency(OtcNettingSetViewDto view, DateOnly asOfDate)
    {
        ArgumentNullException.ThrowIfNull(view);
        if (view.CollateralAgreement is { } csa)
            return csa.Currency;
        var source = SelectContracts(view.Contracts, asOfDate).FirstOrDefault() ?? view.Contracts.FirstOrDefault();
        return source?.NotionalCurrency
            ?? throw new ArgumentException("The netting set has no collateral agreement and no contracts, so there is no currency to compute exposure in.", nameof(view));
    }

    /// <summary>The value of a collateral balance after its haircut.</summary>
    public static decimal HaircutAdjusted(decimal valuedAmount, decimal haircutPercent)
        => valuedAmount * (1m - haircutPercent / 100m);

    /// <summary>Rounds a delivery up to the rounding amount; no rounding amount leaves it as is.</summary>
    public static decimal RoundUp(decimal amount, decimal? roundingAmount)
        => roundingAmount is { } rounding && rounding > 0m ? Math.Ceiling(amount / rounding) * rounding : amount;

    /// <summary>Rounds a return down to the rounding amount; no rounding amount leaves it as is.</summary>
    public static decimal RoundDown(decimal amount, decimal? roundingAmount)
        => roundingAmount is { } rounding && rounding > 0m ? Math.Floor(amount / rounding) * rounding : amount;

    /// <summary>
    /// The credit support amount owed to a Secured Party: its exposure plus the Pledgor's
    /// independent amount, less the Secured Party's own independent amount and the Pledgor's
    /// threshold, floored at zero.
    /// </summary>
    public static decimal CreditSupportAmount(decimal exposure, decimal independentAmountPledgor, decimal independentAmountSecuredParty, decimal thresholdPledgor)
        => Math.Max(0m, exposure + independentAmountPledgor - independentAmountSecuredParty - thresholdPledgor);

    /// <summary>
    /// The movement one side's arithmetic implies. The minimum transfer amount is tested on the
    /// unrounded delivery or return; a delivery then rounds up and a return rounds down, and a
    /// return that rounds to nothing is no movement. The Pledgor delivers; the Secured Party returns.
    /// </summary>
    public static OtcCollateralMovementDto? Movement(
        decimal deliveryRaw,
        decimal returnRaw,
        decimal minimumTransferAmount,
        decimal? roundingAmount,
        OtcMovementParty pledgor,
        OtcMovementParty securedParty,
        string currency)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currency);
        if (deliveryRaw > 0m && deliveryRaw >= minimumTransferAmount)
        {
            var amount = RoundUp(deliveryRaw, roundingAmount);
            return amount > 0m ? new OtcCollateralMovementDto(OtcMovementKind.Delivery, pledgor, amount, currency) : null;
        }

        if (returnRaw > 0m && returnRaw >= minimumTransferAmount)
        {
            var amount = RoundDown(returnRaw, roundingAmount);
            return amount > 0m ? new OtcCollateralMovementDto(OtcMovementKind.Return, securedParty, amount, currency) : null;
        }

        return null;
    }

    /// <summary>
    /// Computes the exposure snapshot for a netting set as of a date from the valuations of the
    /// contracts that enter it and the collateral balances under its collateral agreement. The
    /// independent amount on the collateral agreement is read as the amount the fund posts, and each
    /// balance takes the haircut of the agreement's eligible-collateral line it is posted as. Any
    /// unvalued contract, a missing, not-yet-effective, terminated or shared collateral agreement,
    /// an open contract past its termination date, or a positive balance valued after the as-of
    /// date or no longer eligible leaves the snapshot <see cref="OtcExposureSnapshotStatus.Incomplete"/>:
    /// exposures are reported for what was valued and no movement is computed. With no collateral
    /// agreement both credit support sides are null. <c>nettingSetsUnderAgreement</c> is every
    /// netting set that references the collateral agreement, as the store reports it (empty with no
    /// agreement); any netting set other than this one is a
    /// <see cref="OtcValuationBlockerCodes.SharedCollateralAgreement"/> blocker. The returned
    /// snapshot carries no version and no digest; the store assigns both.
    /// </summary>
    public static OtcExposureSnapshotDto Compute(
        OtcNettingSetViewDto view,
        IReadOnlyList<OtcContractValuationDto> valuations,
        IReadOnlyList<OtcCollateralBalanceDto> balances,
        IReadOnlyCollection<Guid> nettingSetsUnderAgreement,
        DateOnly asOfDate,
        string computedBy,
        DateTimeOffset computedAt)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(valuations);
        ArgumentNullException.ThrowIfNull(balances);
        ArgumentNullException.ThrowIfNull(nettingSetsUnderAgreement);
        if (string.IsNullOrWhiteSpace(computedBy))
            throw new ArgumentException("An exposure snapshot names who computed it.", nameof(computedBy));

        var currency = ResolveCurrency(view, asOfDate);
        var csa = view.CollateralAgreement;
        var blockers = new List<OtcValuationBlockerDto>();
        if (csa is null)
            blockers.Add(new OtcValuationBlockerDto(OtcValuationBlockerCodes.NoCollateralAgreement, "The netting set has no collateral agreement; exposures are reported without credit support."));
        else if (csa.TerminationDate is { } csaTermination && csaTermination < asOfDate)
            blockers.Add(new OtcValuationBlockerDto(OtcValuationBlockerCodes.CollateralAgreementTerminated, $"Collateral agreement {csa.CollateralAgreementId:D} terminated on {csaTermination:yyyy-MM-dd}, before {asOfDate:yyyy-MM-dd}."));
        else if (csa.EffectiveDate > asOfDate)
            blockers.Add(new OtcValuationBlockerDto(OtcValuationBlockerCodes.CollateralAgreementNotYetEffective, $"Collateral agreement {csa.CollateralAgreementId:D} takes effect on {csa.EffectiveDate:yyyy-MM-dd}, after {asOfDate:yyyy-MM-dd}; its thresholds and independent amount do not yet apply."));

        if (csa is not null)
        {
            var sharedWith = nettingSetsUnderAgreement
                .Where(id => id != view.NettingSet.NettingSetId)
                .Distinct()
                .Order()
                .ToArray();
            if (sharedWith.Length > 0)
                blockers.Add(new OtcValuationBlockerDto(OtcValuationBlockerCodes.SharedCollateralAgreement, $"Collateral agreement {csa.CollateralAgreementId:D} is also referenced by netting set(s) {string.Join(", ", sharedWith.Select(id => id.ToString("D")))}; credit support is not allocated across netting sets in this increment."));
        }

        var selected = SelectContracts(view.Contracts, asOfDate);
        var byContract = valuations
            .GroupBy(valuation => valuation.ContractId)
            .ToDictionary(group => group.Key, group => group.First());
        var contractValuations = new List<OtcContractValuationDto>(selected.Count);
        decimal grossPositive = 0m, grossNegative = 0m, net = 0m;
        var allValued = true;
        foreach (var contract in selected)
        {
            if (contract.TerminationDate is { } termination && termination < asOfDate)
                blockers.Add(new OtcValuationBlockerDto(OtcValuationBlockerCodes.PastTermination, $"Contract {contract.ContractId:D} is {contract.Status} but its termination date {termination:yyyy-MM-dd} has passed; close it or confirm it is still live."));

            var valuation = Reconcile(contract, byContract.GetValueOrDefault(contract.ContractId), asOfDate, currency);
            contractValuations.Add(valuation);
            if (valuation.Status == OtcValuationStatus.Valued && valuation.MarkToMarket is { } mark)
            {
                net += mark;
                if (mark > 0m)
                    grossPositive += mark;
                else
                    grossNegative += -mark;
            }
            else
            {
                allValued = false;
                foreach (var blocker in valuation.Blockers)
                    blockers.Add(new OtcValuationBlockerDto(blocker.Code, $"Contract {contract.ContractId:D}: {blocker.Reason}"));
            }
        }

        var basis = PermitsCloseOutNetting(view.MasterAgreement.AgreementType) ? OtcNettingBasis.CloseOutNet : OtcNettingBasis.Gross;
        var exposureFund = basis == OtcNettingBasis.CloseOutNet ? net : grossPositive;
        // Negating an exact zero would give a negative-zero decimal, which jsonb normalizes away
        // and the digest would then not reproduce from the stored payload.
        var exposureCounterparty = basis == OtcNettingBasis.CloseOutNet ? (net == 0m ? 0m : -net) : grossNegative;

        var held = new List<OtcCollateralHeldLineDto>();
        if (csa is not null)
        {
            foreach (var balance in balances)
            {
                // A fully returned line, or one under another agreement, is not held collateral.
                if (balance.CollateralAgreementId != csa.CollateralAgreementId || balance.Quantity <= 0m)
                    continue;

                // Balances are current-state rows: a positive balance valued after the as-of date
                // says nothing about what was held on that date, so it blocks rather than counting as zero.
                if (balance.ValuedAsOf > asOfDate)
                {
                    blockers.Add(new OtcValuationBlockerDto(OtcValuationBlockerCodes.CollateralValuationAfterAsOf, $"Collateral balance {balance.BalanceId:D} ({balance.EligibleCollateralDescription}) is valued as of {balance.ValuedAsOf:yyyy-MM-dd}, after {asOfDate:yyyy-MM-dd}; what was held on {asOfDate:yyyy-MM-dd} is not recorded."));
                    continue;
                }

                // A balance written under the agreement's earlier currency cannot be credited in the
                // current one without FX, which this increment does not do.
                if (!string.Equals(balance.Currency, currency, StringComparison.OrdinalIgnoreCase))
                {
                    blockers.Add(new OtcValuationBlockerDto(OtcValuationBlockerCodes.CollateralCurrencyMismatch, $"Collateral balance {balance.BalanceId:D} ({balance.EligibleCollateralDescription}) is held in {balance.Currency}; the exposure is computed in {currency} and this increment does no FX."));
                    continue;
                }

                // The haircut is the agreement's current one for the line, not the copy on the balance.
                if (OtcCollateralRules.FindEligible(csa, balance.EligibleCollateralDescription) is not { } eligible)
                {
                    blockers.Add(new OtcValuationBlockerDto(OtcValuationBlockerCodes.IneligibleCollateral, $"Collateral balance {balance.BalanceId:D} is posted as '{balance.EligibleCollateralDescription}', which is no longer eligible under collateral agreement {csa.CollateralAgreementId:D} version {csa.Version}."));
                    continue;
                }

                held.Add(new OtcCollateralHeldLineDto(
                    balance.BalanceId,
                    balance.Direction,
                    balance.Kind,
                    balance.EligibleCollateralDescription,
                    balance.ValuedAmount,
                    eligible.HaircutPercent,
                    HaircutAdjusted(balance.ValuedAmount, eligible.HaircutPercent),
                    balance.ValuedAsOf));
            }
        }

        var complete = allValued && blockers.Count == 0;
        OtcCreditSupportSideDto? fundSide = null, counterpartySide = null;
        var movements = new List<OtcCollateralMovementDto>(2);
        if (csa is not null)
        {
            // With no collateral agreement there are no credit support terms to report: the sides
            // stay null rather than being filled from zero thresholds.
            var heldByFund = held.Where(line => line.Direction == OtcCollateralDirection.Received).Sum(line => line.HaircutAdjusted);
            var heldByCounterparty = held.Where(line => line.Direction == OtcCollateralDirection.Posted).Sum(line => line.HaircutAdjusted);
            var independentAmountOurs = csa.IndependentAmount ?? 0m;
            const decimal independentAmountTheirs = 0m;

            fundSide = Side(
                OtcMovementParty.Fund,
                OtcMovementParty.Counterparty,
                exposureFund,
                independentAmountPledgor: independentAmountTheirs,
                independentAmountSecuredParty: independentAmountOurs,
                thresholdPledgor: csa.ThresholdTheirs,
                heldByFund,
                csa,
                currency,
                complete);
            counterpartySide = Side(
                OtcMovementParty.Counterparty,
                OtcMovementParty.Fund,
                exposureCounterparty,
                independentAmountPledgor: independentAmountOurs,
                independentAmountSecuredParty: independentAmountTheirs,
                thresholdPledgor: csa.ThresholdOurs,
                heldByCounterparty,
                csa,
                currency,
                complete);

            if (fundSide.Movement is { } fundMovement)
                movements.Add(fundMovement);
            if (counterpartySide.Movement is { } counterpartyMovement)
                movements.Add(counterpartyMovement);
        }

        return new OtcExposureSnapshotDto(
            Guid.NewGuid(),
            view.NettingSet.NettingSetId,
            view.MasterAgreement.AgreementId,
            csa?.CollateralAgreementId,
            asOfDate,
            0,
            RuleVersion,
            currency,
            basis,
            complete ? OtcExposureSnapshotStatus.Complete : OtcExposureSnapshotStatus.Incomplete,
            DataProvenanceExtensions.Strongest(contractValuations.Select(valuation => valuation.Provenance)),
            view.MasterAgreement.Version,
            csa?.Version,
            contractValuations,
            grossPositive,
            grossNegative,
            net,
            fundSide,
            counterpartySide,
            held,
            movements,
            blockers,
            string.Empty,
            computedBy.Trim(),
            computedAt);
    }

    /// <summary>
    /// The digest a snapshot is stored and verified with: the canonical serialization of the
    /// snapshot with an empty <see cref="OtcExposureSnapshotDto.PayloadSha256"/>.
    /// </summary>
    public static string Digest(OtcExposureSnapshotDto snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return Sha256Digest.ComputeUtf8(SerializePayload(snapshot));
    }

    /// <summary>The snapshot as its jsonb payload is written: the digest field is empty, so the payload can be digested.</summary>
    public static string SerializePayload(OtcExposureSnapshotDto snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return JsonSerializer.Serialize(snapshot with { PayloadSha256 = string.Empty }, OtcCollateralJsonContext.Default.OtcExposureSnapshotDto);
    }

    private static OtcValuationBlockerDto[] Blocker(string code, string reason) => [new OtcValuationBlockerDto(code, reason)];

    private static OtcContractValuationDto Reconcile(OtcContractDto contract, OtcContractValuationDto? valuation, DateOnly asOfDate, string currency)
    {
        if (valuation is null)
        {
            return new OtcContractValuationDto(
                contract.ContractId, contract.SecurityId, asOfDate, OtcValuationStatus.Unvalued, null, currency, string.Empty, [],
                Blocker(OtcValuationBlockerCodes.MissingValuation, "The valuation source returned nothing for this contract."), DataProvenance.Real);
        }

        if (valuation.Status != OtcValuationStatus.Valued)
        {
            var blockers = valuation.Blockers.Count > 0
                ? valuation.Blockers
                : Blocker(OtcValuationBlockerCodes.MissingInput, "The valuation source reported the contract unvalued without a reason.");
            return valuation with { MarkToMarket = null, Blockers = blockers };
        }

        // A mark for another date or another instrument is not this contract's valuation.
        if (valuation.AsOfDate != asOfDate || valuation.SecurityId != contract.SecurityId)
        {
            var mismatch = valuation.AsOfDate != asOfDate
                ? $"The valuation source returned a mark as of {valuation.AsOfDate:yyyy-MM-dd} for a computation as of {asOfDate:yyyy-MM-dd}."
                : $"The valuation source returned a mark for security {valuation.SecurityId:D}; the contract is on security {contract.SecurityId:D}.";
            return valuation with
            {
                Status = OtcValuationStatus.Unvalued,
                MarkToMarket = null,
                Blockers = Blocker(OtcValuationBlockerCodes.MissingValuation, mismatch)
            };
        }

        if (valuation.MarkToMarket is null)
        {
            return valuation with
            {
                Status = OtcValuationStatus.Unvalued,
                Blockers = Blocker(OtcValuationBlockerCodes.MissingInput, "The valuation source reported a valued contract without a mark.")
            };
        }

        if (!string.Equals(valuation.Currency, currency, StringComparison.OrdinalIgnoreCase))
        {
            return valuation with
            {
                Status = OtcValuationStatus.Unvalued,
                MarkToMarket = null,
                Blockers = Blocker(OtcValuationBlockerCodes.CurrencyMismatch, $"The mark is in {valuation.Currency}; the exposure is computed in {currency} and this increment does no FX.")
            };
        }

        return valuation;
    }

    private static OtcCreditSupportSideDto Side(
        OtcMovementParty securedParty,
        OtcMovementParty pledgor,
        decimal exposure,
        decimal independentAmountPledgor,
        decimal independentAmountSecuredParty,
        decimal thresholdPledgor,
        decimal held,
        OtcCollateralAgreementDto csa,
        string currency,
        bool complete)
    {
        var creditSupport = CreditSupportAmount(exposure, independentAmountPledgor, independentAmountSecuredParty, thresholdPledgor);
        var deliveryRaw = creditSupport - held;
        var returnRaw = held - creditSupport;
        var movement = complete
            ? Movement(deliveryRaw, returnRaw, csa.MinimumTransferAmount, csa.RoundingAmount, pledgor, securedParty, currency)
            : null;
        return new OtcCreditSupportSideDto(
            securedParty,
            exposure,
            independentAmountPledgor,
            independentAmountSecuredParty,
            thresholdPledgor,
            creditSupport,
            held,
            deliveryRaw,
            returnRaw,
            movement);
    }
}
