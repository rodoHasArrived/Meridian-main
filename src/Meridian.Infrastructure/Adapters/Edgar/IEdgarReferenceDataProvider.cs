using Meridian.Contracts.SecurityMaster;

namespace Meridian.Infrastructure.Adapters.Edgar;

public interface IEdgarReferenceDataProvider
{
    Task<IReadOnlyList<EdgarTickerAssociation>> FetchTickerAssociationsAsync(CancellationToken ct = default);

    Task<IReadOnlyList<EdgarFilerRecord>> FetchBulkSubmissionsAsync(int? maxFilers = null, CancellationToken ct = default);

    Task<IReadOnlyList<EdgarXbrlFact>> FetchBulkCompanyFactsAsync(
        IReadOnlySet<string>? ciks = null,
        int? maxFilers = null,
        CancellationToken ct = default);

    Task<EdgarFilerRecord?> FetchSubmissionAsync(string cik, CancellationToken ct = default);

    Task<IReadOnlyList<EdgarXbrlFact>> FetchCompanyFactsAsync(string cik, CancellationToken ct = default);

    Task<EdgarSecurityDataRecord> FetchSecurityDataAsync(
        EdgarFilerRecord filer,
        int? maxFilings = null,
        CancellationToken ct = default);
}
