using FluentAssertions;
using Meridian.Ui.Services;

namespace Meridian.Ui.Tests.Services;

/// <summary>
/// Tests for <see cref="DiagnosticsService"/> result DTOs, model defaults,
/// severity classifications, and data integrity.
/// Note: The service methods require a running backend (ApiClientService),
/// so these tests focus on the result models and their behavior.
/// </summary>
public sealed class DiagnosticsServiceTests
{
    // ── Singleton ────────────────────────────────────────────────────

    [Fact]
    public void Instance_ShouldReturnSingleton()
    {
        var a = DiagnosticsService.Instance;
        var b = DiagnosticsService.Instance;
        a.Should().BeSameAs(b);
    }

    // ── DryRunResult model ───────────────────────────────────────────

    [Fact]
    public void DryRunResult_ShouldHaveDefaults()
    {
        var result = new DryRunResult();
        result.Success.Should().BeFalse();
        result.ConfigurationValid.Should().BeFalse();
        result.CredentialsValid.Should().BeFalse();
        result.StorageWritable.Should().BeFalse();
        result.ProvidersReachable.Should().BeFalse();
        result.SymbolsValidated.Should().Be(0);
        result.Warnings.Should().NotBeNull().And.BeEmpty();
        result.Errors.Should().NotBeNull().And.BeEmpty();
        result.ValidationDetails.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void DryRunResult_ShouldAcceptValues()
    {
        var result = new DryRunResult
        {
            Success = true,
            ConfigurationValid = true,
            CredentialsValid = true,
            StorageWritable = true,
            ProvidersReachable = true,
            SymbolsValidated = 5,
            Warnings = new List<string> { "rate limit close" },
            Errors = new List<string>(),
            ValidationDetails = new List<ValidationDetail>
            {
                new() { Category = "Config", Item = "Symbols", Valid = true, Message = "OK" }
            }
        };

        result.Success.Should().BeTrue();
        result.SymbolsValidated.Should().Be(5);
        result.Warnings.Should().HaveCount(1);
        result.ValidationDetails.Should().HaveCount(1);
    }

    // ── ValidationDetail model ───────────────────────────────────────

    [Fact]
    public void ValidationDetail_ShouldStoreValues()
    {
        var detail = new ValidationDetail
        {
            Category = "Storage",
            Item = "DiskSpace",
            Valid = true,
            Message = "Sufficient space"
        };

        detail.Category.Should().Be("Storage");
        detail.Item.Should().Be("DiskSpace");
        detail.Valid.Should().BeTrue();
        detail.Message.Should().Be("Sufficient space");
    }

    // ── PreflightResult model ────────────────────────────────────────

    [Fact]
    public void PreflightResult_ShouldHaveDefaults()
    {
        var result = new PreflightResult();
        result.Success.Should().BeFalse();
        result.PassedCount.Should().Be(0);
        result.FailedCount.Should().Be(0);
        result.Checks.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void PreflightResult_ShouldCountPassedAndFailed()
    {
        var result = new PreflightResult
        {
            Success = true,
            PassedCount = 4,
            FailedCount = 1,
            Checks = new List<PreflightCheck>
            {
                new() { Name = "Network", Passed = true, Severity = CheckSeverity.Info },
                new() { Name = "Storage", Passed = true, Severity = CheckSeverity.Info },
                new() { Name = "Config", Passed = true, Severity = CheckSeverity.Info },
                new() { Name = "Symbols", Passed = true, Severity = CheckSeverity.Info },
                new() { Name = "Provider", Passed = false, Severity = CheckSeverity.Warning }
            }
        };

        result.PassedCount.Should().Be(4);
        result.FailedCount.Should().Be(1);
        result.Checks.Count(c => c.Passed).Should().Be(4);
    }

    // ── PreflightCheck model ─────────────────────────────────────────

    [Fact]
    public void PreflightCheck_ShouldHaveDefaults()
    {
        var check = new PreflightCheck();
        check.Name.Should().BeEmpty();
        check.Category.Should().BeEmpty();
        check.Passed.Should().BeFalse();
        check.Message.Should().BeEmpty();
        check.Severity.Should().Be(CheckSeverity.Info);
    }

    // ── CheckSeverity enum ───────────────────────────────────────────

    [Theory]
    [InlineData(CheckSeverity.Info)]
    [InlineData(CheckSeverity.Warning)]
    [InlineData(CheckSeverity.Critical)]
    public void CheckSeverity_AllValues_ShouldBeDefined(CheckSeverity severity)
    {
        Enum.IsDefined(typeof(CheckSeverity), severity).Should().BeTrue();
    }

    // ── DiagnosticBundleOptions model ────────────────────────────────

    [Fact]
    public void DiagnosticBundleOptions_ShouldHaveDefaults()
    {
        var opts = new DiagnosticBundleOptions();
        opts.IncludeLogs.Should().BeTrue();
        opts.IncludeConfig.Should().BeTrue();
        opts.IncludeMetrics.Should().BeTrue();
        opts.IncludeSampleData.Should().BeFalse();
        opts.LogDays.Should().Be(7);
        opts.RedactSecrets.Should().BeTrue();
    }

    // ── DiagnosticBundleResult model ─────────────────────────────────

    [Fact]
    public void DiagnosticBundleResult_ShouldHaveDefaults()
    {
        var result = new DiagnosticBundleResult();
        result.Success.Should().BeFalse();
        result.Error.Should().BeNull();
        result.BundlePath.Should().BeNull();
        result.FileSizeBytes.Should().Be(0);
        result.IncludedFiles.Should().NotBeNull().And.BeEmpty();
    }

    // ── DiagnosticSystemMetrics model ────────────────────────────────

    [Fact]
    public void DiagnosticSystemMetrics_ShouldHaveDefaults()
    {
        var metrics = new DiagnosticSystemMetrics();
        metrics.CpuUsagePercent.Should().Be(0);
        metrics.MemoryUsedBytes.Should().Be(0);
        metrics.MemoryTotalBytes.Should().Be(0);
        metrics.DiskUsagePercent.Should().Be(0);
        metrics.ActiveConnections.Should().Be(0);
        metrics.ActiveSubscriptions.Should().Be(0);
        metrics.EventsPerSecond.Should().Be(0);
        metrics.TotalEventsProcessed.Should().Be(0);
        metrics.Uptime.Should().Be(TimeSpan.Zero);
    }

    // ── ValidationResult model ───────────────────────────────────────

    [Fact]
    public void ValidationResult_ShouldHaveDefaults()
    {
        var result = new ValidationResult();
        result.Valid.Should().BeFalse();
        result.Error.Should().BeNull();
        result.Suggestion.Should().BeNull();
    }

    // ── DiagnosticProviderTestResult model ───────────────────────────

    [Fact]
    public void DiagnosticProviderTestResult_ShouldHaveDefaults()
    {
        var result = new DiagnosticProviderTestResult();
        result.Success.Should().BeFalse();
        result.Error.Should().BeNull();
        result.LatencyMs.Should().Be(0);
        result.Version.Should().BeNull();
        result.Capabilities.Should().BeNull();
    }

    // ── QuickCheckResult model ───────────────────────────────────────

    [Fact]
    public void QuickCheckResult_ShouldHaveDefaults()
    {
        var result = new QuickCheckResult();
        result.Success.Should().BeFalse();
        result.Error.Should().BeNull();
        result.Overall.Should().BeEmpty();
        result.Checks.Should().NotBeNull().And.BeEmpty();
    }

    // ── SelfTestOptions model ────────────────────────────────────────

    [Fact]
    public void SelfTestOptions_ShouldHaveDefaults()
    {
        var opts = new SelfTestOptions();
        opts.TestStorage.Should().BeTrue();
        opts.TestProviders.Should().BeTrue();
        opts.TestConfiguration.Should().BeTrue();
        opts.TestNetwork.Should().BeTrue();
    }

    // ── SelfTestResult model ─────────────────────────────────────────

    [Fact]
    public void SelfTestResult_ShouldHaveDefaults()
    {
        var result = new SelfTestResult();
        result.Success.Should().BeFalse();
        result.Tests.Should().NotBeNull().And.BeEmpty();
        result.PassedCount.Should().Be(0);
        result.FailedCount.Should().Be(0);
        result.SkippedCount.Should().Be(0);
    }

    // ── ErrorCodeInfo model ──────────────────────────────────────────

    [Fact]
    public void ErrorCodeInfo_ShouldStoreValues()
    {
        var info = new ErrorCodeInfo
        {
            Code = "ERR-001",
            Category = "Connection",
            Description = "Connection refused",
            Resolution = "Check the service URL",
            Severity = "Error"
        };

        info.Code.Should().Be("ERR-001");
        info.Category.Should().Be("Connection");
        info.Description.Should().Be("Connection refused");
        info.Resolution.Should().Be("Check the service URL");
        info.Severity.Should().Be("Error");
    }

    // ── ConfigIssue model ────────────────────────────────────────────

    [Fact]
    public void ConfigIssue_ShouldHaveDefaults()
    {
        var issue = new ConfigIssue();
        issue.Section.Should().BeEmpty();
        issue.Key.Should().BeEmpty();
        issue.Severity.Should().BeEmpty();
        issue.Message.Should().BeEmpty();
        issue.Suggestion.Should().BeNull();
    }

    // ── ProviderCredentialStatus model ───────────────────────────────

    [Fact]
    public void ProviderCredentialStatus_ShouldStoreValues()
    {
        var status = new ProviderCredentialStatus
        {
            Provider = "Alpaca",
            IsValid = true,
            Status = "Active",
            ExpiresAt = new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc)
        };

        status.Provider.Should().Be("Alpaca");
        status.IsValid.Should().BeTrue();
        status.Status.Should().Be("Active");
        status.ExpiresAt.Should().NotBeNull();
    }

    // ── ProviderConnectivityResult model ─────────────────────────────

    [Fact]
    public void ProviderConnectivityResult_ShouldStoreValues()
    {
        var result = new ProviderConnectivityResult
        {
            Provider = "Polygon",
            Connected = true,
            LatencyMs = 45.6,
            Version = "2.0"
        };

        result.Provider.Should().Be("Polygon");
        result.Connected.Should().BeTrue();
        result.LatencyMs.Should().Be(45.6);
        result.Version.Should().Be("2.0");
    }

    // ── QuickCheckItem model ─────────────────────────────────────────

    [Fact]
    public void QuickCheckItem_ShouldHaveDefaults()
    {
        var item = new QuickCheckItem();
        item.Name.Should().BeEmpty();
        item.Status.Should().BeEmpty();
        item.Details.Should().BeNull();
    }

    // ── SelfTestItem model ───────────────────────────────────────────

    [Fact]
    public void SelfTestItem_ShouldHaveDefaults()
    {
        var item = new SelfTestItem();
        item.Name.Should().BeEmpty();
        item.Category.Should().BeEmpty();
        item.Status.Should().BeEmpty();
        item.Message.Should().BeNull();
        item.DurationMs.Should().Be(0);
    }

    // ── ShowConfigResult model ───────────────────────────────────────

    [Fact]
    public void ShowConfigResult_ShouldHaveDefaults()
    {
        var result = new ShowConfigResult();
        result.Success.Should().BeFalse();
        result.Error.Should().BeNull();
        result.Sections.Should().NotBeNull().And.BeEmpty();
    }

    // ── ConfigSection / ConfigItem models ────────────────────────────

    [Fact]
    public void ConfigSection_ShouldContainItems()
    {
        var section = new ConfigSection
        {
            Name = "DataSource",
            Items = new List<ConfigItem>
            {
                new() { Key = "ActiveProvider", Value = "Alpaca", Source = "config file", IsSensitive = false },
                new() { Key = "ApiKey", Value = "****", Source = "env var", IsSensitive = true }
            }
        };

        section.Name.Should().Be("DataSource");
        section.Items.Should().HaveCount(2);
        section.Items[1].IsSensitive.Should().BeTrue();
    }
}
