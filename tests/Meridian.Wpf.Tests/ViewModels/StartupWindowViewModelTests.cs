using System.Windows;
using FluentAssertions;
using Meridian.Ui.Services.Services;
using Meridian.Wpf.Services;
using Meridian.Wpf.Tests.Services;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Tests.ViewModels;

[Collection("DesktopAuthenticationEnvironment")]
public sealed class StartupWindowViewModelTests
{
    [Fact]
    public async Task SignInCommand_WithValidCredentials_ShouldCompleteStartupAndClearPassword()
    {
        using var env = new DesktopAuthenticationSessionTests.EnvironmentVariableScope()
            .Set("MDC_USERS", DesktopAuthenticationSessionTests.HashedDesktopAdminUsersJson())
            .Set("MDC_USERNAME", null)
            .Set("MDC_PASSWORD_HASH", null)
            .Set("MDC_AUTH_MODE", null);
        var session = DesktopAuthenticationSessionTests.CreateSession("Production");
        var viewModel = CreateViewModel(session, "Production");
        var completed = false;
        var resetRequested = false;
        viewModel.StartupCompleted += (_, _) => completed = true;
        viewModel.PasswordResetRequested += (_, _) => resetRequested = true;

        viewModel.SignInCommand.CanExecute(null).Should().BeFalse();
        viewModel.Username = "desktop-admin";
        viewModel.Password = "pw";

        viewModel.SignInCommand.CanExecute(null).Should().BeTrue();
        await viewModel.SignInCommand.ExecuteAsync(null);

        completed.Should().BeTrue();
        resetRequested.Should().BeTrue();
        session.CurrentActor.Should().Be("desktop-admin");
        viewModel.Password.Should().BeEmpty();
        viewModel.StatusVisibility.Should().Be(Visibility.Visible);
        viewModel.StatusText.Should().Contain("Signed in");
    }

    [Fact]
    public async Task SignInCommand_WithInvalidCredentials_ShouldShowErrorAndKeepStartupOpen()
    {
        using var env = new DesktopAuthenticationSessionTests.EnvironmentVariableScope()
            .Set("MDC_USERS", DesktopAuthenticationSessionTests.HashedDesktopAdminUsersJson())
            .Set("MDC_USERNAME", null)
            .Set("MDC_PASSWORD_HASH", null)
            .Set("MDC_AUTH_MODE", null);
        var session = DesktopAuthenticationSessionTests.CreateSession("Production");
        var viewModel = CreateViewModel(session, "Production");
        var completed = false;
        viewModel.StartupCompleted += (_, _) => completed = true;

        viewModel.Username = "desktop-admin";
        viewModel.Password = "wrong";
        await viewModel.SignInCommand.ExecuteAsync(null);

        completed.Should().BeFalse();
        session.IsAuthenticated.Should().BeFalse();
        viewModel.Password.Should().BeEmpty();
        viewModel.SignInCommand.CanExecute(null).Should().BeFalse();
        viewModel.StatusVisibility.Should().Be(Visibility.Visible);
        viewModel.StatusText.Should().Contain("does not match");
    }

    [Fact]
    public void ContinueWithoutCredentialsCommand_WhenDevelopmentUnconfigured_ShouldCompleteStartup()
    {
        using var env = new DesktopAuthenticationSessionTests.EnvironmentVariableScope()
            .Set("MDC_USERS", null)
            .Set("MDC_USERNAME", null)
            .Set("MDC_PASSWORD_HASH", null)
            .Set("MDC_AUTH_MODE", "optional");
        var session = DesktopAuthenticationSessionTests.CreateSession("Development");
        var viewModel = CreateViewModel(session, "Development");
        var completed = false;
        viewModel.StartupCompleted += (_, _) => completed = true;

        viewModel.LoginFormVisibility.Should().Be(Visibility.Collapsed);
        viewModel.MissingConfigurationVisibility.Should().Be(Visibility.Visible);
        viewModel.ContinueWithoutCredentialsVisibility.Should().Be(Visibility.Visible);
        viewModel.ContinueWithoutCredentialsCommand.Execute(null);

        completed.Should().BeTrue();
        session.IsAnonymousDevelopmentSession.Should().BeTrue();
        viewModel.StatusText.Should().Contain("Continuing without credentials");
    }

    private static StartupWindowViewModel CreateViewModel(DesktopAuthenticationSession session, string environmentName)
        => new(
            session,
            FixtureModeDetector.Instance,
            new DesktopAuthenticationSessionTests.FakeHostEnvironment(environmentName));
}
