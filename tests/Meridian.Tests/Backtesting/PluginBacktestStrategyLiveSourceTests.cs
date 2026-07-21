using FluentAssertions;
using Meridian.Backtesting.Plugins;
using Meridian.Backtesting.Sdk;
using Meridian.Strategies.Live;
using Xunit;

namespace Meridian.Tests.Backtesting;

/// <summary>
/// Covers <see cref="PluginBacktestStrategyLiveSource"/>: resolution of user-authored
/// <see cref="IBacktestStrategy"/> plugins for live execution, including the directory and
/// file-name guardrails that keep runs from loading arbitrary assemblies.
/// </summary>
public sealed class PluginBacktestStrategyLiveSourceTests
{
    /// <summary>
    /// Loaded end-to-end through <see cref="StrategyPluginLoader"/> from this test assembly's
    /// path, standing in for a user-authored plugin strategy.
    /// </summary>
    public sealed class SamplePluginStrategy : BacktestStrategyBase
    {
        public override string Name => "sample-plugin-strategy";

        [StrategyParameter("Fast period")]
        public int FastPeriod { get; set; } = 10;
    }

    private static LiveStrategyCreationContext Context(
        string strategyId = "user-run",
        params (string Key, string Value)[] parameters)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in parameters)
        {
            map[key] = value;
        }

        return new LiveStrategyCreationContext(strategyId, map);
    }

    [Fact]
    public void TryCreate_WithoutPluginAssemblyParameter_IsSilentlyNotHandled()
    {
        var source = new PluginBacktestStrategyLiveSource(pluginDirectory: Path.GetTempPath());

        var handled = source.TryCreate(Context(), out var strategy, out var failureReason);

        handled.Should().BeFalse();
        strategy.Should().BeNull();
        failureReason.Should().BeNull("non-plugin runs must not produce noise for other fallbacks");
    }

    [Fact]
    public void TryCreate_WithoutConfiguredDirectory_FailsWithConfigurationReason()
    {
        var source = new PluginBacktestStrategyLiveSource(pluginDirectory: null);

        var handled = source.TryCreate(
            Context(parameters: ("pluginAssembly", "user.dll")),
            out var strategy,
            out var failureReason);

        handled.Should().BeFalse();
        strategy.Should().BeNull();
        failureReason.Should().Contain("StrategyPluginDirectory");
    }

    [Theory]
    [InlineData("../outside.dll")]
    [InlineData("sub/dir.dll")]
    [InlineData("not-a-dll.exe")]
    public void TryCreate_WithUnsafeAssemblyName_IsRejected(string assemblyName)
    {
        var source = new PluginBacktestStrategyLiveSource(pluginDirectory: Path.GetTempPath());

        var handled = source.TryCreate(
            Context(parameters: ("pluginAssembly", assemblyName)),
            out var strategy,
            out var failureReason);

        handled.Should().BeFalse();
        strategy.Should().BeNull();
        failureReason.Should().Contain("bare .dll file name");
    }

    [Fact]
    public void TryCreate_WithMissingAssemblyFile_FailsWithNotFoundReason()
    {
        var source = new PluginBacktestStrategyLiveSource(pluginDirectory: Path.GetTempPath());

        var handled = source.TryCreate(
            Context(parameters: ("pluginAssembly", $"missing-{Guid.NewGuid():N}.dll")),
            out var strategy,
            out var failureReason);

        handled.Should().BeFalse();
        strategy.Should().BeNull();
        failureReason.Should().Contain("was not found");
    }

    [Fact]
    public void TryCreate_WithRealAssemblyAndType_LoadsAndBindsParameters()
    {
        // The test assembly itself acts as the plugin: it contains SamplePluginStrategy and
        // lives on disk, so the full StrategyPluginLoader path (isolated load context, type
        // scan, instantiation, parameter binding) is exercised for real.
        var assemblyPath = typeof(PluginBacktestStrategyLiveSourceTests).Assembly.Location;
        var source = new PluginBacktestStrategyLiveSource(Path.GetDirectoryName(assemblyPath));

        var handled = source.TryCreate(
            Context(
                strategyId: "promoted-user-run",
                parameters:
                [
                    ("pluginAssembly", Path.GetFileName(assemblyPath)),
                    ("pluginType", typeof(SamplePluginStrategy).FullName!),
                    ("FastPeriod", "25")
                ]),
            out var strategy,
            out var failureReason);

        handled.Should().BeTrue(failureReason);
        strategy.Should().NotBeNull();
        strategy!.Name.Should().Be("sample-plugin-strategy");

        // The instance comes from an isolated load context, so read the bound
        // parameter reflectively rather than casting across contexts.
        var boundValue = strategy.GetType().GetProperty(nameof(SamplePluginStrategy.FastPeriod))!.GetValue(strategy);
        boundValue.Should().Be(25);
    }

    [Fact]
    public void TryCreate_WithUnconvertibleParameter_FailsClosed()
    {
        // A live strategy must never launch with a default value silently substituted for an
        // unconvertible operator-supplied parameter.
        var assemblyPath = typeof(PluginBacktestStrategyLiveSourceTests).Assembly.Location;
        var source = new PluginBacktestStrategyLiveSource(Path.GetDirectoryName(assemblyPath));

        var handled = source.TryCreate(
            Context(parameters:
            [
                ("pluginAssembly", Path.GetFileName(assemblyPath)),
                ("pluginType", typeof(SamplePluginStrategy).FullName!),
                ("FastPeriod", "not-a-number")
            ]),
            out var strategy,
            out var failureReason);

        handled.Should().BeFalse();
        strategy.Should().BeNull();
        failureReason.Should().Contain("FastPeriod");
    }

    [Fact]
    public void TryCreate_WithUnknownPluginType_ListsAvailableStrategies()
    {
        var assemblyPath = typeof(PluginBacktestStrategyLiveSourceTests).Assembly.Location;
        var source = new PluginBacktestStrategyLiveSource(Path.GetDirectoryName(assemblyPath));

        var handled = source.TryCreate(
            Context(parameters:
            [
                ("pluginAssembly", Path.GetFileName(assemblyPath)),
                ("pluginType", "NoSuchStrategyType")
            ]),
            out var strategy,
            out var failureReason);

        handled.Should().BeFalse();
        strategy.Should().BeNull();
        failureReason.Should().Contain("NoSuchStrategyType");
    }
}
