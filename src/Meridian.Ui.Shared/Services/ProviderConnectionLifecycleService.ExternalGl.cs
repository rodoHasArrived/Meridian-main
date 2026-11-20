using Meridian.Contracts.AccountingSystem;
using Meridian.DataIntegration.AccountingSystem.QuickBooks;

namespace Meridian.Ui.Shared.Services;

public sealed partial class ProviderConnectionLifecycleService : IQuickBooksPostingConnectionResolver
{
    QuickBooksPostingConnection IQuickBooksPostingConnectionResolver.Resolve(string tenantId, string connectionId)
    {
        var selected = ForConnection(connectionId, tenantId, QuickBooksOnlineAccountingProvider.Id);
        var scope = selected._ownershipScope ?? throw new UnauthorizedAccessException("Select a tenant-owned QuickBooks connection.");
        return new QuickBooksPostingConnection(
            new ExternalGlDestination(QuickBooksOnlineAccountingProvider.Id, scope.ConnectionId, scope.ExternalAccountId, scope.Environment),
            new QuickBooksOnlineProviderCredentialConnectionStore(selected._credentialStore));
    }
}
