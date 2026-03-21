namespace Meridian.Mcp.Tools;

[McpServerToolType]
public sealed class ConventionTools(RepoPathService repo)
{
    [McpServerTool(Name = "get_coding_conventions")]
    [Description("Get all must-follow C# coding conventions for Meridian: async patterns, logging, DI, sealed classes, ADR attributes.")]
    public string GetCodingConventions()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Meridian C# Coding Conventions\n");

        // Core rules embedded from CLAUDE.md — always available even without file read
        sb.AppendLine("## Must-Follow Rules\n");
        sb.AppendLine("| Rule | Good | Bad |");
        sb.AppendLine("|------|------|-----|");
        sb.AppendLine("| Async suffix | `LoadDataAsync` | `LoadData` |");
        sb.AppendLine("| CancellationToken | `async Task FooAsync(CancellationToken ct = default)` | Missing `ct` parameter |");
        sb.AppendLine("| Private fields | `_fieldName` | `fieldName`, `m_field` |");
        sb.AppendLine("| Structured logging | `_logger.LogInfo(\"Got {Count}\", n)` | `_logger.LogInfo($\"Got {n}\")` |");
        sb.AppendLine("| Sealed classes | `public sealed class Foo` | `public class Foo` (unless inheritance-designed) |");
        sb.AppendLine("| Exception types | `throw new DataProviderException(...)` | `throw new Exception(...)` |");
        sb.AppendLine("| JSON serialization | `JsonSerializer.Serialize(obj, MyContext.Default.MyType)` | `JsonSerializer.Serialize(obj)` |");
        sb.AppendLine("| IOptions hot config | `IOptionsMonitor<T>` for runtime-changeable | `IOptions<T>` for truly static only |");
        sb.AppendLine("| Central packages | No `Version=` in `<PackageReference>` | `<PackageReference Include=\"Foo\" Version=\"1.0\" />` |");
        sb.AppendLine();

        sb.AppendLine("## Anti-Patterns (Never Do These)\n");
        sb.AppendLine("| Anti-Pattern | Why |");
        sb.AppendLine("|--------------|-----|");
        sb.AppendLine("| Blocking task completion APIs | Deadlocks in async contexts |");
        sb.AppendLine("| `Task.Run` for I/O-bound work | Wastes thread pool threads |");
        sb.AppendLine("| Direct `HttpClient` construction | Socket exhaustion, DNS caching issues |");
        sb.AppendLine("| String interpolation in logger | Loses structured log benefits |");
        sb.AppendLine("| Swallowing exceptions silently | Hides bugs, undebuggable |");
        sb.AppendLine("| Hardcoding credentials | Security risk |");
        sb.AppendLine("| Missing `[ImplementsAdr]` | Loses ADR traceability |");
        sb.AppendLine("| Missing `CancellationToken` | Prevents graceful shutdown |");
        sb.AppendLine("| Direct `FileStream` writes | Use `AtomicFileWriter` instead |");
        sb.AppendLine("| `Version=` on PackageReference | Violates CPM (NU1008 error) |");
        sb.AppendLine();

        // Append the csharp.instructions.md if available
        if (File.Exists(repo.CSharpInstructionsFile))
        {
            sb.AppendLine("---");
            sb.AppendLine("## Full C# Instructions\n");
            sb.Append(File.ReadAllText(repo.CSharpInstructionsFile));
        }

        return sb.ToString();
    }

    [McpServerTool(Name = "get_anti_patterns")]
    [Description("Get the list of anti-patterns to avoid in Meridian with explanations of why each is harmful.")]
    public string GetAntiPatterns()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Meridian Anti-Patterns\n");
        sb.AppendLine("These patterns are forbidden in this codebase. Each entry explains the symptom, why it's harmful, and the correct alternative.\n");

        var patterns = new[]
        {
            ("Swallowing exceptions silently", "Hides bugs, makes debugging impossible", "Log the exception with context and rethrow or handle explicitly"),
            ("Hardcoding credentials", "Security risk, inflexible deployment", "Use environment variables; never put keys in source or config files"),
            ("`Task.Run` for I/O-bound work", "Wastes thread pool threads, adds unnecessary overhead", "Use async/await with native async I/O APIs"),
            ("Blocking task completion APIs", "Causes deadlocks in synchronization-context environments", "Use `await` all the way up the call stack"),
            ("Direct `HttpClient` instances", "Socket exhaustion, DNS staleness, connection pool thrash", "Inject `IHttpClientFactory` and use named/typed clients"),
            ("String interpolation in logger calls", "Loses structured log parameter names, prevents log filtering", "Use semantic parameters: `_logger.LogInfo(\"{Symbol} bars: {Count}\", sym, n)`"),
            ("Missing `CancellationToken`", "Prevents graceful shutdown, can hang on I/O indefinitely", "Add `CancellationToken ct = default` to every async method signature"),
            ("Missing `[ImplementsAdr]` attribute", "Loses traceability from code to ADR contracts", "Add `[ImplementsAdr(\"ADR-XXX\", \"reason\")]` on all provider/sink classes"),
            ("Adding `Version=` to `<PackageReference>`", "Violates Central Package Management — causes NU1008 build error", "Define all versions in `Directory.Packages.props` only"),
            ("Direct `FileStream` or `File.WriteAllText` for sinks", "No crash safety — partial writes corrupt data on power loss", "Route all sink writes through `AtomicFileWriter`"),
            ("Non-sealed public classes", "Unintended inheritance, harder to reason about, missed optimization", "Mark `sealed` unless explicitly designed as a base class"),
            ("Reflection-based JSON serialization", "Slow, AOT-incompatible, trim-unsafe", "Use source-generated `JsonSerializerContext` (ADR-014)"),
        };

        foreach (var (pattern, harm, fix) in patterns)
        {
            sb.AppendLine($"### ❌ {pattern}");
            sb.AppendLine($"- **Why harmful:** {harm}");
            sb.AppendLine($"- **Correct approach:** {fix}");
            sb.AppendLine();
        }

        return sb.ToString();
    }
}
