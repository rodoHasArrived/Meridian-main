using System.Net.Http;
using Meridian.Contracts.Configuration;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed class ConfigurationChangeStatusViewModelTests
{
    [Fact]
    public async Task ReconnectWithoutRuntimeRecycle_ShowsPendingHostRestart()
    {
        var status = new ConfigurationChangeStatus(ConfigurationChangeImpact.ReconnectProvider,
            [new("dataSource", ConfigurationChangeImpact.ReconnectProvider)],
            [new("dataSource", ConfigurationChangeImpact.ReconnectProvider)], DateTimeOffset.UtcNow);
        var model = new ConfigurationChangeStatusViewModel(_ => Task.FromResult<ConfigurationChangeStatus?>(status));

        await model.RefreshAsync(hasLocalSave: false);

        model.Title.Should().Be("Pending host restart");
        model.Detail.Should().Contain("reconnecting alone does not rebuild providers");
        model.Paths.Should().Contain("dataSource: ReconnectProvider");
    }

    [Fact]
    public async Task LocalDiskSave_IsNeverPresentedAsConfirmedAppliedByEmptyHostStatus()
    {
        var status = new ConfigurationChangeStatus(ConfigurationChangeImpact.Live, [], [], null);
        var model = new ConfigurationChangeStatusViewModel(_ => Task.FromResult<ConfigurationChangeStatus?>(status));

        await model.RefreshAsync(hasLocalSave: true);

        model.Title.Should().Be("Local save awaits host verification");
        model.Detail.Should().Contain("does not verify that local save");
    }

    [Fact]
    public async Task FailedRead_ClearsOldPendingRows_AndReportsUnknownActivation()
    {
        var available = true;
        var model = new ConfigurationChangeStatusViewModel(_ => available
            ? Task.FromResult<ConfigurationChangeStatus?>(new(ConfigurationChangeImpact.RestartHost,
                [], [new("backfill.providers.polygon.priority", ConfigurationChangeImpact.RestartHost)], null))
            : Task.FromException<ConfigurationChangeStatus?>(new HttpRequestException("unavailable")));
        await model.RefreshAsync(hasLocalSave: false);
        available = false;

        await model.RefreshAsync(hasLocalSave: false);

        model.Title.Should().Be("Setting activation unavailable");
        model.Paths.Should().BeEmpty();
        model.Detail.Should().Contain("applied status is unknown");
    }

    [Fact]
    public async Task CancelledPageLifetime_DiscardsLateHostResponse()
    {
        var delayed = new TaskCompletionSource<ConfigurationChangeStatus?>();
        var model = new ConfigurationChangeStatusViewModel(_ => delayed.Task);
        using var lifetime = new CancellationTokenSource();
        var refresh = model.RefreshAsync(hasLocalSave: false, lifetime.Token);
        lifetime.Cancel();
        model.CancelPending();
        delayed.SetResult(new(ConfigurationChangeImpact.RestartHost, [],
            [new("dataSources", ConfigurationChangeImpact.RestartHost)], null));

        await refresh;

        model.Title.Should().Be("Setting activation not checked");
        model.Paths.Should().BeEmpty();
        model.IsBusy.Should().BeFalse();
    }

    [Fact]
    public async Task SupersededResponse_CannotReplaceTheNewerHostState()
    {
        var delayed = new TaskCompletionSource<ConfigurationChangeStatus?>();
        var calls = 0;
        var model = new ConfigurationChangeStatusViewModel(_ => ++calls == 1 ? delayed.Task :
            Task.FromResult<ConfigurationChangeStatus?>(new(ConfigurationChangeImpact.Live, [], [], null)));
        var first = model.RefreshAsync(hasLocalSave: false);
        await model.RefreshAsync(hasLocalSave: false);
        delayed.SetResult(new(ConfigurationChangeImpact.RestartHost, [],
            [new("backfill", ConfigurationChangeImpact.RestartHost)], null));

        await first;

        model.Title.Should().Be("No host-reported pending settings");
        model.Paths.Should().BeEmpty();
    }
}
