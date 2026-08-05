using System.Net;
using System.Text.Json;
using FluentAssertions;
using Meridian.Identity;
using Meridian.Identity.Auth;
using Meridian.Tests.Identity;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Tests.Integration.EndpointTests;

/// <summary>
/// Guards the operator-visible failure mode when a host maps operational routes without their
/// required backfill runtime services.
/// </summary>
[Trait("Category", "Integration")]
[Collection("IdentityEnvironment")]
public sealed class OperationalProblemDetailsEndpointTests
{
    [Fact]
    public async Task ApiKeyMiddleware_InvalidCredential_ReturnsUnauthorizedProblem()
    {
        using var environment = new EnvironmentVariableScope()
            .Set("MDC_API_KEY", "expected-api-key")
            .Set("MDC_DISABLE_RATE_LIMIT", "true");
        await using var app = await CreateMiddlewareHostAsync(
            static host => host.UseApiKeyAuthentication(),
            static host => host.MapGet("/api/problem-details/protected", () => Results.Ok()));
        using var client = app.GetTestClient();

        var response = await client.GetAsync("/api/problem-details/protected");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        await AssertProblemDetailsAsync(
            response,
            ApiProblemTypes.Unauthorized,
            "Authentication Required");
    }

    [Fact]
    public async Task CookieCsrfMiddleware_InvalidToken_ReturnsForbiddenProblem()
    {
        await using var app = await CreateMiddlewareHostAsync(
            static host => host.UseCookieCsrfProtection(),
            static host => host.MapPost("/api/problem-details/mutate", () => Results.Ok()));
        using var client = app.GetTestClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/problem-details/mutate");
        request.Headers.Add(
            "Cookie",
            $"{LoginSessionMiddleware.SessionCookieName}=session-without-csrf");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await AssertProblemDetailsAsync(
            response,
            ApiProblemTypes.Forbidden,
            "Access Denied");
    }

    [Fact]
    public async Task ApiKeyRateLimitMiddleware_LimitExceeded_ReturnsTooManyRequestsProblem()
    {
        using var environment = new EnvironmentVariableScope()
            .Set("MDC_DISABLE_RATE_LIMIT", null);
        await using var app = await CreateMiddlewareHostAsync(
            static host => host.UseMiddleware<ApiKeyRateLimitMiddleware>(),
            static host => host.MapGet("/api/problem-details/rate-limited", () => Results.Ok()));
        using var client = app.GetTestClient();

        for (var requestNumber = 0; requestNumber < 120; requestNumber++)
        {
            using var accepted = await client.GetAsync("/api/problem-details/rate-limited");
            accepted.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        using var response = await client.GetAsync("/api/problem-details/rate-limited");

        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter.Should().NotBeNull();
        var problem = await AssertProblemDetailsAsync(
            response,
            ApiProblemTypes.TooManyRequests,
            "Rate Limit Exceeded");
        problem.GetProperty("limit").GetInt32().Should().Be(120);
        problem.GetProperty("retryAfterSeconds").GetInt32().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task LoginSessionMiddleware_RequiredAuthenticationNotConfigured_ReturnsServiceUnavailableProblem()
    {
        using var environment = new EnvironmentVariableScope()
            .Set("MDC_USERS", null)
            .Set("MDC_DEMO_USERS", null)
            .Set("MDC_USERNAME", null)
            .Set("MDC_PASSWORD_HASH", null)
            .Set("MDC_AUTH_MODE", "required");
        var sessionService = new LoginSessionService(
            new FakeHostEnvironment("Production"),
            new UserProfileRegistry());
        await using var app = await CreateMiddlewareHostAsync(
            static host => host.UseLoginSessionAuthentication(),
            static host => host.MapGet("/api/problem-details/session", () => Results.Ok()),
            services => services.AddSingleton(sessionService));
        using var client = app.GetTestClient();

        var response = await client.GetAsync("/api/problem-details/session");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var problem = await AssertProblemDetailsAsync(
            response,
            ApiProblemTypes.ServiceUnavailable,
            "Service Unavailable");
        problem.GetProperty("service").GetString().Should().Be("authentication");
    }

    [Fact]
    public async Task BackfillHost_MissingCoordinator_FailsClosedWithServiceUnavailableProblem()
    {
        await using var app = await CreateHostAsync(static (host, jsonOptions) =>
            host.MapBackfillEndpoints(jsonOptions, jsonOptions));
        using var client = app.GetTestClient();

        var response = await client.GetAsync("/api/backfill/providers");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        await AssertProblemDetailsAsync(
            response,
            ApiProblemTypes.ServiceUnavailable,
            "Service Unavailable");
    }

    [Fact]
    public async Task BackfillHost_MissingPermissionContext_ReturnsUnauthorizedProblem()
    {
        await using var app = await CreateHostAsync(
            static (host, jsonOptions) => host.MapBackfillEndpoints(jsonOptions, jsonOptions),
            includePermissions: false);
        using var client = app.GetTestClient();

        var response = await client.GetAsync("/api/backfill/providers");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        await AssertProblemDetailsAsync(
            response,
            ApiProblemTypes.Unauthorized,
            "Authentication Required");
    }

    [Fact]
    public async Task BackfillHost_MissingTenantScope_ReturnsForbiddenProblem()
    {
        await using var app = await CreateHostAsync(
            static (host, jsonOptions) => host.MapBackfillEndpoints(jsonOptions, jsonOptions),
            includeTenant: false);
        using var client = app.GetTestClient();

        var response = await client.GetAsync("/api/backfill/providers");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await AssertProblemDetailsAsync(
            response,
            ApiProblemTypes.Forbidden,
            "Access Denied");
    }

    [Fact]
    public async Task BackfillScheduling_MissingManager_FailsClosedWithServiceUnavailableProblem()
    {
        await using var app = await CreateHostAsync(static (host, jsonOptions) =>
            host.MapBackfillScheduleEndpoints(jsonOptions));
        using var client = app.GetTestClient();

        var response = await client.GetAsync("/api/backfill/health");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var problem = await AssertProblemDetailsAsync(
            response,
            ApiProblemTypes.ServiceUnavailable,
            "Service Unavailable");
        problem.GetProperty("service").GetString().Should().Be("backfill schedule manager");
    }

    [Fact]
    public async Task ExceptionHandler_UnhandledFailure_ReturnsSafeInternalProblem()
    {
        await using var app = await CreateHostAsync(static (host, _) =>
            host.MapGet(
                "/api/problem-details/unhandled",
                (Func<IResult>)ThrowUnhandledFailure));
        using var client = app.GetTestClient();

        var response = await client.GetAsync("/api/problem-details/unhandled");

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain("sensitive implementation detail");
        using var document = JsonDocument.Parse(body);
        var problem = document.RootElement;
        problem.GetProperty("type").GetString().Should().Be(ApiProblemTypes.Internal);
        problem.GetProperty("title").GetString().Should().Be("Internal Server Error");
        problem.GetProperty("status").GetInt32().Should().Be(StatusCodes.Status500InternalServerError);
        problem.GetProperty("traceId").GetString().Should().NotBeNullOrWhiteSpace();
        problem.GetProperty("timestamp").GetDateTimeOffset().Should().BeCloseTo(
            DateTimeOffset.UtcNow,
            TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task ExceptionHandler_BadRequestFailure_ReturnsValidationProblem()
    {
        await using var app = await CreateHostAsync(static (host, _) =>
            host.MapGet(
                "/api/problem-details/bad-request",
                (Func<IResult>)ThrowBadRequestFailure));
        using var client = app.GetTestClient();

        var response = await client.GetAsync("/api/problem-details/bad-request");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain("sensitive parser detail");
        using var document = JsonDocument.Parse(body);
        var problem = document.RootElement;
        problem.GetProperty("type").GetString().Should().Be(ApiProblemTypes.Validation);
        problem.GetProperty("title").GetString().Should().Be("Validation Failed");
        problem.GetProperty("status").GetInt32().Should().Be(StatusCodes.Status400BadRequest);
        problem.GetProperty("errors").GetProperty("request")[0].GetString()
            .Should().Be("The request could not be processed.");
    }

    [Fact]
    public async Task ExceptionHandler_RequestCancellation_IsNotConvertedToProblem()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var context = new DefaultHttpContext
        {
            RequestAborted = cancellation.Token
        };
        var handler = new MeridianApiExceptionHandler(
            NullLogger<MeridianApiExceptionHandler>.Instance);

        var handled = await handler.TryHandleAsync(
            context,
            new OperationCanceledException(cancellation.Token),
            CancellationToken.None);

        handled.Should().BeFalse();
        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    private static async Task<WebApplication> CreateHostAsync(
        Action<WebApplication, JsonSerializerOptions> mapEndpoints,
        bool includeTenant = true,
        bool includePermissions = true)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddLogging();
        builder.Services.AddMeridianApiProblemDetails();

        var app = builder.Build();
        app.UseExceptionHandler();
        app.Use(async (context, next) =>
        {
            if (includeTenant)
            {
                context.Items[LoginSessionMiddleware.CurrentTenantIdKey] =
                    "tenant-problem-details";
            }

            if (includePermissions)
            {
                context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] =
                    UserPermission.ViewHistoricalData | UserPermission.TriggerBackfill;
            }

            await next(context);
        });

        mapEndpoints(app, UiEndpoints.CreateEndpointJsonOptions());
        await app.StartAsync();
        return app;
    }

    private static async Task<WebApplication> CreateMiddlewareHostAsync(
        Action<WebApplication> configureMiddleware,
        Action<WebApplication> mapEndpoints,
        Action<IServiceCollection>? configureServices = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddLogging();
        builder.Services.AddMeridianApiProblemDetails();
        configureServices?.Invoke(builder.Services);

        var app = builder.Build();
        app.UseExceptionHandler();
        configureMiddleware(app);
        mapEndpoints(app);
        await app.StartAsync();
        return app;
    }

    private static IResult ThrowUnhandledFailure()
        => throw new InvalidOperationException("sensitive implementation detail");

    private static IResult ThrowBadRequestFailure()
        => throw new BadHttpRequestException("sensitive parser detail");

    private static async Task<JsonElement> AssertProblemDetailsAsync(
        HttpResponseMessage response,
        string expectedType,
        string expectedTitle)
    {
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var problem = document.RootElement.Clone();
        problem.GetProperty("type").GetString().Should().Be(expectedType);
        problem.GetProperty("title").GetString().Should().Be(expectedTitle);
        problem.GetProperty("status").GetInt32().Should().Be((int)response.StatusCode);
        problem.GetProperty("instance").GetString().Should().NotBeNullOrWhiteSpace();
        problem.GetProperty("traceId").GetString().Should().NotBeNullOrWhiteSpace();
        problem.GetProperty("timestamp").GetDateTimeOffset().Should().BeCloseTo(
            DateTimeOffset.UtcNow,
            TimeSpan.FromMinutes(1));
        return problem;
    }
}
