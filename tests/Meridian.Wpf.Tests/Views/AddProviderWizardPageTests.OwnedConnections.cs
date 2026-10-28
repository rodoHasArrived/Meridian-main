using Meridian.Wpf.Tests.Support;

namespace Meridian.Wpf.Tests.Views;

public sealed partial class AddProviderWizardPageTests
{
    [Fact]
    public void ExistingSingleConnection_StillRequiresExplicitSelection()
    {
        WpfTestThread.Run(async () =>
        {
            using var fixture = await Fixture.CreateAsync(selectOwned: false);
            fixture.ViewModel.OwnedConnections.Should().ContainSingle();
            fixture.ViewModel.SelectedOwnedConnection.Should().BeNull();
            fixture.ViewModel.CanUseOwnedCredentials.Should().BeFalse();
            await fixture.Page.TestProviderConnectionAsync();
            await fixture.Page.SaveProviderAsync();
            fixture.Writes.Should().BeEmpty();
            fixture.VerificationCalls.Should().Be(0);
            fixture.ViewModel.SaveStatusText.Should().Contain("cannot fall back");
            fixture.StatusQueries.Should().NotContain(query => query.Contains("scope=provider"));
        });
    }

    [Fact]
    public void NoOwnedConnections_ExplainsOnboardingWithoutCreatingOwnership()
    {
        WpfTestThread.Run(async () =>
        {
            using var fixture = await Fixture.CreateAsync(selectOwned: false, configure: fixture => fixture.OwnedConnections.Clear());
            fixture.ViewModel.OwnedConnections.Should().BeEmpty();
            fixture.ViewModel.OwnedConnectionStatusText.Should().Contain("onboarding").And.Contain("cannot assign ownership");
            await fixture.Page.SaveProviderAsync();
            fixture.Writes.Should().BeEmpty();
            fixture.DeletedConnections.Should().BeEmpty();
        });
    }

    [Fact]
    public void SelectedConnection_AllCredentialMutationsRemainConnectionScoped()
    {
        WpfTestThread.Run(async () =>
        {
            using var fixture = await Fixture.CreateAsync();
            fixture.EnterCredentials();
            await fixture.Page.TestProviderConnectionAsync();
            await fixture.Page.SaveProviderAsync();
            fixture.ViewModel.RemoveCredentialsConfirmed = true;
            await fixture.Page.RemoveOwnedCredentialsAsync();
            fixture.Writes.Should().ContainSingle().Which.ConnectionId.Should().Be("owned-alpaca");
            fixture.DeletedConnections.Should().Equal("owned-alpaca");
            fixture.Records["alpaca"].Configured.Should().BeFalse();
            fixture.ViewModel.ConnectionTestStatusText.Should().NotContain("Credentials verified");
            fixture.Secret.Secret.Should().BeEmpty();
            fixture.StatusQueries.Skip(1).Should().OnlyContain(query => query == "?connectionId=owned-alpaca");
        });
    }

    [Fact]
    public void RemovedConnection_BlocksBeforePersistenceAndClearsSecretEditors()
    {
        WpfTestThread.Run(async () =>
        {
            using var fixture = await Fixture.CreateAsync();
            fixture.EnterCredentials();
            var secret = fixture.Secret;
            fixture.OwnedConnections.RemoveAll(connection => connection.ConnectionId == "owned-alpaca");
            await fixture.Page.TestProviderConnectionAsync();
            fixture.Writes.Should().BeEmpty();
            fixture.VerificationCalls.Should().Be(0);
            fixture.ViewModel.CanUseOwnedCredentials.Should().BeFalse();
            fixture.ViewModel.OwnedConnectionStatusText.Should().Contain("removed or changed ownership");
            secret.Secret.Should().BeEmpty();
        });
    }

    [Fact]
    public void ForeignOrMismatchedSelection_CannotReusePreviousProviderCredentials()
    {
        WpfTestThread.Run(async () =>
        {
            using var fixture = await Fixture.CreateAsync();
            fixture.EnterCredentials();
            var secret = fixture.Secret;
            await fixture.Page.SelectOwnedConnectionAsync("owned-polygon");
            fixture.ViewModel.SelectedOwnedConnection.Should().BeNull();
            fixture.ViewModel.CanUseOwnedCredentials.Should().BeFalse();
            secret.Secret.Should().BeEmpty();
            await fixture.Page.SaveProviderAsync();
            fixture.Writes.Should().BeEmpty();
            fixture.ViewModel.SaveStatusText.Should().Contain("cannot fall back");
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RedundantSelectionDuringSaveOrTest_PreservesNewCredentialEdits(bool testConnection)
    {
        WpfTestThread.Run(async () =>
        {
            using var fixture = await Fixture.CreateAsync();
            fixture.EnterCredentials();
            fixture.PendingWrite = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var operation = testConnection ? fixture.Page.TestProviderConnectionAsync() : fixture.Page.SaveProviderAsync();
            await fixture.WriteStarted.Task;
            fixture.Key.Text = "newer-fixture-key";
            fixture.Secret.Secret = "newer-fixture-secret";
            var scope = fixture.ViewModel.SelectedOwnedConnection!;
            // A deferred binding can supply a new DTO instance or updated display label for the
            // same retained scope; it must not cancel the command or recreate secret editors.
            var notification = scope with { DisplayName = "Refreshed display label" };
            fixture.ViewModel.SelectedOwnedConnection = notification;
            await fixture.Page.ApplyOwnedConnectionSelectionAsync(notification);
            fixture.Key.Text.Should().Be("newer-fixture-key");
            fixture.Secret.Secret.Should().Be("newer-fixture-secret");
            fixture.PendingWrite.SetResult();
            await operation;
            fixture.ViewModel.SelectedOwnedConnection!.ConnectionId.Should().Be(scope.ConnectionId);
            fixture.Key.Text.Should().Be("newer-fixture-key");
            fixture.Secret.Secret.Should().Be("newer-fixture-secret");
            fixture.Writes.Should().ContainSingle().Which.ConnectionId.Should().Be(scope.ConnectionId);
            fixture.ViewModel.ConnectionTestStatusText.Should().Contain("Credentials changed");
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ActualDifferentOrClearedSelection_StillInvalidatesAcceptedScope(bool clearSelection)
    {
        WpfTestThread.Run(async () =>
        {
            using var fixture = await Fixture.CreateAsync(configure: fixture =>
            {
                fixture.OwnedConnections.Add(Fixture.Connection("alpaca", "owned-alpaca-second"));
                fixture.Records["owned-alpaca-second"] = new(false, false, null);
            });
            fixture.EnterCredentials();
            var previousSecret = fixture.Secret;
            var incoming = clearSelection ? null : fixture.OwnedConnections.Single(connection => connection.ConnectionId == "owned-alpaca-second");
            // The binding updates the view model before SelectionChanged. Accepted scope is
            // still the previous connection, so this must invalidate rather than deduplicate.
            fixture.ViewModel.SelectedOwnedConnection = incoming;
            await fixture.Page.ApplyOwnedConnectionSelectionAsync(incoming);
            previousSecret.Secret.Should().BeEmpty();
            (fixture.ViewModel.SelectedOwnedConnection?.ConnectionId).Should().Be(incoming?.ConnectionId);
            fixture.ViewModel.CanUseOwnedCredentials.Should().Be(!clearSelection);
            fixture.ViewModel.ConnectionTestStatusText.Should().Be("Not tested yet");
        });
    }

    [Fact]
    public void ConnectionSwitch_ClearsSecretsAndDiscardsLatePersistenceAndVerification()
    {
        WpfTestThread.Run(async () =>
        {
            using var fixture = await Fixture.CreateAsync(configure: fixture =>
            {
                fixture.OwnedConnections.Add(Fixture.Connection("alpaca", "owned-alpaca-second"));
                fixture.Records["owned-alpaca-second"] = new(false, false, null);
            });
            fixture.EnterCredentials();
            var oldSecret = fixture.Secret;
            fixture.PendingWrite = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var oldTest = fixture.Page.TestProviderConnectionAsync();
            await fixture.WriteStarted.Task;
            await fixture.Page.SelectOwnedConnectionAsync("owned-alpaca-second");
            oldSecret.Secret.Should().BeEmpty();
            fixture.ViewModel.ConnectionTestStatusText.Should().Be("Not tested yet");
            fixture.Secret.Secret = "second-connection-fixture-secret";
            fixture.PendingWrite.SetResult();
            await oldTest;
            fixture.ViewModel.SelectedOwnedConnection!.ConnectionId.Should().Be("owned-alpaca-second");
            fixture.Secret.Secret.Should().Be("second-connection-fixture-secret");
            fixture.VerificationCalls.Should().Be(0);
            fixture.ViewModel.ConnectionTestStatusText.Should().NotContain("Credentials verified");
            fixture.Writes.Should().ContainSingle().Which.ConnectionId.Should().Be("owned-alpaca");
        });
    }
}
