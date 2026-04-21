#if WINDOWS
using System.IO;
using Meridian.Ui.Services;
using Meridian.Wpf.Services;
using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed class WorkspacePageViewModelTests : IDisposable
{
    private static string CreateTestSettingsFilePath()
        => Path.Combine(
            Path.GetTempPath(),
            "Meridian.Wpf.Tests",
            "workspace-vm-tests",
            $"{Guid.NewGuid():N}.workspace-data.json");

    private static async Task<WorkspaceService> CreateServiceAsync()
    {
        var settingsFilePath = CreateTestSettingsFilePath();
        var service = (WorkspaceService)Activator.CreateInstance(typeof(WorkspaceService), nonPublic: true)!;
        WorkspaceService.SetSettingsFilePathOverrideForTests(settingsFilePath);
        Directory.CreateDirectory(Path.GetDirectoryName(settingsFilePath)!);
        if (File.Exists(settingsFilePath))
        {
            File.Delete(settingsFilePath);
        }

        service.ResetForTests();
        await service.LoadWorkspacesAsync();
        return service;
    }

    private static WorkspacePageViewModel CreateViewModel(WorkspaceService svc)
    {
        return new WorkspacePageViewModel(
            svc,
            Meridian.Wpf.Services.NotificationService.Instance,
            Meridian.Wpf.Services.LoggingService.Instance);
    }

    public void Dispose()
    {
        WorkspaceService.SetSettingsFilePathOverrideForTests(null);
    }

    [Fact]
    public async Task LoadAsync_PopulatesWorkspaces_FromService()
    {
        var service = await CreateServiceAsync();

        WpfTestThread.Run(async () =>
        {
            using var viewModel = CreateViewModel(service);

            await viewModel.LoadAsync();

            viewModel.Workspaces.Count.Should().Be(service.Workspaces.Count);
        });
    }

    [Fact]
    public async Task LoadAsync_SetsActiveWorkspaceName_WhenActiveExists()
    {
        var service = await CreateServiceAsync();
        var workspace = service.Workspaces.First(item => item.Id == "research");
        await service.ActivateWorkspaceAsync(workspace.Id);

        WpfTestThread.Run(async () =>
        {
            using var viewModel = CreateViewModel(service);
            await viewModel.LoadAsync();

            viewModel.ActiveWorkspaceName.Should().Be(workspace.Name);
        });
    }

    [Fact]
    public async Task LoadAsync_WhenNoActiveWorkspace_SetsNoneText()
    {
        var service = await CreateServiceAsync();

        WpfTestThread.Run(async () =>
        {
            using var viewModel = CreateViewModel(service);

            await viewModel.LoadAsync();

            viewModel.ActiveWorkspaceName.Should().Be("None");
        });
    }

    [Fact]
    public async Task CreateWorkspaceCommand_WithEmptyName_SetsValidationError()
    {
        var service = await CreateServiceAsync();

        WpfTestThread.Run(async () =>
        {
            using var viewModel = CreateViewModel(service);
            await viewModel.LoadAsync();

            await viewModel.CreateWorkspaceCommand.ExecuteAsync(null);

            viewModel.HasNewNameError.Should().BeTrue();
            viewModel.NewNameError.Should().Be("Name is required");
        });
    }

    [Fact]
    public async Task CreateWorkspaceCommand_WithEmptyName_DoesNotCallService()
    {
        var service = await CreateServiceAsync();

        WpfTestThread.Run(async () =>
        {
            using var viewModel = CreateViewModel(service);
            await viewModel.LoadAsync();
            var initialCount = service.Workspaces.Count;

            await viewModel.CreateWorkspaceCommand.ExecuteAsync(null);

            service.Workspaces.Count.Should().Be(initialCount);
        });
    }

    [Fact]
    public async Task CreateWorkspaceCommand_WithValidName_CreatesWorkspace_AndClearsForm()
    {
        var service = await CreateServiceAsync();

        WpfTestThread.Run(async () =>
        {
            using var viewModel = CreateViewModel(service);
            await viewModel.LoadAsync();
            var initialCount = viewModel.Workspaces.Count;

            viewModel.NewName = "VM Workspace";
            viewModel.NewDescription = "Created from test";
            viewModel.NewCategoryIndex = 4;

            await viewModel.CreateWorkspaceCommand.ExecuteAsync(null);

            viewModel.Workspaces.Count.Should().Be(initialCount + 1);
            viewModel.NewName.Should().BeEmpty();
            viewModel.NewDescription.Should().BeEmpty();
            viewModel.HasNewNameError.Should().BeFalse();
        });
    }

    [Fact]
    public async Task DeleteWorkspaceCommand_BuiltInWorkspace_IsNotExecutable()
    {
        var service = await CreateServiceAsync();

        WpfTestThread.Run(async () =>
        {
            using var viewModel = CreateViewModel(service);
            await viewModel.LoadAsync();
            var builtIn = viewModel.Workspaces.First(item => !item.CanDelete);

            builtIn.CanDelete.Should().BeFalse();
            viewModel.DeleteWorkspaceCommand.CanExecute(builtIn.Id).Should().BeFalse();
        });
    }

    [Fact]
    public async Task DeleteWorkspaceCommand_UserWorkspace_RemovesFromList()
    {
        var service = await CreateServiceAsync();
        var workspace = await service.CreateWorkspaceAsync("Delete Me", "temp", WorkspaceCategory.Custom);

        WpfTestThread.Run(async () =>
        {
            using var viewModel = CreateViewModel(service);
            await viewModel.LoadAsync();

            await viewModel.DeleteWorkspaceCommand.ExecuteAsync(workspace.Id);

            viewModel.Workspaces.Should().NotContain(item => item.Id == workspace.Id);
        });
    }

    [Fact]
    public async Task ActivateWorkspaceCommand_SetsActiveWorkspaceName()
    {
        var service = await CreateServiceAsync();

        WpfTestThread.Run(async () =>
        {
            using var viewModel = CreateViewModel(service);
            await viewModel.LoadAsync();
            var target = viewModel.Workspaces.Last();

            await viewModel.ActivateWorkspaceCommand.ExecuteAsync(target.Id);

            viewModel.ActiveWorkspaceName.Should().Be(target.Name);
        });
    }
}
#endif
