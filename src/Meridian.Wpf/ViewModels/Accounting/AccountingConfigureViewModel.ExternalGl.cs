using System;
using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using Meridian.Contracts.AccountingSystem;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.AccountingSystem;
using Meridian.FinancialOperations.Ledger;
using Meridian.Ui.Shared.Services;
using Meridian.Ui.Services.Services.Accounting;
using Meridian.Wpf.Models;
using Meridian.Wpf.Services;
using Meridian.Contracts.Integrity;
using UtcFormat = Meridian.Ui.Services.FormatHelpers;

namespace Meridian.Wpf.ViewModels.Accounting;

public sealed partial class AccountingConfigureViewModel
{
    public async Task SaveExternalGlMappingProfileAsync(CancellationToken ct = default)
    {
        if (_activeFundProfile is null)
        {
            ExternalGlMappingProfileStatusText = "Select a fund-linked context before saving external GL mapping profiles.";
            StatusText = ExternalGlMappingProfileStatusText;
            return;
        }

        if (_accountingSystemIntegrationService is null)
        {
            ExternalGlMappingProfileStatusText = "External GL mapping profile service is not registered for this desktop session.";
            StatusText = ExternalGlMappingProfileStatusText;
            return;
        }

        var providerId = ExternalGlMappingProviderId.Trim();
        var profileId = ExternalGlMappingProfileId.Trim();
        var displayName = ExternalGlMappingDisplayName.Trim();
        var accountMappings = ParseExternalGlAccountMappings(ExternalGlMappingAccountMappingsText);
        var meridianDimensions = ParseLedgerDimensionSet(
            ExternalGlMappingMeridianDimensionsText,
            BuildDefaultMeridianExternalGlDimensions(providerId, profileId));
        var externalDimensions = ParseLedgerDimensionSet(
            ExternalGlMappingExternalDimensionsText,
            BuildDefaultProviderExternalGlDimensions());
        var retainedEvidence = NormalizeTenantAdministrationEvidence(ExternalGlMappingEvidenceText);
        if (string.IsNullOrWhiteSpace(providerId) ||
            string.IsNullOrWhiteSpace(profileId) ||
            string.IsNullOrWhiteSpace(displayName) ||
            accountMappings.Count == 0 ||
            retainedEvidence.Count == 0)
        {
            ExternalGlMappingProfileStatusText = "Provider, profile id, display name, account mappings, and retained mapping evidence are required before saving.";
            StatusText = ExternalGlMappingProfileStatusText;
            NotifyExternalGlMappingProfileSaveStateChanged();
            return;
        }

        try
        {
            var profile = new ExternalGlMappingProfileDto(
                profileId,
                providerId,
                displayName,
                DateTimeOffset.UtcNow,
                [BuildExternalGlDimensionMappingProfile(providerId, profileId, meridianDimensions, externalDimensions, ExternalGlMappingProfileCertified)],
                accountMappings,
                ExternalGlMappingProfileCertified ? AccountingCertificationStateDto.Certified : AccountingCertificationStateDto.ReadyForReview);
            var evidence = WithExternalGlMappingProfileEvidence(
                retainedEvidence,
                providerId,
                _activeFundProfile.FundProfileId,
                profileId,
                _configuration?.LedgerBookId);
            var saved = await _accountingSystemIntegrationService.UpsertMappingProfileAsync(
                new AccountingSystemMappingProfileUpsertRequestDto(
                    profile,
                    DefaultLifecycleActor,
                    providerId,
                    _activeFundProfile.FundProfileId,
                    _configuration?.LedgerBookId,
                    $"wpf-external-gl-mapping-profile-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
                    evidence,
                    ResolveTenantAdministrationTenantId(),
                    ResolveTenantAdministrationCompanyId()),
                ct).ConfigureAwait(false);
            ApplyExternalGlMappingProfileDraft(saved);
            ExternalGlMappingProfileStatusText = $"External GL mapping profile {saved.ProfileId} saved as {saved.CertificationState}; readiness refreshed from retained provider mapping.";
            StatusText = ExternalGlMappingProfileStatusText;
            await RefreshExternalGlAsync(ct).ConfigureAwait(false);
            await RefreshProductionReadinessAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            ExternalGlMappingProfileStatusText = $"External GL mapping profile save failed: {ex.Message}";
            StatusText = ExternalGlMappingProfileStatusText;
        }
    }

    public async Task RefreshExternalGlAsync(CancellationToken ct = default)
    {
        ProviderRows.Clear();
        ExternalGlMappingProfileRows.Clear();
        ExternalGlEvidencePackageRows.Clear();
        ExternalGlRows.Clear();
        if (_accountingSystemIntegrationService is null)
        {
            ExternalGlStatusText = "External GL evidence service is not registered for this desktop session.";
            PostingPostureText = "Posting/export remains unavailable.";
            return;
        }

        try
        {
            var providers = await _accountingSystemIntegrationService.ListProvidersAsync(ct).ConfigureAwait(false);
            foreach (var provider in providers)
            {
                var requirementSummary = provider.MappingRequirements.Count == 0
                    ? provider.StatusDetail
                    : $"{provider.StatusDetail} Requirements: {string.Join(", ", provider.MappingRequirements.Take(3).Select(static requirement => requirement.Label))}.";
                ProviderRows.Add(new AccountingWorkbenchRow(
                    provider.DisplayName,
                    provider.State.ToString(),
                    requirementSummary,
                    provider.ProviderId));
            }

            if (_activeFundProfile is null)
            {
                ExternalGlStatusText = "Select a fund-linked context before importing external GL evidence.";
                PostingPostureText = "Posting/export remains disabled until Meridian-owned ledger entries are approved.";
                return;
            }

            var mappingProfiles = await _accountingSystemIntegrationService
                .ListMappingProfilesAsync(
                    fundProfileId: _activeFundProfile.FundProfileId,
                    ledgerBookId: _configuration?.LedgerBookId,
                    ct: ct,
                    tenantId: ResolveTenantAdministrationTenantId(),
                    companyId: ResolveTenantAdministrationCompanyId())
                .ConfigureAwait(false);
            foreach (var profile in mappingProfiles.Take(20))
            {
                ExternalGlMappingProfileRows.Add(new AccountingWorkbenchRow(
                    profile.DisplayName,
                    profile.CertificationState.ToString(),
                    $"{profile.ProviderId}; {profile.AccountMappings.Count} account mapping(s), {profile.DimensionMappings.Count} dimension mapping(s).",
                    profile.ProfileId,
                    profile.ProfileId));
            }

            if (mappingProfiles.Count > 0)
            {
                ApplyExternalGlMappingProfileDraft(mappingProfiles[0]);
            }
            else
            {
                ApplyDefaultExternalGlMappingDimensionDraft();
            }

            var reconciliation = await _accountingSystemIntegrationService
                .ReconcileLatestAsync(fundProfileId: _activeFundProfile.FundProfileId, ledgerBookId: _configuration?.LedgerBookId, ct: ct)
                .ConfigureAwait(false);

            foreach (var package in reconciliation.EvidencePackages.Take(20))
            {
                ExternalGlEvidencePackageRows.Add(new AccountingWorkbenchRow(
                    package.Label,
                    package.Status.ToString(),
                    package.RequiredActions.Count > 0
                        ? string.Join(" ", package.RequiredActions)
                        : $"{package.EvidenceReferenceCount} retained evidence reference(s).",
                    package.EvidenceReferences.FirstOrDefault() ?? package.PackageId,
                    package.PackageId));
            }

            foreach (var row in reconciliation.Rows.Take(50))
            {
                ExternalGlRows.Add(new ExternalGlEvidenceRow(
                    row.AccountCode,
                    row.AccountName,
                    row.Status.ToString(),
                    row.ExternalDebit,
                    row.ExternalCredit,
                    row.MeridianDebit,
                    row.MeridianCredit,
                    row.Variance,
                    row.Detail,
                    row.EvidenceRef ?? string.Empty));
            }

            ExternalGlStatusText = $"{reconciliation.ProviderId} evidence: {reconciliation.MatchedCount} matched, {reconciliation.BreakCount} review row(s).";
            PostingPostureText = reconciliation.PostingEnabled
                ? "External GL posting/export is enabled by connection policy."
                : reconciliation.PostingDisabledReason;
        }
        catch (Exception ex)
        {
            ExternalGlStatusText = $"External GL evidence could not be loaded: {ex.Message}";
            PostingPostureText = "Posting/export remains disabled while external GL evidence is unavailable.";
        }
    }

    private void NotifyExternalGlMappingProfileSaveStateChanged()
    {
        RaisePropertyChanged(nameof(CanSaveExternalGlMappingProfile));
        SaveExternalGlMappingProfileCommand.NotifyCanExecuteChanged();
    }

    private void ApplyDefaultExternalGlMappingDimensionDraft()
    {
        var providerId = string.IsNullOrWhiteSpace(ExternalGlMappingProviderId)
            ? "quickbooks-fixture"
            : ExternalGlMappingProviderId.Trim();
        var profileId = string.IsNullOrWhiteSpace(ExternalGlMappingProfileId)
            ? $"{providerId}-ledger-book-mapping"
            : ExternalGlMappingProfileId.Trim();
        ExternalGlMappingMeridianDimensionsText = FormatExternalGlLedgerDimensionSet(BuildDefaultMeridianExternalGlDimensions(providerId, profileId));
        ExternalGlMappingExternalDimensionsText = FormatExternalGlLedgerDimensionSet(BuildDefaultProviderExternalGlDimensions());
    }

    private DimensionMappingProfileDto BuildExternalGlDimensionMappingProfile(
        string providerId,
        string profileId,
        LedgerDimensionSetDto meridianDimensions,
        LedgerDimensionSetDto externalDimensions,
        bool certified)
    {
        return new DimensionMappingProfileDto(
            $"{profileId}-dimension-scope",
            "Canonical and provider dimension scope",
            providerId,
            meridianDimensions,
            externalDimensions,
            certified ? AccountingCertificationStateDto.Certified : AccountingCertificationStateDto.ReadyForReview);
    }

    private LedgerDimensionSetDto BuildDefaultMeridianExternalGlDimensions(string providerId, string profileId)
    {
        var ledgerBookText = _configuration?.LedgerBookId?.ToString("D", CultureInfo.InvariantCulture);
        var fundProfileId = _activeFundProfile?.FundProfileId ?? "fund-scope";
        var entityId = _configuration?.LedgerBookSetupCandidate?.FundStructureNodeId.ToString("D", CultureInfo.InvariantCulture)
            ?? _activeFundProfile?.EntityIds?.FirstOrDefault()
            ?? fundProfileId;
        return new LedgerDimensionSetDto(
            FundId: fundProfileId,
            EntityId: entityId,
            BookId: ledgerBookText,
            CostCenterId: "fund-accounting",
            ExternalGlDimensions: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Provider"] = providerId,
                ["MappingProfile"] = profileId
            });
    }

    private LedgerDimensionSetDto BuildDefaultProviderExternalGlDimensions()
    {
        var ledgerBookText = _configuration?.LedgerBookId?.ToString("D", CultureInfo.InvariantCulture);
        var fundProfileId = _activeFundProfile?.FundProfileId ?? "fund-scope";
        var entityId = _configuration?.LedgerBookSetupCandidate?.FundStructureNodeId.ToString("D", CultureInfo.InvariantCulture)
            ?? _activeFundProfile?.EntityIds?.FirstOrDefault()
            ?? fundProfileId;
        return new LedgerDimensionSetDto(
            FundId: fundProfileId,
            EntityId: entityId,
            BookId: ledgerBookText is null ? null : $"Book:{ledgerBookText}",
            CostCenterId: "FundAccounting",
            ExternalGlDimensions: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Class"] = fundProfileId,
                ["Book"] = ledgerBookText ?? "fund-scope"
            });
    }

    private static IReadOnlyDictionary<string, string> ParseExternalGlAccountMappings(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        return value
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(static line =>
            {
                var index = line.IndexOf('=');
                return index <= 0 || index >= line.Length - 1
                    ? default
                    : new KeyValuePair<string, string>(line[..index].Trim(), line[(index + 1)..].Trim());
            })
            .Where(static pair => !string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
            .GroupBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.First().Value, StringComparer.OrdinalIgnoreCase);
    }

    private static LedgerDimensionSetDto ParseLedgerDimensionSet(string? value, LedgerDimensionSetDto defaults)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaults;
        }

        var externalDimensions = new Dictionary<string, string>(defaults.ExternalGlDimensions ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
        var fundId = defaults.FundId;
        var entityId = defaults.EntityId;
        var sleeveId = defaults.SleeveId;
        var strategyId = defaults.StrategyId;
        var investorId = defaults.InvestorId;
        var capitalAccountId = defaults.CapitalAccountId;
        var instrumentId = defaults.InstrumentId;
        var taxLotId = defaults.TaxLotId;
        var costCenterId = defaults.CostCenterId;
        var counterpartyId = defaults.CounterpartyId;
        var organizationId = defaults.OrganizationId;
        var portfolioId = defaults.PortfolioId;
        var bookId = defaults.BookId;
        var accountId = defaults.AccountId;
        var customerId = defaults.CustomerId;
        var vendorId = defaults.VendorId;
        var projectId = defaults.ProjectId;

        foreach (var pair in ParseExternalGlAccountMappings(value))
        {
            switch (NormalizeDimensionKey(pair.Key))
            {
                case "fundid":
                    fundId = pair.Value;
                    break;
                case "entityid":
                    entityId = pair.Value;
                    break;
                case "sleeveid":
                    sleeveId = pair.Value;
                    break;
                case "strategyid":
                    strategyId = pair.Value;
                    break;
                case "investorid":
                    investorId = pair.Value;
                    break;
                case "capitalaccountid":
                    capitalAccountId = pair.Value;
                    break;
                case "instrumentid":
                    if (Guid.TryParse(pair.Value, out var parsedInstrumentId))
                    {
                        instrumentId = parsedInstrumentId;
                    }
                    break;
                case "taxlotid":
                    taxLotId = pair.Value;
                    break;
                case "costcenterid":
                    costCenterId = pair.Value;
                    break;
                case "counterpartyid":
                    counterpartyId = pair.Value;
                    break;
                case "organizationid":
                    organizationId = pair.Value;
                    break;
                case "portfolioid":
                    portfolioId = pair.Value;
                    break;
                case "bookid":
                    bookId = pair.Value;
                    break;
                case "accountid":
                    accountId = pair.Value;
                    break;
                case "customerid":
                    customerId = pair.Value;
                    break;
                case "vendorid":
                    vendorId = pair.Value;
                    break;
                case "projectid":
                    projectId = pair.Value;
                    break;
                default:
                    externalDimensions[pair.Key] = pair.Value;
                    break;
            }
        }

        return new LedgerDimensionSetDto(
            fundId,
            entityId,
            sleeveId,
            strategyId,
            investorId,
            capitalAccountId,
            instrumentId,
            taxLotId,
            costCenterId,
            counterpartyId,
            externalDimensions,
            organizationId,
            portfolioId,
            bookId,
            accountId,
            customerId,
            vendorId,
            projectId);
    }

    private static string NormalizeDimensionKey(string key)
    {
        return new string(key.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
    }

    private static string FormatExternalGlLedgerDimensionSet(LedgerDimensionSetDto dimensions)
    {
        var rows = new List<string>();
        Add("fundId", dimensions.FundId);
        Add("entityId", dimensions.EntityId);
        Add("sleeveId", dimensions.SleeveId);
        Add("strategyId", dimensions.StrategyId);
        Add("investorId", dimensions.InvestorId);
        Add("capitalAccountId", dimensions.CapitalAccountId);
        Add("instrumentId", dimensions.InstrumentId?.ToString("D", CultureInfo.InvariantCulture));
        Add("taxLotId", dimensions.TaxLotId);
        Add("costCenterId", dimensions.CostCenterId);
        Add("counterpartyId", dimensions.CounterpartyId);
        Add("organizationId", dimensions.OrganizationId);
        Add("portfolioId", dimensions.PortfolioId);
        Add("bookId", dimensions.BookId);
        Add("accountId", dimensions.AccountId);
        Add("customerId", dimensions.CustomerId);
        Add("vendorId", dimensions.VendorId);
        Add("projectId", dimensions.ProjectId);
        foreach (var pair in (dimensions.ExternalGlDimensions ?? new Dictionary<string, string>()).OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            Add(pair.Key, pair.Value);
        }

        return string.Join(Environment.NewLine, rows);

        void Add(string key, string? entryValue)
        {
            if (!string.IsNullOrWhiteSpace(entryValue))
            {
                rows.Add($"{key}={entryValue.Trim()}");
            }
        }
    }

    private static IReadOnlyList<string> WithExternalGlMappingProfileEvidence(
        IReadOnlyList<string> evidence,
        string providerId,
        string fundProfileId,
        string profileId,
        Guid? ledgerBookId)
        => evidence
            .Append($"approval:external-gl-mapping:{profileId}")
            .Append($"evidence://external-gl/mapping-certification/provider/{providerId}/fund/{fundProfileId}/profile/{profileId}")
            .Concat(ledgerBookId is { } bookId
                ? [$"evidence://ledger-book/{bookId:D}/external-gl/mapping-certification/{profileId}"]
                : [])
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Select(static item => item.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static IReadOnlyList<string> WithLedgerBookTenantAdministrationEvidence(
        IReadOnlyList<string> evidence,
        string tenantId,
        string companyId,
        Guid? ledgerBookId,
        bool ledgerBookAdministrationStudioConfigured,
        bool implementationSandboxConfigured)
    {
        if (ledgerBookId is not { } bookId)
        {
            return evidence;
        }

        var bookText = bookId.ToString("D", CultureInfo.InvariantCulture);
        var generated = new List<string>();
        if (ledgerBookAdministrationStudioConfigured
            && !HasTenantAdministrationLedgerBookEvidence(evidence, bookText, "ledger-book-administration"))
        {
            generated.Add($"evidence://tenant-admin/{tenantId}/{companyId}/ledger-book-administration/ledgerBookId={bookText}");
        }

        if (implementationSandboxConfigured)
        {
            generated.AddRange(BuildImplementationSandboxEvidence(tenantId, companyId, bookText));
        }

        return evidence
            .Concat(generated)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
