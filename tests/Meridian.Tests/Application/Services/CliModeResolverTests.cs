using FluentAssertions;
using Meridian.Application.Services;
using Xunit;

namespace Meridian.Tests;

public sealed class CliModeResolverTests
{
    [Fact]
    public void TranslateLegacyFlags_WithNoFlags_ShouldReturnNull()
    {
        // Arrange
        var args = Array.Empty<string>();

        // Act
        var result = CliModeResolver.TranslateLegacyFlags(args);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void TranslateLegacyFlags_WithExplicitMode_ShouldReturnMode()
    {
        // Arrange
        var args = new[] { "--mode", "desktop" };

        // Act
        var result = CliModeResolver.TranslateLegacyFlags(args);

        // Assert
        result.Should().Be("web");
    }

    [Theory]
    [InlineData("--ui")]
    [InlineData("--UI")]
    [InlineData("--Ui")]
    public void TranslateLegacyFlags_WithUiFlag_ShouldReturnNull(string flag)
    {
        // Arrange
        var args = new[] { flag };

        // Act
        var result = CliModeResolver.TranslateLegacyFlags(args);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void TranslateLegacyFlags_ExplicitModeTakesPrecedence()
    {
        // Arrange
        var args = new[] { "--ui", "--mode", "desktop" };

        // Act
        var result = CliModeResolver.TranslateLegacyFlags(args);

        // Assert
        result.Should().Be("desktop");
    }

    [Fact]
    public void Resolve_WithNoArgs_ShouldReturnHeadless()
    {
        // Arrange
        var args = Array.Empty<string>();

        // Act
        var result = CliModeResolver.Resolve(args);

        // Assert
        result.Should().Be(CliModeResolver.RunMode.Headless);
    }

    [Theory]
    [InlineData("desktop", CliModeResolver.RunMode.Desktop)]
    [InlineData("DESKTOP", CliModeResolver.RunMode.Desktop)]
    [InlineData("headless", CliModeResolver.RunMode.Headless)]
    [InlineData("HEADLESS", CliModeResolver.RunMode.Headless)]
    public void Resolve_WithExplicitMode_ShouldReturnCorrectMode(string modeArg, CliModeResolver.RunMode expected)
    {
        // Arrange
        var args = new[] { "--mode", modeArg };

        // Act
        var result = CliModeResolver.Resolve(args);

        // Assert
        result.Should().Be(expected);
    }

    [Fact]
    public void Resolve_WithLegacyUiFlag_ShouldFallBackToHeadless()
    {
        // Arrange
        var args = new[] { "--ui" };

        // Act
        var result = CliModeResolver.Resolve(args);

        // Assert
        result.Should().Be(CliModeResolver.RunMode.Headless);
    }

    [Fact]
    public void ResolveWithError_WithValidMode_ShouldHaveNoError()
    {
        // Arrange
        var args = new[] { "--mode", "desktop" };

        // Act
        var (mode, error) = CliModeResolver.ResolveWithError(args);

        // Assert
        mode.Should().Be(CliModeResolver.RunMode.Desktop);
        error.Should().BeNull();
    }

    [Fact]
    public void ResolveWithError_WithLegacyUiFlag_ShouldReturnRemovalGuidance()
    {
        var (mode, error) = CliModeResolver.ResolveWithError(["--ui"]);

        mode.Should().Be(CliModeResolver.RunMode.Headless);
        error.Should().Be("The web dashboard has been removed; use desktop or headless mode instead of '--ui'.");
    }

    [Fact]
    public void ResolveWithError_WithWebMode_ShouldReturnRemovalGuidance()
    {
        var (mode, error) = CliModeResolver.ResolveWithError(["--mode", "web"]);

        mode.Should().Be(CliModeResolver.RunMode.Headless);
        error.Should().Be("The web dashboard has been removed; use desktop or headless mode instead of '--mode web'.");
    }

    [Fact]
    public void ResolveWithError_WithUnknownMode_ShouldReturnError()
    {
        // Arrange
        var args = new[] { "--mode", "unknown" };

        // Act
        var (mode, error) = CliModeResolver.ResolveWithError(args);

        // Assert
        mode.Should().Be(CliModeResolver.RunMode.Headless); // Default fallback
        error.Should().NotBeNullOrEmpty();
        error.Should().Contain("unknown");
        error.Should().Contain("desktop");
        error.Should().Contain("headless");
    }

    [Fact]
    public void ResolveWithError_WithWhitespacePaddedMode_ShouldTrimAndResolve()
    {
        var args = new[] { "--mode", "  desktop  " };

        var (mode, error) = CliModeResolver.ResolveWithError(args);

        mode.Should().Be(CliModeResolver.RunMode.Desktop);
        error.Should().BeNull();
    }

    [Fact]
    public void TranslateLegacyFlags_WithModeMissingValue_PreservesCurrentLiteralValueBehavior()
    {
        var args = new[] { "--mode", "--ui" };

        var result = CliModeResolver.TranslateLegacyFlags(args);

        result.Should().Be("--ui");
    }

    [Fact]
    public void HasFlag_WithPresentFlag_ShouldReturnTrue()
    {
        // Arrange
        var args = new[] { "--foo", "--bar", "value" };

        // Act
        var result = CliModeResolver.HasFlag(args, "--foo");

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void HasFlag_WithMissingFlag_ShouldReturnFalse()
    {
        // Arrange
        var args = new[] { "--foo", "--bar", "value" };

        // Act
        var result = CliModeResolver.HasFlag(args, "--baz");

        // Assert
        result.Should().BeFalse();
    }

    [Theory]
    [InlineData("--Flag")]
    [InlineData("--FLAG")]
    [InlineData("--flag")]
    public void HasFlag_IsCaseInsensitive(string flagVariant)
    {
        // Arrange
        var args = new[] { "--flag" };

        // Act
        var result = CliModeResolver.HasFlag(args, flagVariant);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void GetArgValue_WithExistingKey_ShouldReturnValue()
    {
        // Arrange
        var args = new[] { "--key", "value", "--other", "stuff" };

        // Act
        var result = CliModeResolver.GetArgValue(args, "--key");

        // Assert
        result.Should().Be("value");
    }

    [Fact]
    public void GetArgValue_WithMissingKey_ShouldReturnNull()
    {
        // Arrange
        var args = new[] { "--key", "value" };

        // Act
        var result = CliModeResolver.GetArgValue(args, "--missing");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void GetArgValue_WithKeyAtEnd_ShouldReturnNull()
    {
        // Arrange - key is last element, no value follows
        var args = new[] { "--key" };

        // Act
        var result = CliModeResolver.GetArgValue(args, "--key");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void GetArgValue_IsCaseInsensitive()
    {
        // Arrange
        var args = new[] { "--KEY", "value" };

        // Act
        var result = CliModeResolver.GetArgValue(args, "--key");

        // Assert
        result.Should().Be("value");
    }

    [Fact]
    public void LegacyFlagTranslation_MultipleFlags_WorksTogether()
    {
        // Arrange - mixed legacy and new flags
        var args = new[] { "--ui", "--http-port", "9000", "--watch-config" };

        // Act
        var mode = CliModeResolver.Resolve(args);
        var port = CliModeResolver.GetArgValue(args, "--http-port");
        var hasWatch = CliModeResolver.HasFlag(args, "--watch-config");

        // Assert
        mode.Should().Be(CliModeResolver.RunMode.Headless);
        port.Should().Be("9000");
        hasWatch.Should().BeTrue();
    }
}
