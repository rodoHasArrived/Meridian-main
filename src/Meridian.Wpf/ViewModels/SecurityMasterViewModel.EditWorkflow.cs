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
    private void OnCreateNew()
    {
        if (!EnsureCanModifySecurityMaster())
            return;

        if (!TryAuthorizeSecurityMasterMutation("create a security", out _))
            return;

        EditVm = SecurityMasterEditViewModel.CreateNew(_loggingService, _notificationService, _service, _authenticationSession, _mutationAuthorization, _governedTermAmendmentPolicy);
        WireEditVmEvents();
        IsEditPanelVisible = true;
    }

    private void OnEditSelected()
    {
        if (SelectedSecurity is null ||
            !EnsureCanModifySecurityMaster() ||
            !TryAuthorizeSecurityMasterMutation("edit a security", out _))
            return;

        // Fetch the full detail so we have all the required information
        _ = LoadAndEditAsync();
    }

    private async Task LoadAndEditAsync()
    {
        if (SelectedSecurity?.SecurityId is not { } id)
            return;

        try
        {
            var detail = await _queryService.GetByIdAsync(id, CancellationToken.None)
                .ConfigureAwait(false);

            if (detail is not null)
            {
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    EditVm = new SecurityMasterEditViewModel(_loggingService, _notificationService, _service, _authenticationSession, _mutationAuthorization, _governedTermAmendmentPolicy);
                    EditVm.LoadForEdit(detail);
                    WireEditVmEvents();
                    IsEditPanelVisible = true;
                });
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _loggingService.LogError($"Failed to load security {id} for edit", ex);
            StatusText = "Failed to load security for editing.";
            _notificationService.ShowNotification("Security Master", "Failed to load security.", NotificationType.Error);
        }
    }

    private void OnDeactivateSelected()
    {
        if (SelectedSecurity is null ||
            !EnsureCanModifySecurityMaster() ||
            !TryAuthorizeSecurityMasterMutation("deactivate a security", out _))
            return;

        DeactivateVm = new SecurityMasterDeactivateViewModel(
            _loggingService,
            _notificationService,
            _service,
            _authenticationSession,
            _mutationAuthorization)
        {
            SecurityName = SelectedSecurity.DisplayName,
            SecurityId = SelectedSecurity.SecurityId,
            Version = SelectedSecurity.EconomicDefinition?.Version ?? 0
        };
        WireDeactivateVmEvents();
        IsDeactivatePanelVisible = true;
    }

    private bool IsSelectedSecurityActive()
    {
        return SelectedSecurity?.Status == SecurityStatusDto.Active;
    }

    private void WireEditVmEvents()
    {
        if (EditVm is null)
            return;

        EditVm.CancelRequested += OnEditCancelled;
        EditVm.SaveCompleted += OnEditSaveCompleted;
    }

    private void UnwireEditVmEvents()
    {
        if (EditVm is null)
            return;

        EditVm.CancelRequested -= OnEditCancelled;
        EditVm.SaveCompleted -= OnEditSaveCompleted;
    }

    private void OnEditCancelled()
    {
        UnwireEditVmEvents();
        IsEditPanelVisible = false;
    }

    private void OnEditSaveCompleted(SecurityDetailDto result)
    {
        UnwireEditVmEvents();
        IsEditPanelVisible = false;
        StatusText = "Security saved successfully.";

        // Refresh search results
        _ = SearchAsync();
    }

    private void WireDeactivateVmEvents()
    {
        if (DeactivateVm is null)
            return;

        DeactivateVm.CancelRequested += OnDeactivateCancelled;
        DeactivateVm.DeactivateCompleted += OnDeactivateCompleted;
    }

    private void UnwireDeactivateVmEvents()
    {
        if (DeactivateVm is null)
            return;

        DeactivateVm.CancelRequested -= OnDeactivateCancelled;
        DeactivateVm.DeactivateCompleted -= OnDeactivateCompleted;
    }

    private void OnDeactivateCancelled()
    {
        UnwireDeactivateVmEvents();
        IsDeactivatePanelVisible = false;
    }

    private void OnDeactivateCompleted()
    {
        UnwireDeactivateVmEvents();
        IsDeactivatePanelVisible = false;
        StatusText = "Security deactivated successfully.";

        // Refresh search results
        _ = SearchAsync();
    }

    private async Task OnBackfillTradingParams()
    {
        // The largest single mutation on this lane: one invocation amends up to 1,000 securities.
        // Its authority is TriggerBackfill, matching the shared HTTP boundary where backfill
        // routes require that grant alone — a profile delegated backfill without broader Security
        // Master edit rights may run it — enforced on both the host posture below and the
        // signed-in operator that follows.
        if (!EnsureCanTriggerSecurityMasterBackfill())
            return;

        if (_authenticationSession is null ||
            !_authenticationSession.TryAuthorize(UserPermission.TriggerBackfill, out var initiatedBy))
        {
            const string message = "Sign in with backfill permission to backfill trading parameters.";
            StatusText = message;
            _notificationService.ShowNotification("Security Master", message, NotificationType.Error);
            _loggingService.LogWarning(
                "Security Master trading-parameter backfill refused: the active desktop session does not grant TriggerBackfill or cannot name a valid actor.");
            return;
        }

        try
        {
            IsBackfillingTradingParams = true;
            BackfillStatus = "Starting trading parameters backfill…";

            // The resolved operator rides into every amendment's UpdatedBy: a backfill can
            // amend up to 1,000 securities, and the audit trail must name who pressed the
            // button rather than the automation that acted for them.
            await _backfillService.BackfillAllAsync(initiatedBy).ConfigureAwait(false);

            BackfillStatus = "Trading parameters backfill completed successfully.";
            _notificationService.ShowNotification("Security Master",
                "Trading parameters backfilled successfully.", NotificationType.Success);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _loggingService.LogError("Trading parameters backfill failed", ex);
            BackfillStatus = "Backfill failed. Check logs for details.";
            _notificationService.ShowNotification("Security Master",
                "Trading parameters backfill failed.", NotificationType.Error);
        }
        finally
        {
            IsBackfillingTradingParams = false;
        }
    }

    // ── Lifecycle ───────────────────────────────────────────────────────────
    public void Stop()
    {
        CancelSelectedSecurityLoad();
        IsLoading = false;
        IsTrustSnapshotLoading = false;

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;

        _workflowCts?.Cancel();
        _workflowCts?.Dispose();
        _workflowCts = null;
        _workflowPollingTask = null;
    }
}
