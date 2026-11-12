using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Tests.Ui;

public sealed class WorkstationPersonalTableViewStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-personal-views-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task PersonalViews_SurviveRestart_WithoutCrossingOperatorOrTenantBoundaries()
    {
        var store = NewStore();
        var saved = await store.SavePersonalViewAsync("TENANT", "alice", "portfolio-positions",
            new WorkstationTableViewSaveRequest("My positions", "AAPL", ["symbol", "quantity"], "compact"));
        var restarted = NewStore();
        (await restarted.LoadPersonalViewsAsync("tenant", "alice", "portfolio-positions")).Should().ContainSingle(view => view.ViewId == saved.ViewId && !view.IsReadOnly);
        (await restarted.LoadPersonalViewsAsync("tenant", "bob", "portfolio-positions")).Should().BeEmpty();
        (await restarted.LoadPersonalViewsAsync("other", "alice", "portfolio-positions")).Should().BeEmpty();
        (await restarted.LoadPersonalViewsAsync("tenant", "alice", "strategy-runs")).Should().BeEmpty();
    }

    [Fact]
    public async Task LegacyViews_RemainReadOnlyAndOwnerless_WhenPersonalViewsAreAdded()
    {
        Directory.CreateDirectory(Path.Combine(_root, "explorers"));
        await File.WriteAllTextAsync(Path.Combine(_root, "explorers", "financial-record-explorer-saved-views.json"), """
            {"version":1,"views":[{"tenantId":"tenant","explorerId":"ledger","savedView":{"viewId":"old","label":"Original","description":"","isSystem":false,"isActive":false,"filters":[],"searchText":"cash","columnIds":["account"]}}]}
            """);
        var store = NewStore();
        var legacy = (await store.LoadPersonalViewsAsync("tenant", "alice", "ledger")).Single();
        legacy.IsReadOnly.Should().BeTrue();
        await store.SavePersonalViewAsync("tenant", "alice", "ledger", new("My copy", legacy.SearchText, legacy.ColumnIds, "compact"));
        var bobViews = await NewStore().LoadPersonalViewsAsync("tenant", "bob", "ledger");
        bobViews.Should().ContainSingle(view => view.ViewId == "old" && view.IsReadOnly);
        (await NewStore().LoadPersonalViewsAsync("tenant", "alice", "ledger")).Should().HaveCount(2);
    }

    [Fact]
    public async Task InvalidPresentationSettings_AreRejectedBeforePersistence()
    {
        var action = () => NewStore().SavePersonalViewAsync("tenant", "alice", "ledger", new("View", "", [], "compact"));
        await action.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task IndependentWriters_RetainEveryPersonalView()
    {
        var stores = new[] { NewStore(), NewStore() };
        await Task.WhenAll(Enumerable.Range(0, 12).Select(index => stores[index % stores.Length].SavePersonalViewAsync(
            "tenant", "alice", "ledger", new($"View {index}", "", ["account"], "compact"))));
        (await NewStore().LoadPersonalViewsAsync("tenant", "alice", "ledger")).Should().HaveCount(12);
    }

    private FileFinancialRecordExplorerSavedViewStore NewStore() => new(_root, NullLogger<FileFinancialRecordExplorerSavedViewStore>.Instance);
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
