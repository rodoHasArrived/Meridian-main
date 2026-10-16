using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Xunit;

namespace Meridian.Tests.Architecture;

/// <summary>
/// Inspects MSBuild's evaluated ProjectReference items, including unused references and
/// references supplied by imports/properties and prepared framework/platform metadata.
/// Inspection does not restore or compile projects.
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
                    // Context identity alone cannot bound a cycle that expands a property.
                    // Fail closed rather than silently dropping a potentially forbidden edge.
                    Assert.True(visited.Count <= 1024 &&
                        visited.Count(context => PathComparer.Equals(context.Path, reference.Path)) <= 32,
                        $"Project-reference context budget exceeded at {reference.Path}; " +
                        "possible non-stabilizing property cycle: " + string.Join(" -> ", nextPath));
                    pending.Enqueue((reference, nextPath));
                }
            }
        }

        return [];
    }

    private static async Task<IReadOnlyList<ProjectContext>> ReadReferencesAsync(ProjectContext project)
    {
        // Preprocessing discovers imported targets without running restore/build. Bare
        // source-free projects have no PrepareProjectReferences target; SDK/Common
        // projects must use its negotiated items, not evaluation-time metadata.
        var preprocessed = await RunMSBuildAsync(project, "-preprocess");
        var hasPreparation = XDocument.Parse(preprocessed).Descendants()
            .Any(element => element.Name.LocalName == "Target" &&
                StringComparer.OrdinalIgnoreCase.Equals((string?)element.Attribute("Name"), "PrepareProjectReferences"));
        var itemNames = hasPreparation ? "ProjectReference,_MSBuildProjectReferenceExistent" : "ProjectReference";
        var arguments = new List<string>
        {
            $"-getItem:{itemNames}",
            "-getProperty:_GlobalPropertiesToRemoveFromProjectReferences"
        };
        if (hasPreparation)
        {
            arguments.Add("-target:PrepareProjectReferences");
        }

        var output = await RunMSBuildAsync(project, arguments.ToArray());
        using var evaluation = JsonDocument.Parse(output);
        var removals = evaluation.RootElement.GetProperty("Properties")
            .GetProperty("_GlobalPropertiesToRemoveFromProjectReferences").GetString() ?? "";
        var items = evaluation.RootElement.GetProperty("Items");
        var declared = items.GetProperty("ProjectReference").EnumerateArray().ToArray();
        var prepared = hasPreparation
            ? items.GetProperty("_MSBuildProjectReferenceExistent").EnumerateArray().ToArray()
            : [];
        var unrepresented = declared.ToList();
        foreach (var reference in prepared)
        {
            var path = Path.GetFullPath(reference.GetProperty("FullPath").GetString()!);
            var candidates = unrepresented.Where(item => PathComparer.Equals(path,
                Path.GetFullPath(item.GetProperty("FullPath").GetString()!))).ToArray();
            if (candidates.Length == 0)
            {
                continue;
            }

            var contexts = candidates.Select(item => CreateContext(project.Properties, item, removals)).ToArray();
            var comparer = new ProjectContextComparer();
            JsonElement? represented = contexts.Distinct(comparer).Count() == 1 ? candidates[0] : null;
            if (represented is null)
            {
                var scored = candidates.Select(item => (Item: item, Score: ReferenceMatchScore(item, reference)))
                    .Where(item => item.Score >= 0).OrderByDescending(item => item.Score).ToArray();
                if (scored.Length > 0)
                {
                    var best = scored.Where(item => item.Score == scored[0].Score).ToArray();
                    if (best.Select(item => CreateContext(project.Properties, item.Item, removals))
                        .Distinct(comparer).Count() == 1)
                    {
                        represented = best[0].Item;
                    }
                }
            }

            if (represented is { } matched)
            {
                var context = CreateContext(project.Properties, matched, removals);
                unrepresented.RemoveAll(item => comparer.Equals(context,
                    CreateContext(project.Properties, item, removals)));
            }
        }

        // Keep distinct unrepresented contexts, even when a prepared item shares their path.
        // Ambiguous partial mappings retain declarations rather than dropping an edge.
        return prepared.Concat(unrepresented)
            .Select(reference => new ProjectContext(
                Path.GetFullPath(reference.GetProperty("FullPath").GetString()!),
                GetReferenceProperties(project.Properties, reference, removals),
                ReadMetadata(reference, "ToolsVersion") is { Length: > 0 } toolsVersion ? toolsVersion : null))
            .ToArray();
    }

    private static ProjectContext CreateContext(
        IReadOnlyDictionary<string, string> parentProperties, JsonElement reference, string removals) =>
        new(Path.GetFullPath(reference.GetProperty("FullPath").GetString()!),
            GetReferenceProperties(parentProperties, reference, removals),
            ReadMetadata(reference, "ToolsVersion") is { Length: > 0 } toolsVersion ? toolsVersion : null);

    private static int ReferenceMatchScore(JsonElement declared, JsonElement prepared)
    {
        // Preparation may add framework/platform properties, but an explicit declared
        // override or toolset must agree before it can identify a represented context.
        var empty = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var expected = GetReferenceProperties(empty, declared, "");
        var actual = GetReferenceProperties(empty, prepared, "");
        var toolset = ReadMetadata(declared, "ToolsVersion");
        var preparedToolset = ReadMetadata(prepared, "ToolsVersion");
        if (expected.Any(property => !actual.TryGetValue(property.Key, out var value) ||
            !StringComparer.Ordinal.Equals(property.Value, value)) ||
            (toolset.Length > 0 && !StringComparer.OrdinalIgnoreCase.Equals(toolset,
                preparedToolset.Length > 0 ? preparedToolset : "Current")))
        {
            return -1;
        }

        var removed = (ReadMetadata(declared, "UndefineProperties") + ";" +
            ReadMetadata(declared, "GlobalPropertiesToRemove"))
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var preparedRemoved = (ReadMetadata(prepared, "UndefineProperties") + ";" +
            ReadMetadata(prepared, "GlobalPropertiesToRemove"))
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return removed.All(preparedRemoved.Contains)
            ? expected.Count + removed.Length + (toolset.Length > 0 ? 1 : 0)
            : -1;
    }

    private static async Task<string> RunMSBuildAsync(ProjectContext project, params string[] arguments)
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
        startInfo.ArgumentList.Add("-maxcpucount:1");
        startInfo.ArgumentList.Add("-nodeReuse:false");
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }
        if (project.ToolsVersion is not null)
        {
            startInfo.ArgumentList.Add($"-toolsVersion:{project.ToolsVersion}");
        }
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

        return output;
    }

    private static Dictionary<string, string> GetReferenceProperties(
        IReadOnlyDictionary<string, string> parentProperties, JsonElement reference, string targetRemovals)
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
            ReadMetadata(reference, "GlobalPropertiesToRemove") + ";" + targetRemovals)
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
        // Preserve residual escapes: the MSBuild task forwards them as literal values.
        // Like PropertyParser, preserve semicolon-separated value fragments that have no new assignment.
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

    private sealed record ProjectContext(string Path, IReadOnlyDictionary<string, string> Properties, string? ToolsVersion = null);

    private sealed class ProjectContextComparer : IEqualityComparer<ProjectContext>
    {
        public bool Equals(ProjectContext? x, ProjectContext? y) =>
            ReferenceEquals(x, y) || (x is not null && y is not null &&
            PathComparer.Equals(x.Path, y.Path) &&
            StringComparer.OrdinalIgnoreCase.Equals(x.ToolsVersion, y.ToolsVersion) && x.Properties.Count == y.Properties.Count &&
            x.Properties.All(property => y.Properties.TryGetValue(property.Key, out var value) &&
                StringComparer.Ordinal.Equals(property.Value, value)));

        public int GetHashCode(ProjectContext project)
        {
            var hash = new HashCode();
            hash.Add(project.Path, PathComparer);
            hash.Add(project.ToolsVersion, StringComparer.OrdinalIgnoreCase);
            foreach (var property in project.Properties.OrderBy(property => property.Key, StringComparer.OrdinalIgnoreCase))
            {
                hash.Add(property.Key, StringComparer.OrdinalIgnoreCase);
                hash.Add(property.Value, StringComparer.Ordinal);
            }

            return hash.ToHashCode();
        }
    }
}
