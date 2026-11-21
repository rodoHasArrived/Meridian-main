using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Meridian.Application.Composition;
using Meridian.Contracts.Tenancy;
using Meridian.Identity;
using Meridian.Identity.Auth;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using Xunit;

namespace Meridian.Tests.Ui;

/// <summary>
/// The sign-in endpoint is unauthenticated, so it carries its own per-IP limiter: without one
/// a single client can guess credentials, or churn the bounded failed-attempt table, as fast
/// as it can send requests.
/// </summary>
public sealed class LoginRateLimitEndpointTests
{
    [Fact]
    public async Task Login_BeyondPerIpBudget_IsRejectedBeforeCredentialsAreChecked()
    {
        await using var app = await CreateHostAsync();
        using var http = app.GetTestClient();

        var statuses = new List<HttpStatusCode>();
        HttpResponseMessage? last = null;
        for (var attempt = 0; attempt < 11; attempt++)
        {
            last?.Dispose();
            // A fresh username each time keeps the per-target lockout (5 failures) out of the picture.
            last = await http.PostAsJsonAsync(
                "/api/auth/login",
                new { username = $"unknown-{attempt}", password = "wrong" });
            statuses.Add(last.StatusCode);
        }

        using (last)
        {
            statuses.Take(10).Should().OnlyContain(status => status == HttpStatusCode.Unauthorized);
            statuses[10].Should().Be(HttpStatusCode.TooManyRequests);
            last!.Headers.Contains("Retry-After").Should().BeTrue();
            last.Headers.Contains("RateLimit-Limit").Should().BeFalse(
                "the limiter rejects the request before the login handler runs");
        }
    }

    private static async Task<WebApplication> CreateHostAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.WebHost.UseTestServer();
        var settings = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["MDC_AUTH_MODE"] = "required",
            ["MDC_USERS"] = $$"""
                [{"username":"alice","passwordHash":"{{PasswordHashing.HashPassword("synthetic-rate-limit-password")}}","role":"Admin","companyId":"company-a"}]
                """
        }).Build();
        var authentication = new AuthenticationConfiguration(settings);
        builder.Services.AddSingleton(authentication);
        builder.Services.AddSingleton(new UserProfileRegistry(null, null, TenantScopeEnforcementOptions.FailClosed, authentication));
        builder.Services.AddSingleton<LoginSessionService>();
        // The account-administration routes mapped alongside login resolve these from DI.
        builder.Services.AddSingleton(Mock.Of<IRolePermissionProfileStore>());
        builder.Services.AddSingleton(Mock.Of<IUserAccountStore>());
        builder.Services.AddSingleton(Mock.Of<IScopedAccessAssignmentService>());
        builder.Services.AddMutationRateLimiter(forceEnable: true, configuration: settings);

        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            // TestServer has no socket; give the limiter a stable client address to partition on.
            context.Connection.RemoteIpAddress = IPAddress.Loopback;
            await next();
        });
        app.UseRateLimiter();
        app.MapAuthEndpoints();
        await app.StartAsync();
        return app;
    }
}
