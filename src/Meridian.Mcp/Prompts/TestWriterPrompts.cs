namespace Meridian.Mcp.Prompts;

[McpServerPromptType]
public sealed class TestWriterPrompts(RepoPathService repo)
{
    [McpServerPrompt(Name = "write_tests")]
    [Description("Test writing prompt with Meridian xUnit + FluentAssertions patterns. Generates appropriate test scaffold for the given component type.")]
    public string WriteTests(
        [Description("Component type: provider | historical-provider | service | pipeline | storage | wpf-service | endpoint")] string componentType,
        [Description("The class name to write tests for, e.g. 'AlpacaMarketDataClient'")] string className,
        [Description("Optional: paste relevant source code to write tests against")] string? sourceCode = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Write xUnit Tests for `{className}`\n");
        sb.AppendLine($"**Component type:** {componentType}");
        sb.AppendLine($"**Test project:** `tests/Meridian.Tests/`");
        sb.AppendLine();

        sb.AppendLine("## Meridian Test Conventions\n");
        sb.AppendLine("- Framework: **xUnit** with **FluentAssertions**");
        sb.AppendLine("- Mocking: **NSubstitute** (preferred) or **Moq**");
        sb.AppendLine("- Test naming: `MethodName_StateUnderTest_ExpectedBehavior`");
        sb.AppendLine("- Async tests: `async Task` return, proper `await`, never block on task completion APIs");
        sb.AppendLine("- One assertion concept per test — use `FluentAssertions` `.Should().Be()` etc.");
        sb.AppendLine("- `CancellationToken.None` in tests (or `CancellationToken ct = default` at boundary)");
        sb.AppendLine("- No shared mutable state between tests — use `IClassFixture<T>` for expensive setup");
        sb.AppendLine();

        AppendComponentPatterns(sb, componentType, className);

        if (File.Exists(repo.TestingGuideFile))
        {
            sb.AppendLine("---");
            sb.AppendLine("## Meridian Testing Guide\n");
            sb.Append(File.ReadAllText(repo.TestingGuideFile));
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(sourceCode))
        {
            sb.AppendLine("---");
            sb.AppendLine("## Source Code to Test\n");
            sb.AppendLine("```csharp");
            sb.AppendLine(sourceCode);
            sb.AppendLine("```");
        }

        sb.AppendLine("---");
        sb.AppendLine($"Write a comprehensive xUnit test class for `{className}` following the patterns above.");
        sb.AppendLine("Cover: happy path, edge cases, error paths, and cancellation token propagation.");

        return sb.ToString();
    }

    private static void AppendComponentPatterns(StringBuilder sb, string componentType, string className)
    {
        sb.AppendLine("## Pattern for This Component Type\n");

        switch (componentType.ToLowerInvariant())
        {
            case "provider":
            case "streaming":
                sb.AppendLine("```csharp");
                sb.AppendLine($"public sealed class {className}Tests");
                sb.AppendLine("{");
                sb.AppendLine("    private readonly ILogger<" + className + "> _logger = Substitute.For<ILogger<" + className + ">>();");
                sb.AppendLine("    private readonly IHttpClientFactory _httpClientFactory = Substitute.For<IHttpClientFactory>();");
                sb.AppendLine();
                sb.AppendLine("    [Fact]");
                sb.AppendLine("    public void IsEnabled_WhenCredentialsPresent_ReturnsTrue() { }");
                sb.AppendLine();
                sb.AppendLine("    [Fact]");
                sb.AppendLine("    public async Task ConnectAsync_WhenConnectionSucceeds_DoesNotThrow() { }");
                sb.AppendLine();
                sb.AppendLine("    [Fact]");
                sb.AppendLine("    public async Task ConnectAsync_WhenCancelled_ThrowsOperationCancelledException() { }");
                sb.AppendLine("}");
                sb.AppendLine("```");
                break;

            case "historical-provider":
                sb.AppendLine("```csharp");
                sb.AppendLine($"public sealed class {className}Tests");
                sb.AppendLine("{");
                sb.AppendLine("    private readonly HttpClient _mockHttpClient;");
                sb.AppendLine("    private readonly IHttpClientFactory _factory = Substitute.For<IHttpClientFactory>();");
                sb.AppendLine();
                sb.AppendLine("    [Fact]");
                sb.AppendLine("    public async Task GetDailyBarsAsync_ValidSymbol_ReturnsBars()");
                sb.AppendLine("    {");
                sb.AppendLine("        // Arrange: mock HTTP response with JSON fixture");
                sb.AppendLine("        // Act: call GetDailyBarsAsync(\"AAPL\", from, to, ct)");
                sb.AppendLine("        // Assert: bars.Should().NotBeEmpty(); bars[0].Close.Should().BeGreaterThan(0);");
                sb.AppendLine("    }");
                sb.AppendLine();
                sb.AppendLine("    [Fact]");
                sb.AppendLine("    public async Task GetDailyBarsAsync_WhenRateLimited_WaitsAndRetries() { }");
                sb.AppendLine("}");
                sb.AppendLine("```");
                break;

            case "service":
                sb.AppendLine("```csharp");
                sb.AppendLine($"public sealed class {className}Tests");
                sb.AppendLine("{");
                sb.AppendLine("    // Inject all dependencies as NSubstitute mocks");
                sb.AppendLine("    [Fact] public async Task MethodName_StateUnderTest_ExpectedResult() { }");
                sb.AppendLine("    [Fact] public async Task MethodName_WhenCancelled_PropagatesCancellation() { }");
                sb.AppendLine("}");
                sb.AppendLine("```");
                break;

            case "storage":
                sb.AppendLine("```csharp");
                sb.AppendLine($"public sealed class {className}Tests : IDisposable");
                sb.AppendLine("{");
                sb.AppendLine("    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());");
                sb.AppendLine();
                sb.AppendLine("    [Fact]");
                sb.AppendLine("    public async Task WriteAsync_ValidEvent_PersistsToFile()");
                sb.AppendLine("    {");
                sb.AppendLine("        // Use _tempDir for isolation — never write to real data/");
                sb.AppendLine("    }");
                sb.AppendLine();
                sb.AppendLine("    public void Dispose() => Directory.Delete(_tempDir, recursive: true);");
                sb.AppendLine("}");
                sb.AppendLine("```");
                break;

            case "wpf-service":
                sb.AppendLine("These tests live in `tests/Meridian.Ui.Tests/Services/`.\n");
                sb.AppendLine("```csharp");
                sb.AppendLine($"public sealed class {className}Tests");
                sb.AppendLine("{");
                sb.AppendLine("    // UI services have no UI dependency — test service logic only");
                sb.AppendLine("    // Mock IApiClientService, ILoggingService, etc.");
                sb.AppendLine("    [Fact] public void PropertyChange_WhenValueSet_RaisesNotification() { }");
                sb.AppendLine("}");
                sb.AppendLine("```");
                break;

            default:
                sb.AppendLine($"Use the standard xUnit pattern. Inject dependencies as NSubstitute substitutes in the constructor.");
                break;
        }
        sb.AppendLine();
    }
}
