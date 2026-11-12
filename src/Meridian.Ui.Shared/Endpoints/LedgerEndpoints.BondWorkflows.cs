using System.Text.Json;
using Meridian.Application.Integrations;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.Api;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.Ledger;
using Meridian.Storage.FixedIncome;
using Meridian.Ui.Shared.Evidence;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

public static partial class LedgerEndpoints
{

    public static void MapLedgerBondWorkflowEndpoints(this WebApplication app)
    {
        OversightRead(app.MapPost(UiApiRoutes.LedgerAccountBondWorkflow, async (
            Guid ledgerBookId, Guid accountId, Guid securityId, HttpContext context) =>
            await ExecuteAccountOversightAsync(context, ledgerBookId, accountId, false, async scope =>
            {
                var request = await BondWorkflowEndpoints.BodyAsync<BondWorkflowRequest>(context).ConfigureAwait(false);
                var result = await context.RequestServices.GetRequiredService<IBondWorkflowService>()
                    .CalculateAsync(request with { SecurityId = securityId, AccountId = scope.AccountId }, context.RequestAborted).ConfigureAwait(false);
                return result is null ? Results.NotFound() : Results.Json(result, BondWorkflowEndpoints.Json);
            }).ConfigureAwait(false)).WithName("CalculateAccountBondWorkflow"));
        OversightRead(app.MapPost(UiApiRoutes.LedgerAccountBondIncomePreview, async (
            Guid ledgerBookId, Guid accountId, HttpContext context) =>
            await ExecuteAccountOversightAsync(context, ledgerBookId, accountId, false, async scope =>
            {
                var request = await BondWorkflowEndpoints.BodyAsync<AccountBondIncomePlanningRequest>(context).ConfigureAwait(false);
                var bound = request with { Scope = scope.ForecastScope };
                if (bound.OpeningBalances is null || bound.FxConversions is null)
                    return Results.BadRequest(new { error = "Opening balances and explicit FX collections are required." });
                var sources = new AccountCashForecastRequestDto("verify-planning-inputs", scope.ForecastScope,
                    bound.PeriodStart, bound.PeriodEnd, null, null, [], bound.FxConversions, DateTimeOffset.UtcNow)
                { CalculationVersion = "account-cash-forecast-v2", NativeOpeningBalances = bound.OpeningBalances };
                await new AccountCashForecastEvidenceAdapter(context.RequestServices.GetRequiredService<IEvidenceArtifactStore>())
                    .VerifyAsync(sources, context.RequestAborted).ConfigureAwait(false);
                var result = await context.RequestServices.GetRequiredService<IAccountBondIncomePlanningService>()
                    .PlanAsync(bound, context.RequestAborted).ConfigureAwait(false);
                await context.RequestServices.GetRequiredService<BondWorkspaceSnapshotStore>().RetainAsync(
                    scope.ForecastScope, "income-plan", result.MonitoringInputs.RunId, result, context.RequestAborted).ConfigureAwait(false);
                return Results.Json(result, BondWorkflowEndpoints.Json);
            }).ConfigureAwait(false)).WithName("PreviewAccountBondIncome"));

        OversightRead(app.MapGet(UiApiRoutes.LedgerAccountBondMonitoring, async (
            Guid ledgerBookId, Guid accountId, HttpContext context) =>
            await ExecuteAccountOversightAsync(context, ledgerBookId, accountId, false, async scope =>
                Results.Json(await context.RequestServices.GetRequiredService<IBondMonitoringService>()
                    .GetHistoryAsync(scope.ForecastScope, context.RequestAborted).ConfigureAwait(false), BondWorkflowEndpoints.Json))
                .ConfigureAwait(false)).WithName("GetAccountBondMonitoring"));

        OversightMutation(app.MapPost(UiApiRoutes.LedgerAccountBondMonitoring, async (
            Guid ledgerBookId, Guid accountId, HttpContext context) =>
            await ExecuteAccountOversightAsync(context, ledgerBookId, accountId, true, async scope =>
            {
                var request = await BondWorkflowEndpoints.BodyAsync<BondMonitoringCaptureRequest>(context).ConfigureAwait(false);
                var plan = await context.RequestServices.GetRequiredService<BondWorkspaceSnapshotStore>()
                    .ReadAsync<AccountBondIncomePlanDto>(scope.ForecastScope, "income-plan", request.PlanningRunId ?? request.RunId,
                        context.RequestAborted).ConfigureAwait(false);
                if (plan is null || JsonSerializer.Serialize(request.Obligations) != JsonSerializer.Serialize(plan.MonitoringInputs.Obligations))
                    return Results.Conflict(new { error = "Monitoring requires the exact server-retained plan obligations. Rebuild the account plan." });
                var prior = await context.RequestServices.GetRequiredService<IBondMonitoringService>()
                    .GetHistoryAsync(scope.ForecastScope, context.RequestAborted).ConfigureAwait(false);
                var recordedAt = prior.FirstOrDefault(row => row.Inputs.RunId == request.RunId)?.Inputs.RecordedAtUtc
                    ?? DateTimeOffset.UtcNow;
                var bound = request with
                {
                    Scope = scope.ForecastScope,
                    RecordedAtUtc = recordedAt,
                    CalculationContext = plan.MonitoringInputs.CalculationContext,
                    Resets = plan.MonitoringInputs.Resets,
                    ExerciseWindows = plan.MonitoringInputs.ExerciseWindows,
                    MarketWatch = plan.MonitoringInputs.MarketWatch
                };
                await new AccountCashForecastEvidenceAdapter(context.RequestServices.GetRequiredService<IEvidenceArtifactStore>())
                    .VerifyMonitoringAsync(bound, context.RequestAborted).ConfigureAwait(false);
                var captured = await context.RequestServices.GetRequiredService<IBondMonitoringService>()
                    .CaptureAsync(bound, context.RequestAborted).ConfigureAwait(false);
                await context.RequestServices.GetRequiredService<BondCollectionCaseworkProjection>()
                    .ProjectAsync(captured, scope.Actor, context.RequestAborted).ConfigureAwait(false);
                return Results.Json(captured, BondWorkflowEndpoints.Json);
            }).ConfigureAwait(false)).WithName("CaptureAccountBondMonitoring"));

        OversightRead(app.MapPost(UiApiRoutes.LedgerAccountBondRisk, async (
            Guid ledgerBookId, Guid accountId, HttpContext context) =>
            await ExecuteAccountOversightAsync(context, ledgerBookId, accountId, false, async scope =>
            {
                var request = await BondWorkflowEndpoints.BodyAsync<BondPortfolioRiskRequest>(context).ConfigureAwait(false);
                var result = await context.RequestServices.GetRequiredService<IBondPortfolioRiskService>()
                    .CalculateAsync(scope.ForecastScope, request, context.RequestAborted).ConfigureAwait(false);
                var runId = request.RunId ?? Guid.NewGuid().ToString("D");
                request = request with { RunId = runId };
                result = result with { RunId = runId };
                await context.RequestServices.GetRequiredService<BondWorkspaceSnapshotStore>().RetainAsync(
                    scope.ForecastScope, "risk", runId, new BondPortfolioRiskSnapshotDto(request, result), context.RequestAborted).ConfigureAwait(false);
                return Results.Json(result, BondWorkflowEndpoints.Json);
            }).ConfigureAwait(false)).WithName("CalculateAccountBondRisk"));
        OversightRead(app.MapGet(UiApiRoutes.LedgerAccountBondRiskSnapshot, async (
            Guid ledgerBookId, Guid accountId, string runId, HttpContext context) =>
            await ExecuteAccountOversightAsync(context, ledgerBookId, accountId, false, async scope =>
            {
                var retained = await context.RequestServices.GetRequiredService<BondWorkspaceSnapshotStore>()
                    .ReadAsync<BondPortfolioRiskSnapshotDto>(scope.ForecastScope, "risk", runId, context.RequestAborted).ConfigureAwait(false);
                return retained is null ? Results.NotFound() : Results.Json(retained, BondWorkflowEndpoints.Json);
            }).ConfigureAwait(false)).WithName("GetAccountBondRisk"));

        OversightMutation(app.MapPost(UiApiRoutes.LedgerAccountBondComparisons, async (
            Guid ledgerBookId, Guid accountId, HttpContext context) =>
            await ExecuteAccountOversightAsync(context, ledgerBookId, accountId, true, async scope =>
            {
                var request = await BondWorkflowEndpoints.BodyAsync<BondComparisonRequest>(context).ConfigureAwait(false);
                if (request.Bonds is null || request.Bonds.Any(row => row is null))
                    return Results.BadRequest();
                request = request with { Bonds = request.Bonds.Select(row => row with { AccountId = scope.AccountId }).ToArray() };
                var result = await context.RequestServices.GetRequiredService<IBondComparisonService>()
                    .CompareAsync(request, context.RequestAborted).ConfigureAwait(false);
                await context.RequestServices.GetRequiredService<BondWorkspaceSnapshotStore>()
                    .RetainAsync(scope.ForecastScope, "comparison", request.RunId, new BondComparisonSnapshotDto(request, result),
                        context.RequestAborted).ConfigureAwait(false);
                return Results.Json(result, BondWorkflowEndpoints.Json);
            }).ConfigureAwait(false)).WithName("SaveAccountBondComparison"));

        OversightRead(app.MapGet(UiApiRoutes.LedgerAccountBondComparison, async (
            Guid ledgerBookId, Guid accountId, string runId, HttpContext context) =>
            await ExecuteAccountOversightAsync(context, ledgerBookId, accountId, false, async scope =>
            {
                var retained = await context.RequestServices.GetRequiredService<BondWorkspaceSnapshotStore>()
                    .ReadAsync<BondComparisonSnapshotDto>(scope.ForecastScope, "comparison", runId, context.RequestAborted).ConfigureAwait(false);
                return retained is null ? Results.NotFound() : Results.Json(retained, BondWorkflowEndpoints.Json);
            }).ConfigureAwait(false)).WithName("GetAccountBondComparison"));

        OversightRead(app.MapPost(UiApiRoutes.LedgerAccountBondPerformance, async (
            Guid ledgerBookId, Guid accountId, HttpContext context) =>
            await ExecuteAccountOversightAsync(context, ledgerBookId, accountId, false, async scope =>
            {
                var request = await BondWorkflowEndpoints.BodyAsync<BondPerformanceRequest>(context).ConfigureAwait(false);
                return Results.Json(await context.RequestServices.GetRequiredService<IBondPerformanceService>()
                    .ExplainAsync(request with { Scope = scope.ForecastScope }, context.RequestAborted).ConfigureAwait(false), BondWorkflowEndpoints.Json);
            }).ConfigureAwait(false)).WithName("ExplainAccountBondPerformance"));
        OversightMutation(app.MapPost(UiApiRoutes.LedgerAccountBondPerformanceTransactions, async (
            Guid ledgerBookId, Guid accountId, HttpContext context) =>
            await ExecuteAccountOversightAsync(context, ledgerBookId, accountId, true, async scope =>
            {
                var request = await BondWorkflowEndpoints.BodyAsync<BondPerformanceTransactionImportRequest>(context).ConfigureAwait(false);
                return Results.Json(await context.RequestServices.GetRequiredService<BondPerformanceTransactionSource>()
                    .ImportAsync(scope.ForecastScope, request.EvidenceUri, request.DocumentId, scope.Actor,
                        context.RequestAborted).ConfigureAwait(false), BondWorkflowEndpoints.Json);
            }).ConfigureAwait(false)).WithName("ImportAccountBondPerformanceTransactions"));

        OversightMutation(app.MapPost(UiApiRoutes.LedgerAccountBondHoldingsImport, async (
            Guid ledgerBookId, Guid accountId, HttpContext context) =>
            await ExecuteAccountOversightAsync(context, ledgerBookId, accountId, true, async scope =>
            {
                var request = await BondWorkflowEndpoints.BodyAsync<BondHoldingImportRequest>(context).ConfigureAwait(false);
                if (request.AccountId != accountId)
                    return EndpointHelpers.Forbidden();
                return Results.Json(await context.RequestServices.GetRequiredService<BondHoldingImportService>()
                    .AcceptAsync(scope.TenantId, scope.Actor, request, context.RequestAborted).ConfigureAwait(false), BondWorkflowEndpoints.Json);
            }).ConfigureAwait(false)).WithName("ImportAccountBondHoldings"));

        OversightRead(app.MapPost(UiApiRoutes.LedgerAccountBondAccountingPreview, async (
            Guid ledgerBookId, Guid accountId, HttpContext context) =>
            await ExecuteAccountOversightAsync(context, ledgerBookId, accountId, false, async scope =>
            {
                var request = await BondWorkflowEndpoints.BodyAsync<BondIncomePreviewRequestDto>(context).ConfigureAwait(false);
                var service = context.RequestServices.GetService<IBondIncomeService>();
                if (service is null)
                    return OversightUnavailable("An authoritative ledger lot store and explicitly adopted bond-income policies are required.");
                return Results.Json(await service
                    .PreviewAsync(request with { LedgerBookId = ledgerBookId, AccountId = accountId }, context.RequestAborted).ConfigureAwait(false), BondWorkflowEndpoints.Json);
            }).ConfigureAwait(false)).WithName("PreviewAccountBondAccountingIncome"));

        OversightMutation(app.MapPost(UiApiRoutes.LedgerAccountBondAccountingPrepare, async (
            Guid ledgerBookId, Guid accountId, HttpContext context) =>
            await ExecuteAccountOversightAsync(context, ledgerBookId, accountId, true, async scope =>
            {
                var request = await BondWorkflowEndpoints.BodyAsync<BondIncomePreparationRequestDto>(context).ConfigureAwait(false);
                if (request.Request is null)
                    return Results.BadRequest();
                var bound = request with { Request = request.Request with { LedgerBookId = ledgerBookId, AccountId = accountId } };
                var service = context.RequestServices.GetService<BondIncomePreparationService>();
                if (service is null)
                    return OversightUnavailable("An authoritative ledger lot store, accounting review path and explicitly adopted bond-income policies are required.");
                return Results.Json(await service
                    .PrepareAsync(bound, scope.TenantId, scope.CompanyId, scope.Actor, DateTimeOffset.UtcNow,
                        context.RequestAborted).ConfigureAwait(false), BondWorkflowEndpoints.Json);
            }).ConfigureAwait(false)).WithName("PrepareAccountBondAccountingIncome"));

        OversightRead(app.MapPost(UiApiRoutes.LedgerAccountBondCouponPreview, async (
            Guid ledgerBookId, Guid accountId, HttpContext context) =>
            await ExecuteAccountOversightAsync(context, ledgerBookId, accountId, false, async scope =>
            {
                var service = context.RequestServices.GetService<IBondCouponRecognitionService>();
                if (service is null)
                    return OversightUnavailable("Durable coupon recognition receipts and adopted book policies are required.");
                var request = await BondWorkflowEndpoints.BodyAsync<BondCouponRecognitionRequest>(context).ConfigureAwait(false);
                return Results.Json(await service.PreviewAsync(scope.ForecastScope, request, context.RequestAborted).ConfigureAwait(false), BondWorkflowEndpoints.Json);
            }).ConfigureAwait(false)).WithName("PreviewAccountBondCouponRecognition"));
        OversightMutation(app.MapPost(UiApiRoutes.LedgerAccountBondCouponPrepare, async (
            Guid ledgerBookId, Guid accountId, HttpContext context) =>
            await ExecuteAccountOversightAsync(context, ledgerBookId, accountId, true, async scope =>
            {
                var service = context.RequestServices.GetService<IBondCouponRecognitionService>();
                var spine = context.RequestServices.GetService<IAssetAccountingEventSpineService>();
                if (service is null || spine is null)
                    return OversightUnavailable("Canonical coupon recognition and accounting review services are required.");
                var body = await BondWorkflowEndpoints.BodyAsync<BondCouponRecognitionPrepareRequestDto>(context).ConfigureAwait(false);
                if (body.Request is null)
                    return Results.BadRequest();
                var retained = context.RequestServices.GetRequiredService<BondWorkspaceSnapshotStore>();
                var preparation = await retained.ReadAsync<BondCouponRecognitionPreparationDto>(scope.ForecastScope,
                    "coupon-preparation", body.Request.RunId, context.RequestAborted).ConfigureAwait(false);
                if (preparation is not null && (preparation.Preview.Fingerprint != body.ExpectedFingerprint
                    || JsonSerializer.Serialize(preparation.Preview.Instruction.Request) != JsonSerializer.Serialize(body.Request)))
                    return Results.Conflict(new { error = "Coupon run identity already names a different reviewed preparation." });
                if (preparation is null)
                {
                    preparation = await service.PrepareAsync(scope.ForecastScope, body.Request, body.ExpectedFingerprint,
                        scope.Actor, DateTimeOffset.UtcNow, context.RequestAborted).ConfigureAwait(false);
                    await retained.RetainAsync(scope.ForecastScope, "coupon-preparation", body.Request.RunId,
                        preparation, context.RequestAborted).ConfigureAwait(false);
                }
                var projected = await spine.ProjectAsync(preparation.Projection, context.RequestAborted).ConfigureAwait(false);
                return Results.Json(new { Preparation = preparation, Projection = projected }, BondWorkflowEndpoints.Json);
            }).ConfigureAwait(false)).WithName("PrepareAccountBondCouponRecognition"));
    }
}
