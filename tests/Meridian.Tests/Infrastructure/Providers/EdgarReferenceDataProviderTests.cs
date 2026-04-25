using System.IO.Compression;
using System.Net;
using System.Text;
using FluentAssertions;
using Meridian.Contracts.SecurityMaster;
using Meridian.Infrastructure.Adapters.Edgar;
using Meridian.Storage;
using Meridian.Storage.SecurityMaster;
using Meridian.Tests.TestHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meridian.Tests.Infrastructure.Providers;

[Trait("Category", "Unit")]
public sealed class EdgarReferenceDataProviderTests
{
    private const string CompanyTickers = """
        {
          "0": {"cik_str": 789019, "ticker": "MSFT", "title": "MICROSOFT CORP"}
        }
        """;

    private const string ExchangeTickers = """
        {
          "fields": ["cik", "name", "ticker", "exchange"],
          "data": [[320193, "Apple Inc.", "AAPL", "Nasdaq"]]
        }
        """;

    private const string MutualFundTickers = """
        {
          "fields": ["cik", "seriesId", "classId", "ticker"],
          "data": [[1000045, "S000001", "C000001", "MMFXX"]]
        }
        """;

    private const string Submission = """
        {
          "cik": "0000789019",
          "entityType": "operating",
          "name": "MICROSOFT CORP",
          "sic": "7372",
          "sicDescription": "Prepackaged Software",
          "category": "Large accelerated filer",
          "description": "Technology issuer",
          "website": "https://www.microsoft.com",
          "investorWebsite": "https://www.microsoft.com/investor",
          "phone": "425-882-8080",
          "tickers": ["MSFT"],
          "exchanges": ["Nasdaq"],
          "ein": "91-1144442",
          "stateOfIncorporation": "WA",
          "fiscalYearEnd": "0630",
          "insiderTransactionForOwnerExists": 0,
          "insiderTransactionForIssuerExists": 1,
          "flags": "well-known-seasoned-issuer",
          "addresses": {
            "business": {
              "street1": "ONE MICROSOFT WAY",
              "city": "REDMOND",
              "stateOrCountry": "WA",
              "stateOrCountryDescription": "Washington",
              "zipCode": "98052"
            },
            "mailing": {
              "street1": "ONE MICROSOFT WAY",
              "city": "REDMOND",
              "stateOrCountry": "WA",
              "zipCode": "98052"
            }
          },
          "formerNames": [{"name": "MICROSOFT", "from": "1975-01-01", "to": "1981-06-25"}],
          "filings": {
            "recent": {
              "accessionNumber": ["0001564590-23-000001"],
              "form": ["10-K"],
              "filingDate": ["2023-07-27"],
              "reportDate": ["2023-06-30"],
              "primaryDocument": ["msft-10k.htm"],
              "primaryDocDescription": ["10-K annual report"],
              "fileNumber": ["001-37845"],
              "filmNumber": ["231111111"],
              "items": [""],
              "size": [123456],
              "isXBRL": [1],
              "isInlineXBRL": [1]
            }
          }
        }
        """;

    private const string CompanyFacts = """
        {
          "cik": 789019,
          "entityName": "MICROSOFT CORP",
          "facts": {
            "us-gaap": {
              "Assets": {
                "units": {
                  "USD": [
                    {"end": "2023-06-30", "val": 411976000000, "fy": 2023, "fp": "FY", "form": "10-K", "accn": "0001564590-23-000001", "filed": "2023-07-27"}
                  ]
                }
              },
              "NetIncomeLoss": {
                "units": {
                  "USD": [
                    {"end": "2023-06-30", "val": 72361000000, "fy": 2023, "fp": "FY", "form": "10-K", "accn": "0001564590-23-000001", "filed": "2023-07-27"}
                  ]
                }
              }
            },
            "dei": {
              "TradingSymbol": {
                "units": {
                  "pure": [
                    {"end": "2023-06-30", "val": "MSFT", "fy": 2023, "fp": "FY", "form": "10-K", "accn": "0001564590-23-000001", "filed": "2023-07-27"}
                  ]
                }
              },
              "Security12bTitle": {
                "units": {
                  "pure": [
                    {"end": "2023-06-30", "val": "Common stock, $0.00000625 par value per share", "fy": 2023, "fp": "FY", "form": "10-K", "accn": "0001564590-23-000001", "filed": "2023-07-27"}
                  ]
                }
              },
              "SecurityExchangeName": {
                "units": {
                  "pure": [
                    {"end": "2023-06-30", "val": "NASDAQ", "fy": 2023, "fp": "FY", "form": "10-K", "accn": "0001564590-23-000001", "filed": "2023-07-27"}
                  ]
                }
              },
              "EntityFileNumber": {
                "units": {
                  "pure": [
                    {"end": "2023-06-30", "val": "001-37845", "fy": 2023, "fp": "FY", "form": "10-K", "accn": "0001564590-23-000001", "filed": "2023-07-27"}
                  ]
                }
              },
              "EntityPublicFloat": {
                "units": {
                  "USD": [
                    {"end": "2023-06-30", "val": 2400000000000, "fy": 2023, "fp": "FY", "form": "10-K", "accn": "0001564590-23-000001", "filed": "2023-07-27"}
                  ]
                }
              }
            }
          }
        }
        """;

    private const string DebtProspectus = """
        <html><body>
        <h1>Prospectus Supplement</h1>
        <p>CUSIP Number: 594918 BN3</p>
        <p>U.S.$1,000,000,000 aggregate principal amount of 4.200% Senior Notes due August 1, 2033.</p>
        <p>The notes are senior unsecured obligations and rank equally with our other senior unsecured debt.</p>
        <p>Issue Date: July 31, 2023. First Interest Payment Date: February 1, 2024.</p>
        <p>Interest will be paid semi-annually on February 1 and August 1. Day count: 30/360.</p>
        <p>Public offering price 99.500%.</p>
        <p>The notes will be issued in minimum denominations of $2,000 and integral multiples of $1,000.</p>
        <p>Optional redemption: we may redeem the notes on or after May 1, 2033 at the redemption price.</p>
        <p>U.S. Bank Trust Company, as trustee.</p>
        <p>Underwriters: J.P. Morgan Securities LLC; BofA Securities, Inc.</p>
        </body></html>
        """;

    private const string NportXml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <edgarSubmission>
          <formData>
            <invstOrSec>
              <name>Apple Inc.</name>
              <title>Common Stock</title>
              <cusip>037833100</cusip>
              <isin>US0378331005</isin>
              <ticker>AAPL</ticker>
              <assetCat>EC</assetCat>
              <issuerCat>CORP</issuerCat>
              <invCountry>US</invCountry>
              <balance>100</balance>
              <units>NS</units>
              <curCd>USD</curCd>
              <valUSD>19000</valUSD>
              <pctVal>1.20</pctVal>
              <isRestrictedSec>false</isRestrictedSec>
              <fairValLevel>1</fairValLevel>
            </invstOrSec>
            <invstOrSec>
              <name>Example Issuer Notes</name>
              <title>Senior Notes</title>
              <cusip>123456AB1</cusip>
              <assetCat>DBT</assetCat>
              <balance>200000</balance>
              <units>PA</units>
              <curCd>USD</curCd>
              <valUSD>198000</valUSD>
              <pctVal>0.50</pctVal>
              <debtSec>
                <annualizedRt>5.25</annualizedRt>
                <maturityDt>2030-12-15</maturityDt>
              </debtSec>
            </invstOrSec>
          </formData>
        </edgarSubmission>
        """;

    [Fact]
    public void ParseCompanyTickers_NormalizesCikAndTicker()
    {
        var associations = EdgarReferenceDataProvider.ParseCompanyTickers(CompanyTickers);

        associations.Should().ContainSingle();
        associations[0].Cik.Should().Be("0000789019");
        associations[0].Ticker.Should().Be("MSFT");
        associations[0].Source.Should().Be("company_tickers");
    }

    [Fact]
    public void ParseCompanyTickersExchange_ParsesFieldsDataPayload()
    {
        var associations = EdgarReferenceDataProvider.ParseCompanyTickersExchange(ExchangeTickers);

        associations.Should().ContainSingle();
        associations[0].Cik.Should().Be("0000320193");
        associations[0].Ticker.Should().Be("AAPL");
        associations[0].Exchange.Should().Be("Nasdaq");
    }

    [Fact]
    public void ParseMutualFundTickers_KeepsSeriesAndClassAsProviderIdentifiers()
    {
        var associations = EdgarReferenceDataProvider.ParseMutualFundTickers(MutualFundTickers);

        associations.Should().ContainSingle();
        associations[0].Cik.Should().Be("0001000045");
        associations[0].SeriesId.Should().Be("S000001");
        associations[0].ClassId.Should().Be("C000001");
        associations[0].SecurityType.Should().Be("MutualFund");
    }

    [Fact]
    public void ParseSubmission_ParsesFilerMetadataAndRecentFilings()
    {
        var record = EdgarReferenceDataProvider.ParseSubmission(Submission, fallbackCik: null);

        record.Should().NotBeNull();
        record!.Cik.Should().Be("0000789019");
        record.EntityType.Should().Be("operating");
        record.Website.Should().Be("https://www.microsoft.com");
        record.InvestorWebsite.Should().Be("https://www.microsoft.com/investor");
        record.BusinessAddress!.City.Should().Be("REDMOND");
        record.Flags.Should().ContainSingle("well-known-seasoned-issuer");
        record.InsiderTransactionForIssuerExists.Should().BeTrue();
        record.Tickers.Should().Contain("MSFT");
        record.FormerNames.Should().ContainSingle(n => n.Name == "MICROSOFT");
        record.RecentFilings.Should().ContainSingle(f =>
            f.Form == "10-K" &&
            f.PrimaryDocDescription == "10-K annual report" &&
            f.FileNumber == "001-37845" &&
            f.Size == 123456 &&
            f.IsXbrl == true &&
            f.IsInlineXbrl == true);
    }

    [Fact]
    public void ParseCompanyFacts_ParsesFactsAndBuildsSnapshot()
    {
        var facts = EdgarReferenceDataProvider.ParseCompanyFacts(CompanyFacts);
        var snapshot = EdgarReferenceDataProvider.CreateIssuerFactSnapshot("789019", facts);

        facts.Should().HaveCount(7);
        snapshot.Cik.Should().Be("0000789019");
        snapshot.TradingSymbol!.RawValue.Should().Be("MSFT");
        snapshot.Security12bTitle!.RawValue.Should().Contain("Common stock");
        snapshot.SecurityExchangeName!.RawValue.Should().Be("NASDAQ");
        snapshot.EntityFileNumber!.RawValue.Should().Be("001-37845");
        snapshot.Assets!.NumericValue.Should().Be(411976000000);
        snapshot.NetIncomeLoss!.NumericValue.Should().Be(72361000000);
        snapshot.PublicFloat!.NumericValue.Should().Be(2400000000000);
    }

    [Fact]
    public void ParseCompanyFacts_WithMalformedPayload_ReturnsEmpty()
    {
        var facts = EdgarReferenceDataProvider.ParseCompanyFacts("{not-json");
        facts.Should().BeEmpty();
    }

    [Fact]
    public async Task FetchSecurityDataAsync_ParsesDebtTermsAndFundHoldingsFromFilingDocuments()
    {
        var filer = new EdgarFilerRecord(
            "0000789019",
            "MICROSOFT CORP",
            "operating",
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            [],
            ["MSFT"],
            ["Nasdaq"],
            [],
            [
                new EdgarFilingSummary(
                    "0000789019",
                    "0001564590-23-000002",
                    "424B2",
                    new DateOnly(2023, 7, 31),
                    null,
                    "msft-424b2.htm",
                    "Prospectus supplement",
                    "333-000001",
                    "231111112",
                    null,
                    1000,
                    true,
                    true,
                    ["debt-offering"]),
                new EdgarFilingSummary(
                    "0000789019",
                    "0001564590-23-000003",
                    "NPORT-P",
                    new DateOnly(2023, 8, 31),
                    null,
                    "primary_doc.xml",
                    "Monthly portfolio investments report",
                    "811-000001",
                    "231111113",
                    null,
                    1000,
                    false,
                    false,
                    ["fund-holdings"])
            ],
            DateTimeOffset.UtcNow);

        using var archiveHandler = new StubHttpMessageHandler(request =>
        {
            var url = request.RequestUri!.ToString();
            if (url.EndsWith("msft-424b2.htm", StringComparison.OrdinalIgnoreCase))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(DebtProspectus, Encoding.UTF8, "text/html")
                };
            }

            if (url.EndsWith("primary_doc.xml", StringComparison.OrdinalIgnoreCase))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(NportXml, Encoding.UTF8, "application/xml")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        using var archiveClient = new HttpClient(archiveHandler);
        using var webClient = new HttpClient(new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)));
        using var dataClient = new HttpClient(new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)));

        using var provider = new EdgarReferenceDataProvider(
            NullLogger<EdgarReferenceDataProvider>.Instance,
            webClient: webClient,
            dataClient: dataClient,
            archiveClient: archiveClient);

        var data = await provider.FetchSecurityDataAsync(filer, maxFilings: 10, CancellationToken.None);

        data.Cik.Should().Be("0000789019");
        data.DebtOfferings.Should().ContainSingle();
        var debt = data.DebtOfferings[0];
        debt.Cusip.Should().Be("594918BN3");
        debt.CouponRate.Should().Be(4.200m);
        debt.MaturityDate.Should().Be(new DateOnly(2033, 8, 1));
        debt.PrincipalAmount.Should().Be(1_000_000_000);
        debt.Seniority.Should().Be("Senior Unsecured");
        debt.IsCallable.Should().BeTrue();
        debt.MinimumDenomination.Should().Be(2_000);
        debt.AdditionalDenomination.Should().Be(1_000);

        data.FundHoldings.Should().HaveCount(2);
        data.FundHoldings.Should().Contain(h =>
            h.HoldingName == "Apple Inc." &&
            h.Cusip == "037833100" &&
            h.Ticker == "AAPL" &&
            h.PercentageOfNetAssets == 1.20m);
        data.FundHoldings.Should().Contain(h =>
            h.Cusip == "123456AB1" &&
            h.CouponRate == 5.25m &&
            h.MaturityDate == new DateOnly(2030, 12, 15));
    }

    [Fact]
    public async Task FetchBulkSubmissionsAsync_StreamsZipEntriesAndHonorsMaxFilers()
    {
        var archiveBytes = CreateZip(("CIK0000789019.json", Submission), ("ignored.txt", "skip"));
        using var archiveHandler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(archiveBytes)
            });
        using var archiveClient = new HttpClient(archiveHandler);
        using var webClient = new HttpClient(new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)));
        using var dataClient = new HttpClient(new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)));

        using var provider = new EdgarReferenceDataProvider(
            NullLogger<EdgarReferenceDataProvider>.Instance,
            webClient: webClient,
            dataClient: dataClient,
            archiveClient: archiveClient);

        var filers = await provider.FetchBulkSubmissionsAsync(maxFilers: 1, CancellationToken.None);

        filers.Should().ContainSingle();
        filers[0].Cik.Should().Be("0000789019");
    }

    [Fact]
    public async Task FileEdgarReferenceDataStore_RoundTripsPartitions()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-edgar-tests", Guid.NewGuid().ToString("N"));
        var store = new FileEdgarReferenceDataStore(new StorageOptions { RootPath = root });
        var association = new EdgarTickerAssociation("0000789019", "MSFT", "MICROSOFT CORP", "Nasdaq", null, null, "OperatingCompany", "test");
        var filer = EdgarReferenceDataProvider.ParseSubmission(Submission, null)!;
        var facts = EdgarReferenceDataProvider.ParseCompanyFacts(CompanyFacts);

        await store.SaveTickerAssociationsAsync([association], CancellationToken.None);
        await store.SaveFilerAsync(filer, CancellationToken.None);
        await store.SaveFactsAsync(
            new EdgarFactsRecord(
                "789019",
                facts,
                EdgarReferenceDataProvider.CreateIssuerFactSnapshot("789019", facts),
                DateTimeOffset.UtcNow),
            CancellationToken.None);
        await store.SaveSecurityDataAsync(
            new EdgarSecurityDataRecord(
                "789019",
                [
                    new EdgarDebtOfferingTerms(
                        "789019",
                        "0001564590-23-000002",
                        "424B2",
                        "msft-424b2.htm",
                        "Prospectus supplement",
                        "MICROSOFT CORP",
                        "4.200% Senior Notes due 2033",
                        "594918BN3",
                        null,
                        "USD",
                        1_000_000_000,
                        1_000_000_000,
                        4.2m,
                        "Fixed",
                        "30/360",
                        "SemiAnnual",
                        new DateOnly(2023, 7, 31),
                        new DateOnly(2033, 8, 1),
                        new DateOnly(2024, 2, 1),
                        "Senior Unsecured",
                        "rank equally",
                        true,
                        new DateOnly(2033, 5, 1),
                        99.5m,
                        2_000,
                        1_000,
                        "U.S. Bank Trust Company",
                        ["J.P. Morgan Securities LLC"],
                        ["Optional redemption at make-whole price."],
                        0.95m,
                        [])
                ],
                [],
                DateTimeOffset.UtcNow),
            CancellationToken.None);

        var associations = await store.LoadTickerAssociationsAsync(CancellationToken.None);
        var loadedFiler = await store.LoadFilerAsync("789019", CancellationToken.None);
        var loadedFacts = await store.LoadFactsAsync("0000789019", CancellationToken.None);
        var loadedSecurityData = await store.LoadSecurityDataAsync("0000789019", CancellationToken.None);

        associations.Should().ContainSingle(a => a.Ticker == "MSFT");
        loadedFiler!.Cik.Should().Be("0000789019");
        loadedFacts!.Snapshot!.Assets!.NumericValue.Should().Be(411976000000);
        loadedSecurityData!.DebtOfferings.Should().ContainSingle(o => o.Cusip == "594918BN3");
        File.Exists(Path.Combine(root, "reference-data", "edgar", "manifest.json")).Should().BeTrue();
    }

    private static byte[] CreateZip(params (string Name, string Content)[] entries)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                var entry = zip.CreateEntry(name);
                using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
                writer.Write(content);
            }
        }

        return stream.ToArray();
    }
}
