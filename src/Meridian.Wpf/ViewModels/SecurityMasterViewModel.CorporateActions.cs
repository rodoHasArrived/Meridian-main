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
    private void OnShowRecordCorpAction()
    {
        IsRecordCorpActionVisible = true;
        CorpActType = "Dividend";
        CorpActExDate = string.Empty;
        CorpActAmount = 0m;
        CorpActCurrency = "USD";
    }

    private void OnCancelRecordCorpAction()
    {
        IsRecordCorpActionVisible = false;
        CorpActType = "Dividend";
        CorpActExDate = string.Empty;
        CorpActAmount = 0m;
        CorpActCurrency = "USD";
    }

    private async Task OnRecordCorpAction(CancellationToken ct = default)
    {
        if (SelectedSecurity?.SecurityId is not { } securityId)
            return;

        if (string.IsNullOrWhiteSpace(CorpActExDate))
        {
            _notificationService.ShowNotification("Corporate Actions", "Please enter an ex-date.", NotificationType.Warning);
            return;
        }

        if (CorpActAmount <= 0)
        {
            _notificationService.ShowNotification("Corporate Actions", "Please enter a valid amount/ratio.", NotificationType.Warning);
            return;
        }

        try
        {
            // Parse the ex-date
            if (!DateOnly.TryParse(CorpActExDate, out var exDate))
            {
                _notificationService.ShowNotification("Corporate Actions", "Invalid date format. Use yyyy-MM-dd.", NotificationType.Warning);
                return;
            }

            // Build the CorporateActionDto
            var dto = new CorporateActionDto(
                CorpActId: Guid.NewGuid(),
                SecurityId: securityId,
                EventType: CorpActType,
                ExDate: exDate,
                PayDate: null,
                DividendPerShare: CorpActType == "Dividend" ? CorpActAmount : null,
                Currency: CorpActType == "Dividend" ? CorpActCurrency : null,
                SplitRatio: CorpActType == "StockSplit" ? CorpActAmount : null,
                NewSecurityId: null,
                DistributionRatio: null,
                AcquirerSecurityId: null,
                ExchangeRatio: null,
                SubscriptionPricePerShare: null,
                RightsPerShare: null);

            var result = (await ApiClientService.Instance
                .PostWithResponseAsync<SecurityMasterCorporateActionAppendResultDto>(UiApiRoutes.WithParam(UiApiRoutes.SecurityMasterCorporateActions, "securityId", securityId.ToString()), dto, ct)
                .ConfigureAwait(false)).DataOrLoggedNull("Record corporate action");

            if (result is not null)
            {
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    IsRecordCorpActionVisible = false;
                    CorpActType = "Dividend";
                    CorpActExDate = string.Empty;
                    CorpActAmount = 0m;
                    CorpActCurrency = "USD";
                    _notificationService.ShowNotification("Corporate Actions", "Corporate action recorded successfully.", NotificationType.Success);
                });

                await LoadSelectedTrustSnapshotAsync(securityId, ct).ConfigureAwait(false);
            }
            else
            {
                _notificationService.ShowNotification("Corporate Actions", "Failed to record corporate action.", NotificationType.Error);
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
            _loggingService.LogError("Failed to record corporate action", ex);
            _notificationService.ShowNotification("Corporate Actions", "An error occurred while recording the corporate action.", NotificationType.Error);
        }
    }

    // ── Conflict badge ───────────────────────────────────────────────────────
    private async Task RefreshConflictCountAsync(CancellationToken ct = default)
    {
        await RefreshOperatorWorkflowAsync(ct).ConfigureAwait(false);
    }
}
