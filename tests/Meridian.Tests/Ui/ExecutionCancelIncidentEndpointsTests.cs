using System.Net;
using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Tenancy;
using Meridian.Contracts.Workstation;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Meridian.Identity;
using Meridian.Identity.Auth;
using Meridian.PortfolioRecords.Accounts;
using Meridian.Ui.Shared.Endpoints;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Meridian.Tests.Ui;

public sealed class ExecutionCancelIncidentEndpointsTests
{
    private const string TenantId = "incident-tenant";
    private const string CompanyId = "incident-company";
    private const string IncidentId = "incident-mixed-accounts";
    private static readonly Guid AccountId = Guid.Parse("c5103236-c4ee-423d-9f34-80104912a7d0");
    private static readonly Guid OtherAccountId = Guid.Parse("05804034-e528-4132-b209-67c59366f10c");
    private static readonly Guid FundId = Guid.Parse("b55afccc-1c4e-4c24-a4b5-ad5cc2356746");
    private static readonly DateTimeOffset ObservedAt = new(2026, 1, 12, 15, 30, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData("/api/execution/cancel-incidents")]
    [InlineData("/api/execution/cancel-incidents/incident-mixed-accounts")]
    public async Task IncidentReads_RequireAnExplicitNonemptyAccount(string route)
    {
        await using var fixture = await EndpointFixture.CreateAsync(includeAudit: false);

        using var missing = await fixture.Client.GetAsync(route, fixture.Token);
        using var empty = await fixture.Client.GetAsync($"{route}?fundAccountId={Guid.Empty:D}", fixture.Token);

        missing.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        empty.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await missing.Content.ReadAsStringAsync(fixture.Token)).Should().Contain("Select an execution fund account");
    }

    [Theory]
    [InlineData("/api/execution/cancel-incidents")]
    [InlineData("/api/execution/cancel-incidents/incident-mixed-accounts")]
    public async Task IncidentReads_RejectAnExistingAccountOutsideTheCallersTradingScope(string route)
    {
        await using var fixture = await EndpointFixture.CreateAsync(includeAudit: false);

        using var response = await fixture.Client.GetAsync($"{route}?fundAccountId={OtherAccountId:D}", fixture.Token);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "account existence and global ViewTrades must not grant scoped account access");
    }

    [Theory]
    [InlineData("/api/execution/cancel-incidents")]
    [InlineData("/api/execution/cancel-incidents/incident-mixed-accounts")]
    public async Task IncidentReads_RequireGlobalViewTradesEvenWithAnAccountAssignment(string route)
    {
        await using var fixture = await EndpointFixture.CreateAsync(
            includeAudit: false, permissions: UserPermission.ExecuteTrades);

        using var response = await fixture.Client.GetAsync($"{route}?fundAccountId={AccountId:D}", fixture.Token);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("foreign-tenant", CompanyId, false)]
    [InlineData(TenantId, "foreign-company", false)]
    [InlineData("foreign-tenant", CompanyId, true)]
    [InlineData(TenantId, "foreign-company", true)]
    [InlineData(null, null, false)]
    [InlineData(TenantId, null, false)]
    [InlineData(null, null, true)]
    [InlineData(TenantId, null, true)]
    public async Task IncidentReads_RequireOwningTenantAndCompanyEvenForAdministrators(
        string? tenantId, string? companyId, bool administrator)
    {
        var permissions = UserPermission.ViewTrades
            | (administrator ? UserPermission.AdminMaintenance : UserPermission.None);
        await using var fixture = await EndpointFixture.CreateAsync(
            includeAudit: false, permissions: permissions, tenantId: tenantId, companyId: companyId);

        using var list = await fixture.Client.GetAsync(
            $"/api/execution/cancel-incidents?fundAccountId={AccountId:D}", fixture.Token);
        using var detail = await fixture.Client.GetAsync(
            $"/api/execution/cancel-incidents/{IncidentId}?fundAccountId={AccountId:D}", fixture.Token);

        list.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        detail.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "an administrator's scoped-access override does not override fund ownership");
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task IncidentReads_FailClosedWithoutAuthoritativeFundOwnership(
        bool registerTenancy, bool bindFund)
    {
        await using var fixture = await EndpointFixture.CreateAsync(
            includeAudit: false, registerTenancy: registerTenancy, bindFund: bindFund);

        using var list = await fixture.Client.GetAsync(
            $"/api/execution/cancel-incidents?fundAccountId={AccountId:D}", fixture.Token);
        using var detail = await fixture.Client.GetAsync(
            $"/api/execution/cancel-incidents/{IncidentId}?fundAccountId={AccountId:D}", fixture.Token);

        list.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        detail.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task IncidentReads_DistinguishUnavailableStorageFromAnEmptyHistoryAndAnAbsentIncident()
    {
        await using var unavailable = await EndpointFixture.CreateAsync(includeAudit: false);
        using var unavailableList = await unavailable.Client.GetAsync(
            $"/api/execution/cancel-incidents?fundAccountId={AccountId:D}", unavailable.Token);
        using var unavailableDetail = await unavailable.Client.GetAsync(
            $"/api/execution/cancel-incidents/{IncidentId}?fundAccountId={AccountId:D}", unavailable.Token);
        unavailableList.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        unavailableDetail.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await unavailableList.Content.ReadAsStringAsync(unavailable.Token)).Should().Contain("evidence is unavailable");

        await using var empty = await EndpointFixture.CreateAsync();
        using var emptyList = await empty.Client.GetAsync(
            $"/api/execution/cancel-incidents?fundAccountId={AccountId:D}", empty.Token);
        using var absentDetail = await empty.Client.GetAsync(
            $"/api/execution/cancel-incidents/{IncidentId}?fundAccountId={AccountId:D}", empty.Token);
        emptyList.StatusCode.Should().Be(HttpStatusCode.OK);
        var history = JsonSerializer.Deserialize<CancelAllIncidentHistoryDto>(
            await emptyList.Content.ReadAsStringAsync(empty.Token), JsonOptions)!;
        history.Returned.Should().Be(0);
        history.Incidents.Should().BeEmpty();
        history.HasMore.Should().BeFalse();
        history.CoverageNote.Should().Contain("do not establish current exposure");
        absentDetail.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ReloadedIncident_PreservesEveryAccountOrderExactAmountsAndUnknownExposure()
    {
        var mixed = CreateMixedIncident();
        var older = CreateAccountIncident("incident-older", ObservedAt.AddMinutes(-10));
        var foreignOnly = CreateAccountIncident("incident-other-account", ObservedAt.AddMinutes(10)) with
        {
            FundAccountId = OtherAccountId,
            Orders = [CreateOtherAccountOrder()]
        };
        await using var fixture = await EndpointFixture.CreateAsync(
            incidents: [mixed, older, foreignOnly], reopenAudit: true);

        using var listResponse = await fixture.Client.GetAsync(
            $"/api/execution/cancel-incidents?fundAccountId={AccountId:D}&take=1", fixture.Token);
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var listJson = await listResponse.Content.ReadAsStringAsync(fixture.Token);
        var history = JsonSerializer.Deserialize<CancelAllIncidentHistoryDto>(listJson, JsonOptions)!;
        history.Returned.Should().Be(1);
        history.Limit.Should().Be(1);
        history.HasMore.Should().BeTrue();
        history.Incidents.Should().ContainSingle().Which.IncidentId.Should().Be(IncidentId);
        history.Incidents[0].OrderCount.Should().Be(12);
        history.Incidents[0].UnresolvedOrderCount.Should().Be(4);
        history.Incidents[0].FundAccountId.Should().Be(AccountId);
        listJson.Should().NotContain("incident-other-account");
        history.Incidents[0].EvidenceError.Should().NotBeNullOrWhiteSpace();
        listJson.Should().NotContain("private-audit-path");

        using var detailResponse = await fixture.Client.GetAsync(
            $"/api/execution/cancel-incidents/{IncidentId}?fundAccountId={AccountId:D}", fixture.Token);
        detailResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var detailJson = await detailResponse.Content.ReadAsStringAsync(fixture.Token);
        var detail = JsonSerializer.Deserialize<CancelAllIncidentDto>(detailJson, JsonOptions)!;
        detail.Summary.EvidenceStatus.Should().Be("Complete");
        detail.Summary.BrokerViewUnavailable.Should().BeTrue(
            "complete retained evidence is independent of successful broker convergence");
        detail.Summary.UnresolvedOrderCount.Should().Be(4);
        detail.Orders.Should().HaveCount(12, "the incident detail must not inherit a ten-row UI preview limit");
        detail.Orders.Should().OnlyContain(order => order.FundAccountId == AccountId);
        detail.Orders.Select(order => order.OrderId).Should().BeEquivalentTo(
            Enumerable.Range(0, 12).Select(index => $"account-order-{index:D2}"));
        detailJson.Should().NotContain("other-account-order").And.NotContain("unattributed-broker-order");
        detailJson.Should().NotContain(OtherAccountId.ToString("D"));
        detail.BrokerViewError.Should().NotBeNullOrWhiteSpace();
        detailJson.Should().NotContain("other-broker-account-secret").And.NotContain("private-audit-path");
        detail.CorrelationId.Should().Be("synthetic-cancel-correlation");

        var replacement = detail.Orders.Single(order => order.OrderId == "account-order-01");
        replacement.HasUnconfirmedReplacement.Should().BeTrue();
        replacement.CandidateBrokerOrderId.Should().Be("candidate-broker-order");
        replacement.CandidateClientOrderId.Should().Be("candidate-client-order");
        replacement.Status.Should().Be("Cancelled");
        var working = detail.Orders.Single(order => order.OrderId == "account-order-02");
        working.Disposition.Should().Be("StillWorking");
        working.InitialStatus.Should().Be("Accepted");
        working.Status.Should().Be("PartiallyFilled");
        working.ValuationCoverage.Should().Be("ObservedUsdEquityQuote");
        working.ValuationSource.Should().Be("synthetic-ask");
        working.ValuationObservedAt.Should().Be(ObservedAt);
        working.Currency.Should().Be("USD");

        using var document = JsonDocument.Parse(detailJson);
        var orders = document.RootElement.GetProperty("orders").EnumerateArray().ToArray();
        var precise = orders.Single(order => order.GetProperty("orderId").GetString() == "account-order-02");
        AssertDecimalText(precise, "initialQuantity", "9007199254740993.123456789");
        AssertDecimalText(precise, "initialFilledQuantity", "0");
        AssertDecimalText(precise, "quantity", "9007199254740993.123456789");
        AssertDecimalText(precise, "filledQuantity", "0.000000003");
        AssertDecimalText(precise, "remainingQuantity", "9007199254740993.123456786");
        AssertDecimalText(precise, "residualNotional", "9007199.254740993123456786");
        AssertDecimalText(precise, "valuationPrice", "0.000000001");
        var unknown = orders.Single(order => order.GetProperty("orderId").GetString() == "account-order-00");
        unknown.GetProperty("disposition").GetString().Should().Be("Unknown");
        unknown.GetProperty("status").GetString().Should().Be("PendingCancel");
        foreach (var property in new[] { "quantity", "filledQuantity", "remainingQuantity", "residualNotional", "valuationPrice" })
            unknown.GetProperty(property).ValueKind.Should().Be(JsonValueKind.Null,
                "an unobserved amount cannot be represented as zero");

        using var excluded = await fixture.Client.GetAsync(
            $"/api/execution/cancel-incidents/incident-other-account?fundAccountId={AccountId:D}", fixture.Token);
        excluded.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "knowing an incident ID must not reveal evidence with no orders attributed to the selected account");
    }

    [Fact]
    public async Task GenericAuditFeed_PreservesActionSummaryWithoutTheMultiAccountEvidencePayload()
    {
        await using var fixture = await EndpointFixture.CreateAsync(
            incidents: [CreateMixedIncident()], reopenAudit: true);

        using var response = await fixture.Client.GetAsync("/api/execution/audit", fixture.Token);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync(fixture.Token);
        using var document = JsonDocument.Parse(json);
        var entry = document.RootElement.EnumerateArray().Should().ContainSingle().Which;
        entry.GetProperty("action").GetString().Should().Be("CancelAllIncidentCompleted");
        entry.GetProperty("outcome").GetString().Should().Be("Complete");
        if (entry.TryGetProperty("cancelAllEvidence", out var evidence))
            evidence.ValueKind.Should().Be(JsonValueKind.Null);
        json.Should().NotContain("account-order-").And.NotContain("other-account-order")
            .And.NotContain("unattributed-broker-order").And.NotContain("candidate-broker-order")
            .And.NotContain("other-broker-account-secret").And.NotContain("private-audit-path");
    }

    [Fact]
    public async Task PublicAuditReads_RedactLegacySweepAndLifecycleDetailsBeforeSearchAndExport()
    {
        var sensitiveEntries = new[]
        {
            SensitiveSweepAudit("KillSwitchSweep"),
            SensitiveSweepAudit("CircuitBreakerCancelAll"),
            SensitiveSweepAudit("OrderCancelled", "commandOrigin"),
            SensitiveSweepAudit("OrderCancelRejected", "cancelAllIncidentId")
        };
        var ordinary = new ExecutionAuditEntry("ordinary", "Order", "OrderCancelled", "Cancelled", ObservedAt,
            OrderId: "authorized-single-order", Symbol: "VISIBLE", Message: "Ordinary per-order audit.");
        await using var fixture = await EndpointFixture.CreateAsync(
            auditEntries: [.. sensitiveEntries, ordinary], reopenAudit: true);

        using var feedResponse = await fixture.Client.GetAsync("/api/execution/audit", fixture.Token);
        feedResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var feedJson = await feedResponse.Content.ReadAsStringAsync(fixture.Token);
        feedJson.Should().NotContain("private-account");
        var feed = JsonSerializer.Deserialize<ExecutionAuditEntry[]>(feedJson, JsonOptions)!;
        foreach (var original in sensitiveEntries)
        {
            var summary = feed.Single(entry => entry.AuditId == original.AuditId);
            summary.Action.Should().Be(original.Action);
            summary.Outcome.Should().Be(original.Outcome);
            summary.Actor.Should().Be(original.Actor);
            summary.CorrelationId.Should().Be(original.CorrelationId);
            summary.OrderId.Should().BeNull();
            summary.RunId.Should().BeNull();
            summary.Symbol.Should().BeNull();
            summary.Scope.Should().BeNull();
            summary.Reason.Should().BeNull();
            summary.CancelAllEvidence.Should().BeNull();
            summary.Message.Should().Contain("Select an execution account");
            summary.Metadata!.Keys.Should().Equal("publicProjection");
        }
        feed.Single(entry => entry.AuditId == ordinary.AuditId).Should().BeEquivalentTo(ordinary);

        using var browseResponse = await fixture.Client.GetAsync("/api/execution/audit/search", fixture.Token);
        browseResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var browseJson = await browseResponse.Content.ReadAsStringAsync(fixture.Token);
        browseJson.Should().NotContain("private-account");
        var browse = JsonSerializer.Deserialize<AuditTrailExplorerResultDto>(browseJson, JsonOptions)!;
        browse.Entries.Should().HaveCount(5);
        var projected = browse.Entries.Single(entry => entry.AuditId == "legacy-OrderCancelled");
        projected.ActionLedgerStatus.Should().Be("WalRetainedPublicSummary");
        // This is the existing canonical hash of the original retained lifecycle record, not
        // the hash of its redacted public summary. Redaction must not change action identity.
        projected.CurrentActionHash.Should().Be("613541b134725cf4581dca00ef0f7cb0560ec6ae1d33262e8ff9a79fb9573621");
        projected.OrderId.Should().BeNull();
        projected.ObjectId.Should().Be("incident-public-correlation");

        foreach (var query in new[]
        {
            "searchText=private-account",
            "orderId=private-account-client",
            "orderId=private-account-broker",
            "objectId=private-account-order",
            "symbol=PRIVATE-ACCOUNT-SYMBOL",
            "runId=private-account-run",
            "relatedObjectId=private-account-strategy"
        })
        {
            using var response = await fixture.Client.GetAsync($"/api/execution/audit/search?{query}", fixture.Token);
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var matches = JsonSerializer.Deserialize<AuditTrailExplorerResultDto>(
                await response.Content.ReadAsStringAsync(fixture.Token), JsonOptions)!;
            matches.TotalMatched.Should().Be(0, "matching must use the public projection, not hidden identifiers");
            matches.Entries.Should().BeEmpty();
        }

        var explorer = new AuditTrailExplorerService(fixture.AuditTrail);
        var export = await explorer.ExportAuthorizedAsync(null,
            (workflow, token) => Task.FromResult(false), fixture.Token);
        JsonSerializer.Serialize(export, JsonOptions).Should().NotContain("private-account");
        export.Entries.Single(entry => entry.AuditId == projected.AuditId).CurrentActionHash
            .Should().Be(projected.CurrentActionHash);
        var hiddenExport = await explorer.ExportAuthorizedAsync(new(SearchText: "private-account"),
            (workflow, token) => Task.FromResult(false), fixture.Token);
        hiddenExport.Entries.Should().BeEmpty();

        var retained = await fixture.AuditTrail!.GetAllAsync(fixture.Token);
        retained.Should().BeEquivalentTo(sensitiveEntries.Append(ordinary),
            "HTTP projection must not rewrite the retained evidence");
    }

    private static ExecutionAuditEntry SensitiveSweepAudit(string action, string? marker = null)
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["clientOrderId"] = "private-account-client",
            ["gatewayOrderId"] = "private-account-broker",
            ["strategyId"] = "private-account-strategy",
            ["privatePath"] = "/private-account/incident.wal"
        };
        if (marker is not null)
            metadata[marker] = marker == "commandOrigin" ? "KillSwitch" : "cancel-marker";
        return new ExecutionAuditEntry($"legacy-{action}", "Order", action, "Cancelled", ObservedAt,
            Actor: "incident-operator", BrokerName: "paper", OrderId: "private-account-order",
            RunId: "private-account-run", Symbol: "PRIVATE-ACCOUNT-SYMBOL",
            CorrelationId: "incident-public-correlation", Message: "Broker rejection for private-account-order.",
            Reason: "private-account-rejection", Scope: "strategy:private-account-strategy", Metadata: metadata);
    }

    private static void AssertDecimalText(JsonElement order, string name, string expected)
    {
        order.GetProperty(name).ValueKind.Should().Be(JsonValueKind.String,
            "JavaScript number parsing must not round retained execution quantities");
        order.GetProperty(name).GetString().Should().Be(expected);
    }

    private static CancelAllIncident CreateAccountIncident(string id, DateTimeOffset startedAt) => new()
    {
        IncidentId = id,
        StartedAt = startedAt,
        CompletedAt = startedAt.AddSeconds(1),
        Actor = "incident-operator",
        CorrelationId = "synthetic-cancel-correlation",
        GatewayId = "paper",
        ExecutionMode = ExecutionMode.Paper,
        TenantId = TenantId,
        CompanyId = CompanyId,
        FundAccountId = AccountId,
        EvidenceStatus = "Complete",
        BrokerViewUnavailable = true,
        BrokerViewError = "Broker convergence has not been observed."
    };

    private static CancelAllIncident CreateMixedIncident()
    {
        var orders = Enumerable.Range(0, 12).Select(index => new CancelAllOrderOutcome
        {
            ObservationKey = $"local:account-order-{index:D2}",
            OrderId = $"account-order-{index:D2}",
            BrokerOrderId = $"broker-order-{index:D2}",
            FundAccountId = AccountId,
            BrokerAccountId = "paper-account",
            Symbol = "TESTX",
            Side = OrderSide.Buy,
            Disposition = CancelAllOrderDisposition.ConfirmedCancelled,
            InitialStatus = OrderStatus.Accepted,
            Status = OrderStatus.Cancelled,
            InitialQuantity = 10m,
            InitialFilledQuantity = 0m,
            Quantity = 10m,
            FilledQuantity = 0m,
            RemainingQuantity = 0m,
            CancellationAttempted = true,
            ResidualNotional = 0m,
            Currency = "USD",
            ValuationCoverage = "NoRemainingOrderQuantity",
            ObservedAt = ObservedAt,
            Detail = "Synthetic cancel observation."
        }).ToArray();
        orders[0] = orders[0] with
        {
            Disposition = CancelAllOrderDisposition.Unknown,
            Status = OrderStatus.PendingCancel,
            Quantity = null, FilledQuantity = null, RemainingQuantity = null, ResidualNotional = null,
            RequiresReconciliation = true, ValuationCoverage = "Unavailable"
        };
        orders[1] = orders[1] with
        {
            HasUnconfirmedReplacement = true,
            CandidateBrokerOrderId = "candidate-broker-order",
            CandidateClientOrderId = "candidate-client-order",
            RemainingQuantity = null, ResidualNotional = null,
            ValuationCoverage = "Unavailable: unverified order sizing or successor exposure"
        };
        orders[2] = orders[2] with
        {
            Disposition = CancelAllOrderDisposition.StillWorking,
            Status = OrderStatus.PartiallyFilled,
            InitialQuantity = 9007199254740993.123456789m,
            Quantity = 9007199254740993.123456789m,
            FilledQuantity = 0.000000003m,
            RemainingQuantity = 9007199254740993.123456786m,
            ResidualNotional = 9007199.254740993123456786m,
            ValuationCoverage = "ObservedUsdEquityQuote",
            ValuationReference = new OrderMarketReference(0.000000001m, ObservedAt, "synthetic-ask")
        };
        orders[3] = orders[3] with { RequiresReconciliation = true };
        return CreateAccountIncident(IncidentId, ObservedAt) with
        {
            // A gateway-wide sweep can include several accounts and an unattributed broker order.
            FundAccountId = null,
            BrokerViewError = "Broker request failed for other-broker-account-secret.",
            EvidenceError = "Could not persist /private-audit-path/incident.wal.",
            Orders = [.. orders, CreateOtherAccountOrder(), new CancelAllOrderOutcome
            {
                ObservationKey = "broker:unattributed-broker-order",
                OrderId = "unattributed-broker-order",
                Disposition = CancelAllOrderDisposition.Unknown,
                ObservedAt = ObservedAt
            }]
        };
    }

    private static CancelAllOrderOutcome CreateOtherAccountOrder() => new()
    {
        ObservationKey = "local:other-account-order",
        OrderId = "other-account-order",
        FundAccountId = OtherAccountId,
        Symbol = "OTHER",
        Disposition = CancelAllOrderDisposition.StillWorking,
        Quantity = 100m,
        RemainingQuantity = 100m,
        ObservedAt = ObservedAt
    };

    private sealed class EndpointFixture(WebApplication app, ExecutionAuditTrailService? audit, string root,
        CancellationTokenSource timeout) : IAsyncDisposable
    {
        public HttpClient Client { get; } = app.GetTestClient();
        public CancellationToken Token => timeout.Token;
        public ExecutionAuditTrailService? AuditTrail => audit;

        public static async Task<EndpointFixture> CreateAsync(
            bool includeAudit = true,
            UserPermission permissions = UserPermission.ViewTrades,
            string? tenantId = TenantId,
            string? companyId = CompanyId,
            bool registerTenancy = true,
            bool bindFund = true,
            IReadOnlyList<CancelAllIncident>? incidents = null,
            IReadOnlyList<ExecutionAuditEntry>? auditEntries = null,
            bool reopenAudit = false)
        {
            var root = Path.Combine(Path.GetTempPath(), $"cancel-incident-http-{Guid.NewGuid():N}");
            var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var tenancy = new FileFundProfileTenancyRegistry(Path.Combine(root, "tenancy.json"));
            if (bindFund)
                await tenancy.BindAsync(FundId.ToString("D"), TenantId, CompanyId, timeout.Token);

            ExecutionAuditTrailService? audit = includeAudit ? CreateAudit(root) : null;
            if (audit is not null)
            {
                foreach (var incident in incidents ?? [])
                    await audit.RecordAsync(new ExecutionAuditEntry(
                        AuditId: $"{incident.IncidentId}:1",
                        Category: "Order",
                        Action: "CancelAllIncidentCompleted",
                        Outcome: "Complete",
                        OccurredAt: incident.CompletedAt!.Value,
                        Actor: incident.Actor,
                        CancelAllEvidence: new CancelAllIncidentUpdate(incident.IncidentId, 1, Snapshot: incident)),
                        timeout.Token);
                foreach (var entry in auditEntries ?? [])
                    await audit.RecordAsync(entry, timeout.Token);
                if (reopenAudit)
                {
                    await audit.DisposeAsync();
                    audit = CreateAudit(root);
                }
            }

            var accounts = Substitute.For<IAccountQueryService>();
            accounts.GetAccountAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(call =>
            {
                var id = call.ArgAt<Guid>(0);
                return Task.FromResult<AccountSummaryDto?>(id == AccountId || id == OtherAccountId
                    ? new AccountSummaryDto(id, AccountTypeDto.Brokerage, null, FundId, null, null,
                        "TEST-ACCOUNT", "Synthetic incident account", "USD", "Paper", true,
                        DateTimeOffset.UnixEpoch, null, null, null, null, null)
                    : null);
            });

            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = Environments.Development
            });
            builder.WebHost.UseTestServer();
            builder.Services.AddSingleton(accounts);
            builder.Services.AddSingleton<IScopedAuthorizationService>(new AccountTradingAuthorization());
            if (registerTenancy)
                builder.Services.AddSingleton<IFundProfileTenancyRegistry>(tenancy);
            if (audit is not null)
                builder.Services.AddSingleton(audit);
            var app = builder.Build();
            app.Use(async (context, next) =>
            {
                context.Items[LoginSessionMiddleware.CurrentUserKey] = "incident-operator";
                context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = permissions;
                if (tenantId is not null)
                    context.Items[LoginSessionMiddleware.CurrentTenantIdKey] = tenantId;
                if (companyId is not null)
                    context.Items[LoginSessionMiddleware.CurrentUserCompanyIdKey] = companyId;
                await next(context);
            });
            app.MapExecutionEndpoints(JsonOptions);
            await app.StartAsync(timeout.Token);
            return new EndpointFixture(app, audit, root, timeout);
        }

        private static ExecutionAuditTrailService CreateAudit(string root) => new(
            new ExecutionAuditTrailOptions(Path.Combine(root, "audit")),
            NullLogger<ExecutionAuditTrailService>.Instance);

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await app.DisposeAsync();
            if (audit is not null)
                await audit.DisposeAsync();
            timeout.Dispose();
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private sealed class AccountTradingAuthorization : IScopedAuthorizationService
    {
        public Task<ScopedAuthorizationDecisionDto> AuthorizeAsync(string actor, UserPermission requiredPermission,
            AccessScopeKindDto scopeKind, Guid? scopeId, UserPermission globalPermissions, CancellationToken ct = default)
        {
            var allowed = actor == "incident-operator" && requiredPermission == UserPermission.ViewTrades
                && scopeKind == AccessScopeKindDto.Account && scopeId == AccountId
                && globalPermissions.HasFlag(requiredPermission);
            return Task.FromResult(new ScopedAuthorizationDecisionDto(allowed, actor, requiredPermission,
                scopeKind, scopeId, allowed ? "Assigned account." : "No assigned account."));
        }
    }
}
