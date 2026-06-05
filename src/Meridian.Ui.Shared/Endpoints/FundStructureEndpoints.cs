using System.Text.Json;
using Meridian.Identity.Auth;
using Meridian.Application.FundStructure;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;

namespace Meridian.Ui.Shared.Endpoints;

public static class FundStructureEndpoints
{
    public static void MapFundStructureEndpoints(this WebApplication app, JsonSerializerOptions jsonOptions)
    {
        var group = app.MapGroup("/api/fund-structure").WithTags("Fund Structure");


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
        .WithName("ValidateFundStructureSetupDraft")
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
        .WithName("CreateFundStructureSetupDraft")
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
        .WithName("CreateOrganization")
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
        .WithName("CreateBusiness")
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
        .WithName("CreateClient")
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
        .WithName("CreateStructureFund")
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
        .WithName("CreateSleeve")
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
        .WithName("CreateVehicle")
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
        .WithName("CreateLegalEntity")
        .Produces<LegalEntitySummaryDto>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status400BadRequest);

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
        .WithName("CreateInvestmentPortfolio")
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
        .WithName("LinkFundStructureNodes")
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
        .AddEndpointFilter(EndpointAuthorization.Require(UserPermission.ManageFundStructure))
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
        .AddEndpointFilter(EndpointAuthorization.Require(UserPermission.ManageFundStructure))
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
        .AddEndpointFilter(EndpointAuthorization.Require(UserPermission.ManageFundStructure))
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
        .WithName("ValidateOwnershipGraph")
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
        .WithName("AssignFundStructureNode")
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
        .WithName("AssignLedgerMapping")
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

            var result = await service.GetOrganizationStructureAsync(query, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(result, jsonOptions);
        })
        .WithName("GetOrganizationStructureGraph")
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

            var result = await service.GetFundStructureGraphAsync(query, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(result, jsonOptions);
        })
        .WithName("GetLegacyFundStructureGraph")
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

            var result = await service.GetAdvisoryViewAsync(query, context.RequestAborted).ConfigureAwait(false);
            return result is null ? Results.NotFound() : Results.Json(result, jsonOptions);
        })
        .WithName("GetAdvisoryStructureView")
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

            var result = await service.GetFundOperatingViewAsync(query, context.RequestAborted).ConfigureAwait(false);
            return result is null ? Results.NotFound() : Results.Json(result, jsonOptions);
        })
        .WithName("GetFundOperatingStructureView")
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

            var result = await service.GetAccountingViewAsync(query, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(result, jsonOptions);
        })
        .WithName("GetAccountingStructureView")
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

            var accountingView = await service.GetAccountingViewAsync(query, context.RequestAborted).ConfigureAwait(false);
            var result = LedgerMappingWorkbenchService.Build(accountingView, asOf);
            return Results.Json(result, jsonOptions);
        })
        .WithName("GetLedgerMappingWorkbench")
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

            var result = await service.GetCashFlowViewAsync(query, context.RequestAborted).ConfigureAwait(false);
            return result is null ? Results.NotFound() : Results.Json(result, jsonOptions);
        })
        .WithName("GetGovernanceCashFlowView")
        .Produces<GovernanceCashFlowViewDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/workspace-view", async (HttpContext context) =>
        {
            var service = ResolveWorkspaceService(context);
            if (service is null)
            {
                return WorkspaceServiceUnavailable();
            }

            var q = context.Request.Query;
            var fundProfileId = q["fundProfileId"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(fundProfileId))
            {
                return Results.Problem(
                    "fundProfileId is required.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            var query = new FundOperationsWorkspaceQuery(
                FundProfileId: fundProfileId,
                AsOf: ParseDateTimeOffset(q["asOf"]),
                Currency: q["currency"].FirstOrDefault(),
                ScopeKind: ParseFundLedgerScope(q["scopeKind"]) ?? FundLedgerScope.Consolidated,
                ScopeId: q["scopeId"].FirstOrDefault(),
                SelectedLedgerIds: ParseSelectedLedgerIds(q["selectedLedgerIds"], q["selectedLedgerId"]));

            var result = await service.GetWorkspaceAsync(query, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(result, jsonOptions);
        })
        .WithName("GetFundOperationsWorkspaceView")
        .Produces<FundOperationsWorkspaceDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest);

        group.MapPost("/report-pack-preview", async (JsonElement body, HttpContext context) =>
        {
            var service = ResolveWorkspaceService(context);
            if (service is null)
            {
                return WorkspaceServiceUnavailable();
            }

            var request = JsonSerializer.Deserialize<FundReportPackPreviewRequestDto>(body.GetRawText(), jsonOptions);
            if (request is null || string.IsNullOrWhiteSpace(request.FundProfileId))
            {
                return Results.Problem(
                    "A request body with fundProfileId is required.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            var result = await service.PreviewReportPackAsync(request, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(result, jsonOptions);
        })
        .WithName("PreviewFundReportPack")
        .Produces<FundReportPackPreviewDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest);

        group.MapPost("/report-packs", async (JsonElement body, HttpContext context) =>
        {
            var service = ResolveWorkspaceService(context);
            if (service is null)
            {
                return WorkspaceServiceUnavailable();
            }

            FundReportPackGenerateRequestDto? request;
            try
            {
                request = JsonSerializer.Deserialize<FundReportPackGenerateRequestDto>(body.GetRawText(), jsonOptions);
            }
            catch (JsonException ex)
            {
                return Results.Problem(
                    $"Report-pack request is invalid JSON. {ex.Message}",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            if (!TryValidateReportPackGenerateRequest(request, out var validationError))
            {
                return Results.Problem(validationError, statusCode: StatusCodes.Status400BadRequest);
            }

            try
            {
                var result = await service.GenerateReportPackAsync(request!, context.RequestAborted).ConfigureAwait(false);
                return Results.Json(result, jsonOptions, statusCode: StatusCodes.Status201Created);
            }
            catch (ArgumentException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        })
        .WithName("GenerateFundReportPack")
        .Produces<FundReportPackSnapshotDto>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status400BadRequest);

        group.MapGet("/report-packs", async (HttpContext context) =>
        {
            var service = ResolveWorkspaceService(context);
            if (service is null)
            {
                return WorkspaceServiceUnavailable();
            }

            var q = context.Request.Query;
            var fundProfileId = q["fundProfileId"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(fundProfileId))
            {
                return Results.Problem(
                    "fundProfileId is required.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            var limit = ParseInt(q["limit"], 20);
            var result = await service
                .GetReportPackHistoryAsync(fundProfileId, limit, context.RequestAborted)
                .ConfigureAwait(false);
            return Results.Json(result, jsonOptions);
        })
        .WithName("GetFundReportPackHistory")
        .Produces<IReadOnlyList<FundReportPackHistoryItemDto>>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest);


        group.MapGet("/reporting/templates", (HttpContext context) =>
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
            return Results.Json(registry.List(includeSuperseded), jsonOptions);
        })
        .WithName("ListReportTemplates")
        .Produces<IReadOnlyList<ReportTemplateGovernanceRecordDto>>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status403Forbidden);

        group.MapPost("/reporting/templates", (ReportTemplateDefinitionDto request, HttpContext context) =>
        {
            if (!HasReportingWorkflowPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var registry = context.RequestServices.GetService<ReportTemplateRegistryService>();
            return registry is null ? WorkspaceServiceUnavailable() : Results.Json(registry.Register(request), jsonOptions, statusCode: StatusCodes.Status201Created);
        })
        .WithName("RegisterReportTemplate")
        .Produces<ReportTemplateDefinitionDto>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status403Forbidden);

        group.MapPost("/reporting/templates/drafts", (ReportTemplateDraftRequestDto request, HttpContext context) =>
        {
            if (!HasReportingWorkflowPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            if (!EndpointAuthorization.TryResolveActor(context, out var actor))
            {
                return Results.Unauthorized();
            }

            var registry = context.RequestServices.GetService<ReportTemplateRegistryService>();
            if (registry is null)
            {
                return WorkspaceServiceUnavailable();
            }

            try
            {
                return Results.Json(registry.CreateDraft(request, actor), jsonOptions, statusCode: StatusCodes.Status201Created);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        })
        .WithName("CreateReportTemplateDraft")
        .Produces<ReportTemplateGovernanceRecordDto>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden);

        group.MapPost("/reporting/templates/{templateName}/versions/{version:int}/submit", (string templateName, int version, ReportTemplateDecisionRequestDto? request, HttpContext context) =>
        {
            if (!HasReportingWorkflowPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            if (!EndpointAuthorization.TryResolveActor(context, out var actor))
            {
                return Results.Unauthorized();
            }

            var registry = context.RequestServices.GetService<ReportTemplateRegistryService>();
            if (registry is null)
            {
                return WorkspaceServiceUnavailable();
            }

            try
            {
                return Results.Json(registry.Submit(new VersionedReportTemplateIdDto(templateName, version), actor, request?.Rationale), jsonOptions);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or KeyNotFoundException)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        })
        .WithName("SubmitReportTemplateDraft")
        .Produces<ReportTemplateGovernanceRecordDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden);

        group.MapPost("/reporting/templates/{templateName}/versions/{version:int}/approve", (string templateName, int version, ReportTemplateDecisionRequestDto request, HttpContext context) =>
        {
            if (!HasReportingWorkflowPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            if (!EndpointAuthorization.TryResolveActor(context, out var actor))
            {
                return Results.Unauthorized();
            }

            var registry = context.RequestServices.GetService<ReportTemplateRegistryService>();
            if (registry is null)
            {
                return WorkspaceServiceUnavailable();
            }

            try
            {
                return Results.Json(registry.Approve(new VersionedReportTemplateIdDto(templateName, version), request, actor), jsonOptions);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or KeyNotFoundException)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        })
        .WithName("ApproveReportTemplateDraft")
        .Produces<ReportTemplateGovernanceRecordDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden);

        group.MapPost("/reporting/templates/{templateName}/versions/{version:int}/reject", (string templateName, int version, ReportTemplateDecisionRequestDto request, HttpContext context) =>
        {
            if (!HasReportingWorkflowPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            if (!EndpointAuthorization.TryResolveActor(context, out var actor))
            {
                return Results.Unauthorized();
            }

            var registry = context.RequestServices.GetService<ReportTemplateRegistryService>();
            if (registry is null)
            {
                return WorkspaceServiceUnavailable();
            }

            try
            {
                return Results.Json(registry.Reject(new VersionedReportTemplateIdDto(templateName, version), request, actor), jsonOptions);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or KeyNotFoundException)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        })
        .WithName("RejectReportTemplateDraft")
        .Produces<ReportTemplateGovernanceRecordDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden);

        group.MapPost("/reporting/templates/render", (RenderReportTemplateRequestDto request, HttpContext context) =>
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
                return Results.Json(registry.Render(request), jsonOptions);
            }
            catch (ArgumentException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        });

        group.MapPost("/reporting/packs/create", (string fundProfileId, string fundAccountId, string period, VersionedReportTemplateIdDto templateId, HttpContext context) =>
        {
            if (!HasReportingWorkflowPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            if (!EndpointAuthorization.TryResolveActor(context, out var actor))
            {
                return Results.Unauthorized();
            }

            var svc = context.RequestServices.GetService<ReportPackWorkflowService>();
            return svc is null ? WorkspaceServiceUnavailable() : Results.Json(svc.Create(fundProfileId, fundAccountId, period, templateId, actor), jsonOptions, statusCode: StatusCodes.Status201Created);
        });

        group.MapPost("/reporting/packs", (ReportPackCreateRequestDto request, HttpContext context) =>
        {
            if (!HasReportingWorkflowPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            if (!EndpointAuthorization.TryResolveActor(context, out var actor))
            {
                return Results.Unauthorized();
            }

            var svc = context.RequestServices.GetService<ReportPackWorkflowService>();
            if (svc is null)
            {
                return WorkspaceServiceUnavailable();
            }

            return Results.Json(
                svc.Create(request.FundProfileId, request.FundAccountId, request.Period, request.TemplateId, actor, request.LineProvenance),
                jsonOptions,
                statusCode: StatusCodes.Status201Created);
        })
        .WithName("CreateReportingPackWorkflow")
        .Produces<ReportPackWorkflowRecordDto>(StatusCodes.Status201Created);

        group.MapPost("/reporting/packs/{reportId:guid}/validate", (Guid reportId, HttpContext context) => TransitionPack(context, reportId, ReportPackWorkflowStateDto.Validated));
        group.MapPost("/reporting/packs/{reportId:guid}/submit", (Guid reportId, HttpContext context) => SubmitPack(context, reportId));
        group.MapPost("/reporting/packs/{reportId:guid}/approve", (Guid reportId, HttpContext context) => TransitionPack(context, reportId, ReportPackWorkflowStateDto.Approved));
        group.MapPost("/reporting/packs/{reportId:guid}/reject", (Guid reportId, ReportPackRejectRequestDto request, HttpContext context) =>
        {
            if (!HasReportingWorkflowPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            if (!TryResolveAuthorizedActorAndRole(context, out var actor, out var role))
            {
                return Results.Unauthorized();
            }

            var svc = context.RequestServices.GetService<ReportPackWorkflowService>();
            if (svc is null)
            {
                return WorkspaceServiceUnavailable();
            }

            try
            {
                var auditRequest = request with
                {
                    Actor = actor,
                    ActorRole = role
                };
                return Results.Json(svc.Reject(reportId, auditRequest), statusCode: StatusCodes.Status200OK);
            }
            catch (Exception ex) when (ex is ArgumentException or UnauthorizedAccessException or InvalidOperationException or KeyNotFoundException)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        })
        .WithName("RejectReportingPackWorkflow")
        .Accepts<ReportPackRejectRequestDto>("application/json")
        .Produces<ReportPackWorkflowRecordDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden);
        group.MapPost("/reporting/packs/{reportId:guid}/publish", (Guid reportId, ReportPackPublishRequestDto request, HttpContext context) =>
        {
            if (!HasReportingWorkflowPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            if (!TryResolveAuthorizedActorAndRole(context, out var actor, out var role))
            {
                return Results.Unauthorized();
            }

            var svc = context.RequestServices.GetService<ReportPackWorkflowService>();
            if (svc is null)
            {
                return WorkspaceServiceUnavailable();
            }

            try
            {
                return Results.Json(
                    svc.Publish(
                        reportId,
                        actor,
                        role,
                        request.SignedOffBy,
                        request.EvidenceHash,
                        request.ManifestId,
                        request.RetainedManifestPath,
                        request.EvidenceLinks,
                        request.Note),
                    jsonOptions);
            }
            catch (Exception ex) when (ex is ArgumentException or UnauthorizedAccessException or InvalidOperationException or KeyNotFoundException)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        });

        group.MapPost("/reporting/packs/{reportId:guid}/restatements", (Guid reportId, ReportPackRestateRequestDto request, HttpContext context) =>
        {
            if (!HasReportingWorkflowPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            if (!TryResolveAuthorizedActorAndRole(context, out var actor, out var role))
            {
                return Results.Unauthorized();
            }

            var svc = context.RequestServices.GetService<ReportPackWorkflowService>();
            if (svc is null)
            {
                return WorkspaceServiceUnavailable();
            }

            try
            {
                var approver = string.IsNullOrWhiteSpace(request.Approver) ? actor : request.Approver.Trim();
                return Results.Json(svc.Restate(reportId, actor, role, request.ReasonCode, approver, request.PriorVersionReportId, request.ChangedLines), jsonOptions);
            }
            catch (Exception ex) when (ex is ArgumentException or UnauthorizedAccessException or InvalidOperationException or KeyNotFoundException)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        })
        .WithName("RestateReportingPackWorkflow")
        .Produces<ReportPackWorkflowRecordDto>(StatusCodes.Status200OK);

        group.MapPost("/reporting/packs/{reportId:guid}/archive", (Guid reportId, HttpContext context) => TransitionPack(context, reportId, ReportPackWorkflowStateDto.Archived));

        group.MapPost("/reporting/packs/{reportId:guid}/restate", (Guid reportId, string reasonCode, Guid priorVersionReportId, ReportPackChangedLineDto[] changedLines, HttpContext context) =>
        {
            if (!HasReportingWorkflowPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            if (!TryResolveAuthorizedActorAndRole(context, out var actor, out var role))
            {
                return Results.Unauthorized();
            }

            var svc = context.RequestServices.GetService<ReportPackWorkflowService>();
            if (svc is null)
            {
                return WorkspaceServiceUnavailable();
            }

            try
            {
                return Results.Json(svc.Restate(reportId, actor, role, reasonCode, actor, priorVersionReportId, changedLines), jsonOptions);
            }
            catch (Exception ex) when (ex is ArgumentException or UnauthorizedAccessException or InvalidOperationException or KeyNotFoundException)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        });

        group.MapGet("/reporting/packs/history", (string period, string fundAccountId, HttpContext context) =>
        {
            if (!HasReportingReadPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var svc = context.RequestServices.GetService<ReportPackWorkflowService>();
            return svc is null ? WorkspaceServiceUnavailable() : Results.Json(svc.GetHistory(period, fundAccountId), jsonOptions);
        });
        group.MapGet("/report-packs/{reportId:guid}", async (Guid reportId, HttpContext context) =>
        {
            var service = ResolveWorkspaceService(context);
            if (service is null)
            {
                return WorkspaceServiceUnavailable();
            }

            var result = await service.GetReportPackAsync(reportId, context.RequestAborted).ConfigureAwait(false);
            return result is null ? Results.NotFound() : Results.Json(result, jsonOptions);
        })
        .WithName("GetFundReportPack")
        .Produces<FundReportPackSnapshotDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/report-packs/{reportId:guid}/evidence-bundle", async (Guid reportId, HttpContext context) =>
        {
            var service = ResolveWorkspaceService(context);
            if (service is null)
            {
                return WorkspaceServiceUnavailable();
            }

            var auditActor = context.Request.Query["auditActor"].FirstOrDefault();
            var result = await service
                .ExportReportPackEvidenceBundleAsync(reportId, auditActor, context.RequestAborted)
                .ConfigureAwait(false);
            return result is null ? Results.NotFound() : Results.Json(result, jsonOptions);
        })
        .WithName("ExportFundReportPackEvidenceBundle")
        .Produces<FundReportPackEvidenceBundleDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/report-packs/{reportId:guid}/ledger-provenance", async (Guid reportId, string scopeKey, HttpContext context) =>
        {
            var service = context.RequestServices.GetService<LedgerAmountProvenanceService>();
            if (service is null)
            {
                return WorkspaceServiceUnavailable();
            }

            var result = await service.GetAsync(reportId, scopeKey, context.RequestAborted).ConfigureAwait(false);
            return result is null ? Results.NotFound() : Results.Json(result, jsonOptions);
        })
        .WithName("GetFundReportPackLedgerProvenance")
        .Produces<LedgerAmountProvenanceDetailDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

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
        .WithName("PreviewInvestmentAccountingTransactionLab")
        .Produces<InvestmentAccountingTransactionLabPreviewDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden);
    }


    private static IResult SubmitPack(HttpContext context, Guid reportId)
    {
        if (!HasReportingWorkflowPermission(context))
        {
            return EndpointHelpers.Forbidden();
        }

        if (!TryResolveAuthorizedActorAndRole(context, out var actor, out var role))
        {
            return Results.Unauthorized();
        }

        var svc = context.RequestServices.GetService<ReportPackWorkflowService>();
        if (svc is null)
        {
            return WorkspaceServiceUnavailable();
        }

        try
        {
            return Results.Json(svc.Submit(reportId, actor, role), statusCode: StatusCodes.Status200OK);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or InvalidOperationException or KeyNotFoundException)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    private static IResult TransitionPack(HttpContext context, Guid reportId, ReportPackWorkflowStateDto target)
    {
        if (!HasReportingWorkflowPermission(context))
        {
            return EndpointHelpers.Forbidden();
        }

        if (!TryResolveAuthorizedActorAndRole(context, out var actor, out var role))
        {
            return Results.Unauthorized();
        }

        var svc = context.RequestServices.GetService<ReportPackWorkflowService>();
        if (svc is null)
        {
            return WorkspaceServiceUnavailable();
        }

        try
        {
            return Results.Json(svc.Transition(reportId, target, actor, role), statusCode: StatusCodes.Status200OK);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or InvalidOperationException or KeyNotFoundException)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
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
        => EndpointAuthorization.HasAnyPermission(context, UserPermission.ManageStrategies, UserPermission.AdminMaintenance);

    private static bool HasReportingReadPermission(HttpContext context)
        => EndpointAuthorization.HasAnyPermission(context, UserPermission.ViewAnalytics, UserPermission.ManageStrategies, UserPermission.AdminMaintenance);

    private static bool TryResolveAuthorizedActorAndRole(HttpContext context, out string actor, out string role)
    {
        if (!EndpointAuthorization.TryResolveActor(context, out actor))
        {
            role = string.Empty;
            return false;
        }

        if (!context.Items.TryGetValue(LoginSessionMiddleware.CurrentUserRoleKey, out var rawRole) || rawRole is not UserRole currentRole)
        {
            role = string.Empty;
            return false;
        }

        role = currentRole.ToString();
        return true;
    }

    private static IResult ServiceUnavailable() =>
        Results.Problem("Fund structure service is not registered.", statusCode: StatusCodes.Status501NotImplemented);

    private static IResult WorkspaceServiceUnavailable() =>
        Results.Problem("Fund operations workspace service is not registered.", statusCode: StatusCodes.Status501NotImplemented);

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

    private static bool TryValidateReportPackGenerateRequest(
        FundReportPackGenerateRequestDto? request,
        out string error)
    {
        if (request is null)
        {
            error = "A request body is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(request.FundProfileId))
        {
            error = "fundProfileId is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(request.AuditActor))
        {
            error = "auditActor is required.";
            return false;
        }

        if (request.Formats is { Count: 0 })
        {
            error = "At least one report-pack artifact format is required.";
            return false;
        }

        if (request.Formats is not null
            && request.Formats.Any(static format => !Enum.IsDefined(format)))
        {
            error = "One or more report-pack artifact formats are unsupported.";
            return false;
        }

        if (request.ExpectedSchemaVersion is { } expectedSchemaVersion
            && expectedSchemaVersion != GovernanceReportPackContract.CurrentSchemaVersion)
        {
            error = $"expectedSchemaVersion must be {GovernanceReportPackContract.CurrentSchemaVersion}.";
            return false;
        }

        error = string.Empty;
        return true;
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
