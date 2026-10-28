using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.Api;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Tenancy;
using Meridian.FinancialOperations.Reconciliation;
using Meridian.Identity.Auth;
using Meridian.Storage.Ledger;
using Meridian.Storage.AssetOperations;
using Meridian.Ui.Shared.Evidence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

public sealed record AssignAccountCashCaseRequest(int ExpectedVersion, string Owner, string Reason);
public sealed record AccountOversightContextDto(string AccountId, string BookId, string BaseCurrency,
    string BookName, string Actor, bool CanWrite, bool CanReadPrices, bool CanManagePrices, bool CanProvision);

[JsonSerializable(typeof(AssignAccountCashCaseRequest))]
[JsonSerializable(typeof(AccountOversightContextDto))]
internal sealed partial class AccountOversightJsonContext : JsonSerializerContext;

/// <summary>Shared account oversight over exact, already-owned account ledger books.</summary>
public static partial class LedgerEndpoints
{
    private static void MapAccountOversightEndpoints(WebApplication app, JsonSerializerOptions jsonOptions)
    {
        // HTTP amounts remain exact in the browser; retained domain evidence keeps its original serialization.
        jsonOptions = new JsonSerializerOptions(jsonOptions);
        jsonOptions.Converters.Insert(0, new AccountOversightDecimalJsonConverter());

        OversightRead(app.MapGet(UiApiRoutes.LedgerAccountOversightContext, async (
            Guid ledgerBookId, Guid accountId, HttpContext context) =>
            await ExecuteAccountOversightAsync(context, ledgerBookId, accountId, false, scope =>
            {
                var canWrite = HasLedgerMutationPermission(context);
                var canManagePrices = EndpointAuthorization.HasPermission(context, UserPermission.ModifySecurityMaster);
                return Task.FromResult<IResult>(Results.Json(new AccountOversightContextDto(scope.AccountId, scope.BookId,
                    scope.Book.BaseCurrency, scope.Book.DisplayName, scope.Actor, canWrite,
                    canManagePrices || EndpointAuthorization.HasPermission(context, UserPermission.ViewSecurityMaster),
                    canWrite && canManagePrices,
                    canWrite && EndpointAuthorization.HasPermission(context, UserPermission.AdminMaintenance)), jsonOptions));
            }).ConfigureAwait(false)).WithName("GetLedgerAccountOversightContext"));

        OversightMutation(app.MapPost(UiApiRoutes.LedgerAccountOversightProvision, async (
            Guid ledgerBookId, Guid accountId, HttpContext context) =>
            await ExecuteAccountOversightAsync(context, ledgerBookId, accountId, true, async scope =>
            {
                var cash = context.RequestServices.GetService<FileAccountCashReconciliationStore>();
                var forecast = context.RequestServices.GetService<IAccountCashForecastStore>() as FileAccountCashForecastStore;
                if (cash is null || forecast is null)
                    return OversightUnavailable("Explicit provisioning requires the configured durable account oversight stores.");
                await cash.InitializeAsync(context.RequestAborted).ConfigureAwait(false);
                await forecast.InitializeAsync(context.RequestAborted).ConfigureAwait(false);
                return Results.Ok(new { scope.AccountId, scope.BookId, provisioned = true });
            }).ConfigureAwait(false)).WithName("ProvisionLedgerAccountOversight")
            .RequirePermission(UserPermission.AdminMaintenance));

        OversightMutation(app.MapPost(UiApiRoutes.LedgerAccountCashReconciliationRuns, async (
            Guid ledgerBookId, Guid accountId, AccountCashReconciliationRequest request, HttpContext context) =>
            await ExecuteAccountOversightAsync(context, ledgerBookId, accountId, true, async scope =>
            {
                if (request.Scope is null || request.Scope.AccountId != scope.AccountId
                    || request.Scope.LedgerBookId != ledgerBookId)
                    return OversightScopeDenied();
                if (request.ObservedAtUtc > DateTimeOffset.UtcNow)
                    throw new ArgumentException("Reconciliation observation time cannot be later than the authenticated run time.", nameof(request));
                var service = context.RequestServices.GetService<AccountCashReconciliationService>();
                if (service is null)
                    return OversightUnavailable("Account cash continuity is not configured.");
                return Results.Json(await service.RunAsync(request with { Scope = scope.ReconciliationScope }, context.RequestAborted)
                    .ConfigureAwait(false), jsonOptions);
            }).ConfigureAwait(false)).WithName("RunLedgerAccountCashReconciliation"));

        OversightRead(app.MapGet(UiApiRoutes.LedgerAccountCashReconciliationCases, async (
            Guid ledgerBookId, Guid accountId, HttpContext context) =>
            await ExecuteAccountOversightAsync(context, ledgerBookId, accountId, false, async scope =>
            {
                var service = context.RequestServices.GetService<AccountCashReconciliationService>();
                if (service is null)
                    return OversightUnavailable("Account cash continuity is not configured.");
                return Results.Json(await service.GetCasesAsync(scope.ReconciliationScope, context.RequestAborted)
                    .ConfigureAwait(false), jsonOptions);
            }).ConfigureAwait(false)).WithName("ListLedgerAccountCashReconciliationCases"));

        OversightRead(app.MapGet(UiApiRoutes.LedgerAccountCashReconciliationReplay, async (
            Guid ledgerBookId, Guid accountId, string runId, HttpContext context) =>
            await ExecuteAccountOversightAsync(context, ledgerBookId, accountId, false, async scope =>
            {
                var service = context.RequestServices.GetService<AccountCashReconciliationService>();
                if (service is null)
                    return OversightUnavailable("Account cash continuity is not configured.");
                return Results.Json(await service.ReplayAsync(scope.ReconciliationScope, runId, context.RequestAborted)
                    .ConfigureAwait(false), jsonOptions);
            }).ConfigureAwait(false)).WithName("ReplayLedgerAccountCashReconciliation"));

        OversightMutation(app.MapPost(UiApiRoutes.LedgerAccountCashReconciliationAssignment, async (
            Guid ledgerBookId, Guid accountId, string caseId, AssignAccountCashCaseRequest request, HttpContext context) =>
            await ExecuteAccountOversightAsync(context, ledgerBookId, accountId, true, async scope =>
            {
                var service = context.RequestServices.GetService<AccountCashReconciliationService>();
                if (service is null)
                    return OversightUnavailable("Account cash continuity is not configured.");
                return Results.Json(await service.AssignAsync(scope.ReconciliationScope, caseId, request.ExpectedVersion,
                    request.Owner, scope.Actor, request.Reason, DateTimeOffset.UtcNow, context.RequestAborted)
                    .ConfigureAwait(false), jsonOptions);
            }).ConfigureAwait(false)).WithName("AssignLedgerAccountCashReconciliationCase"));

        OversightMutation(app.MapPost(UiApiRoutes.LedgerAccountClosePriceSets, async (
            Guid ledgerBookId, Guid accountId, DateOnly valuationDate, CreateSecurityClosePriceSetRequest request, HttpContext context) =>
            await ExecuteAccountOversightAsync(context, ledgerBookId, accountId, true, async scope =>
            {
                if (request.AccountId != scope.AccountId || request.BookId != scope.BookId || request.ValuationDate != valuationDate)
                    return OversightScopeDenied();
                var service = ResolveClosePriceService(context);
                if (service is null)
                    return OversightUnavailable("Close-price snapshots are not configured.");
                return Results.Json(await service.CreateClosePriceSetAsync(request with { CreatedBy = scope.Actor }, context.RequestAborted)
                    .ConfigureAwait(false), jsonOptions);
            }).ConfigureAwait(false)).WithName("CreateLedgerAccountClosePriceSet")
            .RequirePermission(UserPermission.ModifySecurityMaster));

        OversightRead(app.MapGet(UiApiRoutes.LedgerAccountClosePriceSets, async (
            Guid ledgerBookId, Guid accountId, DateOnly valuationDate, HttpContext context) =>
            await ExecuteAccountOversightAsync(context, ledgerBookId, accountId, false, async scope =>
            {
                var service = ResolveClosePriceService(context);
                if (service is null)
                    return OversightUnavailable("Close-price snapshots are not configured.");
                return Results.Json(await service.GetClosePriceSetsAsync(scope.AccountId, scope.BookId,
                    valuationDate, context.RequestAborted).ConfigureAwait(false), jsonOptions);
            }).ConfigureAwait(false)).WithName("ListLedgerAccountClosePriceSets")
            .RequireAnyPermission(UserPermission.ViewSecurityMaster, UserPermission.ModifySecurityMaster));

        OversightRead(app.MapGet(UiApiRoutes.LedgerAccountClosePriceSet, async (
            Guid ledgerBookId, Guid accountId, DateOnly valuationDate, Guid snapshotId, HttpContext context) =>
            await ExecuteAccountOversightAsync(context, ledgerBookId, accountId, false, async scope =>
            {
                var service = ResolveClosePriceService(context);
                if (service is null)
                    return OversightUnavailable("Close-price snapshots are not configured.");
                var retained = await service.GetClosePriceSetAsync(scope.AccountId, scope.BookId, valuationDate,
                    snapshotId, context.RequestAborted).ConfigureAwait(false);
                return retained is null ? Results.NotFound() : Results.Json(retained, jsonOptions);
            }).ConfigureAwait(false)).WithName("GetLedgerAccountClosePriceSet")
            .RequireAnyPermission(UserPermission.ViewSecurityMaster, UserPermission.ModifySecurityMaster));

        OversightMutation(app.MapPost(UiApiRoutes.LedgerAccountClosePriceSetApproval, async (
            Guid ledgerBookId, Guid accountId, DateOnly valuationDate, Guid snapshotId,
            ApproveSecurityClosePriceSetRequest request, HttpContext context) =>
            await ExecuteAccountOversightAsync(context, ledgerBookId, accountId, true, async scope =>
            {
                if (request.AccountId != scope.AccountId || request.BookId != scope.BookId
                    || request.ValuationDate != valuationDate || request.SnapshotId != snapshotId)
                    return OversightScopeDenied();
                var service = ResolveClosePriceService(context);
                if (service is null)
                    return OversightUnavailable("Close-price snapshots are not configured.");
                return Results.Json(await service.ApproveClosePriceSetAsync(request with { Reviewer = scope.Actor }, context.RequestAborted)
                    .ConfigureAwait(false), jsonOptions);
            }).ConfigureAwait(false)).WithName("ApproveLedgerAccountClosePriceSet")
            .RequirePermission(UserPermission.ModifySecurityMaster));

        OversightMutation(app.MapPost(UiApiRoutes.LedgerAccountCashForecasts, async (
            Guid ledgerBookId, Guid accountId, AccountCashForecastRequestDto request, HttpContext context) =>
            await ExecuteAccountOversightAsync(context, ledgerBookId, accountId, true, async scope =>
            {
                if (request.Scope is null || request.Scope.AccountId != scope.AccountId
                    || request.Scope.BookId != scope.BookId || request.Scope.BaseCurrency != scope.Book.BaseCurrency)
                    return OversightScopeDenied();
                // Caller-input checks run before any retained-evidence verification or vault I/O.
                if (request.RecordedAtUtc == default || request.RecordedAtUtc.Offset != TimeSpan.Zero)
                    throw new ArgumentException("Forecast recording time must be an explicit UTC instant.", nameof(request));
                if (request.RecordedAtUtc > DateTimeOffset.UtcNow)
                    throw new ArgumentException("Forecast recording time cannot be later than the authenticated capture time.", nameof(request));
                var service = context.RequestServices.GetService<IAccountCashForecastService>();
                var evidence = context.RequestServices.GetService<IEvidenceArtifactStore>();
                if (service is null || evidence is null)
                    return OversightUnavailable("Durable account forecasts and scoped retained source evidence are required.");
                var bound = request with { Scope = scope.ForecastScope };
                await new AccountCashForecastEvidenceAdapter(evidence).VerifyAsync(bound, context.RequestAborted).ConfigureAwait(false);
                return Results.Json(await service.CaptureAsync(bound, context.RequestAborted).ConfigureAwait(false), jsonOptions);
            }).ConfigureAwait(false)).WithName("CaptureLedgerAccountCashForecast"));

        OversightRead(app.MapGet(UiApiRoutes.LedgerAccountCashForecasts, async (
            Guid ledgerBookId, Guid accountId, HttpContext context) =>
            await ExecuteAccountOversightAsync(context, ledgerBookId, accountId, false, async scope =>
            {
                var service = context.RequestServices.GetService<IAccountCashForecastService>();
                if (service is null)
                    return OversightUnavailable("Account cash forecasts are not configured.");
                return Results.Json(await service.GetHistoryAsync(scope.ForecastScope, context.RequestAborted)
                    .ConfigureAwait(false), jsonOptions);
            }).ConfigureAwait(false)).WithName("ListLedgerAccountCashForecasts"));

        OversightRead(app.MapGet(UiApiRoutes.LedgerAccountCashForecast, async (
            Guid ledgerBookId, Guid accountId, string snapshotId, HttpContext context) =>
            await ExecuteAccountOversightAsync(context, ledgerBookId, accountId, false, async scope =>
            {
                var service = context.RequestServices.GetService<IAccountCashForecastService>();
                if (service is null)
                    return OversightUnavailable("Account cash forecasts are not configured.");
                var retained = await service.GetAsync(scope.ForecastScope, snapshotId, context.RequestAborted).ConfigureAwait(false);
                return retained is null ? Results.NotFound() : Results.Json(retained, jsonOptions);
            }).ConfigureAwait(false)).WithName("GetLedgerAccountCashForecast"));
    }

    private static RouteHandlerBuilder OversightRead(RouteHandlerBuilder route) => route
        .RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending,
            UserPermission.ViewLedgerReports, UserPermission.ManageLedgerReports)
        .RequireAuthenticatedSession().RequireWorkstationTenantCompanyScope();

    private static RouteHandlerBuilder OversightMutation(RouteHandlerBuilder route) => route
        .RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending, UserPermission.ManageLedgerReports)
        .RequireAuthenticatedSession().RequireWorkstationTenantCompanyScope().RequireFundScopedWriteTenant()
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

    private static async Task<IResult> ExecuteAccountOversightAsync(HttpContext context, Guid ledgerBookId,
        Guid accountId, bool mutation, Func<AccountOversightScope, Task<IResult>> execute)
    {
        if (mutation ? !HasLedgerMutationPermission(context) : !HasLedgerReadPermission(context))
            return EndpointHelpers.Forbidden();
        var tenant = HttpContextWorkstationTenantContextAccessor.Resolve(context);
        if (!HasAccountingPackageTenantScope(tenant) || !EndpointAuthorization.TryResolveActor(context, out var actor))
            return OversightScopeDenied();
        var books = context.RequestServices.GetService<ILedgerBookService>();
        var registry = context.RequestServices.GetService<IFundProfileTenancyRegistry>();
        if (books is null || registry is null)
            return OversightUnavailable("Authoritative account ledger books and retained ownership are required.");
        try
        {
            var book = await books.GetBookAsync(ledgerBookId, context.RequestAborted).ConfigureAwait(false);
            if (book is null || book.LedgerBookId != ledgerBookId || accountId == Guid.Empty
                || book.FundStructureNodeKind != FundStructureNodeKindDto.Account || book.FundStructureNodeId != accountId)
                return OversightScopeDenied();
            var owner = await registry.ResolveAsync(book.FundProfileId, context.RequestAborted).ConfigureAwait(false);
            if (owner is null || !CloseWorkflowOwnerMatches(owner, tenant))
                return OversightScopeDenied();
            return await execute(new(tenant.TenantId!, tenant.CompanyId!, accountId.ToString("D"), book, actor.Trim()))
                .ConfigureAwait(false);
        }
        catch (KeyNotFoundException) { return Results.NotFound(); }
        catch (ArgumentException ex) { return Results.Problem(ex.Message, statusCode: StatusCodes.Status422UnprocessableEntity); }
        catch (AccountCashForecastSourceAuthorityException ex)
        { return Results.Conflict(new { code = "CASH_FORECAST_SOURCE_AUTHORITY_REQUIRED", error = ex.Message }); }
        catch (InvalidOperationException ex) { return Results.Conflict(new { error = ex.Message }); }
        catch (InvalidDataException) { return OversightUnavailable("Retained oversight evidence is unavailable or corrupt; restore it before retrying."); }
        catch (TimeoutException) { return OversightUnavailable("Retained oversight history is locked by another writer; retry shortly."); }
        catch (LedgerBookServiceException ex) { return MapServiceException(ex); }
    }

    /// <summary>
    /// The unconfigured Security Master fallback answers reads with empty history and refuses writes
    /// with a conflict; retained close prices are unavailable, not absent, so treat it as missing.
    /// </summary>
    private static ISecurityMasterPricingService? ResolveClosePriceService(HttpContext context)
        => context.RequestServices.GetService<ISecurityMasterPricingService>()
            is { } service and not Meridian.Application.SecurityMaster.NullSecurityMasterPricingService ? service : null;

    private static IResult OversightScopeDenied() => Results.Problem(
        "The exact account ledger book is not accessible to the authenticated tenant and company.",
        statusCode: StatusCodes.Status403Forbidden);
    private static IResult OversightUnavailable(string detail) => Results.Problem(detail, statusCode: StatusCodes.Status503ServiceUnavailable);

    private sealed record AccountOversightScope(string TenantId, string CompanyId, string AccountId, LedgerBookDto Book, string Actor)
    {
        public string BookId => Book.LedgerBookId.ToString("D");
        public AccountCashReconciliationScope ReconciliationScope => new(TenantId, CompanyId, AccountId, Book.LedgerBookId);
        public AccountCashForecastScopeDto ForecastScope => new(TenantId, CompanyId, AccountId, BookId, Book.BaseCurrency);
    }

    private sealed class AccountOversightDecimalJsonConverter : JsonConverter<decimal>
    {
        public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Number && reader.TryGetDecimal(out var numeric))
                return numeric;
            if (reader.TokenType == JsonTokenType.String
                && decimal.TryParse(reader.GetString(), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture, out var text))
                return text;
            throw new JsonException("An exact decimal amount is required.");
        }

        public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options)
            => writer.WriteStringValue(value.ToString("0.############################", CultureInfo.InvariantCulture));
    }
}
