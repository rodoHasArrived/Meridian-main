using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.Contracts.Ledger;
using Meridian.Identity.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Meridian.Ui.Shared.Endpoints;

public static partial class LedgerEndpoints
{
    private static void MapAccountingConfigurationEndpoints(WebApplication app, JsonSerializerOptions jsonOptions)
    {
        app.MapGet(UiApiRoutes.LedgerAccountingConfiguration, async (
            string? fundProfileId,
            Guid? ledgerBookId,
            HttpContext context) =>
        {
            if (!HasLedgerReadPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var service = ResolveAccountingConfigurationService(context);
            if (service is null)
            {
                return ServiceUnavailable();
            }

            var tenantContext = HttpContextWorkstationTenantContextAccessor.Resolve(context);
            var workspace = await service
                .GetWorkspaceAsync(fundProfileId, ledgerBookId, context.RequestAborted, tenantContext.TenantId, tenantContext.CompanyId)
                .ConfigureAwait(false);
            return Results.Json(workspace, jsonOptions);
        })
        .WithName("GetAccountingConfiguration")
        .RequireFundProfileTenantScope(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending)
        .Produces<AccountingConfigurationWorkspaceDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status501NotImplemented);

        app.MapPost(UiApiRoutes.LedgerAccountingConfigurationChart, async (UpsertChartOfAccountsNodeRequest request, HttpContext context) =>
        {
            if (!HasLedgerMutationPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var service = ResolveAccountingConfigurationService(context);
            if (service is null)
            {
                return ServiceUnavailable();
            }

            try
            {
                var result = await service.UpsertChartNodeAsync(WithAccessContext(request, context), context.RequestAborted).ConfigureAwait(false);
                return Results.Json(result, jsonOptions);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        })
        .WithName("UpsertAccountingConfigurationChartNode")
        .Produces<AccountingConfigurationWorkspaceDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status501NotImplemented)
        .RequireFundScopedWriteTenant()
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        app.MapPost(UiApiRoutes.LedgerAccountingConfigurationTemplates, async (UpsertJournalEntryTemplateRequest request, HttpContext context) =>
        {
            if (!HasLedgerMutationPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var service = ResolveAccountingConfigurationService(context);
            if (service is null)
            {
                return ServiceUnavailable();
            }

            try
            {
                var result = await service.UpsertTemplateAsync(WithAccessContext(request, context), context.RequestAborted).ConfigureAwait(false);
                return Results.Json(result, jsonOptions);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        })
        .WithName("UpsertAccountingConfigurationTemplate")
        .Produces<AccountingConfigurationWorkspaceDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status501NotImplemented)
        .RequireFundScopedWriteTenant()
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        app.MapPost(UiApiRoutes.LedgerAccountingConfigurationPostingRules, async (UpsertPostingRuleRequest request, HttpContext context) =>
        {
            if (!HasLedgerMutationPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var service = ResolveAccountingConfigurationService(context);
            if (service is null)
            {
                return ServiceUnavailable();
            }

            try
            {
                var result = await service.UpsertPostingRuleAsync(WithAccessContext(request, context), context.RequestAborted).ConfigureAwait(false);
                return Results.Json(result, jsonOptions);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        })
        .WithName("UpsertAccountingConfigurationPostingRule")
        .Produces<AccountingConfigurationWorkspaceDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status501NotImplemented)
        .RequireFundScopedWriteTenant()
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        app.MapPost(UiApiRoutes.LedgerAccountingConfigurationPostingRulePromotionApprovals, async (ApprovePostingRulePromotionRequest request, HttpContext context) =>
        {
            if (!HasLedgerCertificationPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var service = ResolveAccountingConfigurationService(context);
            if (service is null)
            {
                return ServiceUnavailable();
            }

            try
            {
                var result = await service.ApprovePostingRulePromotionAsync(WithAccessContext(request, context), context.RequestAborted).ConfigureAwait(false);
                return Results.Json(result, jsonOptions);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
        })
        .WithName("ApproveAccountingConfigurationPostingRulePromotion")
        .Produces<AccountingConfigurationWorkspaceDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status409Conflict)
        .Produces(StatusCodes.Status501NotImplemented)
        .RequireFundScopedWriteTenant()
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        app.MapPost(UiApiRoutes.LedgerAccountingConfigurationPostingRuleTestCases, async (UpsertAccountingRuleTestCaseRequest request, HttpContext context) =>
        {
            if (!HasLedgerMutationPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var service = ResolveAccountingConfigurationService(context);
            if (service is null)
            {
                return ServiceUnavailable();
            }

            try
            {
                var result = await service.UpsertRuleTestCaseAsync(WithAccessContext(request, context), context.RequestAborted).ConfigureAwait(false);
                return Results.Json(result, jsonOptions);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        })
        .WithName("UpsertAccountingConfigurationPostingRuleTestCase")
        .Produces<AccountingConfigurationWorkspaceDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status501NotImplemented)
        .RequireFundScopedWriteTenant()
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        app.MapPost(UiApiRoutes.LedgerAccountingConfigurationPreview, async (PreviewJournalTemplateRequest request, HttpContext context) =>
        {
            if (!HasLedgerReadPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var service = ResolveAccountingConfigurationService(context);
            if (service is null)
            {
                return ServiceUnavailable();
            }

            var tenantContext = HttpContextWorkstationTenantContextAccessor.Resolve(context);
            if (!await IsBodyFundScopeAccessibleAsync(context, tenantContext, request.FundProfileId).ConfigureAwait(false))
            {
                return EndpointHelpers.Forbidden();
            }

            var result = await service.PreviewTemplateAsync(request with
            {
                Actor = ResolveMutationActor(context, request.Actor),
                TenantId = tenantContext.TenantId,
                CompanyId = tenantContext.CompanyId
            }, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(result, jsonOptions);
        })
        .WithName("PreviewAccountingConfigurationTemplate")
        .Produces<AccountingJournalTemplatePreviewDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status501NotImplemented)
        // SEC-005 slice 4c-iii: body-scoped fund EVALUATION (read-permission POST). The fund-scope read
        // gate above is fail-open; this adds the write/evaluate tenant gate so enforcement fails closed.
        .RequireFundScopedWriteTenant();

        app.MapPost(UiApiRoutes.LedgerAccountingConfigurationPostingRuleDryRun, async (RuleDryRunRequestDto request, HttpContext context) =>
        {
            if (!HasLedgerReadPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var service = ResolveAccountingConfigurationService(context);
            if (service is null)
            {
                return ServiceUnavailable();
            }

            try
            {
                var tenantContext = HttpContextWorkstationTenantContextAccessor.Resolve(context);
                if (!await IsBodyFundScopeAccessibleAsync(context, tenantContext, request.FundProfileId).ConfigureAwait(false))
                {
                    return EndpointHelpers.Forbidden();
                }

                var result = await service.DryRunPostingRuleAsync(request with
                {
                    Actor = ResolveMutationActor(context, request.Actor),
                    TenantId = tenantContext.TenantId,
                    CompanyId = tenantContext.CompanyId
                }, context.RequestAborted).ConfigureAwait(false);
                return Results.Json(result, jsonOptions);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        })
        .WithName("DryRunAccountingConfigurationPostingRule")
        .Produces<RuleDryRunResultDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status501NotImplemented)
        .RequireFundScopedWriteTenant();

        app.MapPost(UiApiRoutes.LedgerAccountingConfigurationPostingRuleCandidates, async (PostingRuleJournalCandidateRequestDto request, HttpContext context) =>
        {
            if (!HasLedgerReadPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var service = ResolveAccountingPostingCandidateService(context);
            if (service is null)
            {
                return ServiceUnavailable();
            }

            try
            {
                var tenantContext = HttpContextWorkstationTenantContextAccessor.Resolve(context);
                if (!await IsBodyFundScopeAccessibleAsync(context, tenantContext, request.FundProfileId).ConfigureAwait(false))
                {
                    return EndpointHelpers.Forbidden();
                }

                var result = await service
                    .BuildCandidateAsync(request with
                    {
                        Actor = ResolveMutationActor(context, request.Actor),
                        TenantId = tenantContext.TenantId,
                        CompanyId = tenantContext.CompanyId
                    }, context.RequestAborted)
                    .ConfigureAwait(false);
                return Results.Json(result, jsonOptions);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        })
        .WithName("BuildAccountingConfigurationPostingRuleCandidate")
        .Produces<PostingRuleJournalCandidateResultDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status501NotImplemented)
        .RequireFundScopedWriteTenant();

        app.MapPost(UiApiRoutes.LedgerAccountingConfigurationPostingRuleCandidatePosts, async (PostPostingRuleJournalCandidateRequestDto request, HttpContext context) =>
        {
            if (!HasLedgerCertificationPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var service = ResolveAccountingPostingCandidatePostService(context);
            if (service is null)
            {
                return ServiceUnavailable();
            }

            try
            {
                var tenantContext = HttpContextWorkstationTenantContextAccessor.Resolve(context);
                var result = await service
                    .PostCandidateAsync(request with
                    {
                        Actor = ResolveMutationActor(context, request.Actor),
                        TenantId = tenantContext.TenantId,
                        CompanyId = tenantContext.CompanyId
                    }, context.RequestAborted)
                    .ConfigureAwait(false);
                return Results.Json(result, jsonOptions);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
        })
        .WithName("PostAccountingConfigurationPostingRuleCandidate")
        .Produces<PostedPostingRuleJournalCandidateResultDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status409Conflict)
        .Produces(StatusCodes.Status501NotImplemented)
        .RequireFundScopedWriteTenant()
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        app.MapPost(UiApiRoutes.LedgerAccountingConfigurationPostingRuleProjectionSets, async (AccountingBasisProjectionSetRequestDto request, HttpContext context) =>
        {
            if (!HasLedgerReadPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var service = ResolveAccountingBasisProjectionSetService(context);
            if (service is null)
            {
                return ServiceUnavailable();
            }

            try
            {
                var tenantContext = HttpContextWorkstationTenantContextAccessor.Resolve(context);
                if (!await IsBodyFundScopeAccessibleAsync(context, tenantContext, request.FundProfileId).ConfigureAwait(false))
                {
                    return EndpointHelpers.Forbidden();
                }

                var result = await service
                    .BuildProjectionSetAsync(request with
                    {
                        Actor = ResolveMutationActor(context, request.Actor),
                        TenantId = tenantContext.TenantId,
                        CompanyId = tenantContext.CompanyId
                    }, context.RequestAborted)
                    .ConfigureAwait(false);
                return Results.Json(result, jsonOptions);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        })
        .WithName("BuildAccountingConfigurationPostingRuleProjectionSet")
        .Produces<AccountingBasisProjectionSetDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status501NotImplemented)
        .RequireFundScopedWriteTenant();

        app.MapPost(UiApiRoutes.LedgerAccountingConfigurationPostingRuleTests, async (ExecuteAccountingRuleTestCasesRequestDto request, HttpContext context) =>
        {
            if (!HasLedgerReadPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var service = ResolveAccountingConfigurationService(context);
            if (service is null)
            {
                return ServiceUnavailable();
            }

            try
            {
                var tenantContext = HttpContextWorkstationTenantContextAccessor.Resolve(context);
                if (!await IsBodyFundScopeAccessibleAsync(context, tenantContext, request.FundProfileId).ConfigureAwait(false))
                {
                    return EndpointHelpers.Forbidden();
                }

                var result = await service.ExecuteRuleTestCasesAsync(request with
                {
                    Actor = ResolveMutationActor(context, request.Actor),
                    TenantId = tenantContext.TenantId,
                    CompanyId = tenantContext.CompanyId
                }, context.RequestAborted).ConfigureAwait(false);
                return Results.Json(result, jsonOptions);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        })
        .WithName("ExecuteAccountingConfigurationPostingRuleTests")
        .Produces<AccountingRuleTestSuiteResultDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status501NotImplemented)
        .RequireFundScopedWriteTenant();

        app.MapPost(UiApiRoutes.LedgerAccountingConfigurationActivate, async (ActivateAccountingConfigurationRequest request, HttpContext context) =>
        {
            if (!HasLedgerCertificationPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var service = ResolveAccountingConfigurationService(context);
            if (service is null)
            {
                return ServiceUnavailable();
            }

            try
            {
                var result = await service.ActivateAsync(WithAccessContext(request, context), context.RequestAborted).ConfigureAwait(false);
                return Results.Json(result, jsonOptions);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        })
        .WithName("ActivateAccountingConfiguration")
        .Produces<AccountingConfigurationWorkspaceDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status501NotImplemented)
        .RequireFundScopedWriteTenant()
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        app.MapGet(UiApiRoutes.LedgerAccountingConfigurationAudit, async (
            string? fundProfileId,
            Guid? ledgerBookId,
            HttpContext context) =>
        {
            if (!HasLedgerReadPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var service = ResolveAccountingConfigurationService(context);
            if (service is null)
            {
                return ServiceUnavailable();
            }

            var tenantContext = HttpContextWorkstationTenantContextAccessor.Resolve(context);
            var audit = await service
                .ListAuditAsync(fundProfileId, ledgerBookId, context.RequestAborted, tenantContext.TenantId, tenantContext.CompanyId)
                .ConfigureAwait(false);
            return Results.Json(audit, jsonOptions);
        })
        .WithName("ListAccountingConfigurationAudit")
        .RequireFundProfileTenantScope(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending)
        .Produces<IReadOnlyList<AccountingActionAuditEventDto>>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status501NotImplemented);
    }
}
