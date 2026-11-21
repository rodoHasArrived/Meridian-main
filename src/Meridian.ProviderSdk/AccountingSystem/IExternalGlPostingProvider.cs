using Meridian.Contracts.AccountingSystem;

namespace Meridian.ProviderSdk.AccountingSystem;

/// <summary>Posts immutable, balanced Meridian documents and resolves their outcome by deterministic key.</summary>
public interface IExternalGlPostingProvider
{
    string ProviderId { get; }

    /// <summary>Resolves a verified, explicitly owned connection without falling back to provider-wide credentials.</summary>
    Task<ExternalGlDestination> ResolveDestinationAsync(string tenantId, string connectionId, CancellationToken ct);

    /// <summary>Checks that a document can be represented losslessly before operator review.</summary>
    void ValidateDocument(ExternalGlPostingDocument document);

    /// <summary>Creates a document using its retained idempotency key on every attempt.</summary>
    Task<ExternalGlProviderReceipt> PostAsync(
        string tenantId, ExternalGlDestination destination, ExternalGlPostingDocument document, CancellationToken ct);

    /// <summary>
    /// Queries by the Meridian idempotency key or by its deterministic searchable provider reference.
    /// Return NotFound only after a successful, conclusive lookup. Replays after an ambiguous create
    /// use the exact same document and key, which the provider must deduplicate.
    /// </summary>
    Task<ExternalGlProviderReceipt> FindAsync(
        string tenantId, ExternalGlDestination destination, ExternalGlPostingDocument document, CancellationToken ct);
}
