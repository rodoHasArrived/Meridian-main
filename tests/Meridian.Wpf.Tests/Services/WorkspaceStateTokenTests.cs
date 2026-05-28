using System.Text.Json;
using Meridian.Ui.Services;
using Meridian.Wpf.Models;
using Meridian.Wpf.Services;

namespace Meridian.Wpf.Tests.Services;

public sealed class WorkspaceStateTokenTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "Meridian.Wpf.Tests",
        "workspace-state-token-tests",
        Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        WorkspaceService.SetSettingsFilePathOverrideForTests(null);
        WorkspaceService.SetWorkspaceStateFilePathOverrideForTests(null);
        WorkspaceStateTokenStore.SetFilePathOverrideForTests(null);
    }

    [Fact]
    public void DefaultFilePath_ShouldUseLocalAppDataMeridianWorkspaceState()
    {
        var path = WorkspaceStateTokenStore.GetDefaultFilePath();

        path.Should().EndWith(Path.Combine("Meridian", "workspace-state.json"));
        path.Should().Contain(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
    }

    [Fact]
    public void CreateToken_ShouldCaptureTradingAndGovernanceCriticalState()
    {
        var session = CreateAccountingSession();

        IWorkspaceStateToken token = WorkspaceStateTokenStore.CreateToken("accounting", session);

        token.Version.Should().Be(WorkspaceStateTokenVersions.Current);
        token.WorkspaceId.Should().Be("accounting");
        token.PageTag.Should().Be("FundLedger");
        token.OperatingContextKey.Should().Be("fund:alpha");
        token.SelectedFundProfileId.Should().Be("alpha");
        token.SelectedAccountId.Should().Be("account-1");
        token.ActiveWorkItemId.Should().Be("work-item-1");
        token.SelectedReconciliationBreakId.Should().Be("break-1");
        token.SelectedReconciliationCaseId.Should().Be("case-1");
        token.SelectedRunId.Should().Be("run-1");
        token.PaneLayout.Should().NotBeNull();
        token.PaneLayout!.ActivePaneId.Should().Be("ledger-pane");
        token.StateValues.Should().ContainKey("SelectedSymbol").WhoseValue.Should().Be("AAPL");
    }

    [Fact]
    public async Task SaveAndRestoreAsync_ShouldPersistTokenToWorkspaceStateFile()
    {
        var path = ConfigureTokenStorePath();
        var store = new WorkspaceStateTokenStore();
        var token = WorkspaceStateTokenStore.CreateToken("accounting", CreateAccountingSession());

        await store.SaveAsync(token);
        var restored = await store.RestoreAsync(
            "accounting",
            "FundLedger",
            new WorkspaceStateRestoreValidation
            {
                ExistingFundProfileIds = Set("alpha"),
                ExistingAccountIds = Set("account-1"),
                OpenWorkItemIds = Set("work-item-1"),
                OpenReconciliationBreakIds = Set("break-1"),
                OpenReconciliationCaseIds = Set("case-1"),
                ExistingRunIds = Set("run-1"),
                ExistingSymbols = Set("AAPL")
            });

        File.Exists(path).Should().BeTrue();
        restored.Should().NotBeNull();
        restored!.IsRecoveryState.Should().BeFalse();
        restored.Token.SelectedAccountId.Should().Be("account-1");
        restored.Token.PaneLayout!.Panes.Should().ContainSingle(pane => pane.PaneId == "ledger-pane");
    }

    [Theory]
    [InlineData("SelectedSessionId", "missing-session")]
    [InlineData("SelectedSymbol", "deleted-symbol")]
    [InlineData("SelectedReconciliationCaseId", "closed-reconciliation-case")]
    public async Task RestoreAsync_WithStaleReferences_ShouldReturnClearRecoveryState(
        string staleField,
        string expectedReason)
    {
        ConfigureTokenStorePath();
        var store = new WorkspaceStateTokenStore();
        var session = CreateAccountingSession();
        session.ActiveFilters["SelectedSessionId"] = "session-1";
        var token = WorkspaceStateTokenStore.CreateToken("accounting", session);

        await store.SaveAsync(token);

        var restored = await store.RestoreAsync(
            "accounting",
            "FundLedger",
            BuildValidationMissing(staleField));

        restored.Should().NotBeNull();
        restored!.IsRecoveryState.Should().BeTrue();
        restored.RecoveryReason.Should().Be(expectedReason);
        restored.Token.SelectedAccountId.Should().BeNull();
        restored.Token.SelectedReconciliationCaseId.Should().BeNull();
        restored.Token.PaneLayout.Should().BeNull();
        restored.Token.StateValues.Should().ContainKey("RecoveryReason").WhoseValue.Should().Be(expectedReason);
    }

    [Fact]
    public async Task WorkspaceService_SaveSessionStateAsync_ShouldPersistRestartStyleToken()
    {
        var settingsPath = Path.Combine(_root, "workspace-data.json");
        ConfigureTokenStorePath();
        var service = CreateWorkspaceService(settingsPath);
        await service.ActivateWorkspaceAsync("accounting");

        await service.SaveSessionStateAsync(CreateAccountingSession(), "alpha");

        var restored = await service.RestoreWorkspaceStateTokenAsync(
            "accounting",
            "FundLedger",
            new WorkspaceStateRestoreValidation
            {
                ExistingFundProfileIds = Set("alpha"),
                ExistingAccountIds = Set("account-1"),
                OpenWorkItemIds = Set("work-item-1"),
                OpenReconciliationBreakIds = Set("break-1"),
                OpenReconciliationCaseIds = Set("case-1"),
                ExistingRunIds = Set("run-1"),
                ExistingSymbols = Set("AAPL")
            });

        restored.Should().NotBeNull();
        restored!.IsRecoveryState.Should().BeFalse();
        restored.Token.SelectedFundProfileId.Should().Be("alpha");
        restored.Token.PageTag.Should().Be("FundLedger");
    }

    [Fact]
    public async Task RestoreAsync_WithVersionMismatch_ShouldReturnRecoveryState()
    {
        var path = ConfigureTokenStorePath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new
        {
            Version = WorkspaceStateTokenVersions.Current,
            SavedAt = DateTimeOffset.UtcNow,
            Tokens = new[]
            {
                new
                {
                    Version = 999,
                    WorkspaceId = "trading",
                    PageTag = "TradingShell",
                    CapturedAt = DateTimeOffset.UtcNow,
                    SelectedRunId = "run-1",
                    StateValues = new Dictionary<string, string>()
                }
            }
        }));

        var restored = await new WorkspaceStateTokenStore().RestoreAsync("trading", "TradingShell");

        restored.Should().NotBeNull();
        restored!.IsRecoveryState.Should().BeTrue();
        restored.RecoveryReason.Should().Be("version-mismatch");
        restored.Token.SelectedRunId.Should().BeNull();
    }

    private string ConfigureTokenStorePath()
    {
        var path = Path.Combine(_root, "workspace-state.json");
        WorkspaceStateTokenStore.SetFilePathOverrideForTests(path);
        WorkspaceService.SetWorkspaceStateFilePathOverrideForTests(path);
        return path;
    }

    private static WorkspaceService CreateWorkspaceService(string settingsFilePath)
    {
        WorkspaceService.SetSettingsFilePathOverrideForTests(settingsFilePath);
        var service = (WorkspaceService)Activator.CreateInstance(typeof(WorkspaceService), nonPublic: true)!;
        service.ResetForTests();
        service.LoadWorkspacesAsync().GetAwaiter().GetResult();
        return service;
    }

    private static SessionState CreateAccountingSession()
        => new()
        {
            ActiveWorkspaceId = "accounting",
            ActivePageTag = "FundLedger",
            WorkspaceContext =
            {
                ["OperatingContextKey"] = "fund:alpha",
                ["SelectedFundProfileId"] = "alpha"
            },
            ActiveFilters =
            {
                ["SelectedAccountId"] = "account-1",
                ["ActiveWorkItemId"] = "work-item-1",
                ["SelectedReconciliationBreakId"] = "break-1",
                ["SelectedReconciliationCaseId"] = "case-1",
                ["SelectedRunId"] = "run-1",
                ["SelectedSymbol"] = "AAPL"
            },
            WorkstationLayout = new WorkstationLayoutState
            {
                LayoutId = "accounting-ledger",
                ActivePaneId = "ledger-pane",
                OperatingContextKey = "fund:alpha",
                Panes =
                {
                    new WorkstationPaneState
                    {
                        PaneId = "ledger-pane",
                        PageTag = "FundLedger",
                        Title = "Fund Ledger",
                        IsActive = true
                    }
                }
            }
        };

    private static WorkspaceStateRestoreValidation BuildValidationMissing(string staleField)
        => new()
        {
            ExistingSessionIds = staleField == "SelectedSessionId" ? Set("other-session") : Set("session-1"),
            ExistingSymbols = staleField == "SelectedSymbol" ? Set("MSFT") : Set("AAPL"),
            ExistingFundProfileIds = Set("alpha"),
            ExistingAccountIds = Set("account-1"),
            OpenWorkItemIds = Set("work-item-1"),
            OpenReconciliationBreakIds = Set("break-1"),
            OpenReconciliationCaseIds = staleField == "SelectedReconciliationCaseId" ? Set("case-2") : Set("case-1"),
            ExistingRunIds = Set("run-1")
        };

    private static IReadOnlySet<string> Set(params string[] values)
        => new HashSet<string>(values, StringComparer.OrdinalIgnoreCase);
}
