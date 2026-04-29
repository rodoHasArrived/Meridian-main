namespace Meridian.Contracts.SecurityMaster;

/// <summary>
/// Request for EDGAR reference-data ingest.
/// </summary>
public sealed record EdgarIngestRequest(
    string Scope = "all-filers",
    bool IncludeXbrl = false,
    string? Cik = null,
    int? MaxFilers = null,
    bool DryRun = false,
    bool IncludeFilingDocuments = false);

/// <summary>
/// Summary returned after an EDGAR ingest run.
/// </summary>
public sealed record EdgarIngestResult(
    int FilersProcessed,
    int TickerAssociationsStored,
    int FactsStored,
    int SecurityDataStored,
    int SecuritiesCreated,
    int SecuritiesAmended,
    int SecuritiesSkipped,
    int ConflictsDetected,
    bool DryRun,
    IReadOnlyList<string> Errors);

/// <summary>
/// Local EDGAR filer reference record derived from the SEC submissions API.
/// </summary>
public sealed record EdgarFilerRecord(
    string Cik,
    string Name,
    string? EntityType,
    string? Sic,
    string? SicDescription,
    string? Category,
    string? StateOfIncorporation,
    string? StateOfIncorporationDescription,
    string? FiscalYearEnd,
    string? Ein,
    string? Description,
    string? Website,
    string? InvestorWebsite,
    string? Phone,
    EdgarAddress? BusinessAddress,
    EdgarAddress? MailingAddress,
    bool? InsiderTransactionForOwnerExists,
    bool? InsiderTransactionForIssuerExists,
    IReadOnlyList<string> Flags,
    IReadOnlyList<string> Tickers,
    IReadOnlyList<string> Exchanges,
    IReadOnlyList<EdgarFormerName> FormerNames,
    IReadOnlyList<EdgarFilingSummary> RecentFilings,
    DateTimeOffset RetrievedAtUtc);

public sealed record EdgarAddress(
    string? Street1,
    string? Street2,
    string? City,
    string? StateOrCountry,
    string? StateOrCountryDescription,
    string? ZipCode);

public sealed record EdgarFormerName(
    string Name,
    DateOnly? From,
    DateOnly? To);

/// <summary>
/// Association between an EDGAR filer and a listed ticker or mutual-fund series/class identifier.
/// </summary>
public sealed record EdgarTickerAssociation(
    string Cik,
    string Ticker,
    string? Name,
    string? Exchange,
    string? SeriesId,
    string? ClassId,
    string? SecurityType,
    string Source);

public sealed record EdgarFilingSummary(
    string Cik,
    string AccessionNumber,
    string Form,
    DateOnly? FilingDate,
    DateOnly? ReportDate,
    string? PrimaryDocument,
    string? PrimaryDocDescription,
    string? FileNumber,
    string? FilmNumber,
    string? Items,
    long? Size,
    bool? IsXbrl,
    bool? IsInlineXbrl,
    IReadOnlyList<string> SecurityDataTags);

/// <summary>
/// Security-specific data parsed from EDGAR filing documents and retained outside Security Master.
/// </summary>
public sealed record EdgarSecurityDataRecord(
    string Cik,
    IReadOnlyList<EdgarDebtOfferingTerms> DebtOfferings,
    IReadOnlyList<EdgarFundHolding> FundHoldings,
    DateTimeOffset RetrievedAtUtc);

public sealed record EdgarDebtOfferingTerms(
    string Cik,
    string AccessionNumber,
    string Form,
    string SourceDocument,
    string? SourceDocumentDescription,
    string? IssuerName,
    string? SecurityTitle,
    string? Cusip,
    string? Isin,
    string? Currency,
    decimal? PrincipalAmount,
    decimal? IssueSize,
    decimal? CouponRate,
    string? CouponType,
    string? DayCount,
    string? InterestPaymentFrequency,
    DateOnly? IssueDate,
    DateOnly? MaturityDate,
    DateOnly? FirstInterestPaymentDate,
    string? Seniority,
    string? Ranking,
    bool? IsCallable,
    DateOnly? FirstCallDate,
    decimal? OfferingPrice,
    decimal? MinimumDenomination,
    decimal? AdditionalDenomination,
    string? Trustee,
    IReadOnlyList<string> Underwriters,
    IReadOnlyList<string> RedemptionTerms,
    decimal ExtractionConfidence,
    IReadOnlyList<string> ExtractionNotes);

public sealed record EdgarFundHolding(
    string Cik,
    string AccessionNumber,
    string Form,
    string SourceDocument,
    string? HoldingName,
    string? IssuerName,
    string? Title,
    string? Cusip,
    string? Isin,
    string? Ticker,
    string? Lei,
    string? AssetCategory,
    string? IssuerCategory,
    string? Country,
    string? Currency,
    decimal? Balance,
    string? Units,
    decimal? ValueUsd,
    decimal? PercentageOfNetAssets,
    decimal? CouponRate,
    DateOnly? MaturityDate,
    bool? Restricted,
    string? FairValueLevel);

/// <summary>
/// Normalized XBRL fact retained in the local EDGAR facts store.
/// </summary>
public sealed record EdgarXbrlFact(
    string Cik,
    string Taxonomy,
    string Concept,
    string Unit,
    decimal? NumericValue,
    string? RawValue,
    DateOnly? StartDate,
    DateOnly? EndDate,
    int? FiscalYear,
    string? FiscalPeriod,
    string? Form,
    string? AccessionNumber,
    DateOnly? FiledDate,
    string? Frame);

/// <summary>
/// Common issuer-level facts selected from the full XBRL fact history for fast lookup.
/// </summary>
public sealed record IssuerFactSnapshot(
    string Cik,
    DateTimeOffset AsOfUtc,
    EdgarXbrlFact? TradingSymbol,
    EdgarXbrlFact? Security12bTitle,
    EdgarXbrlFact? SecurityExchangeName,
    EdgarXbrlFact? EntityFileNumber,
    EdgarXbrlFact? Assets,
    EdgarXbrlFact? Liabilities,
    EdgarXbrlFact? StockholdersEquity,
    EdgarXbrlFact? Revenue,
    EdgarXbrlFact? NetIncomeLoss,
    EdgarXbrlFact? EarningsPerShareDiluted,
    EdgarXbrlFact? OperatingCashFlow,
    EdgarXbrlFact? SharesOutstanding,
    EdgarXbrlFact? CommonStockSharesIssued,
    EdgarXbrlFact? WeightedAverageBasicSharesOutstanding,
    EdgarXbrlFact? WeightedAverageDilutedSharesOutstanding,
    EdgarXbrlFact? PublicFloat);

/// <summary>
/// Stored fact partition for a single EDGAR filer.
/// </summary>
public sealed record EdgarFactsRecord(
    string Cik,
    IReadOnlyList<EdgarXbrlFact> Facts,
    IssuerFactSnapshot? Snapshot,
    DateTimeOffset RetrievedAtUtc);

public sealed record EdgarReferenceDataManifest(
    DateTimeOffset UpdatedAtUtc,
    int TickerAssociationCount,
    int FilerPartitionCount,
    int FactPartitionCount,
    int SecurityDataPartitionCount);
