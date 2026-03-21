using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Meridian.Ui.Services.Services;
using Xunit;

namespace Meridian.Ui.Tests.Services;

/// <summary>
/// Concrete test implementation of DataQualityServiceBase.
/// </summary>
internal sealed class TestDataQualityService : DataQualityServiceBase
{
    private readonly Dictionary<string, object?> _getResponses = new();
    private readonly Dictionary<string, (bool Success, object? Data)> _postResponses = new();

    public void SetGetResponse<T>(string endpoint, T? response) where T : class
    {
        _getResponses[endpoint] = response;
    }

    public void SetPostResponse<T>(string endpoint, bool success, T? data) where T : class
    {
        _postResponses[endpoint] = (success, data);
    }

    protected override Task<T?> GetAsync<T>(string endpoint, CancellationToken ct) where T : class
    {
        if (_getResponses.TryGetValue(endpoint, out var response))
        {
            return Task.FromResult(response as T);
        }
        return Task.FromResult<T?>(null);
    }

    protected override Task<T?> PostAsync<T>(string endpoint, object? body, CancellationToken ct) where T : class
    {
        if (_postResponses.TryGetValue(endpoint, out var response))
        {
            return Task.FromResult(response.Data as T);
        }
        return Task.FromResult<T?>(null);
    }

    protected override Task<(bool Success, T? Data)> PostWithResponseAsync<T>(string endpoint, object? body, CancellationToken ct) where T : class
    {
        if (_postResponses.TryGetValue(endpoint, out var response))
        {
            return Task.FromResult((response.Success, response.Data as T));
        }
        return Task.FromResult<(bool, T?)>((false, null));
    }
}

public sealed class DataQualityServiceBaseTests
{
    private readonly TestDataQualityService _sut = new();

    [Fact]
    public async Task GetQualitySummaryAsync_ReturnsSummary()
    {
        var expected = new DataQualitySummary { OverallScore = 95.5, TotalFiles = 100 };
        _sut.SetGetResponse("/api/storage/quality/summary", expected);

        var result = await _sut.GetQualitySummaryAsync();

        result.Should().NotBeNull();
        result!.OverallScore.Should().Be(95.5);
        result.TotalFiles.Should().Be(100);
    }

    [Fact]
    public async Task GetQualitySummaryAsync_ReturnsNull_WhenNotAvailable()
    {
        var result = await _sut.GetQualitySummaryAsync();

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetQualityScoresAsync_ReturnsScores()
    {
        var expected = new List<QualityScoreEntry>
        {
            new() { Symbol = "AAPL", Score = 98.0 },
            new() { Symbol = "MSFT", Score = 92.0 }
        };
        _sut.SetGetResponse("/api/storage/quality/scores", expected);

        var result = await _sut.GetQualityScoresAsync();

        result.Should().NotBeNull();
        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetQualityScoresAsync_WithMinScore_UsesQueryParam()
    {
        var expected = new List<QualityScoreEntry>
        {
            new() { Symbol = "AAPL", Score = 98.0 }
        };
        _sut.SetGetResponse("/api/storage/quality/scores?minScore=95", expected);

        var result = await _sut.GetQualityScoresAsync(minScore: 95);

        result.Should().NotBeNull();
        result.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetSymbolQualityAsync_ReturnsReport()
    {
        var expected = new SymbolQualityReport
        {
            Symbol = "SPY",
            OverallScore = 97.0,
            Issues = new List<QualityIssue>()
        };
        _sut.SetGetResponse("/api/storage/quality/symbol/SPY", expected);

        var result = await _sut.GetSymbolQualityAsync("SPY");

        result.Should().NotBeNull();
        result!.Symbol.Should().Be("SPY");
        result.OverallScore.Should().Be(97.0);
    }

    [Fact]
    public async Task GetQualityAlertsAsync_ReturnsAlerts()
    {
        var expected = new List<QualityAlert>
        {
            new() { Id = "alert1", Severity = "warning", Message = "Test alert" }
        };
        _sut.SetGetResponse("/api/storage/quality/alerts", expected);

        var result = await _sut.GetQualityAlertsAsync();

        result.Should().NotBeNull();
        result.Should().HaveCount(1);
        result![0].Id.Should().Be("alert1");
    }

    [Fact]
    public async Task GetQualityAlertsAsync_WithSeverity_UsesQueryParam()
    {
        var expected = new List<QualityAlert>();
        _sut.SetGetResponse("/api/storage/quality/alerts?severity=critical", expected);

        var result = await _sut.GetQualityAlertsAsync(severity: "critical");

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task AcknowledgeAlertAsync_ReturnsTrue_WhenSuccessful()
    {
        _sut.SetPostResponse<AcknowledgeResponse>(
            "/api/storage/quality/alerts/alert1/acknowledge",
            true,
            new AcknowledgeResponse { Success = true });

        var result = await _sut.AcknowledgeAlertAsync("alert1");

        result.Should().BeTrue();
    }

    [Fact]
    public async Task AcknowledgeAlertAsync_ReturnsFalse_WhenFailed()
    {
        var result = await _sut.AcknowledgeAlertAsync("nonexistent");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task GetSourceRankingsAsync_ReturnsRankings()
    {
        var expected = new List<SourceRanking>
        {
            new() { Source = "Alpaca", Rank = 1, QualityScore = 99.0 }
        };
        _sut.SetGetResponse("/api/storage/quality/rankings/SPY", expected);

        var result = await _sut.GetSourceRankingsAsync("SPY");

        result.Should().NotBeNull();
        result.Should().HaveCount(1);
        result![0].Rank.Should().Be(1);
    }

    [Fact]
    public async Task GetDataGapsAsync_ReturnsGaps_FromSymbolReport()
    {
        var report = new SymbolQualityReport
        {
            Symbol = "AAPL",
            Gaps = new List<QualityDataGap>
            {
                new() { Start = new DateTime(2025, 1, 1), End = new DateTime(2025, 1, 3), MissingRecords = 2 }
            }
        };
        _sut.SetGetResponse("/api/storage/quality/symbol/AAPL", report);

        var result = await _sut.GetDataGapsAsync("AAPL");

        result.Should().HaveCount(1);
        result[0].MissingBars.Should().Be(2);
        result[0].StartDate.Should().Be(new DateTime(2025, 1, 1));
    }

    [Fact]
    public async Task GetDataGapsAsync_ReturnsEmpty_WhenNoReport()
    {
        var result = await _sut.GetDataGapsAsync("UNKNOWN");

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task VerifySymbolIntegrityAsync_ReturnsValid_WhenHighScore()
    {
        var checkResult = new QualityCheckResult
        {
            Success = true,
            Score = 99.0,
            Issues = new List<string>(),
            Recommendations = new List<string>(),
            CheckedAt = DateTime.UtcNow
        };
        _sut.SetPostResponse("/api/storage/quality/check", false, checkResult);

        var result = await _sut.VerifySymbolIntegrityAsync("SPY");

        result.IsValid.Should().BeTrue();
        result.Score.Should().Be(99.0);
    }

    [Fact]
    public async Task VerifySymbolIntegrityAsync_ReturnsInvalid_WhenLowScore()
    {
        var checkResult = new QualityCheckResult
        {
            Success = true,
            Score = 80.0,
            Issues = new List<string> { "Missing data" },
            CheckedAt = DateTime.UtcNow
        };
        _sut.SetPostResponse("/api/storage/quality/check", false, checkResult);

        var result = await _sut.VerifySymbolIntegrityAsync("AAPL");

        result.IsValid.Should().BeFalse();
        result.Issues.Should().Contain("Missing data");
    }

    [Fact]
    public async Task VerifySymbolIntegrityAsync_ReturnsInvalid_WhenCheckFails()
    {
        // No response set, so PostAsync returns null

        var result = await _sut.VerifySymbolIntegrityAsync("BAD");

        result.IsValid.Should().BeFalse();
        result.Issues.Should().Contain("Failed to run integrity check");
    }

    [Fact]
    public async Task GetQualityTrendsAsync_ReturnsTrends()
    {
        var expected = new QualityTrendData
        {
            AverageScore = 96.5,
            TimeWindow = "7d",
            OverallTrend = new List<TrendDataPoint>
            {
                new() { Score = 95.0 },
                new() { Score = 98.0 }
            }
        };
        _sut.SetGetResponse("/api/storage/quality/trends?window=7d", expected);

        var result = await _sut.GetQualityTrendsAsync("7d");

        result.Should().NotBeNull();
        result!.AverageScore.Should().Be(96.5);
        result.OverallTrend.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetAnomaliesAsync_ReturnsAnomalies()
    {
        var expected = new List<AnomalyEvent>
        {
            new() { Id = "a1", Type = "spike", Symbol = "SPY" }
        };
        _sut.SetGetResponse("/api/storage/quality/anomalies", expected);

        var result = await _sut.GetAnomaliesAsync();

        result.Should().NotBeNull();
        result.Should().HaveCount(1);
        result![0].Type.Should().Be("spike");
    }
}
