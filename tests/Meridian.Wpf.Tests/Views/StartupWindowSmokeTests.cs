using System.Windows.Controls;
using FluentAssertions;
using Meridian.Ui.Services.Services;
using Meridian.Wpf.Tests.Services;
using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.ViewModels;
using Meridian.Wpf.Views;

namespace Meridian.Wpf.Tests.Views;

[Collection("DesktopAuthenticationEnvironment")]
public sealed class StartupWindowSmokeTests
{
    [Fact]
    public void StartupWindow_ShouldInstantiateWithApplicationResources()
    {
        WpfTestThread.Run(() =>
        {
            using var env = new DesktopAuthenticationSessionTests.EnvironmentVariableScope()
                .Set("MDC_USERS", DesktopAuthenticationSessionTests.HashedDesktopAdminUsersJson())
                .Set("MDC_USERNAME", null)
                .Set("MDC_PASSWORD_HASH", null)
                .Set("MDC_AUTH_MODE", null);

            RunMatUiAutomationFacade.EnsureApplicationResources();
            var session = DesktopAuthenticationSessionTests.CreateSession("Production");
            var viewModel = new StartupWindowViewModel(
                session,
                FixtureModeDetector.Instance,
                new DesktopAuthenticationSessionTests.FakeHostEnvironment("Production"));
            var window = new StartupWindow(viewModel);

            try
            {
                window.ApplyTemplate();

                window.FindName("UsernameBox").Should().BeOfType<TextBox>();
                window.FindName("StartupPasswordInput").Should().BeOfType<SecretInputControl>();
                window.DataContext.Should().BeSameAs(viewModel);
            }
            finally
            {
                window.Close();
            }
        });
    }
}
