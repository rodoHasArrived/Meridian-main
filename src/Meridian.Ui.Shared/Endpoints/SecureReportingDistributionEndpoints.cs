using System.Collections.Immutable;
using Meridian.Identity.Auth;
using Meridian.Reporting;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Meridian.Ui.Shared.Endpoints;

public static class SecureReportingDistributionRoutes
{
    public const string QueueDelivery = "/api/fund-structure/reporting/distribution/deliveries";
    public const string ProcessDue = "/api/fund-structure/reporting/distribution/deliveries/process-due";
    public const string RecordReceipt = "/api/fund-structure/reporting/distribution/deliveries/{jobId}/receipts";
    public const string IssueGrant = "/api/fund-structure/reporting/distribution/access-grants";
    public const string RevokeGrant = "/api/fund-structure/reporting/distribution/access-grants/{grantId}/revoke";
    public const string DownloadArtifact = "/api/fund-structure/reporting/distribution/packages/{runId}/artifacts/{artifactId}";
    public const string ExchangeGrant = "/portal/reporting/access-grants/{grantId}/exchange";
}

public sealed record SecureReportingDeliveryReceiptResponse(
    string ReceiptId,
    string Kind,
    DateTimeOffset OccurredAtUtc,
    string? ProviderReference,
    string? EvidenceReference);

public sealed record SecureReportingDeliveryResponse(
    string JobId,
    string PackageId,
    string DistributionId,
    string TransportId,
    string State,
    int AttemptCount,
    int MaxAttempts,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? NextAttemptAtUtc,
    string? LastErrorCode,
    string? ProviderMessageId,
    string? AccessGrantId,
    IReadOnlyList<SecureReportingDeliveryReceiptResponse> Receipts);

public sealed record SecureReportingGrantResponse(
    string GrantId,
    string BearerToken,
    string ExchangePath,
    DateTimeOffset ExpiresAtUtc,
    string Audience,
    string PackageId,
    IReadOnlyList<string> ArtifactIds);

public sealed record SecureReportingGrantRevocationRequest(string Reason);

public sealed record SecureReportingGrantRevocationResponse(string GrantId, bool Revoked);

/// <summary>
/// Canonical HTTP surface for release-gated reporting distribution. Add one call to
/// <c>app.MapSecureReportingDistributionEndpoints()</c> from the shared endpoint coordinator; all
/// route and permission policy remains in this partial feature file.
/// </summary>
public static class SecureReportingDistributionEndpoints
{
    public static void MapSecureReportingDistributionEndpoints(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup("/api/fund-structure/reporting/distribution")
            .RequireWorkstationTenantScope();

        group.MapPost("/deliveries", async (
                SecureReportingDeliveryQueueCommand command,
                ReportingSecureDistributionApplicationService service,
                HttpContext context,
                CancellationToken ct) =>
            await ExecuteAsync(async () =>
            {
                var job = await service
                    .QueueDeliveryAsync(command, BuildAuthority(context), ct)
                    .ConfigureAwait(false);
                return Results.Ok(Project(job));
            }).ConfigureAwait(false))
            .RequirePermission(UserPermission.DeliverReporting);

        group.MapPost("/deliveries/process-due", async (
                ReportingSecureDistributionApplicationService service,
                HttpContext context,
                CancellationToken ct) =>
            await ExecuteAsync(async () =>
            {
                var jobs = await service
                    .ProcessDueAsync(BuildAuthority(context), ct)
                    .ConfigureAwait(false);
                return Results.Ok(jobs.Select(Project).ToArray());
            }).ConfigureAwait(false))
            .RequirePermission(UserPermission.AdminMaintenance);

        group.MapPost("/deliveries/{jobId}/receipts", async (
                string jobId,
                SecureReportingDeliveryReceiptCommand command,
                ReportingSecureDistributionApplicationService service,
                HttpContext context,
                CancellationToken ct) =>
            await ExecuteAsync(async () =>
            {
                var job = await service
                    .RecordProviderReceiptAsync(jobId, command, BuildAuthority(context), ct)
                    .ConfigureAwait(false);
                return Results.Ok(Project(job));
            }).ConfigureAwait(false))
            .RequirePermission(UserPermission.DeliverReporting);

        group.MapPost("/access-grants", async (
                SecureReportingGrantIssueCommand command,
                ReportingSecureDistributionApplicationService service,
                HttpContext context,
                CancellationToken ct) =>
            await ExecuteAsync(async () =>
            {
                var grant = await service
                    .IssueAccessGrantAsync(command, BuildAuthority(context), ct)
                    .ConfigureAwait(false);
                SetNoStore(context.Response);
                return Results.Ok(new SecureReportingGrantResponse(
                    grant.GrantId,
                    grant.BearerToken,
                    grant.ExchangePath,
                    grant.ExpiresAtUtc,
                    grant.Audience,
                    grant.PackageId,
                    grant.ArtifactIds));
            }).ConfigureAwait(false))
            .RequirePermission(UserPermission.DeliverReporting);

        group.MapPost("/access-grants/{grantId}/revoke", async (
                string grantId,
                SecureReportingGrantRevocationRequest request,
                ReportingSecureDistributionApplicationService service,
                HttpContext context,
                CancellationToken ct) =>
            await ExecuteAsync(async () =>
            {
                var revoked = await service
                    .RevokeAccessGrantAsync(grantId, request.Reason, BuildAuthority(context), ct)
                    .ConfigureAwait(false);
                return revoked
                    ? Results.Ok(new SecureReportingGrantRevocationResponse(grantId, Revoked: true))
                    : Results.NotFound();
            }).ConfigureAwait(false))
            .RequirePermission(UserPermission.DeliverReporting);

        group.MapGet("/packages/{runId}/artifacts/{artifactId}", async (
                string runId,
                string artifactId,
                ReportingSecureDistributionApplicationService service,
                HttpContext context,
                CancellationToken ct) =>
            await ExecuteAsync(async () =>
            {
                var download = await service
                    .DownloadArtifactAsync(runId, artifactId, BuildAuthority(context), ct)
                    .ConfigureAwait(false);
                SetNoStore(context.Response);
                return Results.File(
                    download.Content,
                    download.Artifact.ContentType,
                    download.Artifact.FileName,
                    enableRangeProcessing: false);
            }).ConfigureAwait(false))
            .RequirePermission(UserPermission.ViewReporting);

        app.MapPost("/portal/reporting/access-grants/{grantId}/exchange", async (
                string grantId,
                SecureReportingGrantExchangeCommand command,
                ReportingSecureDistributionApplicationService service,
                HttpContext context,
                CancellationToken ct) =>
            await ExecuteAsync(async () =>
            {
                var download = await service
                    .ExchangeGrantForDownloadAsync(grantId, command, context.TraceIdentifier, ct)
                    .ConfigureAwait(false);
                SetNoStore(context.Response);
                return Results.File(
                    download.Content,
                    download.Artifact.ContentType,
                    download.Artifact.FileName,
                    enableRangeProcessing: false);
            }).ConfigureAwait(false))
            .AddEndpointFilter(RejectQueryBearerAsync);
    }

    private static ReportingDistributionAuthority BuildAuthority(HttpContext context)
    {
        var tenant = HttpContextWorkstationTenantContextAccessor.Resolve(context);
        var actor = tenant.Actor?.Trim();
        var tenantId = tenant.TenantId?.Trim();
        if (string.IsNullOrWhiteSpace(actor) || string.IsNullOrWhiteSpace(tenantId))
        {
            throw new UnauthorizedAccessException(
                "A server-resolved authenticated actor and tenant are required.");
        }

        var principals = EndpointAuthorization.ResolveReportGroupPrincipalIds(context)
            .Append(actor)
            .Distinct(StringComparer.Ordinal)
            .ToImmutableArray();
        return new ReportingDistributionAuthority(
            actor,
            tenantId,
            tenant.CompanyId,
            principals,
            CanView: EndpointAuthorization.HasAnyPermission(
                context,
                UserPermission.ViewReporting,
                UserPermission.DeliverReporting,
                UserPermission.AdminMaintenance),
            CanDeliver: EndpointAuthorization.HasPermission(context, UserPermission.DeliverReporting),
            CanAdminister: EndpointAuthorization.HasPermission(context, UserPermission.AdminMaintenance),
            CorrelationId: context.TraceIdentifier);
    }

    private static SecureReportingDeliveryResponse Project(ReportingDeliveryJobRecord job) =>
        new(
            job.JobId,
            job.PackageId,
            job.DistributionId,
            job.TransportId,
            job.State.ToString(),
            job.AttemptCount,
            job.MaxAttempts,
            job.CreatedAtUtc,
            job.UpdatedAtUtc,
            job.NextAttemptAtUtc,
            job.LastErrorCode,
            job.ProviderMessageId,
            job.AccessGrantId,
            job.Receipts.Select(static receipt => new SecureReportingDeliveryReceiptResponse(
                receipt.ReceiptId,
                receipt.Kind.ToString(),
                receipt.OccurredAtUtc,
                receipt.ProviderReference,
                receipt.EvidenceReference)).ToArray());

    private static async Task<IResult> ExecuteAsync(Func<Task<IResult>> action)
    {
        try
        {
            return await action().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (SecureReportingAccessGrantDeniedException)
        {
            return Results.NotFound();
        }
        catch (ReportingArtifactVaultAccessDeniedException)
        {
            return Results.NotFound();
        }
        catch (UnauthorizedAccessException)
        {
            return Results.NotFound();
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound();
        }
        catch (ArgumentException exception)
        {
            return Results.Problem(
                title: "Invalid secure reporting distribution request",
                detail: exception.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }
        catch (InvalidOperationException exception)
        {
            return Results.Problem(
                title: "Reporting distribution is not ready",
                detail: exception.Message,
                statusCode: StatusCodes.Status409Conflict);
        }
        catch (Exception exception) when (
            exception is ReportingArtifactCatalogIntegrityException
                or ReportingArtifactIntegrityException
                or InvalidDataException
                or IOException)
        {
            return Results.Problem(
                title: "Reporting distribution evidence failed integrity verification",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    private static ValueTask<object?> RejectQueryBearerAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        if (context.HttpContext.Request.QueryString.HasValue)
        {
            return ValueTask.FromResult<object?>(Results.Problem(
                title: "Grant exchange query parameters are not accepted",
                detail: "Submit the opaque bearer only in the POST body.",
                statusCode: StatusCodes.Status400BadRequest));
        }

        return next(context);
    }

    private static void SetNoStore(HttpResponse response)
    {
        response.Headers.CacheControl = "no-store, private";
        response.Headers.Pragma = "no-cache";
        response.Headers.Expires = "0";
        response.Headers["Referrer-Policy"] = "no-referrer";
    }
}
