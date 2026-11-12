using System.Globalization;
using Meridian.Contracts.Domain.Models;
using Meridian.Contracts.Options;
using Meridian.Instruments.Options;

namespace Meridian.Ui.Shared.Services;

/// <summary>Bounded, current-snapshot option exploration. Does not evaluate account or execution permission.</summary>
public sealed class OptionScreeningService(OptionsChainService chains, TimeProvider timeProvider)
{
    public const int MaximumExpirations = 12;

    public async Task<OptionScreenResult> ScreenAsync(OptionScreenRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.UnderlyingSymbol) || request.UnderlyingSymbol.Length > 64)
            throw new ArgumentException("An underlying symbol of at most 64 characters is required.");
        if (request.MinDte < 0 || request.MaxDte < request.MinDte || request.MaxDte > 3650
            || request.MinDelta < -1m || request.MaxDelta > 1m || request.MinDelta > request.MaxDelta
            || request.MinOpenInterest < 0 || request.MinVolume < 0 || request.MaxSpreadFraction < 0m
            || request.MinPremium < 0m || request.StrikeRange is < 0 or > OptionsChainService.MaxStrikeRange)
            throw new ArgumentException("Check DTE (0–3650), signed delta (-1 to 1), nonnegative liquidity/premium/spread and strike range (0–100).");
        if (request.Right is not ("Both" or "Call" or "Put") || request.PremiumBasis is not ("Bid" or "Ask" or "Mid"))
            throw new ArgumentException("Right must be Both, Call or Put; premium basis must be Bid, Ask or Mid.");
        request = request with { UnderlyingSymbol = request.UnderlyingSymbol.Trim().ToUpperInvariant() };
        var now = timeProvider.GetUtcNow();
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var discovery = await chains.DiscoverExpirationsAsync(request.UnderlyingSymbol, ct).ConfigureAwait(false);
        if (!discovery.IsAvailable)
            throw new InvalidOperationException("Option expiration discovery is unavailable. No provider supplied expiration coverage for this underlying. Check provider status and retry.");
        var expirations = discovery.Expirations
            .Distinct().Where(expiry => expiry.DayNumber - today.DayNumber >= request.MinDte
                && expiry.DayNumber - today.DayNumber <= request.MaxDte).Order().ToArray();
        var candidates = new List<OptionScreenCandidate>();
        var loaded = new List<string>();
        var failed = new List<string>();
        foreach (var expiry in expirations.Take(MaximumExpirations))
        {
            ct.ThrowIfCancellationRequested();
            OptionChainSnapshot? snapshot;
            try
            {
                // Fetch the requested strike scope. A cached chain may have been collected with a different scope.
                snapshot = await chains.FetchChainSnapshotAsync(request.UnderlyingSymbol, expiry, request.StrikeRange, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            { failed.Add(expiry.ToString("yyyy-MM-dd")); continue; }
            if (snapshot is null)
            { failed.Add(expiry.ToString("yyyy-MM-dd")); continue; }
            if (!string.Equals(snapshot.UnderlyingSymbol, request.UnderlyingSymbol, StringComparison.OrdinalIgnoreCase)
                || snapshot.Expiration != expiry)
            { failed.Add(expiry.ToString("yyyy-MM-dd")); continue; }
            loaded.Add(expiry.ToString("yyyy-MM-dd"));
            foreach (var quote in snapshot.Calls.Concat(snapshot.Puts))
            {
                var right = quote.Contract.Right.ToString();
                if (request.Right != "Both" && right != request.Right)
                    continue;
                var reasons = new List<string>();
                if (!string.Equals(quote.Contract.UnderlyingSymbol, request.UnderlyingSymbol, StringComparison.OrdinalIgnoreCase)
                    || quote.Contract.Expiration != expiry)
                    reasons.Add("Provider quote contract does not match the requested underlying or expiration");
                var dte = quote.Contract.Expiration.DayNumber - today.DayNumber;
                var spread = quote.MidPrice is > 0m ? quote.Spread / quote.MidPrice.Value : (decimal?)null;
                if (quote.Delta is null)
                    reasons.Add("Delta unavailable");
                else if (quote.Delta < request.MinDelta || quote.Delta > request.MaxDelta)
                    reasons.Add("Delta outside selected bounds");
                if (quote.MidPrice is null)
                    reasons.Add("Valid two-sided quote unavailable");
                if (request.MinOpenInterest > 0 && (quote.OpenInterest is null || quote.OpenInterest < request.MinOpenInterest))
                    reasons.Add("Open interest below minimum or unavailable");
                if (request.MinVolume > 0 && (quote.Volume is null || quote.Volume < request.MinVolume))
                    reasons.Add("Volume below minimum or unavailable");
                if (request.MaxSpreadFraction is { } ceiling && (spread is null || spread > ceiling))
                    reasons.Add("Spread above maximum or unavailable");
                decimal? premium = request.PremiumBasis switch { "Ask" => quote.AskPrice, "Mid" => quote.MidPrice, _ => quote.BidPrice };
                if (request.MinPremium is { } floor && (premium is null || premium < floor))
                    reasons.Add("Selected premium below minimum or unavailable");
                if (dte < request.MinDte || dte > request.MaxDte)
                    reasons.Add("DTE outside selected bounds");
                var id = OptionAssessmentValidation.Digest(OptionAssessmentValidation.CanonicalBytes(new
                { quote.Symbol, quote.Source, quote.Contract }));
                candidates.Add(new(id, quote.Symbol, quote.Contract.UnderlyingSymbol, right,
                    quote.Contract.Expiration.ToString("yyyy-MM-dd"), dte, Text(quote.Contract.Strike), Text(quote.Delta),
                    Text(quote.BidPrice), Text(quote.AskPrice), Text(quote.MidPrice), Text(spread), quote.OpenInterest,
                    quote.Volume, quote.Contract.Multiplier.ToString(CultureInfo.InvariantCulture), quote.Contract.Currency,
                    quote.Contract.Exchange, quote.Contract.Style.ToString(), quote.Source, quote.Timestamp, reasons.Count == 0, reasons));
            }
        }
        if (expirations.Length > 0 && loaded.Count == 0)
            throw new InvalidOperationException("No requested expiration could be loaded. Check provider evidence and retry.");
        return new(request, now, candidates.GroupBy(candidate => candidate.Id).Select(group => group.First()).ToArray(),
            loaded, failed, expirations.Skip(MaximumExpirations).Select(expiry => expiry.ToString("yyyy-MM-dd")).ToArray(),
            request.StrikeRange,
            $"At most {MaximumExpirations} expirations; requested {request.StrikeRange} strikes each side of ATM. Provider coverage may be smaller. Review omitted/failed expirations and each quote time/source.",
            "Current provider snapshots only, not historical point-in-time data. Screen pass is neutral criteria matching, not account permission, collateral reservation or predicted execution.");
    }

    private static string Text(decimal value) => value.ToString("0.############################", CultureInfo.InvariantCulture);
    private static string? Text(decimal? value) => value?.ToString("0.############################", CultureInfo.InvariantCulture);
}
