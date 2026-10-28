using Meridian.Contracts.SecurityMaster;

namespace Meridian.Infrastructure.Adapters.Edgar;

/// <summary>
/// Fetches SEC EDGAR ticker, filer, XBRL fact, and security reference data.
/// </summary>
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
