using System.Text.Json;
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

        group.MapPost("/organizations", async (JsonElement body, HttpContext context) =>
        {
            var service = ResolveService(context);
            if (service is null) return ServiceUnavailable();

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
            if (service is null) return ServiceUnavailable();

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
            if (service is null) return ServiceUnavailable();

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
            if (service is null) return ServiceUnavailable();

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
            if (service is null) return ServiceUnavailable();

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
            if (service is null) return ServiceUnavailable();

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
            if (service is null) return ServiceUnavailable();

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
            if (service is null) return ServiceUnavailable();

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
            if (service is null) return ServiceUnavailable();

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

        group.MapPost("/assignments", async (JsonElement body, HttpContext context) =>
        {
            var service = ResolveService(context);
            if (service is null) return ServiceUnavailable();

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

        group.MapGet("/graph", async (HttpContext context) =>
        {
            var service = ResolveService(context);
            if (service is null) return ServiceUnavailable();

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
            if (service is null) return ServiceUnavailable();

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
            if (service is null) return ServiceUnavailable();

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
            if (service is null) return ServiceUnavailable();

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
            if (service is null) return ServiceUnavailable();

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

        group.MapGet("/cash-flow-view", async (HttpContext context) =>
        {
            var service = ResolveService(context);
            if (service is null) return ServiceUnavailable();

            var q = context.Request.Query;
            var scopeKind = ParseCashFlowScopeKind(q["scopeKind"]);
            if (scopeKind is null)
            {
                return Results.Problem(
                    "scopeKind is required and must be a valid governance cash-flow scope.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            var ledgerGroupId = ParseLedgerGroupId(q["ledgerGroupId"], out var ledgerGroupParseError);
            if (ledgerGroupParseError is not null)
            {
                return Results.Problem(ledgerGroupParseError, statusCode: StatusCodes.Status400BadRequest);
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
    }

    private static IFundStructureService? ResolveService(HttpContext context) =>
        context.RequestServices.GetService<IFundStructureService>();

    private static FundOperationsWorkspaceReadService? ResolveWorkspaceService(HttpContext context) =>
        context.RequestServices.GetService<FundOperationsWorkspaceReadService>();

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

    private static IReadOnlyList<string>? ParseSelectedLedgerIds(params StringValues[] valueSets)
    {
        var parsed = valueSets
            .SelectMany(static values => values)
            .SelectMany(static value => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
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
