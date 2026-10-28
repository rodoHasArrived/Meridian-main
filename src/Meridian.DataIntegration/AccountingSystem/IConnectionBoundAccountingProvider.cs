using Meridian.DataIntegration.Credentials;
using Meridian.ProviderSdk.AccountingSystem;

namespace Meridian.DataIntegration.AccountingSystem;

/// <summary>Creates a verifier whose reads, token rotation and results use only the supplied credential store.</summary>
public interface IConnectionBoundAccountingProvider
{
    IAccountingSystemConnectionVerifier BindCredentials(IProviderCredentialStore credentialStore);
}
