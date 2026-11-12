using Meridian.Mcp.Services;
using Xunit;

namespace Meridian.Tests.Mcp;

/// <summary>
/// <see cref="RepoPathService"/> discovers its root from the process-wide <c>MDC_REPO_ROOT</c>
/// environment variable, so every test that sets it runs in this non-parallel collection.
/// </summary>
[CollectionDefinition(CollectionName, DisableParallelization = true)]
public sealed class McpRepoRootCollection
{
    public const string CollectionName = "MCP repository root environment";
}

/// <summary>
/// Creates a disposable fake Meridian repository (a directory containing <c>Meridian.sln</c>)
/// inside a disposable sandbox directory, and points <c>MDC_REPO_ROOT</c> at it.
/// </summary>
internal sealed class TempMeridianRepo : IDisposable
{
    public const string EnvVar = "MDC_REPO_ROOT";
    private readonly string? _previousRoot;

    public TempMeridianRepo(string repoDirectoryName = "repo", bool setEnvironment = true)
    {
        Sandbox = Path.Combine(
            Path.GetTempPath(),
            "meridian-tests",
            "mcp-repo-path",
            Guid.NewGuid().ToString("N"));
        Root = Path.Combine(Sandbox, repoDirectoryName);
        Directory.CreateDirectory(Root);
        File.WriteAllText(Path.Combine(Root, "Meridian.sln"), string.Empty);

        _previousRoot = Environment.GetEnvironmentVariable(EnvVar);
        if (setEnvironment)
            Environment.SetEnvironmentVariable(EnvVar, Root);
    }

    /// <summary>Parent directory that holds the fake repository and any "outside" files.</summary>
    public string Sandbox { get; }

    /// <summary>The fake repository root.</summary>
    public string Root { get; }

    public string WriteFile(string relativePath, string content)
    {
        var path = Path.Combine(Root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    public string WriteOutsideFile(string name, string content)
    {
        var path = Path.Combine(Sandbox, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    /// <summary>
    /// Snapshot of every file below the sandbox, keyed by full path, with its content. Python
    /// bytecode caches written next to the copied edit tool are interpreter noise, not edits.
    /// </summary>
    public IReadOnlyDictionary<string, string> SnapshotSandbox()
        => Directory.EnumerateFiles(Sandbox, "*", SearchOption.AllDirectories)
            .Where(path => !path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Contains("__pycache__"))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToDictionary(path => path, File.ReadAllText, StringComparer.Ordinal);

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(EnvVar, _previousRoot);
        try
        {
            if (Directory.Exists(Sandbox))
                Directory.Delete(Sandbox, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort temp cleanup.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort temp cleanup.
        }
    }
}
