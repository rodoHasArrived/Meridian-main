using FluentAssertions;
using Meridian.Application.Commands;
using Serilog;
using Xunit;

namespace Meridian.Tests.Application.Commands;

/// <summary>
/// Tests for the SymbolCommands CLI handler.
/// Validates argument parsing and routing for all symbol management subcommands.
/// </summary>
public sealed class SymbolCommandsTests
{
    private static readonly ILogger Logger = new LoggerConfiguration()
        .MinimumLevel.Debug()
        .WriteTo.Console()
        .CreateLogger();

    [Theory]
    [InlineData("--symbols")]
    [InlineData("--symbols-monitored")]
    [InlineData("--symbols-archived")]
    [InlineData("--symbols-add")]
    [InlineData("--symbols-remove")]
    [InlineData("--symbol-status")]
    public void CanHandle_WithSymbolFlag_ReturnsTrue(string flag)
    {
        var cmd = CreateCommand();
        cmd.CanHandle(new[] { flag }).Should().BeTrue();
    }

    [Theory]
    [InlineData("--SYMBOLS")]
    [InlineData("--Symbols-Add")]
    [InlineData("--SYMBOL-STATUS")]
    public void CanHandle_CaseInsensitive_ReturnsTrue(string flag)
    {
        var cmd = CreateCommand();
        cmd.CanHandle(new[] { flag }).Should().BeTrue();
    }

    [Fact]
    public void CanHandle_WithNonSymbolFlag_ReturnsFalse()
    {
        var cmd = CreateCommand();
        cmd.CanHandle(new[] { "--help" }).Should().BeFalse();
    }

    [Fact]
    public void CanHandle_EmptyArgs_ReturnsFalse()
    {
        var cmd = CreateCommand();
        cmd.CanHandle(Array.Empty<string>()).Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_AddWithoutValue_ReturnsError()
    {
        var cmd = CreateCommand();
        // --symbols-add without a value should return 2 (validation error)
        var result = await cmd.ExecuteAsync(new[] { "--symbols-add" });
        result.ExitCode.Should().Be(2);
    }

    [Fact]
    public async Task ExecuteAsync_RemoveWithoutValue_ReturnsError()
    {
        var cmd = CreateCommand();
        var result = await cmd.ExecuteAsync(new[] { "--symbols-remove" });
        result.ExitCode.Should().Be(2);
    }

    [Fact]
    public async Task ExecuteAsync_StatusWithoutValue_ReturnsError()
    {
        var cmd = CreateCommand();
        var result = await cmd.ExecuteAsync(new[] { "--symbol-status" });
        result.ExitCode.Should().Be(2);
    }

    [Fact]
    public void FormatBytes_FormatsCorrectly()
    {
        SymbolCommands.FormatBytes(0).Should().Be("0 B");
        SymbolCommands.FormatBytes(1023).Should().Be("1023 B");
        SymbolCommands.FormatBytes(1024).Should().Be("1 KB");
        SymbolCommands.FormatBytes(1024 * 1024).Should().Be("1 MB");
        SymbolCommands.FormatBytes(1024L * 1024 * 1024).Should().Be("1 GB");
        SymbolCommands.FormatBytes(1024L * 1024 * 1024 * 1024).Should().Be("1 TB");
    }

    [Fact]
    public void FormatBytes_HandlesPartialValues()
    {
        SymbolCommands.FormatBytes(1536).Should().Be("1.5 KB");
    }

    // ─── ParseSymbolFile tests ───────────────────────────────────────────────

    [Fact]
    public void ParseSymbolFile_EmptyContent_ReturnsEmpty()
    {
        SymbolCommands.ParseSymbolFile("", "symbols.txt").Should().BeEmpty();
        SymbolCommands.ParseSymbolFile("   ", "symbols.txt").Should().BeEmpty();
    }

    [Fact]
    public void ParseSymbolFile_JsonArray_ReturnsSymbols()
    {
        var result = SymbolCommands.ParseSymbolFile("[\"spy\", \"aapl\", \"msft\"]", "symbols.json");
        result.Should().BeEquivalentTo(new[] { "SPY", "AAPL", "MSFT" });
    }

    [Fact]
    public void ParseSymbolFile_JsonArrayWithWhitespace_TrimsAndUppercases()
    {
        var result = SymbolCommands.ParseSymbolFile("[ \" spy \", \"aapl\" ]", "symbols.json");
        result.Should().Contain("SPY").And.Contain("AAPL");
    }

    [Fact]
    public void ParseSymbolFile_JsonArrayWithDuplicates_Deduplicates()
    {
        var result = SymbolCommands.ParseSymbolFile("[\"SPY\", \"spy\", \"AAPL\"]", "symbols.json");
        result.Should().HaveCount(2).And.Contain("SPY").And.Contain("AAPL");
    }

    [Fact]
    public void ParseSymbolFile_SingleLineCsv_ReturnsSplitSymbols()
    {
        var result = SymbolCommands.ParseSymbolFile("SPY,AAPL,MSFT", "symbols.txt");
        result.Should().BeEquivalentTo(new[] { "SPY", "AAPL", "MSFT" });
    }

    [Fact]
    public void ParseSymbolFile_OnePerLine_ReturnsAllSymbols()
    {
        var content = "SPY\nAAPL\nMSFT";
        var result = SymbolCommands.ParseSymbolFile(content, "symbols.txt");
        result.Should().BeEquivalentTo(new[] { "SPY", "AAPL", "MSFT" });
    }

    [Fact]
    public void ParseSymbolFile_SkipsCommentLines()
    {
        var content = "# this is a comment\nSPY\n// another comment\nAAPL";
        var result = SymbolCommands.ParseSymbolFile(content, "symbols.txt");
        result.Should().BeEquivalentTo(new[] { "SPY", "AAPL" });
    }

    [Fact]
    public void ParseSymbolFile_CsvWithSymbolHeader_SkipsHeader()
    {
        var content = "SYMBOL\nSPY\nAAPL";
        var result = SymbolCommands.ParseSymbolFile(content, "symbols.csv");
        result.Should().BeEquivalentTo(new[] { "SPY", "AAPL" });
    }

    [Fact]
    public void ParseSymbolFile_CsvWithTickerHeader_SkipsHeader()
    {
        var content = "TICKER\nSPY\nAAPL";
        var result = SymbolCommands.ParseSymbolFile(content, "symbols.csv");
        result.Should().BeEquivalentTo(new[] { "SPY", "AAPL" });
    }

    [Fact]
    public void ParseSymbolFile_CsvMultiColumn_TakesFirstColumn()
    {
        var content = "SYMBOL,DESCRIPTION\nSPY,S&P 500 ETF\nAAPL,Apple Inc";
        var result = SymbolCommands.ParseSymbolFile(content, "symbols.csv");
        result.Should().BeEquivalentTo(new[] { "SPY", "AAPL" });
    }

    [Fact]
    public void ParseSymbolFile_LowercaseInput_NormalizesToUppercase()
    {
        var result = SymbolCommands.ParseSymbolFile("spy\naapl\nmsft", "symbols.txt");
        result.Should().BeEquivalentTo(new[] { "SPY", "AAPL", "MSFT" });
    }

    [Fact]
    public void ParseSymbolFile_InvalidJsonFallsBackToLineParsing()
    {
        // Starts with '[' but is not valid JSON — should fall through to line parsing
        var content = "[not valid json\nSPY\nAAPL";
        var result = SymbolCommands.ParseSymbolFile(content, "symbols.txt");
        result.Should().Contain("SPY").And.Contain("AAPL");
    }

    /// <summary>
    /// Creates a <see cref="SymbolCommands"/> backed by a lightweight
    /// <see cref="Meridian.Application.Subscriptions.Services.SymbolManagementService"/>
    /// that targets an isolated temp-directory config file.
    /// <para>
    /// This instance is sufficient for two test categories:
    /// <list type="bullet">
    ///   <item><description>
    ///     <b>CanHandle tests</b> – <c>CanHandle</c> only inspects the args array and
    ///     never calls into the service, so any valid (non-null) service instance works.
    ///   </description></item>
    ///   <item><description>
    ///     <b>ExecuteAsync validation tests</b> (missing required argument) – the command
    ///     returns <see cref="Meridian.Application.ResultTypes.ErrorCode.RequiredFieldMissing"/>
    ///     (exit code 2) before invoking the service, so no real config state is needed.
    ///   </description></item>
    /// </list>
    /// </para>
    /// </summary>
    private static SymbolCommands CreateCommand()
    {
        var configStore = new Meridian.Application.UI.ConfigStore(
            Path.Combine(Path.GetTempPath(), $"mdc-test-{Guid.NewGuid()}.json"));
        var service = new Meridian.Application.Subscriptions.Services.SymbolManagementService(
            configStore, Path.GetTempPath(), Logger);
        return new SymbolCommands(service, Logger);
    }
}
