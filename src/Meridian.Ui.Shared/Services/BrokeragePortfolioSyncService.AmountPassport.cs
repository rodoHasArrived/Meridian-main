using System.Globalization;
using System.Text.Json;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Workstation;
using Meridian.Execution.Sdk;
using Meridian.Storage.Archival;
using static Meridian.Ui.Shared.Services.AmountPassportFacts;

namespace Meridian.Ui.Shared.Services;

public sealed partial class BrokeragePortfolioSyncService
{
    private const string BrokerageBasis = "Brokerage observation";
    private static readonly string[] PositionFields = ["quantity", "average-price", "mark", "market-value", "unrealized-pnl"];

    private async Task<AmountPassportScopeDto?> BuildPassportScopeAsync(Guid accountId, string? tenantId,
        string? companyId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(companyId))
            return null;
        var account = await ResolveFundAccountAsync(accountId, ct).ConfigureAwait(false);
        return account is null ? null : new(tenantId, companyId, BrokerageBasis,
            account.FundId?.ToString("D"), EntityId: account.EntityId?.ToString("D"), AccountId: accountId.ToString("D"));
    }

    private async Task<(string Path, BrokerageAmountPassportSnapshotDto? Snapshot)> RetainRawSnapshotAsync(
        BrokerageRawSyncSnapshot raw, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(raw, JsonOptions);
        if (raw.PassportScope is not { AccountId: { } accountId } scope || raw.Portfolio is null)
        {
            var legacyPath = BuildRawSnapshotPath(raw.ProviderId, raw.ExternalAccountId, raw.CapturedAt);
            await AtomicFileWriter.WriteAsync(legacyPath, json, ct).ConfigureAwait(false);
            return (legacyPath, null);
        }
        var digest = Sha256Digest.ComputeUtf8(json);
        var path = BuildPassportSnapshotPath(Guid.Parse(accountId), raw.CapturedAt.UtcTicks, digest);
        await AtomicFileWriter.WriteAsync(path, json, ct).ConfigureAwait(false);
        return (path, new(scope, raw.CapturedAt, raw.Portfolio.RetrievedAt, digest, raw.Portfolio.IsComplete));
    }

    private string BuildPassportSnapshotPath(Guid accountId, long ticks, string digest)
        => Path.Combine(_options.RootDirectory, "raw", "scoped", accountId.ToString("N"),
            FormattableString.Invariant($"{ticks:D19}-{digest}.json"));

    internal static bool TryParsePositionPassport(string id, out Guid accountId, out long ticks,
        out int row, out string field)
    {
        accountId = Guid.Empty;
        ticks = 0;
        row = -1;
        field = "";
        var parts = id.Split(':');
        return parts.Length == 4 &&
            Guid.TryParseExact(parts[0], "D", out accountId) && accountId != Guid.Empty &&
            long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out ticks) && ticks > 0 &&
            int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out row) &&
            PositionFields.Contains(field = parts[3], StringComparer.Ordinal);
    }

    private IReadOnlyDictionary<string, AmountPassportReferenceDto>? BuildPositionReferences(FundAccountBrokerageSyncActivityDto projection,
        FundAccountBrokeragePositionDto position, int row, string? tenantId, string? companyId)
    {
        var snapshot = projection.AmountPassportSnapshot;
        if (snapshot is null || snapshot.Scope.TenantId != tenantId || snapshot.Scope.CompanyId != companyId ||
            snapshot.Scope.AccountId != projection.FundAccountId.ToString("D"))
            return null;
        var freshness = SnapshotFreshness(snapshot.AsOf, _timeProvider.GetUtcNow());
        return PositionFields.ToDictionary(field => field, field => PositionReference(snapshot, row, field,
            position.Currency, position.ValuationBasis, field switch
            {
                "quantity" => position.Quantity,
                "average-price" => position.AverageEntryPrice,
                "mark" => position.MarketPrice,
                "market-value" => position.MarketValue,
                _ => position.UnrealizedPnl
            }, freshness.Status), StringComparer.Ordinal);
    }

    private static AmountPassportReferenceDto PositionReference(BrokerageAmountPassportSnapshotDto snapshot, int row,
        string field, string? currency, string? valuationBasis, decimal value, string freshness)
    {
        var valuation = field is "mark" or "market-value" or "unrealized-pnl";
        var origin = value == 0m && !snapshot.IsComplete ? "unavailable" : !valuation ? "sourced" : valuationBasis switch
        {
            nameof(BrokeragePositionValuationBasis.ProviderReported) => "sourced",
            nameof(BrokeragePositionValuationBasis.CostDerived) when field != "unrealized-pnl" => "estimated",
            _ => "unavailable"
        };
        var basis = valuation ? $"{BrokerageBasis} ({valuationBasis ?? "Unavailable"})" : BrokerageBasis;
        var id = FormattableString.Invariant($"{snapshot.Scope.AccountId}:{snapshot.CapturedAt.UtcTicks}:{row}:{field}");
        var identity = new AmountPassportIdentityDto(PositionKind, id, snapshot.Scope with { Basis = basis },
            Timestamp(snapshot.AsOf), snapshot.ContentHash);
        return new(identity, field == "quantity" ? null : currency,
            field == "quantity" ? "position units" : field is "mark" or "average-price" ? "currency per position unit" : CurrencyUnit,
            origin, origin != "unavailable" && value == 0m, freshness, origin == "unavailable" ? null : Exact(value));
    }

    internal async Task<AmountPassportDto?> GetPositionPassportAsync(AmountPassportIdentityDto identity, CancellationToken ct)
    {
        if (!TryParsePositionPassport(identity.SubjectId, out var accountId, out var ticks, out var row, out var field)
            || identity.Scope.AccountId != accountId.ToString("D") || !Sha256Digest.IsCanonical(identity.Revision))
            return null;
        var raw = await ReadPassportSnapshotAsync(accountId, ticks, identity.Revision, ct).ConfigureAwait(false);
        if (raw?.PassportScope is not { } scope || raw.Portfolio is not { } portfolio || row >= portfolio.Positions.Count ||
            scope.TenantId != identity.Scope.TenantId || scope.CompanyId != identity.Scope.CompanyId ||
            scope.AccountId != identity.Scope.AccountId || raw.CapturedAt.UtcTicks != ticks)
            return null;

        var position = portfolio.Positions[row];
        var value = field switch
        {
            "quantity" => position.Quantity,
            "average-price" => position.AverageEntryPrice,
            "mark" => position.MarketPrice,
            "market-value" => position.MarketValue,
            _ => position.UnrealizedPnl
        };
        var snapshot = new BrokerageAmountPassportSnapshotDto(scope, raw.CapturedAt, portfolio.RetrievedAt, identity.Revision, portfolio.IsComplete);
        var freshness = SnapshotFreshness(portfolio.RetrievedAt, _timeProvider.GetUtcNow());
        var reference = PositionReference(snapshot, row, field, position.Currency,
            position.ValuationBasis.ToString(), value, freshness.Status);
        if (identity != reference.Identity)
            throw new AmountPassportScopeMismatchException();

        var observedValue = reference.Origin is "sourced" or "estimated";
        var valuation = field is "mark" or "market-value" or "unrealized-pnl";
        var history = await ReadPositionHistoryAsync(raw, snapshot, identity, position, accountId, field, ct).ConfigureAwait(false);
        var calculation = !observedValue ? null : reference.Origin == "estimated"
            ? "Provider cost-derived estimate retained in the snapshot."
            : "Read directly from the retained brokerage snapshot.";
        var missingMark = valuation ? "The broker did not retain the underlying mark observation timestamp or its pricing policy."
            : null;
        if (missingMark is not null && freshness.Status == "current")
            freshness = freshness with { Status = "review-required", Detail = missingMark };
        var warnings = new List<string>();
        if (!portfolio.IsComplete)
            warnings.Add("The provider reported an incomplete snapshot. Field presence could not be verified for zero placeholders.");
        if (field != "quantity" && string.IsNullOrWhiteSpace(reference.Currency))
            warnings.Add("The provider did not declare the position currency.");
        return Build(reference, $"{position.Symbol} {field.Replace('-', ' ')}", observedValue ? value : null,
            null, calculation, field == "quantity" ? "Exchange rates do not apply to quantity."
                : "The source reports its currency. Upstream FX conversion details are unavailable.",
            freshness, new("allowed", "read-only", "Retained brokerage observations are read-only. This source has no ledger period."),
            [
                Component("scope", true, "Exact retained account, tenant, company, snapshot, row and field."),
                Component("source", observedValue, observedValue ? "The retained broker observation was verified." : "This position has no supported value for the selected field."),
                Component("calculation", observedValue && !valuation, valuation ? "The broker's upstream valuation and FX inputs are unavailable." : calculation ?? "No calculation rule was supplied."),
                Component("freshness", freshness.Status == "current" && missingMark is null, missingMark ?? freshness.Detail),
                new("evidence", "not-applicable", "The verified retained snapshot is the source record."),
                Component("history", true, "The original snapshot was retained and the current projection was checked."),
                Component("permissions", true, "Inspection is allowed. Retained observations are read-only.")
            ],
            [new("brokerage-snapshot", $"{raw.CapturedAt:O}", "Retained brokerage observation", ContentHash: identity.Revision)],
            [], history, warnings, blocked: freshness.Status == "blocked");
    }

    private AmountPassportFreshnessDto SnapshotFreshness(DateTimeOffset observedAt, DateTimeOffset evaluatedAt)
    {
        var age = evaluatedAt - observedAt;
        var status = age < -MaximumProviderFutureSkew ? "blocked" : age > _options.StaleAfter ? "stale" : "current";
        return new(status, evaluatedAt, $"Brokerage sync snapshot maximum age {_options.StaleAfter.TotalMinutes.ToString("G", CultureInfo.InvariantCulture)} minutes",
            status == "blocked" ? "The source timestamp is after the allowed provider clock window."
                : status == "stale" ? "Stale source snapshot. Review is required before relying on it."
                : "Brokerage snapshot age is within the sync policy.", observedAt,
            age >= TimeSpan.Zero ? (long)age.TotalMinutes : null);
    }

    private async Task<IReadOnlyList<AmountPassportRecordDto>> ReadPositionHistoryAsync(BrokerageRawSyncSnapshot raw,
        BrokerageAmountPassportSnapshotDto snapshot, AmountPassportIdentityDto identity, BrokeragePositionSnapshotDto position,
        Guid accountId, string field, CancellationToken ct)
    {
        var history = new List<AmountPassportRecordDto> { new("original", identity.SubjectId, "Original snapshot observation", ContentHash: identity.Revision) };
        var current = (await TryLoadPriorProjectionAsync(accountId, ct).ConfigureAwait(false))?.AmountPassportSnapshot;
        if (current is null || current.Scope != snapshot.Scope || current.CapturedAt <= snapshot.CapturedAt)
            return history;

        var successor = await ReadPassportSnapshotAsync(accountId, current.CapturedAt.UtcTicks, current.ContentHash, ct).ConfigureAwait(false);
        if (successor?.PassportScope != current.Scope || successor.CapturedAt != current.CapturedAt ||
            successor.Portfolio?.RetrievedAt != current.AsOf || successor.ProviderId != raw.ProviderId ||
            successor.ExternalAccountId != raw.ExternalAccountId)
            return history;
        var matches = successor.Portfolio.Positions.Select((item, index) => (item, index))
            .Where(candidate => !string.IsNullOrWhiteSpace(position.PositionId) && candidate.item.PositionId == position.PositionId)
            .ToArray();
        var next = matches.Length == 1 ? PositionReference(current, matches[0].index, field,
            matches[0].item.Currency, matches[0].item.ValuationBasis.ToString(), 0m, "current").Identity : null;
        history.Add(new(next is null ? "newer-snapshot" : "successor", current.ContentHash, next is null
            ? "A newer account snapshot is retained. No exact successor position was identified."
            : "Newer observation for the same provider position", Passport: next));
        return history;
    }

    private async Task<BrokerageRawSyncSnapshot?> ReadPassportSnapshotAsync(Guid accountId, long ticks,
        string digest, CancellationToken ct)
    {
        if (!Sha256Digest.IsCanonical(digest))
            return null;
        try
        {
            var json = await File.ReadAllTextAsync(BuildPassportSnapshotPath(accountId, ticks, digest), ct).ConfigureAwait(false);
            return Sha256Digest.FixedEquals(digest, Sha256Digest.ComputeUtf8(json))
                ? JsonSerializer.Deserialize<BrokerageRawSyncSnapshot>(json, JsonOptions) : null;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException or JsonException)
        {
            return null;
        }
    }
}
