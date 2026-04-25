using Meridian.Contracts.SecurityMaster;

namespace Meridian.Storage.SecurityMaster;

public interface IEdgarReferenceDataStore
{
    Task SaveTickerAssociationsAsync(IReadOnlyList<EdgarTickerAssociation> associations, CancellationToken ct = default);

    Task<IReadOnlyList<EdgarTickerAssociation>> LoadTickerAssociationsAsync(CancellationToken ct = default);

    Task SaveFilerAsync(EdgarFilerRecord record, CancellationToken ct = default);

    Task<EdgarFilerRecord?> LoadFilerAsync(string cik, CancellationToken ct = default);

    Task SaveFactsAsync(EdgarFactsRecord record, CancellationToken ct = default);

    Task<EdgarFactsRecord?> LoadFactsAsync(string cik, CancellationToken ct = default);

    Task SaveSecurityDataAsync(EdgarSecurityDataRecord record, CancellationToken ct = default);

    Task<EdgarSecurityDataRecord?> LoadSecurityDataAsync(string cik, CancellationToken ct = default);
}
