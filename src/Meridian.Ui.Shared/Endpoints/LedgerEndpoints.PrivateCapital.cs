using System.Collections.Immutable;
using System.Text.Json;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Reporting;
using Meridian.Contracts.Tenancy;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.PrivateCapital;
using Meridian.Identity.Auth;
using Meridian.Ledger;
using Meridian.Reporting;
using Meridian.Ui.Shared.Evidence;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

public static partial class LedgerEndpoints
{
    private const string PrivateCapitalRoute = "/api/ledger/private-capital";

    private static void MapPrivateCapitalEndpoints(WebApplication app, JsonSerializerOptions options)
    {
        // Exact decimal text is a transport concern; retained domain hashes keep canonical decimals.
        var json = new JsonSerializerOptions(options);
        json.Converters.Insert(0, new AccountOversightDecimalJsonConverter());

        CapitalRead(app.MapGet(PrivateCapitalRoute + "/workbench", async (Guid ledgerBookId, HttpContext context) =>
            await ExecutePrivateCapitalAsync(context, ledgerBookId, false, async (scope, service, actor) =>
            {
                var workbench = await service.GetWorkbenchAsync(scope, context.RequestAborted).ConfigureAwait(false);
                if (context.RequestServices.GetService<CapitalCallNoticeReportingService>() is { } notices)
                    workbench = workbench with
                    {
                        Notices = await notices.ReadNoticeSummariesAsync(scope, PrivateCapitalReportAccess(context, scope, actor),
                            context.RequestAborted).ConfigureAwait(false)
                    };
                return Results.Json(workbench, json);
            })
                .ConfigureAwait(false)).WithName("GetPrivateCapitalWorkbench"));

        CapitalMutation(app.MapPost(PrivateCapitalRoute + "/commitments", async (
            Guid ledgerBookId, CreatePrivateCapitalCommitmentRequest request, HttpContext context) =>
            await ExecutePrivateCapitalAsync(context, ledgerBookId, true, async (scope, service, actor) =>
            {
                await VerifyPrivateCapitalEvidenceAsync(context, scope, request.EvidenceLinks).ConfigureAwait(false);
                return Results.Json(await service.CreateCommitmentAsync(scope, request, actor, context.RequestAborted)
                    .ConfigureAwait(false), json, statusCode: StatusCodes.Status201Created);
            }).ConfigureAwait(false)).WithName("CreatePrivateCapitalCommitment"));

        CapitalMutation(app.MapPost(PrivateCapitalRoute + "/commitments/{commitmentId}/approve", async (
            Guid ledgerBookId, string commitmentId, ApprovePrivateCapitalCommitmentRequest request, HttpContext context) =>
            await ExecutePrivateCapitalAsync(context, ledgerBookId, true, async (scope, service, actor) =>
            {
                var store = context.RequestServices.GetRequiredService<IPrivateCapitalStore>();
                var commitment = await store.GetCommitmentAsync(scope, commitmentId, request.Revision, context.RequestAborted)
                    .ConfigureAwait(false) ?? throw new KeyNotFoundException("The commitment revision was not found.");
                await VerifyPrivateCapitalEvidenceAsync(context, scope, commitment.EvidenceLinks).ConfigureAwait(false);
                return Results.Json(await service.ApproveCommitmentAsync(scope, commitmentId, request.Revision, actor,
                    context.RequestAborted).ConfigureAwait(false), json);
            }).ConfigureAwait(false)).WithName("ApprovePrivateCapitalCommitment"));

        CapitalRead(app.MapPost(PrivateCapitalRoute + "/calls/preview", async (
            Guid ledgerBookId, PreviewPrivateCapitalCallRequest request, HttpContext context) =>
            await ExecutePrivateCapitalAsync(context, ledgerBookId, false, async (scope, service, actor) =>
            {
                await VerifyPrivateCapitalPeriodAsync(context, scope, request.PeriodId, request.NoticeDate).ConfigureAwait(false);
                return Results.Json(await service.PreviewCallAsync(scope, request, context.RequestAborted).ConfigureAwait(false), json);
            }).ConfigureAwait(false)).WithName("PreviewPrivateCapitalCall")
            .DeclareNonMutating("Read-only allocation preview over retained approved commitment revisions; no call, journal or approval is created."));

        CapitalMutation(app.MapPost(PrivateCapitalRoute + "/calls", async (
            Guid ledgerBookId, RetainPrivateCapitalCallRequest request, HttpContext context) =>
            await ExecutePrivateCapitalAsync(context, ledgerBookId, true, async (scope, service, actor) =>
            {
                ArgumentNullException.ThrowIfNull(request.Preview);
                var retained = await context.RequestServices.GetRequiredService<IPrivateCapitalStore>()
                    .GetCallAsync(scope, request.Preview.CallId, context.RequestAborted).ConfigureAwait(false);
                // Exact recovery returns the original immutable plan; only a new call needs period admission.
                if (retained is null)
                    await VerifyPrivateCapitalPeriodAsync(context, scope, request.Preview.PeriodId, request.Preview.NoticeDate).ConfigureAwait(false);
                return Results.Json(await service.CreateCallAsync(scope, request, actor, context.RequestAborted)
                    .ConfigureAwait(false), json, statusCode: StatusCodes.Status201Created);
            }).ConfigureAwait(false)).WithName("RetainPrivateCapitalCall"));

        CapitalMutation(app.MapPost(PrivateCapitalRoute + "/calls/{callId}/intake", async (
            Guid ledgerBookId, string callId, HttpContext context) =>
            await ExecutePrivateCapitalAsync(context, ledgerBookId, true, async (scope, service, actor) =>
            {
                var store = context.RequestServices.GetRequiredService<IPrivateCapitalStore>();
                var call = await store.GetCallAsync(scope, callId, context.RequestAborted).ConfigureAwait(false)
                    ?? throw new KeyNotFoundException("The retained call was not found.");
                await VerifyPrivateCapitalPeriodAsync(context, scope, call.PeriodId, call.NoticeDate).ConfigureAwait(false);
                var commitments = new List<CapitalCallCommitmentInput>();
                foreach (var line in call.Lines)
                {
                    var commitment = await store.GetCommitmentAsync(scope, line.CommitmentId, line.CommitmentRevision,
                        context.RequestAborted).ConfigureAwait(false)
                        ?? throw new InvalidOperationException("The call's retained commitment revision is unavailable.");
                    await VerifyPrivateCapitalEvidenceAsync(context, scope, commitment.EvidenceLinks).ConfigureAwait(false);
                    commitments.Add(new(commitment.CommitmentId, commitment.CapitalAccountId, commitment.InvestorId,
                        commitment.TotalCommitment, commitment.CommitmentDate,
                        commitment.IsActive ? CommitmentStatus.Active : CommitmentStatus.Closed,
                        commitment.InvestmentPeriodEndDate, commitment.EvidenceLinks));
                }
                var runner = context.RequestServices.GetRequiredService<AutomatedJournalIntakeRunner>();
                var result = await runner.RunCapitalCallIssuanceIntakeAsync(new(scope.FundProfileId, scope.Currency,
                    actor, call.CallId, call.AmountToCall, call.NoticeDate, call.DueDate, commitments,
                    Purpose: call.Purpose, LedgerBookId: scope.LedgerBookId, PeriodId: call.PeriodId,
                    EntityId: scope.EntityId, TenantId: scope.TenantId, CompanyId: scope.CompanyId), context.RequestAborted)
                    .ConfigureAwait(false);
                return Results.Json(result, json);
            }).ConfigureAwait(false)).WithName("IntakeRetainedPrivateCapitalCall"));

        CapitalMutation(app.MapPost(PrivateCapitalRoute + "/calls/{callId}/funding-intake", async (
            Guid ledgerBookId, string callId, RunCapitalCallFundingDraftIntakeRequest request, HttpContext context) =>
            await ExecutePrivateCapitalAsync(context, ledgerBookId, true, async (scope, service, actor) =>
            {
                if (!string.Equals(request.CallId, callId, StringComparison.Ordinal))
                    throw new ArgumentException("The receipt must reference the selected call.");
                await VerifyPrivateCapitalPeriodAsync(context, scope, request.PeriodId, request.ReceivedDate).ConfigureAwait(false);
                foreach (var receipt in request.Fundings)
                    await VerifyPrivateCapitalEvidenceAsync(context, scope, receipt.EvidenceLinks).ConfigureAwait(false);
                var runner = context.RequestServices.GetRequiredService<AutomatedJournalIntakeRunner>();
                return Results.Json(await runner.RunCapitalCallFundingIntakeAsync(request with
                {
                    Actor = actor, FundProfileId = scope.FundProfileId, Currency = scope.Currency,
                    LedgerBookId = scope.LedgerBookId, EntityId = scope.EntityId,
                    TenantId = scope.TenantId, CompanyId = scope.CompanyId
                }, context.RequestAborted).ConfigureAwait(false), json);
            }).ConfigureAwait(false)).WithName("IntakeRetainedPrivateCapitalFunding"));

        CapitalMutation(app.MapPost(PrivateCapitalRoute + "/bank-observations", async (
            Guid ledgerBookId, ImportPrivateCapitalBankObservationRequest request, HttpContext context) =>
            await ExecutePrivateCapitalAsync(context, ledgerBookId, true, async (scope, service, actor) =>
            {
                var observations = context.RequestServices.GetService<PrivateCapitalBankObservationService>();
                if (observations is null)
                    return PrivateCapitalUnavailable("Bank observation import requires reviewed Evidence Vault documents and banking persistence in the ledger database.");
                return Results.Json(await observations.ImportAsync(scope, request, actor, context.RequestAborted)
                    .ConfigureAwait(false), json, statusCode: StatusCodes.Status201Created);
            }).ConfigureAwait(false)).WithName("ImportPrivateCapitalBankObservation"));

        CapitalMutation(app.MapPost(PrivateCapitalRoute + "/calls/{callId}/investors/{investorId}/notice", async (
            Guid ledgerBookId, string callId, string investorId, HttpContext context) =>
            await ExecutePrivateCapitalAsync(context, ledgerBookId, true, async (scope, service, actor) =>
            {
                if (!EndpointAuthorization.HasAnyPermission(context, UserPermission.ManageReporting, UserPermission.AdminMaintenance)
                    || !EndpointAuthorization.TryGetPermissions(context, out var permissions))
                    return EndpointHelpers.Forbidden();
                var notices = context.RequestServices.GetService<CapitalCallNoticeReportingService>();
                if (notices is null) return PrivateCapitalUnavailable("Governed capital-call reporting is not configured.");
                var groups = EndpointAuthorization.ResolveReportGroupPrincipalIds(context).ToImmutableArray();
                var access = PrivateCapitalReportAccess(context, scope, actor);
                var caller = new ReportingGovernanceCallerContext(actor, scope.TenantId, scope.CompanyId, permissions,
                    ReportingCommandOrigin.HumanOperator, context.TraceIdentifier, groups);
                return Results.Json(await notices.GenerateAsync(scope, callId, investorId, actor, access, caller,
                    context.RequestAborted).ConfigureAwait(false), json);
            }).ConfigureAwait(false)).WithName("GeneratePrivateCapitalCallNotice")
            .RequireAnyPermission(UserPermission.ManageReporting, UserPermission.AdminMaintenance));
    }

    private static RouteHandlerBuilder CapitalRead(RouteHandlerBuilder builder) => builder
        .RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending)
        .RequireWorkstationTenantCompanyScope();

    private static RouteHandlerBuilder CapitalMutation(RouteHandlerBuilder builder) => builder
        .RequireAuthenticatedSession()
        .RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending)
        .RequireWorkstationTenantCompanyScope()
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

    private static async Task<IResult> ExecutePrivateCapitalAsync(HttpContext context, Guid bookId, bool mutation,
        Func<PrivateCapitalScopeDto, PrivateCapitalLifecycleService, string, Task<IResult>> execute)
    {
        if (!EndpointAuthorization.HasAnyPermission(context, UserPermission.AdminMaintenance, UserPermission.ManageDirectLending))
            return EndpointHelpers.Forbidden();
        var tenant = HttpContextWorkstationTenantContextAccessor.Resolve(context);
        if (!HasAccountingPackageTenantScope(tenant) || !EndpointAuthorization.TryResolveActor(context, out var actor))
            return EndpointHelpers.Forbidden();
        var service = context.RequestServices.GetService<PrivateCapitalLifecycleService>();
        var books = context.RequestServices.GetService<ILedgerBookService>();
        var registry = context.RequestServices.GetService<IFundProfileTenancyRegistry>();
        if (service is null || books is null || registry is null)
            return PrivateCapitalUnavailable("Durable private-capital authority is not configured.");
        try
        {
            var book = await books.GetBookAsync(bookId, context.RequestAborted).ConfigureAwait(false);
            if (book is null) return Results.NotFound();
            var owner = await registry.ResolveAsync(book.FundProfileId, context.RequestAborted).ConfigureAwait(false);
            if (owner is null || !string.Equals(owner.FundProfileId, book.FundProfileId, StringComparison.OrdinalIgnoreCase)
                || !CloseWorkflowOwnerMatches(owner, tenant))
                return EndpointHelpers.Forbidden();
            if (book.FundStructureNodeKind != FundStructureNodeKindDto.Entity || book.FundStructureNodeId == Guid.Empty)
                throw new InvalidOperationException("This private-capital slice requires a legal-entity-owned ledger book.");
            var scope = PrivateCapitalAuthorityRules.NormalizeScope(new(tenant.TenantId!, tenant.CompanyId!, book.FundStructureNodeId.ToString("D"),
                book.FundProfileId, book.LedgerBookId, book.BaseCurrency));
            return await execute(scope, service, actor).ConfigureAwait(false);
        }
        catch (KeyNotFoundException) { return Results.NotFound(); }
        catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
        catch (InvalidOperationException ex) { return Results.Conflict(new { error = ex.Message }); }
        catch (Exception ex) when (ex is Npgsql.NpgsqlException or IOException or NotSupportedException or TimeoutException)
        { return PrivateCapitalUnavailable("Retained private-capital authority or evidence is unavailable; no operation was authorized."); }
    }

    private static IResult PrivateCapitalUnavailable(string detail) =>
        Results.Problem(detail, statusCode: StatusCodes.Status503ServiceUnavailable);

    private static ReportAccessQueryContext PrivateCapitalReportAccess(HttpContext context, PrivateCapitalScopeDto scope, string actor)
        => new(actor, EndpointAuthorization.ResolveReportGroupPrincipalIds(context), scope.CompanyId,
            EndpointAuthorization.HasPermission(context, UserPermission.AdminMaintenance), scope.TenantId, true);

    private static async Task VerifyPrivateCapitalPeriodAsync(HttpContext context, PrivateCapitalScopeDto scope,
        string? periodId, DateOnly date)
    {
        if (!Guid.TryParse(periodId, out var id)) throw new ArgumentException("An exact accounting period is required.");
        var books = context.RequestServices.GetRequiredService<ILedgerBookService>();
        var periods = await books.ListPeriodsAsync(new LedgerPeriodQuery(scope.LedgerBookId), context.RequestAborted).ConfigureAwait(false);
        var period = periods.SingleOrDefault(p => p.PeriodId == id);
        if (period is null || period.LedgerBookId != scope.LedgerBookId || period.Status != LedgerPeriodStatusDto.Open
            || date < period.StartDate || date > period.EndDate)
            throw new InvalidOperationException("The effective date must belong to the selected book's open accounting period.");
    }

    private static async Task VerifyPrivateCapitalEvidenceAsync(HttpContext context, PrivateCapitalScopeDto scope,
        IReadOnlyList<string>? links)
    {
        if (links is not { Count: > 0 }) throw new ArgumentException("Retained Evidence Vault source documents are required.");
        var vault = context.RequestServices.GetService<IEvidenceArtifactStore>()
            ?? throw new NotSupportedException("Evidence Vault is not configured.");
        foreach (var link in links)
        {
            if (!EvidenceVaultReference.TryParseCanonical(link, out var id))
                throw new ArgumentException("Evidence must reference a retained Evidence Vault artifact.");
            var identity = await vault.TryGetVaultIdentityAsync(id, scope.TenantId, scope.CompanyId, context.RequestAborted).ConfigureAwait(false);
            if (identity is null || !await vault.VerifyRetainedContentAsync(id, scope.TenantId, scope.CompanyId,
                context.RequestAborted).ConfigureAwait(false))
                throw new InvalidOperationException("The retained source evidence could not be verified in the current scope.");
            if (identity.Documents.Count == 0 || identity.Documents.Any(d => d.ReviewerState.Status != EvidenceDocumentReviewStatusDto.Accepted))
                throw new InvalidOperationException("Source documents require accepted Evidence Vault review before use.");
        }
    }
}
