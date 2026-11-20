using Meridian.Contracts.Api;
using Meridian.Contracts.Workstation;
using Microsoft.AspNetCore.WebUtilities;

namespace Meridian.Tests.TestHelpers;

internal static class AmountPassportTestRoutes
{
    internal static string PassportRoute(AmountPassportIdentityDto identity)
    {
        var scope = identity.Scope;
        return QueryHelpers.AddQueryString(
            UiApiRoutes.WorkstationAmountPassport
                .Replace("{subjectKind}", Uri.EscapeDataString(identity.SubjectKind), StringComparison.Ordinal)
                .Replace("{subjectId}", Uri.EscapeDataString(identity.SubjectId), StringComparison.Ordinal),
            new Dictionary<string, string?>
            {
                ["asOf"] = identity.AsOf, ["revision"] = identity.Revision,
                ["tenantId"] = scope.TenantId, ["companyId"] = scope.CompanyId, ["basis"] = scope.Basis,
                ["fundProfileId"] = scope.FundProfileId, ["ledgerBookId"] = scope.LedgerBookId?.ToString("D"),
                ["periodId"] = scope.PeriodId?.ToString("D"), ["entityId"] = scope.EntityId,
                ["accountId"] = scope.AccountId, ["organizationId"] = scope.OrganizationId
            });
    }
}
