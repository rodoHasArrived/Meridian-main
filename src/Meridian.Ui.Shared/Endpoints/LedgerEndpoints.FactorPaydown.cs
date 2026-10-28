using System.Globalization;
using System.Text.Json;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.Api;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Tenancy;
using Meridian.FinancialOperations.Ledger;
using Meridian.Identity.Auth;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

public static partial class LedgerEndpoints
{
    private static void MapFactorPaydownEndpoints(WebApplication app, JsonSerializerOptions jsonOptions)
    {
        jsonOptions = new JsonSerializerOptions(jsonOptions);
        jsonOptions.Converters.Insert(0, new AccountOversightDecimalJsonConverter());
        app.MapGet(UiApiRoutes.LedgerFactorPaydownDraftCapabilities, async (
            Guid ledgerBookId, Guid journalEntryId, string fundProfileId, HttpContext context) =>
        {
            if (!HasLedgerReadPermission(context) || !TryResolveActor(context, out var actor))
                return EndpointHelpers.Forbidden();
            var tenant = HttpContextWorkstationTenantContextAccessor.Resolve(context);
            var books = ResolveService(context);
            var owners = context.RequestServices.GetService<IFundProfileTenancyRegistry>();
            if (!HasAccountingPackageTenantScope(tenant) || books is null || owners is null)
                return EndpointHelpers.Forbidden();
            var book = await books.GetBookAsync(ledgerBookId, context.RequestAborted).ConfigureAwait(false);
            if (book is null || book.LedgerBookId != ledgerBookId || book.FundProfileId != fundProfileId)
                return EndpointHelpers.Forbidden();
            var owner = await owners.ResolveAsync(book.FundProfileId, context.RequestAborted).ConfigureAwait(false);
            if (owner is null || owner.FundProfileId != book.FundProfileId || !CloseWorkflowOwnerMatches(owner, tenant))
                return EndpointHelpers.Forbidden();
            if (ResolveManualJournalEntryWorkbenchService(context) is not ManualJournalEntryWorkbenchService workbench)
                return ServiceUnavailable("The governed asset posting workflow is unavailable.");
            var queue = await workbench.GetWorkbenchAsync(fundProfileId, ledgerBookId, context.RequestAborted,
                tenant.TenantId, tenant.CompanyId).ConfigureAwait(false);
            var draft = queue.Drafts.SingleOrDefault(item => item.JournalEntryId == journalEntryId &&
                item.LedgerBookId == ledgerBookId && item.FundProfileId == fundProfileId && item.AssetAccountingDraft is not null);
            if (draft is null)
                return Results.NotFound();
            return Results.Json(await workbench.GetAssetAccountingCapabilitiesAsync(draft, actor,
                HasLedgerCertificationPermission(context), context.RequestAborted).ConfigureAwait(false), jsonOptions);
        })
        .WithName("GetLedgerFactorPaydownDraftCapabilities")
        .RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending,
            UserPermission.ManageLedgerReports)
        .RequireWorkstationTenantCompanyScope().RequireAuthenticatedSession()
        .Produces<AssetAccountingDraftCapabilitiesDto>(StatusCodes.Status200OK);

        app.MapPost(UiApiRoutes.LedgerFactorPaydownPreparation, async (
            Guid ledgerBookId, PrepareFactorPaydownRequestDto request, HttpContext context) =>
            await ExecuteFactorPaydownAsync(context, ledgerBookId, request, async (prepared, _) =>
                Results.Json(new FactorPaydownPreparationDto(prepared.Instruction, prepared.LotMutation,
                    prepared.ProjectRequest, prepared.Components.ToDictionary(static item => item.Key,
                        static item => item.Value.ToString(CultureInfo.InvariantCulture)),
                    FactorPaydownPreviewFingerprint(prepared.Instruction, prepared.ProjectRequest.ProjectedEffect)), jsonOptions))
                .ConfigureAwait(false))
            .WithName("PrepareLedgerFactorPaydown")
            .RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending, UserPermission.ManageLedgerReports)
            .RequireWorkstationTenantCompanyScope().RequireAuthenticatedSession()
            .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy)
            .Produces<FactorPaydownPreparationDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest).Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status409Conflict).Produces(StatusCodes.Status501NotImplemented);

        app.MapPost(UiApiRoutes.LedgerFactorPaydownDrafts, async (
            Guid ledgerBookId, CreateFactorPaydownDraftRequestDto request, HttpContext context) =>
            await ExecuteFactorPaydownAsync(context, ledgerBookId, request.Request, async (prepared, actor) =>
            {
                if (string.IsNullOrWhiteSpace(request.Description) || string.IsNullOrWhiteSpace(request.ExpectedPreparationFingerprint))
                    return Results.BadRequest(new { error = "A reviewed preparation fingerprint and draft description are required." });
                var fingerprint = FactorPaydownPreviewFingerprint(prepared.Instruction, prepared.ProjectRequest.ProjectedEffect);
                if (!string.Equals(fingerprint, request.ExpectedPreparationFingerprint, StringComparison.Ordinal))
                    return Results.Conflict(new { error = "Factor paydown preparation changed. Prepare and review it again.", code = "FACTOR_PAYDOWN_PREVIEW_STALE" });
                if (ResolveManualJournalEntryWorkbenchService(context) is not ManualJournalEntryWorkbenchService workbench)
                    return ServiceUnavailable("The durable manual-journal review workbench is required for factor draft intake.");
                var spine = ResolveAssetAccountingEventSpineService(context);
                if (spine is null)
                    return ServiceUnavailable("Asset accounting event spine service is not registered.");
                var project = prepared.ProjectRequest;
                var retained = await spine.GetLatestAsync(project.EconomicEvent.EventId,
                    project.EconomicEvent.EventVersion, context.RequestAborted).ConfigureAwait(false);
                if (retained is { SpineVersion: 1, ProjectedEffect: null, FactorPaydown: { } expectedInstruction })
                {
                    var expected = retained.Stages.SingleOrDefault(stage => stage.Stage == AssetAccountingLifecycleStageDto.Expected);
                    if (expected is null || !string.Equals(expected.AttestedBy, actor, StringComparison.Ordinal) ||
                        !string.Equals(fingerprint, FactorPaydownPreviewFingerprint(expectedInstruction, project.ProjectedEffect), StringComparison.Ordinal))
                        return Results.Conflict(new { error = "The retained factor expectation differs from this reviewed command." });
                    project = project with
                    {
                        EconomicEvent = retained.EconomicEvent,
                        ProjectionLineage = retained.ProjectionLineage,
                        Scope = retained.Scope,
                        RetainedEvidence = retained.RetainedEvidence,
                        FactorPaydown = expectedInstruction,
                        ProjectedAtUtc = expected.AttestedAtUtc
                    };
                    retained = (await spine.ProjectAsync(project, context.RequestAborted).ConfigureAwait(false)).Spine;
                }
                if (retained is not null)
                {
                    if (retained.FactorPaydown is null || retained.ProjectedEffect is null ||
                        !string.Equals(fingerprint, FactorPaydownPreviewFingerprint(retained.FactorPaydown, retained.ProjectedEffect), StringComparison.Ordinal))
                        return Results.Conflict(new { error = "The retained factor event differs from the reviewed preparation." });
                    if (retained.PostedJournalImpact is not null || retained.Stages.Any(static stage => stage.Stage == AssetAccountingLifecycleStageDto.Posted))
                        return Results.Conflict(new { error = "The factor event is already posted." });
                    if (retained.DraftedCandidate is { } drafted && retained.DraftedCandidateResult is { } candidate)
                    {
                        if (!string.Equals(drafted.Description, request.Description.Trim(), StringComparison.Ordinal) ||
                            !string.Equals(drafted.Actor, actor, StringComparison.Ordinal))
                            return Results.Conflict(new { error = "The factor event already has a different reviewed draft." });
                        var retainedDraft = new AssetAccountingPostingCandidateDto(retained, candidate);
                        await workbench.SaveAssetAccountingDraftAsync(retainedDraft, actor, context.RequestAborted).ConfigureAwait(false);
                        return Results.Json(retainedDraft, jsonOptions);
                    }
                    // Resume after a retained projection using its exact lineage, including generation time.
                    project = project with
                    {
                        EconomicEvent = retained.EconomicEvent,
                        ProjectionLineage = retained.ProjectionLineage,
                        Scope = retained.Scope,
                        RetainedEvidence = retained.RetainedEvidence,
                        FactorPaydown = retained.FactorPaydown
                    };
                }
                else
                    retained = (await spine.ProjectAsync(project, context.RequestAborted).ConfigureAwait(false)).Spine;
                var now = DateTimeOffset.UtcNow;
                var result = await spine.BuildPostingCandidateAsync(new AssetAccountingPostingCandidateRequestDto(
                    project.EventKind, project.Scope, project.EconomicEvent, project.ProjectionLineage,
                    project.EventAmount, project.Currency, actor, now, request.Description.Trim(), retained.SpineVersion,
                    project.ExpectedPeriodVersion, project.RetainedEvidence,
                    prepared.LotMutation with { FactorPaydown = project.FactorPaydown }, project.EconomicEvent.CorrelationId),
                    context.RequestAborted).ConfigureAwait(false);
                await workbench.SaveAssetAccountingDraftAsync(result, actor, context.RequestAborted).ConfigureAwait(false);
                return Results.Json(result, jsonOptions);
            }).ConfigureAwait(false))
            .WithName("CreateLedgerFactorPaydownDraft")
            .RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending, UserPermission.ManageLedgerReports)
            .RequireWorkstationTenantCompanyScope().RequireAuthenticatedSession()
            .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy)
            .Produces<AssetAccountingPostingCandidateDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest).Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status409Conflict).Produces(StatusCodes.Status501NotImplemented);
    }

    private static async Task<IResult> ExecuteFactorPaydownAsync(HttpContext context, Guid ledgerBookId,
        PrepareFactorPaydownRequestDto? request, Func<FactorPaydownPreparation, string, Task<IResult>> execute)
    {
        if (!HasLedgerMutationPermission(context) || !TryResolveActor(context, out var actor))
            return EndpointHelpers.Forbidden();
        var tenant = HttpContextWorkstationTenantContextAccessor.Resolve(context);
        if (!HasAccountingPackageTenantScope(tenant))
            return EndpointHelpers.Forbidden();
        if (request is null || request.LedgerBookId != ledgerBookId || request.FactorEvidence is null ||
            request.FactorEvidence.Any(static evidence => evidence is null))
            return Results.BadRequest(new { error = "The preparation request must match the route book and contain factor evidence." });
        if (string.IsNullOrWhiteSpace(request.CashAccountPath) || string.IsNullOrWhiteSpace(request.CarryingAccountPath) ||
            string.IsNullOrWhiteSpace(request.RealizedGainAccountPath) || string.IsNullOrWhiteSpace(request.RealizedLossAccountPath))
            return Results.BadRequest(new { error = "Cash, carrying, realized-gain and realized-loss account paths are required." });
        var books = ResolveService(context);
        var periods = ResolveJournalStore(context);
        var registry = context.RequestServices.GetService<IFundProfileTenancyRegistry>();
        var prepare = context.RequestServices.GetService<FactorPaydownLotInstructionService>();
        if (books is null || periods is null || registry is null || prepare is null)
            return ServiceUnavailable("Authoritative book, period, tenancy and factor-paydown services are required.");
        try
        {
            var book = await books.GetBookAsync(ledgerBookId, context.RequestAborted).ConfigureAwait(false);
            if (book is null || book.LedgerBookId != ledgerBookId || !string.Equals(book.FundProfileId, request.FundProfileId, StringComparison.Ordinal))
                return EndpointHelpers.Forbidden();
            var owner = await registry.ResolveAsync(book.FundProfileId, context.RequestAborted).ConfigureAwait(false);
            if (owner is null || !string.Equals(owner.FundProfileId, book.FundProfileId, StringComparison.OrdinalIgnoreCase) || !CloseWorkflowOwnerMatches(owner, tenant))
                return EndpointHelpers.Forbidden();
            var period = await periods.GetPeriodAsync(request.PeriodId, context.RequestAborted).ConfigureAwait(false);
            if (period is null || period.LedgerBookId != ledgerBookId)
                return EndpointHelpers.Forbidden();
            if (book.AccountingBasis != request.AccountingBasis ||
                period.Version != request.ExpectedPeriodVersion || !string.Equals(period.Status, "Open", StringComparison.Ordinal) ||
                request.EffectiveDate < period.StartDate || request.EffectiveDate > period.EndDate)
                return Results.Conflict(new { error = "The accounting basis, open period, date or expected period version is no longer valid." });
            if (request.OccurredAtUtc == default || request.OccurredAtUtc.Offset != TimeSpan.Zero || request.OccurredAtUtc > DateTimeOffset.UtcNow)
                return Results.BadRequest(new { error = "Factor observation time must be a non-future UTC timestamp." });
            var prepared = await prepare.PrepareAsync(new FactorPaydownPreparationRequest(
                request.LedgerBookId, request.SecurityId, request.BookPositionId, request.PeriodId,
                request.ExpectedPeriodVersion, request.AccountingBasis, book.FundProfileId, request.EffectiveDate,
                request.PriorFactor, request.NewFactor, request.SourceDomain, request.SourceEntityId, request.SourceContentHash,
                request.OccurredAtUtc, request.FactorEvidence, request.CashAccountPath, request.CarryingAccountPath,
                request.RealizedGainAccountPath, request.RealizedLossAccountPath, tenant.TenantId, tenant.CompanyId,
                CorrelationId: request.CorrelationId), actor, DateTimeOffset.UtcNow, context.RequestAborted).ConfigureAwait(false);
            // The projector may obtain its generation clock after the entry clock above.
            prepared = prepared with { ProjectRequest = prepared.ProjectRequest with { ProjectedAtUtc = DateTimeOffset.UtcNow } };
            return await execute(prepared, actor).ConfigureAwait(false);
        }
        catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
        catch (InvalidOperationException ex) { return Results.Conflict(new { error = ex.Message }); }
        catch (UnauthorizedAccessException) { return EndpointHelpers.Forbidden(); }
        catch (LedgerBookServiceException ex) { return MapServiceException(ex); }
    }

    private static string FactorPaydownPreviewFingerprint(OpenLotFactorPaydownInstructionDto instruction, ProjectedAccountingEffectDto effect)
        // Generation time is observational, not an economic input. Everything reviewed, including
        // exact lots/versions, source evidence, ownership and proposed account lines, remains bound.
        => AssetAccountingEventSpineValidator.CanonicalPayloadFingerprint(new
        {
            Instruction = instruction with { Lineage = instruction.Lineage with { GeneratedAtUtc = instruction.EconomicEvent.OccurredAtUtc } },
            Effect = effect
        });
}
