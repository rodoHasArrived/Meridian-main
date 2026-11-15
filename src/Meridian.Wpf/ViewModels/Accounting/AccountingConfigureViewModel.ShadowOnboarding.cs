using Meridian.Ui.Services.Services.Accounting;

namespace Meridian.Wpf.ViewModels.Accounting;

public sealed partial class AccountingConfigureViewModel
{
    /// <summary>
    /// Shadow-mode parallel run for bounded onboarding workspaces (W11-SHADOW-001), over the same API and contracts
    /// as the browser shadow panel. The desktop keeps no onboarding state of its own.
    /// </summary>
    public OnboardingShadowWorkspace ShadowOnboarding { get; }

    private void AttachShadowOnboardingSessionReset()
    {
        if (_authenticationSession is null)
            return;
        System.Windows.WeakEventManager<Meridian.Wpf.Services.DesktopAuthenticationSession, EventArgs>.AddHandler(
            _authenticationSession, nameof(Meridian.Wpf.Services.DesktopAuthenticationSession.SignedOut), OnShadowOnboardingSessionChanged);
        System.Windows.WeakEventManager<Meridian.Wpf.Services.DesktopAuthenticationSession, EventArgs>.AddHandler(
            _authenticationSession, nameof(Meridian.Wpf.Services.DesktopAuthenticationSession.SignedIn), OnShadowOnboardingSessionChanged);
    }

    private void OnShadowOnboardingSessionChanged(object? sender, EventArgs args)
    {
        // Workspaces read under one operator's session never carry over to the next.
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
            dispatcher.Invoke(ShadowOnboarding.ResetSession);
        else
            ShadowOnboarding.ResetSession();
    }
}
