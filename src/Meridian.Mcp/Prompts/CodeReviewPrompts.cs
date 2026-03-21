namespace Meridian.Mcp.Prompts;

[McpServerPromptType]
public sealed class CodeReviewPrompts(RepoPathService repo)
{
    [McpServerPrompt(Name = "review_code")]
    [Description("Code review prompt using Meridian's 6-lens review framework. Checks MVVM, error handling, performance, provider compliance, test quality, and documentation.")]
    public string ReviewCode(
        [Description("C# source code to review (paste the full file or relevant section)")] string code,
        [Description("Focus area: mvvm | performance | provider | storage | tests | all")] string focus = "all")
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Meridian Code Review\n");
        sb.AppendLine("Review the following C# code using the Meridian 6-lens framework. For each lens, identify:");
        sb.AppendLine("- **CRITICAL** issues (block merge, must fix)");
        sb.AppendLine("- **WARNING** issues (should fix before ship)");
        sb.AppendLine("- **SUGGESTION** items (optional improvements)");
        sb.AppendLine();

        if (focus == "all" || focus == "mvvm")
        {
            sb.AppendLine("## Lens 1: MVVM Compliance");
            sb.AppendLine("- Business logic in ViewModels, not `.xaml.cs` code-behind");
            sb.AppendLine("- `BindableBase` used for all ViewModels (no direct `INotifyPropertyChanged` impl)");
            sb.AppendLine("- No `DispatcherTimer` in code-behind — use `PeriodicTimer` in ViewModel");
            sb.AppendLine("- Service dependencies injected via constructor — not `new` in Page constructor");
            sb.AppendLine();
        }

        if (focus == "all" || focus == "performance")
        {
            sb.AppendLine("## Lens 2: Performance & Async Safety");
            sb.AppendLine("- No blocking task completion APIs — use `await` throughout");
            sb.AppendLine("- No `Task.Run` wrapping I/O — use native async APIs");
            sb.AppendLine("- No direct `HttpClient` construction — use `IHttpClientFactory`");
            sb.AppendLine("- Every `async` method has `CancellationToken ct = default`");
            sb.AppendLine("- No allocations in hot paths — check for unnecessary LINQ, boxing, or string concat");
            sb.AppendLine();
        }

        if (focus == "all" || focus == "provider")
        {
            sb.AppendLine("## Lens 3: Provider / ADR Compliance");
            sb.AppendLine("- `[DataSource]` attribute present with correct id, display name, type, category");
            sb.AppendLine("- `[ImplementsAdr(\"ADR-001\")]`, `[ImplementsAdr(\"ADR-004\")]`, `[ImplementsAdr(\"ADR-005\")]` present");
            sb.AppendLine("- Credentials only from environment variables — no hardcoded keys");
            sb.AppendLine("- Rate limiting via `WaitForRateLimitSlotAsync` (historical providers)");
            sb.AppendLine("- Class is `sealed`");
            sb.AppendLine();
        }

        if (focus == "all" || focus == "storage")
        {
            sb.AppendLine("## Lens 4: Storage Safety");
            sb.AppendLine("- All file writes routed through `AtomicFileWriter` (ADR-007)");
            sb.AppendLine("- No direct `File.WriteAllText` or raw `FileStream` for sink output");
            sb.AppendLine("- WAL flushed before acknowledging pipeline ingest");
            sb.AppendLine("- JSON serialization uses `MarketDataJsonContext` — no reflection (ADR-014)");
            sb.AppendLine("- `IStorageSink` implementations also implement `IFlushable`");
            sb.AppendLine();
        }

        if (focus == "all" || focus == "tests")
        {
            sb.AppendLine("## Lens 5: Error Handling & Observability");
            sb.AppendLine("- No bare `catch (Exception)` that swallows and ignores");
            sb.AppendLine("- All caught exceptions logged with structured context (symbol, provider, timestamp)");
            sb.AppendLine("- Structured logging only: `_logger.LogInfo(\"{Symbol}: {Count}\", sym, n)` — no `$\"{n}\"`");
            sb.AppendLine("- Custom exception types used: `DataProviderException`, `StorageException`, etc.");
            sb.AppendLine("- Prometheus metrics updated on error paths");
            sb.AppendLine();
        }

        if (focus == "all" || focus == "tests")
        {
            sb.AppendLine("## Lens 6: Test Quality");
            sb.AppendLine("- Meaningful assertions — not just `Assert.NotNull(result)` or `Does not throw`");
            sb.AppendLine("- Tests named: `MethodName_StateUnderTest_ExpectedBehavior`");
            sb.AppendLine("- No shared mutable state between tests");
            sb.AppendLine("- External dependencies (HTTP, file system, time) are mocked");
            sb.AppendLine("- Async tests use `await` — no blocking task completion in test code");
            sb.AppendLine();
        }

        // Append code-review agent guide if available
        if (File.Exists(repo.CodeReviewAgentFile))
        {
            sb.AppendLine("---");
            sb.AppendLine("## Full Code Review Agent Guide\n");
            sb.Append(File.ReadAllText(repo.CodeReviewAgentFile));
            sb.AppendLine();
        }

        sb.AppendLine("---");
        sb.AppendLine("## Code to Review\n");
        sb.AppendLine("```csharp");
        sb.AppendLine(code);
        sb.AppendLine("```");

        return sb.ToString();
    }
}
