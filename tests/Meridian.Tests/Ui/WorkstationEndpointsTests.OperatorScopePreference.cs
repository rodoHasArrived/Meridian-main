using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.Identity.Auth;
using Meridian.Ui.Shared.Workflows;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meridian.Tests.Ui;

public sealed partial class WorkstationEndpointsTests
{
    private const string OperatorScopePreferencePath = "/api/workstation/operator/scope-preference";

    [Fact]
    public async Task MapWorkstationEndpoints_OperatorScopePreference_IsKeptPerOperatorAndCanBeCleared()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-tests", "scope-preference", Guid.NewGuid().ToString("N"));
        var store = new FileOperatorScopePreferenceStore(root, NullLogger<FileOperatorScopePreferenceStore>.Instance);

        await using var alice = await CreateAppAsync(services => services.AddSingleton<IOperatorScopePreferenceStore>(store),
            currentUserPermissions: UserPermission.ViewTrades, currentUserName: "alice");
        await using var bob = await CreateAppAsync(services => services.AddSingleton<IOperatorScopePreferenceStore>(store),
            currentUserPermissions: UserPermission.ViewTrades, currentUserName: "bob");
        var aliceClient = alice.GetTestClient();
        var bobClient = bob.GetTestClient();

        using (var empty = await ReadJsonAsync(aliceClient, OperatorScopePreferencePath))
        {
            empty.RootElement.GetProperty("defaultScope").ValueKind.Should().Be(JsonValueKind.Null);
        }

        var save = await aliceClient.PutAsJsonAsync(OperatorScopePreferencePath, new OperatorScopePreferenceSaveRequest(
            new OperatingScopeValuesDto(Symbol: "  AAPL ", FundAccountId: "11111111-1111-1111-1111-111111111111")));
        save.StatusCode.Should().Be(HttpStatusCode.OK);

        using (var saved = await ReadJsonAsync(aliceClient, OperatorScopePreferencePath))
        {
            var scope = saved.RootElement.GetProperty("defaultScope");
            scope.GetProperty("symbol").GetString().Should().Be("AAPL");
            scope.GetProperty("fundAccountId").GetString().Should().Be("11111111-1111-1111-1111-111111111111");
            saved.RootElement.GetProperty("updatedAt").ValueKind.Should().Be(JsonValueKind.String);
        }

        // Another operator does not see it.
        using (var other = await ReadJsonAsync(bobClient, OperatorScopePreferencePath))
        {
            other.RootElement.GetProperty("defaultScope").ValueKind.Should().Be(JsonValueKind.Null);
        }

        // An empty scope clears the default.
        (await aliceClient.PutAsJsonAsync(OperatorScopePreferencePath, new OperatorScopePreferenceSaveRequest(new OperatingScopeValuesDto())))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        using (var cleared = await ReadJsonAsync(aliceClient, OperatorScopePreferencePath))
        {
            cleared.RootElement.GetProperty("defaultScope").ValueKind.Should().Be(JsonValueKind.Null);
        }
    }

    [Fact]
    public async Task MapWorkstationEndpoints_OperatorScopePreference_RejectsOversizedValues()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-tests", "scope-preference", Guid.NewGuid().ToString("N"));
        var store = new FileOperatorScopePreferenceStore(root, NullLogger<FileOperatorScopePreferenceStore>.Instance);
        await using var app = await CreateAppAsync(services => services.AddSingleton<IOperatorScopePreferenceStore>(store),
            currentUserPermissions: UserPermission.ViewTrades);

        var response = await app.GetTestClient().PutAsJsonAsync(OperatorScopePreferencePath, new OperatorScopePreferenceSaveRequest(
            new OperatingScopeValuesDto(Symbol: new string('X', 129))));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await store.GetAsync("tenant-test|tenant-test|ops-user")).DefaultScope.Should().BeNull();
    }

    [Fact]
    public async Task FileOperatorScopePreferenceStore_SurvivesReopeningTheStore()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-tests", "scope-preference", Guid.NewGuid().ToString("N"));
        var first = new FileOperatorScopePreferenceStore(root, NullLogger<FileOperatorScopePreferenceStore>.Instance);
        await first.SetAsync("t|c|alice", new OperatingScopeValuesDto(Provider: "alpaca"));

        var reopened = new FileOperatorScopePreferenceStore(root, NullLogger<FileOperatorScopePreferenceStore>.Instance);
        (await reopened.GetAsync("t|c|alice")).DefaultScope.Should().Be(new OperatingScopeValuesDto(Provider: "alpaca"));
        (await reopened.GetAsync("t|c|bob")).DefaultScope.Should().BeNull();
    }
}
