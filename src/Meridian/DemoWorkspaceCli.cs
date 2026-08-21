using System.Text.Json.Nodes;
using Meridian.Application.Commands;
using Meridian.Application.Composition.Startup;
using Meridian.Contracts.Configuration;
using Meridian.Identity.Auth;
using Meridian.Ui.Shared.Services;
using ConfigStore = Meridian.Application.UI.ConfigStore;

namespace Meridian;

/// <summary>
/// Host-level entry point for the one-command demo experience: <c>--seed-demo</c> provisions an
/// isolated, durable, Seeded-labelled demo workspace and (unless <c>--seed-only</c>) immediately
/// serves it; <c>--reset-demo</c> tears it down; <c>--demo</c> re-serves an already-seeded workspace.
/// </summary>
/// <remarks>
/// This lives in the host because only the host composition can reach both the shared seeder
/// (<see cref="DemoWorkspaceSeeder"/>, in <c>Meridian.Ui.Shared</c>) and the startup bootstrapper.
/// The seeded and served roots converge through a generated demo config whose <c>dataRoot</c> points
/// at the isolated demo root, so the same durable stores the seeder wrote are the ones the workstation
/// reads. No production data root is ever touched.
/// </remarks>
internal static class DemoWorkspaceCli
{
    private const string DemoConfigFileName = "appsettings.demo.json";

    /// <summary>True when the args (or the MERIDIAN_DEMO env var) select a demo verb this class owns.</summary>
    public static bool Handles(string[] args)
        => DemoWorkspaceLayout.HasSeedDemoFlag(args)
        || DemoWorkspaceLayout.HasResetDemoFlag(args)
        || DemoWorkspaceLayout.IsDemoModeRequested(args);

    public static async Task<int> RunAsync(
        string[] args,
        DashboardServerFactory dashboardServerFactory,
        CancellationToken ct = default)
    {
        string baseDataRoot;
        int demoPort;
        try
        {
            var parsed = CliArguments.Parse(args);
            var configStore = new ConfigStore(parsed.ConfigPath);
            baseDataRoot = configStore.GetDataRoot(configStore.Load());
            demoPort = parsed.HttpPort ?? 8080;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Console.Error.WriteLine($"Could not resolve the Meridian data root: {ex.Message}");
            return 3;
        }

        var seeder = new DemoWorkspaceSeeder(baseDataRoot);

        if (DemoWorkspaceLayout.HasResetDemoFlag(args))
        {
            return await ResetAsync(seeder, ct).ConfigureAwait(false);
        }

        if (DemoWorkspaceLayout.HasSeedDemoFlag(args))
        {
            var seedExit = await SeedAsync(seeder, ct).ConfigureAwait(false);
            if (seedExit != 0)
            {
                return seedExit;
            }

            if (DemoWorkspaceLayout.HasSeedOnlyFlag(args))
            {
                // A seeded workspace has to be re-openable with --demo, and --demo gates on the
                // demo config rather than on the workspace itself. Returning here before writing
                // it left `--seed-demo --seed-only` followed by `--demo` - the sequence the start
                // guide documents - telling the operator to seed a workspace that is already
                // seeded. The serving paths below rewrite this config with their own port.
                WriteDemoConfig(seeder.DemoRoot, demoPort);
                return 0;
            }

            var demoConfig = WriteDemoConfig(seeder.DemoRoot, demoPort);
            PrepareDemoRuntimeEnvironment();
            Console.WriteLine("Starting the browser workstation on the seeded demo workspace...");
            return await SharedStartupBootstrapper.RunAsync(
                BuildServeArgs(args, demoConfig),
                dashboardServerFactory,
                ct).ConfigureAwait(false);
        }

        // Bare --demo / MERIDIAN_DEMO: serve an already-seeded workspace.
        if (!File.Exists(ResolveDemoConfigPath(seeder.DemoRoot)))
        {
            Console.Error.WriteLine(
                $"No seeded demo workspace found at {seeder.DemoRoot}. Run 'dotnet run --project src/Meridian -- --seed-demo' first.");
            return 3;
        }

        // Rewrite existing generated configs so workspaces seeded before loopback enforcement also
        // cannot inherit a remote host binding from their environment.
        var existingConfig = WriteDemoConfig(seeder.DemoRoot, demoPort);
        PrepareDemoRuntimeEnvironment();
        return await SharedStartupBootstrapper.RunAsync(
            BuildServeArgs(args, existingConfig),
            dashboardServerFactory,
            ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Makes the demo start cleanly on a fresh checkout with no database. When the operator has not
    /// configured PostgreSQL persistence (via MERIDIAN_DATABASE_URL), the governed workstation would
    /// otherwise fail closed at startup; the demo opts into the local in-memory governance profile and
    /// an optional-auth, non-production environment. Every value is only set when unset, so an operator
    /// who has configured a real database (for a fully durable demo) or auth mode is never overridden.
    /// </summary>
    private static void PrepareDemoRuntimeEnvironment()
    {
        // Mark this process as the demo serving host so composition (W9-TRUTH-001) pins the seeded
        // provenance label without re-parsing CLI verbs. Set unconditionally rather than only when
        // unset: this method runs on the demo serve paths and nowhere else, so the process is the
        // demo host as a matter of fact. An inherited MERIDIAN_DEMO=false would otherwise outrank an
        // explicit --demo or --seed-demo, and readers that consult the marker without the argument
        // vector -- the provenance label, and the login middleware's seeded tenant fallback -- would
        // conclude the demo is not running while it is, leaving the workstation refused for want of
        // a tenant.
        Environment.SetEnvironmentVariable(DemoWorkspaceLayout.DemoModeEnvironmentVariable, "true");

        if (IsUnset("MERIDIAN_DATABASE_URL") && IsUnset("MERIDIAN_USE_INMEMORY_GOVERNANCE"))
        {
            Environment.SetEnvironmentVariable("MERIDIAN_USE_INMEMORY_GOVERNANCE", "true");
        }

        // In-memory governance is forbidden in a Production environment, which is the default when no
        // environment is named, so name a non-production one for the demo when the operator has not.
        if (IsUnset("DOTNET_ENVIRONMENT") && IsUnset("ASPNETCORE_ENVIRONMENT"))
        {
            Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", "Development");
        }

        if (IsUnset("MDC_AUTH_MODE"))
        {
            Environment.SetEnvironmentVariable("MDC_AUTH_MODE", "optional");
        }

        // Optional authentication leaves the caller without an authorization context, and the governed
        // surface refuses a caller it cannot authorize -- correct by default, but it would leave the
        // demo unable to open its own workstation. The demo is a single-operator local box with no
        // accounts, so it names the role that operator carries. This is the one place that opts in:
        // an ordinary optional-auth deployment stays refused unless it makes the same explicit choice.
        if (IsUnset("MDC_ANONYMOUS_ROLE"))
        {
            Environment.SetEnvironmentVariable("MDC_ANONYMOUS_ROLE", nameof(UserRole.Admin));
        }
    }

    private static bool IsUnset(string variable)
        => string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(variable));

    private static async Task<int> SeedAsync(DemoWorkspaceSeeder seeder, CancellationToken ct)
    {
        try
        {
            var report = await seeder.SeedAsync(ct).ConfigureAwait(false);
            Console.WriteLine($"Seeded Meridian demo workspace ({report.Provenance}) at:");
            Console.WriteLine($"  {report.DemoRoot}");
            Console.WriteLine(
                $"  reconciliation casework: {(report.Provisioning.ReconciliationLoaded ? "loaded" : "unavailable")} ({report.Provisioning.ReconciliationBreaksSeeded} new)");
            Console.WriteLine(
                $"  paper strategy run:      {(report.Provisioning.StrategyRunLoaded ? "loaded" : "unavailable")}");
            Console.WriteLine(
                $"  fund account:            {(report.Provisioning.FundAccountLoaded ? "loaded" : "unavailable")}");
            Console.WriteLine(
                $"  portfolio positions:     {(report.Provisioning.PortfolioPositionsLoaded ? "loaded" : "unavailable")}");
            Console.WriteLine(
                $"  journal drafts:          {report.Provisioning.JournalDraftsSeeded} retained");
            Console.WriteLine(
                $"  report pack:             {(report.Provisioning.ReportPackLoaded ? "loaded" : "unavailable")}");
            Console.WriteLine(
                $"  market history:          {report.MarketHistoryTradePrintsSeeded} seeded trade prints");
            foreach (var warning in report.Provisioning.Warnings)
            {
                Console.WriteLine($"  warning: {warning}");
            }

            return 0;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Console.Error.WriteLine($"Failed to seed the demo workspace: {ex.Message}");
            return 1;
        }
    }

    private static async Task<int> ResetAsync(DemoWorkspaceSeeder seeder, CancellationToken ct)
    {
        try
        {
            var report = await seeder.ResetAsync(ct).ConfigureAwait(false);
            Console.WriteLine(report.Deleted
                ? $"Removed the demo workspace at {report.DemoRoot}."
                : $"No demo workspace to remove at {report.DemoRoot}.");
            return 0;
        }
        catch (DemoWorkspaceIsolationException ex)
        {
            Console.Error.WriteLine($"Refusing to reset the demo workspace: {ex.Message}");
            return 2;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Console.Error.WriteLine($"Failed to reset the demo workspace: {ex.Message}");
            return 1;
        }
    }

    private static string ResolveDemoConfigPath(string demoRoot)
        => Path.Combine(demoRoot, DemoConfigFileName);

    /// <summary>
    /// Writes a minimal demo config whose absolute <c>dataRoot</c> is the isolated demo root, so a
    /// workstation host started with <c>--config &lt;this&gt;</c> reads exactly the seeded stores.
    /// </summary>
    internal static string WriteDemoConfig(string demoRoot, int port = 8080)
    {
        var configPath = ResolveDemoConfigPath(demoRoot);
        var config = new JsonObject
        {
            ["dataRoot"] = demoRoot,
            // Demo mode deliberately binds only to loopback. This prevents inherited host URL
            // environment variables from exposing the optional-auth demo workstation remotely.
            ["ApiHost"] = new JsonObject
            {
                ["DeploymentMode"] = "LocalWorkstation",
                ["Urls"] = new JsonArray($"http://localhost:{port}"),
            },
        };

        File.WriteAllText(configPath, config.ToJsonString());
        return configPath;
    }

    /// <summary>
    /// Rebuilds the argument vector for the serve step: drops the demo verbs, forces the generated
    /// demo config, and defaults to the browser workstation mode when none was requested.
    /// </summary>
    private static string[] BuildServeArgs(string[] args, string demoConfigPath)
    {
        var result = new List<string>(args.Length + 4);
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg.Equals(DemoWorkspaceLayout.SeedDemoFlag, StringComparison.OrdinalIgnoreCase)
                || arg.Equals(DemoWorkspaceLayout.SeedOnlyFlag, StringComparison.OrdinalIgnoreCase)
                || arg.Equals(DemoWorkspaceLayout.ResetDemoFlag, StringComparison.OrdinalIgnoreCase)
                || arg.Equals(DemoWorkspaceLayout.DemoFlag, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Drop any caller-supplied --config; the demo serve must use the generated demo config.
            if (arg.Equals("--config", StringComparison.OrdinalIgnoreCase))
            {
                i++; // also skip its value
                continue;
            }

            result.Add(arg);
        }

        result.Add("--config");
        result.Add(demoConfigPath);

        if (!result.Any(a => a.Equals("--mode", StringComparison.OrdinalIgnoreCase)))
        {
            result.Add("--mode");
            result.Add("workstation");
        }

        return result.ToArray();
    }
}
