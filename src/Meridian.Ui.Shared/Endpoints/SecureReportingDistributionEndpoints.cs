using System.Collections.Immutable;
using System.Security.Cryptography;
using Meridian.Identity.Auth;
using Meridian.Reporting;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Meridian.Ui.Shared.Endpoints;

public static class SecureReportingDistributionRoutes
{
    public const string QueueDelivery = "/api/fund-structure/reporting/distribution/deliveries";
    public const string GetDelivery = "/api/fund-structure/reporting/distribution/deliveries/{jobId}";
    public const string ListDeliveries = "/api/fund-structure/reporting/distribution/packages/{runId}/deliveries";
    public const string ListTransports = "/api/fund-structure/reporting/distribution/transports";
    public const string RecordProviderReceipt = "/hooks/reporting/distribution/{transportId}/deliveries/{jobId}/receipts";
    public const string IssueGrant = "/api/fund-structure/reporting/distribution/access-grants";
    public const string GetGrant = "/api/fund-structure/reporting/distribution/access-grants/{grantId}";
    public const string ListGrants = "/api/fund-structure/reporting/distribution/packages/{runId}/access-grants";
    public const string RevokeGrant = "/api/fund-structure/reporting/distribution/access-grants/{grantId}/revoke";
    public const string DownloadArtifact = "/api/fund-structure/reporting/distribution/packages/{runId}/artifacts/{artifactId}";
    public const string PortalPackage = "/portal/reporting/secure/packages/{runId}";
    public const string ExchangeGrant = "/portal/reporting/access-grants/{grantId}/exchange";
}

public sealed record SecureReportingDeliveryReceiptResponse(
    string ReceiptId,
    string Kind,
    DateTimeOffset OccurredAtUtc,
    string? ProviderReference,
    string? EvidenceReference,
    string? Detail);

public sealed record SecureReportingDeliveryResponse(
    string JobId,
    string RunId,
    string PackageId,
    string ReleaseVersion,
    string ArtifactManifestHashSha256,
    string DistributionId,
    string TransportId,
    string Recipient,
    string Destination,
    string Subject,
    string State,
    int AttemptCount,
    int MaxAttempts,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? NextAttemptAtUtc,
    string? LastErrorCode,
    string? LastError,
    string? ProviderMessageId,
    string? AccessGrantId,
    IReadOnlyList<SecureReportingDeliveryReceiptResponse> Receipts,
    string RecipientKind = nameof(ReportingAccessPrincipalKind.User));

public sealed record SecureReportingGrantResponse(
    string GrantId,
    string RecipientAccessUri,
    DateTimeOffset ExpiresAtUtc,
    string Audience,
    string RunId,
    string PackageId,
    IReadOnlyList<string> ArtifactIds,
    ReportingAccessPrincipalKind AudienceKind = ReportingAccessPrincipalKind.User);

public sealed record SecureReportingGrantRevocationRequest(string Reason);

public sealed record SecureReportingGrantRevocationResponse(string GrantId, bool Revoked);

public sealed record SecureReportingAccessGrantSummaryResponse(
    string GrantId,
    string RunId,
    string PackageId,
    string Audience,
    bool AllowPackageRead,
    IReadOnlyList<string> ArtifactIds,
    string State,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    int MaxUses,
    int UseCount,
    DateTimeOffset? LastUsedAtUtc,
    DateTimeOffset? RevokedAtUtc,
    string? RevokedBy,
    string? RevocationReason,
    ReportingAccessPrincipalKind AudienceKind = ReportingAccessPrincipalKind.User);

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

        group.MapGet("/deliveries/{jobId}", async (
                string jobId,
                ReportingSecureDistributionApplicationService service,
                HttpContext context,
                CancellationToken ct) =>
            await ExecuteAsync(async () =>
            {
                var job = await service
                    .GetDeliveryAsync(jobId, BuildAuthority(context), ct)
                    .ConfigureAwait(false);
                return Results.Ok(Project(job));
            }).ConfigureAwait(false))
            .RequirePermission(UserPermission.ViewReporting);

        group.MapGet("/packages/{runId}/deliveries", async (
                string runId,
                ReportingSecureDistributionApplicationService service,
                HttpContext context,
                CancellationToken ct) =>
            await ExecuteAsync(async () =>
            {
                var jobs = await service
                    .ListDeliveriesAsync(runId, BuildAuthority(context), ct)
                    .ConfigureAwait(false);
                return Results.Ok(jobs.Select(Project).ToArray());
            }).ConfigureAwait(false))
            .RequirePermission(UserPermission.ViewReporting);

        group.MapGet("/transports", (
                ReportingSecureDistributionApplicationService service,
                HttpContext context) =>
            Results.Ok(service.GetDistributionCapabilities(BuildAuthority(context))))
            .RequirePermission(UserPermission.ViewReporting);

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
                    grant.RecipientAccessUri,
                    grant.ExpiresAtUtc,
                    grant.Audience,
                    grant.RunId,
                    grant.PackageId,
                    grant.ArtifactIds,
                    grant.AudienceKind));
            }).ConfigureAwait(false))
            .RequirePermission(UserPermission.DeliverReporting);

        group.MapGet("/access-grants/{grantId}", async (
                string grantId,
                ReportingSecureDistributionApplicationService service,
                HttpContext context,
                CancellationToken ct) =>
            await ExecuteAsync(async () =>
            {
                var grant = await service
                    .GetAccessGrantAsync(grantId, BuildAuthority(context), ct)
                    .ConfigureAwait(false);
                return Results.Ok(Project(grant));
            }).ConfigureAwait(false))
            .RequirePermission(UserPermission.DeliverReporting);

        group.MapGet("/packages/{runId}/access-grants", async (
                string runId,
                ReportingSecureDistributionApplicationService service,
                HttpContext context,
                CancellationToken ct) =>
            await ExecuteAsync(async () =>
            {
                var grants = await service
                    .ListAccessGrantsAsync(runId, BuildAuthority(context), ct)
                    .ConfigureAwait(false);
                return Results.Ok(grants.Select(Project).ToArray());
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

        app.MapGet("/portal/reporting/secure/packages/{runId}", async (
                string runId,
                ReportingSecureDistributionApplicationService service,
                HttpContext context,
                CancellationToken ct) =>
            await ExecuteAsync(async () =>
            {
                await service
                    .AuthorizePortalPackageAsync(runId, BuildAuthority(context), ct)
                    .ConfigureAwait(false);
                SetNoStore(context.Response);
                return Results.Redirect(
                    $"/workstation/reporting/runs/detail?runId={Uri.EscapeDataString(runId)}",
                    permanent: false,
                    preserveMethod: false);
            }).ConfigureAwait(false))
            .RequireWorkstationTenantScope()
            .RequirePermission(UserPermission.ViewReporting);

        app.MapPost("/hooks/reporting/distribution/{transportId}/deliveries/{jobId}/receipts", async (
                string transportId,
                string jobId,
                SecureReportingDeliveryReceiptCommand command,
                ReportingSecureDistributionApplicationService service,
                HttpContext context,
                CancellationToken ct) =>
            await ExecuteAsync(async () =>
            {
                await service.RecordVerifiedProviderReceiptAsync(
                        transportId,
                        jobId,
                        command,
                        new ReportingProviderReceiptAuthentication(
                            context.Request.Headers["X-Meridian-Reporting-Timestamp"].ToString(),
                            context.Request.Headers["X-Meridian-Reporting-Signature"].ToString()),
                        ct)
                    .ConfigureAwait(false);
                return Results.Accepted();
            }).ConfigureAwait(false))
            .AddEndpointFilter(RejectQueryBearerAsync);

        app.MapGet("/portal/reporting/access-grants/{grantId}/exchange", (
                string grantId,
                HttpContext context) =>
            ExecuteLandingPage(grantId, context))
            .AddEndpointFilter(RejectQueryBearerAsync);

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
            job.ReleaseAuthorization.RunId,
            job.PackageId,
            job.ReleaseAuthorization.ReleaseVersion,
            job.ReleaseAuthorization.ArtifactManifestHashSha256,
            job.DistributionId,
            job.TransportId,
            job.Payload.Recipient,
            job.Payload.Destination,
            job.Payload.Subject,
            job.State.ToString(),
            job.AttemptCount,
            job.MaxAttempts,
            job.CreatedAtUtc,
            job.UpdatedAtUtc,
            job.NextAttemptAtUtc,
            job.LastErrorCode,
            job.LastError,
            job.ProviderMessageId,
            job.AccessGrantId,
            job.Receipts.Select(static receipt => new SecureReportingDeliveryReceiptResponse(
                receipt.ReceiptId,
                receipt.Kind.ToString(),
                receipt.OccurredAtUtc,
                receipt.ProviderReference,
                receipt.EvidenceReference,
                receipt.Detail)).ToArray(),
            job.Payload.RecipientKind.ToString());

    private static SecureReportingAccessGrantSummaryResponse Project(
        SecureReportingAccessGrantSummary grant) =>
        new(
            grant.GrantId,
            grant.RunId,
            grant.PackageId,
            grant.Audience,
            grant.AllowPackageRead,
            grant.ArtifactIds,
            grant.State,
            grant.CreatedAtUtc,
            grant.ExpiresAtUtc,
            grant.MaxUses,
            grant.UseCount,
            grant.LastUsedAtUtc,
            grant.RevokedAtUtc,
            grant.RevokedBy,
            grant.RevocationReason,
            grant.AudienceKind);

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

        if (context.HttpContext.Request.Headers.ContainsKey("Authorization"))
        {
            return ValueTask.FromResult<object?>(Results.Problem(
                title: "Authorization headers are not accepted on this endpoint",
                detail: "Submit the provider signature headers or opaque grant bearer in the defined request fields only.",
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

    private static IResult ExecuteLandingPage(string grantId, HttpContext context)
    {
        if (string.IsNullOrWhiteSpace(grantId) || grantId.Length > 256)
        {
            return Results.NotFound();
        }

        var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(18));
        SetNoStore(context.Response);
        context.Response.Headers.ContentSecurityPolicy =
            $"default-src 'none'; script-src 'nonce-{nonce}'; style-src 'nonce-{nonce}'; connect-src 'self'; img-src 'none'; font-src 'none'; object-src 'none'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'";
        context.Response.Headers.XContentTypeOptions = "nosniff";
        context.Response.Headers.XFrameOptions = "DENY";
        context.Response.Headers["Cross-Origin-Opener-Policy"] = "same-origin";
        context.Response.Headers["Cross-Origin-Resource-Policy"] = "same-origin";
        context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
        var html = $$"""
            <!doctype html>
            <html lang="en">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width,initial-scale=1">
              <title>Meridian secure report</title>
              <style nonce="{{nonce}}">
                body{font-family:system-ui,sans-serif;max-width:42rem;margin:4rem auto;padding:0 1.5rem;color:#17202a}
                form{display:grid;gap:.75rem} input,button{font:inherit;padding:.7rem} #status{min-height:1.5rem}
              </style>
            </head>
            <body>
              <main>
                <h1>Secure report access</h1>
                <p id="status">Preparing your one-time report access…</p>
                <form id="download-form" hidden autocomplete="off">
                  <label for="artifact">Artifact identifier</label>
                  <input id="artifact" name="artifact" required maxlength="256" spellcheck="false">
                  <button type="submit">Download verified report</button>
                </form>
              </main>
              <script nonce="{{nonce}}">
                (() => {
                  const status = document.getElementById('status');
                  const form = document.getElementById('download-form');
                  const artifact = document.getElementById('artifact');
                  const fragment = new URLSearchParams(location.hash.slice(1));
                  let bearerToken = fragment.get('token') || '';
                  artifact.value = fragment.get('artifact') || '';
                  history.replaceState(null, document.title, location.pathname);
                  if (!bearerToken) {
                    status.textContent = 'This access link is missing its one-time credential.';
                    return;
                  }
                  status.textContent = artifact.value
                    ? 'Your report is ready to download.'
                    : 'Enter the artifact identifier supplied with your report notice.';
                  form.hidden = false;
                  form.addEventListener('submit', async event => {
                    event.preventDefault();
                    form.hidden = true;
                    status.textContent = 'Verifying retained report bytes…';
                    const body = JSON.stringify({ bearerToken, artifactId: artifact.value.trim() });
                    bearerToken = '';
                    const response = await fetch(location.pathname, {
                      method: 'POST',
                      credentials: 'omit',
                      cache: 'no-store',
                      referrerPolicy: 'no-referrer',
                      headers: { 'Content-Type': 'application/json' },
                      body
                    });
                    if (!response.ok) {
                      status.textContent = 'Access is unavailable. The link may be expired, revoked, or already used.';
                      return;
                    }
                    const blob = await response.blob();
                    const objectUrl = URL.createObjectURL(blob);
                    const anchor = document.createElement('a');
                    anchor.href = objectUrl;
                    anchor.download = artifact.value.trim() || 'meridian-report-artifact';
                    anchor.rel = 'noreferrer';
                    anchor.click();
                    URL.revokeObjectURL(objectUrl);
                    status.textContent = 'The verified report download has started.';
                  });
                })();
              </script>
            </body>
            </html>
            """;
        return Results.Content(html, "text/html; charset=utf-8");
    }
}
