using System.Text.Json;
using Meridian.Identity.Auth;
using Meridian.Application.FundStructure;
using Meridian.Contracts.Api;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Services;
using Meridian.Contracts.Workstation;
using Meridian.Entities.FundStructure;
using Meridian.Reporting;
using Meridian.Storage.Reporting;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;

namespace Meridian.Ui.Shared.Endpoints;

public static partial class FundStructureEndpoints
{
    public static void MapFundStructureEndpoints(this WebApplication app, JsonSerializerOptions jsonOptions)
    {
        var group = app.MapGroup("/api/fund-structure").WithTags("Fund Structure");
        var reportingGroup = group.MapGroup("/reporting").RequireWorkstationTenantScope();
        var legacyReportingGroup = group.MapGroup(string.Empty).RequireWorkstationTenantScope();


        group.MapPost("/setup-drafts/validate", (JsonElement body, HttpContext context) =>
        {
            var workflow = ResolveSetupWorkflow(context);
            if (workflow is null)
                return ServiceUnavailable();

            FundStructureSetupDraftDto? draft;
            try
            {
                draft = JsonSerializer.Deserialize<FundStructureSetupDraftDto>(body.GetRawText(), jsonOptions);
            }
            catch (JsonException ex)
            {
                return Results.Problem($"Setup draft is invalid JSON. {ex.Message}", statusCode: StatusCodes.Status400BadRequest);
            }

            var result = workflow.Preview(draft);
            return Results.Json(result, jsonOptions);
        })
        .WithName("ValidateFundStructureSetupDraft").RequireAnyPermission(UserPermission.ManageDirectLending, UserPermission.AdminMaintenance)
        .Produces<FundStructureSetupPreviewDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest);

        group.MapPost("/setup-drafts/create", async (JsonElement body, HttpContext context) =>
        {
            if (!HasFundStructureSetupCreatePermission(context))
            {
                return EndpointAuthorization.TryGetPermissions(context, out _)
                    ? EndpointHelpers.Forbidden()
                    : Results.Unauthorized();
            }

            if (!EndpointAuthorization.TryResolveActor(context, out var actor))
            {
                return Results.Unauthorized();
            }

            var workflow = ResolveSetupWorkflow(context);
            if (workflow is null)
                return ServiceUnavailable();

            FundStructureSetupDraftDto? draft;
            try
            {
                draft = JsonSerializer.Deserialize<FundStructureSetupDraftDto>(body.GetRawText(), jsonOptions);
            }
            catch (JsonException ex)
            {
                return Results.Problem($"Setup draft is invalid JSON. {ex.Message}", statusCode: StatusCodes.Status400BadRequest);
            }

            var validation = workflow.Validate(draft);
            if (!validation.IsValid)
            {
                return Results.Json(validation, jsonOptions, statusCode: StatusCodes.Status400BadRequest);
            }

            var result = await workflow.CreateAsync(draft!, actor, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(result, jsonOptions, statusCode: StatusCodes.Status201Created);
        })
        .WithName("CreateFundStructureSetupDraft").RequireAnyPermission(UserPermission.ManageDirectLending, UserPermission.AdminMaintenance)
        .Produces<FundStructureSetupResultDto>(StatusCodes.Status201Created)
        .Produces<FundStructureSetupValidationSummaryDto>(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden);

        group.MapPost("/organizations", async (JsonElement body, HttpContext context) =>
        {
            var service = ResolveService(context);
            if (service is null)
                return ServiceUnavailable();

            var request = JsonSerializer.Deserialize<CreateOrganizationRequest>(body.GetRawText(), jsonOptions);
            if (request is null)
            {
                return Results.Problem("Request body is required.", statusCode: StatusCodes.Status400BadRequest);
            }

            var result = await service.CreateOrganizationAsync(request, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(result, jsonOptions, statusCode: StatusCodes.Status201Created);
        })
        .WithName("CreateOrganization").RequirePermission(UserPermission.ManageFundStructure)
        .Produces<OrganizationSummaryDto>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status400BadRequest);

        group.MapPost("/businesses", async (JsonElement body, HttpContext context) =>
        {
            var service = ResolveService(context);
            if (service is null)
                return ServiceUnavailable();

            var request = JsonSerializer.Deserialize<CreateBusinessRequest>(body.GetRawText(), jsonOptions);
            if (request is null)
            {
                return Results.Problem("Request body is required.", statusCode: StatusCodes.Status400BadRequest);
            }

            var result = await service.CreateBusinessAsync(request, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(result, jsonOptions, statusCode: StatusCodes.Status201Created);
        })
        .WithName("CreateBusiness").RequirePermission(UserPermission.ManageFundStructure)
        .Produces<BusinessSummaryDto>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status400BadRequest);

        group.MapPost("/clients", async (JsonElement body, HttpContext context) =>
        {
            var service = ResolveService(context);
            if (service is null)
                return ServiceUnavailable();

            var request = JsonSerializer.Deserialize<CreateClientRequest>(body.GetRawText(), jsonOptions);
            if (request is null)
            {
                return Results.Problem("Request body is required.", statusCode: StatusCodes.Status400BadRequest);
            }

            var result = await service.CreateClientAsync(request, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(result, jsonOptions, statusCode: StatusCodes.Status201Created);
        })
        .WithName("CreateClient").RequirePermission(UserPermission.ManageFundStructure)
        .Produces<ClientSummaryDto>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status400BadRequest);

        group.MapPost("/funds", async (JsonElement body, HttpContext context) =>
        {
            var service = ResolveService(context);
            if (service is null)
                return ServiceUnavailable();

            var request = JsonSerializer.Deserialize<CreateFundRequest>(body.GetRawText(), jsonOptions);
            if (request is null)
            {
                return Results.Problem("Request body is required.", statusCode: StatusCodes.Status400BadRequest);
            }

            var result = await service.CreateFundAsync(request, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(result, jsonOptions, statusCode: StatusCodes.Status201Created);
        })
        .WithName("CreateStructureFund").RequirePermission(UserPermission.ManageFundStructure)
        .Produces<FundSummaryDto>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status400BadRequest);

        group.MapPost("/sleeves", async (JsonElement body, HttpContext context) =>
        {
            var service = ResolveService(context);
            if (service is null)
                return ServiceUnavailable();

            var request = JsonSerializer.Deserialize<CreateSleeveRequest>(body.GetRawText(), jsonOptions);
            if (request is null)
            {
                return Results.Problem("Request body is required.", statusCode: StatusCodes.Status400BadRequest);
            }

            var result = await service.CreateSleeveAsync(request, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(result, jsonOptions, statusCode: StatusCodes.Status201Created);
        })
        .WithName("CreateSleeve").RequirePermission(UserPermission.ManageFundStructure)
        .Produces<SleeveSummaryDto>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status400BadRequest);

        group.MapPost("/vehicles", async (JsonElement body, HttpContext context) =>
        {
            var service = ResolveService(context);
            if (service is null)
                return ServiceUnavailable();

            var request = JsonSerializer.Deserialize<CreateVehicleRequest>(body.GetRawText(), jsonOptions);
            if (request is null)
            {
                return Results.Problem("Request body is required.", statusCode: StatusCodes.Status400BadRequest);
            }

            var result = await service.CreateVehicleAsync(request, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(result, jsonOptions, statusCode: StatusCodes.Status201Created);
        })
        .WithName("CreateVehicle").RequirePermission(UserPermission.ManageFundStructure)
        .Produces<VehicleSummaryDto>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status400BadRequest);

        group.MapPost("/entities", async (JsonElement body, HttpContext context) =>
        {
            var service = ResolveService(context);
            if (service is null)
                return ServiceUnavailable();

            var request = JsonSerializer.Deserialize<CreateLegalEntityRequest>(body.GetRawText(), jsonOptions);
            if (request is null)
            {
                return Results.Problem("Request body is required.", statusCode: StatusCodes.Status400BadRequest);
            }

            var result = await service.CreateLegalEntityAsync(request, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(result, jsonOptions, statusCode: StatusCodes.Status201Created);
        })
        .WithName("CreateLegalEntity").RequirePermission(UserPermission.ManageFundStructure)
        .Produces<LegalEntitySummaryDto>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status400BadRequest);

        group.MapPut("/entities/{entityId:guid}", async (Guid entityId, JsonElement body, HttpContext context) =>
        {
            var service = ResolveService(context);
            if (service is null)
                return ServiceUnavailable();

            UpdateLegalEntityProfileRequest? request;
            try
            {
                request = JsonSerializer.Deserialize<UpdateLegalEntityProfileRequest>(body.GetRawText(), jsonOptions);
            }
            catch (JsonException ex)
            {
                return Results.Problem($"Legal entity profile request is invalid JSON. {ex.Message}", statusCode: StatusCodes.Status400BadRequest);
            }

            if (request is null)
            {
                return Results.Problem("Request body is required.", statusCode: StatusCodes.Status400BadRequest);
            }

            request = request with { EntityId = entityId };
            try
            {
                var result = await service.UpdateLegalEntityProfileAsync(request, context.RequestAborted).ConfigureAwait(false);
                return Results.Json(result, jsonOptions);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        })
        .WithName("UpdateLegalEntityProfile")
        .RequirePermission(UserPermission.ManageFundStructure)
        .Produces<LegalEntitySummaryDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden);

        group.MapPost("/investment-portfolios", async (JsonElement body, HttpContext context) =>
        {
            var service = ResolveService(context);
            if (service is null)
                return ServiceUnavailable();

            var request = JsonSerializer.Deserialize<CreateInvestmentPortfolioRequest>(body.GetRawText(), jsonOptions);
            if (request is null)
            {
                return Results.Problem("Request body is required.", statusCode: StatusCodes.Status400BadRequest);
            }

            var result = await service.CreateInvestmentPortfolioAsync(request, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(result, jsonOptions, statusCode: StatusCodes.Status201Created);
        })
        .WithName("CreateInvestmentPortfolio").RequirePermission(UserPermission.ManageFundStructure)
        .Produces<InvestmentPortfolioSummaryDto>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status400BadRequest);

        group.MapPost("/links", async (JsonElement body, HttpContext context) =>
        {
            var service = ResolveService(context);
            if (service is null)
                return ServiceUnavailable();

            var request = JsonSerializer.Deserialize<LinkFundStructureNodesRequest>(body.GetRawText(), jsonOptions);
            if (request is null)
            {
                return Results.Problem("Request body is required.", statusCode: StatusCodes.Status400BadRequest);
            }

            var result = await service.LinkNodesAsync(request, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(result, jsonOptions, statusCode: StatusCodes.Status201Created);
        })
        .WithName("LinkFundStructureNodes").RequirePermission(UserPermission.ManageFundStructure)
        .Produces<OwnershipLinkDto>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status400BadRequest);

        group.MapPut("/links/{ownershipLinkId:guid}", async (Guid ownershipLinkId, JsonElement body, HttpContext context) =>
        {
            var service = ResolveService(context);
            if (service is null)
                return ServiceUnavailable();

            var request = JsonSerializer.Deserialize<UpdateOwnershipLinkRequest>(body.GetRawText(), jsonOptions);
            if (request is null)
            {
                return Results.Problem("Request body is required.", statusCode: StatusCodes.Status400BadRequest);
            }

            request = request with { OwnershipLinkId = ownershipLinkId };
            try
            {
                var result = await service.UpdateOwnershipLinkAsync(request, context.RequestAborted).ConfigureAwait(false);
                return Results.Json(result, jsonOptions);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        })
        .WithName("UpdateOwnershipLink")
        .RequirePermission(UserPermission.ManageFundStructure)
        .Produces<OwnershipLinkDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden);

        group.MapPost("/links/{ownershipLinkId:guid}/expire", async (Guid ownershipLinkId, JsonElement body, HttpContext context) =>
        {
            var service = ResolveService(context);
            if (service is null)
                return ServiceUnavailable();

            var request = JsonSerializer.Deserialize<ExpireOwnershipLinkRequest>(body.GetRawText(), jsonOptions);
            if (request is null)
            {
                return Results.Problem("Request body is required.", statusCode: StatusCodes.Status400BadRequest);
            }

            request = request with { OwnershipLinkId = ownershipLinkId };
            try
            {
                var result = await service.ExpireOwnershipLinkAsync(request, context.RequestAborted).ConfigureAwait(false);
                return Results.Json(result, jsonOptions);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        })
        .WithName("ExpireOwnershipLink")
        .RequirePermission(UserPermission.ManageFundStructure)
        .Produces<OwnershipLinkDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden);

        group.MapPost("/links/{ownershipLinkId:guid}/replace", async (Guid ownershipLinkId, JsonElement body, HttpContext context) =>
        {
            var service = ResolveService(context);
            if (service is null)
                return ServiceUnavailable();

            var request = JsonSerializer.Deserialize<ReplaceOwnershipLinkRequest>(body.GetRawText(), jsonOptions);
            if (request is null)
            {
                return Results.Problem("Request body is required.", statusCode: StatusCodes.Status400BadRequest);
            }

            request = request with { OwnershipLinkId = ownershipLinkId };
            try
            {
                var result = await service.ReplaceOwnershipLinkAsync(request, context.RequestAborted).ConfigureAwait(false);
                return Results.Json(result, jsonOptions, statusCode: StatusCodes.Status201Created);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        })
        .WithName("ReplaceOwnershipLink")
        .RequirePermission(UserPermission.ManageFundStructure)
        .Produces<OwnershipLinkDto>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden);

        group.MapPost("/links/validate", async (JsonElement body, HttpContext context) =>
        {
            var service = ResolveService(context);
            if (service is null)
                return ServiceUnavailable();

            var request = JsonSerializer.Deserialize<ValidateOwnershipGraphRequest>(body.GetRawText(), jsonOptions)
                ?? new ValidateOwnershipGraphRequest();
            var result = await service.ValidateOwnershipGraphAsync(request, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(result, jsonOptions);
        })
        .WithName("ValidateOwnershipGraph").RequirePermission(UserPermission.ManageFundStructure)
        .Produces<OwnershipGraphValidationResultDto>(StatusCodes.Status200OK);

        group.MapPost("/assignments", async (JsonElement body, HttpContext context) =>
        {
            var service = ResolveService(context);
            if (service is null)
                return ServiceUnavailable();

            var request = JsonSerializer.Deserialize<AssignFundStructureNodeRequest>(body.GetRawText(), jsonOptions);
            if (request is null)
            {
                return Results.Problem("Request body is required.", statusCode: StatusCodes.Status400BadRequest);
            }

            request = NormalizeLedgerGroupAssignmentRequest(request, out var assignmentReferenceError);
            if (assignmentReferenceError is not null)
            {
                return Results.Problem(assignmentReferenceError, statusCode: StatusCodes.Status400BadRequest);
            }

            var result = await service.AssignNodeAsync(request, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(result, jsonOptions, statusCode: StatusCodes.Status201Created);
        })
        .WithName("AssignFundStructureNode").RequirePermission(UserPermission.ManageFundStructure)
        .Produces<FundStructureAssignmentDto>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status400BadRequest);

        group.MapPost("/ledger-mapping-assignments", async (JsonElement body, HttpContext context) =>
        {
            LedgerMappingAssignmentRequestDto? request;
            try
            {
                request = JsonSerializer.Deserialize<LedgerMappingAssignmentRequestDto>(body.GetRawText(), jsonOptions);
            }
            catch (JsonException ex)
            {
                return Results.Problem(
                    $"Ledger mapping assignment request is invalid JSON. {ex.Message}",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            if (!TryValidateLedgerMappingAssignmentRequest(request, out var validationError))
            {
                return Results.Problem(validationError, statusCode: StatusCodes.Status400BadRequest);
            }

            if (!HasLedgerMappingAssignmentPermission(context))
            {
                return EndpointAuthorization.TryGetPermissions(context, out _)
                    ? EndpointHelpers.Forbidden()
                    : Results.Unauthorized();
            }

            if (!EndpointAuthorization.TryResolveActor(context, out var actor))
            {
                return Results.Unauthorized();
            }

            var service = ResolveService(context);
            if (service is null)
                return ServiceUnavailable();

            var effectiveFrom = request!.EffectiveFrom ?? DateTimeOffset.UtcNow;
            var beforeView = await service.GetAccountingViewAsync(
                    new AccountingStructureQuery(ActiveOnly: true, AsOf: effectiveFrom),
                    context.RequestAborted)
                .ConfigureAwait(false);
            var beforeWorkbench = LedgerMappingWorkbenchService.Build(beforeView, effectiveFrom);
            var beforeAccount = beforeWorkbench.Accounts.FirstOrDefault(account => account.AccountId == request.AccountId);
            if (beforeAccount is null)
            {
                return Results.Problem(
                    $"Account {request.AccountId} was not found in the accounting structure view.",
                    statusCode: StatusCodes.Status404NotFound);
            }

            var assignmentRequest = new AssignFundStructureNodeRequest(
                request.AssignmentId ?? Guid.NewGuid(),
                request.AccountId,
                LedgerGroupingRules.LedgerGroupAssignmentType,
                request.LedgerGroupId,
                effectiveFrom,
                actor,
                IsPrimary: true);
            assignmentRequest = NormalizeLedgerGroupAssignmentRequest(assignmentRequest, out var assignmentReferenceError);
            if (assignmentReferenceError is not null)
            {
                return Results.Problem(assignmentReferenceError, statusCode: StatusCodes.Status400BadRequest);
            }

            FundStructureAssignmentDto assignment;
            try
            {
                assignment = await service.AssignNodeAsync(assignmentRequest, context.RequestAborted).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is ArgumentException or FormatException or InvalidOperationException)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }

            var afterView = await service.GetAccountingViewAsync(
                    new AccountingStructureQuery(ActiveOnly: true, AsOf: effectiveFrom),
                    context.RequestAborted)
                .ConfigureAwait(false);
            var afterWorkbench = LedgerMappingWorkbenchService.Build(afterView, effectiveFrom);
            var afterAccount = afterWorkbench.Accounts.FirstOrDefault(account => account.AccountId == request.AccountId)
                ?? beforeAccount;
            var audit = new LedgerMappingAssignmentAuditEventDto(
                Guid.NewGuid(),
                "ledger-mapping-assigned",
                DateTimeOffset.UtcNow,
                actor,
                request.Rationale.Trim(),
                string.IsNullOrWhiteSpace(request.CorrelationId) ? $"ledger-map-{assignment.AssignmentId:N}" : request.CorrelationId.Trim(),
                request.AccountId,
                beforeAccount.AccountCode,
                beforeAccount.Mapping.RequiresUserMapping ? null : beforeAccount.Mapping.LedgerGroupId.Value,
                assignment.AssignmentReference,
                assignment.AssignmentId);

            return Results.Json(
                new LedgerMappingAssignmentResultDto(assignment, afterAccount, audit, afterWorkbench),
                jsonOptions,
                statusCode: StatusCodes.Status201Created);
        })
        .WithName("AssignLedgerMapping").RequireAnyPermission(UserPermission.ManageFundStructure, UserPermission.ManageDirectLending, UserPermission.AdminMaintenance)
        .Produces<LedgerMappingAssignmentResultDto>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/graph", async (HttpContext context) =>
        {
            var service = ResolveService(context);
            if (service is null)
                return ServiceUnavailable();

            var q = context.Request.Query;
            var query = new OrganizationStructureQuery(
                OrganizationId: ParseGuid(q["organizationId"]),
                BusinessId: ParseGuid(q["businessId"]),
                NodeId: ParseGuid(q["nodeId"]),
                NodeKind: ParseNodeKind(q["nodeKind"]),
                ActiveOnly: ParseActiveOnly(q["activeOnly"]),
                AsOf: ParseDateTimeOffset(q["asOf"]));

            var scopeGuard = await RequireScopedFundStructureGraphReadAccessAsync(query, context).ConfigureAwait(false);
            if (scopeGuard is not null)
            {
                return scopeGuard;
            }

            var result = await service.GetOrganizationStructureAsync(query, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(result, jsonOptions);
        })
        .WithName("GetOrganizationStructureGraph").RequireAnyPermission(UserPermission.ManageFundStructure, UserPermission.ManageDirectLending, UserPermission.AdminMaintenance)
        .Produces<OrganizationStructureGraphDto>(StatusCodes.Status200OK);

        group.MapGet("/legacy-graph", async (HttpContext context) =>
        {
            var service = ResolveService(context);
            if (service is null)
                return ServiceUnavailable();

            var q = context.Request.Query;
            var query = new FundStructureQuery(
                FundId: ParseGuid(q["fundId"]),
                NodeId: ParseGuid(q["nodeId"]),
                NodeKind: ParseNodeKind(q["nodeKind"]),
                ActiveOnly: ParseActiveOnly(q["activeOnly"]),
                AsOf: ParseDateTimeOffset(q["asOf"]));

            var scopeGuard = await RequireScopedFundStructureGraphReadAccessAsync(query, context).ConfigureAwait(false);
            if (scopeGuard is not null)
            {
                return scopeGuard;
            }

            var result = await service.GetFundStructureGraphAsync(query, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(result, jsonOptions);
        })
        .WithName("GetLegacyFundStructureGraph").RequireAnyPermission(UserPermission.ManageFundStructure, UserPermission.ManageDirectLending, UserPermission.AdminMaintenance)
        .Produces<FundStructureGraphDto>(StatusCodes.Status200OK);

        group.MapGet("/businesses/{businessId:guid}/advisory-view", async (Guid businessId, HttpContext context) =>
        {
            var service = ResolveService(context);
            if (service is null)
                return ServiceUnavailable();

            var q = context.Request.Query;
            var query = new AdvisoryStructureQuery(
                businessId,
                OrganizationId: ParseGuid(q["organizationId"]),
                ClientId: ParseGuid(q["clientId"]),
                InvestmentPortfolioId: ParseGuid(q["investmentPortfolioId"]),
                ActiveOnly: ParseActiveOnly(q["activeOnly"]),
                AsOf: ParseDateTimeOffset(q["asOf"]));

            var scopeGuard = await RequireScopedFundStructureReadAccessAsync(
                context,
                (AccessScopeKindDto.InvestmentPortfolio, query.InvestmentPortfolioId),
                (AccessScopeKindDto.Client, query.ClientId),
                (AccessScopeKindDto.Business, query.BusinessId),
                (AccessScopeKindDto.Organization, query.OrganizationId)).ConfigureAwait(false);
            if (scopeGuard is not null)
            {
                return scopeGuard;
            }

            var result = await service.GetAdvisoryViewAsync(query, context.RequestAborted).ConfigureAwait(false);
            return result is null ? Results.NotFound() : Results.Json(result, jsonOptions);
        })
        .WithName("GetAdvisoryStructureView").RequireAnyPermission(UserPermission.ManageFundStructure, UserPermission.ManageDirectLending, UserPermission.AdminMaintenance)
        .Produces<AdvisoryStructureViewDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/businesses/{businessId:guid}/fund-view", async (Guid businessId, HttpContext context) =>
        {
            var service = ResolveService(context);
            if (service is null)
                return ServiceUnavailable();

            var q = context.Request.Query;
            var query = new FundOperatingStructureQuery(
                businessId,
                OrganizationId: ParseGuid(q["organizationId"]),
                FundId: ParseGuid(q["fundId"]),
                SleeveId: ParseGuid(q["sleeveId"]),
                VehicleId: ParseGuid(q["vehicleId"]),
                InvestmentPortfolioId: ParseGuid(q["investmentPortfolioId"]),
                ActiveOnly: ParseActiveOnly(q["activeOnly"]),
                AsOf: ParseDateTimeOffset(q["asOf"]));

            var scopeGuard = await RequireScopedFundStructureReadAccessAsync(
                context,
                (AccessScopeKindDto.InvestmentPortfolio, query.InvestmentPortfolioId),
                (AccessScopeKindDto.Vehicle, query.VehicleId),
                (AccessScopeKindDto.Sleeve, query.SleeveId),
                (AccessScopeKindDto.Fund, query.FundId),
                (AccessScopeKindDto.Business, query.BusinessId),
                (AccessScopeKindDto.Organization, query.OrganizationId)).ConfigureAwait(false);
            if (scopeGuard is not null)
            {
                return scopeGuard;
            }

            var result = await service.GetFundOperatingViewAsync(query, context.RequestAborted).ConfigureAwait(false);
            return result is null ? Results.NotFound() : Results.Json(result, jsonOptions);
        })
        .WithName("GetFundOperatingStructureView").RequireAnyPermission(UserPermission.ManageFundStructure, UserPermission.ManageDirectLending, UserPermission.AdminMaintenance)
        .Produces<FundOperatingViewDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/accounting-view", async (HttpContext context) =>
        {
            var service = ResolveService(context);
            if (service is null)
                return ServiceUnavailable();

            var q = context.Request.Query;
            var query = new AccountingStructureQuery(
                OrganizationId: ParseGuid(q["organizationId"]),
                BusinessId: ParseGuid(q["businessId"]),
                ClientId: ParseGuid(q["clientId"]),
                FundId: ParseGuid(q["fundId"]),
                SleeveId: ParseGuid(q["sleeveId"]),
                VehicleId: ParseGuid(q["vehicleId"]),
                InvestmentPortfolioId: ParseGuid(q["investmentPortfolioId"]),
                LedgerReference: q["ledgerReference"].FirstOrDefault(),
                ActiveOnly: ParseActiveOnly(q["activeOnly"]),
                AsOf: ParseDateTimeOffset(q["asOf"]));

            var scopeGuard = await RequireScopedAccountingStructureReadAccessAsync(query, context).ConfigureAwait(false);
            if (scopeGuard is not null)
            {
                return scopeGuard;
            }

            var result = await service.GetAccountingViewAsync(query, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(result, jsonOptions);
        })
        .WithName("GetAccountingStructureView").RequireAnyPermission(UserPermission.ManageFundStructure, UserPermission.ManageDirectLending, UserPermission.AdminMaintenance)
        .Produces<AccountingStructureViewDto>(StatusCodes.Status200OK);

        group.MapGet("/ledger-mapping-view", async (HttpContext context) =>
        {
            var service = ResolveService(context);
            if (service is null)
                return ServiceUnavailable();

            var q = context.Request.Query;
            var asOf = ParseDateTimeOffset(q["asOf"]) ?? DateTimeOffset.UtcNow;
            var query = new AccountingStructureQuery(
                OrganizationId: ParseGuid(q["organizationId"]),
                BusinessId: ParseGuid(q["businessId"]),
                ClientId: ParseGuid(q["clientId"]),
                FundId: ParseGuid(q["fundId"]),
                SleeveId: ParseGuid(q["sleeveId"]),
                VehicleId: ParseGuid(q["vehicleId"]),
                InvestmentPortfolioId: ParseGuid(q["investmentPortfolioId"]),
                LedgerReference: q["ledgerReference"].FirstOrDefault(),
                ActiveOnly: ParseActiveOnly(q["activeOnly"]),
                AsOf: asOf);

            var scopeGuard = await RequireScopedAccountingStructureReadAccessAsync(query, context).ConfigureAwait(false);
            if (scopeGuard is not null)
            {
                return scopeGuard;
            }

            var accountingView = await service.GetAccountingViewAsync(query, context.RequestAborted).ConfigureAwait(false);
            var result = LedgerMappingWorkbenchService.Build(accountingView, asOf);
            return Results.Json(result, jsonOptions);
        })
        .WithName("GetLedgerMappingWorkbench").RequireAnyPermission(UserPermission.ManageFundStructure, UserPermission.ManageDirectLending, UserPermission.AdminMaintenance)
        .Produces<LedgerMappingWorkbenchDto>(StatusCodes.Status200OK);

        group.MapGet("/cash-flow-view", async (HttpContext context) =>
        {
            var q = context.Request.Query;
            var scopeKind = ParseCashFlowScopeKind(q["scopeKind"]);
            if (scopeKind is null)
            {
                return Results.Json(
                    new { error = "scopeKind is required and must be a valid governance cash-flow scope." },
                    jsonOptions,
                    statusCode: StatusCodes.Status400BadRequest);
            }

            var ledgerGroupId = ParseLedgerGroupId(q["ledgerGroupId"], out var ledgerGroupParseError);
            if (ledgerGroupParseError is not null)
            {
                return Results.Json(
                    new { error = ledgerGroupParseError },
                    jsonOptions,
                    statusCode: StatusCodes.Status400BadRequest);
            }

            var query = new GovernanceCashFlowQuery(
                scopeKind.Value,
                OrganizationId: ParseGuid(q["organizationId"]),
                BusinessId: ParseGuid(q["businessId"]),
                ClientId: ParseGuid(q["clientId"]),
                FundId: ParseGuid(q["fundId"]),
                SleeveId: ParseGuid(q["sleeveId"]),
                VehicleId: ParseGuid(q["vehicleId"]),
                InvestmentPortfolioId: ParseGuid(q["investmentPortfolioId"]),
                AccountId: ParseGuid(q["accountId"]),
                LedgerGroupId: ledgerGroupId,
                ActiveOnly: ParseActiveOnly(q["activeOnly"]),
                AsOf: ParseDateTimeOffset(q["asOf"]),
                Currency: q["currency"].FirstOrDefault(),
                HistoricalDays: ParseInt(q["historicalDays"], 7),
                ForecastDays: ParseInt(q["forecastDays"], 7),
                BucketDays: ParseInt(q["bucketDays"], 7));

            var scopeGuard = await RequireScopedGovernanceCashFlowReadAccessAsync(query, context).ConfigureAwait(false);
            if (scopeGuard is not null)
            {
                return scopeGuard;
            }

            if (TryCreateEmptyUnassignedLedgerGroupCashFlowView(scopeKind.Value, ledgerGroupId, q)
                is { } emptyUnassignedLedgerGroupView)
            {
                return Results.Json(emptyUnassignedLedgerGroupView, jsonOptions);
            }

            var service = ResolveService(context);
            if (service is null)
            {
                return ServiceUnavailable();
            }

            var result = await service.GetCashFlowViewAsync(query, context.RequestAborted).ConfigureAwait(false);
            return result is null ? Results.NotFound() : Results.Json(result, jsonOptions);
        })
        .WithName("GetGovernanceCashFlowView").RequireAnyPermission(UserPermission.ManageFundStructure, UserPermission.ManageDirectLending, UserPermission.AdminMaintenance)
        .Produces<GovernanceCashFlowViewDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound);

        MapFundOperationsWorkspaceAndLegacyReportingEndpoints(
            group,
            reportingGroup,
            legacyReportingGroup,
            jsonOptions);

        reportingGroup.MapGet("/templates", (HttpContext context) =>
        {
            if (!HasReportingReadPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var registry = context.RequestServices.GetService<ReportTemplateRegistryService>();
            if (registry is null)
            {
                return WorkspaceServiceUnavailable();
            }

            var includeSuperseded = string.Equals(
                context.Request.Query["includeSuperseded"].FirstOrDefault(),
                "true",
                StringComparison.OrdinalIgnoreCase);
            var accessContext = BuildReportAccessQueryContext(context);
            var records = registry
                .List(accessContext, includeSuperseded)
                .Where(record => ReportAccessPolicyEvaluator.Evaluate(record.Definition.AccessPolicy, accessContext).IsAccessible)
                .ToArray();
            return Results.Json(records, jsonOptions);
        })
        .WithName("ListReportTemplates").RequireAnyPermission(UserPermission.ViewReporting, UserPermission.ManageReporting, UserPermission.ApproveReporting, UserPermission.DeliverReporting, UserPermission.AdminMaintenance)
        .Produces<IReadOnlyList<ReportTemplateGovernanceRecordDto>>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status403Forbidden);

        reportingGroup.MapPost("/templates", (ReportTemplateDefinitionDto request, HttpContext context) =>
        {
            if (!HasReportingWorkflowPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            if (!EndpointAuthorization.TryResolveActor(context, out var actor))
            {
                return Results.Unauthorized();
            }

            if (context.RequestServices.GetService<IReportTemplateGovernanceStore>() is null)
            {
                return ReportingMutationStoreUnavailable("Custom report-template governance");
            }

            var registry = context.RequestServices.GetService<ReportTemplateRegistryService>();
            if (registry is null)
            {
                return WorkspaceServiceUnavailable();
            }

            try
            {
                var accessContext = BuildReportAccessQueryContext(context);
                var draft = registry.CreateDraft(
                    new ReportTemplateDraftRequestDto(
                        request.TemplateId.Name,
                        request.DisplayName,
                        request.Sections ?? [],
                        request.Parameters,
                        Family: "Custom",
                        Rationale: "Created through the compatibility template endpoint; explicit review and approval are required.",
                        Grids: request.Grids,
                        AccessPolicy: request.AccessPolicy),
                    actor,
                    accessContext.CompanyId,
                    accessContext.GroupPrincipalIds,
                    accessContext.TenantId);
                return Results.Json(draft, jsonOptions, statusCode: StatusCodes.Status201Created);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status403Forbidden);
            }
        })
        .WithName("RegisterReportTemplate").RequireAnyPermission(UserPermission.ManageReporting, UserPermission.AdminMaintenance)
        .Produces<ReportTemplateGovernanceRecordDto>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status503ServiceUnavailable);

        reportingGroup.MapPost("/templates/drafts", (ReportTemplateDraftRequestDto request, HttpContext context) =>
        {
            if (!HasReportingWorkflowPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            if (!EndpointAuthorization.TryResolveActor(context, out var actor))
            {
                return Results.Unauthorized();
            }

            if (context.RequestServices.GetService<IReportTemplateGovernanceStore>() is null)
            {
                return ReportingMutationStoreUnavailable("Custom report-template governance");
            }

            var registry = context.RequestServices.GetService<ReportTemplateRegistryService>();
            if (registry is null)
            {
                return WorkspaceServiceUnavailable();
            }

            try
            {
                var accessContext = BuildReportAccessQueryContext(context);
                return Results.Json(
                    registry.CreateDraft(
                        request,
                        actor,
                        accessContext.CompanyId,
                        accessContext.GroupPrincipalIds,
                        accessContext.TenantId),
                    jsonOptions,
                    statusCode: StatusCodes.Status201Created);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status403Forbidden);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        })
        .WithName("CreateReportTemplateDraft").RequireAnyPermission(UserPermission.ManageReporting, UserPermission.AdminMaintenance)
        .Produces<ReportTemplateGovernanceRecordDto>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status503ServiceUnavailable);

        reportingGroup.MapPost("/templates/{templateName}/versions/{version:int}/submit", (string templateName, int version, ReportTemplateDecisionRequestDto? request, HttpContext context) =>
        {
            if (!HasReportingWorkflowPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            if (!EndpointAuthorization.TryResolveActor(context, out var actor))
            {
                return Results.Unauthorized();
            }

            if (context.RequestServices.GetService<IReportTemplateGovernanceStore>() is null)
            {
                return ReportingMutationStoreUnavailable("Custom report-template governance");
            }

            var registry = context.RequestServices.GetService<ReportTemplateRegistryService>();
            if (registry is null)
            {
                return WorkspaceServiceUnavailable();
            }

            try
            {
                return Results.Json(
                    registry.Submit(
                        new VersionedReportTemplateIdDto(templateName, version),
                        actor,
                        request?.Rationale,
                        BuildReportAccessQueryContext(context)),
                    jsonOptions);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status403Forbidden);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or KeyNotFoundException)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        })
        .WithName("SubmitReportTemplateDraft").RequireAnyPermission(UserPermission.ManageReporting, UserPermission.AdminMaintenance)
        .Produces<ReportTemplateGovernanceRecordDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status503ServiceUnavailable);

        reportingGroup.MapPost("/templates/{templateName}/versions/{version:int}/approve", (string templateName, int version, ReportTemplateDecisionRequestDto request, HttpContext context) =>
        {
            if (!HasReportingApprovalPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            if (!EndpointAuthorization.TryResolveActor(context, out var actor))
            {
                return Results.Unauthorized();
            }

            if (context.RequestServices.GetService<IReportTemplateGovernanceStore>() is null)
            {
                return ReportingMutationStoreUnavailable("Custom report-template governance");
            }

            var registry = context.RequestServices.GetService<ReportTemplateRegistryService>();
            if (registry is null)
            {
                return WorkspaceServiceUnavailable();
            }

            try
            {
                return Results.Json(
                    registry.Approve(
                        new VersionedReportTemplateIdDto(templateName, version),
                        request,
                        actor,
                        BuildReportAccessQueryContext(context)),
                    jsonOptions);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status403Forbidden);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or KeyNotFoundException)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        })
        .WithName("ApproveReportTemplateDraft").RequireAnyPermission(UserPermission.ApproveReporting, UserPermission.AdminMaintenance)
        .Produces<ReportTemplateGovernanceRecordDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status503ServiceUnavailable);

        reportingGroup.MapPost("/templates/{templateName}/versions/{version:int}/reject", (string templateName, int version, ReportTemplateDecisionRequestDto request, HttpContext context) =>
        {
            if (!HasReportingApprovalPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            if (!EndpointAuthorization.TryResolveActor(context, out var actor))
            {
                return Results.Unauthorized();
            }

            if (context.RequestServices.GetService<IReportTemplateGovernanceStore>() is null)
            {
                return ReportingMutationStoreUnavailable("Custom report-template governance");
            }

            var registry = context.RequestServices.GetService<ReportTemplateRegistryService>();
            if (registry is null)
            {
                return WorkspaceServiceUnavailable();
            }

            try
            {
                return Results.Json(
                    registry.Reject(
                        new VersionedReportTemplateIdDto(templateName, version),
                        request,
                        actor,
                        BuildReportAccessQueryContext(context)),
                    jsonOptions);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status403Forbidden);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or KeyNotFoundException)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        })
        .WithName("RejectReportTemplateDraft").RequireAnyPermission(UserPermission.ApproveReporting, UserPermission.AdminMaintenance)
        .Produces<ReportTemplateGovernanceRecordDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status503ServiceUnavailable);

        reportingGroup.MapPost("/templates/render", (RenderReportTemplateRequestDto request, HttpContext context) =>
        {
            if (!HasReportingReadPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var registry = context.RequestServices.GetService<ReportTemplateRegistryService>();
            if (registry is null)
            {
                return WorkspaceServiceUnavailable();
            }

            try
            {
                var accessContext = BuildReportAccessQueryContext(context);
                var template = registry.Get(request.TemplateId, accessContext);
                if (template is null)
                {
                    return Results.Problem("Template not found.", statusCode: StatusCodes.Status400BadRequest);
                }

                if (!ReportAccessPolicyEvaluator.Evaluate(template.AccessPolicy, accessContext).IsAccessible)
                {
                    return EndpointHelpers.Forbidden();
                }

                return Results.Json(registry.Render(request, accessContext), jsonOptions);
            }
            catch (ArgumentException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        }).DeclareNonMutating("Renders a report template and returns the result; ReportingWorkflowService.Render resolves the template, evaluates the grids through ReportWriterGridEngine in memory, and returns a DTO without persisting anything. The body carries the template id, parameters, grid definitions and dataset rows, which is why it is a POST.").RequireAnyPermission(UserPermission.ViewReporting, UserPermission.ManageReporting, UserPermission.ApproveReporting, UserPermission.DeliverReporting, UserPermission.AdminMaintenance);

        reportingGroup.MapPost("/packs/create", (HttpContext context) =>
            LegacyReportingRouteGone(
                context,
                "/api/fund-structure/reporting/runs",
                "Legacy report-pack creation bypassed the canonical run contract and certified snapshot."));

        reportingGroup.MapPost("/packs", (HttpContext context) =>
            LegacyReportingRouteGone(
                context,
                "/api/fund-structure/reporting/runs",
                "Legacy report-pack creation bypassed the canonical run contract and certified snapshot."))
        .WithName("CreateReportingPackWorkflow")
        .ProducesProblem(StatusCodes.Status410Gone);

        reportingGroup.MapPost("/packs/{reportId:guid}/validate", (HttpContext context) =>
            LegacyReportingRouteGone(
                context,
                "/api/fund-structure/reporting/runs/{runId}/validate",
                "The legacy pack lifecycle was mutable and is not authoritative."));
        reportingGroup.MapPost("/packs/{reportId:guid}/submit", (HttpContext context) =>
            LegacyReportingRouteGone(
                context,
                "/api/fund-structure/reporting/runs/{runId}/submit",
                "The legacy pack lifecycle was mutable and is not authoritative."));
        reportingGroup.MapPost("/packs/{reportId:guid}/approve", (HttpContext context) =>
            LegacyReportingRouteGone(
                context,
                "/api/fund-structure/reporting/runs/{runId}/approve",
                "The legacy pack lifecycle did not enforce the canonical maker-checker state machine."));
        reportingGroup.MapPost("/packs/{reportId:guid}/reject", (HttpContext context) =>
            LegacyReportingRouteGone(
                context,
                "/api/fund-structure/reporting/runs",
                "Legacy rejection mutated the old pack record; remediation must create or advance a governed run."))
        .WithName("RejectReportingPackWorkflow")
        .ProducesProblem(StatusCodes.Status410Gone);
        reportingGroup.MapPost("/packs/{reportId:guid}/publish", (HttpContext context) =>
            LegacyReportingRouteGone(
                context,
                "/api/fund-structure/reporting/runs/{runId}/release",
                "Legacy publication accepted caller-supplied signers, hashes, manifest ids, and retention paths instead of verified retained artifacts."));

        reportingGroup.MapPost("/packs/{reportId:guid}/restatements", (HttpContext context) =>
            LegacyReportingRouteGone(
                context,
                "/api/fund-structure/reporting/runs/{runId}/restatement-requests",
                "In-place legacy restatement could rewrite a released pack; governed restatement creates a new revision after independent approval."))
        .WithName("RestateReportingPackWorkflow")
        .ProducesProblem(StatusCodes.Status410Gone);

        reportingGroup.MapPost("/packs/{reportId:guid}/archive", (HttpContext context) =>
            LegacyReportingRouteGone(
                context,
                "/api/fund-structure/reporting/runs/{runId}",
                "Client-driven archive mutation is not part of the immutable governed reporting lifecycle."));

        reportingGroup.MapGet("/packs/{reportId:guid}/deliveries", (HttpContext context) =>
            LegacyReportingRouteGone(
                context,
                "/api/fund-structure/reporting/distribution/packages/{runId}/deliveries",
                "Legacy delivery history embedded deterministic query-token routes and synthetic delivery state."))
        .WithName("GetReportingPackDeliveryHistory").RequireAnyPermission(UserPermission.ViewReporting, UserPermission.ManageReporting, UserPermission.ApproveReporting, UserPermission.DeliverReporting, UserPermission.AdminMaintenance)
        .ProducesProblem(StatusCodes.Status410Gone);

        reportingGroup.MapGet("/packs/{reportId:guid}/deliveries/{attemptId:guid}/package", (HttpContext context) =>
            LegacyReportingRouteGone(
                context,
                "/portal/reporting/access-grants/{grantId}/exchange",
                "Query-string package tokens are retired; exchange an opaque, scoped grant in a POST body."))
        .WithName("GetReportingPackDeliveryPackage").RequireAnyPermission(UserPermission.ViewReporting, UserPermission.ManageReporting, UserPermission.ApproveReporting, UserPermission.DeliverReporting, UserPermission.AdminMaintenance)
        .ProducesProblem(StatusCodes.Status410Gone);

        reportingGroup.MapGet("/packs/{reportId:guid}/deliveries/{attemptId:guid}/artifacts/{artifactName}", (HttpContext context) =>
            LegacyReportingRouteGone(
                context,
                "/api/fund-structure/reporting/distribution/packages/{runId}/artifacts/{artifactId}",
                "Query-string artifact tokens are retired; authenticated downloads now use the immutable artifact vault."))
        .WithName("GetReportingPackDeliveryArtifact").RequireAnyPermission(UserPermission.ViewReporting, UserPermission.ManageReporting, UserPermission.ApproveReporting, UserPermission.DeliverReporting, UserPermission.AdminMaintenance)
        .ProducesProblem(StatusCodes.Status410Gone);

        app.MapGet(UiApiRoutes.ReportingPackDeliveryPortalPackage, (HttpContext context) =>
            LegacyReportingRouteGone(
                context,
                "/portal/reporting/access-grants/{grantId}/exchange",
                "Query-string portal tokens are retired; the opaque grant must be exchanged through a no-store POST body."))
        .WithName("GetReportingPortalDeliveryPackage").DeclareOpenRead("Retired route that only answers 410 Gone with a pointer to the grant-exchange replacement; it reads nothing.")
        .ProducesProblem(StatusCodes.Status410Gone);

        reportingGroup.MapPost("/packs/{reportId:guid}/deliveries", (HttpContext context) =>
            LegacyReportingRouteGone(
                context,
                "/api/fund-structure/reporting/distribution/deliveries",
                "Legacy delivery could bypass canonical Released verification and durable transport receipts."))
        .WithName("CreateReportingPackDeliveryAttempt")
        .ProducesProblem(StatusCodes.Status410Gone);

        reportingGroup.MapPost("/packs/{reportId:guid}/deliveries/failures", (HttpContext context) =>
            LegacyReportingRouteGone(
                context,
                "/api/fund-structure/reporting/distribution/deliveries/{jobId}",
                "Client-recorded synthetic failures are retired; provider receipts are server-owned and reflected on the durable delivery job."))
        .WithName("CreateReportingPackDeliveryFailure")
        .ProducesProblem(StatusCodes.Status410Gone);

        reportingGroup.MapPost("/packs/{reportId:guid}/restate", (HttpContext context) =>
            LegacyReportingRouteGone(
                context,
                "/api/fund-structure/reporting/runs/{runId}/restatement-requests",
                "In-place legacy restatement is retired; approval creates a new immutable governed revision."));

        reportingGroup.MapGet("/runs", async (int? limit, HttpContext context) =>
        {
            if (!HasReportingReadPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            if (RequireReadyReportingDeployment(
                    context,
                    "Canonical reporting history is unavailable until the authoritative reporting deployment is ready.")
                is { } deploymentFailure)
            {
                return deploymentFailure;
            }

            var service = context.RequestServices.GetService<ReportPackRunReadService>();
            if (service is null)
            {
                return WorkspaceServiceUnavailable();
            }

            try
            {
                var history = await service
                    .BuildCanonicalHistoryAsync(
                        BuildReportAccessQueryContext(context),
                        Math.Clamp(limit ?? 25, 1, 200),
                        context.RequestAborted)
                    .ConfigureAwait(false);
                return Results.Json(history, jsonOptions);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status403Forbidden);
            }
            catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException)
            {
                return Results.Problem(
                    "The canonical reporting history store is temporarily unavailable.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        })
        .WithName("ListCanonicalReportingRuns").RequireAnyPermission(UserPermission.ViewReporting, UserPermission.ManageReporting, UserPermission.ApproveReporting, UserPermission.DeliverReporting, UserPermission.AdminMaintenance)
        .Produces<WorkstationReportingHistoryPayload>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        reportingGroup.MapPost("/runs/readiness", async (ReportingRunRequestDto request, HttpContext context) =>
        {
            if (!HasReportingWorkflowPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            if (!EndpointAuthorization.TryResolveActor(context, out _))
            {
                return Results.Unauthorized();
            }

            if (RequireReadyReportingDeployment(
                    context,
                    "Reporting run readiness is unavailable until the authoritative reporting deployment is ready.")
                is { } deploymentFailure)
            {
                return deploymentFailure;
            }

            var readinessService = context.RequestServices.GetService<ReportingRunReadinessService>();
            if (readinessService is null)
            {
                return WorkspaceServiceUnavailable();
            }

            try
            {
                var readiness = await readinessService
                    .AssessAsync(request, BuildReportAccessQueryContext(context), context.RequestAborted)
                    .ConfigureAwait(false);
                return Results.Json(readiness, jsonOptions);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status403Forbidden);
            }
            catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException or KeyNotFoundException)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        })
        .WithName("AssessReportingRunReadiness").RequireAnyPermission(UserPermission.ManageReporting, UserPermission.AdminMaintenance)
        .Accepts<ReportingRunRequestDto>("application/json")
        .Produces<ReportingRunReadinessDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        reportingGroup.MapPost("/runs", async (ReportingRunRequestDto request, HttpContext context) =>
        {
            if (!HasReportingWorkflowPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            if (!EndpointAuthorization.TryResolveActor(context, out var actor))
            {
                return Results.Unauthorized();
            }

            if (!TryResolveReportingGovernanceCaller(context, out var governanceCaller, out var callerError))
            {
                return callerError!;
            }

            if (RequireReadyReportingDeployment(
                    context,
                    "Ad-hoc reporting runs are unavailable until the authoritative reporting deployment is ready.")
                is { } deploymentFailure)
            {
                return deploymentFailure;
            }

            var svc = context.RequestServices.GetService<ReportingRunCommandService>();
            if (svc is null)
            {
                return WorkspaceServiceUnavailable();
            }

            try
            {
                return Results.Json(
                    await svc.RunAsync(
                            request,
                            actor,
                            BuildReportAccessQueryContext(context),
                            governanceCaller,
                            context.RequestAborted)
                        .ConfigureAwait(false),
                    jsonOptions,
                    statusCode: StatusCodes.Status201Created);
            }
            catch (ReportingRunReadinessBlockedException ex)
            {
                return Results.Json(ex.Readiness, jsonOptions, statusCode: StatusCodes.Status409Conflict);
            }
            catch (ReportingRunDeploymentNotReadyException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status403Forbidden);
            }
            catch (ReportingAuthoritativeSourceUnavailableException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
            }
            catch (ReportingGovernancePersistenceException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
            }
            catch (ReportingGovernanceAuthorizationException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status403Forbidden);
            }
            catch (ReportingGovernanceConcurrencyException ex)
            {
                return ApiProblemDetails.VersionConflict(context, ex.Message);
            }
            catch (ReportingGovernanceException ex)
            {
                return ApiProblemDetails.Conflict(context, ex.Message);
            }
            catch (ReportingArtifactCatalogIntegrityException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
            }
            catch (IOException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
            }
            catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException or InvalidOperationException or KeyNotFoundException)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        })
        .WithName("RunReportingNow").RequireAnyPermission(UserPermission.ManageReporting, UserPermission.AdminMaintenance)
        .Accepts<ReportingRunRequestDto>("application/json")
        .Produces<ReportingRunResultDto>(StatusCodes.Status201Created)
        .Produces<ReportingRunReadinessDto>(StatusCodes.Status409Conflict)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        reportingGroup.MapGet("/runs/{runId}/audit", (
            string runId,
            HttpContext context) =>
        {
            if (!HasReportingReadPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var orchestration = context.RequestServices.GetService<IReportingOrchestrationService>();
            if (orchestration is null)
            {
                return WorkspaceServiceUnavailable();
            }

            try
            {
                var accessContext = BuildReportAccessQueryContext(context);
                if (accessContext.RequireBoundScope != true
                    || string.IsNullOrWhiteSpace(accessContext.TenantId)
                    || string.IsNullOrWhiteSpace(accessContext.CompanyId))
                {
                    throw new UnauthorizedAccessException(
                        "Reporting run audit access requires a bound tenant and company scope.");
                }

                var manifest = orchestration.GetManifest(
                        accessContext.TenantId,
                        runId.Trim())
                    ?? orchestration.GetManifest(runId.Trim())
                    ?? throw new KeyNotFoundException($"Reporting run '{runId}' was not found.");
                var evaluation = ReportAccessPolicyEvaluator.Evaluate(
                    manifest,
                    accessContext);
                if (!evaluation.IsAccessible)
                {
                    throw new UnauthorizedAccessException(evaluation.Reason);
                }

                return Results.Json(
                    ProjectReportingRunAuditTrail(
                        manifest,
                        orchestration.GetAudit(
                            manifest.OperationalScope?.TenantId
                                ?? accessContext.TenantId,
                            manifest.RunId)),
                    jsonOptions);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status403Forbidden);
            }
            catch (KeyNotFoundException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status404NotFound);
            }
            catch (ArgumentException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        })
        .WithName("GetReportingRunAuditTrail").RequireAnyPermission(UserPermission.ViewReporting, UserPermission.ManageReporting, UserPermission.ApproveReporting, UserPermission.DeliverReporting, UserPermission.AdminMaintenance)
        .Produces<ReportingRunAuditTrailDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound);

        reportingGroup.MapGet("/runs/{runId}/report-writer-grids/{gridId}", (
            string runId,
            string gridId,
            string? format,
            HttpContext context) =>
        {
            if (!HasReportingReadPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var svc = context.RequestServices.GetService<ReportWriterGridArtifactService>();
            if (svc is null)
            {
                return WorkspaceServiceUnavailable();
            }

            try
            {
                var artifact = svc.GetArtifact(runId, gridId, format, jsonOptions, BuildReportAccessQueryContext(context));
                if (string.Equals(artifact.ContentType, "application/json", StringComparison.OrdinalIgnoreCase)
                    && string.IsNullOrWhiteSpace(format))
                {
                    return Results.Json(svc.GetGrid(runId, gridId, BuildReportAccessQueryContext(context)), jsonOptions);
                }

                return Results.File(artifact.Content, artifact.ContentType, artifact.FileName);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status403Forbidden);
            }
            catch (KeyNotFoundException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status404NotFound);
            }
            catch (ArgumentException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        })
        .WithName("GetReportingRunReportWriterGrid").RequireAnyPermission(UserPermission.ViewReporting, UserPermission.ManageReporting, UserPermission.ApproveReporting, UserPermission.DeliverReporting, UserPermission.AdminMaintenance)
        .Produces<ReportWriterGridRenderDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status200OK, contentType: "application/json")
        .Produces(StatusCodes.Status200OK, contentType: "text/csv")
        .Produces(StatusCodes.Status200OK, contentType: "application/pdf")
        .Produces(StatusCodes.Status200OK, contentType: StructuredXlsxContentType)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound);

        reportingGroup.MapGet("/starter-kits", (HttpContext context) =>
        {
            if (!HasReportingReadPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var svc = context.RequestServices.GetService<ReportingStarterKitService>();
            return svc is null ? WorkspaceServiceUnavailable() : Results.Json(svc.ListKits(), jsonOptions);
        })
        .WithName("ListReportingStarterKits").RequireAnyPermission(UserPermission.ViewReporting, UserPermission.ManageReporting, UserPermission.ApproveReporting, UserPermission.DeliverReporting, UserPermission.AdminMaintenance)
        .Produces<IReadOnlyList<ReportingStarterKitDto>>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status403Forbidden);

        reportingGroup.MapPost("/starter-kits/{kitId}/provision", (string kitId, HttpContext context) =>
        {
            if (!HasReportingWorkflowPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            if (!EndpointAuthorization.TryResolveActor(context, out var actor))
            {
                return Results.Unauthorized();
            }

            if (context.RequestServices.GetService<IReportingStarterKitStore>() is null)
            {
                return ReportingMutationStoreUnavailable("Reporting starter-kit provisioning");
            }

            var svc = context.RequestServices.GetService<ReportingStarterKitService>();
            if (svc is null)
            {
                return WorkspaceServiceUnavailable();
            }

            try
            {
                return Results.Json(
                    svc.Provision(kitId, actor, BuildReportAccessQueryContext(context)),
                    jsonOptions,
                    statusCode: StatusCodes.Status201Created);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status403Forbidden);
            }
            catch (KeyNotFoundException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status404NotFound);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        })
        .WithName("ProvisionReportingStarterKit").RequireAnyPermission(UserPermission.ManageReporting, UserPermission.AdminMaintenance)
        .Produces<ReportingStarterKitProvisionResultDto>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status503ServiceUnavailable);

        reportingGroup.MapGet("/schedules", (HttpContext context) =>
        {
            if (!HasReportingReadPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var svc = context.RequestServices.GetService<ReportingScheduleService>();
            return svc is null
                ? WorkspaceServiceUnavailable()
                : Results.Json(svc.ListSchedules(BuildReportAccessQueryContext(context)), jsonOptions);
        })
        .WithName("ListReportingSchedules").RequireAnyPermission(UserPermission.ViewReporting, UserPermission.ManageReporting, UserPermission.ApproveReporting, UserPermission.DeliverReporting, UserPermission.AdminMaintenance)
        .Produces<IReadOnlyList<ReportingScheduleRecordDto>>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status403Forbidden);

        reportingGroup.MapPost("/schedules", async Task<IResult> (ReportingScheduleUpsertRequestDto request, HttpContext context) =>
        {
            if (!HasReportingWorkflowPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            if (RequireReadyReportingDeployment(context) is { } deploymentFailure)
            {
                return deploymentFailure;
            }

            if (!EndpointAuthorization.TryResolveActor(context, out var actor))
            {
                return Results.Unauthorized();
            }

            var svc = context.RequestServices.GetService<ReportingScheduleService>();
            if (svc is null)
            {
                return WorkspaceServiceUnavailable();
            }

            try
            {
                return Results.Json(
                    await svc.UpsertAsync(
                            request with { RequestedBy = actor },
                            BuildReportAccessQueryContext(context),
                            context.RequestAborted)
                        .ConfigureAwait(false),
                    jsonOptions,
                    statusCode: StatusCodes.Status201Created);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status403Forbidden);
            }
            catch (Exception ex) when (ex is ArgumentException
                                           or ArgumentOutOfRangeException
                                           or InvalidDataException
                                           or InvalidOperationException)
            {
                return ReportingScheduleFailure(context, ex);
            }
        })
        .WithName("UpsertReportingSchedule").RequireAnyPermission(UserPermission.ManageReporting, UserPermission.AdminMaintenance)
        .Accepts<ReportingScheduleUpsertRequestDto>("application/json")
        .Produces<ReportingScheduleRecordDto>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        reportingGroup.MapPost("/schedules/{scheduleId}/pause", (string scheduleId, HttpContext context) => SetScheduleState(context, scheduleId, ReportingScheduleStateDto.Paused))
            .WithName("PauseReportingSchedule").RequireAnyPermission(UserPermission.ManageReporting, UserPermission.AdminMaintenance)
            .Produces<ReportingScheduleRecordDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        reportingGroup.MapPost("/schedules/{scheduleId}/resume", (string scheduleId, HttpContext context) => SetScheduleState(context, scheduleId, ReportingScheduleStateDto.Active))
            .WithName("ResumeReportingSchedule").RequireAnyPermission(UserPermission.ManageReporting, UserPermission.AdminMaintenance)
            .Produces<ReportingScheduleRecordDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        reportingGroup.MapPost("/schedules/{scheduleId}/run", async (string scheduleId, HttpContext context) =>
        {
            if (!HasReportingWorkflowPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            if (RequireReadyReportingDeployment(context) is { } deploymentFailure)
            {
                return deploymentFailure;
            }

            if (!EndpointAuthorization.TryResolveActor(context, out var actor))
            {
                return Results.Unauthorized();
            }

            var svc = context.RequestServices.GetService<ReportingScheduleService>();
            if (svc is null)
            {
                return WorkspaceServiceUnavailable();
            }

            try
            {
                return Results.Json(
                    await svc.RunNowAsync(scheduleId, actor, BuildReportAccessQueryContext(context), context.RequestAborted).ConfigureAwait(false),
                    jsonOptions);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status403Forbidden);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or KeyNotFoundException)
            {
                return ReportingScheduleFailure(context, ex);
            }
        })
        .WithName("RunReportingScheduleNow").RequireAnyPermission(UserPermission.ManageReporting, UserPermission.AdminMaintenance)
        .Produces<ReportingScheduleRunResultDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        reportingGroup.MapPost("/schedules/run-due", (HttpContext context) =>
            LegacyReportingRouteGone(
                context,
                "the internal reporting scheduler worker",
                "Public due-schedule execution is retired; due work is leased and executed only by the server-owned background worker."))
        .WithName("RunDueReportingSchedules")
        .ProducesProblem(StatusCodes.Status410Gone);

        reportingGroup.MapGet("/packs/history", (string period, string fundAccountId, HttpContext context) =>
        {
            if (!HasReportingReadPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            if (context.RequestServices.GetService<IGovernanceReportPackRepository>() is null)
            {
                return LegacyReportingRouteGone(
                    context,
                    "/api/fund-structure/reporting/runs",
                    "Legacy file-backed report-pack history is not available in this deployment.");
            }

            var svc = context.RequestServices.GetService<ReportPackWorkflowService>();
            if (svc is null)
            {
                return WorkspaceServiceUnavailable();
            }

            var accessContext = BuildReportAccessQueryContext(context);
            var history = svc
                .GetHistory(period, fundAccountId)
                .Where(record => IsTenantSafeLegacyReportingPackRead(record, accessContext))
                .ToArray();
            return Results.Json(history, jsonOptions);
        }).RequireAnyPermission(UserPermission.ViewReporting, UserPermission.ManageReporting, UserPermission.ApproveReporting, UserPermission.DeliverReporting, UserPermission.AdminMaintenance)
        .Produces<IReadOnlyList<ReportPackWorkflowRecordDto>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status410Gone);
        legacyReportingGroup.MapGet("/report-packs/{reportId:guid}", async (Guid reportId, HttpContext context) =>
        {
            if (!HasReportingReadPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            if (context.RequestServices.GetService<IGovernanceReportPackRepository>() is null)
            {
                return LegacyReportingRouteGone(
                    context,
                    "/api/fund-structure/reporting/runs",
                    "Legacy file-backed report-pack detail is not available in this deployment.");
            }

            var service = ResolveWorkspaceService(context);
            if (service is null)
            {
                return WorkspaceServiceUnavailable();
            }

            var result = await service.GetReportPackAsync(reportId, context.RequestAborted).ConfigureAwait(false);
            if (result is null)
            {
                return Results.NotFound();
            }

            if (await RequireReportingFundProfileTenantAccessAsync(context, result.FundProfileId).ConfigureAwait(false) is { } scopeFailure)
            {
                return scopeFailure;
            }

            return Results.Json(result, jsonOptions);
        })
        .WithName("GetFundReportPack").RequireAnyPermission(UserPermission.ViewReporting, UserPermission.ManageReporting, UserPermission.ApproveReporting, UserPermission.DeliverReporting, UserPermission.AdminMaintenance)
        .Produces<FundReportPackSnapshotDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status410Gone);

        legacyReportingGroup.MapGet("/report-packs/{reportId:guid}/evidence-bundle", (HttpContext context) =>
            LegacyReportingRouteGone(
                context,
                "/api/fund-structure/reporting/distribution/packages/{runId}/artifacts/{artifactId}",
                "Legacy evidence export regenerated a mutable bundle instead of returning verified bytes from the immutable artifact vault."))
        .WithName("ExportFundReportPackEvidenceBundle").RequireAnyPermission(UserPermission.ViewReporting, UserPermission.ManageReporting, UserPermission.ApproveReporting, UserPermission.DeliverReporting, UserPermission.AdminMaintenance)
        .ProducesProblem(StatusCodes.Status410Gone);

        legacyReportingGroup.MapGet("/report-packs/{reportId:guid}/ledger-provenance", async (Guid reportId, string scopeKey, HttpContext context) =>
        {
            if (!HasReportingReadPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            if (context.RequestServices.GetService<IGovernanceReportPackRepository>() is null)
            {
                return LegacyReportingRouteGone(
                    context,
                    "/api/fund-structure/reporting/runs",
                    "Legacy file-backed report-pack provenance is not available in this deployment.");
            }

            var service = context.RequestServices.GetService<LedgerAmountProvenanceService>();
            var workspace = ResolveWorkspaceService(context);
            if (service is null || workspace is null)
            {
                return WorkspaceServiceUnavailable();
            }

            var pack = await workspace.GetReportPackAsync(reportId, context.RequestAborted).ConfigureAwait(false);
            if (pack is null)
            {
                return Results.NotFound();
            }

            if (await RequireReportingFundProfileTenantAccessAsync(context, pack.FundProfileId).ConfigureAwait(false) is { } scopeFailure)
            {
                return scopeFailure;
            }

            var tenantContext = HttpContextWorkstationTenantContextAccessor.Resolve(context);
            var accessScope = new ReconciliationBreakQueueScope(
                tenantContext.TenantId!,
                tenantContext.CompanyId!);
            var result = await service
                .GetAsync(reportId, scopeKey, accessScope, context.RequestAborted)
                .ConfigureAwait(false);
            return result is null ? Results.NotFound() : Results.Json(result, jsonOptions);
        })
        .WithName("GetFundReportPackLedgerProvenance").RequireAnyPermission(UserPermission.ViewReporting, UserPermission.ManageReporting, UserPermission.ApproveReporting, UserPermission.DeliverReporting, UserPermission.AdminMaintenance)
        .Produces<LedgerAmountProvenanceDetailDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status410Gone)
        .RequireWorkstationTenantCompanyScope();

        group.MapPost("/accounting/transaction-lab/preview", (InvestmentAccountingTransactionLabRequestDto request, HttpContext context) =>
        {
            if (!HasReportingWorkflowPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var service = context.RequestServices.GetService<InvestmentAccountingTransactionLabService>();
            if (service is null)
            {
                return Results.Problem("Investment Accounting Transaction Lab service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            try
            {
                return Results.Json(service.Preview(request), jsonOptions);
            }
            catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        })
        .WithName("PreviewInvestmentAccountingTransactionLab").RequireAnyPermission(UserPermission.ManageReporting, UserPermission.AdminMaintenance)
        .Produces<InvestmentAccountingTransactionLabPreviewDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden);
    }

    private static FundStructureSetupWorkflowService? ResolveSetupWorkflow(HttpContext context)
        => context.RequestServices.GetService<FundStructureSetupWorkflowService>()
           ?? (ResolveService(context) is { } service ? new FundStructureSetupWorkflowService(service) : null);

    private static IFundStructureService? ResolveService(HttpContext context) =>
        context.RequestServices.GetService<IFundStructureService>();

    private static FundOperationsWorkspaceReadService? ResolveWorkspaceService(HttpContext context) =>
        context.RequestServices.GetService<FundOperationsWorkspaceReadService>();

    private static bool HasFundStructureSetupCreatePermission(HttpContext context)
        => EndpointAuthorization.HasAnyPermission(context, UserPermission.ManageDirectLending, UserPermission.AdminMaintenance);

    private static bool HasLedgerMappingAssignmentPermission(HttpContext context)
        => EndpointAuthorization.HasAnyPermission(
            context,
            UserPermission.ManageFundStructure,
            UserPermission.ManageDirectLending,
            UserPermission.AdminMaintenance);

    private static bool HasReportingWorkflowPermission(HttpContext context)
        => EndpointAuthorization.HasAnyPermission(context, UserPermission.ManageReporting, UserPermission.AdminMaintenance);

    private static bool HasReportingApprovalPermission(HttpContext context)
        => EndpointAuthorization.HasAnyPermission(context, UserPermission.ApproveReporting, UserPermission.AdminMaintenance);

    private static bool HasReportingReadPermission(HttpContext context)
        => EndpointAuthorization.HasAnyPermission(
            context,
            UserPermission.ViewReporting,
            UserPermission.ManageReporting,
            UserPermission.ApproveReporting,
            UserPermission.DeliverReporting,
            UserPermission.AdminMaintenance);

    private static Task<IResult?> RequireScopedFundStructureGraphReadAccessAsync(
        OrganizationStructureQuery query,
        HttpContext context)
        => RequireScopedFundStructureReadAccessAsync(
            context,
            MapFundStructureNodeScope(query.NodeKind, query.NodeId),
            (AccessScopeKindDto.Business, query.BusinessId),
            (AccessScopeKindDto.Organization, query.OrganizationId));

    private static Task<IResult?> RequireScopedFundStructureGraphReadAccessAsync(
        FundStructureQuery query,
        HttpContext context)
        => RequireScopedFundStructureReadAccessAsync(
            context,
            MapFundStructureNodeScope(query.NodeKind, query.NodeId),
            (AccessScopeKindDto.Fund, query.FundId));

    private static Task<IResult?> RequireScopedAccountingStructureReadAccessAsync(
        AccountingStructureQuery query,
        HttpContext context)
        => RequireScopedFundStructureReadAccessAsync(
            context,
            (AccessScopeKindDto.InvestmentPortfolio, query.InvestmentPortfolioId),
            (AccessScopeKindDto.Vehicle, query.VehicleId),
            (AccessScopeKindDto.Sleeve, query.SleeveId),
            (AccessScopeKindDto.Fund, query.FundId),
            (AccessScopeKindDto.Client, query.ClientId),
            (AccessScopeKindDto.Business, query.BusinessId),
            (AccessScopeKindDto.Organization, query.OrganizationId));

    private static Task<IResult?> RequireScopedGovernanceCashFlowReadAccessAsync(
        GovernanceCashFlowQuery query,
        HttpContext context)
        => RequireScopedFundStructureReadAccessAsync(
            context,
            (AccessScopeKindDto.Account, query.AccountId),
            (AccessScopeKindDto.InvestmentPortfolio, query.InvestmentPortfolioId),
            (AccessScopeKindDto.Vehicle, query.VehicleId),
            (AccessScopeKindDto.Sleeve, query.SleeveId),
            (AccessScopeKindDto.Fund, query.FundId),
            (AccessScopeKindDto.Client, query.ClientId),
            (AccessScopeKindDto.Business, query.BusinessId),
            (AccessScopeKindDto.Organization, query.OrganizationId));

    private static async Task<IResult?> RequireScopedFundStructureReadAccessAsync(
        HttpContext context,
        params (AccessScopeKindDto Kind, Guid? Id)[] orderedScopes)
    {
        if (!EndpointAuthorization.TryGetPermissions(context, out _))
        {
            return Results.Unauthorized();
        }

        if (EndpointAuthorization.HasPermission(context, UserPermission.AdminMaintenance))
        {
            return null;
        }

        var scope = orderedScopes.FirstOrDefault(static candidate => candidate.Id.HasValue);
        if (!scope.Id.HasValue)
        {
            return EndpointHelpers.Forbidden();
        }

        var allowed = await EndpointAuthorization.HasScopedPermissionAsync(
            context,
            UserPermission.ManageFundStructure,
            scope.Kind,
            scope.Id.Value,
            context.RequestAborted).ConfigureAwait(false);

        return allowed ? null : EndpointHelpers.Forbidden();
    }

    private static (AccessScopeKindDto Kind, Guid? Id) MapFundStructureNodeScope(
        FundStructureNodeKindDto? nodeKind,
        Guid? nodeId)
        => nodeKind switch
        {
            FundStructureNodeKindDto.Organization => (AccessScopeKindDto.Organization, nodeId),
            FundStructureNodeKindDto.Business => (AccessScopeKindDto.Business, nodeId),
            FundStructureNodeKindDto.Client => (AccessScopeKindDto.Client, nodeId),
            FundStructureNodeKindDto.Fund => (AccessScopeKindDto.Fund, nodeId),
            FundStructureNodeKindDto.Sleeve => (AccessScopeKindDto.Sleeve, nodeId),
            FundStructureNodeKindDto.Vehicle => (AccessScopeKindDto.Vehicle, nodeId),
            FundStructureNodeKindDto.InvestmentPortfolio => (AccessScopeKindDto.InvestmentPortfolio, nodeId),
            FundStructureNodeKindDto.Entity => (AccessScopeKindDto.LegalEntity, nodeId),
            FundStructureNodeKindDto.Account => (AccessScopeKindDto.Account, nodeId),
            _ => (AccessScopeKindDto.Global, null)
        };

    private static bool IsTenantSafeLegacyReportingPackRead(
        ReportPackWorkflowRecordDto record,
        ReportAccessQueryContext accessContext) =>
        !string.IsNullOrWhiteSpace(record.TenantId)
        && !string.IsNullOrWhiteSpace(record.CompanyId)
        && !string.IsNullOrWhiteSpace(record.AccessPolicySnapshotHash)
        && string.Equals(record.TenantId, accessContext.TenantId, StringComparison.Ordinal)
        && string.Equals(record.CompanyId, accessContext.CompanyId, StringComparison.Ordinal)
        && ReportAccessPolicyEvaluator.Evaluate(record.AccessPolicy, accessContext).IsAccessible;

    private static async Task<IResult?> RequireReportingFundProfileTenantAccessAsync(
        HttpContext context,
        string fundProfileId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fundProfileId);
        var tenant = HttpContextWorkstationTenantContextAccessor.Resolve(context);
        if (!tenant.HasTenantScope || string.IsNullOrWhiteSpace(tenant.CompanyId))
        {
            return Results.Problem(
                "A tenant and company scoped session is required for reporting.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        var guard = context.RequestServices.GetService<IFundProfileTenantGuard>();
        if (guard is null)
        {
            return Results.Problem(
                "Authoritative fund-profile tenancy validation is unavailable; reporting access is blocked.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        var decision = await guard
            .EvaluateAsync(tenant, fundProfileId.Trim(), context.RequestAborted)
            .ConfigureAwait(false);
        return decision.IsAllowed
            ? null
            : Results.Problem(
                "The requested fund profile is not accessible to the current tenant.",
                statusCode: StatusCodes.Status403Forbidden);
    }

    private static ReportingRunAuditTrailDto ProjectReportingRunAuditTrail(
        ReportingOutputManifest manifest,
        IReadOnlyList<ReportingRunAuditEntry> auditTrail) =>
        new(
            manifest.RunId,
            manifest.TemplateId,
            manifest.AsOfDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            manifest.Status.ToString(),
            manifest.Trigger.ToString(),
            manifest.AttemptCount,
            auditTrail
                .OrderBy(static entry => entry.TimestampUtc)
                .Select(static entry => new ReportingRunAuditEntryDto(
                    entry.RunId,
                    entry.TimestampUtc,
                    entry.Action,
                    entry.Actor,
                    entry.Notes))
                .ToArray(),
            manifest.ReportWriterDatasetSourceId,
            manifest.ReportWriterDatasetSourceLabel,
            manifest.ReportWriterDatasetRowCount);

    private static ReportAccessQueryContext BuildReportAccessQueryContext(HttpContext context)
    {
        var actor = EndpointAuthorization.TryResolveActor(context, out var resolvedActor)
            ? resolvedActor
            : null;
        var tenant = HttpContextWorkstationTenantContextAccessor.Resolve(context);
        return new ReportAccessQueryContext(
            ActorPrincipalId: actor,
            GroupPrincipalIds: EndpointAuthorization.ResolveReportGroupPrincipalIds(context),
            CompanyId: EndpointAuthorization.ResolveCompanyId(context),
            HasGlobalOverride: EndpointAuthorization.HasPermission(context, UserPermission.AdminMaintenance),
            TenantId: tenant.TenantId,
            RequireBoundScope: true);
    }

    private static void ApplyStructuredExportAuditHeaders(
        HttpContext context,
        StructuredReportingExportPayloadDto payload)
    {
        context.Response.Headers["X-Meridian-Export-Id"] = payload.Export.ExportId;
        context.Response.Headers["X-Meridian-Export-Generated-At"] = payload.GeneratedAtUtc.ToString("O");
        if (!string.IsNullOrWhiteSpace(payload.GeneratedByPrincipalId))
        {
            context.Response.Headers["X-Meridian-Export-Generated-By"] = payload.GeneratedByPrincipalId;
        }

        if (!string.IsNullOrWhiteSpace(payload.GeneratedForCompanyId))
        {
            context.Response.Headers["X-Meridian-Export-Company"] = payload.GeneratedForCompanyId;
        }

        if (payload.GeneratedForGroupPrincipalIds is { Count: > 0 })
        {
            context.Response.Headers["X-Meridian-Export-Groups"] = string.Join(",", payload.GeneratedForGroupPrincipalIds);
        }

        if (!string.IsNullOrWhiteSpace(payload.Export.VersionStamp))
        {
            context.Response.Headers["X-Meridian-Export-Version"] = payload.Export.VersionStamp;
        }

        if (!string.IsNullOrWhiteSpace(payload.Export.IntegrityHashSha256))
        {
            context.Response.Headers["X-Meridian-Export-Sha256"] = payload.Export.IntegrityHashSha256;
        }
    }

    private static IResult ServiceUnavailable() =>
        Results.Problem("Fund structure service is not registered.", statusCode: StatusCodes.Status501NotImplemented);

    private static IResult WorkspaceServiceUnavailable() =>
        Results.Problem("Fund operations workspace service is not registered.", statusCode: StatusCodes.Status501NotImplemented);

    private static IResult ReportingMutationStoreUnavailable(string capability) =>
        Results.Problem(
            detail: $"{capability} requires a registered persistence store and is unavailable in this deployment.",
            statusCode: StatusCodes.Status503ServiceUnavailable,
            title: "Durable reporting mutation unavailable");

    private static IResult LegacyReportingRouteGone(
        HttpContext context,
        string canonicalRoute,
        string reason)
    {
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers.Pragma = "no-cache";
        return Results.Problem(
            detail: $"{reason} Use {canonicalRoute}.",
            statusCode: StatusCodes.Status410Gone,
            title: "Legacy reporting route retired");
    }

    private static Guid? ParseGuid(string? value) =>
        Guid.TryParse(value, out var parsed) ? parsed : null;

    private static DateTimeOffset? ParseDateTimeOffset(string? value) =>
        DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;

    private static bool ParseActiveOnly(string? value) =>
        !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase);

    private static FundStructureNodeKindDto? ParseNodeKind(string? value) =>
        Enum.TryParse<FundStructureNodeKindDto>(value, ignoreCase: true, out var parsed) ? parsed : null;

    private static GovernanceCashFlowScopeKindDto? ParseCashFlowScopeKind(string? value) =>
        Enum.TryParse<GovernanceCashFlowScopeKindDto>(value, ignoreCase: true, out var parsed) ? parsed : null;

    private static FundLedgerScope? ParseFundLedgerScope(string? value) =>
        Enum.TryParse<FundLedgerScope>(value, ignoreCase: true, out var parsed) ? parsed : null;

    private static LedgerGroupId? ParseLedgerGroupId(StringValues values, out string? error)
    {
        error = null;
        if (StringValues.IsNullOrEmpty(values))
        {
            return null;
        }

        var raw = values.ToString();
        if (!LedgerGroupId.TryCreate(raw, out var parsed))
        {
            error = $"ledgerGroupId is invalid. {LedgerGroupId.ValidationMessage}";
            return null;
        }

        return parsed;
    }

    private static bool HasExplicitCashFlowScope(IQueryCollection query) =>
        HasAnyQueryValue(
            query,
            "organizationId",
            "businessId",
            "clientId",
            "fundId",
            "sleeveId",
            "vehicleId",
            "investmentPortfolioId",
            "accountId");

    private static bool HasAnyQueryValue(IQueryCollection query, params string[] keys) =>
        keys.Any(key => query.TryGetValue(key, out var values) && values.Any(static value => !string.IsNullOrWhiteSpace(value)));

    private static GovernanceCashFlowViewDto? TryCreateEmptyUnassignedLedgerGroupCashFlowView(
        GovernanceCashFlowScopeKindDto scopeKind,
        LedgerGroupId? ledgerGroupId,
        IQueryCollection query)
    {
        if (scopeKind != GovernanceCashFlowScopeKindDto.LedgerGroup
            || ledgerGroupId != LedgerGroupId.Unassigned
            || HasExplicitCashFlowScope(query))
        {
            return null;
        }

        return CreateEmptyUnassignedLedgerGroupCashFlowView(query);
    }

    private static GovernanceCashFlowViewDto CreateEmptyUnassignedLedgerGroupCashFlowView(IQueryCollection query)
    {
        var asOf = ParseDateTimeOffset(query["asOf"]) ?? DateTimeOffset.UtcNow;
        var historicalDays = Math.Max(1, ParseInt(query["historicalDays"], 7));
        var forecastDays = Math.Max(1, ParseInt(query["forecastDays"], 7));
        var bucketDays = Math.Max(1, ParseInt(query["bucketDays"], 7));
        var currency = string.IsNullOrWhiteSpace(query["currency"].FirstOrDefault())
            ? "USD"
            : query["currency"].FirstOrDefault()!;
        var windowAnchor = StartOfDayUtc(asOf);
        var historicalWindowStart = windowAnchor.AddDays(-(historicalDays - 1));
        var projectionWindowStart = windowAnchor.AddDays(1);
        var projectionWindowEnd = projectionWindowStart.AddDays(forecastDays);
        var scope = new GovernanceCashFlowScopeDto(
            GovernanceCashFlowScopeKindDto.LedgerGroup,
            LedgerGroupId.Unassigned.Value,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            LedgerGroupId.Unassigned,
            Array.Empty<Guid>(),
            Array.Empty<Guid>());

        return new GovernanceCashFlowViewDto(
            scope,
            asOf,
            historicalWindowStart,
            projectionWindowEnd,
            currency,
            historicalDays,
            forecastDays,
            bucketDays,
            0,
            0m,
            0m,
            Array.Empty<GovernanceCashFlowAccountViewDto>(),
            Array.Empty<GovernanceCashFlowEntryDto>(),
            Array.Empty<GovernanceCashFlowEntryDto>(),
            CreateEmptyCashFlowLadder(historicalWindowStart, historicalDays, currency, bucketDays),
            CreateEmptyCashFlowLadder(projectionWindowStart, forecastDays, currency, bucketDays),
            new GovernanceCashFlowVarianceSummaryDto(0m, 0m, 0m, 0m, 0m, 0m, 0m, "No accounts in scope."),
            Array.Empty<GovernanceCashFlowVarianceBucketDto>());
    }

    private static GovernanceCashFlowLadderDto CreateEmptyCashFlowLadder(
        DateTimeOffset anchor,
        int windowDays,
        string currency,
        int bucketDays)
    {
        var effectiveBucketDays = Math.Max(1, bucketDays);
        var effectiveWindowDays = Math.Max(1, windowDays);
        var windowEnd = anchor.AddDays(effectiveWindowDays);
        var bucketCount = Math.Max(1, (int)Math.Ceiling(effectiveWindowDays / (double)effectiveBucketDays));
        var buckets = new List<GovernanceCashFlowBucketDto>(bucketCount);

        for (var bucketIndex = 0; bucketIndex < bucketCount; bucketIndex++)
        {
            var bucketStart = anchor.AddDays(bucketIndex * effectiveBucketDays);
            var bucketEnd = bucketStart.AddDays(Math.Min(effectiveBucketDays, effectiveWindowDays - (bucketIndex * effectiveBucketDays)));
            buckets.Add(new GovernanceCashFlowBucketDto(
                bucketIndex,
                bucketStart,
                bucketEnd,
                0m,
                0m,
                0m,
                currency,
                0));
        }

        return new GovernanceCashFlowLadderDto(
            anchor,
            windowEnd,
            currency,
            effectiveBucketDays,
            0m,
            0m,
            0m,
            buckets);
    }

    private static DateTimeOffset StartOfDayUtc(DateTimeOffset value) =>
        new(value.UtcDateTime.Date, TimeSpan.Zero);

    private static IReadOnlyList<string>? ParseSelectedLedgerIds(params StringValues[] valueSets)
    {
        var parsed = valueSets
            .SelectMany(static values => values)
            .Where(static value => value is not null)
            .SelectMany(static value => value!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return parsed.Length == 0 ? null : parsed;
    }

    private static bool TryValidateLedgerMappingAssignmentRequest(
        LedgerMappingAssignmentRequestDto? request,
        out string error)
    {
        if (request is null)
        {
            error = "A request body is required.";
            return false;
        }

        if (request.AccountId == Guid.Empty)
        {
            error = "accountId is required.";
            return false;
        }

        if (!LedgerGroupId.TryCreate(request.LedgerGroupId, out var ledgerGroupId)
            || ledgerGroupId == LedgerGroupId.Unassigned)
        {
            error = $"ledgerGroupId is invalid. {LedgerGroupId.ValidationMessage}";
            return false;
        }

        if (string.IsNullOrWhiteSpace(request.RequestedBy))
        {
            error = "requestedBy is required for ledger mapping audit evidence.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(request.Rationale))
        {
            error = "rationale is required for ledger mapping audit evidence.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static AssignFundStructureNodeRequest NormalizeLedgerGroupAssignmentRequest(
        AssignFundStructureNodeRequest request,
        out string? error)
    {
        error = null;
        if (!LedgerGroupingRules.IsLedgerGroupAssignmentType(request.AssignmentType))
        {
            return request;
        }

        try
        {
            return request with
            {
                AssignmentReference = LedgerGroupingRules.NormalizeAssignmentReference(
                    request.AssignmentType,
                    request.AssignmentReference)
            };
        }
        catch (FormatException)
        {
            error = $"assignmentReference is invalid for '{LedgerGroupingRules.LedgerGroupAssignmentType}'. {LedgerGroupId.ValidationMessage}";
            return request;
        }
    }

    private static int ParseInt(string? value, int defaultValue) =>
        int.TryParse(value, out var parsed) ? parsed : defaultValue;
}
