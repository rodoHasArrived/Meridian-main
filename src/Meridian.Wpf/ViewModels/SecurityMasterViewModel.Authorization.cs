using System;
using Meridian.Identity.Auth;
using Meridian.Ui.Services;

namespace Meridian.Wpf.ViewModels;

/// <summary>
/// The Security Master mutation gate, split out of the main view-model file like the bulk-import
/// flow so the authorization posture reads in one piece. The desktop lane reaches
/// <see cref="Meridian.Ui.Services.ISecurityMasterService"/> in-process with no endpoint filter in
/// between, so these members are the only place the ModifySecurityMaster grant every HTTP mutation
/// route requires can be enforced.
/// </summary>
public sealed partial class SecurityMasterViewModel
{
    /// <summary>
    /// Whether this desktop session may mutate the Security Master golden record. Every HTTP route
    /// that mutates it requires <see cref="UserPermission.ModifySecurityMaster"/>; the desktop
    /// create, edit, deactivate, and file-import commands reach the same services in-process, so
    /// they are held to the same grant. Both halves of the gate must agree:
    /// the host posture must grant the permission (so a credential-free host whose
    /// MDC_ANONYMOUS_ROLE names a read-only role refuses) and the active desktop session must name
    /// an authorized operator to record the write against. Gates command enablement and is
    /// re-checked by every handler before the service call.
    /// </summary>
    public bool CanModifySecurityMaster
        => _mutationAuthorization.IsGranted(UserPermission.ModifySecurityMaster) &&
           HasAuthorizedSecurityMasterOperator();

    private bool HasAuthorizedSecurityMasterOperator()
        => _authenticationSession is not null &&
           _authenticationSession.TryAuthorize(UserPermission.ModifySecurityMaster, out _);

    /// <summary>
    /// Enforcement half of the posture gate: command enablement is advisory (a command can be
    /// executed programmatically regardless of its predicate), so every mutation handler calls this
    /// before reaching a service, then resolves the operator through
    /// <see cref="TryAuthorizeSecurityMasterMutation"/>.
    /// </summary>
    private bool EnsureCanModifySecurityMaster()
    {
        if (_mutationAuthorization.IsGranted(UserPermission.ModifySecurityMaster))
        {
            return true;
        }

        _loggingService.LogWarning("Security Master mutation refused: this desktop session does not hold the ModifySecurityMaster permission.");
        _notificationService.ShowNotification(
            "Security Master",
            "This operator is not permitted to modify the Security Master.",
            NotificationType.Error);
        return false;
    }

    /// <summary>
    /// Backfill authority mirrors the shared HTTP boundary, where backfill routes require
    /// <see cref="UserPermission.TriggerBackfill"/> alone rather than broader Security Master edit
    /// rights, so a profile delegated only that grant can still run the backfill. The gate keeps
    /// the same two-legged shape as <see cref="CanModifySecurityMaster"/> — host posture and a
    /// signed-in operator to record the trigger against — but both legs check TriggerBackfill.
    /// </summary>
    private bool CanTriggerSecurityMasterBackfill()
        => _mutationAuthorization.IsGranted(UserPermission.TriggerBackfill) &&
           _authenticationSession is not null &&
           _authenticationSession.TryAuthorize(UserPermission.TriggerBackfill, out _);

    /// <summary>
    /// Enforcement half of the backfill posture gate, mirroring
    /// <see cref="EnsureCanModifySecurityMaster"/>: the handler re-checks the host posture before
    /// reaching the backfill service, then resolves the operator against the session.
    /// </summary>
    private bool EnsureCanTriggerSecurityMasterBackfill()
    {
        if (_mutationAuthorization.IsGranted(UserPermission.TriggerBackfill))
        {
            return true;
        }

        _loggingService.LogWarning("Security Master trading-parameter backfill refused: this desktop session does not hold the TriggerBackfill permission.");
        _notificationService.ShowNotification(
            "Security Master",
            "This operator is not permitted to trigger the trading-parameter backfill.",
            NotificationType.Error);
        return false;
    }

    private bool TryAuthorizeSecurityMasterMutation(string operation, out string actor)
    {
        if (_authenticationSession is not null &&
            _authenticationSession.TryAuthorize(UserPermission.ModifySecurityMaster, out actor))
        {
            return true;
        }

        actor = string.Empty;
        var message = $"Sign in with Security Master edit permission to {operation}.";
        StatusText = message;
        _notificationService.ShowNotification("Security Master", message, NotificationType.Error);
        _loggingService.LogWarning(
            "Security Master mutation refused: the active desktop session does not grant ModifySecurityMaster or cannot name a valid actor.",
            ("operation", operation));
        return false;
    }

    private void OnAuthenticationSessionSignedOut(object? sender, EventArgs e)
    {
        CreateNewCommand.NotifyCanExecuteChanged();
        EditSelectedCommand.NotifyCanExecuteChanged();
        DeactivateSelectedCommand.NotifyCanExecuteChanged();
        BackfillTradingParamsCommand.NotifyCanExecuteChanged();
        ImportFromFileCommand.NotifyCanExecuteChanged();
    }
}
