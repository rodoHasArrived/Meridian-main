using FluentAssertions;
using Meridian.Application.Config;
using Meridian.Application.Services;
using Xunit;

namespace Meridian.Tests.Platform.Runtime;

public sealed class StartupSummaryTests : IDisposable
{
    private readonly string? _originalAlpacaKeyId = Environment.GetEnvironmentVariable("ALPACA_KEY_ID");
    private readonly string _dataRoot;
    private readonly StringWriter _output = new();

    public StartupSummaryTests()
    {
        _dataRoot = Path.Combine(Path.GetTempPath(), "meridian-startup-summary-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dataRoot);
    }

    [Fact]
    public void Display_DesktopModeIncludesPortAndOmitsDesktopTip()
    {
        var summary = new StartupSummary(_output);
        var config = CreateConfig();

        summary.Display(config, "config/appsettings.json", ["--mode", "desktop", "--http-port", "9090"]);

        var text = _output.ToString();
        text.Should().Contain("Mode: Desktop | Port: 9090");
        text.Should().Contain("Configuration: config/appsettings.json");
        text.Should().NotContain("Add --mode desktop");
    }

    [Fact]
    public void DisplayCompact_WritesProviderSymbolCountAndDataRoot()
    {
        var summary = new StartupSummary(_output);
        var config = CreateConfig();

        summary.DisplayCompact(config);

        _output.ToString().Should().Contain("[Interactive Brokers] 1 symbols -> " + _dataRoot);
    }

    [Fact]
    public void PerformQuickCheck_AlpacaWithoutCredentialsReturnsBlockingIssue()
    {
        Environment.SetEnvironmentVariable("ALPACA_KEY_ID", null);
        var summary = new StartupSummary(_output);
        var config = CreateConfig() with
        {
            DataSource = DataSourceKind.Alpaca,
            Alpaca = new AlpacaOptions { KeyId = "", SecretKey = "", Feed = "iex" }
        };

        var result = summary.PerformQuickCheck(config);

        result.Success.Should().BeFalse();
        result.Issues.Should().Contain(issue =>
            issue.Severity == IssueSeverity.Error &&
            issue.Component == "Alpaca" &&
            issue.Message == "Alpaca credentials not configured");
        result.Suggestions.Should().Contain("Set ALPACA_KEY_ID and ALPACA_SECRET_KEY environment variables");
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("ALPACA_KEY_ID", _originalAlpacaKeyId);
        _output.Dispose();

        if (Directory.Exists(_dataRoot))
        {
            Directory.Delete(_dataRoot, recursive: true);
        }
    }

    private AppConfig CreateConfig()
    {
        return new AppConfig
        {
            DataRoot = _dataRoot,
            DataSource = DataSourceKind.IB,
            IB = new IBOptions
            {
                Host = "127.0.0.1",
                Port = 7497,
                ClientId = 1,
                UsePaperTrading = true
            },
            Symbols =
            [
                new SymbolConfig("SPY", SubscribeTrades: true)
            ]
        };
    }
}
