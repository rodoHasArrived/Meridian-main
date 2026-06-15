#if WINDOWS
using FluentAssertions;
using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.ViewModels;
using WpfServices = Meridian.Wpf.Services;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed class CredentialManagementViewModelTests
{
    [Fact]
    public void RemoveCredentialCommand_FirstClick_ArmsConfirmationWithoutRemoving()
    {
        WpfTestThread.Run(() =>
        {
            var viewModel = CreateViewModel();
            viewModel.SelectedCredential = CreateEntry();

            viewModel.RemoveCredentialCommand.Execute(null);

            viewModel.IsRemoveConfirmPending.Should().BeTrue();
            viewModel.RemoveButtonText.Should().Be("Confirm remove");
            viewModel.IsTestResultVisible.Should().BeTrue();
            viewModel.TestResultText.Should().Contain("Confirm remove");
        });
    }

    [Fact]
    public void CancelRemoveCommand_ClearsPendingConfirmation()
    {
        WpfTestThread.Run(() =>
        {
            var viewModel = CreateViewModel();
            viewModel.SelectedCredential = CreateEntry();
            viewModel.RemoveCredentialCommand.Execute(null);

            viewModel.CancelRemoveCommand.Execute(null);

            viewModel.IsRemoveConfirmPending.Should().BeFalse();
            viewModel.RemoveButtonText.Should().Be("Remove");
            viewModel.IsTestResultVisible.Should().BeFalse();
        });
    }

    [Fact]
    public void ChangingSelection_ResetsPendingRemoveConfirmation()
    {
        WpfTestThread.Run(() =>
        {
            var viewModel = CreateViewModel();
            viewModel.SelectedCredential = CreateEntry();
            viewModel.RemoveCredentialCommand.Execute(null);

            viewModel.SelectedCredential = CreateEntry("other", "Other Provider");

            viewModel.IsRemoveConfirmPending.Should().BeFalse();
            viewModel.RemoveButtonText.Should().Be("Remove");
        });
    }

    private static CredentialManagementViewModel CreateViewModel()
    {
        return new CredentialManagementViewModel(
            new WpfServices.CredentialService(),
            WpfServices.NotificationService.Instance);
    }

    private static CredentialEntryViewModel CreateEntry(
        string providerId = "test-provider",
        string displayName = "Test Provider")
    {
        return new CredentialEntryViewModel
        {
            ProviderId = providerId,
            DisplayName = displayName,
            CredentialType = "API Key",
            HasCredentials = true,
            RequiresCredentials = true,
        };
    }
}
#endif
