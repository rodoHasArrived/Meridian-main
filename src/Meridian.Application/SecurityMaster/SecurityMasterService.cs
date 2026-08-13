using System.Text.Json;
using Meridian.Contracts.SecurityMaster;
using Meridian.FSharp.SecurityMasterInterop;
using Meridian.Infrastructure.Adapters.Polygon;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging;

namespace Meridian.Application.SecurityMaster;

public sealed class SecurityMasterService : ISecurityMasterService, ISecurityMasterAmender, IDisposable
{
    private readonly ISecurityMasterEventStore _eventStore;
    private readonly ISecurityMasterSnapshotStore _snapshotStore;
    private readonly ISecurityMasterStore _store;
    private readonly SecurityMasterAggregateRebuilder _rebuilder;
    private readonly SecurityMasterOptions _options;
    private readonly ILogger<SecurityMasterService> _logger;
    private readonly ISecurityMasterConflictService? _conflictService;
    private readonly IPolygonCorporateActionFetcher? _corporateActionFetcher;
    private readonly SecurityMasterProjectionCache? _projectionCache;
    private readonly SecurityMasterCanonicalSymbolSeedService? _seedService;
    private readonly Validation.AssetClassValidatorRegistry? _assetClassValidators;

    // Owned lifetime token so background fire-and-forget tasks are cancelled on disposal.
    private readonly CancellationTokenSource _serviceCts = new();

    public SecurityMasterService(
        ISecurityMasterEventStore eventStore,
        ISecurityMasterSnapshotStore snapshotStore,
        ISecurityMasterStore store,
        SecurityMasterAggregateRebuilder rebuilder,
        SecurityMasterOptions options,
        ILogger<SecurityMasterService> logger,
        ISecurityMasterConflictService? conflictService = null,
        IPolygonCorporateActionFetcher? corporateActionFetcher = null,
        SecurityMasterProjectionCache? projectionCache = null,
        SecurityMasterCanonicalSymbolSeedService? seedService = null,
        Validation.AssetClassValidatorRegistry? assetClassValidators = null)
    {
        _eventStore = eventStore;
        _snapshotStore = snapshotStore;
        _store = store;
        _rebuilder = rebuilder;
        _options = options;
        _logger = logger;
        _conflictService = conflictService;
        _corporateActionFetcher = corporateActionFetcher;
        _projectionCache = projectionCache;
        _seedService = seedService;
        _assetClassValidators = assetClassValidators;
    }

    public Task<SecurityDetailDto> CreateAsync(CreateSecurityRequest request, CancellationToken ct = default)
        => ExecuteCreateAsync(request, ct);

    public Task<SecurityDetailDto> AmendTermsAsync(AmendSecurityTermsRequest request, CancellationToken ct = default)
        => AmendTermsInternalAsync(request, eventType: "TermsAmended", ct);

    private async Task<SecurityDetailDto> AmendTermsInternalAsync(
        AmendSecurityTermsRequest request,
        string eventType,
        CancellationToken ct)
    {
        var aliasProjection = await _store.GetProjectionAsync(request.SecurityId, ct).ConfigureAwait(false);
        var current = await _rebuilder.RebuildEconomicDefinitionAsync(request.SecurityId, aliasProjection, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Security '{request.SecurityId}' was not found.");

        var currentProjection = SecurityEconomicDefinitionAdapter.ToProjection(current, aliasProjection?.Aliases);
        EnsureAssetClassRoundTripsSafely(currentProjection);
        var currentRecord = SecurityMasterMapping.ToRecord(currentProjection);
        var result = SecurityMasterCommandFacade.Amend(currentRecord, SecurityMasterMapping.ToAmendCommand(request, currentProjection));
        var projection = CreateProjectionFromResult(
            result,
            currentProjection.Aliases,
            GetProfileBackedAssetClassOverride(currentProjection),
            GetProfileBackedAssetSpecificTermsOverride(currentProjection, request.AssetSpecificTermsPatch));
        EnsureProfileBackedTermsAreCatalogValid(projection);
        var economic = SecurityEconomicDefinitionAdapter.ToEconomicRecord(projection);
        var envelope = SecurityMasterMapping.ToEventEnvelope(
            economic,
            eventType,
            request.UpdatedBy,
            request.SourceSystem,
            request.Reason,
            projection.Version);

        await _eventStore.AppendAsync(request.SecurityId, request.ExpectedVersion, [envelope], ct).ConfigureAwait(false);
        await _store.UpsertProjectionAsync(projection, ct).ConfigureAwait(false);
        await SaveSnapshotIfNeededAsync(economic, ct).ConfigureAwait(false);
        await TryRecordConflictsAsync(projection, request.SecurityId, ct).ConfigureAwait(false);
        // The amend seam is the one place the pre-write golden copy and the incoming revision are
        // both in hand: record field-level cross-source conflicts (a different source disagreeing
        // on economic/common terms) so the authority policy has non-identifier conflicts to judge.
        await TryRecordFieldConflictsAsync(currentProjection, projection, request.SecurityId, ct).ConfigureAwait(false);

        // Enqueue a best-effort corporate action re-fetch so that updated identifiers
        // (e.g. ticker changes after a merger rename) are reflected in the backfill history.
        if (_corporateActionFetcher is not null)
        {
            var ticker = projection.PrimaryIdentifierValue;
            var securityId = projection.SecurityId;
            var ct2 = _serviceCts.Token;
            _ = Task.Run(async () =>
            {
                try
                {
                    await _corporateActionFetcher.FetchAndPersistAsync(ticker, securityId, ct2)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Background corporate action sync failed after amendment for {Ticker} ({SecurityId})",
                        ticker, securityId);
                }
            }, ct2);
        }

        // Keep the in-memory projection cache and canonical registry consistent with the DB write.
        _projectionCache?.Upsert(projection);
        TryReseedRegistryInBackground();

        return SecurityMasterMapping.ToDetail(projection);
    }

    public async Task<SecurityDetailDto> AmendPreferredEquityTermsAsync(Guid securityId, AmendPreferredEquityTermsRequest request, CancellationToken ct = default)
    {
        var currentProjection = await _store.GetProjectionAsync(securityId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Security '{securityId}' was not found.");

        var amendRequest = new AmendSecurityTermsRequest(
            SecurityId: securityId,
            ExpectedVersion: request.ExpectedVersion,
            CommonTerms: null,
            AssetSpecificTermsPatch: SecurityMasterMapping.BuildPreferredEquityTermsPatch(currentProjection, request),
            IdentifiersToAdd: Array.Empty<SecurityIdentifierDto>(),
            IdentifiersToExpire: Array.Empty<SecurityIdentifierDto>(),
            EffectiveFrom: request.EffectiveFrom,
            SourceSystem: request.SourceSystem,
            UpdatedBy: request.UpdatedBy,
            SourceRecordId: request.SourceRecordId,
            Reason: request.Reason);

        return await AmendTermsInternalAsync(amendRequest, eventType: "PreferredTermsAmended", ct).ConfigureAwait(false);
    }

    public async Task<SecurityDetailDto> AmendConvertibleEquityTermsAsync(Guid securityId, AmendConvertibleEquityTermsRequest request, CancellationToken ct = default)
    {
        var currentProjection = await _store.GetProjectionAsync(securityId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Security '{securityId}' was not found.");

        var amendRequest = new AmendSecurityTermsRequest(
            SecurityId: securityId,
            ExpectedVersion: request.ExpectedVersion,
            CommonTerms: null,
            AssetSpecificTermsPatch: SecurityMasterMapping.BuildConvertibleEquityTermsPatch(currentProjection, request),
            IdentifiersToAdd: Array.Empty<SecurityIdentifierDto>(),
            IdentifiersToExpire: Array.Empty<SecurityIdentifierDto>(),
            EffectiveFrom: request.EffectiveFrom,
            SourceSystem: request.SourceSystem,
            UpdatedBy: request.UpdatedBy,
            SourceRecordId: request.SourceRecordId,
            Reason: request.Reason);

        return await AmendTermsInternalAsync(amendRequest, eventType: "ConvertibleTermsAmended", ct).ConfigureAwait(false);
    }

    public async Task DeactivateAsync(DeactivateSecurityRequest request, CancellationToken ct = default)
    {
        var aliasProjection = await _store.GetProjectionAsync(request.SecurityId, ct).ConfigureAwait(false);
        var current = await _rebuilder.RebuildEconomicDefinitionAsync(request.SecurityId, aliasProjection, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Security '{request.SecurityId}' was not found.");

        var currentProjection = SecurityEconomicDefinitionAdapter.ToProjection(current, aliasProjection?.Aliases);
        EnsureAssetClassRoundTripsSafely(currentProjection);
        var currentRecord = SecurityMasterMapping.ToRecord(currentProjection);
        var result = SecurityMasterCommandFacade.Deactivate(currentRecord, SecurityMasterMapping.ToDeactivateCommand(request));
        var projection = CreateProjectionFromResult(
            result,
            currentProjection.Aliases,
            GetProfileBackedAssetClassOverride(currentProjection),
            GetProfileBackedAssetSpecificTermsOverride(currentProjection, assetSpecificTermsPatch: null));
        var economic = SecurityEconomicDefinitionAdapter.ToEconomicRecord(projection);
        var envelope = SecurityMasterMapping.ToEventEnvelope(
            economic,
            "SecurityDeactivated",
            request.UpdatedBy,
            request.SourceSystem,
            request.Reason,
            projection.Version);

        await _eventStore.AppendAsync(request.SecurityId, request.ExpectedVersion, [envelope], ct).ConfigureAwait(false);
        await _store.UpsertProjectionAsync(projection, ct).ConfigureAwait(false);
        await SaveSnapshotIfNeededAsync(economic, ct).ConfigureAwait(false);
    }

    public Task<SecurityAliasDto> UpsertAliasAsync(UpsertSecurityAliasRequest request, CancellationToken ct = default)
    {
        var alias = new SecurityAliasDto(
            request.AliasId,
            request.SecurityId,
            request.AliasKind,
            request.AliasValue,
            request.Provider,
            request.Scope,
            request.Reason,
            request.CreatedBy,
            DateTimeOffset.UtcNow,
            request.ValidFrom,
            request.ValidTo,
            true);

        return UpsertAliasAsyncCore(alias, ct);
    }

    private async Task<SecurityDetailDto> ExecuteCreateAsync(CreateSecurityRequest request, CancellationToken ct)
    {
        var result = SecurityMasterCommandFacade.Create(SecurityMasterMapping.ToCreateCommand(request));
        var projection = CreateProjectionFromResult(
            result,
            aliases: null,
            GetProfileBackedAssetClassOverride(request.AssetClass, request.AssetSpecificTerms),
            GetProfileBackedAssetSpecificTermsOverride(request.AssetSpecificTerms));
        EnsureProfileBackedTermsAreCatalogValid(projection);
        var economic = SecurityEconomicDefinitionAdapter.ToEconomicRecord(projection);
        var envelope = SecurityMasterMapping.ToEventEnvelope(
            economic,
            "SecurityCreated",
            request.UpdatedBy,
            request.SourceSystem,
            request.Reason,
            projection.Version);

        await _eventStore.AppendAsync(request.SecurityId, expectedVersion: 0, [envelope], ct).ConfigureAwait(false);
        await _store.UpsertProjectionAsync(projection, ct).ConfigureAwait(false);
        await SaveSnapshotIfNeededAsync(economic, ct).ConfigureAwait(false);

        await TryRecordConflictsAsync(projection, request.SecurityId, ct).ConfigureAwait(false);

        // Enqueue a best-effort corporate action backfill for the newly-created security so
        // that historical corp action data is available immediately for backtesting.
        if (_corporateActionFetcher is not null)
        {
            var ticker = projection.PrimaryIdentifierValue;
            var securityId = projection.SecurityId;
            var ct2 = _serviceCts.Token;
            _ = Task.Run(async () =>
            {
                try
                {
                    await _corporateActionFetcher.FetchAndPersistAsync(ticker, securityId, ct2)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Background corporate action sync failed for new security {Ticker} ({SecurityId})",
                        ticker, securityId);
                }
            }, ct2);
        }

        // Keep the in-memory projection cache and canonical registry consistent with the DB write.
        _projectionCache?.Upsert(projection);
        TryReseedRegistryInBackground();

        return SecurityMasterMapping.ToDetail(projection);
    }

    private void TryReseedRegistryInBackground()
    {
        if (_seedService is null)
            return;

        var ct = _serviceCts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await _seedService.SeedAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Background canonical symbol registry re-seed failed.");
            }
        }, ct);
    }

    private async Task TryRecordConflictsAsync(SecurityProjectionRecord projection, Guid securityId, CancellationToken ct)
    {
        if (_conflictService is null)
            return;

        try
        {
            await _conflictService.RecordConflictsForProjectionAsync(projection, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Conflict detection failed for security {SecurityId}", securityId);
        }
    }

    private async Task TryRecordFieldConflictsAsync(
        SecurityProjectionRecord previous,
        SecurityProjectionRecord incoming,
        Guid securityId,
        CancellationToken ct)
    {
        if (_conflictService is null)
            return;

        try
        {
            await _conflictService.RecordFieldConflictsAsync(previous, incoming, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Field conflict detection failed for security {SecurityId}", securityId);
        }
    }

    public void Dispose()
    {
        _serviceCts.Cancel();
        _serviceCts.Dispose();
    }

    /// <summary>
    /// Read tolerance must not become write tolerance: a record whose stored asset class this node
    /// does not recognize deserializes through the OtherSecurity fallback, so re-serializing it on
    /// amend or deactivate would silently rewrite its asset class and drop its terms. Refuse the
    /// write instead — the record stays readable, and the change must come from a node that
    /// supports the class.
    /// </summary>
    private static void EnsureAssetClassRoundTripsSafely(SecurityProjectionRecord projection)
    {
        if (!SecurityAssetClassCatalog.AssetClasses.Contains(projection.AssetClass, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Security '{projection.SecurityId:D}' has asset class '{projection.AssetClass}', which this node does not recognize. " +
                "Amending or deactivating it here would re-serialize the record through the OtherSecurity fallback and rewrite its " +
                "asset class, so the write is refused. Apply the change from a node that supports this asset class.");
        }

        EnsureEquityClassificationRoundTripsSafely(projection);
        EnsureCustomAssetEnvelopeRoundTripsSafely(projection);
    }

    /// <summary>
    /// Profile-backed records reference an approved profile that governs their dynamic fields, but
    /// the F# domain command cannot see the profile catalog — without this check an unknown, draft,
    /// or field-violating profile persists canonically and is only discovered by a later validation
    /// read. Runs the registered profile validator BEFORE the create/amend/deactivate event is
    /// appended and refuses the write on Error-severity issues. Skipped when no registry was
    /// supplied (harnesses that exercise storage mechanics without reference data).
    /// </summary>
    private void EnsureProfileBackedTermsAreCatalogValid(SecurityProjectionRecord projection)
    {
        if (_assetClassValidators is null)
        {
            return;
        }

        var isCustomAsset = string.Equals(projection.AssetClass, "CustomAsset", StringComparison.OrdinalIgnoreCase);
        var terms = projection.AssetSpecificTerms;
        var referencesProfile = terms.ValueKind == JsonValueKind.Object
            && terms.TryGetProperty("customProfileId", out var profileId)
            && profileId.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(profileId.GetString());
        if (!isCustomAsset && !referencesProfile)
        {
            return;
        }

        // The CustomAsset validator requires a profile reference; profile-backed records whose
        // asset class resolved elsewhere validate the referenced profile through the optional
        // OtherSecurity-keyed validator instead.
        if (!_assetClassValidators.TryGetValidator(isCustomAsset ? "CustomAsset" : "OtherSecurity", out var validator))
        {
            return;
        }

        var issues = validator.Validate(new Validation.SecurityValidationContext(projection, DateTimeOffset.UtcNow));
        var errors = issues
            .Where(static issue => issue.Severity == SecurityValidationSeverityDto.Error)
            .ToArray();
        if (errors.Length > 0)
        {
            var summary = string.Join("; ", errors.Select(static issue => $"[{issue.Code}] {issue.Message}"));
            throw new InvalidOperationException(
                $"Security '{projection.SecurityId:D}' references an asset profile that fails catalog validation, " +
                $"so the write is refused: {summary}");
        }
    }

    /// <summary>
    /// A legacy CustomAsset row that predates the profile envelope (no <c>customProfileId</c>)
    /// deserializes through the OtherSecurity salvage path even though "CustomAsset" is a
    /// catalog-recognized class, so an amend or deactivate would re-serialize the fallback as
    /// OtherSecurity and drop the record's unmodeled custom fields. The envelope, not the catalog
    /// name, is what makes the round-trip lossless — refuse the write when it is absent.
    /// </summary>
    private static void EnsureCustomAssetEnvelopeRoundTripsSafely(SecurityProjectionRecord projection)
    {
        if (!string.Equals(projection.AssetClass, "CustomAsset", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var terms = projection.AssetSpecificTerms;
        var hasEnvelope = terms.ValueKind == JsonValueKind.Object
            && terms.TryGetProperty("customProfileId", out var profileId)
            && profileId.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(profileId.GetString());
        if (!hasEnvelope)
        {
            throw new InvalidOperationException(
                $"Security '{projection.SecurityId:D}' is a CustomAsset without a profile envelope (no customProfileId), " +
                "so it reads through the OtherSecurity salvage path. Amending or deactivating it here would re-serialize " +
                "that fallback and drop its custom fields, so the write is refused. Migrate the record to a profile-backed " +
                "envelope through a governed amendment first.");
        }
    }

    /// <summary>
    /// Same read-tolerance-must-not-become-write-tolerance rule, one level down: an equity whose
    /// stored classification discriminant this node does not recognize deserializes as
    /// <c>EquityClassification.Other(raw)</c>, which re-serializes WITHOUT the record's
    /// <c>preferredTerms</c>/<c>convertibleTerms</c> blocks. When those blocks are present the write
    /// would silently delete structure this node did not understand, so it is refused; without them
    /// the Other round-trip is lossless (the raw label survives as <c>otherClassification</c>).
    /// </summary>
    private static void EnsureEquityClassificationRoundTripsSafely(SecurityProjectionRecord projection)
    {
        if (!string.Equals(projection.AssetClass, "Equity", StringComparison.OrdinalIgnoreCase)
            || projection.AssetSpecificTerms.ValueKind != JsonValueKind.Object
            || !projection.AssetSpecificTerms.TryGetProperty("classification", out var classification)
            || classification.ValueKind != JsonValueKind.String)
        {
            return;
        }

        // The serializer's discriminants are exact-case; anything else round-trips as Other.
        var raw = classification.GetString();
        var isKnownDiscriminant = raw is "Common" or "Preferred" or "Convertible" or "ConvertiblePreferred" or "Other";
        if (isKnownDiscriminant)
        {
            return;
        }

        var carriesNestedTerms =
            (projection.AssetSpecificTerms.TryGetProperty("preferredTerms", out var preferred)
                && preferred.ValueKind == JsonValueKind.Object)
            || (projection.AssetSpecificTerms.TryGetProperty("convertibleTerms", out var convertible)
                && convertible.ValueKind == JsonValueKind.Object);
        if (carriesNestedTerms)
        {
            throw new InvalidOperationException(
                $"Security '{projection.SecurityId:D}' is an equity with classification '{raw}', which this node does not " +
                "recognize, and it carries preferred/convertible term blocks tied to that classification. Re-serializing it " +
                "here would degrade the classification to 'Other' and drop those blocks, so the write is refused. Apply the " +
                "change from a node that supports this classification.");
        }
    }

    private static SecurityProjectionRecord CreateProjectionFromResult(
        SecurityMasterCommandResultWrapper result,
        IReadOnlyList<SecurityAliasDto>? aliases = null,
        string? assetClassOverride = null,
        JsonElement? assetSpecificTermsOverride = null)
    {
        if (!result.IsSuccess || result.Snapshot is null)
        {
            var errorText = string.Join("; ", result.ErrorDetails.Select(e => $"[{e.Code}] {e.Message}"));
            throw new InvalidOperationException(errorText);
        }

        var projection = SecurityMasterMapping.ToProjection(result.Snapshot, aliases);
        return projection with
        {
            AssetClass = assetClassOverride ?? projection.AssetClass,
            AssetSpecificTerms = assetSpecificTermsOverride?.Clone() ?? projection.AssetSpecificTerms
        };
    }

    private static string? GetProfileBackedAssetClassOverride(SecurityProjectionRecord projection)
        => IsProfileBackedCustomAsset(projection.AssetClass, projection.AssetSpecificTerms)
            ? GetProfileBackedAssetClassOverride(projection.AssetClass, projection.AssetSpecificTerms)
            : null;

    private static string? GetProfileBackedAssetClassOverride(string assetClass, JsonElement assetSpecificTerms)
    {
        if (!IsProfileBackedCustomAsset(assetClass, assetSpecificTerms))
        {
            return null;
        }

        if (TryResolveProfileBackedAlternativeAssetClass(assetSpecificTerms, out var resolvedAssetClass))
        {
            return resolvedAssetClass;
        }

        return string.Equals(assetClass, "CustomAsset", StringComparison.OrdinalIgnoreCase)
            ? "CustomAsset"
            : null;
    }

    private static JsonElement? GetProfileBackedAssetSpecificTermsOverride(
        SecurityProjectionRecord currentProjection,
        JsonElement? assetSpecificTermsPatch)
    {
        if (assetSpecificTermsPatch is JsonElement patch && IsProfileBackedCustomAsset(currentProjection.AssetClass, patch))
        {
            return patch.Clone();
        }

        return IsProfileBackedCustomAsset(currentProjection.AssetClass, currentProjection.AssetSpecificTerms)
            ? currentProjection.AssetSpecificTerms.Clone()
            : null;
    }

    private static JsonElement? GetProfileBackedAssetSpecificTermsOverride(JsonElement assetSpecificTerms)
        => IsProfileBackedCustomAsset(assetClass: null, assetSpecificTerms)
            ? assetSpecificTerms.Clone()
            : null;

    private static bool IsProfileBackedCustomAsset(string? assetClass, JsonElement assetSpecificTerms)
        => (string.IsNullOrWhiteSpace(assetClass)
            || string.Equals(assetClass, "CustomAsset", StringComparison.OrdinalIgnoreCase)
            || string.Equals(assetClass, "OtherSecurity", StringComparison.OrdinalIgnoreCase)
            || string.Equals(assetClass, "StructuredCredit", StringComparison.OrdinalIgnoreCase)
            || string.Equals(assetClass, "PrivateFundInterest", StringComparison.OrdinalIgnoreCase)
            || string.Equals(assetClass, "PrivateCompanyEquity", StringComparison.OrdinalIgnoreCase)
            || string.Equals(assetClass, "RealEstateHolding", StringComparison.OrdinalIgnoreCase)
            || string.Equals(assetClass, "CommitmentGuarantee", StringComparison.OrdinalIgnoreCase))
           && assetSpecificTerms.ValueKind == System.Text.Json.JsonValueKind.Object
           && assetSpecificTerms.TryGetProperty("customProfileId", out var customProfileId)
           && customProfileId.ValueKind == System.Text.Json.JsonValueKind.String
           && !string.IsNullOrWhiteSpace(customProfileId.GetString());

    private static bool TryResolveProfileBackedAlternativeAssetClass(JsonElement assetSpecificTerms, out string assetClass)
    {
        var profileId = GetString(assetSpecificTerms, "customProfileId");
        var category = GetString(assetSpecificTerms, "category");
        var subType = GetString(assetSpecificTerms, "subType");
        var accountingClassification = GetString(assetSpecificTerms, "accountingClassification");

        if (ProfileFieldsContain(assetSpecificTerms, "tranche", "collateralType", "originalFace", "couponOrIndex")
            && MatchesAny(profileId, category, subType, accountingClassification, "structured-credit-io-po", "StructuredCredit", "MBS", "ABS", "CLO", "CMBS"))
        {
            assetClass = "StructuredCredit";
            return true;
        }

        if (ProfileFieldsContain(assetSpecificTerms, "gpSponsor", "strategy", "vintage", "commitment", "navDate")
            && MatchesAny(profileId, category, subType, accountingClassification, "private-fund-interest", "PrivateFunds", "PartnershipInterest", "PrivateFund"))
        {
            assetClass = "PrivateFundInterest";
            return true;
        }

        if (ProfileFieldsContain(assetSpecificTerms, "issuer", "shareClass", "round", "costBasis")
            && MatchesAny(profileId, category, subType, accountingClassification, "private-company-equity", "PrivateEquity", "PrivateCompanyEquity"))
        {
            assetClass = "PrivateCompanyEquity";
            return true;
        }

        if (ProfileFieldsContain(assetSpecificTerms, "propertyType", "addressOrMarket", "ownershipPercent", "appraisalValue", "valuationDate")
            && MatchesAny(profileId, category, subType, accountingClassification, "real-estate-holding", "RealEstate", "RealEstateInterest", "RealEstateHolding"))
        {
            assetClass = "RealEstateHolding";
            return true;
        }

        if (ProfileFieldsContain(assetSpecificTerms, "counterparty", "committedAmount", "effectiveDate")
            && MatchesAny(profileId, category, subType, accountingClassification, "commitment-guarantee", "CommitmentGuarantee", "UnfundedCommitment", "Guarantee"))
        {
            assetClass = "CommitmentGuarantee";
            return true;
        }

        assetClass = string.Empty;
        return false;
    }

    private static string? GetString(JsonElement json, string propertyName)
        => json.TryGetProperty(propertyName, out var value) && value.ValueKind == System.Text.Json.JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool MatchesAny(string? profileId, string? category, string? subType, string? accountingClassification, params string[] expected)
        => expected.Any(value =>
            string.Equals(profileId, value, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(category, value, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(subType, value, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(accountingClassification, value, StringComparison.OrdinalIgnoreCase));

    private static bool ProfileFieldsContain(JsonElement assetSpecificTerms, params string[] requiredFieldKeys)
        => assetSpecificTerms.TryGetProperty("profileFields", out var profileFields)
           && profileFields.ValueKind == System.Text.Json.JsonValueKind.Object
           && requiredFieldKeys.All(key => profileFields.TryGetProperty(key, out _));

    private async Task<SecurityAliasDto> UpsertAliasAsyncCore(SecurityAliasDto alias, CancellationToken ct)
    {
        await _store.UpsertAliasAsync(alias, ct).ConfigureAwait(false);
        return alias;
    }

    private Task SaveSnapshotIfNeededAsync(SecurityEconomicDefinitionRecord definition, CancellationToken ct)
    {
        if (!ShouldSaveSnapshot(definition))
        {
            return Task.CompletedTask;
        }

        var snapshot = SecurityMasterMapping.ToSnapshot(definition, DateTimeOffset.UtcNow);
        return _snapshotStore.SaveAsync(snapshot, ct);
    }

    private bool ShouldSaveSnapshot(SecurityEconomicDefinitionRecord definition)
        => definition.Version == 1
            || definition.Status == SecurityStatusDto.Inactive
            || (_options.SnapshotIntervalVersions > 0 && definition.Version % _options.SnapshotIntervalVersions == 0);
}
