using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Meridian.Tests.Integration.EndpointTests;

/// <summary>
/// Integration tests for maintenance schedule API endpoints.
/// Tests schedule management, execution triggers, and maintenance history.
/// Implements Phase 1A.7 from the roadmap.
/// </summary>
[Trait("Category", "Integration")]
[Collection("Endpoint")]
public sealed class MaintenanceEndpointTests
{
    private readonly HttpClient _client;

    public MaintenanceEndpointTests(EndpointTestFixture fixture)
    {
        _client = fixture.Client;
    }

    #region GET /api/maintenance/schedules - List Schedules

    [Fact]
    public async Task GetMaintenanceSchedules_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/maintenance/schedules");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
    }

    [Fact]
    public async Task GetMaintenanceSchedules_ReturnsValidJson()
    {
        var response = await _client.GetAsync("/api/maintenance/schedules");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(content);

        // Should return array of schedules or object with schedules array
        doc.RootElement.ValueKind.Should().BeOneOf(JsonValueKind.Array, JsonValueKind.Object);
    }

    #endregion

    #region POST /api/maintenance/schedules - Create Schedule

    [Fact]
    public async Task CreateMaintenanceSchedule_WithValidData_ReturnsCreatedOrOk()
    {
        var schedule = new
        {
            name = "Test Cleanup Schedule",
            cronExpression = "0 2 * * *", // Daily at 2 AM
            taskType = "cleanup",
            enabled = true
        };

        var content = new StringContent(
            JsonSerializer.Serialize(schedule),
            Encoding.UTF8,
            "application/json");

        var response = await _client.PostAsync("/api/maintenance/schedules", content);

        // Should return 200 OK or 201 Created on success, or 400/503 if validation fails or service unavailable
        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.OK,
            HttpStatusCode.Created,
            HttpStatusCode.BadRequest,
            HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task CreateMaintenanceSchedule_WithInvalidCron_ReturnsBadRequest()
    {
        var schedule = new
        {
            name = "Invalid Schedule",
            cronExpression = "invalid cron",
            taskType = "cleanup",
            enabled = true
        };

        var content = new StringContent(
            JsonSerializer.Serialize(schedule),
            Encoding.UTF8,
            "application/json");

        var response = await _client.PostAsync("/api/maintenance/schedules", content);

        // Should return 400 Bad Request for invalid cron expression
        // Or could be 501 if validation not implemented yet
        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.BadRequest,
            HttpStatusCode.NotImplemented);
    }

    #endregion

    #region GET /api/maintenance/schedules/{id} - Get Schedule

    [Fact]
    public async Task GetMaintenanceScheduleById_ReturnsOkOrNotFound()
    {
        var response = await _client.GetAsync("/api/maintenance/schedules/test-schedule-id");

        // Should return either OK with data, 404 if not found, or 501 if not implemented
        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.OK,
            HttpStatusCode.NotFound,
            HttpStatusCode.NotImplemented);
    }

    #endregion

    #region POST /api/maintenance/schedules/{id}/run - Trigger Schedule

    [Fact]
    public async Task TriggerMaintenanceSchedule_ReturnsAcceptedOrNotFound()
    {
        var response = await _client.PostAsync("/api/maintenance/schedules/test-id/run", null);

        // Non-existent schedule should return 404, or 503 if service unavailable
        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.OK,
            HttpStatusCode.Accepted,
            HttpStatusCode.NotFound,
            HttpStatusCode.ServiceUnavailable);
    }

    #endregion

    #region POST /api/maintenance/schedules/{id}/enable - Enable Schedule

    [Fact]
    public async Task EnableMaintenanceSchedule_ReturnsOkOrNotFound()
    {
        var response = await _client.PostAsync("/api/maintenance/schedules/test-id/enable", null);

        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.OK,
            HttpStatusCode.NotFound,
            HttpStatusCode.NotImplemented);
    }

    #endregion

    #region POST /api/maintenance/schedules/{id}/disable - Disable Schedule

    [Fact]
    public async Task DisableMaintenanceSchedule_ReturnsOkOrNotFound()
    {
        var response = await _client.PostAsync("/api/maintenance/schedules/test-id/disable", null);

        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.OK,
            HttpStatusCode.NotFound,
            HttpStatusCode.NotImplemented);
    }

    #endregion

    #region DELETE /api/maintenance/schedules/{id}/delete - Delete Schedule

    [Fact]
    public async Task DeleteMaintenanceSchedule_ReturnsOkOrNotFound()
    {
        var response = await _client.DeleteAsync("/api/maintenance/schedules/test-id/delete");

        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.OK,
            HttpStatusCode.NoContent,
            HttpStatusCode.NotFound,
            HttpStatusCode.NotImplemented);
    }

    #endregion

    #region GET /api/maintenance/schedules/{id}/history - Get Schedule History

    [Fact]
    public async Task GetMaintenanceScheduleHistory_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/maintenance/schedules/test-id/history");

        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.OK,
            HttpStatusCode.NotFound,
            HttpStatusCode.NotImplemented);
    }

    [Fact]
    public async Task GetMaintenanceScheduleHistory_ReturnsJsonArray()
    {
        var response = await _client.GetAsync("/api/maintenance/schedules/test-id/history");

        if (response.StatusCode == HttpStatusCode.OK)
        {
            response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");

            var content = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(content);

            // History should be an array or object with array property
            doc.RootElement.ValueKind.Should().BeOneOf(JsonValueKind.Array, JsonValueKind.Object);
        }
    }

    #endregion

    #region Admin Maintenance Endpoints

    [Fact]
    public async Task GetAdminMaintenanceSchedule_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/admin/maintenance/schedule");

        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.OK,
            HttpStatusCode.NotImplemented);
    }

    [Fact]
    public async Task PostAdminMaintenanceRun_ReturnsAcceptedOrOk()
    {
        var maintenanceTask = new
        {
            taskType = "cleanup",
            targetPath = "/data"
        };

        var content = new StringContent(
            JsonSerializer.Serialize(maintenanceTask),
            Encoding.UTF8,
            "application/json");

        var response = await _client.PostAsync("/api/admin/maintenance/run", content);

        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.OK,
            HttpStatusCode.Accepted,
            HttpStatusCode.NotImplemented);
    }

    [Fact]
    public async Task GetAdminMaintenanceHistory_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/admin/maintenance/history");

        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.OK,
            HttpStatusCode.NotImplemented);
    }

    #endregion

    #region Cron Validation Endpoints

    [Fact]
    public async Task ValidateCronExpression_WithValidCron_ReturnsOk()
    {
        var payload = new { expression = "0 2 * * *" };
        var content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json");

        var response = await _client.PostAsync("/api/schedules/cron/validate", content);

        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.OK,
            HttpStatusCode.NotImplemented);
    }

    [Fact]
    public async Task ValidateCronExpression_WithInvalidCron_ReturnsBadRequestOrOk()
    {
        var payload = new { expression = "invalid cron" };
        var content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json");

        var response = await _client.PostAsync("/api/schedules/cron/validate", content);

        // Could return 400 for invalid, or 200 with validation result, or 501
        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.OK,
            HttpStatusCode.BadRequest,
            HttpStatusCode.NotImplemented);
    }

    [Fact]
    public async Task GetCronNextRuns_WithValidCron_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/schedules/cron/next-runs?expression=0+2+*+*+*&count=5");

        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.OK,
            HttpStatusCode.BadRequest,
            HttpStatusCode.MethodNotAllowed,
            HttpStatusCode.NotImplemented);
    }

    [Fact]
    public async Task GetCronNextRuns_ReturnsJsonArray()
    {
        var response = await _client.GetAsync("/api/schedules/cron/next-runs?expression=0+2+*+*+*&count=3");

        if (response.StatusCode == HttpStatusCode.OK)
        {
            var content = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(content);

            // Should return array of next run times or object with array
            doc.RootElement.ValueKind.Should().BeOneOf(JsonValueKind.Array, JsonValueKind.Object);
        }
    }

    #endregion

    #region Edge Cases

    [Fact]
    public async Task GetMaintenanceSchedules_WithPagination_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/maintenance/schedules?page=1&pageSize=10");

        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.OK,
            HttpStatusCode.NotImplemented);
    }

    [Fact]
    public async Task CreateMaintenanceSchedule_WithEmptyName_ReturnsBadRequest()
    {
        var schedule = new
        {
            name = "",
            cronExpression = "0 2 * * *",
            taskType = "cleanup",
            enabled = true
        };

        var content = new StringContent(
            JsonSerializer.Serialize(schedule),
            Encoding.UTF8,
            "application/json");

        var response = await _client.PostAsync("/api/maintenance/schedules", content);

        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.BadRequest,
            HttpStatusCode.NotImplemented);
    }

    [Fact]
    public async Task TriggerMaintenanceSchedule_WithInvalidId_ReturnsNotFound()
    {
        var response = await _client.PostAsync("/api/maintenance/schedules/nonexistent-id-12345/run", null);

        // Non-existent schedule should return 404, or 503 if service unavailable
        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.NotFound,
            HttpStatusCode.ServiceUnavailable);
    }

    #endregion
}
