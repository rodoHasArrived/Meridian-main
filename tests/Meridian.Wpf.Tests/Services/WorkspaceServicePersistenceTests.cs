using System.IO;
using System.Text.Json;
using Meridian.Ui.Services;
using Meridian.Wpf.Services;

namespace Meridian.Wpf.Tests.Services;

/// <summary>
/// The workspace file must be written outside the state gate. Synchronous members such as
/// <see cref="WorkspaceService.Workspaces"/> are read on the UI thread and wait on that gate, so a
/// write performed while holding it freezes the UI for as long as the disk takes.
/// </summary>
public sealed class WorkspaceServicePersistenceTests : IDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private readonly string _settingsFilePath = Path.Combine(
        Path.GetTempPath(),
        "Meridian.Wpf.Tests",
        "workspace-service-persistence-tests",
        $"{Guid.NewGuid():N}.workspace-data.json");

    public void Dispose()
    {
        WorkspaceService.SetSettingsFilePathOverrideForTests(null);
        WorkspaceService.SetWorkspaceStateFilePathOverrideForTests(null);
        var directory = Path.GetDirectoryName(_settingsFilePath);
        if (directory is not null && Directory.Exists(directory))
        {
            foreach (var file in Directory.GetFiles(directory, Path.GetFileName(_settingsFilePath) + "*"))
            {
                File.Delete(file);
            }
        }
    }

    private WorkspaceService CreateService()
    {
        var service = (WorkspaceService)Activator.CreateInstance(typeof(WorkspaceService), nonPublic: true)!;
        WorkspaceService.SetSettingsFilePathOverrideForTests(_settingsFilePath);
        WorkspaceService.SetWorkspaceStateFilePathOverrideForTests(_settingsFilePath + ".state.json");
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsFilePath)!);
        service.ResetForTests();
        service.LoadWorkspacesAsync().GetAwaiter().GetResult();
        return service;
    }

    /// <summary>Token-store I/O that reports when it is reached and then waits to be released.</summary>
    private sealed class HeldTokenStoreIo
    {
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Started => _started.Task;

        public void Release() => _release.TrySetResult();

        public Task WaitAsync()
        {
            _started.TrySetResult();
            return _release.Task;
        }
    }

    /// <summary>A session on a built-in workspace and one of its pages, whatever the catalog holds.</summary>
    private static SessionState SessionOn(WorkspaceService service, out string workspaceId, out string pageTag)
    {
        var workspace = service.Workspaces.First(candidate => candidate.IsBuiltIn && candidate.Pages.Count > 0);
        workspaceId = workspace.Id;
        pageTag = workspace.Pages[0].PageTag;
        return new SessionState { ActiveWorkspaceId = workspaceId, ActivePageTag = pageTag };
    }

    /// <summary>A write that reports when it starts and then stays open until released.</summary>
    private sealed class HeldWrite
    {
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public List<string> Written { get; } = [];

        public Task Started => _started.Task;

        public void Release() => _release.TrySetResult();

        public async Task WriteAsync(string path, string json)
        {
            _started.TrySetResult();
            await _release.Task.ConfigureAwait(false);
            lock (Written)
            {
                Written.Add(json);
            }
        }
    }

    [Fact]
    public async Task SynchronousReaders_AreNotBlockedWhileTheWorkspaceFileIsBeingWritten()
    {
        var service = CreateService();
        var write = new HeldWrite();
        service.PersistWriterForTests = write.WriteAsync;

        var create = service.CreateWorkspaceAsync("Desk A", "held write", WorkspaceCategory.Custom);
        await write.Started.WaitAsync(Patience);

        // The write is in flight. A UI-thread style synchronous read must return now, with the
        // new workspace already visible, rather than waiting for the disk.
        var read = Task.Run(() => service.Workspaces);
        var workspaces = await read.WaitAsync(Patience);
        workspaces.Should().Contain(workspace => workspace.Name == "Desk A");

        create.IsCompleted.Should().BeFalse("an awaited operation completes only once its state is saved");

        write.Release();
        (await create.WaitAsync(Patience)).Name.Should().Be("Desk A");
        write.Written.Should().ContainSingle().Which.Should().Contain("Desk A");
    }

    [Fact]
    public async Task OverlappingOperations_EndWithTheNewestStateWritten()
    {
        var service = CreateService();
        var write = new HeldWrite();
        service.PersistWriterForTests = write.WriteAsync;

        var first = service.CreateWorkspaceAsync("Desk A", "first", WorkspaceCategory.Custom);
        await write.Started.WaitAsync(Patience);

        // The second operation changes state while the first write is still open.
        var second = service.CreateWorkspaceAsync("Desk B", "second", WorkspaceCategory.Custom);
        await Task.Run(() => service.Workspaces.Should().Contain(workspace => workspace.Name == "Desk B"))
            .WaitAsync(Patience);

        write.Release();
        await Task.WhenAll(first, second).WaitAsync(Patience);

        write.Written.Should().NotBeEmpty();
        write.Written[^1].Should().Contain("Desk A").And.Contain("Desk B",
            "whatever order the writes finish in, the last one must carry the newest state");
    }

    [Fact]
    public async Task Save_WritesACompleteFileAndLeavesNoTemporaryFileBehind()
    {
        var service = CreateService();

        await service.CreateWorkspaceAsync("Desk A", "real write", WorkspaceCategory.Custom);

        File.Exists(_settingsFilePath).Should().BeTrue();
        File.Exists(_settingsFilePath + ".tmp").Should().BeFalse();
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(_settingsFilePath));
        document.RootElement.ValueKind.Should().Be(JsonValueKind.Object);
        (await File.ReadAllTextAsync(_settingsFilePath)).Should().Contain("Desk A");
    }

    [Fact]
    public async Task Save_WhenTheWriteFails_DoesNotFailTheOperationOrLoseInMemoryState()
    {
        var service = CreateService();
        service.PersistWriterForTests = static (_, _) => throw new IOException("disk full");

        var created = await service.CreateWorkspaceAsync("Desk A", "failing write", WorkspaceCategory.Custom);

        created.Name.Should().Be("Desk A");
        service.Workspaces.Should().Contain(workspace => workspace.Name == "Desk A");
    }

    [Fact]
    public async Task SynchronousReaders_AreNotBlockedWhileTheWorkspaceFileIsBeingRead()
    {
        var service = (WorkspaceService)Activator.CreateInstance(typeof(WorkspaceService), nonPublic: true)!;
        WorkspaceService.SetSettingsFilePathOverrideForTests(_settingsFilePath);
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsFilePath)!);
        service.ResetForTests();

        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        service.PersistReaderForTests = _ =>
        {
            started.TrySetResult();
            return release.Task;
        };

        var load = service.LoadWorkspacesAsync();
        await started.Task.WaitAsync(Patience);

        // The initial read is in flight. A synchronous read must return now (nothing is loaded
        // yet) instead of waiting for the disk.
        var workspaces = await Task.Run(() => service.Workspaces).WaitAsync(Patience);
        workspaces.Should().BeEmpty();
        load.IsCompleted.Should().BeFalse();

        release.SetResult(null);
        await load.WaitAsync(Patience);
        service.Workspaces.Should().NotBeEmpty("with no file, the load falls back to the built-in workspaces");
    }

    [Fact]
    public async Task Load_ReadsBackWhatAnEarlierInstanceSaved()
    {
        var first = CreateService();
        await first.CreateWorkspaceAsync("Desk A", "persisted", WorkspaceCategory.Custom);

        var second = (WorkspaceService)Activator.CreateInstance(typeof(WorkspaceService), nonPublic: true)!;
        second.ResetForTests();
        await second.LoadWorkspacesAsync();

        second.Workspaces.Should().Contain(workspace => workspace.Name == "Desk A");
    }

    [Fact]
    public async Task FirstOperation_LoadsThePersistedStateWithoutAnExplicitLoad()
    {
        var first = CreateService();
        await first.CreateWorkspaceAsync("Desk A", "persisted", WorkspaceCategory.Custom);

        // No LoadWorkspacesAsync: the first gated operation must initialize from the file itself.
        var second = (WorkspaceService)Activator.CreateInstance(typeof(WorkspaceService), nonPublic: true)!;
        second.ResetForTests();
        await second.CreateWorkspaceAsync("Desk B", "second instance", WorkspaceCategory.Custom);

        second.Workspaces.Select(workspace => workspace.Name).Should().Contain(["Desk A", "Desk B"]);
    }

    [Fact]
    public async Task SynchronousReaders_AreNotBlockedWhileAStateTokenIsBeingSaved()
    {
        var service = CreateService();
        var session = SessionOn(service, out _, out _);
        var storeIo = new HeldTokenStoreIo();
        service.SetStateTokenStoreIoHookForTests(storeIo.WaitAsync);

        var save = service.SaveSessionStateAsync(session, "alpha");
        await storeIo.Started.WaitAsync(Patience);

        // The token store write is in flight. The saved session must already be readable.
        var lastSession = await Task.Run(() => service.LastSession).WaitAsync(Patience);
        lastSession.Should().BeSameAs(session);
        save.IsCompleted.Should().BeFalse("an awaited save completes only once the token is stored");

        storeIo.Release();
        await save.WaitAsync(Patience);
    }

    [Fact]
    public async Task SynchronousReaders_AreNotBlockedWhileAStateTokenIsBeingRestored()
    {
        var service = CreateService();
        var session = SessionOn(service, out var workspaceId, out var pageTag);
        await service.SaveSessionStateAsync(session, "alpha");

        var storeIo = new HeldTokenStoreIo();
        service.SetStateTokenStoreIoHookForTests(storeIo.WaitAsync);

        var restore = service.RestoreWorkspaceStateTokenAsync(workspaceId, pageTag);
        await storeIo.Started.WaitAsync(Patience);

        // The token store read is in flight; a synchronous read must not wait for it.
        var workspaces = await Task.Run(() => service.Workspaces).WaitAsync(Patience);
        workspaces.Should().NotBeEmpty();
        restore.IsCompleted.Should().BeFalse();

        storeIo.Release();
        var restored = await restore.WaitAsync(Patience);
        restored.Should().NotBeNull();
        restored!.Token.WorkspaceId.Should().Be(workspaceId);
        restored.Token.PageTag.Should().Be(pageTag);
    }
}
