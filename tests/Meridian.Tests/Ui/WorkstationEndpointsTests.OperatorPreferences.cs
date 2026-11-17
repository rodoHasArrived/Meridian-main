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
    private const string OperatorPreferencesPath = "/api/workstation/operator/preferences";

    private static FileOperatorPreferenceStore NewOperatorPreferenceStore() => new(
        Path.Combine(Path.GetTempPath(), "meridian-tests", "operator-preferences", Guid.NewGuid().ToString("N")),
        NullLogger<FileOperatorPreferenceStore>.Instance);

    [Fact]
    public async Task MapWorkstationEndpoints_OperatorPreferences_FollowTheOperatorAndStayPrivate()
    {
        var store = NewOperatorPreferenceStore();
        await using var alice = await CreateAppAsync(services => services.AddSingleton<IOperatorPreferenceStore>(store),
            currentUserPermissions: UserPermission.ViewTrades, currentUserName: "alice");
        await using var bob = await CreateAppAsync(services => services.AddSingleton<IOperatorPreferenceStore>(store),
            currentUserPermissions: UserPermission.ViewTrades, currentUserName: "bob");
        var aliceClient = alice.GetTestClient();
        var bobClient = bob.GetTestClient();

        using (var empty = await ReadJsonAsync(aliceClient, OperatorPreferencesPath))
        {
            var preferences = empty.RootElement.GetProperty("preferences");
            preferences.GetProperty("appearance").ValueKind.Should().Be(JsonValueKind.Null);
            empty.RootElement.GetProperty("updatedAt").ValueKind.Should().Be(JsonValueKind.Null);
        }

        // A quant sets London time and compact density at the office ...
        var save = await aliceClient.PutAsJsonAsync(OperatorPreferencesPath, new OperatorPreferencesSaveRequest(
            new OperatorPreferenceValuesDto(
                Appearance: "dark",
                Density: " compact ",
                DisplayTimeZone: "Europe/London",
                PageDescriptionsHidden: true,
                KeyBindings: new Dictionary<string, string> { ["command-palette"] = "Mod+Shift+P" })));
        save.StatusCode.Should().Be(HttpStatusCode.OK);

        // ... and finds them again from another browser.
        using (var saved = await ReadJsonAsync(aliceClient, OperatorPreferencesPath))
        {
            var preferences = saved.RootElement.GetProperty("preferences");
            preferences.GetProperty("appearance").GetString().Should().Be("dark");
            preferences.GetProperty("density").GetString().Should().Be("compact");
            preferences.GetProperty("displayTimeZone").GetString().Should().Be("Europe/London");
            preferences.GetProperty("pageDescriptionsHidden").GetBoolean().Should().BeTrue();
            preferences.GetProperty("keyBindings").GetProperty("command-palette").GetString().Should().Be("Mod+Shift+P");
            saved.RootElement.GetProperty("updatedAt").ValueKind.Should().Be(JsonValueKind.String);
        }

        // Another operator does not see them.
        using (var other = await ReadJsonAsync(bobClient, OperatorPreferencesPath))
        {
            other.RootElement.GetProperty("preferences").GetProperty("displayTimeZone").ValueKind.Should().Be(JsonValueKind.Null);
        }

        // A save replaces the whole set; an empty set clears it.
        (await aliceClient.PutAsJsonAsync(OperatorPreferencesPath, new OperatorPreferencesSaveRequest(
            new OperatorPreferenceValuesDto(Density: "spacious")))).StatusCode.Should().Be(HttpStatusCode.OK);
        using (var replaced = await ReadJsonAsync(aliceClient, OperatorPreferencesPath))
        {
            var preferences = replaced.RootElement.GetProperty("preferences");
            preferences.GetProperty("density").GetString().Should().Be("spacious");
            preferences.GetProperty("displayTimeZone").ValueKind.Should().Be(JsonValueKind.Null);
        }

        (await aliceClient.PutAsJsonAsync(OperatorPreferencesPath, new OperatorPreferencesSaveRequest(new OperatorPreferenceValuesDto())))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        using (var cleared = await ReadJsonAsync(aliceClient, OperatorPreferencesPath))
        {
            cleared.RootElement.GetProperty("preferences").GetProperty("density").ValueKind.Should().Be(JsonValueKind.Null);
            cleared.RootElement.GetProperty("updatedAt").ValueKind.Should().Be(JsonValueKind.Null);
        }
    }

    public static TheoryData<OperatorPreferenceValuesDto, string> InvalidOperatorPreferences => new()
    {
        { new OperatorPreferenceValuesDto(Appearance: "neon"), "Appearance" },
        { new OperatorPreferenceValuesDto(Density: "cosy"), "Density" },
        { new OperatorPreferenceValuesDto(DisplayTimeZone: "London; DROP TABLE"), "DisplayTimeZone" },
        { new OperatorPreferenceValuesDto(DisplayTimeZone: "Europe/" + new string('X', 64)), "DisplayTimeZone" },
        { new OperatorPreferenceValuesDto(KeyBindings: new Dictionary<string, string> { ["Command Palette"] = "Mod+K" }), "KeyBindings" },
        { new OperatorPreferenceValuesDto(KeyBindings: new Dictionary<string, string> { ["command-palette"] = "  " }), "KeyBindings" },
        { new OperatorPreferenceValuesDto(KeyBindings: new Dictionary<string, string> { ["command-palette"] = "Mod+\u0001" }), "KeyBindings" },
        {
            new OperatorPreferenceValuesDto(KeyBindings: Enumerable.Range(0, 33).ToDictionary(index => $"binding-{index}", _ => "Mod+K")),
            "KeyBindings"
        }
    };

    [Theory]
    [MemberData(nameof(InvalidOperatorPreferences))]
    public async Task MapWorkstationEndpoints_OperatorPreferences_RejectsInvalidValuesAndKeepsTheStoredSet(
        OperatorPreferenceValuesDto invalid, string field)
    {
        var store = NewOperatorPreferenceStore();
        await store.SetAsync("tenant-test|tenant-test|ops-user", new OperatorPreferenceValuesDto(Density: "compact"));
        await using var app = await CreateAppAsync(services => services.AddSingleton<IOperatorPreferenceStore>(store),
            currentUserPermissions: UserPermission.ViewTrades);

        var response = await app.GetTestClient().PutAsJsonAsync(OperatorPreferencesPath, new OperatorPreferencesSaveRequest(invalid));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain(field);
        (await store.GetAsync("tenant-test|tenant-test|ops-user")).Preferences.Density.Should().Be("compact");
    }

    [Theory]
    [InlineData("UTC")]
    [InlineData("Europe/London")]
    [InlineData("America/Argentina/Buenos_Aires")]
    [InlineData("Etc/GMT+5")]
    [InlineData("America/Port-au-Prince")]
    public void FileOperatorPreferenceStore_Normalize_AcceptsIanaZoneNames(string zone)
    {
        FileOperatorPreferenceStore.Normalize(new OperatorPreferenceValuesDto(DisplayTimeZone: $" {zone} "))!
            .DisplayTimeZone.Should().Be(zone);
    }

    [Fact]
    public void FileOperatorPreferenceStore_Normalize_TrimsAndTreatsAllBlankAsNothingStored()
    {
        FileOperatorPreferenceStore.Normalize(new OperatorPreferenceValuesDto(Appearance: " ", DisplayTimeZone: "", KeyBindings: new Dictionary<string, string>()))
            .Should().BeNull();

        var normalized = FileOperatorPreferenceStore.Normalize(new OperatorPreferenceValuesDto(
            PageDescriptionsHidden: false,
            KeyBindings: new Dictionary<string, string> { [" keyboard-help "] = " F1 " }))!;
        normalized.PageDescriptionsHidden.Should().BeFalse();
        normalized.KeyBindings.Should().Equal(new Dictionary<string, string> { ["keyboard-help"] = "F1" });
    }

    [Fact]
    public async Task FileOperatorPreferenceStore_SurvivesReopeningTheStore()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-tests", "operator-preferences", Guid.NewGuid().ToString("N"));
        var first = new FileOperatorPreferenceStore(root, NullLogger<FileOperatorPreferenceStore>.Instance);
        await first.SetAsync("t|c|alice", new OperatorPreferenceValuesDto(
            DisplayTimeZone: "Asia/Tokyo",
            KeyBindings: new Dictionary<string, string> { ["keyboard-help"] = "F1" }));

        var reopened = new FileOperatorPreferenceStore(root, NullLogger<FileOperatorPreferenceStore>.Instance);
        var preferences = (await reopened.GetAsync("t|c|alice")).Preferences;

        preferences.DisplayTimeZone.Should().Be("Asia/Tokyo");
        preferences.KeyBindings.Should().ContainKey("keyboard-help").WhoseValue.Should().Be("F1");
        (await reopened.GetAsync("t|c|bob")).Preferences.Should().Be(new OperatorPreferenceValuesDto());
    }
}
