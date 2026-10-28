using Meridian.Contracts.Operations;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Application.SecurityMaster.Otc;

/// <summary>
/// The first <see cref="IOtcContractValuationSource"/>: marks a contract from the golden-copy
/// price recorded for its instrument. Declared convention: a <see cref="SecurityPriceUnit.PercentOfPar"/>
/// observation on a bilateral instrument is the value per 100 of notional to the buyer of that
/// instrument as <see cref="OtcContractDirection"/> defines buying, so the mark is
/// <c>sign(direction) × notional × price / 100</c>. Any other unit, a missing or stale price, or a
/// notional in another currency leaves the contract unvalued with a named blocker. The price
/// hierarchy retains a selection receipt on every call, which is the replayable provenance the
/// snapshot records as its input reference.
/// </summary>
public sealed class PricingStoreOtcContractValuationSource : IOtcContractValuationSource
{
    public const string Method = "otc-mtm-observed-price-v1";
    private const string GoldenCopyInput = "GoldenCopyPrice";

    private readonly ISecurityMasterPricingService _pricing;

    public PricingStoreOtcContractValuationSource(ISecurityMasterPricingService pricing)
    {
        _pricing = pricing ?? throw new ArgumentNullException(nameof(pricing));
    }

    public async Task<OtcContractValuationDto> ValueAsync(OtcContractValuationRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Contract);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Currency);
        var contract = request.Contract;

        if (!string.Equals(contract.NotionalCurrency, request.Currency, StringComparison.OrdinalIgnoreCase))
        {
            return Unvalued(request, [], OtcValuationBlockerCodes.CurrencyMismatch,
                $"Contract {contract.ContractId:D} is denominated in {contract.NotionalCurrency}; the exposure is computed in {request.Currency} and this increment does no FX.");
        }

        var asOf = new DateTimeOffset(request.AsOfDate.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc));
        var price = await _pricing.GetGoldenCopyPriceAsOfAsync(contract.SecurityId, accountId: null, asOf, ct, request.KnowledgeAsOf).ConfigureAwait(false);
        if (price is null)
        {
            return Unvalued(request, [], OtcValuationBlockerCodes.MissingInput,
                $"No golden-copy price is recorded for security {contract.SecurityId:D} as of {request.AsOfDate:yyyy-MM-dd}.");
        }

        var provenance = DataProvenanceExtensions.CarriesSimulatedOriginToken(price.SelectedSource) ? DataProvenance.Simulated : DataProvenance.Real;
        var input = new OtcValuationInputDto(GoldenCopyInput, price.SelectedSource, price.PriceAsOf, price.SelectionReceiptId?.ToString("D"), provenance);
        if (price.Unit != SecurityPriceUnit.PercentOfPar)
        {
            return Unvalued(request, [input], OtcValuationBlockerCodes.UnitMismatch,
                $"The golden-copy price for security {contract.SecurityId:D} is quoted {price.Unit}; a contract mark is a percent of notional, and a {price.Unit} quote carries no notional convention.");
        }

        if (price.IsStaleFallback)
        {
            return Unvalued(request, [input], OtcValuationBlockerCodes.StaleInput,
                $"The golden-copy price for security {contract.SecurityId:D} from {price.SelectedSource} is {price.DaysStale ?? 0} day(s) old, past its hierarchy's staleness limit.");
        }

        var sign = contract.Direction == OtcContractDirection.Buy ? 1m : -1m;
        var mark = sign * contract.Notional * price.GoldenCopyPrice / 100m;
        return new OtcContractValuationDto(
            contract.ContractId,
            contract.SecurityId,
            request.AsOfDate,
            OtcValuationStatus.Valued,
            mark == 0m ? 0m : mark,
            contract.NotionalCurrency,
            Method,
            [input],
            [],
            provenance);
    }

    private static OtcContractValuationDto Unvalued(OtcContractValuationRequest request, IReadOnlyList<OtcValuationInputDto> inputs, string code, string reason)
        => new(
            request.Contract.ContractId,
            request.Contract.SecurityId,
            request.AsOfDate,
            OtcValuationStatus.Unvalued,
            null,
            request.Currency,
            Method,
            inputs,
            [new OtcValuationBlockerDto(code, reason)],
            DataProvenanceExtensions.Strongest(inputs.Select(input => input.Provenance)));
}
