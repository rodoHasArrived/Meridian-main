using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.Input;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.Api;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.Identity.Auth;
using Meridian.Infrastructure.Adapters.Polygon;
using Meridian.Ui.Services;
using Meridian.Wpf.Models;
using ISmQueryService = Meridian.Contracts.SecurityMaster.ISecurityMasterQueryService;
using ISmService = Meridian.Contracts.SecurityMaster.ISecurityMasterService;
using WpfServices = Meridian.Wpf.Services;

namespace Meridian.Wpf.ViewModels;

public sealed partial class SecurityMasterViewModel
{
    private void RaiseSearchDerivedStateChanged()
    {
        RaisePropertyChanged(nameof(ResultCount));
        RaisePropertyChanged(nameof(HasSearchQuery));
        RaisePropertyChanged(nameof(HasSearchResults));
        RaisePropertyChanged(nameof(HasLoadedResults));
        RaisePropertyChanged(nameof(IsSearchRecoveryVisible));
        RaisePropertyChanged(nameof(SearchRecoveryTitle));
        RaisePropertyChanged(nameof(SearchRecoveryDetail));
        RaisePropertyChanged(nameof(SearchScopeText));
        RaisePropertyChanged(nameof(LoadedResultCount));
        RaisePropertyChanged(nameof(SearchResultCountLabel));
        RaisePropertyChanged(nameof(SearchMetaLabel));
        RaisePropertyChanged(nameof(SearchStatusChipLabel));
        RaisePropertyChanged(nameof(HasActiveSearchWorkspaceFilters));
        RaisePropertyChanged(nameof(SearchFilterSummaryText));
        RaisePropertyChanged(nameof(MappingHealthSummaryText));
        RaisePropertyChanged(nameof(SearchDeckLeadText));
        RaisePropertyChanged(nameof(SearchDeckDetailText));
        SearchCommand?.NotifyCanExecuteChanged();
        ClearSearchCommand?.NotifyCanExecuteChanged();
        ResetSearchFiltersCommand?.NotifyCanExecuteChanged();
    }

    private bool CanSearch()
        => HasSearchQuery && !IsLoading;

    private bool CanClearSearch()
        => HasSearchQuery || HasSearchResults || HasSelectedSecurity || _searchSection.HasSearchAttempted;

    private void OnClearSearch()
    {
        _cts?.Cancel();
        _cts = null;
        _searchSection.HasSearchAttempted = false;

        ResetSearchWorkspaceFilters();
        SearchQuery = string.Empty;
        Results.Clear();
        SelectedSecurity = null;
        HistoryText = string.Empty;
        ClearSelectedSecurityAssuranceState();
        StatusText = "Enter a query and press Search.";
        RaiseSearchDerivedStateChanged();
    }

    private void RefreshSearchWorkspaceState()
    {
        RefreshSearchWorkspaceFilterOptions();
        ApplySearchWorkspaceFilters();
    }

    private void RefreshSearchWorkspaceFilterOptions()
    {
        _isRefreshingSearchWorkspaceFilters = true;

        try
        {
            var assetClassOptions = SecurityMasterSearchWorkspaceService.BuildAssetClassOptions(Results, AllAssetClassesFilterLabel);
            ReplaceCollection(AssetClassFilterOptions, assetClassOptions);

            var providerOptions = SecurityMasterSearchWorkspaceService.BuildProviderOptions(
                Results,
                AllProvidersFilterLabel,
                SecurityMasterPresentation.GetMatchedProvider);
            ReplaceCollection(ProviderFilterOptions, providerOptions);

            if (!assetClassOptions.Contains(SelectedAssetClassFilter, StringComparer.OrdinalIgnoreCase))
            {
                SelectedAssetClassFilter = AllAssetClassesFilterLabel;
            }

            if (!providerOptions.Contains(SelectedProviderFilter, StringComparer.OrdinalIgnoreCase))
            {
                SelectedProviderFilter = AllProvidersFilterLabel;
            }
        }
        finally
        {
            _isRefreshingSearchWorkspaceFilters = false;
        }
    }

    private void ApplySearchWorkspaceFilters()
    {
        var filteredResults = SecurityMasterSearchWorkspaceService.ApplyFilters(
            Results,
            SelectedAssetClassFilter,
            SelectedProviderFilter,
            ShowMappingGapsOnly,
            AllAssetClassesFilterLabel,
            AllProvidersFilterLabel,
            SecurityMasterPresentation.GetMatchedProvider,
            SecurityMasterPresentation.HasProviderMapping);
        ReplaceCollection(FilteredResults, filteredResults);

        if (SelectedSecurity is not null &&
            !FilteredResults.Any(result => result.SecurityId == SelectedSecurity.SecurityId))
        {
            SelectedSecurity = null;
            HistoryText = string.Empty;
            ClearSelectedSecurityAssuranceState();
        }

        RaiseSearchDerivedStateChanged();
    }

    private void ResetSearchWorkspaceFilters()
    {
        _isRefreshingSearchWorkspaceFilters = true;

        try
        {
            SelectedAssetClassFilter = AllAssetClassesFilterLabel;
            SelectedProviderFilter = AllProvidersFilterLabel;
            ShowMappingGapsOnly = false;
        }
        finally
        {
            _isRefreshingSearchWorkspaceFilters = false;
        }

        ApplySearchWorkspaceFilters();
    }

    // ── Detail + history ─────────────────────────────────────────────────────
    public async Task LoadDetailAsync(Guid securityId, CancellationToken ct = default)
        => await LoadSelectedTrustSnapshotAsync(securityId, ct).ConfigureAwait(false);

    public async Task LoadSelectedTrustSnapshotAsync(Guid securityId, CancellationToken ct = default)
    {
        if (securityId == Guid.Empty)
        {
            return;
        }

        if (!TryBeginSelectedSecurityLoad(securityId, ct, out var loadRevision, out var loadCts))
        {
            return;
        }

        var loadToken = loadCts.Token;
        try
        {
            if (!CanCommitSelectedSecurityLoad(securityId, loadRevision, loadToken))
            {
                return;
            }

            IsLoading = true;
            IsTrustSnapshotLoading = true;
            TrustSnapshotErrorText = string.Empty;
            InstrumentPassportErrorText = string.Empty;
            StatusText = "Loading selected security trust snapshot...";

            await _fundContextService.LoadAsync(loadToken).ConfigureAwait(false);
            var fundProfileId = GetCurrentFundProfileId();
            var snapshot = await _workstationSecurityMasterApiClient
                .GetTrustSnapshotAsync(securityId, fundProfileId, loadToken)
                .ConfigureAwait(false);

            if (!CanCommitSelectedSecurityLoad(securityId, loadRevision, loadToken))
            {
                return;
            }

            if (snapshot is not null &&
                (snapshot.SecurityId != securityId || snapshot.Security.SecurityId != securityId))
            {
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    if (!CanCommitSelectedSecurityLoad(securityId, loadRevision, loadToken))
                    {
                        return;
                    }

                    TrustSnapshotErrorText = "Selected security trust snapshot identity did not match the current selection.";
                    StatusText = "Selected security trust snapshot identity did not match the current selection.";
                });
                return;
            }

            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (!CanCommitSelectedSecurityLoad(securityId, loadRevision, loadToken))
                {
                    return;
                }

                if (snapshot is null)
                {
                    ClearSelectedSecurityAssuranceState();
                    StatusText = "Selected security snapshot is unavailable.";
                    return;
                }

                ApplyTrustSnapshot(snapshot);
                ApplyInstrumentPassport(snapshot.InstrumentPassport);
                StatusText = snapshot.InstrumentPassport is null
                    ? string.Concat("Loaded trust snapshot for ", snapshot.Security.DisplayName, ".")
                    : string.Concat("Loaded trust snapshot and instrument passport for ", snapshot.Security.DisplayName, ".");
            });

            if (snapshot?.InstrumentPassport is null &&
                CanCommitSelectedSecurityLoad(securityId, loadRevision, loadToken))
            {
                await LoadInstrumentPassportAsync(
                        securityId,
                        fundProfileId,
                        snapshot?.Security.DisplayName,
                        loadRevision,
                        loadToken)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Cancellation is expected when the view is torn down mid-operation; benign.
            _loggingService.LogDebug(
                "Security Master operation cancelled.",
                ("view", GetType().Name));
        }
        catch (Exception ex)
        {
            if (!CanCommitSelectedSecurityLoad(securityId, loadRevision, loadToken))
            {
                return;
            }

            _loggingService.LogError("Security Master trust snapshot load failed for {SecurityId}", ex,
                ("SecurityId", securityId.ToString()));
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (!CanCommitSelectedSecurityLoad(securityId, loadRevision, loadToken))
                {
                    return;
                }

                TrustSnapshotErrorText = "Failed to load selected security trust snapshot.";
                StatusText = "Failed to load selected security trust snapshot.";
                _notificationService.ShowNotification("Security Master", "Trust snapshot load failed.", NotificationType.Error);
            });
        }
        finally
        {
            try
            {
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    if (!CanCompleteSelectedSecurityLoad(securityId, loadRevision, loadCts))
                    {
                        return;
                    }

                    IsLoading = false;
                    IsTrustSnapshotLoading = false;
                    RefreshSelectedTrustSnapshotCommand.NotifyCanExecuteChanged();
                });
            }
            finally
            {
                CompleteSelectedSecurityLoad(loadRevision, loadCts);
            }
        }
    }
}
