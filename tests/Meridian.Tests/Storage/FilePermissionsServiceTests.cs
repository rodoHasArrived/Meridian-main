using FluentAssertions;
using Meridian.Storage.Services;
using Xunit;

namespace Meridian.Tests;

/// <summary>
/// Tests for FilePermissionsService.
/// Validates cross-platform file permission management and diagnostic capabilities.
/// </summary>
public class FilePermissionsServiceTests : IDisposable
{
    private readonly string _testDir;
    private readonly FilePermissionsService _service;

    public FilePermissionsServiceTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"mdc_test_{Guid.NewGuid():N}");
        _service = new FilePermissionsService();
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDir))
            {
                Directory.Delete(_testDir, recursive: true);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    [Fact]
    public void Constructor_ShouldInitializeWithDefaultOptions()
    {
        // Arrange & Act
        var service = new FilePermissionsService();

        // Assert
        service.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_ShouldAcceptCustomOptions()
    {
        // Arrange
        var options = new FilePermissionsOptions
        {
            DirectoryMode = "700",
            FileMode = "600",
            ApplyToExistingFiles = true,
            RestrictToCurrentUser = true,
            ValidateOnStartup = true
        };

        // Act
        var service = new FilePermissionsService(options);

        // Assert
        service.Should().NotBeNull();
    }

    [Fact]
    public void EnsureDirectoryPermissions_WithNullPath_ShouldReturnFailure()
    {
        // Act
        var result = _service.EnsureDirectoryPermissions(null!);

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("null or empty");
    }

    [Fact]
    public void EnsureDirectoryPermissions_WithEmptyPath_ShouldReturnFailure()
    {
        // Act
        var result = _service.EnsureDirectoryPermissions("");

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("null or empty");
    }

    [Fact]
    public void EnsureDirectoryPermissions_ShouldCreateDirectoryIfNotExists()
    {
        // Arrange
        Directory.Exists(_testDir).Should().BeFalse();

        // Act
        var result = _service.EnsureDirectoryPermissions(_testDir);

        // Assert
        result.Success.Should().BeTrue();
        Directory.Exists(_testDir).Should().BeTrue();
    }

    [Fact]
    public void EnsureDirectoryPermissions_ShouldSucceedForExistingDirectory()
    {
        // Arrange
        Directory.CreateDirectory(_testDir);

        // Act
        var result = _service.EnsureDirectoryPermissions(_testDir);

        // Assert
        result.Success.Should().BeTrue();
    }

    [Fact]
    public void EnsureDirectoryPermissions_ShouldHandleNestedPath()
    {
        // Arrange
        var nestedPath = Path.Combine(_testDir, "level1", "level2", "level3");

        // Act
        var result = _service.EnsureDirectoryPermissions(nestedPath);

        // Assert
        result.Success.Should().BeTrue();
        Directory.Exists(nestedPath).Should().BeTrue();
    }

    [Fact]
    public void VerifyWriteAccess_ShouldSucceedForWritableDirectory()
    {
        // Arrange
        Directory.CreateDirectory(_testDir);

        // Act
        var result = _service.VerifyWriteAccess(_testDir);

        // Assert
        result.Success.Should().BeTrue();
        result.Message.Should().Contain("verified");
    }

    [Fact]
    public void VerifyWriteAccess_ShouldFailForNonExistentDirectory()
    {
        // Act
        var result = _service.VerifyWriteAccess(_testDir);

        // Assert
        result.Success.Should().BeFalse();
    }

    [Fact]
    public void GetPermissionsDiagnostic_ShouldReportNonExistentDirectory()
    {
        // Act
        var diagnostic = _service.GetPermissionsDiagnostic(_testDir);

        // Assert
        diagnostic.Exists.Should().BeFalse();
        diagnostic.HasIssues.Should().BeTrue();
        diagnostic.Issues.Should().Contain("Directory does not exist");
    }

    [Fact]
    public void GetPermissionsDiagnostic_ShouldReportExistingDirectory()
    {
        // Arrange
        Directory.CreateDirectory(_testDir);

        // Act
        var diagnostic = _service.GetPermissionsDiagnostic(_testDir);

        // Assert
        diagnostic.Exists.Should().BeTrue();
        diagnostic.CanRead.Should().BeTrue();
        diagnostic.CanWrite.Should().BeTrue();
        diagnostic.DirectoryPath.Should().Be(_testDir);
    }

    [Fact]
    public void GetPermissionsDiagnostic_ShouldIncludePlatformInfo()
    {
        // Arrange
        Directory.CreateDirectory(_testDir);

        // Act
        var diagnostic = _service.GetPermissionsDiagnostic(_testDir);

        // Assert
        diagnostic.Platform.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void GetPermissionsDiagnostic_ToString_ShouldFormatCorrectly()
    {
        // Arrange
        Directory.CreateDirectory(_testDir);

        // Act
        var diagnostic = _service.GetPermissionsDiagnostic(_testDir);
        var str = diagnostic.ToString();

        // Assert
        str.Should().Contain("[OK]");
        str.Should().Contain(_testDir);
        str.Should().Contain("read=True");
        str.Should().Contain("write=True");
    }

    [Fact]
    public void FilePermissionsResult_CreateSuccess_ShouldHaveCorrectProperties()
    {
        // Act
        var result = FilePermissionsResult.CreateSuccess("Test message");

        // Assert
        result.Success.Should().BeTrue();
        result.Message.Should().Be("Test message");
        result.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void FilePermissionsResult_Failure_ShouldHaveCorrectProperties()
    {
        // Act
        var result = FilePermissionsResult.Failure("Error message");

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().Be("Error message");
    }

    [Fact]
    public void FilePermissionsOptions_ShouldHaveDefaultValues()
    {
        // Act
        var options = new FilePermissionsOptions();

        // Assert
        options.DirectoryMode.Should().Be("755");
        options.FileMode.Should().Be("644");
        options.ApplyToExistingFiles.Should().BeFalse();
        options.RestrictToCurrentUser.Should().BeFalse();
        options.ValidateOnStartup.Should().BeTrue();
    }

    [Fact]
    public void EnsureDirectoryPermissions_WithApplyToExistingFiles_ShouldProcessFiles()
    {
        // Arrange
        Directory.CreateDirectory(_testDir);
        var testFile = Path.Combine(_testDir, "test.txt");
        File.WriteAllText(testFile, "test content");

        var options = new FilePermissionsOptions { ApplyToExistingFiles = true };
        var service = new FilePermissionsService(options);

        // Act
        var result = service.EnsureDirectoryPermissions(_testDir);

        // Assert
        result.Success.Should().BeTrue();
    }

    [Fact]
    public void EnsureDirectoryPermissions_WithNestedFilesAndApplyToExisting_ShouldProcessAll()
    {
        // Arrange
        var subDir = Path.Combine(_testDir, "subdir");
        Directory.CreateDirectory(subDir);
        File.WriteAllText(Path.Combine(_testDir, "root.txt"), "root");
        File.WriteAllText(Path.Combine(subDir, "nested.txt"), "nested");

        var options = new FilePermissionsOptions { ApplyToExistingFiles = true };
        var service = new FilePermissionsService(options);

        // Act
        var result = service.EnsureDirectoryPermissions(_testDir);

        // Assert
        result.Success.Should().BeTrue();
    }
}

/// <summary>
/// Tests for FilePermissionsDiagnostic record.
/// </summary>
public class FilePermissionsDiagnosticTests
{
    [Fact]
    public void HasIssues_WhenNoIssues_ShouldReturnFalse()
    {
        // Arrange
        var diagnostic = new FilePermissionsDiagnostic
        {
            DirectoryPath = "/test",
            Exists = true,
            CanRead = true,
            CanWrite = true
        };

        // Assert
        diagnostic.HasIssues.Should().BeFalse();
        diagnostic.Issues.Should().BeEmpty();
    }

    [Fact]
    public void HasIssues_WhenIssuesExist_ShouldReturnTrue()
    {
        // Arrange
        var diagnostic = new FilePermissionsDiagnostic
        {
            DirectoryPath = "/test",
            Exists = true
        };
        diagnostic.Issues.Add("Test issue");

        // Assert
        diagnostic.HasIssues.Should().BeTrue();
        diagnostic.Issues.Should().Contain("Test issue");
    }

    [Fact]
    public void ToString_WithIssues_ShouldShowIssuesDetected()
    {
        // Arrange
        var diagnostic = new FilePermissionsDiagnostic
        {
            DirectoryPath = "/test",
            Exists = true,
            CanRead = false,
            CanWrite = false
        };
        diagnostic.Issues.Add("Permission denied");

        // Act
        var str = diagnostic.ToString();

        // Assert
        str.Should().Contain("[ISSUES DETECTED]");
    }

    [Fact]
    public void ToString_OnUnix_ShouldIncludeMode()
    {
        // Arrange
        var diagnostic = new FilePermissionsDiagnostic
        {
            DirectoryPath = "/test",
            Exists = true,
            CanRead = true,
            CanWrite = true,
            UnixMode = "755"
        };

        // Act
        var str = diagnostic.ToString();

        // Assert
        str.Should().Contain("mode=755");
    }
}
