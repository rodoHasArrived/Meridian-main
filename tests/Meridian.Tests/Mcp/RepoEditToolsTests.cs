using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Meridian.Mcp.Services;
using Meridian.Mcp.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meridian.Tests.Mcp;

/// <summary>
/// Boundary and edit-semantics coverage for the MCP <c>preview_repo_edit</c>,
/// <c>apply_repo_edit</c>, and <c>explain_repo_edit_plan</c> tools. Argument and
/// path-boundary checks run without Python. The end-to-end cases copy the real
/// <c>build/scripts/ai/ai-edit-tool.py</c> into a throwaway repository and run it through
/// <see cref="RepoEditTools"/>; they no-op when no <c>python</c> executable is on PATH.
/// </summary>
[Collection(McpRepoRootCollection.CollectionName)]
public sealed class RepoEditToolsTests
{
    private const string TargetRelativePath = "src/Demo/Target.cs";
    private const string OriginalContent = "class Target { int alpha = 1; int beta = alpha; }\n";
    private static readonly Regex PlanPathPattern = new(@"\*\*Plan path:\*\* `(?<path>[^`]+)`", RegexOptions.Compiled);
    private static readonly Regex ExitCodePattern = new(@"\*\*Exit code:\*\* (?<code>-?\d+)", RegexOptions.Compiled);

    // ── Argument validation (no Python required) ─────────────────────────────

    [Theory]
    [InlineData("{not json", """{"paths":["src"]}""", "recipeJson")]
    [InlineData("", """{"paths":["src"]}""", "recipeJson")]
    [InlineData("""{"kind":"literal_replace"}""", "[unterminated", "scope")]
    public async Task Preview_InvalidJsonArgument_ThrowsAndCreatesNoPlanDirectory(
        string recipeJson,
        string scope,
        string expectedParameter)
    {
        using var repo = new TempMeridianRepo();
        InstallEditTool(repo);
        var tools = CreateTools();

        var act = () => tools.PreviewRepoEditAsync(recipeJson, scope);

        (await act.Should().ThrowAsync<ArgumentException>()).Which.ParamName.Should().Be(expectedParameter);
        Directory.Exists(Path.Combine(repo.Root, ".codex")).Should().BeFalse();
    }

    [Fact]
    public async Task Preview_EditToolMissing_ThrowsFileNotFoundWithoutCreatingPlanDirectory()
    {
        using var repo = new TempMeridianRepo();
        var tools = CreateTools();

        var act = () => tools.PreviewRepoEditAsync(LiteralRecipe("alpha", "gamma"), Scope("src"));

        (await act.Should().ThrowAsync<FileNotFoundException>())
            .Which.FileName.Should().EndWith(Path.Combine("build", "scripts", "ai", "ai-edit-tool.py"));
        Directory.Exists(Path.Combine(repo.Root, ".codex")).Should().BeFalse();
    }

    [Fact]
    public async Task ApplyAndExplain_EditToolMissing_ThrowFileNotFound()
    {
        using var repo = new TempMeridianRepo();
        var tools = CreateTools();

        var apply = () => tools.ApplyRepoEditAsync("plan.json");
        var explain = () => tools.ExplainRepoEditPlanAsync("plan.json");

        await apply.Should().ThrowAsync<FileNotFoundException>();
        await explain.Should().ThrowAsync<FileNotFoundException>();
    }

    [Theory]
    [InlineData("../evil-plan.json")]
    [InlineData("src/../../evil-plan.json")]
    [InlineData("SANDBOX_ABSOLUTE")]
    public async Task Apply_PlanPathOutsideRoot_ThrowsAndLeavesFilesystemUntouched(string planPath)
    {
        using var repo = new TempMeridianRepo();
        InstallEditTool(repo);
        repo.WriteFile(TargetRelativePath, OriginalContent);
        var evilPlan = repo.WriteOutsideFile("evil-plan.json", "{}");
        if (planPath == "SANDBOX_ABSOLUTE")
            planPath = evilPlan;
        var before = repo.SnapshotSandbox();
        var tools = CreateTools();

        var act = () => tools.ApplyRepoEditAsync(planPath);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*outside the Meridian repository root*");
        repo.SnapshotSandbox().Should().BeEquivalentTo(before);
    }

    [Theory]
    [InlineData("../evil-plan.json")]
    [InlineData("SANDBOX_ABSOLUTE")]
    public async Task Explain_PlanPathOutsideRoot_Throws(string planPath)
    {
        using var repo = new TempMeridianRepo();
        InstallEditTool(repo);
        var evilPlan = repo.WriteOutsideFile("evil-plan.json", "{}");
        if (planPath == "SANDBOX_ABSOLUTE")
            planPath = evilPlan;
        var tools = CreateTools();

        var act = () => tools.ExplainRepoEditPlanAsync(planPath);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*outside the Meridian repository root*");
    }

    [Fact]
    public async Task ApplyAndExplain_NonexistentPlanInsideRoot_ReturnNotFoundWithoutRunningTool()
    {
        using var repo = new TempMeridianRepo();
        InstallEditTool(repo);
        repo.WriteFile(TargetRelativePath, OriginalContent);
        var before = repo.SnapshotSandbox();
        var tools = CreateTools();

        var applied = await tools.ApplyRepoEditAsync(".codex/tmp/ai-edit-plans/missing.json");
        var explained = await tools.ExplainRepoEditPlanAsync("missing.json");

        applied.Should().Be("Plan file not found inside repository root: .codex/tmp/ai-edit-plans/missing.json");
        explained.Should().Be("Plan file not found inside repository root: missing.json");
        repo.SnapshotSandbox().Should().BeEquivalentTo(before);
    }

    // ── End-to-end through the real edit tool ────────────────────────────────

    [Fact]
    public async Task Preview_LiteralReplace_WritesPlanInsideRootWithoutMutatingTarget()
    {
        if (!PythonAvailable())
            return;

        using var repo = new TempMeridianRepo();
        InstallEditTool(repo);
        var target = repo.WriteFile(TargetRelativePath, OriginalContent);
        var tools = CreateTools();

        var output = await tools.PreviewRepoEditAsync(LiteralRecipe("alpha", "gamma"), Scope("src"));

        ExitCode(output).Should().Be(0, output);
        output.Should().Contain("files=1 edits=2");
        var planPath = PlanPath(output);
        planPath.Should().StartWith(Path.Combine(repo.Root, ".codex", "tmp", "ai-edit-plans") + Path.DirectorySeparatorChar);
        File.Exists(planPath).Should().BeTrue();
        File.ReadAllText(target).Should().Be(OriginalContent, "preview must never mutate files");

        using var plan = JsonDocument.Parse(File.ReadAllText(planPath));
        var file = plan.RootElement.GetProperty("files").EnumerateArray().Should().ContainSingle().Subject;
        file.GetProperty("path").GetString().Should().Be(TargetRelativePath);
        file.GetProperty("edits").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task Apply_PreviewedPlan_ReplacesEveryExactMatchOnly()
    {
        if (!PythonAvailable())
            return;

        using var repo = new TempMeridianRepo();
        InstallEditTool(repo);
        var target = repo.WriteFile(TargetRelativePath, OriginalContent);
        var untouched = repo.WriteFile("src/Demo/Other.cs", "class Other { int Alpha = 2; }\n");
        var tools = CreateTools();
        var planPath = PlanPath(await tools.PreviewRepoEditAsync(LiteralRecipe("alpha", "gamma"), Scope("src")));

        // Pass the plan path relative to the root, as an MCP client would.
        var output = await tools.ApplyRepoEditAsync(Path.GetRelativePath(repo.Root, planPath));

        ExitCode(output).Should().Be(0, output);
        File.ReadAllText(target).Should().Be("class Target { int gamma = 1; int beta = gamma; }\n");
        File.ReadAllText(untouched).Should().Be("class Other { int Alpha = 2; }\n", "literal replace is case-sensitive");
    }

    [Fact]
    public async Task Apply_TargetDriftedAfterPreview_FailsAndKeepsDriftedContent()
    {
        if (!PythonAvailable())
            return;

        using var repo = new TempMeridianRepo();
        InstallEditTool(repo);
        var target = repo.WriteFile(TargetRelativePath, OriginalContent);
        var tools = CreateTools();
        var planPath = PlanPath(await tools.PreviewRepoEditAsync(LiteralRecipe("alpha", "gamma"), Scope("src")));
        const string drifted = "class Target { int alpha = 99; int beta = alpha; }\n";
        File.WriteAllText(target, drifted);

        var output = await tools.ApplyRepoEditAsync(planPath);

        ExitCode(output).Should().NotBe(0);
        output.Should().Contain("Hash drift detected");
        File.ReadAllText(target).Should().Be(drifted);
    }

    [Fact]
    public async Task Preview_NoMatches_ProducesEmptyPlanAndApplyChangesNothing()
    {
        if (!PythonAvailable())
            return;

        using var repo = new TempMeridianRepo();
        InstallEditTool(repo);
        repo.WriteFile(TargetRelativePath, OriginalContent);
        var tools = CreateTools();

        var preview = await tools.PreviewRepoEditAsync(LiteralRecipe("doesNotOccur", "x"), Scope("src"));
        ExitCode(preview).Should().Be(0, preview);
        preview.Should().Contain("files=0 edits=0");
        var before = repo.SnapshotSandbox();

        var apply = await tools.ApplyRepoEditAsync(PlanPath(preview));

        ExitCode(apply).Should().Be(0, apply);
        repo.SnapshotSandbox().Should().BeEquivalentTo(before);
    }

    [Fact]
    public async Task Preview_EditCountAboveMaxEdits_FailsWithoutWritingPlan()
    {
        if (!PythonAvailable())
            return;

        using var repo = new TempMeridianRepo();
        InstallEditTool(repo);
        var target = repo.WriteFile(TargetRelativePath, OriginalContent);
        var tools = CreateTools();

        var output = await tools.PreviewRepoEditAsync(LiteralRecipe("alpha", "gamma", maxEdits: 1), Scope("src"));

        ExitCode(output).Should().NotBe(0);
        output.Should().Contain("exceeds maxEdits=1");
        File.Exists(PlanPath(output)).Should().BeFalse();
        File.ReadAllText(target).Should().Be(OriginalContent);
    }

    [Theory]
    [InlineData("..")]
    [InlineData("../outside")]
    [InlineData("src/../..")]
    public async Task Preview_ScopeOutsideRoot_FailsAndNeverPlansOutsideFiles(string scopePath)
    {
        if (!PythonAvailable())
            return;

        using var repo = new TempMeridianRepo();
        InstallEditTool(repo);
        repo.WriteFile(TargetRelativePath, OriginalContent);
        repo.WriteOutsideFile(Path.Combine("outside", "Victim.cs"), "int alpha;");
        var before = repo.SnapshotSandbox();
        var tools = CreateTools();

        var output = await tools.PreviewRepoEditAsync(LiteralRecipe("alpha", "gamma"), Scope(scopePath));

        ExitCode(output).Should().NotBe(0);
        output.Should().Contain("outside repo root");
        repo.SnapshotSandbox().Should().BeEquivalentTo(before);
    }

    [Fact]
    public async Task Apply_TamperedPlanTargetingFileOutsideRoot_IsRejectedAndOutsideFileUntouched()
    {
        if (!PythonAvailable())
            return;

        using var repo = new TempMeridianRepo();
        InstallEditTool(repo);
        var victim = repo.WriteOutsideFile("victim.txt", "alpha");
        var tools = CreateTools();
        var resolvedRoot = new DirectoryInfo(repo.Root).ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? repo.Root;
        var plan = new
        {
            schemaVersion = "1.0",
            root = resolvedRoot,
            files = new[]
            {
                new
                {
                    path = "../victim.txt",
                    originalSha256 = "ignored",
                    plannedSha256 = "ignored",
                    edits = Array.Empty<object>()
                }
            }
        };
        repo.WriteFile(".codex/tmp/ai-edit-plans/tampered.json", JsonSerializer.Serialize(plan));

        var output = await tools.ApplyRepoEditAsync(".codex/tmp/ai-edit-plans/tampered.json");

        ExitCode(output).Should().NotBe(0);
        output.Should().Contain("escapes repo root");
        File.ReadAllText(victim).Should().Be("alpha");
    }

    [Fact]
    public async Task Explain_PreviewedPlan_SummarizesTouchedFilesWithoutMutating()
    {
        if (!PythonAvailable())
            return;

        using var repo = new TempMeridianRepo();
        InstallEditTool(repo);
        repo.WriteFile(TargetRelativePath, OriginalContent);
        var tools = CreateTools();
        var planPath = PlanPath(await tools.PreviewRepoEditAsync(LiteralRecipe("alpha", "gamma"), Scope("src")));
        var before = repo.SnapshotSandbox();

        var output = await tools.ExplainRepoEditPlanAsync(planPath);

        ExitCode(output).Should().Be(0, output);
        output.Should().Contain("Repository Edit Plan Explanation");
        output.Should().Contain("\"fileCount\": 1");
        output.Should().Contain("\"editCount\": 2");
        output.Should().Contain(TargetRelativePath);
        repo.SnapshotSandbox().Should().BeEquivalentTo(before);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static RepoEditTools CreateTools()
        => new(new RepoPathService(), NullLogger<RepoEditTools>.Instance);

    private static void InstallEditTool(TempMeridianRepo repo)
    {
        var sourceDirectory = Path.Combine(FindRealRepoRoot(), "build", "scripts", "ai");
        var destination = Path.Combine(repo.Root, "build", "scripts", "ai");
        Directory.CreateDirectory(destination);
        foreach (var name in new[] { "ai-edit-tool.py", "ai_edit_tool.py" })
            File.Copy(Path.Combine(sourceDirectory, name), Path.Combine(destination, name));
    }

    private static string FindRealRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Meridian.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Could not locate the Meridian repository root.");
    }

    private static bool PythonAvailable()
    {
        var names = OperatingSystem.IsWindows() ? new[] { "python.exe", "python" } : new[] { "python" };
        return (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Any(dir => names.Any(name => File.Exists(Path.Combine(dir.Trim('"'), name))));
    }

    private static string LiteralRecipe(string find, string replace, int maxFiles = 10, int maxEdits = 10)
        => JsonSerializer.Serialize(new
        {
            kind = "literal_replace",
            include = new[] { "src/**" },
            exclude = Array.Empty<string>(),
            find,
            replace,
            maxFiles,
            maxEdits
        });

    private static string Scope(string path) => JsonSerializer.Serialize(new { paths = new[] { path } });

    private static string PlanPath(string output)
    {
        var match = PlanPathPattern.Match(output);
        match.Success.Should().BeTrue("tool output must report the plan path: {0}", output);
        return match.Groups["path"].Value;
    }

    private static int ExitCode(string output)
    {
        var match = ExitCodePattern.Match(output);
        match.Success.Should().BeTrue("tool output must report the exit code: {0}", output);
        return int.Parse(match.Groups["code"].Value, System.Globalization.CultureInfo.InvariantCulture);
    }
}
