using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.QuantScript.Tests;

public sealed class RoslynScriptCompilerTests
{
    private static RoslynScriptCompiler CreateCompiler()
        => new(NullLogger<RoslynScriptCompiler>.Instance);

    // ── Successful compilation ────────────────────────────────────────────────

    [Fact]
    public async Task CompileAsync_ValidSource_ReturnsSuccess()
    {
        var compiler = CreateCompiler();

        var result = await compiler.CompileAsync("var x = 1 + 1;");

        result.Success.Should().BeTrue();
        result.Diagnostics.Should().BeEmpty();
        result.CompilationTime.Should().BeGreaterThanOrEqualTo(TimeSpan.Zero);
    }

    [Fact]
    public async Task CompileAsync_ValidSource_ReturnsInUnderFiveSeconds()
    {
        var compiler = CreateCompiler();

        var result = await compiler.CompileAsync("System.Math.Sqrt(4);");

        result.CompilationTime.Should().BeLessThan(TimeSpan.FromSeconds(5));
    }

    // ── Error detection ───────────────────────────────────────────────────────

    [Fact]
    public async Task CompileAsync_SyntaxError_ReturnsFailed()
    {
        var compiler = CreateCompiler();

        var result = await compiler.CompileAsync("this is not valid csharp !!!;");

        result.Success.Should().BeFalse();
        result.Diagnostics.Should().NotBeEmpty();
    }

    [Fact]
    public async Task CompileAsync_SyntaxError_DiagnosticsContainLineInfo()
    {
        var compiler = CreateCompiler();

        var result = await compiler.CompileAsync("int x = \"oops\";");

        result.Success.Should().BeFalse();
        result.Diagnostics.Should().Contain(d => d.Line >= 1);
    }

    [Fact]
    public async Task CompileAsync_EmptySource_ThrowsArgumentException()
    {
        var compiler = CreateCompiler();

        await Assert.ThrowsAsync<ArgumentException>(
            () => compiler.CompileAsync(string.Empty));
    }

    [Fact]
    public async Task CompileAsync_WhiteSpaceOnlySource_ThrowsArgumentException()
    {
        var compiler = CreateCompiler();

        await Assert.ThrowsAsync<ArgumentException>(
            () => compiler.CompileAsync("   "));
    }

    // ── Caching ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task CompileAsync_SameSourceTwice_SecondCallReturnsCacheHit()
    {
        var compiler = CreateCompiler();
        const string source = "var x = 42;";

        var first = await compiler.CompileAsync(source);
        var second = await compiler.CompileAsync(source);

        first.Success.Should().BeTrue();
        second.Success.Should().BeTrue();
        // Cache hit is reported with zero compile time
        second.CompilationTime.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public async Task CompileAsync_DifferentSources_BothCompileIndependently()
    {
        var compiler = CreateCompiler();

        var r1 = await compiler.CompileAsync("var a = 1;");
        var r2 = await compiler.CompileAsync("var b = 2;");

        r1.Success.Should().BeTrue();
        r2.Success.Should().BeTrue();
    }

    // ── Cancellation ─────────────────────────────────────────────────────────

    [Fact]
    public async Task CompileAsync_CancelledToken_ThrowsOrReturnsBeforeComplete()
    {
        var compiler = CreateCompiler();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Either throws OperationCanceledException or completes if cached
        Func<Task> act = () => compiler.CompileAsync("var z = 1;", cts.Token);
        // We just verify it does not hang
        await act.Should().CompleteWithinAsync(TimeSpan.FromSeconds(5));
    }

    // ── ExtractParameters ─────────────────────────────────────────────────────

    [Fact]
    public void ExtractParameters_EmptySource_ReturnsEmpty()
    {
        var compiler = CreateCompiler();

        var result = compiler.ExtractParameters("var x = 1;");

        result.Should().BeEmpty();
    }
}
