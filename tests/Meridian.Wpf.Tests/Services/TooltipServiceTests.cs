using Meridian.Wpf.Services;

namespace Meridian.Wpf.Tests.Services;

/// <summary>
/// Tests for TooltipService singleton service.
/// Validates tip display logic, dismissal tracking, feature help content, and reset behavior.
/// </summary>
public sealed class TooltipServiceTests
{
    [Fact]
    public void Instance_ShouldReturnSingleton()
    {
        // Arrange & Act
        var instance1 = TooltipService.Instance;
        var instance2 = TooltipService.Instance;

        // Assert
        instance1.Should().NotBeNull();
        instance2.Should().NotBeNull();
        instance1.Should().BeSameAs(instance2, "TooltipService should be a singleton");
    }

    [Fact]
    public void ShouldShowTip_ForNewTip_ShouldReturnTrue()
    {
        // Arrange
        var service = TooltipService.Instance;
        service.ResetAllTips();
        var uniqueTipKey = $"test-tip-new-{Guid.NewGuid():N}";

        // Act
        var result = service.ShouldShowTip(uniqueTipKey);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void ShouldShowTip_ForAlreadyShownTip_ShouldReturnFalse()
    {
        // Arrange
        var service = TooltipService.Instance;
        service.ResetAllTips();
        var tipKey = $"test-tip-shown-{Guid.NewGuid():N}";
        service.ShouldShowTip(tipKey); // First call marks it as shown

        // Act
        var result = service.ShouldShowTip(tipKey);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void DismissTip_ShouldCauseShouldShowTipToReturnFalse()
    {
        // Arrange
        var service = TooltipService.Instance;
        service.ResetAllTips();
        var tipKey = $"test-tip-dismiss-{Guid.NewGuid():N}";

        // Act
        service.DismissTip(tipKey);
        var result = service.ShouldShowTip(tipKey);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void ResetAllTips_ShouldClearAllDismissedTips()
    {
        // Arrange
        var service = TooltipService.Instance;
        var tipKey = $"test-tip-reset-{Guid.NewGuid():N}";
        service.DismissTip(tipKey);

        // Act
        service.ResetAllTips();
        var result = service.ShouldShowTip(tipKey);

        // Assert
        result.Should().BeTrue("tip should be showable again after reset");
    }

    [Fact]
    public void GetFeatureHelp_ForKnownKey_ShouldReturnNonNull()
    {
        // Arrange
        var service = TooltipService.Instance;

        // Act
        var help = service.GetFeatureHelp("dashboard");

        // Assert
        help.Should().NotBeNull();
        help.Title.Should().NotBeNullOrEmpty();
        help.Description.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void GetFeatureHelp_ForUnknownKey_ShouldReturnFallback()
    {
        // Arrange
        var service = TooltipService.Instance;

        // Act
        var help = service.GetFeatureHelp("nonexistent-feature-key");

        // Assert
        help.Should().NotBeNull();
        help.Title.Should().Be("Help");
    }

    [Theory]
    [InlineData("dashboard")]
    [InlineData("backfill")]
    [InlineData("symbols")]
    [InlineData("provider")]
    [InlineData("storage")]
    [InlineData("dataquality")]
    [InlineData("leanintegration")]
    public void GetFeatureHelp_ForRegisteredKeys_ShouldReturnContent(string featureKey)
    {
        // Arrange
        var service = TooltipService.Instance;

        // Act
        var help = service.GetFeatureHelp(featureKey);

        // Assert
        help.Should().NotBeNull();
        help.Title.Should().NotBeNullOrEmpty();
        help.Description.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void GetTooltipContent_ForKnownKey_ShouldReturnNonEmptyString()
    {
        // Arrange
        var service = TooltipService.Instance;

        // Act
        var content = service.GetTooltipContent("dashboard");

        // Assert
        content.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void GetTooltipContent_ForKnownKeyWithTips_ShouldContainTipsSection()
    {
        // Arrange
        var service = TooltipService.Instance;

        // Act
        var content = service.GetTooltipContent("dashboard");

        // Assert
        content.Should().Contain("Tips:");
    }

    [Fact]
    public void GetTooltipContent_ForUnknownKey_ShouldReturnFallbackText()
    {
        // Arrange
        var service = TooltipService.Instance;

        // Act
        var content = service.GetTooltipContent("nonexistent-key");

        // Assert
        content.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void GetOnboardingTips_ForKnownPage_ShouldReturnTips()
    {
        // Arrange
        var service = TooltipService.Instance;

        // Act
        var tips = service.GetOnboardingTips("Dashboard");

        // Assert
        tips.Should().NotBeNull();
        tips.Should().NotBeEmpty();
    }

    [Fact]
    public void GetOnboardingTips_ForUnknownPage_ShouldReturnEmpty()
    {
        // Arrange
        var service = TooltipService.Instance;

        // Act
        var tips = service.GetOnboardingTips("NonExistentPage");

        // Assert
        tips.Should().NotBeNull();
        tips.Should().BeEmpty();
    }
}
