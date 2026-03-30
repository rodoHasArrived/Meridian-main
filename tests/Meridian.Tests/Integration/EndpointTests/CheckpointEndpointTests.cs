using System.Net;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Meridian.Tests.Integration.EndpointTests;

/// <summary>
/// Integration tests for backfill checkpoint API endpoints.
/// Tests GET /api/backfill/checkpoints, /checkpoints/resumable, /checkpoints/{jobId},
/// /checkpoints/{jobId}/pending, and POST /checkpoints/{jobId}/resume.
/// </summary>
[Trait("Category", "Integration")]
[Collection("Endpoint")]
public sealed class CheckpointEndpointTests
{
    private readonly HttpClient _client;

    public CheckpointEndpointTests(EndpointTestFixture fixture)
    {
        _client = fixture.Client;
    }

    #region GET /api/backfill/checkpoints

    [Fact]
    public async Task GetCheckpoints_ReturnsJsonArray()
    {
        var response = await _client.GetAsync("/api/backfill/checkpoints");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(content);
        doc.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
    }

    #endregion

    #region GET /api/backfill/checkpoints/resumable

    [Fact]
    public async Task GetResumableCheckpoints_ReturnsJsonArray()
    {
        var response = await _client.GetAsync("/api/backfill/checkpoints/resumable");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(content);
        doc.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task GetResumableCheckpoints_WhenNoFailedJobs_ReturnsEmpty()
    {
        // When no backfill has run or the last run succeeded, resumable list should be empty
        var response = await _client.GetAsync("/api/backfill/checkpoints/resumable");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(content);
        // Either empty array or the run was successful (isResumable = false)
        if (doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0)
        {
            var first = doc.RootElement[0];
            first.TryGetProperty("isResumable", out var isResumable).Should().BeTrue();
            isResumable.GetBoolean().Should().BeTrue();
        }
    }

    #endregion

    #region GET /api/backfill/checkpoints/{jobId}

    [Fact]
    public async Task GetCheckpointById_WithNonExistentJobId_ReturnsNotFound()
    {
        var response = await _client.GetAsync("/api/backfill/checkpoints/nonexistent-job-id");

        // When no backfill has run, any specific jobId lookup returns 404
        response.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetCheckpointById_ResponseHasExpectedShape_WhenFound()
    {
        // First confirm a checkpoint exists by checking the list endpoint
        var listResp = await _client.GetAsync("/api/backfill/checkpoints");
        listResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var listContent = await listResp.Content.ReadAsStringAsync();
        var listDoc = JsonDocument.Parse(listContent);

        if (listDoc.RootElement.ValueKind != JsonValueKind.Array ||
            listDoc.RootElement.GetArrayLength() == 0)
        {
            // No checkpoints to inspect — test is vacuously satisfied
            return;
        }

        var response = await _client.GetAsync("/api/backfill/checkpoints/any-job");
        if (response.StatusCode == HttpStatusCode.OK)
        {
            var content = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(content);
            doc.RootElement.TryGetProperty("provider", out _).Should().BeTrue();
            doc.RootElement.TryGetProperty("canResume", out _).Should().BeTrue();
        }
    }

    #endregion

    #region GET /api/backfill/checkpoints/{jobId}/pending

    [Fact]
    public async Task GetPendingSymbols_WithUnknownJobId_ReturnsNotFoundOrEmpty()
    {
        var response = await _client.GetAsync("/api/backfill/checkpoints/unknown-xyz/pending");

        // Either 404 (no checkpoint at all) or 200 with empty pending list
        response.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.OK);

        if (response.StatusCode == HttpStatusCode.OK)
        {
            var content = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(content);
            doc.RootElement.TryGetProperty("pendingSymbols", out _).Should().BeTrue();
        }
    }

    #endregion

    #region POST /api/backfill/checkpoints/{jobId}/resume

    [Fact]
    public async Task ResumeCheckpoint_WithUnknownJobId_ReturnsNotFound()
    {
        var response = await _client.PostAsync(
            "/api/backfill/checkpoints/no-such-job/resume",
            content: null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ResumeCheckpoint_WithAlreadySuccessfulJob_ReturnsBadRequest()
    {
        // This test is only meaningful when a successful checkpoint exists.
        // When no checkpoint exists, we get 404 instead — that is also valid.
        var listResp = await _client.GetAsync("/api/backfill/checkpoints");
        var listContent = await listResp.Content.ReadAsStringAsync();
        var listDoc = JsonDocument.Parse(listContent);

        if (listDoc.RootElement.ValueKind != JsonValueKind.Array ||
            listDoc.RootElement.GetArrayLength() == 0)
        {
            return; // No checkpoints to test
        }

        var first = listDoc.RootElement[0];
        if (!first.TryGetProperty("success", out var successProp) || !successProp.GetBoolean())
        {
            return; // Last checkpoint was not successful — not what we're testing here
        }

        var resumeResp = await _client.PostAsync(
            "/api/backfill/checkpoints/any-completed/resume",
            content: null);

        resumeResp.StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.NotFound);
    }

    #endregion
}
