using System.Text.Json;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.Api;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Tenancy;
using Meridian.FinancialOperations.Ledger;
using Meridian.Identity.Auth;
using Meridian.Storage.Ledger;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

public static partial class LedgerEndpoints
{
    private static void MapCanonicalAmortizationEndpoints(WebApplication app, JsonSerializerOptions jsonOptions)
    {
        var exactOptions = new JsonSerializerOptions(jsonOptions);
        exactOptions.Converters.Insert(0, new AccountOversightDecimalJsonConverter());
        app.MapPost(UiApiRoutes.LedgerCanonicalAmortizationPreparation, async (Guid ledgerBookId,
            PrepareCanonicalAmortizationRequestDto request, HttpContext context) =>
            await ExecuteCanonicalAmortizationAsync(context, ledgerBookId, request, async (prepared, _) =>
            {
                await Task.CompletedTask.ConfigureAwait(false);
                return Results.Json(new CanonicalAmortizationPreparationDto(prepared.Preview.Instruction,
                    prepared.Preview.Projection, prepared.Fingerprint, prepared.Project.ProjectedEffect.Lines, prepared.Project.Scope, prepared.Occurrences), exactOptions);
            }).ConfigureAwait(false))
            .WithName("PrepareCanonicalLotAmortization")
            .RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending, UserPermission.ManageLedgerReports)
            .RequireWorkstationTenantCompanyScope().RequireAuthenticatedSession()
            .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy)
            .Produces<CanonicalAmortizationPreparationDto>();

        app.MapPost(UiApiRoutes.LedgerCanonicalAmortizationDrafts, async (Guid ledgerBookId,
            CreateCanonicalAmortizationDraftRequestDto request, HttpContext context) =>
            await ExecuteCanonicalAmortizationAsync(context, ledgerBookId, request.Request, async (prepared, actor) =>
            {
                if (string.IsNullOrWhiteSpace(request.Description))
                    return Results.BadRequest(new { error = "A reviewed journal description is required." });
                if (request.ExpectedPreparationFingerprint != prepared.Fingerprint)
                    return Results.Conflict(new { error = "Amortization inputs changed. Prepare and review again.", code = "AMORTIZATION_PREVIEW_STALE" });
                if (prepared.Project.EventAmount <= 0m)
                    return Results.Conflict(new { error = "No carrying-value movement is due for this occurrence." });
                var spine = ResolveAssetAccountingEventSpineService(context);
                if (spine is null || ResolveManualJournalEntryWorkbenchService(context) is not ManualJournalEntryWorkbenchService workbench)
                    return ServiceUnavailable("The governed asset journal review workflow is unavailable.");
                var project = prepared.Project;
                var retained = await spine.GetLatestAsync(project.EconomicEvent.EventId, 1, context.RequestAborted).ConfigureAwait(false);
                if (retained is not null)
                {
                    if (retained.PostedJournalImpact is not null || retained.EventKind != project.EventKind ||
                        !SameAmortizationProjection(retained, project))
                        return Results.Conflict(new { error = "The retained occurrence differs or is already posted. Inspect its original journal." });
                    if (retained.DraftedCandidate is { } source && retained.DraftedCandidateResult is { } candidate)
                    {
                        if (retained.DraftedLotMutation is null || source.Actor != actor || source.Description != request.Description.Trim() ||
                            AssetLotMutationInstructionValidator.Fingerprint(retained.DraftedLotMutation) !=
                            AssetLotMutationInstructionValidator.Fingerprint(prepared.Mutation))
                            return Results.Conflict(new { error = "The occurrence already has a different reviewed draft." });
                        var replay = new AssetAccountingPostingCandidateDto(retained, candidate);
                        await workbench.SaveAssetAccountingDraftAsync(replay, actor, context.RequestAborted).ConfigureAwait(false);
                        return Results.Json(replay, exactOptions);
                    }
                    project = project with { EconomicEvent = retained.EconomicEvent, ProjectionLineage = retained.ProjectionLineage };
                }
                else
                    retained = (await spine.ProjectAsync(project, context.RequestAborted).ConfigureAwait(false)).Spine;
                var result = await spine.BuildPostingCandidateAsync(new AssetAccountingPostingCandidateRequestDto(
                    project.EventKind, project.Scope, project.EconomicEvent, project.ProjectionLineage,
                    project.EventAmount, project.Currency, actor, DateTimeOffset.UtcNow, request.Description.Trim(),
                    retained.SpineVersion, project.ExpectedPeriodVersion, project.RetainedEvidence, prepared.Mutation), context.RequestAborted).ConfigureAwait(false);
                await workbench.SaveAssetAccountingDraftAsync(result, actor, context.RequestAborted).ConfigureAwait(false);
                return Results.Json(result, exactOptions);
            }).ConfigureAwait(false))
            .WithName("CreateCanonicalLotAmortizationDraft")
            .RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending, UserPermission.ManageLedgerReports)
            .RequireWorkstationTenantCompanyScope().RequireAuthenticatedSession()
            .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy)
            .Produces<AssetAccountingPostingCandidateDto>();
    }

    private sealed record PreparedCanonicalAmortization(CanonicalLotAmortizationPreview Preview,
        ProjectAssetAccountingEventRequestDto Project, AssetLotMutationInstructionDto Mutation, string Fingerprint, IReadOnlyList<CanonicalAmortizationOccurrenceDto> Occurrences);

    private static async Task<IResult> ExecuteCanonicalAmortizationAsync(HttpContext context, Guid ledgerBookId,
        PrepareCanonicalAmortizationRequestDto? request, Func<PreparedCanonicalAmortization, string, Task<IResult>> execute)
    {
        if (!HasLedgerMutationPermission(context) || !TryResolveActor(context, out var actor))
            return EndpointHelpers.Forbidden();
        var tenant = HttpContextWorkstationTenantContextAccessor.Resolve(context);
        if (!HasAccountingPackageTenantScope(tenant)) return EndpointHelpers.Forbidden();
        if (request is null || request.LedgerBookId != ledgerBookId || request.TaxLotRecordId == Guid.Empty ||
            request.SecurityEvidence is null || request.EventEvidence is null ||
            !RetainedEvidenceIdentityValidator.IsComplete(request.SecurityEvidence) ||
            !RetainedEvidenceIdentityValidator.IsComplete(request.EventEvidence) ||
            request.EventEvidence.SubjectType != AssetAccountingEvidenceSubjects.Event ||
            !Guid.TryParseExact(request.EventEvidence.SubjectId, "D", out var eventId) || eventId == Guid.Empty ||
            request.EventEvidence.EffectiveDate != request.AsOfDate ||
            string.IsNullOrWhiteSpace(request.AssetAccountPath) || string.IsNullOrWhiteSpace(request.IncomeAccountPath) ||
            request.AssetAccountPath == request.IncomeAccountPath)
            return Results.BadRequest(new { error = "Supply matching book, lot, date, complete reviewed reference and occurrence evidence, and distinct asset/income account paths." });
        var books = ResolveService(context);
        var periods = ResolveJournalStore(context);
        var registry = context.RequestServices.GetService<IFundProfileTenancyRegistry>();
        var service = context.RequestServices.GetService<CanonicalLotAmortizationService>();
        if (books is null || periods is null || registry is null || service is null)
            return ServiceUnavailable("Authoritative book, period, tenancy and canonical amortization services are required.");
        try
        {
            var book = await books.GetBookAsync(ledgerBookId, context.RequestAborted).ConfigureAwait(false);
            if (book is null || book.LedgerBookId != ledgerBookId || book.FundProfileId != request.FundProfileId) return EndpointHelpers.Forbidden();
            var owner = await registry.ResolveAsync(book.FundProfileId, context.RequestAborted).ConfigureAwait(false);
            if (owner is null || owner.FundProfileId != book.FundProfileId || !CloseWorkflowOwnerMatches(owner, tenant))
                return EndpointHelpers.Forbidden();
            var period = await periods.GetPeriodAsync(request.PeriodId, context.RequestAborted).ConfigureAwait(false);
            if (period is null || period.LedgerBookId != ledgerBookId) return EndpointHelpers.Forbidden();
            if (book.AccountingBasis != request.AccountingBasis || period.Version != request.ExpectedPeriodVersion ||
                period.Status != "Open" || request.AsOfDate < period.StartDate || request.AsOfDate > period.EndDate)
                return Results.Conflict(new { error = "The accounting basis, open period, occurrence date or period version changed." });
            var preview = await service.PreviewExtendedAsync(ledgerBookId, request.TaxLotRecordId, request.AsOfDate,
                request.SecurityEvidence, request.AmortizeRemainingFace, request.AutomaticYieldToWorst, context.RequestAborted).ConfigureAwait(false);
            var instruction = preview.Instruction;
            if (instruction.ExpectedLot.Acquisition.BondIncome is not null)
                return Results.Conflict(new
                {
                    error = "This lot has an adopted bond-income policy. Use its account-owned bond-accounting preview and preparation workflow; legacy amortization is unavailable.",
                    code = "ADOPTED_BOND_INCOME_REQUIRED"
                });
            var months = request.ScheduleFrequency switch { "Monthly" => 1, "Quarterly" => 3, "Annual" => 12, _ => 0 };
            if (months == 0 || request.ScheduleEndDate < request.AsOfDate)
                return Results.BadRequest(new { error = "Choose Monthly, Quarterly or Annual and an end date on or after the occurrence." });
            var occurrences = new List<CanonicalAmortizationOccurrenceDto>
            { new(request.AsOfDate, preview.Projection.TransactionCostBasis, preview.Projection.FunctionalCostBasis) };
            if (request.ScheduleEndDate is { } end)
            {
                for (var index = 1; request.AsOfDate.AddMonths(index * months) <= end; index++)
                {
                    if (index >= 120) return Results.BadRequest(new { error = "Limit schedule previews to 120 occurrences." });
                    var date = request.AsOfDate.AddMonths(index * months);
                    var row = OpenLotAmortization.Project(instruction with { AsOfDate = date });
                    occurrences.Add(new(date, row.TransactionCostBasis, row.FunctionalCostBasis));
                }
            }
            var lot = instruction.ExpectedLot;
            var currency = lot.Acquisition.FunctionalCurrency;
            var evidence = lot.Acquisition.Evidence.Concat([request.SecurityEvidence, request.EventEvidence]).Distinct().ToArray();
            var dimensions = new LedgerDimensionSetDto(FundId: book.FundProfileId, BookId: ledgerBookId.ToString("D"), InstrumentId: lot.SecurityId)
                { PositionId = lot.BookPositionId };
            var scope = new AssetAccountingEventScopeDto(lot.SecurityId, instruction.Security.Version, lot.BookPositionId,
                instruction.ExpectedBookPositionVersion, ledgerBookId, request.PeriodId, book.AccountingBasis, book.FundProfileId,
                tenant.TenantId, tenant.CompanyId, dimensions);
            var now = DateTimeOffset.UtcNow;
            var economicEvent = new EconomicEventReferenceDto(eventId, AssetAccountingEventTypeNames.For(AssetAccountingEventKindDto.DepreciationAmortization),
                1, request.AsOfDate, request.EventEvidence.RetainedAtUtc, request.EventEvidence.SourceSystem,
                request.EventEvidence.SourceReference, SourceContentHash: request.EventEvidence.ContentHashSha256,
                EvidenceLinks: evidence.Select(item => item.EvidenceUri).ToArray())
                { SecurityId = lot.SecurityId, BookPositionId = lot.BookPositionId, RetainedEvidence = evidence };
            var lineage = new ProjectionLineageDto(eventId, null, "canonical-lot-amortization", instruction.CalculationVersion!,
                instruction.CalculationVersion!, "base", request.AsOfDate, now, economicEvent.SourceDomain, economicEvent.SourceEntityId,
                economicEvent, TermsVersion: instruction.Security.Version.ToString(), TermsHash: OpenLotAmortization.SecurityHash(instruction.Security),
                EvidenceLinks: economicEvent.EvidenceLinks) { BookPositionId = lot.BookPositionId, RetainedEvidence = evidence };
            var movement = preview.Projection.FunctionalMovement;
            var amount = Math.Abs(movement);
            var effect = new ProjectedAccountingEffectDto(lineage.ProjectionRunId, lineage.ModelKey, lineage.ModelVersion, request.AsOfDate,
                amount, amount, currency,
                [new(request.AssetAccountPath, Math.Max(movement, 0m), Math.Max(-movement, 0m), currency, Dimensions: dimensions),
                 new(request.IncomeAccountPath, Math.Max(-movement, 0m), Math.Max(movement, 0m), currency, Dimensions: dimensions)]);
            var project = new ProjectAssetAccountingEventRequestDto(AssetAccountingEventKindDto.DepreciationAmortization, scope,
                economicEvent, lineage, effect, amount, currency, request.ExpectedPeriodVersion, actor, now, evidence);
            var mutation = new AssetLotMutationInstructionDto(AssetLotMutationIntentDto.Amortize, AssetAccountId: request.AssetAccountPath, Amortization: instruction);
            var fingerprint = AssetAccountingEventSpineValidator.CanonicalPayloadFingerprint(new { Request = request, Instruction = instruction, Effect = effect, Scope = scope });
            return await execute(new(preview, project, mutation, fingerprint, occurrences), actor).ConfigureAwait(false);
        }
        catch (UnauthorizedAccessException) { return EndpointHelpers.Forbidden(); }
        catch (NotSupportedException ex) { return ServiceUnavailable(ex.Message); }
        catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
        catch (InvalidOperationException ex) { return Results.Conflict(new { error = ex.Message }); }
        catch (LedgerBookServiceException ex) { return MapServiceException(ex); }
    }

    private static bool SameAmortizationProjection(AssetAccountingEventSpineDto retained, ProjectAssetAccountingEventRequestDto project)
        => AssetAccountingEventSpineValidator.CanonicalPayloadFingerprint(new { retained.Scope, retained.EconomicEvent, retained.ProjectedEffect, retained.EventAmount, retained.Currency }) ==
           AssetAccountingEventSpineValidator.CanonicalPayloadFingerprint(new { project.Scope, project.EconomicEvent, project.ProjectedEffect, project.EventAmount, project.Currency });
}
