# EDGAR Reference Data

Meridian treats SEC EDGAR as a reference-data source for filers and issuer facts. EDGAR filer data is stored for every processed CIK, while Security Master writes are limited to ticker-backed instruments.

Official SEC references:

- [SEC EDGAR APIs](https://www.sec.gov/search-filings/edgar-application-programming-interfaces)
- [Accessing EDGAR Data](https://www.sec.gov/search-filings/edgar-search-assistance/accessing-edgar-data)

## CLI

```bash
dotnet run --project src/Meridian/Meridian.csproj -- --security-master-ingest --provider edgar --scope all-filers
dotnet run --project src/Meridian/Meridian.csproj -- --security-master-ingest --provider edgar --scope all-filers --include-xbrl
dotnet run --project src/Meridian/Meridian.csproj -- --security-master-ingest --provider edgar --cik 0000789019 --include-xbrl --include-filing-documents
dotnet run --project src/Meridian/Meridian.csproj -- --security-master-ingest --provider edgar --max-filers 100 --dry-run
```

`--include-xbrl` is explicit because the SEC companyfacts bulk ZIP is large. `--include-filing-documents` is also explicit because it follows filing archive links and parses prospectus supplements, exhibits, N-PORT XML, and 13F information tables. Normal tests and fixtures must stay offline and use stub payloads.

## API

| Method | Route | Purpose |
| --- | --- | --- |
| `POST` | `/api/security-master/ingest/edgar` | Runs EDGAR ingest with `EdgarIngestRequest` |
| `GET` | `/api/reference-data/edgar/filers/{cik}` | Reads the local filer partition |
| `GET` | `/api/reference-data/edgar/facts/{cik}` | Reads the local XBRL facts partition |
| `GET` | `/api/reference-data/edgar/security-data/{cik}` | Reads parsed debt offering and fund holding security data |

## Local Storage

Files are written under `StorageOptions.RootPath/reference-data/edgar`:

- `filers/{cik}.json`
- `facts/{cik}.json`
- `security-data/{cik}.json`
- `ticker-associations.json`
- `manifest.json`

Writes use `AtomicFileWriter`. Bulk ZIP processing streams entries one at a time to avoid buffering every filer or fact payload in memory. Filing-document ingest caps candidate filing documents per filer and streams HTTP responses through the same EDGAR rate limiter.

## Captured Data

Submissions API records include filer identity, entity type, SIC, category, fiscal year end, incorporation state, former names, website/investor website, phone, business/mailing addresses, insider transaction flags, ticker/exchange arrays, and recent filing metadata such as file number, film number, item string, size, XBRL flags, primary document, and primary document description.

Companyfacts/XBRL records are retained in full under `facts/{cik}.json`. `IssuerFactSnapshot` selects common lookup facts, including trading symbol, Section 12(b) security title, exchange name, SEC file number, assets, liabilities, equity, revenue, net income, diluted EPS, operating cash flow, shares outstanding, common shares issued, weighted-average basic/diluted shares, and public float.

Filing-document security data is retained under `security-data/{cik}.json` when `--include-filing-documents` is used:

- Debt offering terms from registration statements, prospectus supplements, free-writing prospectuses, 8-K debt exhibits, and archive attachments. Captured fields include CUSIP, ISIN, security title, currency, principal amount, issue size, coupon rate/type, day count, payment frequency, issue date, maturity date, first interest date, seniority, ranking, callability, first call date, offering price, denominations, trustee, underwriters, redemption text, extraction confidence, and extraction notes.
- Fund and holdings data from N-PORT XML and 13F information tables. Captured fields include holding name, title, issuer, CUSIP, ISIN, ticker, LEI, asset/issuer category, country, currency, balance, units, USD value, percent of net assets, coupon, maturity, restriction flag, and fair-value level.

## Security Master Mapping

CIK is stored as `SecurityIdentifierKind.Cik` with provider `edgar`. Legacy `ProviderSymbol` identifiers with provider `edgar-cik` are still resolved before creating a new record.

Mapping rules:

- Never map EIN to CUSIP.
- CIK identifies a filer or issuer; it is not enough by itself to define a tradable security.
- Mutual-fund series and class IDs remain EDGAR provider identifiers.
- EDGAR XBRL facts stay in the facts store and feed `IssuerFactSnapshot`.
- Debt offering terms and fund holdings stay in the EDGAR security-data store until a downstream process can validate instrument identity and tradability.
- Security Master writes only occur for associations with a ticker.
