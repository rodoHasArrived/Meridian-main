using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Meridian.FinancialOperations.Reconciliation;

/// <summary>
/// Loads the production <see cref="IReconciliationFxRateProvider"/> from an operator-maintained rate
/// table under the deployment data root (<c>reconciliation/fx-rates.json</c>). This is the seam that
/// lets statement reconciliation normalize a non-USD statement line against its base-currency internal
/// balance instead of always failing closed to a cross-currency break.
/// </summary>
/// <remarks>
/// A missing, empty, or malformed file yields an empty table, so the provider still reconciles
/// same-currency lines exactly and fails closed on cross-currency lines until an operator supplies
/// rates — the same safe default as <see cref="IdentityReconciliationFxRateProvider"/>. The file
/// format is a JSON object with an optional triangulation pivot and a list of directional, date-
/// effective quotes; the most recent quote at or before a run's as-of date is applied:
/// <code>
/// { "pivotCurrency": "USD", "quotes": [ { "from": "EUR", "to": "USD", "rate": 1.085, "asOf": "2026-05-31" } ] }
/// </code>
/// </remarks>
public static class FileReconciliationFxRateProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public const string RelativePath = "reconciliation/fx-rates.json";

    public static IReconciliationFxRateProvider Load(string dataRoot, ILogger? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        var path = Path.Combine(dataRoot, "reconciliation", "fx-rates.json");
        if (!File.Exists(path))
        {
            return new TableReconciliationFxRateProvider([]);
        }

        try
        {
            var document = JsonSerializer.Deserialize<ReconciliationFxRateFile>(File.ReadAllText(path), JsonOptions);
            var quotes = (document?.Quotes ?? [])
                .Where(static quote => quote is not null)
                .Select(static quote => new ReconciliationFxQuote(quote!.From, quote.To, quote.Rate, quote.AsOf));
            return new TableReconciliationFxRateProvider(quotes, document?.PivotCurrency);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Fail closed: an unreadable or malformed rate table must reconcile same-currency lines
            // and surface cross-currency lines as breaks, never fabricate a rate.
            logger?.LogWarning(
                ex,
                "Failed to load reconciliation FX rate table from {RelativePath}; reconciling without cross-currency rates.",
                RelativePath);
            return new TableReconciliationFxRateProvider([]);
        }
    }

    private sealed record ReconciliationFxRateFile(string? PivotCurrency, IReadOnlyList<ReconciliationFxRateFileQuote?>? Quotes);

    private sealed record ReconciliationFxRateFileQuote(string From, string To, decimal Rate, DateOnly AsOf);
}
