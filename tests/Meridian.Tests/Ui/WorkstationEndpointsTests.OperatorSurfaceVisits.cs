using System.Net;
using System.Text.Json;
using FluentAssertions;
using Meridian.Identity.Auth;
using Meridian.Ui.Shared.Workflows;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meridian.Tests.Ui;

public sealed partial class WorkstationEndpointsTests
{
    private const string BreakQueueVisitPath = "/api/workstation/operator/visits/reconciliation-breaks";

    [Fact]
    public async Task MapWorkstationEndpoints_OperatorSurfaceVisit_ReturnsThePreviousVisitPerOperatorAndReadsRecordNothing()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-tests", "surface-visits", Guid.NewGuid().ToString("N"));
        var clock = new SurfaceVisitTestClock(new DateTimeOffset(2026, 11, 5, 14, 0, 0, TimeSpan.Zero));
        var store = new FileOperatorSurfaceVisitStore(root, NullLogger<FileOperatorSurfaceVisitStore>.Instance, clock);
        await using var alice = await CreateAppAsync(services => services.AddSingleton<IOperatorSurfaceVisitStore>(store),
            currentUserPermissions: UserPermission.ViewTrades, currentUserName: "alice");
        await using var bob = await CreateAppAsync(services => services.AddSingleton<IOperatorSurfaceVisitStore>(store),
            currentUserPermissions: UserPermission.ViewTrades, currentUserName: "bob");
        var aliceClient = alice.GetTestClient();
        var bobClient = bob.GetTestClient();

        using (var first = await PostJsonAsync(aliceClient, BreakQueueVisitPath))
        {
            first.RootElement.GetProperty("surface").GetString().Should().Be("reconciliation-breaks");
            first.RootElement.GetProperty("previousVisitAt").ValueKind.Should().Be(JsonValueKind.Null, "the first visit has no baseline");
            first.RootElement.GetProperty("visitedAt").GetDateTimeOffset().Should().Be(clock.GetUtcNow());
        }

        clock.Now = clock.Now.AddHours(3);
        using (var read = await ReadJsonAsync(aliceClient, BreakQueueVisitPath))
        {
            read.RootElement.GetProperty("previousVisitAt").GetDateTimeOffset().Should().Be(new DateTimeOffset(2026, 11, 5, 14, 0, 0, TimeSpan.Zero));
            read.RootElement.GetProperty("visitedAt").ValueKind.Should().Be(JsonValueKind.Null, "a read records nothing");
        }

        using (var second = await PostJsonAsync(aliceClient, BreakQueueVisitPath))
        {
            second.RootElement.GetProperty("previousVisitAt").GetDateTimeOffset().Should().Be(new DateTimeOffset(2026, 11, 5, 14, 0, 0, TimeSpan.Zero));
            second.RootElement.GetProperty("visitedAt").GetDateTimeOffset().Should().Be(new DateTimeOffset(2026, 11, 5, 17, 0, 0, TimeSpan.Zero));
        }

        using (var other = await PostJsonAsync(bobClient, BreakQueueVisitPath))
        {
            other.RootElement.GetProperty("previousVisitAt").ValueKind.Should().Be(JsonValueKind.Null, "another operator's visits are their own");
        }
    }

    [Fact]
    public async Task MapWorkstationEndpoints_OperatorSurfaceVisit_RejectsSurfacesThatAreNotSlugs()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-tests", "surface-visits", Guid.NewGuid().ToString("N"));
        var store = new FileOperatorSurfaceVisitStore(root, NullLogger<FileOperatorSurfaceVisitStore>.Instance);
        await using var app = await CreateAppAsync(services => services.AddSingleton<IOperatorSurfaceVisitStore>(store),
            currentUserPermissions: UserPermission.ViewTrades);
        var client = app.GetTestClient();

        var response = await client.PostAsync("/api/workstation/operator/visits/Break%20Queue", content: null);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private sealed class SurfaceVisitTestClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static async Task<JsonDocument> PostJsonAsync(HttpClient client, string path)
    {
        var response = await client.PostAsync(path, content: null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }
}
