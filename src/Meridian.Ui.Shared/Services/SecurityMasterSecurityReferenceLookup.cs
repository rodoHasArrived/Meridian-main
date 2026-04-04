using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.Strategies.Services;
using System.Text.RegularExpressions;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Adapts Security Master query services to workstation-facing symbol enrichment.
/// </summary>
public sealed class SecurityMasterSecurityReferenceLookup : ISecurityReferenceLookup
{
    private readonly ISecurityMasterQueryService _queryService;

    public SecurityMasterSecurityReferenceLookup(ISecurityMasterQueryService queryService)
    {
        _queryService = queryService ?? throw new ArgumentNullException(nameof(queryService));
    }

    public async Task<WorkstationSecurityReference?> GetBySymbolAsync(string symbol, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(symbol))
        {
            return null;
        }

        foreach (var candidate in BuildLookupCandidates(symbol))
        {
            var detail = await _queryService
                .GetByIdentifierAsync(candidate.Kind, candidate.Value, candidate.Provider, ct)
                .ConfigureAwait(false);
            if (detail is null)
            {
                continue;
            }

            var primaryIdentifier = detail.Identifiers
                .FirstOrDefault(static identifier => identifier.IsPrimary)?.Value
                ?? detail.Identifiers.FirstOrDefault()?.Value;

            return new WorkstationSecurityReference(
                SecurityId: detail.SecurityId,
                DisplayName: detail.DisplayName,
                AssetClass: detail.AssetClass,
                Currency: detail.Currency,
                Status: detail.Status,
                PrimaryIdentifier: primaryIdentifier,
                SubType: DeriveSubType(detail.AssetClass));
        }

        return null;
    }

    /// <summary>
    /// Derives the most likely sub-type from the asset class string without requiring a full
    /// aggregate rebuild. Returns null for asset classes that do not have a unique sub-type
    /// (e.g. Equity, which can be CommonShare, Adr, or ReitShare).
    /// </summary>
    internal static string? DeriveSubType(string? assetClass) => assetClass switch
    {
        "Bond" => "Bond",
        "TreasuryBill" => "TreasuryBill",
        "Option" => "OptionContract",
        "Future" => "FutureContract",
        "Swap" => "SwapContract",
        "DirectLoan" => "DirectLoan",
        "Deposit" => "Deposit",
        "MoneyMarketFund" => "MoneyMarket",
        "CertificateOfDeposit" => "CertificateOfDeposit",
        "CommercialPaper" => "CommercialPaper",
        "Repo" => "Repo",
        _ => null
    };

    private static IReadOnlyList<LookupCandidate> BuildLookupCandidates(string rawSymbol)
    {
        var candidates = new List<LookupCandidate>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var trimmed = rawSymbol.Trim();

        void Add(SecurityIdentifierKind kind, string value, string? provider = null)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            var normalized = value.Trim();
            var key = $"{kind}|{provider}|{normalized}";
            if (seen.Add(key))
            {
                candidates.Add(new LookupCandidate(kind, normalized, provider));
            }
        }

        if (TryParseProviderSymbol(trimmed, out var providerSymbol))
        {
            Add(SecurityIdentifierKind.ProviderSymbol, providerSymbol.Value, providerSymbol.Provider);
            foreach (var variant in ExpandTickerCandidates(providerSymbol.Value))
            {
                Add(SecurityIdentifierKind.Ticker, variant);
            }
        }

        foreach (var candidate in BuildExactIdentifierCandidates(trimmed))
        {
            Add(candidate.Kind, candidate.Value, candidate.Provider);
        }

        foreach (var variant in ExpandTickerCandidates(trimmed))
        {
            Add(SecurityIdentifierKind.Ticker, variant);
            Add(SecurityIdentifierKind.ProviderSymbol, variant);
        }

        return candidates;
    }

    private static IEnumerable<LookupCandidate> BuildExactIdentifierCandidates(string value)
    {
        if (IsIsin(value))
        {
            yield return new LookupCandidate(SecurityIdentifierKind.Isin, value);
        }

        if (IsFigi(value))
        {
            yield return new LookupCandidate(SecurityIdentifierKind.Figi, value);
        }

        if (IsCusip(value))
        {
            yield return new LookupCandidate(SecurityIdentifierKind.Cusip, value);
        }

        if (IsSedol(value))
        {
            yield return new LookupCandidate(SecurityIdentifierKind.Sedol, value);
        }
    }

    private static IEnumerable<string> ExpandTickerCandidates(string raw)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        IEnumerable<string> Enumerate()
        {
            yield return raw;

            var noDescriptor = Regex.Replace(raw, @"\s+(Equity|Corp|Index|Comdty)$", string.Empty, RegexOptions.IgnoreCase);
            if (!string.Equals(noDescriptor, raw, StringComparison.OrdinalIgnoreCase))
            {
                yield return noDescriptor;
            }

            var firstToken = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(firstToken))
            {
                yield return firstToken;
            }

            var punctuationSplit = raw.Split(['.', '/', '-'], 2, StringSplitOptions.TrimEntries);
            if (punctuationSplit.Length > 1 && !string.IsNullOrWhiteSpace(punctuationSplit[0]))
            {
                yield return punctuationSplit[0];
            }
        }

        foreach (var candidate in Enumerate())
        {
            var normalized = candidate.Trim();
            if (!string.IsNullOrWhiteSpace(normalized) && seen.Add(normalized))
            {
                yield return normalized;
            }
        }
    }

    private static bool TryParseProviderSymbol(string raw, out LookupCandidate candidate)
    {
        var colonParts = raw.Split(':', 2, StringSplitOptions.TrimEntries);
        if (colonParts.Length == 2 &&
            IsProviderToken(colonParts[0]) &&
            !string.IsNullOrWhiteSpace(colonParts[1]))
        {
            candidate = new LookupCandidate(SecurityIdentifierKind.ProviderSymbol, colonParts[1], colonParts[0]);
            return true;
        }

        var atParts = raw.Split('@', 2, StringSplitOptions.TrimEntries);
        if (atParts.Length == 2 &&
            !string.IsNullOrWhiteSpace(atParts[0]) &&
            IsProviderToken(atParts[1]))
        {
            candidate = new LookupCandidate(SecurityIdentifierKind.ProviderSymbol, atParts[0], atParts[1]);
            return true;
        }

        candidate = default;
        return false;
    }

    private static bool IsProviderToken(string value)
        => Regex.IsMatch(value, "^[A-Za-z][A-Za-z0-9_-]{1,24}$");

    private static bool IsIsin(string value)
        => Regex.IsMatch(value, "^[A-Z]{2}[A-Z0-9]{9}[0-9]$", RegexOptions.IgnoreCase);

    private static bool IsCusip(string value)
        => Regex.IsMatch(value, "^[A-Z0-9*@#]{9}$", RegexOptions.IgnoreCase);

    private static bool IsSedol(string value)
        => Regex.IsMatch(value, "^[0-9BCDFGHJKLMNPQRSTVWXYZ]{7}$", RegexOptions.IgnoreCase);

    private static bool IsFigi(string value)
        => Regex.IsMatch(value, "^BBG[A-Z0-9]{9}$", RegexOptions.IgnoreCase);

    private readonly record struct LookupCandidate(
        SecurityIdentifierKind Kind,
        string Value,
        string? Provider = null);
}
