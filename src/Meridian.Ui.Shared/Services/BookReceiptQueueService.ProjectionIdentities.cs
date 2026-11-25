using Meridian.Contracts.AssetOperations;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Reserved adapter identity for an explicit provider projection reference. It is not an
/// obligation key: only a unique accepted historical flow for the same security and currency
/// may resolve it. Keeping the reference prevents missing current terms from triggering inference.
/// </summary>
internal static class BookReceiptProjectionIdentity
{
    internal const string Prefix = "projected-cash-flow:";
    internal static string For(Guid projectedCashFlowId) => Prefix + projectedCashFlowId.ToString("D");
}

public sealed partial class BookReceiptQueueService
{
    private static BookReceiptDto[] ResolveReceiptProjectionReferences(IReadOnlyList<BookReceiptDto> receipts,
        IReadOnlyDictionary<string, List<BookReceiptExpectationVersionDto>> histories, List<string> issues)
        => receipts.Select(receipt => receipt with
        {
            Allocations = receipt.Allocations.Select(leg => leg with
            {
                ObligationKey = ResolveProjectionReference(leg.ObligationKey, receipt.SecurityId, receipt.Currency, histories, issues)
            }).ToArray()
        }).ToArray();

    private static BookReceiptPostingDto[] ResolvePostingProjectionReferences(IReadOnlyList<BookReceiptPostingDto> postings,
        IReadOnlyList<BookReceiptDto> receipts, IReadOnlyDictionary<string, string> aliases,
        IReadOnlyDictionary<string, List<BookReceiptExpectationVersionDto>> histories, List<string> issues)
    {
        var byId = receipts.ToDictionary(static row => row.ReceiptId, StringComparer.Ordinal);
        return postings.Select(posting =>
        {
            var id = aliases.GetValueOrDefault(posting.ReceiptId) ?? posting.ReceiptId;
            return byId.TryGetValue(id, out var receipt) ? posting with
            {
                ObligationKey = ResolveProjectionReference(posting.ObligationKey, receipt.SecurityId, posting.Currency, histories, issues)
            } : posting;
        }).ToArray();
    }

    private static string ResolveProjectionReference(string key, Guid security, string currency,
        IReadOnlyDictionary<string, List<BookReceiptExpectationVersionDto>> histories, List<string> issues)
    {
        if (!key.StartsWith(BookReceiptProjectionIdentity.Prefix, StringComparison.Ordinal))
            return key;
        var candidates = Guid.TryParse(key[BookReceiptProjectionIdentity.Prefix.Length..], out var flowId) && flowId != Guid.Empty
            ? histories.Values.SelectMany(static versions => versions).Select(static version => version.Expectation)
                .Where(row => row.ProjectedCashFlowId == flowId && row.SecurityId == security && row.Currency == currency)
                .Select(static row => row.ObligationKey).Distinct(StringComparer.Ordinal).ToArray()
            : [];
        if (candidates.Length == 1)
            return candidates[0];
        issues.Add($"Explicit projection reference '{key}' has no unique accepted historical obligation for its security and currency.");
        return key;
    }
}
