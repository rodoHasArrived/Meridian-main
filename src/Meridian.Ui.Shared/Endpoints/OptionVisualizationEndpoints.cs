using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.Contracts.Options;
using Meridian.Identity.Auth;
using Meridian.PortfolioRecords.Accounts;
using Meridian.Ui.Shared.Evidence;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>General option screening and evidence-backed historical broker assessment imports.</summary>
public static class OptionVisualizationEndpoints
{
    public const string ScreenRoute = UiApiRoutes.OptionsScreen;
    public const string AssessmentsRoute = UiApiRoutes.OptionAssessments;

    public static IServiceCollection AddOptionVisualizationServices(this IServiceCollection services, string? dataRoot = null)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddTransient<OptionScreeningService>();
        services.TryAddSingleton(provider => new OptionAssessmentImportService(dataRoot ?? provider.GetRequiredService<Meridian.Storage.StorageOptions>().RootPath,
            provider.GetRequiredService<IEvidenceArtifactStore>(), provider.GetRequiredService<TimeProvider>()));
        return services;
    }

    public static void MapOptionVisualizationEndpoints(this WebApplication app, JsonSerializerOptions json)
    {
        app.MapPost(ScreenRoute, async (OptionScreenRequest request, HttpContext context, CancellationToken ct) =>
        {
            try
            {
                var service = context.RequestServices.GetService<OptionScreeningService>();
                if (service is null)
                    return Results.Problem("Options screening is unavailable.", statusCode: 503);
                return Results.Json(await service.ScreenAsync(request, ct).ConfigureAwait(false), json);
            }
            catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
            catch (InvalidOperationException ex) { return Results.Problem(ex.Message, statusCode: 503); }
        }).WithName("ScreenOptions").WithTags("Options")
            .DeclareNonMutating("Fetches bounded option snapshots and evaluates neutral criteria; creates no strategy, order, reservation or retained assessment.")
            .RequirePermission(UserPermission.ViewMarketData);

        app.MapGet(AssessmentsRoute, async (Guid accountId, HttpContext context, CancellationToken ct) =>
        {
            if (!await Authorized(context, accountId, UserPermission.ViewOptionAssessments, ct).ConfigureAwait(false))
                return EndpointHelpers.Forbidden(context);
            return await Guard(async () =>
            {
                var scope = HttpContextWorkstationTenantContextAccessor.Resolve(context);
                var rows = await context.RequestServices.GetRequiredService<OptionAssessmentImportService>()
                    .ListAsync(scope.TenantId!, scope.CompanyId!, accountId, ct).ConfigureAwait(false);
                return Results.Json(rows, json);
            }).ConfigureAwait(false);
        }).WithName("ListBrokerOptionAssessments").WithTags("Options")
            .RequirePermission(UserPermission.ViewOptionAssessments).RequireWorkstationTenantCompanyScope();

        app.MapPost(UiApiRoutes.OptionAssessmentPreview, async (Guid accountId, OptionAssessmentImportRequest request,
            HttpContext context, CancellationToken ct) =>
        {
            if (!await Authorized(context, accountId, UserPermission.ManageOptionAssessments, ct).ConfigureAwait(false))
                return EndpointHelpers.Forbidden(context);
            return await Guard(async () =>
            {
                await CheckAccount(context, accountId, request.Assessment, ct).ConfigureAwait(false);
                var preview = context.RequestServices.GetRequiredService<OptionAssessmentImportService>().Preview(request);
                return Results.Json(preview, json);
            }).ConfigureAwait(false);
        }).WithName("PreviewBrokerOptionAssessmentImport").WithTags("Options")
            .DeclareNonMutating("Validates canonical broker-assessment inputs and source digest without retaining evidence, changing accounts, reserving capacity or submitting orders.")
            .RequireAllPermissions(UserPermission.ManageOptionAssessments, UserPermission.ViewOptionAssessments)
            .RequireWorkstationTenantCompanyScope();

        app.MapPost(UiApiRoutes.OptionAssessmentAccept, async (Guid accountId, AcceptOptionAssessmentImportRequest command,
            HttpContext context, CancellationToken ct) =>
        {
            if (!await Authorized(context, accountId, UserPermission.ManageOptionAssessments, ct).ConfigureAwait(false))
                return EndpointHelpers.Forbidden(context);
            return await Guard(async () =>
            {
                await CheckAccount(context, accountId, command.Import.Assessment, ct).ConfigureAwait(false);
                var scope = HttpContextWorkstationTenantContextAccessor.Resolve(context);
                if (string.IsNullOrWhiteSpace(scope.Actor))
                    return EndpointHelpers.Forbidden(context);
                var result = await context.RequestServices.GetRequiredService<OptionAssessmentImportService>()
                    .AcceptAsync(scope.TenantId!, scope.CompanyId!, scope.Actor, accountId, command, ct).ConfigureAwait(false);
                return Results.Json(result, json, statusCode: StatusCodes.Status201Created);
            }).ConfigureAwait(false);
        }).WithName("AcceptBrokerOptionAssessmentImport").WithTags("Options")
            .RequireAllPermissions(UserPermission.ManageOptionAssessments, UserPermission.ViewOptionAssessments)
            .RequireWorkstationTenantCompanyScope().RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        app.MapGet(UiApiRoutes.OptionAssessmentSource, async (Guid accountId, string assessmentId,
            HttpContext context, CancellationToken ct) =>
        {
            if (!await Authorized(context, accountId, UserPermission.ViewOptionAssessments, ct).ConfigureAwait(false))
                return EndpointHelpers.Forbidden(context);
            return await Guard(async () =>
            {
                var scope = HttpContextWorkstationTenantContextAccessor.Resolve(context);
                var records = await context.RequestServices.GetRequiredService<OptionAssessmentImportService>()
                    .ListAsync(scope.TenantId!, scope.CompanyId!, accountId, ct).ConfigureAwait(false);
                var record = records.SingleOrDefault(row => row.AssessmentId == assessmentId);
                if (record is null)
                    return Results.NotFound();
                var source = await context.RequestServices.GetRequiredService<IEvidenceArtifactStore>()
                    .TryOpenDocumentContentAsync(record.SourceVaultId, record.SourceDocumentId, scope.TenantId!, scope.CompanyId!, ct).ConfigureAwait(false);
                return source is null ? Results.NotFound() : Results.Stream(source.Content, source.ContentType, source.FileName);
            }).ConfigureAwait(false);
        }).WithName("DownloadBrokerOptionAssessmentSource").WithTags("Options")
            .RequirePermission(UserPermission.ViewOptionAssessments).RequireWorkstationTenantCompanyScope();
    }

    private static async Task<bool> Authorized(HttpContext context, Guid account, UserPermission permission, CancellationToken ct)
    {
        if (account == Guid.Empty || !(await EndpointAuthorization.AuthorizeScopedAsync(context, permission,
            AccessScopeKindDto.Account, account, ct).ConfigureAwait(false)).IsAllowed)
            return false;
        // A permission grant (including a global administrative override) cannot invent an account.
        // The registered query service applies the host's tenant guard before reading authoritative records.
        var query = context.RequestServices.GetService<IAccountQueryService>();
        return query is not null && await query.GetAccountAsync(account, ct).ConfigureAwait(false) is { AccountId: var found } && found == account;
    }

    private static async Task CheckAccount(HttpContext context, Guid accountId, BrokerOptionAssessment? assessment, CancellationToken ct)
    {
        if (assessment is null || assessment.FundAccountId != accountId)
            throw new ArgumentException("Assessment account does not match the selected owned account.");
        // An existing retained brokerage link is authoritative. Never relink it from imported claim text.
        var sync = context.RequestServices.GetService<BrokeragePortfolioSyncService>();
        if (sync is null)
            return;
        var status = await sync.GetStatusAsync(accountId, ct).ConfigureAwait(false);
        if (status.IsLinked && (!string.Equals(status.ProviderId, assessment.ProviderId?.Trim(), StringComparison.OrdinalIgnoreCase)
            || !string.Equals(status.ExternalAccountId, assessment.ExternalAccountId?.Trim(), StringComparison.Ordinal)))
            throw new ArgumentException("Broker identity does not match this fund account's retained brokerage link.");
    }

    private static async Task<IResult> Guard(Func<Task<IResult>> action)
    {
        try
        { return await action().ConfigureAwait(false); }
        catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
        catch (InvalidOperationException ex) { return Results.Conflict(new { error = ex.Message }); }
        catch (JsonException) { return Results.Problem("Retained assessment history is corrupt; no empty history is substituted.", statusCode: 503); }
        catch (IOException) { return Results.Problem("Retained assessment evidence is unavailable or failed verification. Refresh after source recovery.", statusCode: 503); }
    }
}
