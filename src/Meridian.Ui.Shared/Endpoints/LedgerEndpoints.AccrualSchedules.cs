using System.Globalization;
using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.Contracts.Integrity;
using Meridian.FinancialOperations.FundAdministration;
using Meridian.Identity.Auth;
using Meridian.Ledger;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>Explicit operator-reviewed fixed accruals. Decimal text is parsed only by the server.</summary>
public sealed record AccrualScheduleInput(string ScheduleId, string FundProfileId, Guid LedgerBookId,
    string EntityId, string Currency, RecurringJournalCadence Cadence, DateOnly AnchorDate,
    DateOnly EndsOn, string Amount, string DebitAccount, LedgerAccountType DebitAccountType,
    string CreditAccount, LedgerAccountType CreditAccountType, string Description,
    IReadOnlyList<JournalEvidenceReference> Evidence, int ExpectedScheduleVersion = 0,
    int ExpectedTemplateVersion = 0);
public sealed record ConfigureAccrualScheduleInput(AccrualScheduleInput Input, string PreviewFingerprint);
public sealed record RunAccrualScheduleInput(string FundProfileId, Guid LedgerBookId, string EntityId, int ExpectedVersion);

public static partial class LedgerEndpoints
{
    private static void MapAccrualScheduleEndpoints(WebApplication app, JsonSerializerOptions jsonOptions)
    {
        app.MapGet(UiApiRoutes.LedgerAccrualSchedules, async (HttpContext context) =>
        {
            var store = context.RequestServices.GetService<IRecurringJournalStore>();
            if (store is null) return ServiceUnavailable();
            var query = context.Request.Query;
            if (!Guid.TryParse(query["ledgerBookId"], out var book) || book == Guid.Empty ||
                string.IsNullOrWhiteSpace(query["fundProfileId"]) || string.IsNullOrWhiteSpace(query["entityId"]))
                return Results.BadRequest(new { error = "Select an exact fund, ledger book and entity." });
            var tenant = HttpContextWorkstationTenantContextAccessor.Resolve(context);
            try
            {
                await using var session = await store.OpenSessionAsync(context.RequestAborted).ConfigureAwait(false);
                var definitions = session.CurrentSchedules.Where(s => s.ScheduleId.StartsWith("accrual:", StringComparison.Ordinal)
                    && s.Scope.LedgerBookId == book && SameAccrual(s.Scope.FundProfileId, query["fundProfileId"])
                    && SameAccrual(s.Scope.EntityId, query["entityId"]) && SameAccrual(s.Scope.TenantId, tenant.TenantId)
                    && SameAccrual(s.Scope.CompanyId, tenant.CompanyId)).Select(s => new
                    {
                        scheduleId = s.ScheduleId, version = s.Version,
                        templateVersion = session.GetCurrentTemplate(s.Schedule.TemplateId).Version,
                        anchorDate = s.Schedule.AnchorDate, endsOn = s.Schedule.EndsOn,
                        cadence = s.Schedule.Cadence.ToString(), description = s.Schedule.Description,
                        currency = s.Scope.Currency
                    }).ToArray();
                return Results.Json(new { canConfigure = TryResolveActor(context, out _) && EndpointAuthorization.HasAnyPermission(context, UserPermission.ManageLedgerReports),
                    schedules = definitions }, jsonOptions);
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException)
            { return Results.Problem("The retained accrual schedule registry is unavailable.", statusCode: 503); }
        }).WithName("ListAccrualSchedules")
            .RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ViewLedgerReports, UserPermission.ManageLedgerReports)
            .RequireWorkstationTenantCompanyScope();

        app.MapPost(UiApiRoutes.LedgerAccrualSchedulePreview, async (HttpContext context) =>
        {
            var periods = context.RequestServices.GetService<IRecurringJournalPeriodAuthority>();
            if (periods is null) return ServiceUnavailable();
            try
            {
                var input = await context.Request.ReadFromJsonAsync<AccrualScheduleInput>(jsonOptions, context.RequestAborted).ConfigureAwait(false);
                if (input is null) return Results.BadRequest();
                var tenant = HttpContextWorkstationTenantContextAccessor.Resolve(context);
                var prepared = BuildAccrualSchedule(input, tenant.TenantId!, tenant.CompanyId!);
                var rows = new List<object>();
                foreach (var occurrence in RecurringJournalPlanner.Plan(prepared.Schedule, prepared.Template, 1201))
                {
                    var authority = await periods.ResolveAsync(prepared.Scope, occurrence.EffectiveDate, context.RequestAborted).ConfigureAwait(false);
                    rows.Add(new { occurrence.EffectiveDate, authority.PeriodId, authority.Version, authority.IsOpen,
                        authority.Blocker, authority.LockOwner, authority.ReopenPath, amount = input.Amount,
                        debitAccount = input.DebitAccount, creditAccount = input.CreditAccount });
                }
                return Results.Json(new { fingerprint = AccrualFingerprint(input, tenant.TenantId!, tenant.CompanyId!),
                    occurrences = rows, currency = input.Currency }, jsonOptions);
            }
            catch (Exception ex) when (ex is ArgumentException or JsonException or LedgerValidationException)
            { return Results.BadRequest(new { error = ex.Message }); }
            catch (Exception ex) when (ex is InvalidOperationException or IOException)
            { return ApiProblemDetails.Conflict(context, ex.Message); }
        }).WithName("PreviewAccrualSchedule")
            .RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ViewLedgerReports, UserPermission.ManageLedgerReports)
            .RequireWorkstationTenantCompanyScope().RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        app.MapPost(UiApiRoutes.LedgerAccrualScheduleConfigure, async (HttpContext context) =>
        {
            if (!TryResolveActor(context, out var actor)) return EndpointHelpers.Forbidden();
            var store = context.RequestServices.GetService<IRecurringJournalStore>();
            var periods = context.RequestServices.GetService<IRecurringJournalPeriodAuthority>();
            if (store is null || periods is null) return ServiceUnavailable();
            try
            {
                var request = await context.Request.ReadFromJsonAsync<ConfigureAccrualScheduleInput>(jsonOptions, context.RequestAborted).ConfigureAwait(false);
                if (request?.Input is null) return Results.BadRequest();
                var input = request.Input;
                var tenant = HttpContextWorkstationTenantContextAccessor.Resolve(context);
                if (!Sha256Digest.FixedEquals(request.PreviewFingerprint, AccrualFingerprint(input, tenant.TenantId!, tenant.CompanyId!)))
                    return Results.Conflict(new { code = "ACCRUAL_PREVIEW_STALE", error = "The reviewed accrual inputs changed. Preview again." });
                var prepared = BuildAccrualSchedule(input, tenant.TenantId!, tenant.CompanyId!);
                await using var session = await store.OpenSessionAsync(context.RequestAborted).ConfigureAwait(false);
                var existing = session.CurrentSchedules.SingleOrDefault(s => SameAccrual(s.ScheduleId, prepared.Schedule.ScheduleId));
                if (existing is not null && existing.Scope != prepared.Scope) return EndpointHelpers.Forbidden();
                if (existing is not null && existing.Version == input.ExpectedScheduleVersion + 1)
                {
                    var retainedTemplate = session.GetCurrentTemplate(existing.Schedule.TemplateId);
                    if (retainedTemplate.Version == input.ExpectedTemplateVersion + 1 &&
                        AccrualCommandContent(existing.Schedule, retainedTemplate.Template, existing.Scope, existing.Evidence) ==
                        AccrualCommandContent(RecurringJournalScheduleSnapshot.Capture(prepared.Schedule), prepared.Template, prepared.Scope, input.Evidence))
                        return Results.Json(new { existing.ScheduleId, existing.Version, existing.ContentHash }, jsonOptions);
                }
                foreach (var occurrence in RecurringJournalPlanner.Plan(prepared.Schedule, prepared.Template, 1201))
                {
                    var period = await periods.ResolveAsync(prepared.Scope, occurrence.EffectiveDate, context.RequestAborted).ConfigureAwait(false);
                    if (!period.IsOpen) return Results.Conflict(new { code = "ACCRUAL_PREVIEW_STALE", error = period.Blocker ?? "A target period is closed." });
                }
                var now = (context.RequestServices.GetService<TimeProvider>() ?? TimeProvider.System).GetUtcNow();
                var schedule = new RecurringJournalSchedule(prepared.Schedule.ScheduleId, prepared.Template.TemplateId,
                    prepared.Schedule.LedgerKey, input.Cadence, input.AnchorDate, actor, now,
                    prepared.Schedule.Parameters, endsOn: input.EndsOn, dimensions: prepared.Schedule.Dimensions,
                    description: input.Description);
                var saved = await session.ConfigureAsync(schedule, prepared.Template, prepared.Scope, input.Evidence, actor,
                    input.ExpectedScheduleVersion, input.ExpectedTemplateVersion, now, context.RequestAborted).ConfigureAwait(false);
                return Results.Json(new { saved.ScheduleId, saved.Version, saved.ContentHash }, jsonOptions);
            }
            catch (Exception ex) when (ex is ArgumentException or JsonException or LedgerValidationException)
            { return Results.BadRequest(new { error = ex.Message }); }
            catch (RecurringJournalDefinitionChangedException ex)
            { return Results.Conflict(new { code = "ACCRUAL_DEFINITION_STALE", error = ex.Message }); }
            catch (Exception ex) when (ex is IOException or InvalidOperationException)
            { return ApiProblemDetails.Conflict(context, ex.Message); }
        }).WithName("ConfigureAccrualSchedule").RequirePermission(UserPermission.ManageLedgerReports)
            .RequireAuthenticatedSession().RequireWorkstationTenantCompanyScope().RequireFundScopedWriteTenant()
            .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        app.MapPost(UiApiRoutes.LedgerAccrualScheduleRun, async (string scheduleId, HttpContext context) =>
        {
            if (!TryResolveActor(context, out _)) return EndpointHelpers.Forbidden();
            var store = context.RequestServices.GetService<IRecurringJournalStore>();
            var runner = context.RequestServices.GetService<RecurringJournalRunner>();
            if (store is null || runner is null) return ServiceUnavailable();
            try
            {
                var request = await context.Request.ReadFromJsonAsync<RunAccrualScheduleInput>(jsonOptions, context.RequestAborted).ConfigureAwait(false);
                if (request is null || !scheduleId.StartsWith("accrual:", StringComparison.Ordinal)) return Results.BadRequest();
                var tenant = HttpContextWorkstationTenantContextAccessor.Resolve(context);
                RecurringScheduleDefinition definition;
                await using (var session = await store.OpenSessionAsync(context.RequestAborted).ConfigureAwait(false))
                    definition = session.GetCurrentSchedule(scheduleId);
                if (!SameAccrual(definition.Scope.TenantId, tenant.TenantId) || !SameAccrual(definition.Scope.CompanyId, tenant.CompanyId)
                    || !SameAccrual(definition.Scope.FundProfileId, request.FundProfileId) || definition.Scope.LedgerBookId != request.LedgerBookId
                    || !SameAccrual(definition.Scope.EntityId, request.EntityId)) return EndpointHelpers.Forbidden();
                if (definition.Version != request.ExpectedVersion) return ApiProblemDetails.Conflict(context, "The schedule changed. Refresh before running.");
                var now = (context.RequestServices.GetService<TimeProvider>() ?? TimeProvider.System).GetUtcNow();
                return Results.Json(await runner.RunDueAsync(now, tenant.TenantId, tenant.CompanyId, true,
                    context.RequestAborted, scheduleId, definition.Scope, request.ExpectedVersion).ConfigureAwait(false), jsonOptions);
            }
            catch (JsonException ex) { return Results.BadRequest(new { error = ex.Message }); }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException)
            { return ApiProblemDetails.Conflict(context, ex.Message); }
        }).WithName("RunAccrualSchedule").RequirePermission(UserPermission.ManageLedgerReports)
            .RequireAuthenticatedSession().RequireWorkstationTenantCompanyScope().RequireFundScopedWriteTenant()
            .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);
    }

    internal static (RecurringJournalSchedule Schedule, JournalTemplate Template, RecurringJournalScope Scope)
        BuildAccrualSchedule(AccrualScheduleInput input, string tenant, string company)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(input.ScheduleId);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.Description);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.FundProfileId);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.EntityId);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.DebitAccount);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.CreditAccount);
        if (input.LedgerBookId == Guid.Empty || input.Currency?.Length != 3 || !Enum.IsDefined(input.Cadence)
            || !Enum.IsDefined(input.DebitAccountType) || !Enum.IsDefined(input.CreditAccountType)
            || input.EndsOn < input.AnchorDate || input.ExpectedScheduleVersion < 0 || input.ExpectedTemplateVersion < 0)
            throw new ArgumentException("Supply valid scope, cadence, versions and an end date on or after the anchor date.");
        if (input.Amount is null || !System.Text.RegularExpressions.Regex.IsMatch(input.Amount, @"^\d+(\.\d+)?$")
            || !decimal.TryParse(input.Amount, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount) || amount <= 0)
            throw new ArgumentException("Enter a positive exact decimal amount without exponents or separators.");
        var parts = input.Amount.Split('.');
        var normalized = parts[0].TrimStart('0');
        if (normalized.Length == 0) normalized = "0";
        if (parts.Length == 2 && parts[1].TrimEnd('0') is { Length: > 0 } fraction) normalized += "." + fraction;
        if (normalized != amount.ToString("0.############################", CultureInfo.InvariantCulture))
            throw new ArgumentException("The amount exceeds exact decimal precision; rounding is refused.");
        var issue = RecurringJournalEvidenceGuard.ValidateSources(input.Evidence);
        if (issue is not null) throw new ArgumentException(issue);
        var scheduleId = input.ScheduleId.StartsWith("accrual:", StringComparison.Ordinal) ? input.ScheduleId : "accrual:" + input.ScheduleId;
        var templateId = scheduleId + ":template";
        var template = new JournalTemplate(templateId, input.Description, "Reviewed fixed accrual per occurrence", [
            new(new(input.DebitAccount, input.DebitAccountType), JournalTemplateSide.Debit, AmountParameter: "amount"),
            new(new(input.CreditAccount, input.CreditAccountType), JournalTemplateSide.Credit, AmountParameter: "amount")]);
        var dimensions = new LedgerLineDimensionSet(FundId: input.FundProfileId, EntityId: input.EntityId);
        var schedule = new RecurringJournalSchedule(scheduleId, templateId, new(input.FundProfileId, input.LedgerBookId.ToString("D")),
            input.Cadence, input.AnchorDate, "server-preview", DateTimeOffset.UnixEpoch,
            new Dictionary<string, decimal> { ["amount"] = amount }, endsOn: input.EndsOn,
            dimensions: dimensions, description: input.Description);
        if (RecurringJournalPlanner.Plan(schedule, template, 1201).Count > 1200)
            throw new ArgumentException("Limit an accrual schedule to 1200 occurrences.");
        return (schedule, template, new(input.FundProfileId, input.LedgerBookId, input.EntityId, input.Currency!, tenant, company));
    }

    private static string AccrualCommandContent(RecurringJournalScheduleSnapshot schedule, JournalTemplate template,
        RecurringJournalScope scope, IReadOnlyList<JournalEvidenceReference> evidence)
        => JsonSerializer.Serialize(new { schedule = schedule with { CreatedBy = "", CreatedAtUtc = DateTimeOffset.UnixEpoch },
            template, scope, evidence = evidence.Select(item => item.Normalize()).ToArray() });

    private static string AccrualFingerprint(AccrualScheduleInput input, string tenant, string company)
        => Sha256Digest.ComputeUtf8(JsonSerializer.Serialize(new { input, tenant, company }));
    private static bool SameAccrual(string? left, string? right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
