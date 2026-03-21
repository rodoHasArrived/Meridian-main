using FluentAssertions;
using Meridian.Ui.Services;

namespace Meridian.Ui.Tests.Services;

/// <summary>
/// Tests for <see cref="SmartRecommendationsService"/> — singleton access,
/// backfill recommendation models, quick actions, data quality issues, and insights.
/// </summary>
public sealed class SmartRecommendationsServiceTests
{
    // ── Singleton ────────────────────────────────────────────────────

    [Fact]
    public void Instance_ShouldReturnNonNull()
    {
        // Act
        var instance = SmartRecommendationsService.Instance;

        // Assert
        instance.Should().NotBeNull();
    }

    [Fact]
    public void Instance_ShouldReturnSameReference()
    {
        // Act
        var a = SmartRecommendationsService.Instance;
        var b = SmartRecommendationsService.Instance;

        // Assert
        a.Should().BeSameAs(b);
    }

    // ── BackfillRecommendations defaults ────────────────────────────

    [Fact]
    public void BackfillRecommendations_Default_ShouldHaveGeneratedAt()
    {
        // Act
        var rec = new BackfillRecommendations();

        // Assert
        rec.GeneratedAt.Should().BeOnOrBefore(DateTime.UtcNow);
    }

    [Fact]
    public void BackfillRecommendations_Default_IsStale_ShouldBeFalse()
    {
        // Act
        var rec = new BackfillRecommendations();

        // Assert
        rec.IsStale.Should().BeFalse();
    }

    [Fact]
    public void BackfillRecommendations_Default_ErrorMessage_ShouldBeNullOrEmpty()
    {
        // Act
        var rec = new BackfillRecommendations();

        // Assert
        rec.ErrorMessage.Should().BeNullOrEmpty();
    }

    [Fact]
    public void BackfillRecommendations_Default_QuickActions_ShouldBeEmpty()
    {
        // Act
        var rec = new BackfillRecommendations();

        // Assert
        rec.QuickActions.Should().NotBeNull();
        rec.QuickActions.Should().BeEmpty();
    }

    [Fact]
    public void BackfillRecommendations_Default_SuggestedBackfills_ShouldBeEmpty()
    {
        // Act
        var rec = new BackfillRecommendations();

        // Assert
        rec.SuggestedBackfills.Should().NotBeNull();
        rec.SuggestedBackfills.Should().BeEmpty();
    }

    [Fact]
    public void BackfillRecommendations_Default_DataQualityIssues_ShouldBeEmpty()
    {
        // Act
        var rec = new BackfillRecommendations();

        // Assert
        rec.DataQualityIssues.Should().NotBeNull();
        rec.DataQualityIssues.Should().BeEmpty();
    }

    [Fact]
    public void BackfillRecommendations_Default_Insights_ShouldBeEmpty()
    {
        // Act
        var rec = new BackfillRecommendations();

        // Assert
        rec.Insights.Should().NotBeNull();
        rec.Insights.Should().BeEmpty();
    }

    // ── QuickAction model ──────────────────────────────────────────

    [Fact]
    public void QuickAction_ShouldStoreAllProperties()
    {
        // Arrange & Act
        var action = new QuickAction
        {
            Id = "qa-1",
            Title = "Fill data gaps",
            Description = "Fills detected gaps in SPY data",
            Icon = "sync",
            ActionType = QuickActionType.FillGaps,
            Priority = 1,
            AffectedSymbols = new[] { "SPY", "AAPL" },
            EstimatedTime = TimeSpan.FromMinutes(5)
        };

        // Assert
        action.Id.Should().Be("qa-1");
        action.Title.Should().Be("Fill data gaps");
        action.Description.Should().Be("Fills detected gaps in SPY data");
        action.Icon.Should().Be("sync");
        action.ActionType.Should().Be(QuickActionType.FillGaps);
        action.Priority.Should().Be(1);
        action.AffectedSymbols.Should().HaveCount(2);
        action.AffectedSymbols.Should().Contain("SPY");
        action.EstimatedTime.Should().Be(TimeSpan.FromMinutes(5));
    }

    [Fact]
    public void QuickAction_Default_ShouldHaveEmptyCollections()
    {
        // Act
        var action = new QuickAction();

        // Assert
        action.Id.Should().BeEmpty();
        action.Title.Should().BeEmpty();
        action.Description.Should().BeEmpty();
        action.AffectedSymbols.Should().BeNull();
    }

    // ── SuggestedBackfill model ────────────────────────────────────

    [Fact]
    public void SuggestedBackfill_ShouldStoreAllProperties()
    {
        // Arrange & Act
        var backfill = new SuggestedBackfill
        {
            Id = "sb-1",
            Title = "Backfill AAPL",
            Description = "Missing last 30 days of AAPL data",
            Symbols = new[] { "AAPL" },
            RecommendedDateRange = 30,
            Reason = "Gap detected",
            Category = "Gap Fill"
        };

        // Assert
        backfill.Id.Should().Be("sb-1");
        backfill.Title.Should().Be("Backfill AAPL");
        backfill.Description.Should().Be("Missing last 30 days of AAPL data");
        backfill.Symbols.Should().ContainSingle().Which.Should().Be("AAPL");
        backfill.RecommendedDateRange.Should().Be(30);
        backfill.Reason.Should().Be("Gap detected");
        backfill.Category.Should().Be("Gap Fill");
    }

    [Fact]
    public void SuggestedBackfill_Default_ShouldHaveEmptyValues()
    {
        // Act
        var backfill = new SuggestedBackfill();

        // Assert
        backfill.Id.Should().BeEmpty();
        backfill.Title.Should().BeEmpty();
        backfill.Symbols.Should().NotBeNull();
        backfill.Symbols.Should().BeEmpty();
        backfill.RecommendedDateRange.Should().Be(365);
    }

    // ── DataQualityIssue model ─────────────────────────────────────

    [Fact]
    public void DataQualityIssue_ShouldStoreAllProperties()
    {
        // Arrange & Act
        var issue = new DataQualityIssue
        {
            Id = "dqi-1",
            Severity = IssueSeverity.Warning,
            Title = "Stale quotes",
            Description = "SPY quotes are 15 minutes behind",
            AffectedCount = 42,
            SuggestedAction = "Re-subscribe to SPY quotes"
        };

        // Assert
        issue.Id.Should().Be("dqi-1");
        issue.Severity.Should().Be(IssueSeverity.Warning);
        issue.Title.Should().Be("Stale quotes");
        issue.Description.Should().Be("SPY quotes are 15 minutes behind");
        issue.AffectedCount.Should().Be(42);
        issue.SuggestedAction.Should().Be("Re-subscribe to SPY quotes");
    }

    [Fact]
    public void DataQualityIssue_Default_ShouldHaveZeroAffectedCount()
    {
        // Act
        var issue = new DataQualityIssue();

        // Assert
        issue.AffectedCount.Should().Be(0);
        issue.Id.Should().BeEmpty();
        issue.Title.Should().BeEmpty();
    }

    // ── InsightMessage model ───────────────────────────────────────

    [Fact]
    public void InsightMessage_ShouldStoreAllProperties()
    {
        // Arrange & Act
        var insight = new InsightMessage
        {
            Type = InsightType.Tip,
            Title = "Compression tip",
            Message = "Enable ZSTD compression to save 40% disk space"
        };

        // Assert
        insight.Type.Should().Be(InsightType.Tip);
        insight.Title.Should().Be("Compression tip");
        insight.Message.Should().Be("Enable ZSTD compression to save 40% disk space");
    }

    [Fact]
    public void InsightMessage_Default_ShouldHaveEmptyStrings()
    {
        // Act
        var insight = new InsightMessage();

        // Assert
        insight.Title.Should().BeEmpty();
        insight.Message.Should().BeEmpty();
    }

    // ── QuickActionType enum ───────────────────────────────────────

    [Theory]
    [InlineData(QuickActionType.FillGaps)]
    [InlineData(QuickActionType.ExtendCoverage)]
    [InlineData(QuickActionType.BackfillNew)]
    [InlineData(QuickActionType.UpdateLatest)]
    [InlineData(QuickActionType.ValidateData)]
    [InlineData(QuickActionType.Custom)]
    public void QuickActionType_AllValues_ShouldBeDefined(QuickActionType actionType)
    {
        // Assert
        Enum.IsDefined(typeof(QuickActionType), actionType).Should().BeTrue();
    }

    [Fact]
    public void QuickActionType_ShouldHaveExpectedCount()
    {
        // Act
        var values = Enum.GetValues<QuickActionType>();

        // Assert
        values.Should().HaveCount(6);
    }

    // ── IssueSeverity enum ──────────────────────────────

    [Theory]
    [InlineData(IssueSeverity.Info)]
    [InlineData(IssueSeverity.Warning)]
    [InlineData(IssueSeverity.Error)]
    [InlineData(IssueSeverity.Critical)]
    public void IssueSeverity_AllValues_ShouldBeDefined(IssueSeverity severity)
    {
        // Assert
        Enum.IsDefined(typeof(IssueSeverity), severity).Should().BeTrue();
    }

    [Fact]
    public void IssueSeverity_ShouldHaveExpectedCount()
    {
        // Act
        var values = Enum.GetValues<IssueSeverity>();

        // Assert
        values.Should().HaveCount(4);
    }

    // ── InsightType enum ───────────────────────────────────────────

    [Theory]
    [InlineData(InsightType.Info)]
    [InlineData(InsightType.Success)]
    [InlineData(InsightType.Warning)]
    [InlineData(InsightType.Tip)]
    public void InsightType_AllValues_ShouldBeDefined(InsightType insightType)
    {
        // Assert
        Enum.IsDefined(typeof(InsightType), insightType).Should().BeTrue();
    }

    [Fact]
    public void InsightType_ShouldHaveExpectedCount()
    {
        // Act
        var values = Enum.GetValues<InsightType>();

        // Assert
        values.Should().HaveCount(4);
    }
}
