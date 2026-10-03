using System.Diagnostics;
using System.Text;
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
        var source = new ProjectContext(Path.GetFullPath(sourceProject),
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Configuration"] = configuration });
        var forbidden = Path.GetFullPath(forbiddenProject);
        var visited = new HashSet<ProjectContext>(new ProjectContextComparer()) { source };
        var pending = new Queue<(ProjectContext Project, string[] Path)>();
        pending.Enqueue((source, [source.Path]));

        while (pending.TryDequeue(out var current))
        {
            foreach (var reference in await ReadReferencesAsync(current.Project))
            {
                var nextPath = current.Path.Append(reference.Path).ToArray();
                if (PathComparer.Equals(reference.Path, forbidden))
                {
                    return nextPath;
                }

                if (visited.Add(reference))
                {
                    pending.Enqueue((reference, nextPath));
                }
            }
        }

        return [];
    }

    private static async Task<IReadOnlyList<ProjectContext>> ReadReferencesAsync(ProjectContext project)
    {
        var projectPath = project.Path;
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
        foreach (var property in project.Properties)
        {
            startInfo.ArgumentList.Add($"-property:{property.Key}={EscapePropertyValue(property.Value)}");
        }

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
            .Select(reference => new ProjectContext(
                Path.GetFullPath(reference.GetProperty("FullPath").GetString()!),
                GetReferenceProperties(project.Properties, reference)))
            .ToArray();
    }

    private static Dictionary<string, string> GetReferenceProperties(
        IReadOnlyDictionary<string, string> parentProperties, JsonElement reference)
    {
        var properties = new Dictionary<string, string>(parentProperties, StringComparer.OrdinalIgnoreCase);
        var overrides = ReadMetadata(reference, "Properties");
        // Match MSBuild's ProjectReference global-property precedence: Properties replaces
        // the Set* task properties, AdditionalProperties overrides both, and removals win.
        if (string.IsNullOrWhiteSpace(overrides))
        {
            overrides = string.Join(";", ReadMetadata(reference, "SetConfiguration"),
                ReadMetadata(reference, "SetPlatform"), ReadMetadata(reference, "SetTargetFramework"));
        }

        ApplyProperties(properties, overrides);
        ApplyProperties(properties, ReadMetadata(reference, "AdditionalProperties"));
        foreach (var name in (ReadMetadata(reference, "UndefineProperties") + ";" +
            ReadMetadata(reference, "GlobalPropertiesToRemove"))
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            properties.Remove(name);
        }

        return properties;
    }

    private static string ReadMetadata(JsonElement reference, string name)
    {
        foreach (var metadata in reference.EnumerateObject())
        {
            if (StringComparer.OrdinalIgnoreCase.Equals(metadata.Name, name))
            {
                return metadata.Value.GetString() ?? "";
            }
        }

        return "";
    }

    private static void ApplyProperties(Dictionary<string, string> properties, string assignments)
    {
        string? previousName = null;
        // Evaluated metadata is already unescaped. Like MSBuild's PropertyParser,
        // preserve semicolon-separated value fragments that have no new assignment.
        foreach (var assignment in assignments.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var equals = assignment.IndexOf('=');
            if (equals < 0 && previousName is not null)
            {
                properties[previousName] += ";" + assignment;
                continue;
            }

            Assert.True(equals > 0, $"Invalid ProjectReference property assignment: {assignment}");
            var name = assignment[..equals].Trim();
            Assert.False(string.IsNullOrEmpty(name), $"Invalid ProjectReference property assignment: {assignment}");
            properties[name] = assignment[(equals + 1)..].Trim();
            previousName = name;
        }
    }

    private static string EscapePropertyValue(string value)
    {
        var escaped = new StringBuilder();
        foreach (var character in value)
        {
            // Escape MSBuild syntax and command-line property-list separators. ArgumentList
            // handles shell quoting but does not protect values from MSBuild's own parser.
            if (character is '%' or '$' or '@' or '\'' or ';' or ',' or '?' or '*' or '(' or ')' or '"')
            {
                escaped.Append('%').Append(((int)character).ToString("X2"));
            }
            else
            {
                escaped.Append(character);
            }
        }

        return escaped.ToString();
    }

    private sealed record ProjectContext(string Path, IReadOnlyDictionary<string, string> Properties);

    private sealed class ProjectContextComparer : IEqualityComparer<ProjectContext>
    {
        public bool Equals(ProjectContext? x, ProjectContext? y) =>
            ReferenceEquals(x, y) || (x is not null && y is not null &&
            PathComparer.Equals(x.Path, y.Path) && x.Properties.Count == y.Properties.Count &&
            x.Properties.All(property => y.Properties.TryGetValue(property.Key, out var value) &&
                StringComparer.Ordinal.Equals(property.Value, value)));

        public int GetHashCode(ProjectContext project)
        {
            var hash = new HashCode();
            hash.Add(project.Path, PathComparer);
            foreach (var property in project.Properties.OrderBy(property => property.Key, StringComparer.OrdinalIgnoreCase))
            {
                hash.Add(property.Key, StringComparer.OrdinalIgnoreCase);
                hash.Add(property.Value, StringComparer.Ordinal);
            }

            return hash.ToHashCode();
        }
    }
}
