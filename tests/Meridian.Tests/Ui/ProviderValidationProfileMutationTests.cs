using FluentAssertions;
using Meridian.Core.Config;
using Meridian.Ui.Shared.Endpoints;
using Xunit;
using System.Text.Json;
using Meridian.Ui.Shared.Services;
using System.Net;
using System.Net.Http.Json;
using System.Threading.RateLimiting;
using Meridian.Identity;
using Meridian.Identity.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Meridian.Tests.Ui;

public sealed class ProviderValidationProfileMutationTests
{
    [Fact]
    public void ApplyProfile_RejectsLooseningWithoutReason()
    {
        var request = new ProviderValidationProfileRequest(new(StaleToleranceDays: 30));
        var change = () => ProviderValidationEndpoints.ApplyProfile(new AppConfig(), "polygon", request, "operator");
        change.Should().Throw<ArgumentException>().WithMessage("*recorded reason*");
    }

    [Fact]
    public void ApplyProfile_PreservesOtherSettingsAndAppendsBeforeAfterEvidence()
    {
        var config = new AppConfig();
        var first = ProviderValidationEndpoints.ApplyProfile(config, "polygon",
            new(new(StaleToleranceDays: 30), "Thinly traded research universe", ConfirmLoosening: true), "researcher");
        var next = ProviderValidationEndpoints.ApplyProfile(first.Config, "polygon",
            new(new(StaleToleranceDays: 5)), "desk");
        next.Config.DataRoot.Should().Be(config.DataRoot);
        next.Config.Backfill!.ValidationProfileAudit.Should().HaveCount(2);
        first.Result.Loosened.Should().BeTrue();
        first.Result.Reason.Should().Be("Thinly traded research universe");
        next.Result.Before.StaleToleranceDays.Should().Be(30);
        next.Result.After.StaleToleranceDays.Should().Be(5);
        next.Result.Loosened.Should().BeFalse();
        next.Config.Backfill.ValidationProfiles!["polygon"].StaleToleranceDays.Should().Be(5);
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SharedUiGuard_RefusesMalformedProfilesWithoutTouchingFile(bool nullProfile)
    {
        var directory = Path.Combine(Path.GetTempPath(), "meridian-profile-guard-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "appsettings.json");
        try
        {
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new AppConfig(), AppConfigJsonOptions.Write));
            var before = await File.ReadAllTextAsync(path);
            var store = new ConfigStore(path);
            var update = () => store.UpdateAsync(config => config with
            {
                Backfill = new BackfillConfig(ValidationProfiles: new Dictionary<string, ProviderValidationProfile>
                {
                    ["polygon"] = nullProfile ? null! : new(MaxLatencyMs: double.NaN)
                })
            });
            var failure = await update.Should().ThrowAsync<UiConfigurationValidationException>();
            failure.Which.Errors.Should().ContainKey("Backfill.ValidationProfiles");
            (await File.ReadAllTextAsync(path)).Should().Be(before);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData(UserPermission.ManageProviders, true, HttpStatusCode.Forbidden)]
    [InlineData(UserPermission.ManageProviders | UserPermission.ModifyConfig, false, HttpStatusCode.Unauthorized)]
    public async Task Endpoint_RequiresConfigPermissionAndTrustedActor(UserPermission permissions, bool actor, HttpStatusCode expected)
    {
        var (app, store, directory) = await CreateApp(permissions, actor);
        try
        {
            var before = await File.ReadAllTextAsync(store.ConfigPath);
            var response = await app.GetTestClient().PutAsJsonAsync("/api/providers/polygon/validation-profile",
                new ProviderValidationProfileRequest(new(StaleToleranceDays: 30), "Research universe"));
            response.StatusCode.Should().Be(expected);
            (await File.ReadAllTextAsync(store.ConfigPath)).Should().Be(before);
        }
        finally { await app.DisposeAsync(); Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Endpoint_RefusesUnexplainedRelaxationThenRetainsTrustedBeforeAfterAudit()
    {
        var (app, store, directory) = await CreateApp(UserPermission.ManageProviders | UserPermission.ModifyConfig, true);
        try
        {
            var before = await File.ReadAllTextAsync(store.ConfigPath);
            var client = app.GetTestClient();
            var refused = await client.PutAsJsonAsync("/api/providers/polygon/validation-profile",
                new ProviderValidationProfileRequest(new(StaleToleranceDays: 30)));
            refused.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await File.ReadAllTextAsync(store.ConfigPath)).Should().Be(before);
            var unconfirmed = await client.PutAsJsonAsync("/api/providers/polygon/validation-profile",
                new ProviderValidationProfileRequest(new(StaleToleranceDays: 30), "Thinly traded symbols"));
            unconfirmed.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await File.ReadAllTextAsync(store.ConfigPath)).Should().Be(before);
            var saved = await client.PutAsJsonAsync("/api/providers/polygon/validation-profile",
                new ProviderValidationProfileRequest(new(StaleToleranceDays: 30), "Thinly traded symbols", ConfirmLoosening: true));
            saved.StatusCode.Should().Be(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
            var audit = store.Load().Backfill!.ValidationProfileAudit!.Single();
            audit.Actor.Should().Be("profile-test-operator");
            audit.Reason.Should().Be("Thinly traded symbols");
            audit.Before.StaleToleranceDays.Should().Be(10);
            audit.After.StaleToleranceDays.Should().Be(30);
        }
        finally { await app.DisposeAsync(); Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Endpoint_DetectsStaleRevisionAndRequiresConfirmationForRelaxingReset()
    {
        var (app, store, directory) = await CreateApp(UserPermission.ManageProviders | UserPermission.ModifyConfig, true);
        const string path = "/api/providers/polygon/validation-profile";
        try
        {
            using var client = app.GetTestClient();
            var initial = await client.GetFromJsonAsync<JsonElement>(path);
            var initialRevision = initial.GetProperty("revision").GetString();
            var tighten = await client.PutAsJsonAsync(path, new ProviderValidationProfileRequest(
                new(StaleToleranceDays: 4), ExpectedRevision: initialRevision));
            tighten.StatusCode.Should().Be(HttpStatusCode.OK, await tighten.Content.ReadAsStringAsync());
            var tightened = await client.GetFromJsonAsync<JsonElement>(path);
            var revision = tightened.GetProperty("revision").GetString();

            var stale = await client.PutAsJsonAsync(path, new ProviderValidationProfileRequest(
                new(StaleToleranceDays: 3), ExpectedRevision: initialRevision));
            stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
            var refused = await client.PutAsJsonAsync(path, new ProviderValidationProfileRequest(
                Reason: "Restore policy", ResetToDefault: true, ExpectedRevision: revision));
            refused.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            var reset = await client.PutAsJsonAsync(path, new ProviderValidationProfileRequest(
                Reason: "Restore policy", ConfirmLoosening: true, ResetToDefault: true, ExpectedRevision: revision));
            reset.StatusCode.Should().Be(HttpStatusCode.OK, await reset.Content.ReadAsStringAsync());

            store.Load().Backfill!.ValidationProfiles.Should().BeEmpty();
            var persisted = await client.GetFromJsonAsync<JsonElement>(path);
            persisted.GetProperty("isOverride").GetBoolean().Should().BeFalse();
            persisted.GetProperty("profile").GetProperty("staleToleranceDays").GetInt32().Should().Be(10);
            var audit = persisted.GetProperty("audit")[0];
            audit.GetProperty("resetToDefault").GetBoolean().Should().BeTrue();
            audit.GetProperty("before").GetProperty("staleToleranceDays").GetInt32().Should().Be(4);
            audit.GetProperty("after").GetProperty("staleToleranceDays").GetInt32().Should().Be(10);
        }
        finally { await app.DisposeAsync(); Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Endpoint_DataViewerCanReadTheDefaultWithoutWriteAuthority()
    {
        var (app, _, directory) = await CreateApp(UserPermission.ViewHistoricalData, true);
        try
        {
            using var client = app.GetTestClient();
            var profile = await client.GetFromJsonAsync<JsonElement>("/api/providers/polygon/validation-profile");
            profile.GetProperty("canEdit").GetBoolean().Should().BeFalse();
            profile.GetProperty("defaultProfile").GetProperty("staleToleranceDays").GetInt32().Should().Be(10);
            var refused = await client.PutAsJsonAsync("/api/providers/polygon/validation-profile",
                new ProviderValidationProfileRequest(new(StaleToleranceDays: 5)));
            refused.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
        finally { await app.DisposeAsync(); Directory.Delete(directory, true); }
    }

    private static async Task<(WebApplication App, ConfigStore Store, string Directory)> CreateApp(UserPermission permissions, bool actor)
    {
        var directory = Path.Combine(Path.GetTempPath(), "meridian-profile-endpoint-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "appsettings.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new AppConfig(), AppConfigJsonOptions.Write));
        var store = new ConfigStore(path);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(store);
        builder.Services.AddRateLimiter(options => options.AddPolicy(UiEndpoints.MutationRateLimitPolicy,
            _ => RateLimitPartition.GetNoLimiter("test")));
        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = permissions;
            context.Items[LoginSessionMiddleware.CurrentTenantIdKey] = "profile-test-tenant";
            if (actor)
                context.Items[LoginSessionMiddleware.CurrentUserKey] = "profile-test-operator";
            await next();
        });
        app.UseRateLimiter();
        app.MapProviderValidationEndpoints(new JsonSerializerOptions(JsonSerializerDefaults.Web));
        await app.StartAsync();
        return (app, store, directory);
    }

}
