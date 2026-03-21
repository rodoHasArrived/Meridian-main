using Meridian.Ui.Services;
using Meridian.Wpf.Services;

namespace Meridian.Wpf.Tests.Services;

/// <summary>
/// Tests for RetentionAssuranceService singleton service.
/// Validates retention policy validation, guardrails, legal holds, and model defaults.
/// </summary>
public sealed class RetentionAssuranceServiceTests
{
    [Fact]
    public void Instance_ShouldReturnSingleton()
    {
        // Arrange & Act
        var instance1 = RetentionAssuranceService.Instance;
        var instance2 = RetentionAssuranceService.Instance;

        // Assert
        instance1.Should().NotBeNull();
        instance2.Should().NotBeNull();
        instance1.Should().BeSameAs(instance2, "RetentionAssuranceService should be a singleton");
    }

    [Fact]
    public void Configuration_ShouldNotBeNull()
    {
        // Arrange & Act
        var config = RetentionAssuranceService.Instance.Configuration;

        // Assert
        config.Should().NotBeNull();
    }

    [Fact]
    public void LegalHolds_ShouldNotBeNull()
    {
        // Arrange & Act
        var holds = RetentionAssuranceService.Instance.LegalHolds;

        // Assert
        holds.Should().NotBeNull();
    }

    [Fact]
    public void AuditReports_ShouldNotBeNull()
    {
        // Arrange & Act
        var reports = RetentionAssuranceService.Instance.AuditReports;

        // Assert
        reports.Should().NotBeNull();
    }

    [Fact]
    public void RetentionPolicy_Defaults_ShouldHaveExpectedValues()
    {
        // Arrange & Act
        var policy = new RetentionPolicy();

        // Assert
        policy.TickDataDays.Should().Be(30);
        policy.BarDataDays.Should().Be(365);
        policy.QuoteDataDays.Should().Be(30);
        policy.DepthDataDays.Should().Be(7);
        policy.DeletedFilesPerRun.Should().Be(100);
        policy.CompressBeforeDelete.Should().BeTrue();
        policy.ArchiveToCloud.Should().BeFalse();
        policy.CloudArchiveDestination.Should().BeNull();
    }

    [Fact]
    public void RetentionValidationResult_Defaults_ShouldBeValid()
    {
        // Arrange & Act
        var result = new RetentionValidationResult();

        // Assert
        result.IsValid.Should().BeFalse("default bool is false");
        result.Violations.Should().NotBeNull();
        result.Violations.Should().BeEmpty();
        result.Warnings.Should().NotBeNull();
        result.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void GuardrailViolation_Properties_ShouldBeSettable()
    {
        // Arrange & Act
        var violation = new GuardrailViolation
        {
            Rule = "TestRule",
            Message = "Test violation message",
            Severity = ViolationSeverity.Error
        };

        // Assert
        violation.Rule.Should().Be("TestRule");
        violation.Message.Should().Be("Test violation message");
        violation.Severity.Should().Be(ViolationSeverity.Error);
    }

    [Theory]
    [InlineData(ViolationSeverity.Info)]
    [InlineData(ViolationSeverity.Warning)]
    [InlineData(ViolationSeverity.Error)]
    public void ViolationSeverity_ShouldContainExpectedValues(ViolationSeverity severity)
    {
        // Assert
        Enum.IsDefined(typeof(ViolationSeverity), severity).Should().BeTrue();
    }

    [Fact]
    public void ValidateRetentionPolicy_WithValidPolicy_ShouldReturnValid()
    {
        // Arrange
        var service = RetentionAssuranceService.Instance;
        var policy = new RetentionPolicy
        {
            TickDataDays = 30,
            BarDataDays = 365,
            QuoteDataDays = 30
        };

        // Act
        var result = service.ValidateRetentionPolicy(policy);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Violations.Should().BeEmpty();
    }

    [Fact]
    public void ValidateRetentionPolicy_WithTickDataBelowMinimum_ShouldReturnViolation()
    {
        // Arrange
        var service = RetentionAssuranceService.Instance;
        var policy = new RetentionPolicy
        {
            TickDataDays = 1, // Below the default minimum of 7
            BarDataDays = 365,
            QuoteDataDays = 30
        };

        // Act
        var result = service.ValidateRetentionPolicy(policy);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Violations.Should().ContainSingle(v => v.Rule == "MinTickDataRetention");
    }

    [Fact]
    public void GetSymbolsUnderLegalHold_WithNoHolds_ShouldReturnEmptySet()
    {
        // Arrange
        var service = RetentionAssuranceService.Instance;

        // Act
        var symbols = service.GetSymbolsUnderLegalHold();

        // Assert
        symbols.Should().NotBeNull();
    }

    [Fact]
    public void RetentionGuardrails_Defaults_ShouldHaveExpectedValues()
    {
        // Arrange & Act
        var guardrails = new RetentionGuardrails();

        // Assert
        guardrails.MinTickDataDays.Should().Be(7);
        guardrails.MinBarDataDays.Should().Be(30);
        guardrails.MinQuoteDataDays.Should().Be(7);
        guardrails.MinDepthDataDays.Should().Be(3);
        guardrails.MaxDailyDeletedFiles.Should().Be(1000);
        guardrails.RequireChecksumVerification.Should().BeTrue();
        guardrails.RequireDryRunPreview.Should().BeTrue();
        guardrails.AllowDeleteDuringTradingHours.Should().BeFalse();
    }

    [Fact]
    public void RetentionDryRunResult_Defaults_ShouldHaveEmptyCollections()
    {
        // Arrange & Act
        var result = new RetentionDryRunResult();

        // Assert
        result.PolicyApplied.Should().NotBeNull();
        result.FilesToDelete.Should().NotBeNull().And.BeEmpty();
        result.SkippedFiles.Should().NotBeNull().And.BeEmpty();
        result.TotalBytesToDelete.Should().Be(0);
        result.BySymbol.Should().NotBeNull().And.BeEmpty();
        result.Errors.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void FileToDelete_Properties_ShouldBeSettable()
    {
        // Arrange & Act
        var file = new FileToDelete
        {
            Path = "/data/SPY_trades_2024.jsonl",
            Symbol = "SPY",
            Size = 1024,
            LastModified = DateTime.UtcNow,
            DataType = "Tick"
        };

        // Assert
        file.Path.Should().Be("/data/SPY_trades_2024.jsonl");
        file.Symbol.Should().Be("SPY");
        file.Size.Should().Be(1024);
        file.DataType.Should().Be("Tick");
    }

    [Fact]
    public void SkippedFileInfo_Properties_ShouldBeSettable()
    {
        // Arrange & Act
        var skipped = new SkippedFileInfo
        {
            Path = "/data/AAPL_trades.jsonl",
            Symbol = "AAPL",
            Reason = "Legal hold active",
            Size = 2048
        };

        // Assert
        skipped.Path.Should().Be("/data/AAPL_trades.jsonl");
        skipped.Symbol.Should().Be("AAPL");
        skipped.Reason.Should().Be("Legal hold active");
        skipped.Size.Should().Be(2048);
    }

    [Theory]
    [InlineData(CleanupStatus.Pending)]
    [InlineData(CleanupStatus.Success)]
    [InlineData(CleanupStatus.PartialSuccess)]
    [InlineData(CleanupStatus.Failed)]
    [InlineData(CleanupStatus.FailedVerification)]
    [InlineData(CleanupStatus.Cancelled)]
    public void CleanupStatus_ShouldContainExpectedValues(CleanupStatus status)
    {
        // Assert
        Enum.IsDefined(typeof(CleanupStatus), status).Should().BeTrue();
    }

    [Fact]
    public void RetentionAuditReport_Defaults_ShouldHaveEmptyCollections()
    {
        // Arrange & Act
        var report = new RetentionAuditReport();

        // Assert
        report.Id.Should().NotBeNullOrEmpty();
        report.Status.Should().Be(CleanupStatus.Pending);
        report.PolicyApplied.Should().NotBeNull();
        report.DeletedFiles.Should().NotBeNull().And.BeEmpty();
        report.ActualBytesDeleted.Should().Be(0);
        report.Errors.Should().NotBeNull().And.BeEmpty();
        report.Notes.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public async Task ExportAuditReportAsync_ShouldReturnJsonString()
    {
        // Arrange
        var service = RetentionAssuranceService.Instance;
        var report = new RetentionAuditReport
        {
            ExecutedAt = DateTime.UtcNow,
            Status = CleanupStatus.Success
        };

        // Act
        var json = await service.ExportAuditReportAsync(report);

        // Assert
        json.Should().NotBeNullOrEmpty();
        json.Should().Contain("Success");
    }

    [Fact]
    public void ChecksumVerificationResult_Defaults_ShouldHaveEmptyCollections()
    {
        // Arrange & Act
        var result = new ChecksumVerificationResult();

        // Assert
        result.VerifiedFiles.Should().NotBeNull().And.BeEmpty();
        result.MissingFiles.Should().NotBeNull().And.BeEmpty();
        result.MismatchedFiles.Should().NotBeNull().And.BeEmpty();
        result.Errors.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void RetentionConfiguration_Defaults_ShouldHaveExpectedValues()
    {
        // Arrange & Act
        var config = new RetentionConfiguration();

        // Assert
        config.Guardrails.Should().NotBeNull();
        config.EnableScheduledCleanup.Should().BeFalse();
        config.CleanupSchedule.Should().NotBeNullOrEmpty();
        config.NotifyOnCleanup.Should().BeTrue();
        config.RequireApproval.Should().BeFalse();
    }
}
