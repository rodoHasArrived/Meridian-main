using System.Collections.Immutable;
using System.Text.Json;
using Meridian.Contracts.Reporting;
using Meridian.Identity.Auth;
using Meridian.Reporting;
using Meridian.Storage.Reporting;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// Canonical governed-reporting lifecycle API. This mapper is intentionally separate from the
/// legacy report-pack routes so one integration call can activate the new surface without growing
/// the already-large <c>FundStructureEndpoints.cs</c> file.
/// </summary>
public static partial class FundStructureEndpoints
{
    public static void MapReportingGovernanceEndpoints(
        this WebApplication app,
        JsonSerializerOptions jsonOptions)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(jsonOptions);

        var group = app
            .MapGroup("/api/fund-structure/reporting/runs")
            .WithTags("Reporting Governance")
            .RequireWorkstationTenantScope();

        group.MapGet("/series/{seriesId}", (string seriesId, HttpContext context) =>
                GetGovernedReportingSeriesAsync(seriesId, context, jsonOptions))
            .WithName("GetGovernedReportingSeries")
            .Produces<ReportingGovernanceSeriesHistoryDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .RequireAnyPermission(
                UserPermission.ViewReporting,
                UserPermission.ManageReporting,
                UserPermission.ApproveReporting,
                UserPermission.DeliverReporting,
                UserPermission.AdminMaintenance);

        group.MapGet("/{runId}/restatement-requests", (string runId, HttpContext context) =>
                ListGovernedReportingRestatementsAsync(runId, context, jsonOptions))
            .WithName("ListGovernedReportingRestatements")
            .Produces<IReadOnlyList<ReportingGovernanceRestatementDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .RequireAnyPermission(
                UserPermission.ViewReporting,
                UserPermission.ManageReporting,
                UserPermission.ApproveReporting,
                UserPermission.DeliverReporting,
                UserPermission.AdminMaintenance);

        group.MapGet("/restatement-requests/{requestId}", (string requestId, HttpContext context) =>
                GetGovernedReportingRestatementAsync(requestId, context, jsonOptions))
            .WithName("GetGovernedReportingRestatement")
            .Produces<ReportingGovernanceRestatementDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .RequireAnyPermission(
                UserPermission.ViewReporting,
                UserPermission.ManageReporting,
                UserPermission.ApproveReporting,
                UserPermission.DeliverReporting,
                UserPermission.AdminMaintenance);

        group.MapGet("/{runId}", (string runId, HttpContext context) =>
                GetGovernedReportingRunAsync(runId, context, jsonOptions))
            .WithName("GetGovernedReportingRun")
            .Produces<GovernedReportingRunDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .RequireAnyPermission(
                UserPermission.ViewReporting,
                UserPermission.ManageReporting,
                UserPermission.ApproveReporting,
                UserPermission.DeliverReporting,
                UserPermission.AdminMaintenance);

        group.MapGet("/{runId}/artifacts", (string runId, HttpContext context) =>
                ListGovernedReportingArtifactsAsync(runId, context, jsonOptions))
            .WithName("ListGovernedReportingArtifacts")
            .Produces<IReadOnlyList<ReportingGovernanceRetainedArtifactDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .RequireAnyPermission(
                UserPermission.ViewReporting,
                UserPermission.ManageReporting,
                UserPermission.ApproveReporting,
                UserPermission.DeliverReporting,
                UserPermission.AdminMaintenance);

        group.MapGet("/{runId}/artifacts/{artifactId}", (
                string runId,
                string artifactId,
                HttpContext context) => DownloadGovernedReportingArtifactAsync(runId, artifactId, context))
            .WithName("DownloadGovernedReportingArtifact")
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .RequireAnyPermission(
                UserPermission.ViewReporting,
                UserPermission.ManageReporting,
                UserPermission.ApproveReporting,
                UserPermission.DeliverReporting,
                UserPermission.AdminMaintenance);

        group.MapPost("/{runId}/govern", (
                string runId,
                HttpContext context) => GovernCompletedReportingRunAsync(runId, context, jsonOptions))
            .WithName("GovernCompletedReportingRun")
            .Produces<GovernedReportingRunDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .RequireAnyPermission(UserPermission.ManageReporting, UserPermission.AdminMaintenance);

        group.MapPost("/{runId}/validate", (
                string runId,
                ReportingGovernanceVersionRequestDto request,
                HttpContext context) => ValidateGovernedReportingRunAsync(runId, request, context, jsonOptions))
            .WithName("ValidateGovernedReportingRun")
            .Accepts<ReportingGovernanceVersionRequestDto>("application/json")
            .Produces<GovernedReportingRunDto>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .RequireAnyPermission(UserPermission.ManageReporting, UserPermission.AdminMaintenance);

        group.MapPost("/{runId}/submit", (
                string runId,
                ReportingGovernanceVersionRequestDto request,
                HttpContext context) => SubmitGovernedReportingRunAsync(runId, request, context, jsonOptions))
            .WithName("SubmitGovernedReportingRun")
            .Accepts<ReportingGovernanceVersionRequestDto>("application/json")
            .Produces<GovernedReportingRunDto>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .RequireAnyPermission(UserPermission.ManageReporting, UserPermission.AdminMaintenance);

        group.MapPost("/{runId}/approve", (
                string runId,
                ReportingGovernanceApprovalRequestDto request,
                HttpContext context) => ApproveGovernedReportingRunAsync(runId, request, context, jsonOptions))
            .WithName("ApproveGovernedReportingRun")
            .Accepts<ReportingGovernanceApprovalRequestDto>("application/json")
            .Produces<GovernedReportingRunDto>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .RequireAnyPermission(UserPermission.ApproveReporting, UserPermission.AdminMaintenance);

        group.MapPost("/{runId}/release", (
                string runId,
                ReportingGovernanceVersionRequestDto request,
                HttpContext context) => ReleaseGovernedReportingRunAsync(runId, request, context, jsonOptions))
            .WithName("ReleaseGovernedReportingRun")
            .Accepts<ReportingGovernanceVersionRequestDto>("application/json")
            .Produces<GovernedReportingRunDto>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .RequireAnyPermission(UserPermission.DeliverReporting, UserPermission.AdminMaintenance);

        group.MapPost("/{runId}/restatement-requests", (
                string runId,
                ReportingGovernanceRestatementRequestDto request,
                HttpContext context) => RequestGovernedReportingRestatementAsync(runId, request, context, jsonOptions))
            .WithName("RequestGovernedReportingRestatement")
            .Accepts<ReportingGovernanceRestatementRequestDto>("application/json")
            .Produces<ReportingGovernanceRestatementDto>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .RequireAnyPermission(UserPermission.ManageReporting, UserPermission.AdminMaintenance);

        group.MapPost(
                "/restatement-requests/{requestId}/approve",
                (
                    string requestId,
                    ReportingGovernanceRestatementApprovalRequestDto request,
                    HttpContext context) => ApproveGovernedReportingRestatementAsync(
                        requestId,
                        request,
                        context,
                        jsonOptions))
            .WithName("ApproveGovernedReportingRestatement")
            .Accepts<ReportingGovernanceRestatementApprovalRequestDto>("application/json")
            .Produces<ReportingGovernanceRestatementApprovalDto>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .RequireAnyPermission(UserPermission.ApproveReporting, UserPermission.AdminMaintenance);
    }

    private static Task<IResult> GetGovernedReportingRunAsync(
        string runId,
        HttpContext context,
        JsonSerializerOptions jsonOptions) =>
        ExecuteGovernanceAsync(
            context,
            runId,
            static (coordinator, caller, id, ct) => coordinator.GetAsync(id, caller, ct),
            ReportingGovernanceApiProjector.ProjectRun,
            StatusCodes.Status200OK,
            jsonOptions);

    private static Task<IResult> ListGovernedReportingArtifactsAsync(
        string runId,
        HttpContext context,
        JsonSerializerOptions jsonOptions) =>
        ExecuteGovernanceAsync(
            context,
            runId,
            static (coordinator, caller, id, ct) => coordinator.ListRetainedArtifactsAsync(id, caller, ct),
            static (artifacts, _) => artifacts.Select(static artifact =>
                new ReportingGovernanceRetainedArtifactDto(
                    artifact.ArtifactId,
                    artifact.FileName,
                    artifact.ContentType,
                    artifact.ByteLength,
                    artifact.ContentHashSha256,
                    artifact.Kind.ToString(),
                    artifact.IsPreview,
                    artifact.DownloadRoute)).ToArray(),
            StatusCodes.Status200OK,
            jsonOptions);

    private static async Task<IResult> DownloadGovernedReportingArtifactAsync(
        string runId,
        string artifactId,
        HttpContext context)
    {
        if (!TryResolveReportingGovernanceCaller(context, out var caller, out var callerError))
        {
            return callerError!;
        }
        var runValidation = ValidateRequiredText(runId, "runId", 256);
        if (runValidation is not null)
        {
            return runValidation;
        }
        var artifactTokenValidation = ValidateRequiredText(artifactId, "artifactId", 512);
        if (artifactTokenValidation is not null)
        {
            return artifactTokenValidation;
        }
        if (!ReportingArtifactRouteToken.TryDecode(artifactId, out var decodedArtifactId))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["artifactId"] = ["The artifact route token is invalid."]
            });
        }
        var artifactValidation = ValidateRequiredText(decodedArtifactId, "artifactId", 512);
        if (artifactValidation is not null)
        {
            return artifactValidation;
        }
        var coordinator = context.RequestServices.GetService<IReportingGovernanceEndpointCoordinator>();
        if (coordinator is null)
        {
            return Results.Problem(
                "Governed reporting lifecycle service is unavailable.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        try
        {
            var download = await coordinator.DownloadRetainedArtifactAsync(
                    runId.Trim(),
                    decodedArtifactId,
                    caller!,
                    context.RequestAborted)
                .ConfigureAwait(false);
            return Results.File(
                download.Content,
                download.Descriptor.ContentType,
                download.Descriptor.FileName,
                enableRangeProcessing: false);
        }
        catch (ReportingGovernancePersistenceException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (ReportingGovernanceAuthorizationException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status403Forbidden);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status403Forbidden);
        }
        catch (ReportingGovernanceNotFoundException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status404NotFound);
        }
        catch (KeyNotFoundException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status404NotFound);
        }
        catch (ReportingGovernanceException ex)
        {
            // Includes the release-gate refusal for unreleased primaries. Downloads carry no
            // expected version, so a concurrency subclass arriving here is still a state conflict.
            return ApiProblemDetails.Conflict(context, ex.Message);
        }
        catch (IOException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (ArgumentException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
        catch (NotSupportedException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    private static Task<IResult> GetGovernedReportingSeriesAsync(
        string seriesId,
        HttpContext context,
        JsonSerializerOptions jsonOptions) =>
        ExecuteGovernanceAsync(
            context,
            seriesId,
            static (coordinator, caller, id, ct) => coordinator.GetSeriesHistoryAsync(id, caller, ct),
            ReportingGovernanceApiProjector.ProjectSeriesHistory,
            StatusCodes.Status200OK,
            jsonOptions);

    private static Task<IResult> ListGovernedReportingRestatementsAsync(
        string runId,
        HttpContext context,
        JsonSerializerOptions jsonOptions) =>
        ExecuteGovernanceAsync(
            context,
            runId,
            static (coordinator, caller, id, ct) =>
                coordinator.ListRestatementRequestsAsync(id, caller, ct),
            ReportingGovernanceApiProjector.ProjectRestatements,
            StatusCodes.Status200OK,
            jsonOptions);

    private static Task<IResult> GetGovernedReportingRestatementAsync(
        string requestId,
        HttpContext context,
        JsonSerializerOptions jsonOptions) =>
        ExecuteGovernanceAsync(
            context,
            requestId,
            static (coordinator, caller, id, ct) => coordinator.GetRestatementRequestAsync(id, caller, ct),
            ReportingGovernanceApiProjector.ProjectRestatement,
            StatusCodes.Status200OK,
            jsonOptions);

    private static Task<IResult> GovernCompletedReportingRunAsync(
        string runId,
        HttpContext context,
        JsonSerializerOptions jsonOptions) =>
        ExecuteGovernanceAsync(
            context,
            runId,
            static (coordinator, caller, id, ct) =>
                coordinator.CreateFromCompletedCertifiedManifestAsync(id, caller, ct),
            ReportingGovernanceApiProjector.ProjectRun,
            StatusCodes.Status201Created,
            jsonOptions);

    private static Task<IResult> ValidateGovernedReportingRunAsync(
        string runId,
        ReportingGovernanceVersionRequestDto request,
        HttpContext context,
        JsonSerializerOptions jsonOptions)
    {
        var validation = ValidateExpectedVersion(request.ExpectedVersion);
        return validation is null
            ? ExecuteGovernanceAsync(
                context,
                runId,
                (coordinator, caller, id, ct) =>
                    coordinator.ValidateAsync(id, request.ExpectedVersion, caller, ct),
                ReportingGovernanceApiProjector.ProjectRun,
                StatusCodes.Status200OK,
                jsonOptions)
            : Task.FromResult(validation);
    }

    private static Task<IResult> SubmitGovernedReportingRunAsync(
        string runId,
        ReportingGovernanceVersionRequestDto request,
        HttpContext context,
        JsonSerializerOptions jsonOptions)
    {
        var validation = ValidateExpectedVersion(request.ExpectedVersion);
        return validation is null
            ? ExecuteGovernanceAsync(
                context,
                runId,
                (coordinator, caller, id, ct) =>
                    coordinator.SubmitAsync(id, request.ExpectedVersion, caller, ct),
                ReportingGovernanceApiProjector.ProjectRun,
                StatusCodes.Status200OK,
                jsonOptions)
            : Task.FromResult(validation);
    }

    private static Task<IResult> ApproveGovernedReportingRunAsync(
        string runId,
        ReportingGovernanceApprovalRequestDto request,
        HttpContext context,
        JsonSerializerOptions jsonOptions)
    {
        var validation = ValidateExpectedVersion(request.ExpectedVersion)
            ?? ValidateRequiredText(request.DecisionNote, "decisionNote", 4_000);
        return validation is null
            ? ExecuteGovernanceAsync(
                context,
                runId,
                (coordinator, caller, id, ct) =>
                    coordinator.ApproveAsync(id, request.ExpectedVersion, request.DecisionNote.Trim(), caller, ct),
                ReportingGovernanceApiProjector.ProjectRun,
                StatusCodes.Status200OK,
                jsonOptions)
            : Task.FromResult(validation);
    }

    private static Task<IResult> ReleaseGovernedReportingRunAsync(
        string runId,
        ReportingGovernanceVersionRequestDto request,
        HttpContext context,
        JsonSerializerOptions jsonOptions)
    {
        var validation = ValidateExpectedVersion(request.ExpectedVersion);
        return validation is null
            ? ExecuteGovernanceAsync(
                context,
                runId,
                (coordinator, caller, id, ct) =>
                    coordinator.ReleaseAsync(id, request.ExpectedVersion, caller, ct),
                ReportingGovernanceApiProjector.ProjectRun,
                StatusCodes.Status200OK,
                jsonOptions)
            : Task.FromResult(validation);
    }

    private static Task<IResult> RequestGovernedReportingRestatementAsync(
        string runId,
        ReportingGovernanceRestatementRequestDto request,
        HttpContext context,
        JsonSerializerOptions jsonOptions)
    {
        var validation = ValidateExpectedVersion(request.ExpectedVersion)
            ?? ValidateRequiredText(request.Reason, "reason", 4_000);
        return validation is null
            ? ExecuteGovernanceAsync(
                context,
                runId,
                (coordinator, caller, id, ct) => coordinator.RequestRestatementAsync(
                    id,
                    request.ExpectedVersion,
                    request.Reason.Trim(),
                    caller,
                    ct),
                ReportingGovernanceApiProjector.ProjectRestatement,
                StatusCodes.Status201Created,
                jsonOptions)
            : Task.FromResult(validation);
    }

    private static Task<IResult> ApproveGovernedReportingRestatementAsync(
        string requestId,
        ReportingGovernanceRestatementApprovalRequestDto request,
        HttpContext context,
        JsonSerializerOptions jsonOptions)
    {
        var validation = ValidateExpectedVersion(request.ExpectedVersion);
        return validation is null
            ? ExecuteGovernanceAsync(
                context,
                requestId,
                (coordinator, caller, id, ct) =>
                    coordinator.ApproveRestatementAsync(id, request.ExpectedVersion, caller, ct),
                ReportingGovernanceApiProjector.ProjectRestatementApproval,
                StatusCodes.Status200OK,
                jsonOptions)
            : Task.FromResult(validation);
    }

    private static async Task<IResult> ExecuteGovernanceAsync<TDomain, TResponse>(
        HttpContext context,
        string resourceId,
        Func<IReportingGovernanceEndpointCoordinator,
            ReportingGovernanceCallerContext,
            string,
            CancellationToken,
            Task<TDomain>> operation,
        Func<TDomain, ReportingGovernanceCallerContext, TResponse> project,
        int successStatusCode,
        JsonSerializerOptions jsonOptions)
    {
        if (!TryResolveReportingGovernanceCaller(context, out var caller, out var callerError))
        {
            return callerError!;
        }

        var idValidation = ValidateRequiredText(resourceId, "resourceId", 256);
        if (idValidation is not null)
        {
            return idValidation;
        }

        var coordinator = context.RequestServices.GetService<IReportingGovernanceEndpointCoordinator>();
        if (coordinator is null)
        {
            return Results.Problem(
                "Governed reporting lifecycle service is unavailable.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        try
        {
            var result = await operation(
                    coordinator,
                    caller!,
                    resourceId.Trim(),
                    context.RequestAborted)
                .ConfigureAwait(false);
            return Results.Json(project(result, caller!), jsonOptions, statusCode: successStatusCode);
        }
        catch (ReportingGovernancePersistenceException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (ReportingGovernanceAuthorizationException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status403Forbidden);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status403Forbidden);
        }
        catch (ReportingGovernanceNotFoundException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status404NotFound);
        }
        catch (KeyNotFoundException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status404NotFound);
        }
        catch (ReportingGovernanceConcurrencyException ex)
        {
            // The exception carries no structured version data, so only the canonical type/title
            // and detail apply; version extensions join once the exception itself grows them.
            return ApiProblemDetails.VersionConflict(context, ex.Message);
        }
        catch (ReportingGovernanceException ex)
        {
            return ApiProblemDetails.Conflict(context, ex.Message);
        }
        catch (ArgumentException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
        catch (IOException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (NotSupportedException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    private static bool TryResolveReportingGovernanceCaller(
        HttpContext context,
        out ReportingGovernanceCallerContext? caller,
        out IResult? error)
    {
        if (!EndpointAuthorization.TryResolveActor(context, out var actor)
            || !EndpointAuthorization.TryGetPermissions(context, out var permissions))
        {
            caller = null;
            error = Results.Unauthorized();
            return false;
        }

        var tenant = HttpContextWorkstationTenantContextAccessor.Resolve(context);
        if (!tenant.HasTenantScope || string.IsNullOrWhiteSpace(tenant.CompanyId))
        {
            caller = null;
            error = Results.Problem(
                "A tenant and company scoped authenticated session is required for governed reporting.",
                statusCode: StatusCodes.Status403Forbidden);
            return false;
        }

        var principals = EndpointAuthorization.ResolveReportGroupPrincipalIds(context)
            .Where(static principal => !string.IsNullOrWhiteSpace(principal))
            .Select(static principal => principal.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToImmutableArray();
        var correlationId = string.IsNullOrWhiteSpace(context.TraceIdentifier)
            ? Guid.NewGuid().ToString("N")
            : context.TraceIdentifier;

        caller = new ReportingGovernanceCallerContext(
            actor.Trim(),
            tenant.TenantId!.Trim(),
            tenant.CompanyId.Trim(),
            permissions,
            ReportingCommandOrigin.HumanOperator,
            correlationId,
            principals);
        error = null;
        return true;
    }

    private static IResult? ValidateExpectedVersion(long expectedVersion) =>
        expectedVersion > 0
            ? null
            : Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["expectedVersion"] = ["A positive expectedVersion is required."]
            });

    private static IResult? ValidateRequiredText(string? value, string fieldName, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [fieldName] = [$"{fieldName} is required."]
            });
        }

        return value.Trim().Length <= maxLength
            ? null
            : Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [fieldName] = [$"{fieldName} cannot exceed {maxLength} characters."]
            });
    }
}
