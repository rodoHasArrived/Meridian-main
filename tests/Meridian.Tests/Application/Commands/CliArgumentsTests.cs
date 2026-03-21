using FluentAssertions;
using Meridian.Application.Commands;
using Xunit;

namespace Meridian.Tests.Application.Commands;

/// <summary>
/// Tests for the typed CliArguments parser.
/// Validates that all CLI flags and values are correctly parsed from raw args.
/// </summary>
public sealed class CliArgumentsTests
{
    [Fact]
    public void Parse_EmptyArgs_ReturnsDefaults()
    {
        var args = CliArguments.Parse(Array.Empty<string>());

        args.Help.Should().BeFalse();
        args.SelfTest.Should().BeFalse();
        args.SimulateFeed.Should().BeFalse();
        args.Backfill.Should().BeFalse();
        args.DryRun.Should().BeFalse();
        args.Offline.Should().BeFalse();
        args.ValidateConfig.Should().BeFalse();
        args.ValidateSchemas.Should().BeFalse();
        args.StrictSchemas.Should().BeFalse();
        args.Symbols.Should().BeFalse();
        args.SymbolsMonitored.Should().BeFalse();
        args.SymbolsArchived.Should().BeFalse();
        args.SymbolsAdd.Should().BeNull();
        args.SymbolsRemove.Should().BeNull();
        args.SymbolStatus.Should().BeNull();
        args.NoTrades.Should().BeFalse();
        args.NoDepth.Should().BeFalse();
        args.DepthLevels.Should().Be(10);
        args.Update.Should().BeFalse();
        args.ConfigPath.Should().BeNull();
        args.Replay.Should().BeNull();
        args.HttpPort.Should().BeNull();
        args.Mode.Should().BeNull();
        args.HasSymbolCommand.Should().BeFalse();
    }

    [Fact]
    public void Parse_HelpFlag_SetsHelp()
    {
        var args = CliArguments.Parse(new[] { "--help" });
        args.Help.Should().BeTrue();
    }

    [Fact]
    public void Parse_ShortHelpFlag_SetsHelp()
    {
        var args = CliArguments.Parse(new[] { "-h" });
        args.Help.Should().BeTrue();
    }

    [Fact]
    public void Parse_CaseInsensitive_RecognizesFlags()
    {
        var args = CliArguments.Parse(new[] { "--SELFTEST" });
        args.SelfTest.Should().BeTrue();
    }

    [Fact]
    public void Parse_DryRunWithOffline_SetsBothFlags()
    {
        var args = CliArguments.Parse(new[] { "--dry-run", "--offline" });
        args.DryRun.Should().BeTrue();
        args.Offline.Should().BeTrue();
    }

    [Fact]
    public void Parse_ValidateSchemas_WithStrict_SetsBothFlags()
    {
        var args = CliArguments.Parse(new[] { "--validate-schemas", "--strict-schemas" });
        args.ValidateSchemas.Should().BeTrue();
        args.StrictSchemas.Should().BeTrue();
    }

    [Fact]
    public void Parse_SymbolsAdd_ParsesValue()
    {
        var args = CliArguments.Parse(new[] { "--symbols-add", "AAPL,MSFT" });
        args.SymbolsAdd.Should().Be("AAPL,MSFT");
        args.HasSymbolCommand.Should().BeTrue();
    }

    [Fact]
    public void Parse_SymbolsRemove_ParsesValue()
    {
        var args = CliArguments.Parse(new[] { "--symbols-remove", "TSLA" });
        args.SymbolsRemove.Should().Be("TSLA");
        args.HasSymbolCommand.Should().BeTrue();
    }

    [Fact]
    public void Parse_SymbolStatus_ParsesValue()
    {
        var args = CliArguments.Parse(new[] { "--symbol-status", "SPY" });
        args.SymbolStatus.Should().Be("SPY");
        args.HasSymbolCommand.Should().BeTrue();
    }

    [Fact]
    public void Parse_SymbolsFlag_SetsSymbols()
    {
        var args = CliArguments.Parse(new[] { "--symbols" });
        args.Symbols.Should().BeTrue();
        args.HasSymbolCommand.Should().BeTrue();
    }

    [Fact]
    public void Parse_SymbolsMonitored_SetsFlag()
    {
        var args = CliArguments.Parse(new[] { "--symbols-monitored" });
        args.SymbolsMonitored.Should().BeTrue();
        args.HasSymbolCommand.Should().BeTrue();
    }

    [Fact]
    public void Parse_SymbolsArchived_SetsFlag()
    {
        var args = CliArguments.Parse(new[] { "--symbols-archived" });
        args.SymbolsArchived.Should().BeTrue();
        args.HasSymbolCommand.Should().BeTrue();
    }

    [Fact]
    public void Parse_SymbolAddOptions_ParsesAllFlags()
    {
        var args = CliArguments.Parse(new[] { "--symbols-add", "AAPL", "--no-trades", "--no-depth", "--depth-levels", "5", "--update" });
        args.NoTrades.Should().BeTrue();
        args.NoDepth.Should().BeTrue();
        args.DepthLevels.Should().Be(5);
        args.Update.Should().BeTrue();
    }

    [Fact]
    public void Parse_DepthLevels_DefaultsTo10_WhenNotSpecified()
    {
        var args = CliArguments.Parse(new[] { "--symbols-add", "AAPL" });
        args.DepthLevels.Should().Be(10);
    }

    [Fact]
    public void Parse_DepthLevels_DefaultsTo10_WhenInvalidValue()
    {
        var args = CliArguments.Parse(new[] { "--depth-levels", "invalid" });
        args.DepthLevels.Should().Be(10);
    }

    [Fact]
    public void Parse_ConfigPath_ParsesValue()
    {
        var args = CliArguments.Parse(new[] { "--config", "/path/to/config.json" });
        args.ConfigPath.Should().Be("/path/to/config.json");
    }

    [Fact]
    public void Parse_HttpPort_ParsesValue()
    {
        var args = CliArguments.Parse(new[] { "--http-port", "9000" });
        args.HttpPort.Should().Be(9000);
    }

    [Fact]
    public void Parse_HttpPort_NullWhenInvalid()
    {
        var args = CliArguments.Parse(new[] { "--http-port", "abc" });
        args.HttpPort.Should().BeNull();
    }

    [Fact]
    public void Parse_Mode_ParsesValue()
    {
        var args = CliArguments.Parse(new[] { "--mode", "web" });
        args.Mode.Should().Be("web");
    }

    [Fact]
    public void Parse_Replay_ParsesValue()
    {
        var args = CliArguments.Parse(new[] { "--replay", "/path/to/events.jsonl" });
        args.Replay.Should().Be("/path/to/events.jsonl");
    }

    [Fact]
    public void Parse_BackfillOptions_ParsesAll()
    {
        var args = CliArguments.Parse(new[] { "--backfill", "--backfill-provider", "stooq", "--backfill-symbols", "SPY,AAPL", "--backfill-from", "2024-01-01", "--backfill-to", "2024-12-31" });
        args.Backfill.Should().BeTrue();
        args.BackfillProvider.Should().Be("stooq");
        args.BackfillSymbols.Should().Be("SPY,AAPL");
        args.BackfillFrom.Should().Be("2024-01-01");
        args.BackfillTo.Should().Be("2024-12-31");
    }

    [Fact]
    public void Parse_RawArgs_PreservesOriginal()
    {
        var rawArgs = new[] { "--help", "--verbose" };
        var args = CliArguments.Parse(rawArgs);
        args.Raw.Should().BeEquivalentTo(rawArgs);
    }

    [Fact]
    public void Parse_MultipleFlagsAndValues_ParsesCorrectly()
    {
        var args = CliArguments.Parse(new[] { "--dry-run", "--offline", "--config", "my.json", "--validate-schemas", "--strict-schemas" });
        args.DryRun.Should().BeTrue();
        args.Offline.Should().BeTrue();
        args.ConfigPath.Should().Be("my.json");
        args.ValidateSchemas.Should().BeTrue();
        args.StrictSchemas.Should().BeTrue();
    }

    [Fact]
    public void Parse_ValueAtEndOfArgs_ReturnsNull()
    {
        // --symbols-add at the end with no following value
        var args = CliArguments.Parse(new[] { "--symbols-add" });
        args.SymbolsAdd.Should().BeNull();
    }

    [Fact]
    public void HasFlag_CaseInsensitive_ReturnsTrue()
    {
        CliArguments.HasFlag(new[] { "--DRY-RUN" }, "--dry-run").Should().BeTrue();
    }

    [Fact]
    public void HasFlag_Missing_ReturnsFalse()
    {
        CliArguments.HasFlag(new[] { "--help" }, "--dry-run").Should().BeFalse();
    }

    [Fact]
    public void GetValue_ReturnsSubsequentArg()
    {
        CliArguments.GetValue(new[] { "--config", "test.json" }, "--config").Should().Be("test.json");
    }

    [Fact]
    public void GetValue_Missing_ReturnsNull()
    {
        CliArguments.GetValue(new[] { "--help" }, "--config").Should().BeNull();
    }

    [Fact]
    public void GetValue_AtEnd_ReturnsNull()
    {
        CliArguments.GetValue(new[] { "--config" }, "--config").Should().BeNull();
    }

    [Fact]
    public void Parse_WatchConfig_SetsFlag()
    {
        var args = CliArguments.Parse(new[] { "--watch-config" });
        args.WatchConfig.Should().BeTrue();
    }

    [Fact]
    public void Parse_SimulateFeed_SetsFlag()
    {
        var args = CliArguments.Parse(new[] { "--simulate-feed" });
        args.SimulateFeed.Should().BeTrue();
    }

    [Fact]
    public void Parse_ValidateConfig_SetsFlag()
    {
        var args = CliArguments.Parse(new[] { "--validate-config" });
        args.ValidateConfig.Should().BeTrue();
    }
}
