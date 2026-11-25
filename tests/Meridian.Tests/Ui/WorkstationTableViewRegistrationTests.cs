using System.Text.RegularExpressions;
using FluentAssertions;
using Meridian.Identity.Auth;
using Meridian.Ui.Shared.Endpoints;
using Meridian.Ui.Shared.Services;

namespace Meridian.Tests.Ui;

/// <summary>
/// Ratchet: every table the browser workstation offers "Save view" on must be recognised by the
/// host's table-view permission map (or the financial-record explorer allow-list), so the control
/// never fails with a 403 for a table the operator can already read.
/// </summary>
public sealed class WorkstationTableViewRegistrationTests
{
    // Tables deliberately offered without host-backed saved views. Add an entry with a reason.
    private static readonly HashSet<string> OptedOut = new(StringComparer.Ordinal);

    private static readonly Regex WorkbenchIdExpression = new(@"workbenchId=(?:""([^""]*)""|\{([^}]*)\})", RegexOptions.Compiled);
    private static readonly Regex QuotedTableId = new(@"""([a-z0-9]+(?:-[a-z0-9]+)+|[a-z0-9]{2,})""", RegexOptions.Compiled);

    [Fact]
    public void EveryDashboardWorkbenchId_IsRegisteredOnTheHostOrOptedOut()
    {
        var dashboardSource = Path.Combine(FindRepositoryRoot(), "src", "Meridian.Ui", "dashboard", "src");
        var unregistered = new SortedSet<string>(StringComparer.Ordinal);
        var found = 0;

        foreach (var file in Directory.EnumerateFiles(dashboardSource, "*.tsx", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(dashboardSource, file).Replace('\\', '/');
            if (relative.Contains(".test.", StringComparison.Ordinal) || relative.StartsWith("test/", StringComparison.Ordinal) ||
                relative.StartsWith("dev/", StringComparison.Ordinal))
                continue;

            foreach (Match match in WorkbenchIdExpression.Matches(File.ReadAllText(file)))
            {
                var ids = match.Groups[1].Success
                    ? [match.Groups[1].Value]
                    : QuotedTableId.Matches(match.Groups[2].Value).Select(literal => literal.Groups[1].Value).ToArray();
                foreach (var id in ids)
                {
                    found++;
                    if (!WorkstationEndpoints.WorkstationTablePermissions.ContainsKey(id) &&
                        !FinancialRecordExplorerReadService.IsKnownExplorerId(id) && !OptedOut.Contains(id))
                        unregistered.Add($"{id} ({relative})");
                }
            }
        }

        found.Should().BeGreaterThan(50, "the scan should see the dashboard's table workbenches");
        unregistered.Should().BeEmpty(
            "each workbenchId needs a WorkstationTablePermissions entry mirroring its data endpoint's permission, or an explicit opt-out");
    }

    [Theory]
    [InlineData("execution-journal")]
    [InlineData("portfolio-nav-comparison")]
    [InlineData("portfolio-cash-ladder-comparison")]
    [InlineData("portfolio-attribution-period-comparison")]
    [InlineData("accounting-period-closing-balance-comparison")]
    public void PreviouslyMissingTables_AreRegistered(string tableId)
        => WorkstationEndpoints.WorkstationTablePermissions.Should().ContainKey(tableId);

    [Fact]
    public void IncomeAndInstrumentSavedViews_UseThePermissionsOfTheirDataEndpoints()
    {
        var permissions = WorkstationEndpoints.WorkstationTablePermissions;
        permissions["book-income-receipts"].Should().BeEquivalentTo(new[]
        {
            UserPermission.ViewLedgerReports, UserPermission.ManageLedgerReports, UserPermission.AdminMaintenance
        });
        permissions["instrument-dossier-ledger"].Should().BeEquivalentTo(new[]
        {
            UserPermission.AdminMaintenance, UserPermission.ManageDirectLending, UserPermission.ViewLedgerReports, UserPermission.ManageLedgerReports
        });
        permissions["instrument-dossier-positions"].Should().Equal(UserPermission.ViewTrades);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "global.json")))
            root = root.Parent;
        root.Should().NotBeNull("the dashboard source belongs to this repository");
        return root!.FullName;
    }
}
