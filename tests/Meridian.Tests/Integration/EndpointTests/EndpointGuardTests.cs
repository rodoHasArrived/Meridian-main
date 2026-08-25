using FluentAssertions;
using Meridian.Identity;
using Meridian.Identity.Auth;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Tests.Integration.EndpointTests;

public sealed class EndpointGuardTests
{
    [Fact]
    public async Task GuardAsync_ReturnsHandlerResultOnSuccess()
    {
        var expected = Results.Ok("payload");

        var result = await EndpointHelpers.GuardAsync(
            () => Task.FromResult(expected), "Operation failed.");

        result.Should().BeSameAs(expected);
    }

    [Fact]
    public async Task GuardAsync_ConvertsFailuresToProblem()
    {
        var result = await EndpointHelpers.GuardAsync(
            () => throw new InvalidOperationException("boom"),
            "Operation failed.");

        var problem = result.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.ProblemDetails.Detail.Should().Be("Operation failed.");
        problem.ProblemDetails.Status.Should().Be(StatusCodes.Status500InternalServerError);
        problem.ProblemDetails.Type.Should().Be(ApiProblemTypes.Internal);
        problem.ProblemDetails.Title.Should().Be("Internal Server Error");
    }

    [Fact]
    public async Task GuardAsync_DoesNotExposeSensitiveExceptionMessageWhenLegacyFlagIsConfigured()
    {
        var result = await EndpointHelpers.GuardAsync(
            () => throw new InvalidOperationException("database password=super-secret"),
            "Operation failed",
            includeExceptionMessage: true);

        var detail = result.Should().BeOfType<ProblemHttpResult>()
            .Which.ProblemDetails.Detail;
        detail.Should().Be("Operation failed");
        detail.Should().NotContain("super-secret");
    }

    [Fact]
    public async Task AuthorizeScopedAsync_MissingScopedAuthorizationService_DeniesGlobalPermissionFallback()
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var context = new DefaultHttpContext
        {
            RequestServices = services
        };
        context.Items[LoginSessionMiddleware.CurrentUserKey] = "global-admin";
        context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = UserPermission.ManageProviders;

        var decision = await EndpointAuthorization.AuthorizeScopedAsync(
            context,
            UserPermission.ManageProviders,
            AccessScopeKindDto.Fund,
            Guid.NewGuid());

        decision.IsAllowed.Should().BeFalse();
        decision.Reason.Should().Contain("service unavailable");
    }

    [Fact]
    public async Task AuthorizeScopedAsync_ApiKeyCannotInheritUserScopedAssignments()
    {
        var scopedAuthorization = new RecordingScopedAuthorizationService(isAllowed: true);
        var services = new ServiceCollection()
            .AddSingleton<IScopedAuthorizationService>(scopedAuthorization)
            .BuildServiceProvider();
        var context = new DefaultHttpContext
        {
            RequestServices = services
        };
        context.Items[LoginSessionMiddleware.CurrentUserKey] = ApiKeyMiddleware.ApiKeyActor;
        context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = UserPermission.ViewTrades;
        context.Items[ApiKeyMiddleware.ApiKeyPrincipalKey] = true;

        var decision = await EndpointAuthorization.AuthorizeScopedAsync(
            context,
            UserPermission.ViewTrades,
            AccessScopeKindDto.Account,
            Guid.NewGuid());

        decision.IsAllowed.Should().BeFalse();
        decision.Reason.Should().Contain("API-key principals");
        scopedAuthorization.Calls.Should().Be(0, "a key must never be resolved as a User principal");
    }

    [Fact]
    public async Task AuthorizeScopedAsync_ApiKeyWithGlobalOverrideDoesNotNeedUserAssignment()
    {
        var scopedAuthorization = new RecordingScopedAuthorizationService(isAllowed: false);
        var services = new ServiceCollection()
            .AddSingleton<IScopedAuthorizationService>(scopedAuthorization)
            .BuildServiceProvider();
        var context = new DefaultHttpContext
        {
            RequestServices = services
        };
        context.Items[LoginSessionMiddleware.CurrentUserKey] = ApiKeyMiddleware.ApiKeyActor;
        context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = UserPermission.AdminMaintenance;
        context.Items[ApiKeyMiddleware.ApiKeyPrincipalKey] = true;

        var decision = await EndpointAuthorization.AuthorizeScopedAsync(
            context,
            UserPermission.ViewTrades,
            AccessScopeKindDto.Account,
            Guid.NewGuid());

        decision.IsAllowed.Should().BeTrue();
        scopedAuthorization.Calls.Should().Be(0);
    }

    [Fact]
    public async Task AuthorizeScopedAsync_HumanNamedApiKeyUsesUserScopedAssignments()
    {
        var scopedAuthorization = new RecordingScopedAuthorizationService(isAllowed: true);
        var services = new ServiceCollection()
            .AddSingleton<IScopedAuthorizationService>(scopedAuthorization)
            .BuildServiceProvider();
        var context = new DefaultHttpContext
        {
            RequestServices = services
        };
        context.Items[LoginSessionMiddleware.CurrentUserKey] = ApiKeyMiddleware.ApiKeyActor;
        context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = UserPermission.ViewTrades;

        var decision = await EndpointAuthorization.AuthorizeScopedAsync(
            context,
            UserPermission.ViewTrades,
            AccessScopeKindDto.Account,
            Guid.NewGuid());

        decision.IsAllowed.Should().BeTrue();
        scopedAuthorization.Calls.Should().Be(1);
    }

    [Fact]
    public async Task AuthorizeScopedAsync_AnonymousOperatorCannotInheritSameNamedUserAssignments()
    {
        var scopedAuthorization = new RecordingScopedAuthorizationService(isAllowed: true);
        var services = new ServiceCollection()
            .AddSingleton<IScopedAuthorizationService>(scopedAuthorization)
            .BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services };
        context.Items[LoginSessionMiddleware.CurrentUserKey] = LoginSessionMiddleware.AnonymousLocalActor;
        context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = UserPermission.ViewTrades;
        context.Items[LoginSessionMiddleware.AnonymousPrincipalKey] = true;

        var decision = await EndpointAuthorization.AuthorizeScopedAsync(
            context,
            UserPermission.ViewTrades,
            AccessScopeKindDto.Account,
            Guid.NewGuid());

        decision.IsAllowed.Should().BeFalse();
        decision.Reason.Should().Contain("Anonymous principals");
        scopedAuthorization.Calls.Should().Be(0, "an anonymous actor is not a User principal");
    }

    [Fact]
    public async Task AuthorizeScopedManyAsync_AnonymousOperatorCannotInheritSameNamedUserAssignments()
    {
        var scopedAuthorization = new RecordingScopedAuthorizationService(isAllowed: true);
        var services = new ServiceCollection()
            .AddSingleton<IScopedAuthorizationService>(scopedAuthorization)
            .BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services };
        context.Items[LoginSessionMiddleware.CurrentUserKey] = LoginSessionMiddleware.AnonymousLocalActor;
        context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = UserPermission.ViewTrades;
        context.Items[LoginSessionMiddleware.AnonymousPrincipalKey] = true;

        var allowed = await EndpointAuthorization.AuthorizeScopedManyAsync(
            context,
            UserPermission.ViewTrades,
            AccessScopeKindDto.Account,
            [Guid.NewGuid(), Guid.NewGuid()]);

        allowed.Should().BeEmpty();
        scopedAuthorization.Calls.Should().Be(0, "the batch helper must preserve the same principal-kind boundary");
    }

    [Fact]
    public async Task GuardAsync_PropagatesCancellation()
    {
        var act = () => EndpointHelpers.GuardAsync(
            () => throw new OperationCanceledException(),
            "Operation failed.");

        await act.Should().ThrowAsync<OperationCanceledException>(
            "request aborts must not be reported as endpoint failures");
    }

    [Fact]
    public async Task GuardAsync_UsesExceptionMappingBeforeGenericProblem()
    {
        var result = await EndpointHelpers.GuardAsync(
            () => throw new KeyNotFoundException("missing"),
            "Operation failed.",
            mapException: ex => ex is KeyNotFoundException ? Results.NotFound("missing") : null);

        result.Should().BeOfType<NotFound<string>>();
    }

    [Fact]
    public async Task GuardAsync_FallsThroughWhenMappingReturnsNull()
    {
        var result = await EndpointHelpers.GuardAsync(
            () => throw new InvalidOperationException("boom"),
            "Operation failed.",
            mapException: static _ => null);

        result.Should().BeOfType<ProblemHttpResult>();
    }

    [Fact]
    public async Task GuardAsync_LogsFailuresWhenLoggerSupplied()
    {
        var logger = new CapturingLogger();

        await EndpointHelpers.GuardAsync(
            () => throw new InvalidOperationException("boom"),
            "Operation failed.",
            logger);

        logger.Errors.Should().ContainSingle()
            .Which.Should().Contain("Operation failed.");
    }

    [Fact]
    public async Task HandleAsync_MissingRuntime_ReturnsServiceUnavailableProblem()
    {
        var result = await EndpointHelpers.HandleAsync<object>(
            service: null,
            static _ => Task.FromResult<object>("unused"),
            new System.Text.Json.JsonSerializerOptions());

        var problem = result.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.ProblemDetails.Status.Should().Be(StatusCodes.Status503ServiceUnavailable);
        problem.ProblemDetails.Type.Should().Be(ApiProblemTypes.ServiceUnavailable);
        problem.ProblemDetails.Title.Should().Be("Service Unavailable");
        problem.ProblemDetails.Extensions["service"].Should().Be(nameof(Object));
    }

    [Fact]
    public async Task HandleAsync_ArgumentFailure_DoesNotExposeSensitiveExceptionMessage()
    {
        var result = await EndpointHelpers.HandleAsync(
            new object(),
            static _ => Task.FromException<object>(
                new ArgumentException("invalid token secret-value")),
            new System.Text.Json.JsonSerializerOptions());

        var problem = result.Should().BeOfType<ProblemHttpResult>().Subject;
        var validation = problem.ProblemDetails
            .Should().BeOfType<HttpValidationProblemDetails>()
            .Subject;
        validation.Errors["request"].Should().Equal("The request is invalid.");
        validation.Errors["request"].Should().NotContain(message => message.Contains("secret-value"));
    }

    [Fact]
    public async Task HandleAsync_RequestCancellation_PropagatesWithoutProblemResponse()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => EndpointHelpers.HandleAsync(
            new object(),
            static (_, ct) => Task.FromCanceled<object>(ct),
            new System.Text.Json.JsonSerializerOptions(),
            cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>(
            "a disconnected caller must not receive a synthetic endpoint failure");
    }

    private sealed class RecordingScopedAuthorizationService(bool isAllowed) : IScopedAuthorizationService
    {
        public int Calls { get; private set; }

        public Task<ScopedAuthorizationDecisionDto> AuthorizeAsync(
            string actor,
            UserPermission requiredPermission,
            AccessScopeKindDto scopeKind,
            Guid? scopeId,
            UserPermission globalPermissions,
            CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(new ScopedAuthorizationDecisionDto(
                isAllowed,
                actor,
                requiredPermission,
                scopeKind,
                scopeId,
                isAllowed ? "Test scope grants access." : "Test scope denies access."));
        }
    }

    private sealed class CapturingLogger : ILogger
    {
        public List<string> Errors { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
            => NullLogger.Instance.BeginScope(state);

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Error)
                Errors.Add(formatter(state, exception));
        }
    }
}
