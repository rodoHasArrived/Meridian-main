using Meridian.Contracts.SecurityMaster;

namespace Meridian.Application.SecurityMaster.Valuation;

/// <summary>
/// The quoted-price model: the golden-copy price selected through the pricing hierarchy in force at
/// the as-of and knowledge cutoffs. The pricing lane's staleness verdict is reported as a
/// diagnostic, not re-decided, and the selection receipt is the result's input. Every evaluation
/// retains a selection receipt, exactly as the golden-copy read does.
/// </summary>
public sealed class QuotedPriceValuationModel : ISecurityValuationModel
{
    public const string Key = "quoted-price";
    public const string Version = "v1";

    private readonly ISecurityMasterPricingService _pricing;

    public QuotedPriceValuationModel(ISecurityMasterPricingService pricing)
    {
        _pricing = pricing ?? throw new ArgumentNullException(nameof(pricing));
    }

    public SecurityValuationModelKind Kind => SecurityValuationModelKind.QuotedPrice;

    public string ModelKey => Key;

    public string ModelVersion => Version;

    public async Task<SecurityValuationResult> ValueAsync(SecurityValuationRequest request, SecurityDetailDto security, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(security);
        var knowledgeAsOf = request.KnownAt ?? DateTimeOffset.UtcNow;

        var selection = await _pricing
            .GetGoldenCopyPriceAsOfAsync(security.SecurityId, request.AccountId, request.AsOf, ct, request.KnownAt)
            .ConfigureAwait(false);
        if (selection is null)
        {
            return SecurityValuationResult.Blocked(
                request, security, Kind, Key, Version, knowledgeAsOf,
                [new SecurityValuationBlockerDto("NoPriceSelection", "No pricing hierarchy is in force at the as-of date, or no typed price observation is eligible under it.")]);
        }

        var diagnostics = new List<string>();
        if (selection.IsStaleFallback)
        {
            diagnostics.Add($"StaleFallback: the selected source '{selection.SelectedSource}' is {selection.DaysStale ?? 0} day(s) old, beyond its hierarchy allowance.");
        }

        var marketValue = SecurityValuationMarketValues.Compute(security, request.Quantity, selection.GoldenCopyPrice, selection.Unit, diagnostics);
        return new SecurityValuationResult(
            security.SecurityId,
            Kind,
            Key,
            Version,
            request.AsOf,
            selection.KnowledgeAsOf ?? knowledgeAsOf,
            security.Currency,
            selection.GoldenCopyPrice,
            selection.Unit,
            PresentValue: null,
            request.Quantity,
            marketValue,
            selection.PriceAsOf,
            SecurityValuationObservability.QuotedPrice,
            [
                new SecurityValuationInputDto(
                    "PriceSelectionReceipt",
                    selection.SelectedSource,
                    selection.SelectionReceiptId,
                    AsOf: selection.PriceAsOf,
                    RecordedAt: selection.KnowledgeAsOf)
            ],
            diagnostics,
            []);
    }
}
