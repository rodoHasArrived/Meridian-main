using System.Collections.Generic;
using System.Text.Json;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging;

namespace Meridian.Application.SecurityMaster;

/// <summary>
/// Builds the classification drift report: every active security whose latest retained event
/// recorded an asset class or family that the current taxonomy now derives differently.
/// <para>
/// This is the governed record of a taxonomy revision. Classification is derived on read from the
/// F# <c>AssetClassRegistry</c>, so current projections already carry the new labels and no stored
/// row needs rewriting; retained events are append-only and keep the labels they were written
/// with. The report lists which securities those are, so an operator can evidence the change.
/// Nothing is written.
/// </para>
/// </summary>
public sealed class SecurityMasterClassificationDriftReportService : ISecurityMasterClassificationDriftReportService
{
    private readonly ISecurityMasterStore _securityStore;
    private readonly ISecurityMasterEventStore _eventStore;
    private readonly ILogger<SecurityMasterClassificationDriftReportService> _logger;
    private readonly TimeProvider _timeProvider;

    public SecurityMasterClassificationDriftReportService(
        ISecurityMasterStore securityStore,
        ISecurityMasterEventStore eventStore,
        ILogger<SecurityMasterClassificationDriftReportService> logger,
        TimeProvider? timeProvider = null)
    {
        _securityStore = securityStore;
        _eventStore = eventStore;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<SecurityClassificationDriftReportDto> BuildReportAsync(CancellationToken ct = default)
    {
        var securities = await _securityStore.LoadActiveAsync(ct).ConfigureAwait(false);
        var drifted = new List<(string TypeName, SecurityEconomicDefinitionRecord Stored, SecurityClassificationTaxonomy.Labels Current, Guid SecurityId)>();
        var scanned = 0;
        var unreadable = 0;
        var withoutClassification = 0;

        foreach (var projection in securities)
        {
            ct.ThrowIfCancellationRequested();
            if (projection.Status != SecurityStatusDto.Active)
                continue;

            scanned++;
            IReadOnlyList<SecurityMasterEventEnvelope> events;
            try
            {
                events = await _eventStore.LoadAsync(projection.SecurityId, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is InvalidOperationException or JsonException)
            {
                unreadable++;
                _logger.LogWarning(
                    ex,
                    "Classification drift report skipped security {SecurityId}: its event stream could not be read.",
                    projection.SecurityId);
                continue;
            }

            var stored = LatestRetainedClassification(events);
            if (stored is null)
            {
                withoutClassification++;
                continue;
            }

            var typeName = SecurityAssetClassCatalog.Canonicalize(stored.TypeName);
            if (SecurityClassificationTaxonomy.Current(typeName) is not { } current
                || SecurityClassificationTaxonomy.MatchesCurrent(typeName, stored.AssetClass, stored.AssetFamily))
            {
                continue;
            }

            drifted.Add((typeName, stored, current, projection.SecurityId));
        }

        var entries = drifted
            .GroupBy(static item => (
                item.TypeName,
                StoredAssetClass: ClassificationLabelReader.Normalize(item.Stored.AssetClass) ?? string.Empty,
                StoredAssetFamily: ClassificationLabelReader.Normalize(item.Stored.AssetFamily)))
            .Select(static group =>
            {
                var current = group.First().Current;
                var ids = group.Select(static item => item.SecurityId).Order().ToArray();
                return new SecurityClassificationDriftEntryDto(
                    group.Key.TypeName,
                    group.Key.StoredAssetClass,
                    group.Key.StoredAssetFamily,
                    current.AssetClass,
                    current.AssetFamily,
                    ids.Length,
                    ids);
            })
            .OrderBy(static entry => entry.TypeName, StringComparer.Ordinal)
            .ThenBy(static entry => entry.StoredAssetClass, StringComparer.Ordinal)
            .ThenBy(static entry => entry.StoredAssetFamily, StringComparer.Ordinal)
            .ToArray();

        _logger.LogInformation(
            "Classification drift report built: {Scanned} securities scanned, {Drifted} drifted in {Groups} groups, {WithoutClassification} without a retained classification, {Unreadable} unreadable.",
            scanned,
            drifted.Count,
            entries.Length,
            withoutClassification,
            unreadable);

        return new SecurityClassificationDriftReportDto(
            GeneratedAt: _timeProvider.GetUtcNow(),
            SecuritiesScanned: scanned,
            UnreadableCount: unreadable,
            WithoutRetainedClassificationCount: withoutClassification,
            DriftedSecurityCount: drifted.Count,
            Entries: entries);
    }

    /// <summary>
    /// The newest event in the stream whose payload decodes to a definition that names its asset
    /// class and classification. Events that carry no definition (aliases, corporate actions) are
    /// skipped rather than read as an empty classification.
    /// </summary>
    private static SecurityEconomicDefinitionRecord? LatestRetainedClassification(IReadOnlyList<SecurityMasterEventEnvelope> events)
    {
        foreach (var envelope in events.OrderByDescending(static e => e.StreamVersion))
        {
            if (envelope.Payload.ValueKind != JsonValueKind.Object)
                continue;

            SecurityEconomicDefinitionRecord decoded;
            try
            {
                decoded = SecurityMasterMapping.FromEconomicPayload(envelope.Payload);
            }
            catch (Exception ex) when (ex is InvalidOperationException or JsonException or FormatException or ArgumentException or NotSupportedException)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(decoded?.TypeName) && !string.IsNullOrWhiteSpace(decoded.AssetClass))
                return decoded;
        }

        return null;
    }
}
