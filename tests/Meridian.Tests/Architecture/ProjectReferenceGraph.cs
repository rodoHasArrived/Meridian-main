using System.Diagnostics;
using System.Text.Json;
using Xunit;

namespace Meridian.Tests.Architecture;

/// <summary>
/// Inspects MSBuild's evaluated ProjectReference items, including unused references and
/// references supplied by imports/properties. Evaluation does not restore or compile projects.
/// </summary>
internal static class ProjectReferenceGraph
{
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    public static async Task AssertNoDependencyAsync(string sourceProject, string forbiddenProject, string configuration)
    {
        var dependencyPath = await FindDependencyPathAsync(sourceProject, forbiddenProject, configuration);
        Assert.True(
            dependencyPath.Count == 0,
            $"Forbidden project dependency ({configuration}): " +
            string.Join(" -> ", dependencyPath.Select(Path.GetFileNameWithoutExtension)) +
            ". Infrastructure must not depend on Storage, including through an intermediate project.");
    }

    private static async Task<IReadOnlyList<string>> FindDependencyPathAsync(
        string sourceProject,
        string forbiddenProject,
        string configuration)
    {
        var source = Path.GetFullPath(sourceProject);
        var forbidden = Path.GetFullPath(forbiddenProject);
        var visited = new HashSet<string>(PathComparer) { source };
        var pending = new Queue<string[]>();
        pending.Enqueue([source]);

        while (pending.TryDequeue(out var path))
        {
            foreach (var reference in await ReadReferencesAsync(path[^1], configuration))
            {
                var nextPath = path.Append(reference).ToArray();
                if (PathComparer.Equals(reference, forbidden))
                {
                    return nextPath;
                }

                if (visited.Add(reference))
                {
                    pending.Enqueue(nextPath);
                }
            }
        }

        return [];
    }

    private static async Task<IReadOnlyList<string>> ReadReferencesAsync(string projectPath, string configuration)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet",
            WorkingDirectory = Path.GetDirectoryName(projectPath)!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("msbuild");
        startInfo.ArgumentList.Add(projectPath);
        startInfo.ArgumentList.Add("-nologo");
        startInfo.ArgumentList.Add("-verbosity:quiet");
        startInfo.ArgumentList.Add("-getItem:ProjectReference");
        startInfo.ArgumentList.Add($"-property:Configuration={configuration}");

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Unable to start MSBuild to evaluate {projectPath}.");
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            throw new TimeoutException($"MSBuild project-reference evaluation timed out for {projectPath}.");
        }

        var output = await outputTask;
        var errors = await errorTask;
        Assert.True(
            process.ExitCode == 0,
            $"MSBuild could not evaluate project references for {projectPath}: {output}{Environment.NewLine}{errors}");

        using var evaluation = JsonDocument.Parse(output);
        return evaluation.RootElement.GetProperty("Items").GetProperty("ProjectReference")
            .EnumerateArray()
            .Select(reference => Path.GetFullPath(reference.GetProperty("FullPath").GetString()!))
            .Distinct(PathComparer)
            .ToArray();
    }
}
