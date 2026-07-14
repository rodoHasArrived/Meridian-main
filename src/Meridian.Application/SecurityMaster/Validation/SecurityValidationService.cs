using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;

namespace Meridian.Application.SecurityMaster.Validation;

public interface ISecurityValidationService
{
    Task<SecurityValidationReportDto> ValidateSecurityAsync(Guid securityId, CancellationToken ct = default);
    Task<SecurityValidationReportDto> ValidateUniverseAsync(CancellationToken ct = default);
    SecurityValidationReportDto ValidateRecord(
        SecurityProjectionRecord record,
        IReadOnlyList<SecurityProjectionRecord> universe,
        DateTimeOffset? evaluatedAtUtc = null);
}

public sealed class SecurityValidationService : ISecurityValidationService
{
    private static readonly TimeSpan StaleReferenceDataAge = TimeSpan.FromDays(90);
    private static readonly HashSet<SecurityIdentifierKind> CanonicalDuplicateIdentifierKinds =
    [
        SecurityIdentifierKind.Cusip,
        SecurityIdentifierKind.Isin,
        SecurityIdentifierKind.Sedol,
        SecurityIdentifierKind.Figi,
        SecurityIdentifierKind.OccOptionSymbol,
        SecurityIdentifierKind.InternalCode,
        SecurityIdentifierKind.PermId,
        SecurityIdentifierKind.Bbgid,
        SecurityIdentifierKind.Wkn,
        SecurityIdentifierKind.Valoren,
        SecurityIdentifierKind.PermTicker,
        SecurityIdentifierKind.Ric
    ];

    private readonly ISecurityMasterStore _store;
    private readonly AssetClassValidatorRegistry _assetClassValidators;
    private readonly IOperatorOverridesStore? _operatorOverridesStore;

    public SecurityValidationService(
        ISecurityMasterStore store,
        AssetClassValidatorRegistry assetClassValidators,
        IOperatorOverridesStore? operatorOverridesStore = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _assetClassValidators = assetClassValidators ?? throw new ArgumentNullException(nameof(assetClassValidators));
        _operatorOverridesStore = operatorOverridesStore;
    }

    public async Task<SecurityValidationReportDto> ValidateSecurityAsync(Guid securityId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var universe = await _store.LoadAllAsync(ct).ConfigureAwait(false);
        var record = universe.FirstOrDefault(candidate => candidate.SecurityId == securityId);
        if (record is null)
        {
            return BuildReport(
                securityId,
                "Security",
                DateTimeOffset.UtcNow,
                [
                    SecurityValidationIssueFactory.Create(
                        SecurityValidationSeverityDto.Critical,
                        "SM_SECURITY_NOT_FOUND",
                        "Security Master record was not found",
                        $"Security '{securityId}' was not found in the Security Master projection.",
                        ["securityId"],
                        "Refresh the projection or create the missing Security Master record before downstream use.")
                ]);
        }

        var evaluatedAt = DateTimeOffset.UtcNow;
        var issues = ValidateRecord(record, universe, evaluatedAt).Issues.ToList();
        issues.AddRange(await ValidateOperatorOverridesAsync(securityId, ct).ConfigureAwait(false));

        return BuildReport(record.SecurityId, "Security", evaluatedAt, issues);
    }

    public async Task<SecurityValidationReportDto> ValidateUniverseAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var universe = await _store.LoadAllAsync(ct).ConfigureAwait(false);
        var evaluatedAtUtc = DateTimeOffset.UtcNow;
        var issues = new List<SecurityValidationIssueDto>();

        foreach (var record in universe.OrderBy(static record => record.SecurityId))
        {
            ct.ThrowIfCancellationRequested();
            issues.AddRange(ValidateRecord(record, universe, evaluatedAtUtc).Issues);
            issues.AddRange(await ValidateOperatorOverridesAsync(record.SecurityId, ct).ConfigureAwait(false));
        }

        return BuildReport(null, "Universe", evaluatedAtUtc, issues);
    }

    public SecurityValidationReportDto ValidateRecord(
        SecurityProjectionRecord record,
        IReadOnlyList<SecurityProjectionRecord> universe,
        DateTimeOffset? evaluatedAtUtc = null)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(universe);

        var evaluatedAt = evaluatedAtUtc ?? DateTimeOffset.UtcNow;
        var issues = new List<SecurityValidationIssueDto>();

        issues.AddRange(ValidateCommonRecordShape(record, evaluatedAt));
        issues.AddRange(ValidateSchemaCompatibility(record));
        issues.AddRange(ValidateIdentifiers(record, universe, evaluatedAt));
        issues.AddRange(ValidateProvenance(record, evaluatedAt));
        issues.AddRange(ValidateValuationAndAccountingMetadata(record));

        if (_assetClassValidators.TryGetValidator(record.AssetClass, out var assetClassValidator))
        {
            issues.AddRange(assetClassValidator.Validate(new SecurityValidationContext(record, evaluatedAt)));
        }
        else
        {
            issues.Add(SecurityValidationIssueFactory.Create(
                SecurityValidationSeverityDto.Error,
                "SM_ASSET_CLASS_UNSUPPORTED",
                "Security asset class is unsupported",
                $"Asset class '{record.AssetClass}' is not registered in the Security Master validator registry.",
                ["assetClass"],
                $"Register an asset-class validator or map the record to one of: {string.Join(", ", _assetClassValidators.SupportedAssetClasses.Order())}."));
        }

        return BuildReport(record.SecurityId, "Security", evaluatedAt, issues);
    }

    private static IReadOnlyList<SecurityValidationIssueDto> ValidateCommonRecordShape(
        SecurityProjectionRecord record,
        DateTimeOffset evaluatedAtUtc)
    {
        var issues = new List<SecurityValidationIssueDto>();

        if (string.IsNullOrWhiteSpace(record.DisplayName))
        {
            issues.Add(SecurityValidationIssueFactory.Create(
                SecurityValidationSeverityDto.Error,
                "SM_DISPLAY_NAME_REQUIRED",
                "Display name is missing",
                "Security Master records must include a display name for operator review and report evidence.",
                ["displayName", "commonTerms.displayName"],
                "Populate the canonical display name from the winning source system."));
        }

        if (string.IsNullOrWhiteSpace(record.Currency))
        {
            issues.Add(SecurityValidationIssueFactory.Create(
                SecurityValidationSeverityDto.Error,
                "SM_CURRENCY_REQUIRED",
                "Currency is missing",
                "Security Master records must include a currency for valuation, ledger, and reporting workflows.",
                ["currency", "commonTerms.currency"],
                "Populate the instrument currency using the winning source system."));
        }

        if (record.EffectiveTo is { } effectiveTo && effectiveTo <= record.EffectiveFrom)
        {
            issues.Add(SecurityValidationIssueFactory.Create(
                SecurityValidationSeverityDto.Error,
                "SM_EFFECTIVE_WINDOW_INVALID",
                "Effective-date window is invalid",
                "Security Master EffectiveTo must be later than EffectiveFrom when present.",
                ["effectiveFrom", "effectiveTo"],
                "Correct the effective-dated record window before downstream workflows consume the record."));
        }

        if (record.Status == SecurityStatusDto.Active
            && record.EffectiveTo.HasValue
            && record.EffectiveTo.Value <= evaluatedAtUtc)
        {
            issues.Add(SecurityValidationIssueFactory.Create(
                SecurityValidationSeverityDto.Warning,
                "SM_ACTIVE_RECORD_EXPIRED",
                "Active record has already expired",
                "The record is marked Active even though its EffectiveTo timestamp has passed.",
                ["status", "effectiveTo"],
                "Deactivate the record or extend the effective window with an audited amendment."));
        }

        return issues;
    }

    private static IReadOnlyList<SecurityValidationIssueDto> ValidateSchemaCompatibility(SecurityProjectionRecord record)
    {
        var issues = new List<SecurityValidationIssueDto>();
        if (!TryGetJsonInt(record.AssetSpecificTerms, "schemaVersion", out var schemaVersion))
        {
            issues.Add(record.AssetSpecificTerms.ValueKind == JsonValueKind.Object
                && !record.AssetSpecificTerms.TryGetProperty("schemaVersion", out _)
                ? SecurityValidationIssueFactory.Create(
                    SecurityValidationSeverityDto.Warning,
                    "SM_ASSET_SPECIFIC_SCHEMA_VERSION_MISSING",
                    "Asset-specific schema version is missing",
                    "Asset-specific Security Master payloads should carry schemaVersion so compatibility adapters can distinguish legacy terms from newer payload families.",
                    ["assetSpecificTerms.schemaVersion"],
                    $"Stamp assetSpecificTerms.schemaVersion with {SecurityMasterSchemaVersions.LegacyAssetSpecificTerms} when persisting legacy asset-specific payloads.")
                : SecurityValidationIssueFactory.Create(
                    SecurityValidationSeverityDto.Error,
                    "SM_ASSET_SPECIFIC_SCHEMA_VERSION_INVALID",
                    "Asset-specific schema version is invalid",
                    "assetSpecificTerms.schemaVersion must be an integer so the workstation can apply compatibility-safe deserialization rules.",
                    ["assetSpecificTerms.schemaVersion"],
                    $"Persist assetSpecificTerms.schemaVersion as integer version {SecurityMasterSchemaVersions.LegacyAssetSpecificTerms}."));
            return issues;
        }

        var isProfileBackedRecord = IsProfileBackedRecord(record);
        var isSupportedSchemaVersion = SecurityMasterSchemaVersions.IsAcceptedAssetSpecificTermsVersion(
            schemaVersion, isProfileBackedRecord);
        if (!isSupportedSchemaVersion)
        {
            var supportedVersions = SecurityMasterSchemaVersions.DescribeAcceptedAssetSpecificTermsVersions(
                isProfileBackedRecord);
            issues.Add(SecurityValidationIssueFactory.Create(
                SecurityValidationSeverityDto.Error,
                "SM_ASSET_SPECIFIC_SCHEMA_VERSION_UNSUPPORTED",
                "Asset-specific schema version is unsupported",
                $"assetSpecificTerms.schemaVersion '{schemaVersion}' is not supported by the current Security Master compatibility adapter for asset class '{record.AssetClass}'.",
                ["assetSpecificTerms.schemaVersion", "assetClass"],
                $"Migrate the payload to schema version {supportedVersions} or extend the compatibility adapter before this record is used downstream."));
        }

        return issues;
    }

    private static bool IsProfileBackedRecord(SecurityProjectionRecord record)
        => string.Equals(record.AssetClass, "CustomAsset", StringComparison.OrdinalIgnoreCase)
           || (record.AssetSpecificTerms.ValueKind == JsonValueKind.Object
               && record.AssetSpecificTerms.TryGetProperty("customProfileId", out var customProfileId)
               && customProfileId.ValueKind == JsonValueKind.String
               && !string.IsNullOrWhiteSpace(customProfileId.GetString()));

    private static IReadOnlyList<SecurityValidationIssueDto> ValidateIdentifiers(
        SecurityProjectionRecord record,
        IReadOnlyList<SecurityProjectionRecord> universe,
        DateTimeOffset evaluatedAtUtc)
    {
        var issues = new List<SecurityValidationIssueDto>();
        foreach (var identifier in record.Identifiers)
        {
            if (identifier.ValidTo is { } validTo && validTo <= identifier.ValidFrom)
            {
                issues.Add(SecurityValidationIssueFactory.Create(
                    SecurityValidationSeverityDto.Error,
                    "SM_IDENTIFIER_EFFECTIVE_WINDOW_INVALID",
                    "Identifier effective-date window is invalid",
                    $"{identifier.Kind} '{identifier.Value}' expires on or before it becomes effective.",
                    [$"identifiers.{identifier.Kind}.validFrom", $"identifiers.{identifier.Kind}.validTo"],
                    "Correct the identifier effective-date range before downstream workflows consume the record."));
            }
        }

        var activeIdentifiers = record.Identifiers
            .Where(identifier => IsActive(identifier, evaluatedAtUtc))
            .ToArray();
        var activePrimaryIdentifiers = activeIdentifiers
            .Where(static identifier => identifier.IsPrimary)
            .ToArray();

        if (activeIdentifiers.Length == 0)
        {
            issues.Add(SecurityValidationIssueFactory.Create(
                SecurityValidationSeverityDto.Error,
                "SM_IDENTIFIER_MISSING",
                "Active identifier is missing",
                "Security Master records must have at least one active identifier for deterministic resolution.",
                ["identifiers"],
                "Attach a canonical identifier such as ISIN, CUSIP, FIGI, provider symbol, or InternalCode."));
        }

        var primaryCount = activePrimaryIdentifiers.Length;
        if (primaryCount == 0)
        {
            issues.Add(SecurityValidationIssueFactory.Create(
                SecurityValidationSeverityDto.Error,
                "SM_PRIMARY_IDENTIFIER_MISSING",
                "Primary identifier is missing",
                "Security Master records must have exactly one active primary identifier.",
                ["identifiers.isPrimary"],
                "Mark the deterministic canonical identifier as primary."));
        }
        else if (primaryCount > 1)
        {
            issues.Add(SecurityValidationIssueFactory.Create(
                SecurityValidationSeverityDto.Error,
                "SM_PRIMARY_IDENTIFIER_AMBIGUOUS",
                "Primary identifier is ambiguous",
                "Security Master records must not have more than one active primary identifier.",
                ["identifiers.isPrimary"],
                "Retain exactly one primary identifier and expire or demote the others."));
        }

        issues.AddRange(ValidatePrimaryIdentifierProjection(record, activePrimaryIdentifiers));

        // Provider only distinguishes ProviderSymbol identities; canonical identifiers (ISIN,
        // FIGI, RIC, ...) are provider-independent, so a repeated canonical value must collide
        // even when the duplicate rows carry different provider annotations. Keep this consistent
        // with cross-record duplicate detection below.
        var duplicateInRecord = activeIdentifiers
            .GroupBy(
                static identifier => IdentifierKey(
                    identifier,
                    includeProvider: identifier.Kind == SecurityIdentifierKind.ProviderSymbol),
                StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(static group => group.Count() > 1);
        if (duplicateInRecord is not null)
        {
            issues.Add(SecurityValidationIssueFactory.Create(
                SecurityValidationSeverityDto.Error,
                "SM_IDENTIFIER_DUPLICATE_ACTIVE",
                "Duplicate active identifier exists on the record",
                $"The record contains duplicate active identifier '{duplicateInRecord.Key}'.",
                ["identifiers"],
                "Expire duplicate identifiers or consolidate them into a single active identifier."));
        }

        foreach (var identifier in activeIdentifiers)
        {
            if (identifier.Kind == SecurityIdentifierKind.ProviderSymbol
                && string.IsNullOrWhiteSpace(identifier.Provider))
            {
                issues.Add(SecurityValidationIssueFactory.Create(
                    SecurityValidationSeverityDto.Error,
                    "SM_PROVIDER_SYMBOL_PROVIDER_REQUIRED",
                    "Provider symbol is missing its provider",
                    "ProviderSymbol identifiers must keep the raw provider namespace separate from canonical security identity.",
                    [$"identifiers.{identifier.Kind}"],
                    "Set the provider name on the ProviderSymbol identifier."));
            }

            if (identifier.Kind == SecurityIdentifierKind.OccOptionSymbol
                && !string.Equals(record.AssetClass, "Option", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(record.AssetClass, "Warrant", StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(SecurityValidationIssueFactory.Create(
                    SecurityValidationSeverityDto.Warning,
                    "SM_OCC_SYMBOL_ASSET_CLASS_MISMATCH",
                    "OCC option symbol is attached to a non-option security",
                    $"Identifier '{identifier.Value}' uses OCC option syntax but the record asset class is '{record.AssetClass}'.",
                    [$"identifiers.{identifier.Kind}", "assetClass"],
                    "Move the OCC symbol to the listed option or warrant contract, or replace it with the canonical identifier for this asset class."));
            }

            if (!SecurityIdentifierNormalizer.TryValidateFormat(identifier.Kind, identifier.Value, out var formatMessage))
            {
                issues.Add(SecurityValidationIssueFactory.Create(
                    SecurityValidationSeverityDto.Error,
                    "SM_IDENTIFIER_FORMAT_INVALID",
                    "Identifier format is invalid",
                    $"{identifier.Kind} '{identifier.Value}' is invalid. {formatMessage}",
                    [$"identifiers.{identifier.Kind}"],
                    "Correct the identifier value or move the raw vendor token into an alias/provider-mapping field."));
            }

            var normalizedValue = SecurityIdentifierNormalizer.GetOrComputeNormalizedValue(identifier);
            if (RequiresCanonicalNormalizationWarning(identifier.Kind)
                && !string.Equals(identifier.Value, normalizedValue, StringComparison.Ordinal))
            {
                issues.Add(SecurityValidationIssueFactory.Create(
                    SecurityValidationSeverityDto.Warning,
                    "SM_IDENTIFIER_NORMALIZATION_RECOMMENDED",
                    "Identifier should use its canonical normalized form",
                    $"{identifier.Kind} '{identifier.Value}' normalizes to '{normalizedValue}'.",
                    [$"identifiers.{identifier.Kind}"],
                    "Preserve the raw vendor string in provenance or aliases, but store the canonical comparable code on the active identifier."));
            }
        }

        issues.AddRange(ValidateCrossRecordDuplicates(record, universe, activeIdentifiers, evaluatedAtUtc));
        return issues;
    }

    private static IReadOnlyList<SecurityValidationIssueDto> ValidatePrimaryIdentifierProjection(
        SecurityProjectionRecord record,
        IReadOnlyList<SecurityIdentifierDto> activePrimaryIdentifiers)
    {
        var issues = new List<SecurityValidationIssueDto>();
        if (string.IsNullOrWhiteSpace(record.PrimaryIdentifierKind)
            || string.IsNullOrWhiteSpace(record.PrimaryIdentifierValue))
        {
            issues.Add(SecurityValidationIssueFactory.Create(
                SecurityValidationSeverityDto.Error,
                "SM_PRIMARY_IDENTIFIER_PROJECTION_MISSING",
                "Projection primary identifier is missing",
                "The denormalized Security Master projection must carry primary identifier kind and value so search, resolution, and UI summaries remain deterministic.",
                ["primaryIdentifierKind", "primaryIdentifierValue", "identifiers.isPrimary"],
                "Rebuild the projection from the winning active primary identifier before downstream workflows consume this record."));
            return issues;
        }

        if (activePrimaryIdentifiers.Count != 1)
        {
            return issues;
        }

        var activePrimaryIdentifier = activePrimaryIdentifiers[0];
        if (!Enum.TryParse<SecurityIdentifierKind>(record.PrimaryIdentifierKind, ignoreCase: true, out var projectionKind))
        {
            issues.Add(SecurityValidationIssueFactory.Create(
                SecurityValidationSeverityDto.Error,
                "SM_PRIMARY_IDENTIFIER_KIND_INVALID",
                "Projection primary identifier kind is invalid",
                $"Projection primary identifier kind '{record.PrimaryIdentifierKind}' is not a supported SecurityIdentifierKind.",
                ["primaryIdentifierKind"],
                "Rebuild the projection using a supported primary identifier kind from the active identifier set."));
            return issues;
        }

        var projectionNormalizedValue = SecurityIdentifierNormalizer.NormalizeValue(projectionKind, record.PrimaryIdentifierValue);
        var activeNormalizedValue = SecurityIdentifierNormalizer.GetOrComputeNormalizedValue(activePrimaryIdentifier);
        if (projectionKind != activePrimaryIdentifier.Kind
            || !projectionNormalizedValue.Equals(activeNormalizedValue, StringComparison.Ordinal))
        {
            issues.Add(SecurityValidationIssueFactory.Create(
                SecurityValidationSeverityDto.Error,
                "SM_PRIMARY_IDENTIFIER_PROJECTION_MISMATCH",
                "Projection primary identifier is out of sync",
                $"Projection primary identifier '{record.PrimaryIdentifierKind}:{record.PrimaryIdentifierValue}' does not match the active primary identifier '{activePrimaryIdentifier.Kind}:{activePrimaryIdentifier.Value}'.",
                ["primaryIdentifierKind", "primaryIdentifierValue", "identifiers.isPrimary"],
                "Rebuild the projection or correct the active primary identifier so identifier resolution and UI summaries stay aligned."));
        }

        return issues;
    }

    private static IReadOnlyList<SecurityValidationIssueDto> ValidateCrossRecordDuplicates(
        SecurityProjectionRecord record,
        IReadOnlyList<SecurityProjectionRecord> universe,
        IReadOnlyList<SecurityIdentifierDto> activeIdentifiers,
        DateTimeOffset evaluatedAtUtc)
    {
        var issues = new List<SecurityValidationIssueDto>();
        foreach (var identifier in activeIdentifiers.OrderBy(static id => id.Kind).ThenBy(static id => id.Value, StringComparer.OrdinalIgnoreCase))
        {
            var isProviderSymbol = identifier.Kind == SecurityIdentifierKind.ProviderSymbol;
            var isCanonical = CanonicalDuplicateIdentifierKinds.Contains(identifier.Kind);
            if (!isProviderSymbol && !isCanonical)
            {
                continue;
            }

            var includeProvider = isProviderSymbol;
            var key = IdentifierKey(identifier, includeProvider);
            var conflicts = universe
                .Where(candidate => candidate.SecurityId != record.SecurityId)
                .Where(candidate => candidate.Identifiers.Any(other =>
                    IsActive(other, evaluatedAtUtc)
                    && IdentifierKey(other, includeProvider).Equals(key, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(static candidate => candidate.SecurityId)
                .ToArray();

            if (conflicts.Length == 0)
            {
                continue;
            }

            var evidenceLinks = conflicts
                .Select(conflict => new SecurityEvidenceLinkDto(
                    EvidenceKind: "SecurityMasterRecord",
                    EvidenceId: conflict.SecurityId.ToString(),
                    Route: UiApiRoutes.SecurityMasterById.Replace("{securityId:guid}", conflict.SecurityId.ToString(), StringComparison.Ordinal),
                    Summary: $"{conflict.DisplayName} ({conflict.AssetClass})"))
                .ToArray();

            issues.Add(SecurityValidationIssueFactory.Create(
                isProviderSymbol ? SecurityValidationSeverityDto.Error : SecurityValidationSeverityDto.Critical,
                isProviderSymbol ? "SM_PROVIDER_SYMBOL_CONFLICT" : "SM_DUPLICATE_CANONICAL_IDENTIFIER",
                isProviderSymbol ? "Provider symbol maps to multiple securities" : "Canonical identifier maps to multiple securities",
                isProviderSymbol
                    ? $"Provider symbol '{identifier.Value}' for provider '{identifier.Provider}' is assigned to more than one SecurityId."
                    : $"{identifier.Kind} '{identifier.Value}' is assigned to more than one SecurityId.",
                [$"identifiers.{identifier.Kind}"],
                isProviderSymbol
                    ? "Resolve the provider symbol mapping before reconciliation, execution, or lot import consumes this identifier."
                    : "Merge duplicate instruments or expire the losing identifier before downstream workflows consume this record.",
                evidenceLinks));
        }

        return issues;
    }

    private static IReadOnlyList<SecurityValidationIssueDto> ValidateProvenance(
        SecurityProjectionRecord record,
        DateTimeOffset evaluatedAtUtc)
    {
        var issues = new List<SecurityValidationIssueDto>();

        var sourceSystem = TryGetJsonString(record.Provenance, "sourceSystem");
        if (string.IsNullOrWhiteSpace(sourceSystem))
        {
            issues.Add(SecurityValidationIssueFactory.Create(
                SecurityValidationSeverityDto.Error,
                "SM_PROVENANCE_SOURCE_REQUIRED",
                "Source system is missing",
                "Security Master records must identify the source system that supplied the winning terms.",
                ["provenance.sourceSystem"],
                "Populate provenance.sourceSystem from the source adapter or operator override workflow."));
        }

        if (string.IsNullOrWhiteSpace(TryGetJsonString(record.Provenance, "updatedBy")))
        {
            issues.Add(SecurityValidationIssueFactory.Create(
                SecurityValidationSeverityDto.Error,
                "SM_PROVENANCE_UPDATED_BY_REQUIRED",
                "Provenance actor is missing",
                "Security Master records must identify the actor or service that last updated the winning terms.",
                ["provenance.updatedBy"],
                "Populate provenance.updatedBy from the source adapter or operator override workflow."));
        }

        if (!TryGetJsonDateTimeOffset(record.Provenance, "asOf", out var asOf))
        {
            issues.Add(SecurityValidationIssueFactory.Create(
                SecurityValidationSeverityDto.Error,
                "SM_PROVENANCE_ASOF_REQUIRED",
                "Provenance as-of timestamp is missing",
                "Security Master records must include the source as-of timestamp used to judge stale reference data.",
                ["provenance.asOf"],
                "Populate provenance.asOf with the source data timestamp."));
        }
        else if (evaluatedAtUtc - asOf > StaleReferenceDataAge)
        {
            issues.Add(SecurityValidationIssueFactory.Create(
                SecurityValidationSeverityDto.Warning,
                "SM_REFERENCE_DATA_STALE",
                "Reference data is stale",
                $"The winning reference data is older than {StaleReferenceDataAge.TotalDays:0} days.",
                ["provenance.asOf"],
                "Refresh the record from the winning source system or record an audited exception."));
        }

        return issues;
    }

    private static IReadOnlyList<SecurityValidationIssueDto> ValidateValuationAndAccountingMetadata(SecurityProjectionRecord record)
    {
        var issues = new List<SecurityValidationIssueDto>();

        if (!HasPricingSource(record))
        {
            issues.Add(SecurityValidationIssueFactory.Create(
                SecurityValidationSeverityDto.Warning,
                "SM_PRICING_SOURCE_MISSING",
                "Pricing source is missing",
                "The record does not identify a pricing or valuation source for marks, valuation, and report-pack evidence.",
                ["commonTerms.pricingSource", "assetSpecificTerms.pricingSource", "assetSpecificTerms.valuationProfile.pricingSource"],
                "Attach a pricing source or valuation profile before using the record for governed valuation outputs."));
        }

        if (!HasAccountingClassification(record))
        {
            issues.Add(SecurityValidationIssueFactory.Create(
                SecurityValidationSeverityDto.Warning,
                "SM_ACCOUNTING_CLASSIFICATION_MISSING",
                "Accounting classification is missing",
                "The record does not expose an accounting classification for ledger posting and report grouping.",
                ["commonTerms.accountingClassification", "assetSpecificTerms.accountingClassification", "assetSpecificTerms.classification"],
                "Attach the ledger/reporting accounting classification before journal generation relies on the record."));
        }

        return issues;
    }

    private async Task<IReadOnlyList<SecurityValidationIssueDto>> ValidateOperatorOverridesAsync(
        Guid securityId,
        CancellationToken ct)
    {
        if (_operatorOverridesStore is null)
        {
            return [];
        }

        var overrides = await _operatorOverridesStore.GetAsync(securityId, ct).ConfigureAwait(false);
        if (overrides is null || overrides.Values.Count == 0)
        {
            return [];
        }

        var issues = new List<SecurityValidationIssueDto>();
        if (overrides.ApprovalStatus != SecurityOverrideApprovalStatusDto.Approved)
        {
            issues.Add(SecurityValidationIssueFactory.Create(
                SecurityValidationSeverityDto.Error,
                "SM_OVERRIDE_APPROVAL_REQUIRED",
                "Operator override requires approval",
                "The record has operator override values that are not approved for governed run, ledger, reconciliation, or report-pack use.",
                ["operatorOverrides", "operatorOverrides.approvalStatus"],
                "Route the override through reviewer approval or remove the override before governed workflows consume the record.",
                [
                    new SecurityEvidenceLinkDto(
                        EvidenceKind: "SecurityOperatorOverride",
                        EvidenceId: securityId.ToString(),
                        Route: UiApiRoutes.SecurityMasterOperatorOverrides.Replace("{securityId:guid}", securityId.ToString(), StringComparison.Ordinal),
                        Summary: $"Override approval status is {overrides.ApprovalStatus}.")
                ]));
        }

        if (overrides.ApprovalStatus == SecurityOverrideApprovalStatusDto.Approved
            && (string.IsNullOrWhiteSpace(overrides.ReviewedBy) || overrides.ReviewedAt is null))
        {
            issues.Add(SecurityValidationIssueFactory.Create(
                SecurityValidationSeverityDto.Error,
                "SM_OVERRIDE_SIGNOFF_INCOMPLETE",
                "Operator override sign-off is incomplete",
                "Approved operator overrides must include reviewer identity and reviewed-at timestamp for audit reconstruction.",
                ["operatorOverrides.reviewedBy", "operatorOverrides.reviewedAt"],
                "Capture reviewer sign-off metadata before using this override in governed workflows."));
        }

        if (overrides.AuditTrail.Count == 0)
        {
            issues.Add(SecurityValidationIssueFactory.Create(
                SecurityValidationSeverityDto.Warning,
                "SM_OVERRIDE_AUDIT_TRAIL_MISSING",
                "Operator override audit trail is missing",
                "The override row does not expose immutable audit events for its current values.",
                ["operatorOverrides.auditTrail"],
                "Backfill or migrate override audit events so operator review can reconstruct who changed and approved the values."));
        }

        return issues;
    }

    private static bool HasPricingSource(SecurityProjectionRecord record)
        => HasNonBlankString(record.CommonTerms, "pricingSource")
           || HasNonBlankString(record.AssetSpecificTerms, "pricingSource")
           || HasNestedNonBlankString(record.CommonTerms, "valuationProfile", "pricingSource")
           || HasNestedNonBlankString(record.AssetSpecificTerms, "valuationProfile", "pricingSource");

    private static bool HasAccountingClassification(SecurityProjectionRecord record)
        => HasNonBlankString(record.CommonTerms, "accountingClassification")
           || HasNonBlankString(record.AssetSpecificTerms, "accountingClassification")
           || HasNonBlankString(record.AssetSpecificTerms, "classification")
           || HasNestedNonBlankString(record.AssetSpecificTerms, "accountingClassification", "ledgerCategory")
           || HasNestedNonBlankString(record.CommonTerms, "accountingClassification", "ledgerCategory");

    private static bool HasNonBlankString(JsonElement element, string propertyName)
        => element.ValueKind == JsonValueKind.Object
           && element.TryGetProperty(propertyName, out var property)
           && property.ValueKind == JsonValueKind.String
           && !string.IsNullOrWhiteSpace(property.GetString());

    private static bool HasNestedNonBlankString(JsonElement element, string objectName, string propertyName)
        => element.ValueKind == JsonValueKind.Object
           && element.TryGetProperty(objectName, out var nested)
           && nested.ValueKind == JsonValueKind.Object
           && HasNonBlankString(nested, propertyName);

    private static string? TryGetJsonString(JsonElement element, string propertyName)
        => element.ValueKind == JsonValueKind.Object
           && element.TryGetProperty(propertyName, out var property)
           && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static bool TryGetJsonDateTimeOffset(JsonElement element, string propertyName, out DateTimeOffset value)
    {
        value = default;
        return element.ValueKind == JsonValueKind.Object
               && element.TryGetProperty(propertyName, out var property)
               && property.ValueKind == JsonValueKind.String
               && DateTimeOffset.TryParse(property.GetString(), out value);
    }

    private static bool TryGetJsonInt(JsonElement element, string propertyName, out int value)
    {
        value = default;
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var property))
        {
            return false;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out value))
        {
            return true;
        }

        return property.ValueKind == JsonValueKind.String && int.TryParse(property.GetString(), out value);
    }

    private static bool IsActive(SecurityIdentifierDto identifier, DateTimeOffset asOf)
        => identifier.ValidFrom <= asOf
           && (!identifier.ValidTo.HasValue || identifier.ValidTo.Value > asOf);

    private static string IdentifierKey(SecurityIdentifierDto identifier, bool includeProvider)
    {
        var normalizedValue = SecurityIdentifierNormalizer.GetOrComputeNormalizedValue(identifier);
        if (!includeProvider)
        {
            return $"{identifier.Kind}|{normalizedValue}";
        }

        return $"{identifier.Kind}|{normalizedValue}|{SecurityIdentifierNormalizer.GetOrComputeNormalizedProvider(identifier)}";
    }

    private static bool RequiresCanonicalNormalizationWarning(SecurityIdentifierKind kind)
        => kind is SecurityIdentifierKind.Isin
            or SecurityIdentifierKind.Cusip
            or SecurityIdentifierKind.Sedol
            or SecurityIdentifierKind.OccOptionSymbol
            or SecurityIdentifierKind.Lei
            or SecurityIdentifierKind.Wkn
            or SecurityIdentifierKind.Valoren
            or SecurityIdentifierKind.Cik;

    private static SecurityValidationReportDto BuildReport(
        Guid? securityId,
        string scope,
        DateTimeOffset evaluatedAtUtc,
        IReadOnlyList<SecurityValidationIssueDto> issues)
    {
        var orderedIssues = issues
            .OrderByDescending(static issue => issue.Severity)
            .ThenBy(static issue => issue.Code, StringComparer.Ordinal)
            .ThenBy(static issue => issue.Title, StringComparer.Ordinal)
            .ToArray();

        var criticalCount = orderedIssues.Count(static issue => issue.Severity == SecurityValidationSeverityDto.Critical);
        var errorCount = orderedIssues.Count(static issue => issue.Severity == SecurityValidationSeverityDto.Error);

        return new SecurityValidationReportDto(
            SecurityId: securityId,
            Scope: scope,
            EvaluatedAtUtc: evaluatedAtUtc,
            HasBlockingIssues: criticalCount + errorCount > 0,
            CriticalIssueCount: criticalCount,
            ErrorIssueCount: errorCount,
            Issues: orderedIssues);
    }
}

public sealed class NullSecurityValidationService : ISecurityValidationService
{
    public Task<SecurityValidationReportDto> ValidateSecurityAsync(Guid securityId, CancellationToken ct = default)
        => Task.FromResult(BuildUnavailableReport(securityId));

    public Task<SecurityValidationReportDto> ValidateUniverseAsync(CancellationToken ct = default)
        => Task.FromResult(BuildUnavailableReport(null));

    public SecurityValidationReportDto ValidateRecord(
        SecurityProjectionRecord record,
        IReadOnlyList<SecurityProjectionRecord> universe,
        DateTimeOffset? evaluatedAtUtc = null)
        => BuildUnavailableReport(record.SecurityId, evaluatedAtUtc);

    private static SecurityValidationReportDto BuildUnavailableReport(Guid? securityId, DateTimeOffset? evaluatedAtUtc = null)
    {
        var evaluatedAt = evaluatedAtUtc ?? DateTimeOffset.UtcNow;
        return new SecurityValidationReportDto(
            SecurityId: securityId,
            Scope: securityId.HasValue ? "Security" : "Universe",
            EvaluatedAtUtc: evaluatedAt,
            HasBlockingIssues: true,
            CriticalIssueCount: 1,
            ErrorIssueCount: 0,
            Issues:
            [
                SecurityValidationIssueFactory.Create(
                    SecurityValidationSeverityDto.Critical,
                    "SM_SECURITY_MASTER_UNCONFIGURED",
                    "Security Master is not configured",
                    "Security Master validation requires the runtime-backed Security Master store.",
                    ["securityMaster"],
                    "Set MERIDIAN_SECURITY_MASTER_CONNECTION_STRING and rerun validation.")
            ]);
    }
}
