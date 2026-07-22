using Meridian.Contracts.Workstation;
using Meridian.Ui.Shared.Services;
using Meridian.Wpf.Models;

namespace Meridian.Wpf.ViewModels;

public sealed partial class FundLedgerViewModel
{
    private async Task<PrivateCapitalCloseCockpitDto?> LoadPrivateCapitalCloseCockpitAsync(
        FundProfileDetail activeFund,
        FundOperationsNavigationContext? context,
        CancellationToken ct)
    {
        if (_privateCapitalCloseCockpitService is null)
        {
            return null;
        }

        var scope = ResolvePrivateCapitalCloseScope(activeFund, context);
        if (scope is null)
        {
            return null;
        }

        return await _privateCapitalCloseCockpitService
            .GetCockpitAsync(
                fundProfileId: scope.FundProfileId,
                ledgerBookId: scope.LedgerBookId,
                fundAccountId: scope.FundAccountId,
                periodId: scope.PeriodId,
                entityId: scope.EntityId,
                ct: ct,
                tenantId: scope.TenantId,
                companyId: scope.CompanyId)
            .ConfigureAwait(false);
    }

    private PrivateCapitalCloseScope? ResolvePrivateCapitalCloseScope(
        FundProfileDetail activeFund,
        FundOperationsNavigationContext? context)
    {
        if (_privateCapitalCloseScope is not null &&
            !string.Equals(
                _privateCapitalCloseScope.FundProfileId,
                activeFund.FundProfileId,
                StringComparison.OrdinalIgnoreCase))
        {
            _privateCapitalCloseScope = null;
        }

        if (context is not null &&
            string.Equals(context.FundProfileId?.Trim(), activeFund.FundProfileId, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(context.TenantId) &&
            !string.IsNullOrWhiteSpace(context.CompanyId) &&
            context.LedgerBookId is { } ledgerBookId &&
            ledgerBookId != Guid.Empty &&
            !string.IsNullOrWhiteSpace(context.PeriodId) &&
            !string.IsNullOrWhiteSpace(context.EntityId))
        {
            _privateCapitalCloseScope = new PrivateCapitalCloseScope(
                context.TenantId.Trim(),
                context.CompanyId.Trim(),
                activeFund.FundProfileId,
                ledgerBookId,
                context.AccountId,
                context.PeriodId.Trim(),
                context.EntityId.Trim());
        }
        else if (context is not null &&
                 (!string.IsNullOrWhiteSpace(context.TenantId) ||
                  !string.IsNullOrWhiteSpace(context.CompanyId) ||
                  context.LedgerBookId.HasValue ||
                  !string.IsNullOrWhiteSpace(context.PeriodId) ||
                  !string.IsNullOrWhiteSpace(context.EntityId) ||
                  context.AccountId.HasValue))
        {
            _privateCapitalCloseScope = null;
        }

        return _privateCapitalCloseScope;
    }

    private sealed record PrivateCapitalCloseScope(
        string TenantId,
        string CompanyId,
        string FundProfileId,
        Guid LedgerBookId,
        Guid? FundAccountId,
        string PeriodId,
        string EntityId);

}
