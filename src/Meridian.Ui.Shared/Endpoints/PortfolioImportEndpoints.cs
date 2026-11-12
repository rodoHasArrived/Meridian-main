using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Application.Subscriptions.Services;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Operations;
using Meridian.Contracts.Tenancy;
using Meridian.Contracts.Workstation;
using Meridian.Core.Subscriptions.Models;
using Meridian.Identity.Auth;
using Meridian.PortfolioRecords.FundAccounts;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

public sealed record BrokerPortfolioImportRequest(Guid RunId, Guid FinancialAccountId, Guid LedgerBookId,
    Guid PeriodId, DateOnly EffectiveDate, string Broker, PortfolioImportOptions Options,
    bool PrepareOpeningBalances = false);

/// <summary>Account-scoped imports retain their source before subscription changes and opening-draft intake.</summary>
public static class PortfolioImportEndpoints
{
    public static void MapPortfolioImportEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/portfolio/import-runs").WithTags("Portfolio Imports")
            .RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageFundStructure);
        group.AddEndpointFilter(async (invocation, next) =>
        {
            var scope = HttpContextWorkstationTenantContextAccessor.Resolve(invocation.HttpContext);
            if (!scope.HasTenantScope || !TenantReadPredicate.IsResolvedTenant(scope.CompanyId) || string.IsNullOrWhiteSpace(scope.Actor))
                return EndpointHelpers.Forbidden();
            try
            { return await next(invocation); }
            catch (UnauthorizedAccessException) { return EndpointHelpers.Forbidden(); }
            catch (HumanOperatorRequiredException) { return EndpointHelpers.Forbidden(); }
            catch (ArgumentException ex) { return Results.Problem(ex.Message, statusCode: 400); }
            catch (JsonException) { return Results.Problem("The import command is malformed.", statusCode: 400); }
            catch (InvalidOperationException ex) { return Results.Problem(ex.Message, statusCode: 409); }
        });
        group.MapPost("", async (HttpContext context) =>
        {
            OperationsOriginGuard.RequireHumanOperator(
                EndpointAuthorization.ResolveTrustedActionOrigin(context, OperationsActionOriginDto.HumanOperator), "import broker account balances");
            var body = await JsonSerializer.DeserializeAsync(context.Request.Body,
                PortfolioImportApiJsonContext.Default.BrokerPortfolioImportRequest, context.RequestAborted)
                ?? throw new ArgumentException("An import command is required.");
            var scope = HttpContextWorkstationTenantContextAccessor.Resolve(context);
            var account = await VerifyAccountAsync(context, body.FinancialAccountId, body.LedgerBookId, body.Options.BrokerAccountId);
            var service = context.RequestServices.GetRequiredService<PortfolioImportRunService>();
            var run = await service.RunAsync(new PortfolioImportRunRequest(body.RunId, body.Broker, body.Options,
                new PortfolioImportAccountingScope(scope.TenantId!, scope.CompanyId!, account.FundId!.Value.ToString("D"), body.FinancialAccountId,
                    body.LedgerBookId, body.PeriodId, body.EffectiveDate, scope.Actor!, account.EntityId), body.PrepareOpeningBalances), context.RequestAborted);
            return Results.Json(run, PortfolioImportApiJsonContext.Default.PortfolioImportRun);
        }).WithName("RunBrokerPortfolioImport").RequireFundScopedWriteTenant()
            .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);
        group.MapGet("/{runId:guid}", async (Guid runId, HttpContext context) =>
        {
            var scope = HttpContextWorkstationTenantContextAccessor.Resolve(context);
            var run = await context.RequestServices.GetRequiredService<PortfolioImportRunService>()
                .GetAsync(scope.TenantId!, scope.CompanyId!, runId, context.RequestAborted);
            if (run is null)
                return Results.NotFound();
            await VerifyAccountAsync(context, run.Request.Scope.FinancialAccountId, run.Request.Scope.LedgerBookId,
                run.Request.Options.BrokerAccountId);
            return Results.Json(run, PortfolioImportApiJsonContext.Default.PortfolioImportRun);
        }).WithName("GetBrokerPortfolioImportRun");
    }

    private static async Task<AccountSummaryDto> VerifyAccountAsync(HttpContext context, Guid accountId, Guid bookId, string? providerAccount)
    {
        var ct = context.RequestAborted;
        var scope = HttpContextWorkstationTenantContextAccessor.Resolve(context);
        var accounts = context.RequestServices.GetRequiredService<IFundAccountService>();
        var books = context.RequestServices.GetService<ILedgerBookService>()
            ?? throw new InvalidOperationException("Durable account/ledger authority is unavailable.");
        var account = await accounts.GetAccountAsync(accountId, ct);
        var book = await books.GetBookAsync(bookId, ct);
        if (account is null || !account.IsActive || account.FundId is null || book is null ||
            book.FundStructureNodeId != accountId || book.FundProfileId != account.FundId.Value.ToString("D"))
            throw new UnauthorizedAccessException();
        var ownership = await context.RequestServices.GetRequiredService<IFundProfileTenancyRegistry>().ResolveAsync(book.FundProfileId, ct);
        if (ownership is null || !ownership.IsHeldBy(scope.TenantId) || ownership.CompanyId != scope.CompanyId)
            throw new UnauthorizedAccessException();
        var externalAccount = account.CustodianDetails?.SubAccountNumber ?? account.PortfolioId ?? account.AccountCode;
        if (string.IsNullOrWhiteSpace(providerAccount) || externalAccount != providerAccount)
            throw new InvalidOperationException("Select the broker account bound to the retained financial account.");
        return account;
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true)]
[JsonSerializable(typeof(BrokerPortfolioImportRequest))]
[JsonSerializable(typeof(PortfolioImportRun))]
internal partial class PortfolioImportApiJsonContext : JsonSerializerContext;
