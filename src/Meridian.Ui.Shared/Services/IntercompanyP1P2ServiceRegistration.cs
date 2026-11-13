using Meridian.Contracts.Calendars;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Services;
using Meridian.Contracts.Tenancy;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.Intercompany;
using Meridian.Identity.Auth;
using Meridian.Storage.Ledger;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Meridian.Ui.Shared.Services;

internal static class IntercompanyP1P2ServiceRegistration
{
    internal static void AddIntercompanyP1P2Services(this IServiceCollection services)
    {
        services.TryAddSingleton<IEntityLifecycleExternalReviewEvidenceAuthority, WorkstationEntityLifecycleExternalReviewEvidenceAuthority>();
        services.TryAddSingleton<IIntercompanyStructuredTermsPreviewService>(sp =>
            sp.GetService<IBusinessDayCalendarSource>() is { } calendars
                ? new Meridian.Application.FundStructure.IntercompanyStructuredTermsPreviewService(calendars) : null!);
        services.TryAddSingleton<IIntercompanyObligationMonitorService>(sp =>
            sp.GetService<IIntercompanyBalanceScheduleSource>() is { } balances
            && sp.GetService<IIntercompanyAgreementService>() is { } agreements
            && sp.GetService<IEntityLifecycleService>() is { } lifecycle
            && sp.GetService<ILedgerJournalStore>() is { } journals
            && sp.GetService<IIntercompanyAgreementAccessAuthority>() is { } access
                ? new IntercompanyObligationMonitorService(balances, agreements, lifecycle, journals, access, sp.GetService<TimeProvider>()) : null!);
        services.TryAddSingleton<IIntercompanyCloseAdvisorySource, IntercompanyCloseAdvisorySource>();
    }
}

/// <summary>Operation-specific reporting grants; authoring or compliance access never grants accounting observations.</summary>
internal sealed class IntercompanyLedgerReadPermissionScope : IDisposable
{
    private readonly HttpContext _http;
    private readonly bool _hadPrevious;
    private readonly object? _previous;

    private IntercompanyLedgerReadPermissionScope(HttpContext http)
    {
        _http = http;
        _hadPrevious = http.Items.TryGetValue(HttpContextIntercompanyAgreementAccessAuthority.RequiredPermissionKey, out _previous);
        http.Items[HttpContextIntercompanyAgreementAccessAuthority.RequiredPermissionKey] =
            new[] { UserPermission.ViewLedgerReports, UserPermission.ManageLedgerReports };
    }

    internal static IntercompanyLedgerReadPermissionScope? TryEnter(HttpContext http) =>
        EndpointAuthorization.HasAnyPermission(http, UserPermission.ViewLedgerReports,
            UserPermission.ManageLedgerReports, UserPermission.AdminMaintenance) ? new(http) : null;

    public void Dispose()
    {
        if (_hadPrevious)
            _http.Items[HttpContextIntercompanyAgreementAccessAuthority.RequiredPermissionKey] = _previous;
        else
            _http.Items.Remove(HttpContextIntercompanyAgreementAccessAuthority.RequiredPermissionKey);
    }
}

/// <summary>Advisory context is resolved at the selected close period end, with current operator access.</summary>
internal sealed class IntercompanyCloseAdvisorySource(IServiceProvider services) : IIntercompanyCloseAdvisorySource
{
    public async Task<IntercompanyObligationMonitorDto> GetAsync(CloseReadinessScopeDto scope,
        string? tenantId, string? companyId, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        IntercompanyObligationMonitorDto Unknown() => new(DateOnly.FromDateTime(now.UtcDateTime), 30, now,
            "Unavailable", false, "", [], [new("IntercompanyMonitorUnavailable", "Warning",
                "Intercompany advisory monitoring requires the selected period and retained source authority.")], null, []);
        var journals = services.GetService<ILedgerJournalStore>();
        var monitor = services.GetService<IIntercompanyObligationMonitorService>();
        var ownership = services.GetService<IFundProfileTenancyRegistry>();
        var http = services.GetService<IHttpContextAccessor>()?.HttpContext;
        if (journals is null || monitor is null || ownership is null || http is null
            || scope.LedgerBookId is not { } bookId || !Guid.TryParse(scope.PeriodId, out var periodId)
            || string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(companyId))
            return Unknown();
        using var reportAccess = IntercompanyLedgerReadPermissionScope.TryEnter(http);
        if (reportAccess is null)
            return Unknown();
        var caller = HttpContextWorkstationTenantContextAccessor.Resolve(http);
        if (caller.Actor is null || caller.TenantId != tenantId || caller.CompanyId != companyId)
            return Unknown();
        var book = await journals.GetLedgerBookAsync(bookId, ct);
        var owner = book is null ? null : await ownership.ResolveAsync(book.FundProfileId, ct);
        var period = await journals.GetPeriodAsync(periodId, ct);
        if (book is null || owner is null || !owner.IsHeldBy(tenantId) || owner.CompanyId != companyId
            || period is null || period.LedgerBookId != bookId || scope.FundProfileId != book.FundProfileId
            || !Guid.TryParse(scope.EntityId, out var entityId) || entityId != book.FundStructureNodeId)
            return Unknown();
        var advisory = await monitor.EvaluateAsync(new(tenantId, caller.Actor), companyId,
            new(book.FundProfileId, bookId, period.EndDate, 30, entityId), ct);
        return advisory with { BalanceSchedule = null };
    }
}
