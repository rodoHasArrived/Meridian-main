using System.Text;
using System.Text.Json;
using Meridian.Contracts.EntityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Core.Serialization;
using Meridian.Infrastructure.Adapters.Edgar;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging;
using ContractSecurityMasterQueryService = Meridian.Contracts.SecurityMaster.ISecurityMasterQueryService;
using Meridian.Contracts.Integrity;

namespace Meridian.Application.SecurityMaster;

/// <summary>
/// Coordinates EDGAR reference-data persistence and ticker-backed Security Master writes.
/// </summary>
public sealed class EdgarIngestOrchestrator : IEdgarIngestOrchestrator
{
    private const string SourceSystem = "edgar";
    private const string CikProvider = "edgar";
    private const string LegacyCikProvider = "edgar-cik";
    private const string SeriesProvider = "edgar-series";
    private const string ClassProvider = "edgar-class";

    private readonly IEdgarReferenceDataProvider _provider;
    private readonly IEdgarReferenceDataStore _store;
    private readonly ContractSecurityMasterQueryService _queryService;
    private readonly ISecurityMasterService _securityMasterService;
    private readonly ISecurityMasterAmender? _systemAmender;
    private readonly ISecurityMasterConflictService? _conflictService;
    private readonly ILogger<EdgarIngestOrchestrator> _logger;
    private readonly IEntityMasterStore? _entityMaster;
    private readonly Meridian.Contracts.SecurityMaster.ISecurityResolver? _resolver;
    private readonly TimeProvider _clock;

    public EdgarIngestOrchestrator(
        IEdgarReferenceDataProvider provider,
        IEdgarReferenceDataStore store,
        ContractSecurityMasterQueryService queryService,
        ISecurityMasterService securityMasterService,
        ILogger<EdgarIngestOrchestrator> logger,
        ISecurityMasterConflictService? conflictService = null,
        ISecurityMasterAmender? systemAmender = null,
        IEntityMasterStore? entityMaster = null,
        Meridian.Contracts.SecurityMaster.ISecurityResolver? resolver = null,
        TimeProvider? clock = null)
    {
        // ADR-033: the issuer is linked through the entity master by CIK. A CIK identifies the issuer,
        // not a share class, so it is never used on its own to choose which security a ticker belongs to.
        _entityMaster = entityMaster;
        _resolver = resolver;
        _clock = clock ?? TimeProvider.System;
        // Provider ingest amends through the system-ingest path, which the governed-amendment setting
        // does not block. Without an injected amender, the service's own system path is used when it
        // exposes one.
        _systemAmender = systemAmender ?? securityMasterService as ISecurityMasterAmender;
        _provider = provider;
        _store = store;
        _queryService = queryService;
        _securityMasterService = securityMasterService;
        _logger = logger;
        _conflictService = conflictService;
    }

    public async Task<EdgarIngestResult> IngestAsync(EdgarIngestRequest request, CancellationToken ct = default)
    {
        var normalizedRequest = NormalizeRequest(request);
        var errors = new List<string>();
        var securitiesCreated = 0;
        var securitiesAmended = 0;
        var securitiesSkipped = 0;
        var factsStored = 0;
        var securityDataStored = 0;
        var issuersLinked = 0;
        IReadOnlyDictionary<string, IssuerFactSnapshot> factSnapshots = new Dictionary<string, IssuerFactSnapshot>(StringComparer.Ordinal);

        var conflictsBefore = await CountOpenConflictsAsync(ct).ConfigureAwait(false);

        var associations = await _provider.FetchTickerAssociationsAsync(ct).ConfigureAwait(false);
        var filers = await FetchFilersAsync(normalizedRequest, associations, ct).ConfigureAwait(false);
        var selectedCiks = SelectCiks(normalizedRequest, filers, associations);

        associations = FilterAssociations(normalizedRequest, associations, selectedCiks);

        if (!normalizedRequest.DryRun)
        {
            await _store.SaveTickerAssociationsAsync(associations, ct).ConfigureAwait(false);
            foreach (var filer in filers)
            {
                await _store.SaveFilerAsync(filer, ct).ConfigureAwait(false);
            }
        }

        if (normalizedRequest.IncludeXbrl)
        {
            var factResult = await FetchAndStoreFactsAsync(normalizedRequest, selectedCiks, errors, ct)
                .ConfigureAwait(false);
            factsStored = factResult.StoredPartitions;
            factSnapshots = factResult.Snapshots;
        }

        if (normalizedRequest.IncludeFilingDocuments)
        {
            securityDataStored = await FetchAndStoreSecurityDataAsync(normalizedRequest, filers, errors, ct)
                .ConfigureAwait(false);
        }

        var filersByCik = filers
            .GroupBy(f => f.Cik, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        foreach (var association in associations.Where(a => !string.IsNullOrWhiteSpace(a.Ticker)))
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var (result, securityId) = await CreateOrAmendSecurityAsync(
                        association,
                        filersByCik,
                        factSnapshots,
                        normalizedRequest.DryRun,
                        ct)
                    .ConfigureAwait(false);

                if (!normalizedRequest.DryRun
                    && securityId is { } writtenSecurityId
                    && await LinkIssuerAsync(writtenSecurityId, association, filersByCik.GetValueOrDefault(association.Cik), errors, ct).ConfigureAwait(false))
                {
                    issuersLinked++;
                }

                switch (result)
                {
                    case SecurityWriteResult.Created:
                        securitiesCreated++;
                        break;
                    case SecurityWriteResult.Amended:
                        securitiesAmended++;
                        break;
                    case SecurityWriteResult.Skipped:
                        securitiesSkipped++;
                        break;
                }
            }
            // Cancellation is checked FIRST: the duplicate filter below must never get the chance to
            // classify a cancelled write as a skip.
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (SecurityMasterIngestFailureClassifier.IsAlreadyMastered(ex))
            {
                securitiesSkipped++;
                _logger.LogDebug(ex, "EDGAR security write skipped for {Cik}/{Ticker}", association.Cik, association.Ticker);
            }
            catch (Exception ex)
            {
                securitiesSkipped++;
                errors.Add($"{association.Cik}/{association.Ticker}: {ex.Message}");
                _logger.LogWarning(
                    ex,
                    "EDGAR security write failed for {Cik}/{Ticker}",
                    association.Cik,
                    association.Ticker);
            }
        }

        var conflictsAfter = await CountOpenConflictsAsync(ct).ConfigureAwait(false);
        var conflictsDetected = Math.Max(0, conflictsAfter - conflictsBefore);

        return new EdgarIngestResult(
            FilersProcessed: filers.Count,
            TickerAssociationsStored: normalizedRequest.DryRun ? 0 : associations.Count,
            FactsStored: factsStored,
            SecurityDataStored: securityDataStored,
            SecuritiesCreated: securitiesCreated,
            SecuritiesAmended: securitiesAmended,
            SecuritiesSkipped: securitiesSkipped,
            ConflictsDetected: conflictsDetected,
            DryRun: normalizedRequest.DryRun,
            Errors: errors)
        {
            IssuersLinked = issuersLinked
        };
    }

    private async Task<IReadOnlyList<EdgarFilerRecord>> FetchFilersAsync(
        EdgarIngestRequest request,
        IReadOnlyList<EdgarTickerAssociation> associations,
        CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(request.Cik))
        {
            var filer = await _provider.FetchSubmissionAsync(request.Cik, ct).ConfigureAwait(false);
            return filer is null
                ? Array.Empty<EdgarFilerRecord>()
                : new[] { filer };
        }

        var filers = await _provider.FetchBulkSubmissionsAsync(request.MaxFilers, ct).ConfigureAwait(false);
        if (filers.Count > 0)
            return filers;

        return associations
            .GroupBy(a => a.Cik, StringComparer.Ordinal)
            .Take(request.MaxFilers.GetValueOrDefault(int.MaxValue))
            .Select(g => new EdgarFilerRecord(
                Cik: g.Key,
                Name: g.Select(a => a.Name).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)) ?? g.Key,
                EntityType: null,
                Sic: null,
                SicDescription: null,
                Category: null,
                StateOfIncorporation: null,
                StateOfIncorporationDescription: null,
                FiscalYearEnd: null,
                Ein: null,
                Description: null,
                Website: null,
                InvestorWebsite: null,
                Phone: null,
                BusinessAddress: null,
                MailingAddress: null,
                InsiderTransactionForOwnerExists: null,
                InsiderTransactionForIssuerExists: null,
                Flags: Array.Empty<string>(),
                Tickers: g.Select(a => a.Ticker).Where(t => !string.IsNullOrWhiteSpace(t)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                Exchanges: g.Select(a => a.Exchange).Where(e => !string.IsNullOrWhiteSpace(e)).Distinct(StringComparer.OrdinalIgnoreCase).Cast<string>().ToArray(),
                FormerNames: Array.Empty<EdgarFormerName>(),
                RecentFilings: Array.Empty<EdgarFilingSummary>(),
                RetrievedAtUtc: DateTimeOffset.UtcNow))
            .ToList();
    }

    private async Task<FactStoreResult> FetchAndStoreFactsAsync(
        EdgarIngestRequest request,
        IReadOnlySet<string> selectedCiks,
        List<string> errors,
        CancellationToken ct)
    {
        if (selectedCiks.Count == 0)
            return new FactStoreResult(0, new Dictionary<string, IssuerFactSnapshot>(StringComparer.Ordinal));

        IReadOnlyList<EdgarXbrlFact> facts;
        if (!string.IsNullOrWhiteSpace(request.Cik))
        {
            facts = await _provider.FetchCompanyFactsAsync(request.Cik, ct).ConfigureAwait(false);
        }
        else
        {
            facts = await _provider.FetchBulkCompanyFactsAsync(selectedCiks, request.MaxFilers, ct)
                .ConfigureAwait(false);
        }

        var storedPartitions = 0;
        var snapshots = new Dictionary<string, IssuerFactSnapshot>(StringComparer.Ordinal);
        foreach (var group in facts.GroupBy(f => f.Cik, StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var factList = group.ToList();
                var snapshot = EdgarReferenceDataProvider.CreateIssuerFactSnapshot(group.Key, factList);
                snapshots[group.Key] = snapshot;
                if (!request.DryRun)
                {
                    await _store.SaveFactsAsync(
                            new EdgarFactsRecord(
                                Cik: group.Key,
                                Facts: factList,
                                Snapshot: snapshot,
                                RetrievedAtUtc: DateTimeOffset.UtcNow),
                            ct)
                        .ConfigureAwait(false);
                }

                storedPartitions++;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // A cancelled run must stop, not record a per-CIK error and carry on.
                throw;
            }
            catch (Exception ex)
            {
                errors.Add($"{group.Key}/facts: {ex.Message}");
                _logger.LogWarning(ex, "EDGAR facts store write failed for CIK {Cik}", group.Key);
            }
        }

        return new FactStoreResult(request.DryRun ? 0 : storedPartitions, snapshots);
    }

    private async Task<int> FetchAndStoreSecurityDataAsync(
        EdgarIngestRequest request,
        IReadOnlyList<EdgarFilerRecord> filers,
        List<string> errors,
        CancellationToken ct)
    {
        var storedPartitions = 0;
        foreach (var filer in filers)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var record = await _provider.FetchSecurityDataAsync(filer, request.MaxFilers, ct)
                    .ConfigureAwait(false);

                if (record.DebtOfferings.Count == 0 && record.FundHoldings.Count == 0)
                    continue;

                if (!request.DryRun)
                {
                    await _store.SaveSecurityDataAsync(record, ct).ConfigureAwait(false);
                }

                storedPartitions++;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                errors.Add($"{filer.Cik}/security-data: {ex.Message}");
                _logger.LogWarning(ex, "EDGAR security-data store write failed for CIK {Cik}", filer.Cik);
            }
        }

        return request.DryRun ? 0 : storedPartitions;
    }

    private async Task<(SecurityWriteResult Result, Guid? SecurityId)> CreateOrAmendSecurityAsync(
        EdgarTickerAssociation association,
        IReadOnlyDictionary<string, EdgarFilerRecord> filersByCik,
        IReadOnlyDictionary<string, IssuerFactSnapshot> factSnapshots,
        bool dryRun,
        CancellationToken ct)
    {
        var existing = await ResolveExistingSecurityAsync(association, ct).ConfigureAwait(false);
        var now = DateTimeOffset.UtcNow;
        var filer = filersByCik.GetValueOrDefault(association.Cik);
        var snapshot = factSnapshots.GetValueOrDefault(association.Cik);
        var identifiers = BuildIdentifiers(association, now);

        if (existing is null)
        {
            if (dryRun)
                return (SecurityWriteResult.Created, null);

            var request = BuildCreateRequest(association, filer, snapshot, identifiers, now);
            await _securityMasterService.CreateAsync(request, ct).ConfigureAwait(false);
            return (SecurityWriteResult.Created, request.SecurityId);
        }

        var identifiersToAdd = identifiers
            .Where(candidate => !existing.Identifiers.Any(existingId => SameIdentifier(existingId, candidate)))
            .ToArray();

        var commonTerms = BuildCommonTerms(association, filer, snapshot, existing);
        if (dryRun)
            return (identifiersToAdd.Length > 0 ? SecurityWriteResult.Amended : SecurityWriteResult.Skipped, null);

        if (identifiersToAdd.Length == 0 && !ShouldAmendCommonTerms(existing, commonTerms))
            return (SecurityWriteResult.Skipped, existing.SecurityId);

        var amendRequest = new AmendSecurityTermsRequest(
            SecurityId: existing.SecurityId,
            ExpectedVersion: existing.Version,
            CommonTerms: commonTerms,
            AssetSpecificTermsPatch: null,
            IdentifiersToAdd: identifiersToAdd,
            IdentifiersToExpire: Array.Empty<SecurityIdentifierDto>(),
            EffectiveFrom: now,
            SourceSystem: SourceSystem,
            UpdatedBy: nameof(EdgarIngestOrchestrator),
            SourceRecordId: association.Cik,
            Reason: BuildReason(association, filer, snapshot));
        await (_systemAmender is { } systemAmender
                ? systemAmender.AmendTermsAsync(amendRequest, ct)
                : _securityMasterService.AmendTermsAsync(amendRequest, ct))
            .ConfigureAwait(false);

        return (SecurityWriteResult.Amended, existing.SecurityId);
    }

    /// <summary>
    /// Finds the security a ticker association updates. The ticker (EDGAR-sourced first) is the
    /// security-level key. A CIK names the issuer, which can have several share classes, so a CIK match
    /// is used only for a record keyed by CIK alone: exactly one claimant (when the canonical resolver is
    /// composed) and no ticker of its own. Otherwise a new share-class ticker would be attached to a
    /// sibling class (ADR-033; this replaces the first-match CIK lookup).
    /// </summary>
    private async Task<SecurityDetailDto?> ResolveExistingSecurityAsync(
        EdgarTickerAssociation association,
        CancellationToken ct)
    {
        var byEdgarTicker = await _queryService.GetByIdentifierAsync(
                SecurityIdentifierKind.Ticker,
                association.Ticker,
                SourceSystem,
                ct)
            .ConfigureAwait(false);
        if (byEdgarTicker is not null)
            return byEdgarTicker;

        var byTicker = await _queryService.GetByIdentifierAsync(
                SecurityIdentifierKind.Ticker,
                association.Ticker,
                provider: null,
                ct)
            .ConfigureAwait(false);
        if (byTicker is not null)
            return byTicker;

        var byCik = await ResolveCikOnlyRecordAsync(SecurityIdentifierKind.Cik, association.Cik, CikProvider, ct).ConfigureAwait(false);
        if (byCik is not null)
            return byCik;

        return await ResolveCikOnlyRecordAsync(SecurityIdentifierKind.ProviderSymbol, association.Cik, LegacyCikProvider, ct)
            .ConfigureAwait(false);
    }

    private async Task<SecurityDetailDto?> ResolveCikOnlyRecordAsync(
        SecurityIdentifierKind kind,
        string cik,
        string provider,
        CancellationToken ct)
    {
        if (_resolver is not null)
        {
            var resolution = await _resolver
                .ResolveCanonicalAsync(new ResolveSecurityRequest(kind, cik, provider, AsOfUtc: null), ct)
                .ConfigureAwait(false);
            if (resolution.Status != SecurityResolutionStatus.Resolved)
            {
                if (resolution.Status == SecurityResolutionStatus.Ambiguous)
                {
                    _logger.LogDebug(
                        "EDGAR CIK {Cik} is claimed by {ClaimantCount} securities; it does not choose between share classes",
                        cik,
                        resolution.CandidateSecurityIds.Count);
                }

                return null;
            }
        }

        var candidate = await _queryService.GetByIdentifierAsync(kind, cik, provider, ct).ConfigureAwait(false);
        if (candidate is null)
            return null;

        // A record that already has a ticker is a specific share class; a different ticker of the same
        // issuer is a different security.
        return candidate.Identifiers.Any(static identifier => identifier.Kind == SecurityIdentifierKind.Ticker)
            ? null
            : candidate;
    }

    /// <summary>
    /// ADR-033: links the security to its issuer in the entity master, found or registered by CIK.
    /// Best-effort: an entity-master failure is reported in the run's errors and never undoes the
    /// security write. Returns true when a new issuer link was recorded.
    /// </summary>
    private async Task<bool> LinkIssuerAsync(
        Guid securityId,
        EdgarTickerAssociation association,
        EdgarFilerRecord? filer,
        List<string> errors,
        CancellationToken ct)
    {
        if (_entityMaster is null || string.IsNullOrWhiteSpace(association.Cik))
            return false;

        try
        {
            var cik = EntityMasterRules.NormalizeCik(association.Cik)!;
            var issuer = await _entityMaster.FindByCikAsync(cik, ct).ConfigureAwait(false);
            if (issuer is null)
            {
                var legalName = filer?.Name ?? association.Name ?? cik;
                try
                {
                    issuer = await _entityMaster
                        .CreateEntityAsync(new CreateLegalEntityRequest(legalName, Lei: null, Cik: cik, Jurisdiction: null), nameof(EdgarIngestOrchestrator), ct)
                        .ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Another writer may have registered the CIK first; the CIK is unique, so read it back.
                    issuer = await _entityMaster.FindByCikAsync(cik, ct).ConfigureAwait(false);
                    if (issuer is null)
                        throw;
                }
            }

            var today = DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime);
            var current = await _entityMaster.GetIssuerAsync(securityId, today, ct).ConfigureAwait(false);
            if (current?.IssuerEntityId == issuer.EntityId)
                return false;

            if (current is not null && current.ValidFrom >= today)
            {
                // A link that started today cannot be superseded today; report it rather than guess.
                errors.Add($"{association.Cik}/{association.Ticker}: security '{securityId:D}' is already linked to issuer '{current.IssuerEntityId:D}' from {current.ValidFrom:yyyy-MM-dd}; EDGAR names issuer '{issuer.EntityId:D}'.");
                return false;
            }

            await _entityMaster.LinkIssuerAsync(securityId, issuer.EntityId, today, nameof(EdgarIngestOrchestrator), ct).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            errors.Add($"{association.Cik}/{association.Ticker}/issuer-link: {ex.Message}");
            _logger.LogWarning(ex, "EDGAR issuer link failed for {Cik}/{Ticker}", association.Cik, association.Ticker);
            return false;
        }
    }

    private static CreateSecurityRequest BuildCreateRequest(
        EdgarTickerAssociation association,
        EdgarFilerRecord? filer,
        IssuerFactSnapshot? snapshot,
        IReadOnlyList<SecurityIdentifierDto> identifiers,
        DateTimeOffset now)
    {
        var isMutualFund = IsMutualFundAssociation(association);
        var assetClass = isMutualFund ? "OtherSecurity" : "Equity";
        var assetTerms = isMutualFund
            ? JsonSerializer.SerializeToElement(
                new Dictionary<string, object?>
                {
                    ["schemaVersion"] = SecurityMasterSchemaVersions.LegacyAssetSpecificTerms,
                    [SecurityTermsEnvelope.FormatKey] = SecurityTermsFormats.Flat,
                    [SecurityTermsEnvelope.AssetClassTermsVersionKey] = SecurityAssetClassCatalog.GetStampFloor(assetClass),
                    ["category"] = "MutualFund",
                    ["subType"] = association.SecurityType,
                    ["issuerName"] = association.Name ?? filer?.Name
                },
                SecurityMasterJsonContext.Default.DictionaryStringObject)
            : JsonSerializer.SerializeToElement(
                new Dictionary<string, object?>
                {
                    ["schemaVersion"] = SecurityMasterSchemaVersions.LegacyAssetSpecificTerms,
                    [SecurityTermsEnvelope.FormatKey] = SecurityTermsFormats.Flat,
                    [SecurityTermsEnvelope.AssetClassTermsVersionKey] = SecurityAssetClassCatalog.GetStampFloor(assetClass),
                    ["classification"] = "Common",
                    ["shareClass"] = snapshot?.Security12bTitle?.RawValue
                },
                SecurityMasterJsonContext.Default.DictionaryStringObject);

        return new CreateSecurityRequest(
            SecurityId: DeterministicSecurityId(association),
            AssetClass: assetClass,
            CommonTerms: BuildCommonTerms(association, filer, snapshot, existing: null),
            AssetSpecificTerms: assetTerms,
            Identifiers: identifiers,
            EffectiveFrom: now,
            SourceSystem: SourceSystem,
            UpdatedBy: nameof(EdgarIngestOrchestrator),
            SourceRecordId: association.Cik,
            Reason: BuildReason(association, filer, snapshot));
    }

    private static JsonElement BuildCommonTerms(
        EdgarTickerAssociation association,
        EdgarFilerRecord? filer,
        IssuerFactSnapshot? snapshot,
        SecurityDetailDto? existing)
    {
        var existingCommon = existing?.CommonTerms;
        var displayName = association.Name
            ?? filer?.Name
            ?? GetString(existingCommon, "displayName")
            ?? association.Ticker;

        var exchange = association.Exchange
            ?? filer?.Exchanges.FirstOrDefault()
            ?? snapshot?.SecurityExchangeName?.RawValue
            ?? GetString(existingCommon, "exchange");

        var common = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["displayName"] = displayName,
            ["currency"] = GetString(existingCommon, "currency") ?? "USD",
            ["countryOfRisk"] = GetString(existingCommon, "countryOfRisk") ?? "US"
        };

        AddIfNotNull(common, "issuerName", filer?.Name ?? GetString(existingCommon, "issuerName") ?? association.Name);
        AddIfNotNull(common, "exchange", exchange);
        AddIfNotNull(common, "lotSize", GetDecimal(existingCommon, "lotSize"));
        AddIfNotNull(common, "tickSize", GetDecimal(existingCommon, "tickSize"));
        AddIfNotNull(common, "primaryListingMic", GetString(existingCommon, "primaryListingMic"));
        AddIfNotNull(common, "countryOfIncorporation", GetString(existingCommon, "countryOfIncorporation"));
        AddIfNotNull(common, "settlementCycleDays", GetInt(existingCommon, "settlementCycleDays"));
        AddIfNotNull(common, "holidayCalendarId", GetString(existingCommon, "holidayCalendarId"));

        return JsonSerializer.SerializeToElement(
            common,
            SecurityMasterJsonContext.Default.DictionaryStringObject);
    }

    private static void AddIfNotNull(Dictionary<string, object?> dictionary, string key, object? value)
    {
        if (value is not null)
            dictionary[key] = value;
    }

    private static IReadOnlyList<SecurityIdentifierDto> BuildIdentifiers(
        EdgarTickerAssociation association,
        DateTimeOffset now)
    {
        var identifiers = new List<SecurityIdentifierDto>
        {
            new(SecurityIdentifierKind.Ticker, association.Ticker, IsPrimary: true, ValidFrom: now, Provider: SourceSystem),
            new(SecurityIdentifierKind.Cik, association.Cik, IsPrimary: false, ValidFrom: now, Provider: CikProvider)
        };

        if (!string.IsNullOrWhiteSpace(association.SeriesId))
        {
            identifiers.Add(new SecurityIdentifierDto(
                SecurityIdentifierKind.ProviderSymbol,
                association.SeriesId,
                IsPrimary: false,
                ValidFrom: now,
                Provider: SeriesProvider));
        }

        if (!string.IsNullOrWhiteSpace(association.ClassId))
        {
            identifiers.Add(new SecurityIdentifierDto(
                SecurityIdentifierKind.ProviderSymbol,
                association.ClassId,
                IsPrimary: false,
                ValidFrom: now,
                Provider: ClassProvider));
        }

        return identifiers;
    }

    private static bool ShouldAmendCommonTerms(SecurityDetailDto existing, JsonElement commonTerms)
        => !string.Equals(existing.CommonTerms.GetRawText(), commonTerms.GetRawText(), StringComparison.Ordinal);

    private static bool SameIdentifier(SecurityIdentifierDto left, SecurityIdentifierDto right)
        => left.Kind == right.Kind
           && string.Equals(left.Value, right.Value, StringComparison.OrdinalIgnoreCase)
           && string.Equals(left.Provider ?? string.Empty, right.Provider ?? string.Empty, StringComparison.OrdinalIgnoreCase);

    private static string BuildReason(
        EdgarTickerAssociation association,
        EdgarFilerRecord? filer,
        IssuerFactSnapshot? snapshot)
    {
        var parts = new List<string>
        {
            "EDGAR reference-data ingest",
            $"CIK={association.Cik}",
            $"ticker={association.Ticker}"
        };

        if (!string.IsNullOrWhiteSpace(association.Exchange))
            parts.Add($"exchange={association.Exchange}");
        if (!string.IsNullOrWhiteSpace(filer?.Sic))
            parts.Add($"sic={filer.Sic}");
        if (!string.IsNullOrWhiteSpace(filer?.EntityType))
            parts.Add($"entityType={filer.EntityType}");
        if (!string.IsNullOrWhiteSpace(filer?.Category))
            parts.Add($"category={filer.Category}");
        if (!string.IsNullOrWhiteSpace(filer?.FiscalYearEnd))
            parts.Add($"fiscalYearEnd={filer.FiscalYearEnd}");
        if (!string.IsNullOrWhiteSpace(filer?.StateOfIncorporation))
            parts.Add($"stateOfIncorporation={filer.StateOfIncorporation}");
        if (!string.IsNullOrWhiteSpace(filer?.Website))
            parts.Add($"website={filer.Website}");
        if (!string.IsNullOrWhiteSpace(filer?.InvestorWebsite))
            parts.Add($"investorWebsite={filer.InvestorWebsite}");
        if (!string.IsNullOrWhiteSpace(snapshot?.Security12bTitle?.RawValue))
            parts.Add($"securityTitle={snapshot.Security12bTitle.RawValue}");
        if (!string.IsNullOrWhiteSpace(snapshot?.SecurityExchangeName?.RawValue))
            parts.Add($"securityExchangeName={snapshot.SecurityExchangeName.RawValue}");
        if (!string.IsNullOrWhiteSpace(snapshot?.EntityFileNumber?.RawValue))
            parts.Add($"secFileNumber={snapshot.EntityFileNumber.RawValue}");
        if (snapshot?.SharesOutstanding?.NumericValue is decimal sharesOutstanding)
            parts.Add($"sharesOutstanding={sharesOutstanding}");
        if (snapshot?.PublicFloat?.NumericValue is decimal publicFloat)
            parts.Add($"publicFloat={publicFloat}");
        var recentFiling = filer?.RecentFilings.FirstOrDefault();
        if (recentFiling is not null)
        {
            parts.Add($"latestFiling={recentFiling.Form}/{recentFiling.FilingDate?.ToString("yyyy-MM-dd") ?? "undated"}");
            if (!string.IsNullOrWhiteSpace(recentFiling.FileNumber))
                parts.Add($"latestFileNumber={recentFiling.FileNumber}");
        }

        return string.Join("; ", parts);
    }

    private static bool IsMutualFundAssociation(EdgarTickerAssociation association)
        => !string.IsNullOrWhiteSpace(association.SeriesId)
           || !string.IsNullOrWhiteSpace(association.ClassId)
           || (association.SecurityType?.Contains("fund", StringComparison.OrdinalIgnoreCase) ?? false)
           || association.Source.Contains("mf", StringComparison.OrdinalIgnoreCase);

    private static Guid DeterministicSecurityId(EdgarTickerAssociation association)
    {
        var input = string.Join(
            "|",
            "edgar-security",
            association.Cik,
            association.Ticker.ToUpperInvariant(),
            association.SeriesId ?? string.Empty,
            association.ClassId ?? string.Empty);

        var hash = Sha256Digest.ComputeBytesUtf8(input);
        Span<byte> bytes = stackalloc byte[16];
        hash.AsSpan(0, 16).CopyTo(bytes);
        return new Guid(bytes);
    }

    private static EdgarIngestRequest NormalizeRequest(EdgarIngestRequest request)
    {
        var normalizedCik = NormalizeCik(request.Cik);
        return request with
        {
            Scope = string.IsNullOrWhiteSpace(request.Scope) ? "all-filers" : request.Scope,
            Cik = normalizedCik.Length == 0 ? null : normalizedCik,
            MaxFilers = request.MaxFilers is > 0 ? request.MaxFilers : null
        };
    }

    private static IReadOnlySet<string> SelectCiks(
        EdgarIngestRequest request,
        IReadOnlyList<EdgarFilerRecord> filers,
        IReadOnlyList<EdgarTickerAssociation> associations)
    {
        if (!string.IsNullOrWhiteSpace(request.Cik))
            return new HashSet<string>([request.Cik], StringComparer.Ordinal);

        var source = filers.Count > 0
            ? filers.Select(f => f.Cik)
            : associations.Select(a => a.Cik);

        return source
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Take(request.MaxFilers.GetValueOrDefault(int.MaxValue))
            .ToHashSet(StringComparer.Ordinal);
    }

    private static IReadOnlyList<EdgarTickerAssociation> FilterAssociations(
        EdgarIngestRequest request,
        IReadOnlyList<EdgarTickerAssociation> associations,
        IReadOnlySet<string> selectedCiks)
    {
        var filtered = associations
            .Where(a => selectedCiks.Count == 0 || selectedCiks.Contains(a.Cik))
            .ToList();

        return request.MaxFilers is > 0 && selectedCiks.Count == 0
            ? filtered.Take(request.MaxFilers.Value).ToList()
            : filtered;
    }

    private async Task<int> CountOpenConflictsAsync(CancellationToken ct)
    {
        if (_conflictService is null)
            return 0;

        try
        {
            var conflicts = await _conflictService.GetOpenConflictsAsync(ct).ConfigureAwait(false);
            return conflicts.Count;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Reporting zero conflicts on a cancelled run would understate the ingest's outcome.
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "EDGAR ingest could not count Security Master conflicts.");
            return 0;
        }
    }

    internal static string NormalizeCik(string? cik)
    {
        if (string.IsNullOrWhiteSpace(cik))
            return string.Empty;

        var digits = new string(cik.Where(char.IsDigit).ToArray());
        if (digits.Length == 0)
            return string.Empty;

        return digits.Length >= 10
            ? digits[^10..]
            : digits.PadLeft(10, '0');
    }

    private static string? GetString(JsonElement? json, string propertyName)
    {
        if (json is not JsonElement element ||
            element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(propertyName, out var value) ||
            value.ValueKind != JsonValueKind.String)
            return null;

        return value.GetString();
    }

    private static decimal? GetDecimal(JsonElement? json, string propertyName)
    {
        if (json is not JsonElement element ||
            element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(propertyName, out var value) ||
            value.ValueKind != JsonValueKind.Number ||
            !value.TryGetDecimal(out var parsed))
            return null;

        return parsed;
    }

    private static int? GetInt(JsonElement? json, string propertyName)
    {
        if (json is not JsonElement element ||
            element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(propertyName, out var value) ||
            value.ValueKind != JsonValueKind.Number ||
            !value.TryGetInt32(out var parsed))
            return null;

        return parsed;
    }

    private enum SecurityWriteResult
    {
        Created,
        Amended,
        Skipped
    }

    private sealed record FactStoreResult(
        int StoredPartitions,
        IReadOnlyDictionary<string, IssuerFactSnapshot> Snapshots);
}
