using System.Diagnostics;

namespace Meridian.Mcp.Tools;

[McpServerToolType]
public sealed class AuditTools(RepoPathService repo, ILogger<AuditTools> logger)
{
    private const int TimeoutMs = 60_000;

    [McpServerTool(Name = "run_code_audit")]
    [Description("Run the Meridian code convention auditor. Detects missing CancellationToken, string interpolation in loggers, direct HttpClient construction, blocking async, and more. Returns findings as Markdown.")]
    public async Task<string> RunCodeAuditAsync(
        [Description("Optional file glob to scope the audit, e.g. 'src/Meridian.Infrastructure/**'")] string? pathFilter = null,
        CancellationToken ct = default)
    {
        var args = "audit-code";
        if (!string.IsNullOrWhiteSpace(pathFilter))
            args += $" --filter \"{pathFilter}\"";
        return await RunAuditCommandAsync(args, "Code Convention Audit", ct);
    }

    [McpServerTool(Name = "run_provider_audit")]
    [Description("Audit all provider classes for missing [ImplementsAdr] and [DataSource] attributes. Returns a list of non-compliant provider files.")]
    public async Task<string> RunProviderAuditAsync(CancellationToken ct = default) =>
        await RunAuditCommandAsync("audit-providers", "Provider Compliance Audit", ct);

    [McpServerTool(Name = "run_test_audit")]
    [Description("Identify Meridian classes that lack corresponding test files. Returns a prioritized list of coverage gaps.")]
    public async Task<string> RunTestAuditAsync(CancellationToken ct = default) =>
        await RunAuditCommandAsync("audit-tests", "Test Coverage Audit", ct);

    [McpServerTool(Name = "get_diff_summary")]
    [Description("Summarize current uncommitted git changes in the repository. Useful for writing commit messages or understanding what's changed.")]
    public async Task<string> GetDiffSummaryAsync(CancellationToken ct = default) =>
        await RunAuditCommandAsync("diff-summary", "Uncommitted Changes Summary", ct);

    [McpServerTool(Name = "run_full_audit")]
    [Description("Run the full Meridian repository audit (code, docs, tests, config, providers). Returns a consolidated findings report.")]
    public async Task<string> RunFullAuditAsync(CancellationToken ct = default) =>
        await RunAuditCommandAsync("audit", "Full Repository Audit", ct);

    private async Task<string> RunAuditCommandAsync(string command, string label, CancellationToken ct)
    {
        if (!File.Exists(repo.AuditScriptPath))
            return $"Audit script not found at: {repo.AuditScriptPath}\n\nEnsure you are running from the Meridian repository.";

        logger.LogInformation("Running audit command {Command}", command);

        var psi = new ProcessStartInfo("python3", $"\"{repo.AuditScriptPath}\" {command}")
        {
            WorkingDirectory = repo.Root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var process = new Process { StartInfo = psi };
        process.Start();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeoutMs);

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cts.Token);
        var stderrTask = process.StandardError.ReadToEndAsync(cts.Token);

        await process.WaitForExitAsync(cts.Token);
        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        return FormatAuditOutput(label, command, stdout, stderr, process.ExitCode);
    }

    private static string FormatAuditOutput(string label, string command, string stdout, string stderr, int exitCode)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"## {label}\n");
        sb.AppendLine($"**Command:** `python3 ai-repo-updater.py {command}`");
        sb.AppendLine($"**Exit code:** {exitCode}\n");

        if (!string.IsNullOrWhiteSpace(stdout))
        {
            // Try to pretty-print JSON if that's what was returned
            if (stdout.TrimStart().StartsWith('{') || stdout.TrimStart().StartsWith('['))
            {
                sb.AppendLine("### Output\n```json");
                sb.AppendLine(stdout.Trim());
                sb.AppendLine("```");
            }
            else
            {
                sb.AppendLine("### Output\n");
                sb.AppendLine(stdout.Trim());
            }
        }

        if (!string.IsNullOrWhiteSpace(stderr))
        {
            sb.AppendLine("\n### Stderr\n```");
            sb.AppendLine(stderr.Trim());
            sb.AppendLine("```");
        }

        if (exitCode != 0 && string.IsNullOrWhiteSpace(stdout))
            sb.AppendLine($"\n> Audit script exited with code {exitCode}. Check that Python 3 is installed and `ai-repo-updater.py` is present.");

        return sb.ToString();
    }
}
