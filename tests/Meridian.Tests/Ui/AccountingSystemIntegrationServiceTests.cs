using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using FluentAssertions;
using Meridian.Contracts.AccountingSystem;
using Meridian.DataIntegration.AccountingSystem.QuickBooks;
using Meridian.FinancialOperations.AccountingSystem;
using Meridian.Identity.Auth;
using Meridian.Identity;
using Meridian.ProviderSdk.AccountingSystem;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Meridian.Tests.Ui;

public sealed class AccountingSystemIntegrationServiceTests
{
    [Fact]
    public async Task ImportAsync_WithQuickBooksFixture_ReturnsReadOnlyExternalGlEvidence()
    {
        var service = CreateService();

        var detail = await service.ImportAsync(new AccountingSystemImportRequestDto("quickbooks-fixture"));

        detail.Summary.ProviderId.Should().Be("quickbooks-fixture");
        detail.Summary.ChartAccountCount.Should().BeGreaterThan(0);
        detail.Summary.JournalEntryCount.Should().BeGreaterThan(0);
        detail.Summary.TrialBalanceLineCount.Should().BeGreaterThan(0);
        detail.Summary.Warnings.Should().Contain(warning => warning.Contains("source of all ledger truth", StringComparison.OrdinalIgnoreCase));
        detail.Summary.Warnings.Should().Contain(warning => warning.Contains("posting/export is disabled", StringComparison.OrdinalIgnoreCase));
        detail.JournalEntries.Should().OnlyContain(entry => entry.TotalDebits == entry.TotalCredits);
    }

    [Fact]
    public async Task ReconcileLatestAsync_WithoutMeridianLedger_ReturnsMissingMeridianBreaksAndDisabledPosting()
    {
        var service = CreateService();
        await service.ImportAsync(new AccountingSystemImportRequestDto("quickbooks-fixture"));

        var summary = await service.ReconcileLatestAsync("quickbooks-fixture");

        summary.PostingEnabled.Should().BeFalse();
        summary.PostingDisabledReason.Should().Contain("source of all ledger truth");
        summary.PostingDisabledReason.Should().Contain("disabled");
        summary.Rows.Should().NotBeEmpty();
        summary.Rows.Should().OnlyContain(row => row.Status == AccountingSystemReconciliationStatusDto.MissingMeridian);
        summary.BreakCount.Should().Be(summary.Rows.Count);
    }

    [Fact]
    public async Task ImportAsync_PropagatesCancellation()
    {
        var service = CreateService();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => service.ImportAsync(new AccountingSystemImportRequestDto("quickbooks-fixture"), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task AccountingSystemEndpoints_ReturnProviderAndReconciliationContracts()
    {
        await using var app = await CreateAppAsync(UserPermission.ManageFundStructure);

        var providersResponse = await app.GetTestClient().GetAsync("/api/accounting-system/providers");
        var reconciliationResponse = await app.GetTestClient().GetAsync("/api/accounting-system/reconciliation/latest");

        providersResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        reconciliationResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var providers = await ReadAsync<AccountingSystemProviderDto[]>(providersResponse);
        var reconciliation = await ReadAsync<AccountingSystemReconciliationSummaryDto>(reconciliationResponse);
        providers.Should().Contain(row => row.ProviderId == "quickbooks-fixture" && row.State == AccountingSystemProviderStateDto.Available);
        providers.Should().Contain(row => row.ProviderId == "quickbooks" && row.State == AccountingSystemProviderStateDto.Planned);
        reconciliation.ProviderId.Should().Be("quickbooks-fixture");
        reconciliation.PostingEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task ListProvidersAsync_WithQuickBooksOnlineLocalConfig_ReturnsCompanyReadiness()
    {
        var store = FakeQuickBooksConnectionStore.Configured();
        var service = CreateService(new QuickBooksOnlineAccountingProvider(store, new FakeQuickBooksClient()));

        var providers = await service.ListProvidersAsync();

        var quickBooks = providers.Single(row => row.ProviderId == "quickbooks");
        quickBooks.State.Should().Be(AccountingSystemProviderStateDto.Available);
        quickBooks.RequiresCredentials.Should().BeTrue();
        quickBooks.SupportsPosting.Should().BeFalse();
        quickBooks.Connection.Should().NotBeNull();
        quickBooks.Connection!.CompanyId.Should().Be("9130359087654321");
        quickBooks.Connection.CompanyName.Should().Be("Meridian-Dev");
        quickBooks.Connection.MissingFields.Should().BeEmpty();
    }

    [Fact]
    public async Task ImportAsync_WithQuickBooksOnlineProvider_ReturnsReadOnlyCompanyEvidence()
    {
        var store = FakeQuickBooksConnectionStore.Configured();
        var client = new FakeQuickBooksClient();
        var service = CreateService(new QuickBooksOnlineAccountingProvider(store, client));

        var detail = await service.ImportAsync(new AccountingSystemImportRequestDto(
            "quickbooks",
            PeriodStart: new DateOnly(2026, 1, 1),
            PeriodEnd: new DateOnly(2026, 1, 31)));

        detail.Summary.ProviderId.Should().Be("quickbooks");
        detail.Summary.ProviderDisplayName.Should().Contain("Meridian-Dev");
        detail.Summary.ChartAccountCount.Should().Be(2);
        detail.Summary.JournalEntryCount.Should().Be(1);
        detail.Summary.TrialBalanceLineCount.Should().Be(2);
        detail.Summary.Warnings.Should().Contain(warning => warning.Contains("read-only", StringComparison.OrdinalIgnoreCase));
        detail.Summary.Warnings.Should().Contain(warning => warning.Contains("source of all ledger truth", StringComparison.OrdinalIgnoreCase));
        detail.JournalEntries.Should().OnlyContain(entry => entry.TotalDebits == entry.TotalCredits);
        store.SavedRefreshToken.Should().Be("rotated-refresh-token");
        store.LastVerificationSuccess.Should().BeTrue();
        client.ImportCalls.Should().Be(1);
    }

    [Fact]
    public async Task QuickBooksOnlineHttpClient_ReadCompanyEvidenceAsync_MapsReadOnlyApiPayloads()
    {
        using var httpClient = new HttpClient(new QuickBooksStubHandler());
        var client = new QuickBooksOnlineHttpClient(httpClient);
        var connection = new QuickBooksOnlineConnection(
            "qbo-client-id",
            "qbo-client-secret",
            "qbo-refresh-token",
            "9130359087654321",
            "sandbox",
            "Meridian-Dev");

        var token = await client.RefreshAccessTokenAsync(connection);
        var evidence = await client.ReadCompanyEvidenceAsync(
            connection,
            token.AccessToken,
            new AccountingSystemImportRequestDto(
                "quickbooks",
                PeriodStart: new DateOnly(2026, 1, 1),
                PeriodEnd: new DateOnly(2026, 1, 31)));

        token.AccessToken.Should().Be("qbo-access-token");
        token.RefreshToken.Should().Be("rotated-refresh-token");
        evidence.ChartAccounts.Should().HaveCount(2);
        evidence.ChartAccounts.Should().Contain(row => row.ExternalAccountId == "35" && row.AccountCode == "Assets:Checking");
        evidence.JournalEntries.Should().ContainSingle();
        evidence.JournalEntries[0].TotalDebits.Should().Be(4_151.74m);
        evidence.JournalEntries[0].TotalCredits.Should().Be(4_151.74m);
        evidence.JournalEntries[0].Lines.Should().OnlyContain(line => line.Currency == "USD");
        evidence.TrialBalance.Should().HaveCount(2);
        evidence.TrialBalance.Should().Contain(row => row.ExternalAccountId == "35" && row.Debit == 4_151.74m);
        evidence.EvidenceReferences.Should().Contain("quickbooks:company:9130359087654321:trial-balance");
    }

    [Fact]
    public async Task GetLatestImportAsync_WithoutQuickBooksOnlineLocalConfig_DefaultsToFixtureEvidence()
    {
        var service = CreateService(new QuickBooksOnlineAccountingProvider(
            FakeQuickBooksConnectionStore.Missing(),
            new FakeQuickBooksClient()));

        var detail = await service.GetLatestImportAsync();

        detail.Summary.ProviderId.Should().Be("quickbooks-fixture");
        detail.Summary.Warnings.Should().Contain(warning => warning.Contains("Fixture data", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task AccountingSystemEndpoints_WithoutAccountingAccess_ReturnForbidden()
    {
        await using var app = await CreateAppAsync(UserPermission.ViewMarketData);

        var response = await app.GetTestClient().GetAsync("/api/accounting-system/providers");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private static AccountingSystemIntegrationService CreateService(params IAccountingSystemProvider[] additionalProviders)
        => new(new IAccountingSystemProvider[] { new QuickBooksFixtureAccountingProvider() }.Concat(additionalProviders));

    private static async Task<WebApplication> CreateAppAsync(UserPermission permissions)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<IAccountingSystemProvider, QuickBooksFixtureAccountingProvider>();
        builder.Services.AddSingleton<AccountingSystemIntegrationService>();
        builder.Services.AddRateLimiter(options =>
        {
            options.AddPolicy(UiEndpoints.MutationRateLimitPolicy, _ =>
                RateLimitPartition.GetNoLimiter<string>("test"));
        });

        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = permissions;
            await next();
        });
        app.UseRateLimiter();
        app.MapAccountingSystemEndpoints(new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        await app.StartAsync();
        return app;
    }

    private static StringContent JsonContent(object payload) =>
        new(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
        result.Should().NotBeNull($"expected {typeof(T).Name}, got {json}");
        return result!;
    }

    private sealed class FakeQuickBooksConnectionStore : IQuickBooksOnlineConnectionStore
    {
        private readonly QuickBooksOnlineConnection? _connection;
        private readonly IReadOnlyList<string> _missingFields;

        private FakeQuickBooksConnectionStore(QuickBooksOnlineConnection? connection, IReadOnlyList<string> missingFields)
        {
            _connection = connection;
            _missingFields = missingFields;
        }

        public string? SavedRefreshToken { get; private set; }

        public bool? LastVerificationSuccess { get; private set; }

        public static FakeQuickBooksConnectionStore Configured()
            => new(
                new QuickBooksOnlineConnection(
                    "qbo-client-id",
                    "qbo-client-secret",
                    "qbo-refresh-token",
                    "9130359087654321",
                    "sandbox",
                    "Meridian-Dev"),
                []);

        public static FakeQuickBooksConnectionStore Missing()
            => new(null, ["ClientId", "ClientSecret", "RefreshToken", "RealmId"]);

        public Task<QuickBooksOnlineConnection?> ReadAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(_connection);
        }

        public Task<AccountingSystemConnectionMetadataDto> GetMetadataAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(new AccountingSystemConnectionMetadataDto(
                "quickbooks",
                _connection?.Environment ?? "sandbox",
                _connection?.RealmId,
                _connection?.CompanyName,
                HasLocalConfig: _connection is not null,
                HasRefreshToken: _connection is not null,
                LastConnectedAtUtc: LastVerificationSuccess == true ? DateTimeOffset.UtcNow : null,
                StatusLabel: _connection is null ? "Local config required" : "Local config ready",
                StatusDetail: _connection is null
                    ? "QuickBooks Online local config is incomplete."
                    : "Read-only QuickBooks Online evidence is configured for Meridian-Dev.",
                MissingFields: _missingFields));
        }

        public Task SaveRefreshTokenAsync(string refreshToken, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            SavedRefreshToken = refreshToken;
            return Task.CompletedTask;
        }

        public Task RecordConnectionAsync(
            bool success,
            string? externalCompanyId,
            string? error,
            DateTimeOffset? occurredAtUtc = null,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            LastVerificationSuccess = success;
            return Task.CompletedTask;
        }
    }

    private sealed class QuickBooksStubHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var uri = request.RequestUri?.ToString() ?? string.Empty;
            var payload = ResolvePayload(request, uri);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            });
        }

        private static string ResolvePayload(HttpRequestMessage request, string uri)
        {
            if (request.Method == HttpMethod.Post && uri.Contains("oauth.platform.intuit.com", StringComparison.OrdinalIgnoreCase))
            {
                request.Headers.Authorization.Should().NotBeNull();
                request.Headers.Authorization!.Scheme.Should().Be("Basic");
                return """
                    {
                      "access_token": "qbo-access-token",
                      "refresh_token": "rotated-refresh-token",
                      "expires_in": 3600,
                      "token_type": "bearer"
                    }
                    """;
            }

            if (uri.Contains("/query?", StringComparison.OrdinalIgnoreCase) &&
                Uri.UnescapeDataString(uri).Contains("from Account", StringComparison.OrdinalIgnoreCase))
            {
                return """
                    {
                      "QueryResponse": {
                        "Account": [
                          {
                            "Id": "35",
                            "Name": "Checking",
                            "FullyQualifiedName": "Assets:Checking",
                            "Classification": "Asset",
                            "AccountType": "Bank",
                            "CurrencyRef": { "value": "USD" },
                            "Active": true
                          },
                          {
                            "Id": "400",
                            "Name": "Investment Income",
                            "FullyQualifiedName": "Income:Investment",
                            "Classification": "Revenue",
                            "AccountType": "Income",
                            "CurrencyRef": { "value": "USD" },
                            "Active": true
                          }
                        ]
                      }
                    }
                    """;
            }

            if (uri.Contains("/query?", StringComparison.OrdinalIgnoreCase) &&
                Uri.UnescapeDataString(uri).Contains("from JournalEntry", StringComparison.OrdinalIgnoreCase))
            {
                return """
                    {
                      "QueryResponse": {
                        "JournalEntry": [
                          {
                            "Id": "228",
                            "TxnDate": "2026-01-15",
                            "PrivateNote": "Capital entry",
                            "CurrencyRef": { "value": "USD" },
                            "Line": [
                              {
                                "Id": "1",
                                "Description": "Debit checking",
                                "Amount": "4151.74",
                                "JournalEntryLineDetail": {
                                  "PostingType": "Debit",
                                  "AccountRef": { "value": "35", "name": "Checking" }
                                }
                              },
                              {
                                "Id": "2",
                                "Description": "Credit income",
                                "Amount": "4151.74",
                                "JournalEntryLineDetail": {
                                  "PostingType": "Credit",
                                  "AccountRef": { "value": "400", "name": "Investment Income" }
                                }
                              }
                            ]
                          }
                        ]
                      }
                    }
                    """;
            }

            if (uri.Contains("/reports/TrialBalance", StringComparison.OrdinalIgnoreCase))
            {
                return """
                    {
                      "Rows": {
                        "Row": [
                          {
                            "ColData": [
                              { "id": "35", "value": "Checking" },
                              { "value": "4151.74" },
                              { "value": "" }
                            ]
                          },
                          {
                            "ColData": [
                              { "id": "400", "value": "Investment Income" },
                              { "value": "" },
                              { "value": "4151.74" }
                            ]
                          },
                          {
                            "group": "GrandTotal",
                            "type": "Section",
                            "Summary": {
                              "ColData": [
                                { "value": "TOTAL" },
                                { "value": "4151.74" },
                                { "value": "4151.74" }
                              ]
                            }
                          }
                        ]
                      }
                    }
                    """;
            }

            throw new InvalidOperationException($"Unexpected QuickBooks request: {request.Method} {uri}");
        }
    }

    private sealed class FakeQuickBooksClient : IQuickBooksOnlineClient
    {
        public int ImportCalls { get; private set; }

        public Task<QuickBooksOnlineTokenExchangeResult> RefreshAccessTokenAsync(
            QuickBooksOnlineConnection connection,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(new QuickBooksOnlineTokenExchangeResult(
                "qbo-access-token",
                "rotated-refresh-token",
                DateTimeOffset.UtcNow.AddHours(1),
                Warnings: []));
        }

        public Task<QuickBooksOnlineCompanyEvidence> ReadCompanyEvidenceAsync(
            QuickBooksOnlineConnection connection,
            string accessToken,
            AccountingSystemImportRequestDto request,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            ImportCalls++;
            var periodEnd = request.PeriodEnd ?? new DateOnly(2026, 1, 31);
            IReadOnlyList<AccountingSystemChartAccountDto> chart =
            [
                new("qbo-1000", "Assets:Cash:Operating", "Operating Cash", "Asset", "USD", true, EvidenceRef: "quickbooks:company:9130359087654321:account:qbo-1000"),
                new("qbo-4000", "Income:Investment", "Investment Income", "Income", "USD", true, EvidenceRef: "quickbooks:company:9130359087654321:account:qbo-4000")
            ];
            IReadOnlyList<AccountingSystemJournalEntryDto> journal =
            [
                new(
                    "qbo-je-100",
                    new DateOnly(2026, 1, 5),
                    "Capital contribution",
                    "USD",
                    250_000m,
                    250_000m,
                    [
                        new("qbo-je-100-1", "qbo-1000", "Assets:Cash:Operating", "Capital received", 250_000m, 0m, "USD", "quickbooks:company:9130359087654321:journal:qbo-je-100:line:1"),
                        new("qbo-je-100-2", "qbo-4000", "Income:Investment", "Capital offset", 0m, 250_000m, "USD", "quickbooks:company:9130359087654321:journal:qbo-je-100:line:2")
                    ],
                    "quickbooks:company:9130359087654321:journal:qbo-je-100")
            ];
            IReadOnlyList<AccountingSystemTrialBalanceLineDto> trialBalance =
            [
                new("qbo-1000", "Assets:Cash:Operating", "Operating Cash", "Asset", 250_000m, 0m, "USD", periodEnd, "quickbooks:company:9130359087654321:trial-balance:qbo-1000"),
                new("qbo-4000", "Income:Investment", "Investment Income", "Income", 0m, 250_000m, "USD", periodEnd, "quickbooks:company:9130359087654321:trial-balance:qbo-4000")
            ];

            return Task.FromResult(new QuickBooksOnlineCompanyEvidence(
                chart,
                journal,
                trialBalance,
                [
                    "quickbooks:company:9130359087654321:chart-of-accounts",
                    "quickbooks:company:9130359087654321:journal",
                    "quickbooks:company:9130359087654321:trial-balance"
                ],
                Warnings: []));
        }
    }
}
