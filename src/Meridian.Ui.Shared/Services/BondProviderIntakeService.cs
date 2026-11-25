using System.Globalization;
using System.Text.Json;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Workstation;
using Meridian.Infrastructure.Adapters.Fred;
using Meridian.Infrastructure.Adapters.OpenFigi;
using Meridian.Ui.Shared.Evidence;

namespace Meridian.Ui.Shared.Services;

/// <summary>Explicit operator intake; discovery never mutates identity and reviewed FRED mappings never imply curves.</summary>
public sealed class BondProviderIntakeService(OpenFigiClient figi, FredHistoricalDataProvider fred,
    IEvidenceArtifactStore vault, IBondMarketDataService market, IBondProviderResponseStore responses, TimeProvider? clock = null)
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public Task<BondProviderResponseDto?> GetProviderResponseAsync(string tenantId, string companyId,
        string contentSha256, CancellationToken ct = default) => responses.GetAsync(tenantId, companyId, contentSha256, ct);

    public async Task<BondIdentifierDiscoveryResult> DiscoverIdentifierAsync(BondIdentifierDiscoveryRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await figi.LookupBondIdentifierAsync(request.Identifier, request.IdentifierType,
            request.MarketSector, ct).ConfigureAwait(false);
        return new(result.Identifier, result.IdentifierType, result.Candidates.Select(row => new BondIdentifierCandidateDto(
            row.Figi, row.CompositeFigi, row.SecurityType, row.MarketSector, row.Ticker, row.Name, row.ExchangeCode)).ToArray(),
            result.IsAmbiguous, result.Resolved?.Figi, BondProviderEvidenceStatus.LiveUntested);
    }

    public async Task<BondFredIntakeResult> ImportFredAsync(string tenantId, string companyId, string actor,
        BondFredIntakeRequest request, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(companyId);
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        ArgumentNullException.ThrowIfNull(request);
        var today = DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime);
        if (request.From == default || request.Through < request.From || request.Through > request.VintageDate
            || request.VintageDate > today || request.VintageDate == default
            || request.Through.DayNumber - request.From.DayNumber > 3660)
            throw new ArgumentException("FRED intake needs an elapsed vintage and a valid window of at most ten years.");
        var (binding, hash) = await ReadBindingAsync(tenantId, companyId, request, ct).ConfigureAwait(false);
        var response = await fred.GetEconomicObservationsAsync(new(binding.SeriesId, binding.IndexName,
            binding.ObservationKind, binding.Unit, binding.SourceName, binding.Currency), request.From,
            request.Through, request.VintageDate, ct).ConfigureAwait(false);
        // Review may be revoked during a provider request. Recheck it before publishing any observations.
        var (_, currentHash) = await ReadBindingAsync(tenantId, companyId, request, ct).ConfigureAwait(false);
        if (!Sha256Digest.FixedEquals(hash, currentHash))
            throw new InvalidOperationException("The reviewed FRED binding changed during intake; retry with its current accepted revision.");
        var captured = await responses.RetainAsync(tenantId, companyId, "fred", response.RawResponseJson, ct).ConfigureAwait(false);
        if (!Sha256Digest.FixedEquals(captured.ContentSha256, response.ResponseSha256))
            throw new InvalidDataException("Retained FRED response hash differs from the adapter response.");
        var observations = response.Observations.Select(row => row with
        {
            ObservationKey = $"{Sha256Digest.ComputeUtf8(companyId)}:{binding.BindingKey}:{row.ObservationKey}",
            SourceEvidenceSha256 = Sha256Digest.ComputeUtf8(JsonSerializer.Serialize(new[] { row.SourceEvidenceSha256, hash })),
            EvidenceReference = $"{row.EvidenceReference}#binding={Uri.EscapeDataString(request.EvidenceUri)}&document={Uri.EscapeDataString(request.DocumentId)}&sha256={hash}&responseSha256={captured.ContentSha256}"
        }).ToArray();
        var retained = new BondMarketImportResult([], 0);
        if (observations.Length > 0)
        {
            var canonical = await PreserveOriginalResponseReferences(tenantId, companyId, observations, request, binding, hash, ct).ConfigureAwait(false);
            try
            { retained = await market.ImportAsync(tenantId, actor, new(canonical), ct).ConfigureAwait(false); }
            catch (InvalidOperationException)
            {
                // Concurrent overlapping fetches may publish the same source row from different
                // response windows. Keep the first exact response only when all typed facts match.
                canonical = await PreserveOriginalResponseReferences(tenantId, companyId, observations, request, binding, hash, ct).ConfigureAwait(false);
                retained = await market.ImportAsync(tenantId, actor, new(canonical), ct).ConfigureAwait(false);
            }
        }
        return new(retained, response.MissingDates, response.ResponseSha256, hash, BondProviderEvidenceStatus.LiveUntested)
        { ResponseEvidenceReference = captured.EvidenceReference };
    }

    private async Task<BondMarketObservationInput[]> PreserveOriginalResponseReferences(string tenantId, string companyId,
        BondMarketObservationInput[] proposed, BondFredIntakeRequest request, BondFredSeriesBindingDocument binding,
        string bindingHash, CancellationToken ct)
    {
        var existing = await market.QueryAsync(tenantId, new(FromDate: request.From, ThroughDate: request.Through,
            ProviderId: "fred", IncludeSuperseded: true), ct).ConfigureAwait(false);
        var byRevision = existing.ToDictionary(row => BondMarketDataRules.SourceRevisionKey(row.Input), StringComparer.Ordinal);
        var selected = new List<BondMarketObservationInput>();
        foreach (var row in proposed)
        {
            if (byRevision.TryGetValue(BondMarketDataRules.SourceRevisionKey(row), out var original))
            {
                var candidate = row with { EvidenceReference = original.Input.EvidenceReference };
                if (Sha256Digest.FixedEquals(BondMarketDataRules.ContentHash(candidate), original.ContentSha256))
                {
                    const string marker = "&responseSha256=";
                    var markerAt = original.Input.EvidenceReference.LastIndexOf(marker, StringComparison.Ordinal);
                    var proposedMarkerAt = row.EvidenceReference.LastIndexOf(marker, StringComparison.Ordinal);
                    if (markerAt < 0 || proposedMarkerAt < 0
                        || original.Input.EvidenceReference[..markerAt] != row.EvidenceReference[..proposedMarkerAt])
                        throw new InvalidDataException("The original FRED source does not name this exact accepted binding and row.");
                    var originalHash = markerAt < 0 ? null : original.Input.EvidenceReference[(markerAt + marker.Length)..];
                    var source = originalHash is null ? null
                        : await responses.GetAsync(tenantId, companyId, originalHash, ct).ConfigureAwait(false);
                    if (source?.ProviderId != "fred" || !ContainsExactRow(source.RawResponseJson, row, binding, bindingHash))
                        throw new InvalidDataException("The original FRED source response does not prove this exact observed row.");
                    selected.Add(candidate);
                    continue;
                }
            }
            selected.Add(row);
        }
        return selected.ToArray();
    }

    private static bool ContainsExactRow(string rawJson, BondMarketObservationInput observation,
        BondFredSeriesBindingDocument binding, string bindingHash)
    {
        using var document = JsonDocument.Parse(rawJson);
        if (observation.SourceVintageDate is not { } expectedVintage || observation.Unit != binding.Unit
            || observation.Kind != binding.ObservationKind || observation.SeriesKey != binding.IndexName
            || !document.RootElement.TryGetProperty("observations", out var rows) || rows.ValueKind != JsonValueKind.Array)
            return false;
        var matchingDates = 0;
        var exact = false;
        foreach (var row in rows.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Object || !row.TryGetProperty("date", out var dateNode)
                || dateNode.ValueKind != JsonValueKind.String || !DateOnly.TryParseExact(dateNode.GetString(),
                    "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                return false;
            if (date != observation.EconomicDate)
                continue;
            matchingDates++;
            if (!row.TryGetProperty("value", out var valueNode) || valueNode.ValueKind != JsonValueKind.String
                || !decimal.TryParse(valueNode.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                || value != observation.Value)
                return false;
            var vintage = expectedVintage;
            if (row.TryGetProperty("realtime_start", out var vintageNode)
                && (vintageNode.ValueKind != JsonValueKind.String || !DateOnly.TryParseExact(vintageNode.GetString(),
                    "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out vintage)))
                return false;
            if (vintage != expectedVintage)
                return false;
            var facts = JsonSerializer.Serialize(new[] { binding.SeriesId.Trim().ToUpperInvariant(),
                date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), valueNode.GetString(),
                vintage.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) });
            var combined = Sha256Digest.ComputeUtf8(JsonSerializer.Serialize(new[] { Sha256Digest.ComputeUtf8(facts), bindingHash }));
            exact = Sha256Digest.FixedEquals(combined, observation.SourceEvidenceSha256);
        }
        return matchingDates == 1 && exact;
    }

    private async Task<(BondFredSeriesBindingDocument Binding, string Hash)> ReadBindingAsync(string tenantId,
        string companyId, BondFredIntakeRequest request, CancellationToken ct)
    {
        if (!EvidenceVaultReference.TryParseCanonical(request.EvidenceUri, out var vaultId)
            || string.IsNullOrWhiteSpace(request.DocumentId))
            throw new ArgumentException("FRED intake requires a canonical Evidence Vault URI and exact binding document ID.");
        var identity = await vault.TryGetVaultIdentityAsync(vaultId, tenantId, companyId, ct).ConfigureAwait(false);
        var documents = identity?.Documents.Where(row => row.DocumentId == request.DocumentId).ToArray();
        if (identity is null || identity.TenantId != tenantId || identity.Scope != companyId
            || identity.SubjectKind != EvidenceSubjectResolver.ProviderTrustKind || identity.SubjectId != "fred"
            || documents?.Length != 1)
            throw new InvalidOperationException("The FRED binding must belong to this tenant/company's provider-trust/fred vault.");
        var document = documents[0];
        var review = document.ReviewerState;
        if (document.TenantId != tenantId || document.Scope != companyId || string.IsNullOrWhiteSpace(document.Actor)
            || review.Status != EvidenceDocumentReviewStatusDto.Accepted || string.IsNullOrWhiteSpace(review.Reviewer)
            || review.ReviewedAt is not { } reviewed || reviewed.Offset != TimeSpan.Zero || reviewed > _clock.GetUtcNow()
            || string.Equals(document.Actor, review.Reviewer, StringComparison.OrdinalIgnoreCase)
            || !review.ConfirmedFields.Any(row => row.FieldName == "sourceContentHashSha256"
                && Sha256Digest.FixedEquals(row.ConfirmedValue, document.SourceHashSha256)
                && row.ConfirmedBy == review.Reviewer && row.ConfirmedAt == reviewed)
            || !document.AuditTrail.Any(row => row.Action == "DocumentReviewRecorded" && row.Actor == review.Reviewer && row.RecordedAt == reviewed))
            throw new InvalidOperationException("FRED series/index/unit bindings require independent accepted review of their exact bytes.");
        var content = await vault.TryOpenDocumentContentAsync(vaultId, request.DocumentId, tenantId, companyId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The reviewed FRED binding bytes are unavailable.");
        await using var stream = content.Content;
        using var buffer = new MemoryStream();
        var chunk = new byte[4096];
        int count;
        while ((count = await stream.ReadAsync(chunk, ct).ConfigureAwait(false)) != 0)
        {
            if (buffer.Length + count > 65536)
                throw new ArgumentException("FRED binding documents are limited to 64 KiB.");
            buffer.Write(chunk, 0, count);
        }
        var bytes = buffer.ToArray();
        var hash = Sha256Digest.Compute(bytes);
        if (!Sha256Digest.FixedEquals(hash, document.SourceHashSha256))
            throw new InvalidDataException("FRED binding bytes differ from the accepted content hash.");
        using var json = JsonDocument.Parse(bytes);
        RefuseDuplicateProperties(json.RootElement);
        var binding = JsonSerializer.Deserialize(bytes, BondProviderIntakeJsonContext.Default.BondFredSeriesBindingDocument)
            ?? throw new ArgumentException("A typed FRED binding document is required.");
        if (binding.SchemaVersion != 1 || binding.Kind != "bondFredSeriesBinding"
            || string.IsNullOrWhiteSpace(binding.BindingKey) || binding.BindingKey.Length > 120
            || !binding.BindingKey.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.')
            || string.IsNullOrWhiteSpace(binding.SeriesId) || !binding.SeriesId.All(c => char.IsAsciiLetterOrDigit(c) || c == '_')
            || string.IsNullOrWhiteSpace(binding.IndexName) || string.IsNullOrWhiteSpace(binding.SourceName)
            || binding.ObservationKind is not (BondMarketObservationKind.Fixing or BondMarketObservationKind.InflationIndex)
            || binding.ObservationKind == BondMarketObservationKind.Fixing && binding.Unit is not (BondMarketValueUnit.Percent or BondMarketValueUnit.AnnualFraction)
            || binding.ObservationKind == BondMarketObservationKind.InflationIndex && binding.Unit != BondMarketValueUnit.IndexLevel
            || binding.Currency is { } currency && (currency.Length != 3 || !currency.All(char.IsAsciiLetter)))
            throw new ArgumentException("The reviewed FRED binding must explicitly declare a stable key, series, contractual index, source and compatible rate/index units.");
        var current = await vault.TryGetVaultIdentityAsync(vaultId, tenantId, companyId, ct).ConfigureAwait(false);
        var latest = current?.Documents.SingleOrDefault(row => row.DocumentId == request.DocumentId);
        if (current?.TenantId != tenantId || current.Scope != companyId || current.SubjectKind != identity.SubjectKind
            || current.SubjectId != identity.SubjectId || latest is null
            || JsonSerializer.Serialize(latest) != JsonSerializer.Serialize(document))
            throw new InvalidOperationException("FRED binding review changed during verification.");
        return (binding, hash);
    }

    private static void RefuseDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("A FRED binding JSON object is required.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in element.EnumerateObject())
            if (!names.Add(property.Name))
                throw new ArgumentException("FRED binding JSON cannot contain duplicate property names.");
    }
}
