using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Meridian.QuantScript.Tests;

public sealed class RoslynScriptCompilerTests
{
    private static RoslynScriptCompiler BuildCompiler() =>
        new(Options.Create(new QuantScriptOptions()), NullLogger<RoslynScriptCompiler>.Instance);

    [Fact]
    public async Task CompileAsync_ValidScript_ReturnsSuccess()
    {
        var compiler = BuildCompiler();
        var result = await compiler.CompileAsync("// empty script");
        result.Success.Should().BeTrue();
        result.Diagnostics.Should().BeEmpty();
    }

    [Fact]
    public async Task CompileAsync_SyntaxError_ReturnsFailed()
    {
        var compiler = BuildCompiler();
        var result = await compiler.CompileAsync("this is not valid C#!!!");
        result.Success.Should().BeFalse();
        result.Diagnostics.Should().NotBeEmpty();
    }

    [Fact]
    public async Task CompileAsync_SameSource_CachesCompilation()
    {
        var compiler = BuildCompiler();
        var source = "var x = 42;";
        await compiler.CompileAsync(source);
        // Second compile should hit cache — no exception
        var second = await compiler.CompileAsync(source);
        second.Success.Should().BeTrue();
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
        p.DefaultValue.Should().Be("14");
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
}
