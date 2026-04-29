using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Meridian.QuantScript.Tests;

public sealed class RoslynScriptCompilerTests
{
    private static RoslynScriptCompiler BuildCompiler(QuantScriptOptions? options = null) =>
        new(Options.Create(options ?? new QuantScriptOptions()), NullLogger<RoslynScriptCompiler>.Instance);

    // ── Successful compilation ────────────────────────────────────────────────

    [Fact]
    public async Task CompileAsync_ValidSource_ReturnsSuccess()
    {
        var compiler = BuildCompiler();

        var result = await compiler.CompileAsync("var x = 1 + 1;");

        result.Success.Should().BeTrue();
        result.Diagnostics.Should().BeEmpty();
        result.CompilationTime.Should().BeGreaterThanOrEqualTo(TimeSpan.Zero);
    }

    [Fact]
    public async Task CompileAsync_ValidSource_CompletesWithinConfiguredTimeout()
    {
        var options = new QuantScriptOptions { CompilationTimeoutSeconds = 15 };
        var compiler = BuildCompiler(options);

        var result = await compiler.CompileAsync("System.Math.Sqrt(4);");

        result.Success.Should().BeTrue();
        result.CompilationTime.Should().BeLessThan(TimeSpan.FromSeconds(options.CompilationTimeoutSeconds));
    }

    [Fact]
    public async Task CompileAsync_ValidScript_ReturnsSuccess_Alt()
    {
        var compiler = BuildCompiler();
        var result = await compiler.CompileAsync("// empty script");
        result.Success.Should().BeTrue();
        result.Diagnostics.Should().BeEmpty();
    }

    // ── Error detection ───────────────────────────────────────────────────────

    [Fact]
    public async Task CompileAsync_SyntaxError_ReturnsFailed()
    {
        var compiler = BuildCompiler();

        var result = await compiler.CompileAsync("this is not valid csharp !!!;");

        result.Success.Should().BeFalse();
        result.Diagnostics.Should().NotBeEmpty();
    }

    [Fact]
    public async Task CompileAsync_SyntaxError_DiagnosticsContainLineInfo()
    {
        var compiler = BuildCompiler();

        var result = await compiler.CompileAsync("int x = \"oops\";");

        result.Success.Should().BeFalse();
        result.Diagnostics.Should().Contain(d => d.Line >= 1);
    }

    [Fact]
    public async Task CompileAsync_EmptySource_ThrowsArgumentException()
    {
        var compiler = BuildCompiler();

        await Assert.ThrowsAsync<ArgumentException>(
            () => compiler.CompileAsync(string.Empty));
    }

    [Fact]
    public async Task CompileAsync_WhiteSpaceOnlySource_ThrowsArgumentException()
    {
        var compiler = BuildCompiler();

        await Assert.ThrowsAsync<ArgumentException>(
            () => compiler.CompileAsync("   "));
    }

    // ── Caching ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task CompileAsync_SameSourceTwice_SecondCallReturnsCacheHit()
    {
        var compiler = BuildCompiler();
        const string source = "var x = 42;";

        var first = await compiler.CompileAsync(source);
        var second = await compiler.CompileAsync(source);

        first.Success.Should().BeTrue();
        second.Success.Should().BeTrue();
        // Cache hit is reported with zero compile time
        second.CompilationTime.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public async Task CompileAsync_SameSource_CachesCompilation()
    {
        var compiler = BuildCompiler();
        var source = "var x = 42;";
        await compiler.CompileAsync(source);
        var second = await compiler.CompileAsync(source);
        second.Success.Should().BeTrue();
    }

    [Fact]
    public async Task CompileAsync_DifferentSources_BothCompileIndependently()
    {
        var compiler = BuildCompiler();

        var r1 = await compiler.CompileAsync("var a = 1;");
        var r2 = await compiler.CompileAsync("var b = 2;");

        r1.Success.Should().BeTrue();
        r2.Success.Should().BeTrue();
    }

    // ── Cancellation ─────────────────────────────────────────────────────────

    [Fact]
    public async Task CompileAsync_CancelledToken_ThrowsOrReturnsBeforeComplete()
    {
        var compiler = BuildCompiler();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Func<Task> act = () => compiler.CompileAsync("var z = 1;", cts.Token);
        await act.Should().CompleteWithinAsync(TimeSpan.FromSeconds(5));
    }

    // ── ExtractParameters ─────────────────────────────────────────────────────

    [Fact]
    public void ExtractParameters_EmptySource_ReturnsEmpty()
    {
        var compiler = BuildCompiler();

        var result = compiler.ExtractParameters("var x = 1;");

        result.Should().BeEmpty();
    }

    [Fact]
    public void ExtractParameters_NoParams_ReturnsEmpty()
    {
        var compiler = BuildCompiler();
        var result = compiler.ExtractParameters("var x = 42;");
        result.Should().BeEmpty();
    }

    [Fact]
    public void ExtractParameters_ValidParam_ReturnsDescriptor()
    {
        var compiler = BuildCompiler();
        var source = """
            // @param period:Period:14:5:200:RSI window
            int period = 14;
            """;
        var result = compiler.ExtractParameters(source);
        result.Should().HaveCount(1);
        var p = result[0];
        p.Name.Should().Be("period");
        p.Label.Should().Be("Period");
        p.DefaultValue.Should().Be(14);
        p.Min.Should().Be(5);
        p.Max.Should().Be(200);
        p.Description.Should().Be("RSI window");
    }

    [Fact]
    public void ExtractParameters_MultipleParams_ReturnsAll()
    {
        var compiler = BuildCompiler();
        var source = """
            // @param fast:Fast Period:12:2:50:
            // @param slow:Slow Period:26:5:200:
            int fast = 12;
            int slow = 26;
            """;
        var result = compiler.ExtractParameters(source);
        result.Should().HaveCount(2);
    }

    [Fact]
    public void ExtractParameters_ParamCalls_ReturnRuntimeDescriptorShape()
    {
        var compiler = BuildCompiler();
        var source = """
            var lookback = Param<int>("lookback", 20, 5, 100, "Window length");
            """;

        var result = compiler.ExtractParameters(source);

        result.Should().ContainSingle();
        result[0].Name.Should().Be("lookback");
        result[0].TypeName.Should().Be("int");
        result[0].DefaultValue.Should().Be(20);
        result[0].Min.Should().Be(5);
        result[0].Max.Should().Be(100);
        result[0].Description.Should().Be("Window length");
    }

    [Fact]
    public void ExtractParameters_ScriptParamDeclarations_AreDiscovered()
    {
        var compiler = BuildCompiler();
        var source = """
            [ScriptParam("Fast Window", Default = 12, Min = 5, Max = 50, Description = "Used before first run")]
            int fastWindow = 12;
            """;

        var result = compiler.ExtractParameters(source);

        result.Should().ContainSingle();
        result[0].Name.Should().Be("fastWindow");
        result[0].Label.Should().Be("Fast Window");
        result[0].DefaultValue.Should().Be(12);
        result[0].Min.Should().Be(5);
        result[0].Max.Should().Be(50);
        result[0].Description.Should().Be("Used before first run");
    }

    [Fact]
    public void ExtractParameters_WhenMultipleDiscoverySourcesExist_PrefersParamCallsThenAttributesThenComments()
    {
        var compiler = BuildCompiler();
        var source = """
            // @param lookback:Legacy Lookback:10:1:20:legacy
            [ScriptParam("Attribute Lookback", Default = 11, Min = 2, Max = 30, Description = "attribute")]
            int lookback = 11;
            var resolved = Param<int>("lookback", 14, 3, 40, "runtime");
            """;

        var result = compiler.ExtractParameters(source);

        result.Should().ContainSingle();
        result[0].Name.Should().Be("lookback");
        result[0].DefaultValue.Should().Be(14);
        result[0].Min.Should().Be(3);
        result[0].Max.Should().Be(40);
        result[0].Description.Should().Be("runtime");
    }
}
