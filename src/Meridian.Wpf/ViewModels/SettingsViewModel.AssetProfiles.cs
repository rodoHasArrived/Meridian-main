using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.Contracts.SecurityMaster;
using Meridian.Ui.Services;
using static Meridian.Contracts.Text.TextPrimitives;

namespace Meridian.Wpf.ViewModels;

public sealed partial class SettingsViewModel
{
    private const string AssetProfileSourceSystem = "Meridian.Settings.AssetProfiles";

    private bool _isAssetProfileBusy;
    private SettingsAssetProfileRow? _selectedAssetProfileRow;
    private SecurityAssetProfileGovernanceResultDto? _lastAssetProfileGovernanceResult;
    private string _assetProfileStatusText = "Asset profiles not loaded.";
    private string _assetProfileLineageSummaryText = "Lineage not loaded.";
    private string _assetProfileDraftProfileId = string.Empty;
    private string _assetProfileDraftName = string.Empty;
    private string _assetProfileDraftCategory = string.Empty;
    private string _assetProfileDraftSubType = string.Empty;
    private string _assetProfileDraftRationale = "Stage governed custom asset profile for Security Master approval.";
    private string _assetProfileRollbackVersion = "1";
    private string _profileBackedSecurityDisplayName = string.Empty;
    private string _profileBackedSecurityInternalCode = string.Empty;
    private string _profileBackedSecurityCurrency = "USD";
    private string _profileBackedSecurityRationale = "Create profile-backed custom asset with approved Security Master profile version.";

    public bool IsAssetProfileBusy
    {
        get => _isAssetProfileBusy;
        private set
        {
            if (SetProperty(ref _isAssetProfileBusy, value))
            {
                UpdateAssetProfileCommandStates();
            }
        }
    }

    public string AssetProfileStatusText
    {
        get => _assetProfileStatusText;
        private set => SetProperty(ref _assetProfileStatusText, value);
    }

    public string AssetProfileLineageSummaryText
    {
        get => _assetProfileLineageSummaryText;
        private set => SetProperty(ref _assetProfileLineageSummaryText, value);
    }

    public SettingsAssetProfileRow? SelectedAssetProfileRow
    {
        get => _selectedAssetProfileRow;
        set
        {
            if (SetProperty(ref _selectedAssetProfileRow, value))
            {
                ApplySelectedAssetProfile(value);
            }
        }
    }

    public string AssetProfileDraftProfileId
    {
        get => _assetProfileDraftProfileId;
        set
        {
            if (SetProperty(ref _assetProfileDraftProfileId, value))
            {
                UpdateAssetProfileCommandStates();
            }
        }
    }

    public string AssetProfileDraftName
    {
        get => _assetProfileDraftName;
        set
        {
            if (SetProperty(ref _assetProfileDraftName, value))
            {
                UpdateAssetProfileCommandStates();
            }
        }
    }

    public string AssetProfileDraftCategory
    {
        get => _assetProfileDraftCategory;
        set
        {
            if (SetProperty(ref _assetProfileDraftCategory, value))
            {
                UpdateAssetProfileCommandStates();
            }
        }
    }

    public string AssetProfileDraftSubType
    {
        get => _assetProfileDraftSubType;
        set => SetProperty(ref _assetProfileDraftSubType, value);
    }

    public string AssetProfileDraftRationale
    {
        get => _assetProfileDraftRationale;
        set
        {
            if (SetProperty(ref _assetProfileDraftRationale, value))
            {
                UpdateAssetProfileCommandStates();
            }
        }
    }

    public string AssetProfileRollbackVersion
    {
        get => _assetProfileRollbackVersion;
        set
        {
            if (SetProperty(ref _assetProfileRollbackVersion, value))
            {
                UpdateAssetProfileCommandStates();
            }
        }
    }

    public string ProfileBackedSecurityDisplayName
    {
        get => _profileBackedSecurityDisplayName;
        set
        {
            if (SetProperty(ref _profileBackedSecurityDisplayName, value))
            {
                UpdateAssetProfileCommandStates();
            }
        }
    }

    public string ProfileBackedSecurityInternalCode
    {
        get => _profileBackedSecurityInternalCode;
        set
        {
            if (SetProperty(ref _profileBackedSecurityInternalCode, value))
            {
                UpdateAssetProfileCommandStates();
            }
        }
    }

    public string ProfileBackedSecurityCurrency
    {
        get => _profileBackedSecurityCurrency;
        set
        {
            var normalized = string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : value.Trim().ToUpperInvariant();
            if (SetProperty(ref _profileBackedSecurityCurrency, normalized))
            {
                UpdateAssetProfileCommandStates();
            }
        }
    }

    public string ProfileBackedSecurityRationale
    {
        get => _profileBackedSecurityRationale;
        set
        {
            if (SetProperty(ref _profileBackedSecurityRationale, value))
            {
                UpdateAssetProfileCommandStates();
            }
        }
    }

    public int ApprovedAssetProfileCount =>
        AssetProfileRows.Count(static row => row.Profile.Status == SecurityAssetProfileStatusDto.Approved);

    public bool HasAssetProfileRows => AssetProfileRows.Count > 0;

    public bool HasSelectedAssetProfile => SelectedAssetProfileRow is not null;

    public int AssetProfileProjectedFieldCount => SelectedAssetProfileRow?.ProjectedFieldCount ?? 0;

    public bool HasAssetProfileLineageEvents => AssetProfileLineageEvents.Count > 0;

    public string SelectedAssetProfileSummaryText => SelectedAssetProfileRow is { } row
        ? $"{row.Name} {row.VersionLabel} - {row.CategoryLabel} - {row.FieldCountLabel}"
        : "Select an approved starter profile to draft variants or create profile-backed securities.";

    private async Task RefreshAssetProfilesAsync()
    {
        if (_assetProfileClient is null)
        {
            AssetProfileStatusText = "Asset profile workflow is unavailable in this desktop session.";
            return;
        }

        IsAssetProfileBusy = true;
        try
        {
            var profiles = await _assetProfileClient.GetProfilesAsync().ConfigureAwait(true);
            var previousProfileId = SelectedAssetProfileRow?.ProfileId;
            var rows = profiles
                .OrderBy(static profile => profile.Status == SecurityAssetProfileStatusDto.Approved ? 0 : 1)
                .ThenBy(static profile => profile.Name, StringComparer.OrdinalIgnoreCase)
                .ThenByDescending(static profile => profile.Version)
                .Select(static profile => new SettingsAssetProfileRow(profile))
                .ToList();

            AssetProfileRows.Clear();
            foreach (var row in rows)
            {
                AssetProfileRows.Add(row);
            }

            var selected = rows.FirstOrDefault(row =>
                    string.Equals(row.ProfileId, previousProfileId, StringComparison.OrdinalIgnoreCase)
                    && row.Profile.Status == SecurityAssetProfileStatusDto.Approved)
                ?? rows.FirstOrDefault(static row => row.Profile.Status == SecurityAssetProfileStatusDto.Approved)
                ?? rows.FirstOrDefault();

            if (selected is not null)
            {
                SelectedAssetProfileRow = selected;
            }
            else
            {
                SelectedAssetProfileRow = null;
                ApplySelectedAssetProfile(null);
            }

            AssetProfileStatusText = ApprovedAssetProfileCount == 0
                ? "No approved asset profiles are available from the shared Security Master catalog."
                : $"Loaded {ApprovedAssetProfileCount} approved asset profile(s).";
            UpdateAssetProfileDerivedProperties();
        }
        catch (OperationCanceledException)
        {
            AssetProfileStatusText = "Asset profile refresh cancelled.";
        }
        catch (Exception ex)
        {
            AssetProfileStatusText = $"Asset profile refresh failed: {ex.Message}";
            _notificationService.ShowNotification("Asset Profile Refresh Failed", ex.Message, NotificationType.Error);
        }
        finally
        {
            IsAssetProfileBusy = false;
        }
    }

    private async Task DraftAssetProfileAsync()
    {
        if (_assetProfileClient is null || SelectedAssetProfileRow is null)
        {
            AssetProfileStatusText = "Select an approved starter profile before drafting.";
            return;
        }

        if (!CanDraftAssetProfile())
        {
            AssetProfileStatusText = "Profile id, name, category, rationale, and a starter profile are required.";
            return;
        }

        IsAssetProfileBusy = true;
        try
        {
            var starter = SelectedAssetProfileRow.Profile;
            var request = new SecurityAssetProfileDraftRequestDto(
                NormalizeAssetProfileId(AssetProfileDraftProfileId),
                AssetProfileDraftName.Trim(),
                AssetProfileDraftCategory.Trim(),
                NormalizeOptional(AssetProfileDraftSubType),
                starter.Fields,
                starter.IdentifierPreferences,
                starter.LifecycleStates,
                starter.AccountingImpactHints,
                starter.DateOrderRules,
                GetRequestedBy(),
                AssetProfileDraftRationale.Trim(),
                BuildCorrelationId("draft"));

            var response = await _assetProfileClient.DraftProfileAsync(request).ConfigureAwait(true);
            if (!response.Success || response.Data is null)
            {
                AssetProfileStatusText = FormatApiError(response, "Asset profile draft failed.");
                _notificationService.ShowNotification("Asset Profile Draft Failed", AssetProfileStatusText, NotificationType.Error);
                return;
            }

            _lastAssetProfileGovernanceResult = response.Data;
            AssetProfileDraftProfileId = response.Data.Profile.ProfileId;
            AssetProfileRollbackVersion = response.Data.Profile.Version.ToString(CultureInfo.InvariantCulture);
            AssetProfileLineageSummaryText = FormatLineageSummary(response.Data.Lineage);
            ReplaceLineageEvents(response.Data.Lineage);
            AssetProfileStatusText = $"Drafted {response.Data.Profile.Name} v{response.Data.Profile.Version}.";
            _notificationService.ShowNotification("Asset Profile Drafted", AssetProfileStatusText, NotificationType.Success);
        }
        finally
        {
            IsAssetProfileBusy = false;
        }
    }

    private async Task ApproveAssetProfileAsync()
    {
        if (_assetProfileClient is null)
        {
            AssetProfileStatusText = "Asset profile workflow is unavailable in this desktop session.";
            return;
        }

        if (!TryGetGovernanceProfileVersion(out var version))
        {
            AssetProfileStatusText = "Enter a valid profile version before approval.";
            return;
        }

        var profileId = ResolveGovernanceProfileId();
        if (string.IsNullOrWhiteSpace(profileId))
        {
            AssetProfileStatusText = "Enter a profile id before approval.";
            return;
        }

        IsAssetProfileBusy = true;
        try
        {
            var request = new SecurityAssetProfileApprovalRequestDto(
                NormalizeAssetProfileId(profileId),
                version,
                DateOnly.FromDateTime(DateTime.UtcNow.Date),
                $"wpf-settings:{NormalizeAssetProfileId(profileId)}:v{version}",
                GetRequestedBy(),
                AssetProfileDraftRationale.Trim(),
                BuildCorrelationId("approve"));

            var response = await _assetProfileClient.ApproveProfileAsync(request).ConfigureAwait(true);
            if (!response.Success || response.Data is null)
            {
                AssetProfileStatusText = FormatApiError(response, "Asset profile approval failed.");
                _notificationService.ShowNotification("Asset Profile Approval Failed", AssetProfileStatusText, NotificationType.Error);
                return;
            }

            _lastAssetProfileGovernanceResult = response.Data;
            AssetProfileLineageSummaryText = FormatLineageSummary(response.Data.Lineage);
            ReplaceLineageEvents(response.Data.Lineage);
            AssetProfileStatusText = $"Approved {response.Data.Profile.Name} v{response.Data.Profile.Version}.";
            _notificationService.ShowNotification("Asset Profile Approved", AssetProfileStatusText, NotificationType.Success);
            await RefreshAssetProfilesAsync().ConfigureAwait(true);
        }
        finally
        {
            IsAssetProfileBusy = false;
        }
    }

    private async Task LoadAssetProfileLineageAsync()
    {
        if (_assetProfileClient is null)
        {
            AssetProfileStatusText = "Asset profile workflow is unavailable in this desktop session.";
            return;
        }

        var profileId = ResolveGovernanceProfileId();
        if (string.IsNullOrWhiteSpace(profileId))
        {
            AssetProfileStatusText = "Select or enter a profile id before loading lineage.";
            return;
        }

        IsAssetProfileBusy = true;
        try
        {
            var lineage = await _assetProfileClient.GetLineageAsync(NormalizeAssetProfileId(profileId)).ConfigureAwait(true);
            if (lineage is null)
            {
                AssetProfileLineageSummaryText = "Lineage unavailable.";
                AssetProfileStatusText = "Asset profile lineage was not returned by the shared API.";
                ReplaceLineageEvents(null);
                return;
            }

            AssetProfileLineageSummaryText = FormatLineageSummary(lineage);
            ReplaceLineageEvents(lineage);
            AssetProfileStatusText = $"Loaded lineage for {lineage.ProfileId}.";
        }
        finally
        {
            IsAssetProfileBusy = false;
        }
    }

    private async Task RollbackAssetProfileAsync()
    {
        if (_assetProfileClient is null)
        {
            AssetProfileStatusText = "Asset profile workflow is unavailable in this desktop session.";
            return;
        }

        var profileId = ResolveGovernanceProfileId();
        if (string.IsNullOrWhiteSpace(profileId) || !int.TryParse(AssetProfileRollbackVersion, NumberStyles.Integer, CultureInfo.InvariantCulture, out var targetVersion) || targetVersion <= 0)
        {
            AssetProfileStatusText = "Profile id and a positive rollback version are required.";
            return;
        }

        IsAssetProfileBusy = true;
        try
        {
            var normalizedProfileId = NormalizeAssetProfileId(profileId);
            var request = new SecurityAssetProfileRollbackRequestDto(
                normalizedProfileId,
                targetVersion,
                DateOnly.FromDateTime(DateTime.UtcNow.Date),
                $"wpf-settings:{normalizedProfileId}:rollback:v{targetVersion}",
                GetRequestedBy(),
                AssetProfileDraftRationale.Trim(),
                BuildCorrelationId("rollback"));

            var response = await _assetProfileClient.RollbackProfileAsync(request).ConfigureAwait(true);
            if (!response.Success || response.Data is null)
            {
                AssetProfileStatusText = FormatApiError(response, "Asset profile rollback failed.");
                _notificationService.ShowNotification("Asset Profile Rollback Failed", AssetProfileStatusText, NotificationType.Error);
                return;
            }

            _lastAssetProfileGovernanceResult = response.Data;
            AssetProfileLineageSummaryText = FormatLineageSummary(response.Data.Lineage);
            ReplaceLineageEvents(response.Data.Lineage);
            AssetProfileStatusText = $"Rolled {response.Data.Profile.Name} back to v{response.Data.Profile.Version}.";
            _notificationService.ShowNotification("Asset Profile Rolled Back", AssetProfileStatusText, NotificationType.Success);
            await RefreshAssetProfilesAsync().ConfigureAwait(true);
        }
        finally
        {
            IsAssetProfileBusy = false;
        }
    }

    private async Task CreateProfileBackedSecurityAsync()
    {
        if (_assetProfileClient is null || SelectedAssetProfileRow is null)
        {
            AssetProfileStatusText = "Select an approved profile before creating a custom asset.";
            return;
        }

        var selected = SelectedAssetProfileRow.Profile;
        if (selected.Status != SecurityAssetProfileStatusDto.Approved)
        {
            AssetProfileStatusText = "Only approved asset profile versions can back new custom assets.";
            return;
        }

        var missingRequiredFields = AssetProfileFieldInputs
            .Where(static input => input.IsRequired && string.IsNullOrWhiteSpace(input.Value))
            .Select(static input => input.Label)
            .ToArray();

        if (string.IsNullOrWhiteSpace(ProfileBackedSecurityDisplayName)
            || string.IsNullOrWhiteSpace(ProfileBackedSecurityInternalCode)
            || missingRequiredFields.Length > 0)
        {
            AssetProfileStatusText = missingRequiredFields.Length == 0
                ? "Display name and internal code are required."
                : $"Required profile fields are missing: {string.Join(", ", missingRequiredFields)}.";
            return;
        }

        if (!TryBuildProfileFields(out var profileFields, out var validationMessage))
        {
            AssetProfileStatusText = validationMessage ?? "Profile fields are invalid.";
            return;
        }

        IsAssetProfileBusy = true;
        try
        {
            var effectiveFrom = DateTimeOffset.UtcNow;
            var securityId = Guid.NewGuid();
            var commonTerms = JsonSerializer.SerializeToElement(new
            {
                displayName = ProfileBackedSecurityDisplayName.Trim(),
                currency = NormalizeCurrency(ProfileBackedSecurityCurrency)
            });
            var assetSpecificTerms = JsonSerializer.SerializeToElement(new
            {
                schemaVersion = SecurityMasterSchemaVersions.CustomAssetProfileTerms,
                category = selected.Category,
                subType = selected.SubType,
                customProfileId = selected.ProfileId,
                profileVersion = selected.Version,
                profileFields,
                profileApproval = new
                {
                    approvedBy = selected.ApprovedBy,
                    approvedAtUtc = selected.ApprovedAtUtc,
                    approvalReference = $"profile:{selected.ProfileId}:v{selected.Version}"
                },
                evidenceLinks = Array.Empty<SecurityEvidenceLinkDto>()
            });
            var request = new CreateSecurityRequest(
                securityId,
                "CustomAsset",
                commonTerms,
                assetSpecificTerms,
                [
                    new SecurityIdentifierDto(
                        SecurityIdentifierKind.InternalCode,
                        ProfileBackedSecurityInternalCode.Trim(),
                        true,
                        effectiveFrom)
                ],
                effectiveFrom,
                AssetProfileSourceSystem,
                GetRequestedBy(),
                $"asset-profile:{selected.ProfileId}:v{selected.Version}",
                ProfileBackedSecurityRationale.Trim());

            var response = await _assetProfileClient.CreateSecurityAsync(request).ConfigureAwait(true);
            if (!response.Success || response.Data is null)
            {
                AssetProfileStatusText = FormatApiError(response, "Profile-backed security creation failed.");
                _notificationService.ShowNotification("Custom Asset Creation Failed", AssetProfileStatusText, NotificationType.Error);
                return;
            }

            AssetProfileStatusText = $"Security created for {response.Data.DisplayName}.";
            _notificationService.ShowNotification("Custom Asset Created", AssetProfileStatusText, NotificationType.Success);
        }
        finally
        {
            IsAssetProfileBusy = false;
        }
    }

    private void ApplySelectedAssetProfile(SettingsAssetProfileRow? row)
    {
        AssetProfileFieldInputs.Clear();
        _lastAssetProfileGovernanceResult = null;

        if (row is null)
        {
            AssetProfileDraftProfileId = string.Empty;
            AssetProfileDraftName = string.Empty;
            AssetProfileDraftCategory = string.Empty;
            AssetProfileDraftSubType = string.Empty;
            AssetProfileLineageSummaryText = "Lineage not loaded.";
            ReplaceLineageEvents(null);
            UpdateAssetProfileDerivedProperties();
            return;
        }

        AssetProfileDraftProfileId = row.ProfileId;
        AssetProfileDraftName = row.Name;
        AssetProfileDraftCategory = row.Profile.Category;
        AssetProfileDraftSubType = row.Profile.SubType ?? string.Empty;
        AssetProfileRollbackVersion = row.Profile.Version.ToString(CultureInfo.InvariantCulture);
        AssetProfileLineageSummaryText = "Lineage not loaded.";
        ReplaceLineageEvents(null);

        if (string.IsNullOrWhiteSpace(ProfileBackedSecurityDisplayName))
        {
            ProfileBackedSecurityDisplayName = $"{row.Name} custom asset";
        }

        if (string.IsNullOrWhiteSpace(ProfileBackedSecurityInternalCode))
        {
            ProfileBackedSecurityInternalCode = $"{row.ProfileId.ToUpperInvariant()}-001";
        }

        foreach (var field in row.Profile.Fields.OrderBy(static field => field.Label, StringComparer.OrdinalIgnoreCase))
        {
            AssetProfileFieldInputs.Add(new SettingsAssetProfileFieldInput(field, UpdateAssetProfileCommandStates));
        }

        UpdateAssetProfileDerivedProperties();
    }

    private bool CanUseAssetProfileWorkflow() => _assetProfileClient is not null && !IsAssetProfileBusy;

    private bool CanDraftAssetProfile()
        => CanUseAssetProfileWorkflow()
           && SelectedAssetProfileRow is not null
           && !string.IsNullOrWhiteSpace(AssetProfileDraftProfileId)
           && !string.IsNullOrWhiteSpace(AssetProfileDraftName)
           && !string.IsNullOrWhiteSpace(AssetProfileDraftCategory)
           && !string.IsNullOrWhiteSpace(AssetProfileDraftRationale);

    private bool CanApproveAssetProfile()
        => CanUseAssetProfileWorkflow()
           && !string.IsNullOrWhiteSpace(ResolveGovernanceProfileId())
           && TryGetGovernanceProfileVersion(out _)
           && !string.IsNullOrWhiteSpace(AssetProfileDraftRationale);

    private bool CanLoadAssetProfileLineage()
        => CanUseAssetProfileWorkflow() && !string.IsNullOrWhiteSpace(ResolveGovernanceProfileId());

    private bool CanRollbackAssetProfile()
        => CanUseAssetProfileWorkflow()
           && !string.IsNullOrWhiteSpace(ResolveGovernanceProfileId())
           && int.TryParse(AssetProfileRollbackVersion, NumberStyles.Integer, CultureInfo.InvariantCulture, out var version)
           && version > 0
           && !string.IsNullOrWhiteSpace(AssetProfileDraftRationale);

    private bool CanCreateProfileBackedSecurity()
        => CanUseAssetProfileWorkflow()
           && SelectedAssetProfileRow?.Profile.Status == SecurityAssetProfileStatusDto.Approved
           && !string.IsNullOrWhiteSpace(ProfileBackedSecurityDisplayName)
           && !string.IsNullOrWhiteSpace(ProfileBackedSecurityInternalCode)
           && AssetProfileFieldInputs.Where(static input => input.IsRequired).All(static input => !string.IsNullOrWhiteSpace(input.Value));

    private void UpdateAssetProfileCommandStates()
    {
        RefreshAssetProfilesCommand.NotifyCanExecuteChanged();
        DraftAssetProfileCommand.NotifyCanExecuteChanged();
        ApproveAssetProfileCommand.NotifyCanExecuteChanged();
        LoadAssetProfileLineageCommand.NotifyCanExecuteChanged();
        RollbackAssetProfileCommand.NotifyCanExecuteChanged();
        CreateProfileBackedSecurityCommand.NotifyCanExecuteChanged();
    }

    private void UpdateAssetProfileDerivedProperties()
    {
        RaisePropertyChanged(nameof(ApprovedAssetProfileCount));
        RaisePropertyChanged(nameof(HasAssetProfileRows));
        RaisePropertyChanged(nameof(HasSelectedAssetProfile));
        RaisePropertyChanged(nameof(AssetProfileProjectedFieldCount));
        RaisePropertyChanged(nameof(SelectedAssetProfileSummaryText));
        UpdateAssetProfileCommandStates();
    }

    private void ReplaceLineageEvents(SecurityAssetProfileLineageDto? lineage)
    {
        AssetProfileLineageEvents.Clear();
        if (lineage is not null)
        {
            foreach (var auditEvent in lineage.AuditEvents
                         .OrderByDescending(static item => item.OccurredAtUtc)
                         .Take(8))
            {
                AssetProfileLineageEvents.Add(
                    $"{auditEvent.OccurredAtUtc:u} - {auditEvent.EventType} - v{auditEvent.Version} - {auditEvent.Actor}");
            }
        }

        RaisePropertyChanged(nameof(HasAssetProfileLineageEvents));
    }

    private bool TryGetGovernanceProfileVersion(out int version)
    {
        var profileId = ResolveGovernanceProfileId();
        if (_lastAssetProfileGovernanceResult is { } result
            && string.Equals(result.Profile.ProfileId, NormalizeAssetProfileId(profileId), StringComparison.OrdinalIgnoreCase))
        {
            version = result.Profile.Version;
            return version > 0;
        }

        return int.TryParse(AssetProfileRollbackVersion, NumberStyles.Integer, CultureInfo.InvariantCulture, out version)
               && version > 0;
    }

    private string ResolveGovernanceProfileId()
        => !string.IsNullOrWhiteSpace(AssetProfileDraftProfileId)
            ? AssetProfileDraftProfileId.Trim()
            : SelectedAssetProfileRow?.ProfileId ?? string.Empty;

    private bool TryBuildProfileFields(out Dictionary<string, JsonElement> profileFields, out string? validationMessage)
    {
        profileFields = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var input in AssetProfileFieldInputs)
        {
            var raw = input.Value?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(raw) && !input.IsRequired)
            {
                continue;
            }

            if (!TryBuildProfileFieldValue(input.Definition, raw, out var element, out validationMessage))
            {
                return false;
            }

            profileFields[input.Key] = element;
        }

        validationMessage = null;
        return true;
    }

    private static bool TryBuildProfileFieldValue(
        SecurityAssetProfileFieldDefinitionDto field,
        string raw,
        out JsonElement element,
        out string? validationMessage)
    {
        validationMessage = null;
        element = default;

        switch (field.FieldType)
        {
            case SecurityAssetProfileFieldTypeDto.Decimal:
                if (!decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var decimalValue))
                {
                    validationMessage = $"{field.Label} must be a decimal value.";
                    return false;
                }

                if (!TryValidateRange(field, decimalValue, out validationMessage))
                {
                    return false;
                }

                element = JsonSerializer.SerializeToElement(decimalValue);
                return true;

            case SecurityAssetProfileFieldTypeDto.Integer:
                if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integerValue))
                {
                    validationMessage = $"{field.Label} must be an integer value.";
                    return false;
                }

                if (!TryValidateRange(field, integerValue, out validationMessage))
                {
                    return false;
                }

                element = JsonSerializer.SerializeToElement(integerValue);
                return true;

            case SecurityAssetProfileFieldTypeDto.Boolean:
                if (!TryParseBoolean(raw, out var booleanValue))
                {
                    validationMessage = $"{field.Label} must be true or false.";
                    return false;
                }

                element = JsonSerializer.SerializeToElement(booleanValue);
                return true;

            case SecurityAssetProfileFieldTypeDto.Date:
                if (!DateOnly.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateValue))
                {
                    validationMessage = $"{field.Label} must be a date.";
                    return false;
                }

                element = JsonSerializer.SerializeToElement(dateValue.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                return true;

            case SecurityAssetProfileFieldTypeDto.Enum:
                var allowed = field.AllowedValues.FirstOrDefault(value => string.Equals(value, raw, StringComparison.OrdinalIgnoreCase));
                if (field.AllowedValues.Count > 0 && allowed is null)
                {
                    validationMessage = $"{field.Label} must be one of: {string.Join(", ", field.AllowedValues)}.";
                    return false;
                }

                element = JsonSerializer.SerializeToElement(allowed ?? raw);
                return true;

            case SecurityAssetProfileFieldTypeDto.CurrencyCode:
                element = JsonSerializer.SerializeToElement(NormalizeCurrency(raw));
                return true;

            case SecurityAssetProfileFieldTypeDto.SecurityLink:
                if (!Guid.TryParse(raw, out var securityId) || securityId == Guid.Empty)
                {
                    validationMessage = $"{field.Label} must be a security id.";
                    return false;
                }

                element = JsonSerializer.SerializeToElement(securityId.ToString("D"));
                return true;

            default:
                element = JsonSerializer.SerializeToElement(raw);
                return true;
        }
    }

    private static bool TryValidateRange(
        SecurityAssetProfileFieldDefinitionDto field,
        decimal value,
        out string? validationMessage)
    {
        if (field.MinValue.HasValue && value < field.MinValue.Value)
        {
            validationMessage = $"{field.Label} must be at least {field.MinValue.Value.ToString(CultureInfo.InvariantCulture)}.";
            return false;
        }

        if (field.MaxValue.HasValue && value > field.MaxValue.Value)
        {
            validationMessage = $"{field.Label} must be at most {field.MaxValue.Value.ToString(CultureInfo.InvariantCulture)}.";
            return false;
        }

        validationMessage = null;
        return true;
    }

    private static bool TryParseBoolean(string raw, out bool value)
    {
        if (bool.TryParse(raw, out value))
        {
            return true;
        }

        if (string.Equals(raw, "yes", StringComparison.OrdinalIgnoreCase) || raw == "1")
        {
            value = true;
            return true;
        }

        if (string.Equals(raw, "no", StringComparison.OrdinalIgnoreCase) || raw == "0")
        {
            value = false;
            return true;
        }

        return false;
    }

    private static string NormalizeAssetProfileId(string? value)
    {
        var source = value?.Trim().ToLowerInvariant() ?? string.Empty;
        if (source.Length == 0)
        {
            return string.Empty;
        }

        var characters = source
            .Select(static character => char.IsLetterOrDigit(character) ? character : '-')
            .ToArray();
        return string.Join(
                "-",
                new string(characters).Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Trim('-');
    }

    private static string NormalizeCurrency(string? value)
        => string.IsNullOrWhiteSpace(value) ? "USD" : value.Trim().ToUpperInvariant();

    private static string GetRequestedBy()
        => string.IsNullOrWhiteSpace(Environment.UserName) ? "wpf-settings" : Environment.UserName;

    private static string BuildCorrelationId(string action)
        => $"wpf-settings-asset-profile-{action}-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";

    private static string FormatLineageSummary(SecurityAssetProfileLineageDto lineage)
        => $"{lineage.ProfileId}: {lineage.Versions.Count} version(s), {lineage.AuditEvents.Count} audit event(s).";

    private static string FormatApiError<T>(ApiResponse<T> response, string fallback) where T : class
    {
        if (!string.IsNullOrWhiteSpace(response.ErrorMessage))
        {
            return response.StatusCode > 0
                ? $"{fallback} HTTP {response.StatusCode}: {response.ErrorMessage}"
                : $"{fallback} {response.ErrorMessage}";
        }

        return response.StatusCode > 0 ? $"{fallback} HTTP {response.StatusCode}." : fallback;
    }
}

public sealed class SettingsAssetProfileRow
{
    public SettingsAssetProfileRow(SecurityAssetProfileDefinitionDto profile)
    {
        Profile = profile ?? throw new ArgumentNullException(nameof(profile));
    }

    public SecurityAssetProfileDefinitionDto Profile { get; }
    public string ProfileId => Profile.ProfileId;
    public string Name => Profile.Name;
    public string VersionLabel => $"v{Profile.Version}";
    public string StatusLabel => Profile.Status.ToString();
    public string CategoryLabel => string.IsNullOrWhiteSpace(Profile.SubType)
        ? Profile.Category
        : $"{Profile.Category} / {Profile.SubType}";
    public string FieldCountLabel => $"{Profile.Fields.Count} field(s)";
    public int ProjectedFieldCount => Profile.Fields.Count(static profileField => profileField.IsProjected);
    public string ProjectedFieldCountLabel => $"{ProjectedFieldCount} projected";
    public string RequiredIdentifierKindsLabel
    {
        get
        {
            var required = Profile.IdentifierPreferences
                .Where(static preference => preference.IsRequiredForClose)
                .Select(static preference => preference.Kind.ToString())
                .ToArray();
            return required.Length == 0 ? "No close identifier required" : string.Join(", ", required);
        }
    }

    public string AccountingHintsLabel => Profile.AccountingImpactHints.Count == 0
        ? "No accounting hints"
        : string.Join(", ", Profile.AccountingImpactHints);

    public string EffectiveLabel => Profile.EffectiveTo is null
        ? $"Effective from {Profile.EffectiveFrom:yyyy-MM-dd}"
        : $"{Profile.EffectiveFrom:yyyy-MM-dd} to {Profile.EffectiveTo:yyyy-MM-dd}";
}

public sealed class SettingsAssetProfileFieldInput : BindableBase
{
    private readonly Action _changed;
    private string _value = string.Empty;

    public SettingsAssetProfileFieldInput(SecurityAssetProfileFieldDefinitionDto definition, Action changed)
    {
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        _changed = changed ?? throw new ArgumentNullException(nameof(changed));
    }

    public SecurityAssetProfileFieldDefinitionDto Definition { get; }
    public string Key => Definition.Key;
    public string Label => Definition.IsRequired ? $"{Definition.Label} *" : Definition.Label;
    public SecurityAssetProfileFieldTypeDto FieldType => Definition.FieldType;
    public bool IsRequired => Definition.IsRequired;
    public string Description => Definition.Description ?? string.Empty;
    public IReadOnlyList<string> AllowedValues => Definition.AllowedValues;
    public string AllowedValuesLabel => Definition.AllowedValues.Count == 0
        ? string.Empty
        : $"Allowed: {string.Join(", ", Definition.AllowedValues)}";

    public bool IsBooleanField => Definition.FieldType == SecurityAssetProfileFieldTypeDto.Boolean;
    public bool IsEnumField => Definition.FieldType == SecurityAssetProfileFieldTypeDto.Enum
                               && Definition.AllowedValues.Count > 0;
    public bool IsDateField => Definition.FieldType == SecurityAssetProfileFieldTypeDto.Date;
    public bool IsTextEntryField => !IsBooleanField && !IsEnumField && !IsDateField;

    public string InputHint => Definition.FieldType switch
    {
        SecurityAssetProfileFieldTypeDto.Decimal => WithRange("Decimal"),
        SecurityAssetProfileFieldTypeDto.Integer => WithRange("Integer"),
        SecurityAssetProfileFieldTypeDto.Boolean => "true or false",
        SecurityAssetProfileFieldTypeDto.Date => "yyyy-mm-dd",
        SecurityAssetProfileFieldTypeDto.CurrencyCode => "ISO currency",
        SecurityAssetProfileFieldTypeDto.SecurityLink => "Security id",
        _ => Definition.FieldType.ToString()
    };

    public string Value
    {
        get => _value;
        set
        {
            if (SetProperty(ref _value, value))
            {
                _changed();
                RaisePropertyChanged(nameof(BoolValue));
                RaisePropertyChanged(nameof(DateValue));
            }
        }
    }

    /// <summary>Typed checkbox surface over <see cref="Value"/> for Boolean profile fields.</summary>
    public bool BoolValue
    {
        get => string.Equals(_value, "true", StringComparison.OrdinalIgnoreCase)
               || string.Equals(_value, "yes", StringComparison.OrdinalIgnoreCase)
               || _value == "1";
        set => Value = value ? "true" : "false";
    }

    /// <summary>Typed date-picker surface over <see cref="Value"/> for Date profile fields.</summary>
    public DateTime? DateValue
    {
        get => DateOnly.TryParse(_value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date.ToDateTime(TimeOnly.MinValue)
            : null;
        set => Value = value is null
            ? string.Empty
            : DateOnly.FromDateTime(value.Value).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private string WithRange(string baseHint)
    {
        if (!Definition.MinValue.HasValue && !Definition.MaxValue.HasValue)
        {
            return baseHint;
        }

        var minimum = Definition.MinValue?.ToString(CultureInfo.InvariantCulture) ?? "any";
        var maximum = Definition.MaxValue?.ToString(CultureInfo.InvariantCulture) ?? "any";
        return $"{baseHint} ({minimum} to {maximum})";
    }
}
