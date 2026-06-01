using System.Diagnostics;
using System.Text.Json;

namespace Meridian.Mcp.Tools;

[McpServerToolType]
public sealed class RepoEditTools(RepoPathService repo, ILogger<RepoEditTools> logger)
{
    private const int TimeoutMs = 120_000;

    [McpServerTool(Name = "preview_repo_edit")]
    [Description("Create a deterministic preview plan for a scoped repository edit. The recipeJson and scope JSON are passed to build/scripts/ai/ai-edit-tool.py; no files are mutated.")]
    public async Task<string> PreviewRepoEditAsync(
        [Description("Recipe JSON with kind, include, exclude, find/replace or anchors, maxFiles, and maxEdits.")] string recipeJson,
        [Description("Scope JSON with paths relative to the repository root, for example {\"paths\":[\"src/Meridian.Mcp\"]}.")] string scope,
        CancellationToken ct = default)
    {
        ValidateJson(recipeJson, nameof(recipeJson));
        ValidateJson(scope, nameof(scope));
        EnsureToolExists();

        var planDirectory = repo.ResolveInsideRoot(Path.Combine(".codex", "tmp", "ai-edit-plans"));
        Directory.CreateDirectory(planDirectory);
        var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss");
        var planPath = Path.Combine(planDirectory, $"repo-edit-{stamp}-{Guid.NewGuid():N}.json");

        var result = await RunToolAsync(
            "plan",
            args =>
            {
                args.Add("--recipe-json");
                args.Add(recipeJson);
                args.Add("--scope-json");
                args.Add(scope);
                args.Add("--output");
                args.Add(planPath);
                args.Add("--summary");
            },
            ct);

        return FormatToolResult("Repository Edit Preview", result, planPath);
    }

    [McpServerTool(Name = "apply_repo_edit")]
    [Description("Apply a previously generated repository edit plan after ai-edit-tool.py verifies original SHA-256 hashes. Fails on drift.")]
    public async Task<string> ApplyRepoEditAsync(
        [Description("Plan path returned by preview_repo_edit. Must resolve inside the repository root.")] string planPath,
        CancellationToken ct = default)
    {
        EnsureToolExists();
        var safePlanPath = repo.ResolveInsideRoot(planPath);
        if (!File.Exists(safePlanPath))
            return $"Plan file not found inside repository root: {planPath}";

        var result = await RunToolAsync(
            "apply",
            args =>
            {
                args.Add("--plan");
                args.Add(safePlanPath);
                args.Add("--summary");
            },
            ct);

        return FormatToolResult("Repository Edit Apply", result, safePlanPath);
    }

    [McpServerTool(Name = "explain_repo_edit_plan")]
    [Description("Summarize an ai-edit-tool.py plan: touched files, edit count, risk flags, and suggested validation. Does not mutate files.")]
    public async Task<string> ExplainRepoEditPlanAsync(
        [Description("Plan path returned by preview_repo_edit. Must resolve inside the repository root.")] string planPath,
        CancellationToken ct = default)
    {
        EnsureToolExists();
        var safePlanPath = repo.ResolveInsideRoot(planPath);
        if (!File.Exists(safePlanPath))
            return $"Plan file not found inside repository root: {planPath}";

        var result = await RunToolAsync(
            "explain",
            args =>
            {
                args.Add("--plan");
                args.Add(safePlanPath);
            },
            ct);

        return FormatToolResult("Repository Edit Plan Explanation", result, safePlanPath);
    }

    private void EnsureToolExists()
    {
        if (!File.Exists(repo.AiEditToolPath))
            throw new FileNotFoundException("AI edit tool was not found.", repo.AiEditToolPath);
    }

    private static void ValidateJson(string value, string name)
    {
        try
        {
            using var _ = JsonDocument.Parse(value);
        }
        catch (JsonException ex)
        {
            throw new ArgumentException($"{name} must be valid JSON.", name, ex);
        }
    }

    private async Task<ToolRunResult> RunToolAsync(
        string mode,
        Action<ICollection<string>> configureArguments,
        CancellationToken ct)
    {
        logger.LogInformation("Running AI edit tool in {Mode} mode.", mode);

        var psi = new ProcessStartInfo("python")
        {
            WorkingDirectory = repo.Root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add(repo.AiEditToolPath);
        psi.ArgumentList.Add("--root");
        psi.ArgumentList.Add(repo.Root);
        psi.ArgumentList.Add(mode);
        configureArguments(psi.ArgumentList);

        using var process = new Process { StartInfo = psi };
        process.Start();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeoutMs);

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cts.Token);
        var stderrTask = process.StandardError.ReadToEndAsync(cts.Token);

        await process.WaitForExitAsync(cts.Token);
        return new ToolRunResult(
            process.ExitCode,
            await stdoutTask,
            await stderrTask);
    }

    private static string FormatToolResult(string title, ToolRunResult result, string planPath)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"## {title}\n");
        sb.AppendLine($"**Plan path:** `{planPath}`");
        sb.AppendLine($"**Exit code:** {result.ExitCode}\n");

        if (!string.IsNullOrWhiteSpace(result.Stdout))
        {
            sb.AppendLine("### Output\n");
            sb.AppendLine(result.Stdout.Trim());
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(result.Stderr))
        {
            sb.AppendLine("### Stderr\n```");
            sb.AppendLine(result.Stderr.Trim());
            sb.AppendLine("```");
        }

        return sb.ToString();
    }

    private sealed record ToolRunResult(int ExitCode, string Stdout, string Stderr);
}
