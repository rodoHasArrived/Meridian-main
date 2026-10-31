using Meridian.Contracts.AccountingSystem;
using Meridian.DataIntegration.Credentials;
using Meridian.ProviderSdk.AccountingSystem;

namespace Meridian.FinancialOperations.AccountingSystem;

public sealed partial class AccountingSystemIntegrationService
{
    private static async Task<AccountingSystemConnectionMetadataDto> ReadConnectionMetadataAsync(
        string providerId, IAccountingSystemConnectionMetadataProvider provider, CancellationToken ct)
    {
        try
        {
            return await provider.GetConnectionMetadataAsync(ct).ConfigureAwait(false);
        }
        catch (ProviderCredentialOwnershipRequiredException error)
        {
            // Only the deliberately unsupported ownership boundary becomes a disabled row.
            // Storage failures and corrupt configuration still propagate for investigation.
            return new(providerId, Environment: null, CompanyId: null, CompanyName: null,
                HasLocalConfig: false, HasRefreshToken: false, LastConnectedAtUtc: null,
                StatusLabel: "Owned accounting import unavailable", StatusDetail: error.Message,
                MissingFields: []);
        }
    }
}
