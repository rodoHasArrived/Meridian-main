using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Core.Serialization;
using Meridian.Infrastructure.Adapters.Edgar;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using ContractSecurityMasterQueryService = Meridian.Contracts.SecurityMaster.ISecurityMasterQueryService;

namespace Meridian.Tests.Application.SecurityMaster;

[Trait("Category", "Unit")]
public sealed class EdgarIngestOrchestratorTests
{
    [Fact]
    public async Task IngestAsync_DryRun_DoesNotWriteStoreOrSecurityMaster()
    {
        var provider = FakeEdgarProvider.WithTickerBackedCompany();
        var store = new FakeEdgarStore();
        var service = new FakeSecurityMasterService();
        var orchestrator = CreateOrchestrator(provider, store, new FakeSecurityMasterQueryService(), service);

        var result = await orchestrator.IngestAsync(
            new EdgarIngestRequest(MaxFilers: 1, DryRun: true),
            CancellationToken.None);

        result.SecuritiesCreated.Should().Be(1);
        result.TickerAssociationsStored.Should().Be(0);
        store.TickerAssociationsSaved.Should().Be(0);
        store.FilersSaved.Should().Be(0);
        service.CreatedRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task IngestAsync_NoTickerFiler_DoesNotCreateSecurity()
    {
        var provider = new FakeEdgarProvider
        {
            Associations =
            [
                new EdgarTickerAssociation("0000000001", "", "Private Filer", null, null, null, null, "fixture")
            ],
            Filers =
            [
                new EdgarFilerRecord(
                    "0000000001",
                    "Private Filer",
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
                    null,
                    [],
                    [],
                    [],
                    [],
                    [],
                    DateTimeOffset.UtcNow)
            ]
        };
        var service = new FakeSecurityMasterService();
        var orchestrator = CreateOrchestrator(provider, new FakeEdgarStore(), new FakeSecurityMasterQueryService(), service);

        var result = await orchestrator.IngestAsync(new EdgarIngestRequest(MaxFilers: 1), CancellationToken.None);

        result.SecuritiesCreated.Should().Be(0);
        service.CreatedRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task IngestAsync_TickerBackedCompany_CreatesSecurityWithCikIdentifier()
    {
        var provider = FakeEdgarProvider.WithTickerBackedCompany();
        var service = new FakeSecurityMasterService();
        var orchestrator = CreateOrchestrator(provider, new FakeEdgarStore(), new FakeSecurityMasterQueryService(), service);

        var result = await orchestrator.IngestAsync(new EdgarIngestRequest(MaxFilers: 1), CancellationToken.None);

        result.SecuritiesCreated.Should().Be(1);
        service.CreatedRequests.Should().ContainSingle();
        var request = service.CreatedRequests[0];
        request.Identifiers.Should().Contain(id =>
            id.Kind == SecurityIdentifierKind.Cik &&
            id.Value == "0000789019" &&
            id.Provider == "edgar");
        request.Identifiers.Should().Contain(id =>
            id.Kind == SecurityIdentifierKind.Ticker &&
            id.Value == "MSFT" &&
            id.Provider == "edgar");
        request.Identifiers.Should().NotContain(id => id.Kind == SecurityIdentifierKind.Cusip);
        request.SecurityId.Should().NotBeEmpty();
    }

    [Fact]
    public async Task IngestAsync_IncludeXbrl_EnrichesSecurityCreateWithSecuritySpecificSnapshot()
    {
        var provider = FakeEdgarProvider.WithTickerBackedCompany();
        var service = new FakeSecurityMasterService();
        var orchestrator = CreateOrchestrator(provider, new FakeEdgarStore(), new FakeSecurityMasterQueryService(), service);

        var result = await orchestrator.IngestAsync(
            new EdgarIngestRequest(MaxFilers: 1, IncludeXbrl: true),
            CancellationToken.None);

        result.FactsStored.Should().Be(1);
        var request = service.CreatedRequests.Single();
        request.AssetSpecificTerms.GetProperty("shareClass").GetString().Should().Contain("Common stock");
        request.Reason.Should().Contain("securityTitle=Common stock");
        request.Reason.Should().Contain("publicFloat=2400000000000");
    }

    [Fact]
    public async Task IngestAsync_IncludeFilingDocuments_StoresDebtAndFundSecurityData()
    {
        var provider = FakeEdgarProvider.WithTickerBackedCompany();
        var store = new FakeEdgarStore();
        var service = new FakeSecurityMasterService();
        var orchestrator = CreateOrchestrator(provider, store, new FakeSecurityMasterQueryService(), service);

        var result = await orchestrator.IngestAsync(
            new EdgarIngestRequest(MaxFilers: 1, IncludeFilingDocuments: true),
            CancellationToken.None);

        result.SecurityDataStored.Should().Be(1);
        store.SecurityDataSaved.Should().Be(1);
        store.LastSecurityData!.DebtOfferings.Should().ContainSingle(o => o.Cusip == "594918BN3");
        store.LastSecurityData.FundHoldings.Should().ContainSingle(h => h.Cusip == "037833100");
    }

    [Fact]
    public async Task IngestAsync_LegacyEdgarCikRecord_AmendsInsteadOfCreating()
    {
        var provider = FakeEdgarProvider.WithTickerBackedCompany();
        var existing = CreateSecurityDetail(
            Guid.NewGuid(),
            [
                new SecurityIdentifierDto(
                    SecurityIdentifierKind.ProviderSymbol,
                    "0000789019",
                    false,
                    DateTimeOffset.UtcNow.AddDays(-1),
                    Provider: "edgar-cik")
            ]);

        var query = new FakeSecurityMasterQueryService();
        query.AddIdentifier(existing, SecurityIdentifierKind.ProviderSymbol, "0000789019", "edgar-cik");
        var service = new FakeSecurityMasterService();
        var orchestrator = CreateOrchestrator(provider, new FakeEdgarStore(), query, service);

        var result = await orchestrator.IngestAsync(new EdgarIngestRequest(MaxFilers: 1), CancellationToken.None);

        result.SecuritiesCreated.Should().Be(0);
        result.SecuritiesAmended.Should().Be(1);
        service.CreatedRequests.Should().BeEmpty();
        service.AmendRequests.Should().ContainSingle();
        service.AmendRequests[0].IdentifiersToAdd.Should().Contain(id => id.Kind == SecurityIdentifierKind.Cik);
    }

    private static EdgarIngestOrchestrator CreateOrchestrator(
        IEdgarReferenceDataProvider provider,
        IEdgarReferenceDataStore store,
        ContractSecurityMasterQueryService query,
        ISecurityMasterService service)
        => new(
            provider,
            store,
            query,
            service,
            NullLogger<EdgarIngestOrchestrator>.Instance,
            conflictService: null);

    private static SecurityDetailDto CreateSecurityDetail(
        Guid securityId,
        IReadOnlyList<SecurityIdentifierDto> identifiers)
    {
        var common = JsonSerializer.SerializeToElement(
            new Dictionary<string, object?>
            {
                ["displayName"] = "MICROSOFT CORP",
                ["currency"] = "USD",
                ["countryOfRisk"] = "US"
            },
            SecurityMasterJsonContext.Default.DictionaryStringObject);

        var asset = JsonSerializer.SerializeToElement(
            new Dictionary<string, object?> { ["schemaVersion"] = 1, ["classification"] = "Common" },
            SecurityMasterJsonContext.Default.DictionaryStringObject);

        return new SecurityDetailDto(
            securityId,
            "Equity",
            SecurityStatusDto.Active,
            "MICROSOFT CORP",
            "USD",
            common,
            asset,
            identifiers,
            [],
            1,
            DateTimeOffset.UtcNow.AddDays(-1),
            null);
    }

    private sealed class FakeEdgarProvider : IEdgarReferenceDataProvider
    {
        public IReadOnlyList<EdgarTickerAssociation> Associations { get; init; } = [];
        public IReadOnlyList<EdgarFilerRecord> Filers { get; init; } = [];
        public IReadOnlyList<EdgarXbrlFact> Facts { get; init; } = [];
        public EdgarSecurityDataRecord SecurityData { get; init; } = new(
            "0000000000",
            [],
            [],
            DateTimeOffset.UtcNow);

        public static FakeEdgarProvider WithTickerBackedCompany()
            => new()
            {
                Associations =
                [
                    new EdgarTickerAssociation(
                        "0000789019",
                        "MSFT",
                        "MICROSOFT CORP",
                        "Nasdaq",
                        null,
                        null,
                        "OperatingCompany",
                        "fixture")
                ],
                Filers =
                [
                    new EdgarFilerRecord(
                        "0000789019",
                        "MICROSOFT CORP",
                        "operating",
                        "7372",
                        "Prepackaged Software",
                        "Large accelerated filer",
                        "WA",
                        "Washington",
                        "0630",
                        "91-1144442",
                        "Technology issuer",
                        "https://www.microsoft.com",
                        "https://www.microsoft.com/investor",
                        "425-882-8080",
                        new EdgarAddress("ONE MICROSOFT WAY", null, "REDMOND", "WA", "Washington", "98052"),
                        new EdgarAddress("ONE MICROSOFT WAY", null, "REDMOND", "WA", "Washington", "98052"),
                        false,
                        true,
                        ["well-known-seasoned-issuer"],
                        ["MSFT"],
                        ["Nasdaq"],
                        [],
                        [],
                        DateTimeOffset.UtcNow)
                ],
                Facts =
                [
                    new EdgarXbrlFact("0000789019", "dei", "Security12bTitle", "pure", null, "Common stock, $0.00000625 par value per share", null, new DateOnly(2023, 6, 30), 2023, "FY", "10-K", "0001564590-23-000001", new DateOnly(2023, 7, 27), null),
                    new EdgarXbrlFact("0000789019", "dei", "SecurityExchangeName", "pure", null, "NASDAQ", null, new DateOnly(2023, 6, 30), 2023, "FY", "10-K", "0001564590-23-000001", new DateOnly(2023, 7, 27), null),
                    new EdgarXbrlFact("0000789019", "dei", "EntityFileNumber", "pure", null, "001-37845", null, new DateOnly(2023, 6, 30), 2023, "FY", "10-K", "0001564590-23-000001", new DateOnly(2023, 7, 27), null),
                    new EdgarXbrlFact("0000789019", "dei", "EntityCommonStockSharesOutstanding", "shares", 7432000000, null, null, new DateOnly(2023, 6, 30), 2023, "FY", "10-K", "0001564590-23-000001", new DateOnly(2023, 7, 27), null),
                    new EdgarXbrlFact("0000789019", "dei", "EntityPublicFloat", "USD", 2400000000000, null, null, new DateOnly(2023, 6, 30), 2023, "FY", "10-K", "0001564590-23-000001", new DateOnly(2023, 7, 27), null)
                ],
                SecurityData = new EdgarSecurityDataRecord(
                    "0000789019",
                    [
                        new EdgarDebtOfferingTerms(
                            "0000789019",
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
                    [
                        new EdgarFundHolding(
                            "0000789019",
                            "0001564590-23-000003",
                            "NPORT-P",
                            "primary_doc.xml",
                            "Apple Inc.",
                            null,
                            "Common Stock",
                            "037833100",
                            null,
                            "AAPL",
                            null,
                            "EC",
                            "CORP",
                            "US",
                            "USD",
                            100,
                            "NS",
                            19000,
                            1.2m,
                            null,
                            null,
                            false,
                            "1")
                    ],
                    DateTimeOffset.UtcNow)
            };

        public Task<IReadOnlyList<EdgarTickerAssociation>> FetchTickerAssociationsAsync(CancellationToken ct = default)
            => Task.FromResult(Associations);

        public Task<IReadOnlyList<EdgarFilerRecord>> FetchBulkSubmissionsAsync(int? maxFilers = null, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<EdgarFilerRecord>>(Filers.Take(maxFilers ?? int.MaxValue).ToArray());

        public Task<IReadOnlyList<EdgarXbrlFact>> FetchBulkCompanyFactsAsync(
            IReadOnlySet<string>? ciks = null,
            int? maxFilers = null,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<EdgarXbrlFact>>(
                Facts.Where(f => ciks is null || ciks.Contains(f.Cik)).ToArray());

        public Task<EdgarFilerRecord?> FetchSubmissionAsync(string cik, CancellationToken ct = default)
            => Task.FromResult(Filers.FirstOrDefault(f => f.Cik == cik));

        public Task<IReadOnlyList<EdgarXbrlFact>> FetchCompanyFactsAsync(string cik, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<EdgarXbrlFact>>(Facts.Where(f => f.Cik == cik).ToArray());

        public Task<EdgarSecurityDataRecord> FetchSecurityDataAsync(
            EdgarFilerRecord filer,
            int? maxFilings = null,
            CancellationToken ct = default)
            => Task.FromResult(SecurityData with { Cik = filer.Cik });
    }

    private sealed class FakeEdgarStore : IEdgarReferenceDataStore
    {
        public int TickerAssociationsSaved { get; private set; }
        public int FilersSaved { get; private set; }
        public int SecurityDataSaved { get; private set; }
        public EdgarSecurityDataRecord? LastSecurityData { get; private set; }

        public Task SaveTickerAssociationsAsync(IReadOnlyList<EdgarTickerAssociation> associations, CancellationToken ct = default)
        {
            TickerAssociationsSaved += associations.Count;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<EdgarTickerAssociation>> LoadTickerAssociationsAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<EdgarTickerAssociation>>([]);

        public Task SaveFilerAsync(EdgarFilerRecord record, CancellationToken ct = default)
        {
            FilersSaved++;
            return Task.CompletedTask;
        }

        public Task<EdgarFilerRecord?> LoadFilerAsync(string cik, CancellationToken ct = default)
            => Task.FromResult<EdgarFilerRecord?>(null);

        public Task SaveFactsAsync(EdgarFactsRecord record, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<EdgarFactsRecord?> LoadFactsAsync(string cik, CancellationToken ct = default)
            => Task.FromResult<EdgarFactsRecord?>(null);

        public Task SaveSecurityDataAsync(EdgarSecurityDataRecord record, CancellationToken ct = default)
        {
            SecurityDataSaved++;
            LastSecurityData = record;
            return Task.CompletedTask;
        }

        public Task<EdgarSecurityDataRecord?> LoadSecurityDataAsync(string cik, CancellationToken ct = default)
            => Task.FromResult<EdgarSecurityDataRecord?>(null);
    }

    private sealed class FakeSecurityMasterQueryService : ContractSecurityMasterQueryService
    {
        private readonly Dictionary<string, SecurityDetailDto> _byIdentifier = new(StringComparer.OrdinalIgnoreCase);

        public void AddIdentifier(
            SecurityDetailDto detail,
            SecurityIdentifierKind kind,
            string value,
            string? provider)
            => _byIdentifier[Key(kind, value, provider)] = detail;

        public Task<SecurityDetailDto?> GetByIdAsync(Guid securityId, CancellationToken ct = default)
            => Task.FromResult<SecurityDetailDto?>(null);

        public Task<SecurityDetailDto?> GetByIdentifierAsync(
            SecurityIdentifierKind identifierKind,
            string identifierValue,
            string? provider,
            CancellationToken ct = default)
            => Task.FromResult(
                _byIdentifier.TryGetValue(Key(identifierKind, identifierValue, provider), out var detail)
                    ? detail
                    : null);

        public Task<IReadOnlyList<SecuritySummaryDto>> SearchAsync(SecuritySearchRequest request, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<SecuritySummaryDto>>([]);

        public Task<IReadOnlyList<SecurityMasterEventEnvelope>> GetHistoryAsync(SecurityHistoryRequest request, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<SecurityMasterEventEnvelope>>([]);

        public Task<SecurityEconomicDefinitionRecord?> GetEconomicDefinitionByIdAsync(Guid securityId, CancellationToken ct = default)
            => Task.FromResult<SecurityEconomicDefinitionRecord?>(null);

        public Task<TradingParametersDto?> GetTradingParametersAsync(Guid securityId, DateTimeOffset asOf, CancellationToken ct = default)
            => Task.FromResult<TradingParametersDto?>(null);

        public Task<IReadOnlyList<CorporateActionDto>> GetCorporateActionsAsync(Guid securityId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<CorporateActionDto>>([]);

        public Task<PreferredEquityTermsDto?> GetPreferredEquityTermsAsync(Guid securityId, CancellationToken ct = default)
            => Task.FromResult<PreferredEquityTermsDto?>(null);

        public Task<ConvertibleEquityTermsDto?> GetConvertibleEquityTermsAsync(Guid securityId, CancellationToken ct = default)
            => Task.FromResult<ConvertibleEquityTermsDto?>(null);

        private static string Key(SecurityIdentifierKind kind, string value, string? provider)
            => $"{kind}|{value}|{provider ?? string.Empty}";
    }

    private sealed class FakeSecurityMasterService : ISecurityMasterService
    {
        public List<CreateSecurityRequest> CreatedRequests { get; } = [];
        public List<AmendSecurityTermsRequest> AmendRequests { get; } = [];

        public Task<SecurityDetailDto> CreateAsync(CreateSecurityRequest request, CancellationToken ct = default)
        {
            CreatedRequests.Add(request);
            return Task.FromResult(new SecurityDetailDto(
                request.SecurityId,
                request.AssetClass,
                SecurityStatusDto.Active,
                request.CommonTerms.GetProperty("displayName").GetString()!,
                request.CommonTerms.GetProperty("currency").GetString()!,
                request.CommonTerms,
                request.AssetSpecificTerms,
                request.Identifiers,
                [],
                1,
                request.EffectiveFrom,
                null));
        }

        public Task<SecurityDetailDto> AmendTermsAsync(AmendSecurityTermsRequest request, CancellationToken ct = default)
        {
            AmendRequests.Add(request);
            return Task.FromResult(CreateSecurityDetail(request.SecurityId, request.IdentifiersToAdd));
        }

        public Task<SecurityDetailDto> AmendPreferredEquityTermsAsync(Guid securityId, AmendPreferredEquityTermsRequest request, CancellationToken ct = default)
            => Task.FromResult(CreateSecurityDetail(securityId, []));

        public Task<SecurityDetailDto> AmendConvertibleEquityTermsAsync(Guid securityId, AmendConvertibleEquityTermsRequest request, CancellationToken ct = default)
            => Task.FromResult(CreateSecurityDetail(securityId, []));

        public Task DeactivateAsync(DeactivateSecurityRequest request, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<SecurityAliasDto> UpsertAliasAsync(UpsertSecurityAliasRequest request, CancellationToken ct = default)
            => Task.FromResult(new SecurityAliasDto(
                request.AliasId,
                request.SecurityId,
                request.AliasKind,
                request.AliasValue,
                request.Provider,
                request.Scope,
                request.Reason,
                request.CreatedBy,
                DateTimeOffset.UtcNow,
                request.ValidFrom,
                request.ValidTo,
                true));
    }
}
