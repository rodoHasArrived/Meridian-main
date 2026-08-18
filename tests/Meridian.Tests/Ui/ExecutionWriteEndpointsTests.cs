using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using FluentAssertions.Execution;
using Meridian.Backtesting.Sdk;
using Meridian.Contracts.Api;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Workstation;
using Meridian.Execution;
using Meridian.Execution.Models;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Meridian.Identity;
using Meridian.Identity.Auth;
using Meridian.PortfolioRecords.Accounts;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Models;
using Meridian.Strategies.Promotions;
using Meridian.Strategies.Services;
using Meridian.Strategies.Storage;
using Meridian.Testing;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using ExecutionOrderRequest = Meridian.Execution.Sdk.OrderRequest;
using ExecutionServices = Meridian.Execution.Services;

namespace Meridian.Tests.Ui;

/// <summary>
/// Contract tests for execution write-action and blotter endpoints, including
/// named operator scenarios that guard against paper-session replay drift and
/// promotion decisions that are not visibly auditable.
/// </summary>
public sealed class ExecutionWriteEndpointsTests
{
    private const string ApprovedLiveRunId = "run-live-approved";

    [Fact]
    public async Task GetBlotterPositions_WhenServicesNotRegistered_Returns503()
    {
        await using var app = await CreateAppAsync(_ => { });

        var client = app.GetTestClient();
        var response = await client.GetAsync(UiApiRoutes.ExecutionBlotterPositions);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task GetBlotterPositions_WithPaperPositions_ReturnsPaperSnapshotWithoutDemoRows()
    {
        await using var app = await CreateAppAsync(services =>
            RegisterMinimalOms(
                services,
                new ExecutionPosition("AAPL", 10, 180m, 25m, 0m)));

        var client = app.GetTestClient();
        var response = await client.GetAsync(UiApiRoutes.ExecutionBlotterPositions);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var snapshot = await ReadAsync<ExecutionBlotterSnapshotResponse>(response);

        using (new AssertionScope())
        {
            snapshot.IsBrokerBacked.Should().BeFalse();
            snapshot.IsLive.Should().BeFalse();
            snapshot.Source.Should().Be("Paper Trading");
            snapshot.StatusMessage.Should().Contain("paper position");
            snapshot.Positions.Should().ContainSingle();
            snapshot.Positions[0].PositionKey.Should().Be("AAPL");
            snapshot.Positions[0].AssetClass.Should().Be("equity");
            snapshot.Positions[0].ProductDescription.Should().Be("AAPL");
        }
    }

    // ------------------------------------------------------------------ //
    //  POST /api/execution/orders/{orderId}/cancel                        //
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task CancelOrder_WhenOmsNotRegistered_Returns503()
    {
        await using var app = await CreateAppAsync(_ => { });

        var client = app.GetTestClient();
        var response = await client.PostAsync("/api/execution/orders/ord-001/cancel", null);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task CancelOrder_WithUnknownOrderId_ReturnsRejectedActionResult()
    {
        await using var app = await CreateAppAsync(RegisterMinimalOms);

        var client = app.GetTestClient();
        var response = await client.PostAsync("/api/execution/orders/NONEXISTENT/cancel", null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var result = await ReadActionResultAsync(response);
        result.Status.Should().Be("Rejected");
        result.ActionId.Should().NotBeNullOrEmpty();
        result.OccurredAt.Should().NotBe(default);
    }

    [Fact]
    public async Task CancelOrder_WhenUserLacksManageOrders_ReturnsForbidden()
    {
        await using var app = await CreateAppAsync(RegisterMinimalOms, UserPermission.ExecuteTrades);

        var client = app.GetTestClient();
        var response = await client.PostAsync("/api/execution/orders/ord-001/cancel", null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CancelOrder_WhenLiveProductionRoutingDisabled_CancelsOpenOrder()
    {
        var orderManager = new RecordingOrderManager(CreateOrderState("ord-live-001", "AAPL", 1m));
        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton<IOrderManager>(orderManager);
            services.AddSingleton(new BrokerageConfiguration
            {
                Gateway = "robinhood",
                LiveExecutionEnabled = true,
                ReadOnlyPhaseEnabled = true,
                PaperTradingPhaseEnabled = true,
                ProductionRoutingPhaseEnabled = false,
                BrokerFlows = new Dictionary<string, BrokerFlowFlags>(StringComparer.OrdinalIgnoreCase)
                {
                    ["robinhood"] = new() { ProductionOrderRoutingEnabled = true }
                }
            });
        });

        var client = app.GetTestClient();
        var response = await client.PostAsync("/api/execution/orders/ord-live-001/cancel", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await ReadActionResultAsync(response);
        result.Status.Should().Be("Completed");
        result.Message.Should().Contain("cancelled");
        orderManager.CancelledOrderIds.Should().ContainSingle().Which.Should().Be("ord-live-001");
    }

    [Fact]
    public async Task CancelOrder_ForAnotherFundsOrder_IsForbidden()
    {
        // Cancelling a parked order durably withdraws its governed approval. The escalation
        // routes only permit that within the caller's scoped authority over the owning fund,
        // so reaching the same withdrawal by client order id must not bypass the check.
        var ownedByAnotherFund = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        var callerScope = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
        var orderState = CreateOrderState("ord-fund-b", "AAPL", 1m) with { FundAccountId = ownedByAnotherFund };
        var orderManager = new RecordingOrderManager(orderState);

        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton<IOrderManager>(orderManager);
            services.AddSingleton<IScopedAuthorizationService>(
                _ => new SingleAccountScopedAuthorizationService(callerScope));
        });

        var client = app.GetTestClient();
        var response = await client.PostAsync("/api/execution/orders/ord-fund-b/cancel", null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        orderManager.CancelledOrderIds.Should().BeEmpty("an unauthorized cancel must not reach the OMS");
    }

    private sealed class SingleAccountScopedAuthorizationService(Guid allowedAccountId) : IScopedAuthorizationService
    {
        public Task<ScopedAuthorizationDecisionDto> AuthorizeAsync(
            string actor,
            UserPermission required,
            AccessScopeKindDto scopeKind,
            Guid? scopeId,
            UserPermission globalPermissions,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            var isAllowed =
                scopeKind == AccessScopeKindDto.Account &&
                scopeId == allowedAccountId &&
                (globalPermissions & required) == required;

            return Task.FromResult(new ScopedAuthorizationDecisionDto(
                IsAllowed: isAllowed,
                Actor: actor,
                RequiredPermission: required,
                ScopeKind: scopeKind,
                ScopeId: scopeId,
                Reason: isAllowed ? "in scope" : "outside the caller's scoped accounts"));
        }
    }

    // ------------------------------------------------------------------ //
    //  POST /api/execution/orders/cancel-all                              //
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task CancelAllOrders_WhenOmsNotRegistered_Returns503()
    {
        await using var app = await CreateAppAsync(_ => { });

        var client = app.GetTestClient();
        var response = await client.PostAsync("/api/execution/orders/cancel-all", null);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task CancelAllOrders_WhenNoOpenOrders_ReturnsCompletedActionResult()
    {
        await using var app = await CreateAppAsync(RegisterMinimalOms);

        var client = app.GetTestClient();
        var response = await client.PostAsync("/api/execution/orders/cancel-all", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await ReadActionResultAsync(response);
        result.Status.Should().Be("Completed");
        result.ActionId.Should().NotBeNullOrEmpty();
        result.Message.Should().Contain("0");
    }


    [Fact]
    public async Task CancelAllOrders_WhenProductionPhaseDisabledForPaperFlow_StillCancels()
    {
        await using var app = await CreateAppAsync(services =>
        {
            RegisterMinimalOms(services);
            services.AddSingleton(new BrokerageConfiguration
            {
                ProductionRoutingPhaseEnabled = false
            });
        });

        var client = app.GetTestClient();
        var response = await client.PostAsync("/api/execution/orders/cancel-all", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await ReadActionResultAsync(response);
        result.Status.Should().Be("Completed");
    }

    [Fact]
    public async Task CancelAllOrders_WhenLiveProductionRoutingDisabled_CancelsOpenOrders()
    {
        var orderManager = new RecordingOrderManager(CreateOrderState("ord-live-001", "AAPL", 1m));
        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton<IOrderManager>(orderManager);
            services.AddSingleton(new BrokerageConfiguration
            {
                Gateway = "robinhood",
                LiveExecutionEnabled = true,
                ReadOnlyPhaseEnabled = true,
                PaperTradingPhaseEnabled = true,
                ProductionRoutingPhaseEnabled = false,
                BrokerFlows = new Dictionary<string, BrokerFlowFlags>(StringComparer.OrdinalIgnoreCase)
                {
                    ["robinhood"] = new() { ProductionOrderRoutingEnabled = true }
                }
            });
        });

        var client = app.GetTestClient();
        var response = await client.PostAsync("/api/execution/orders/cancel-all", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await ReadActionResultAsync(response);
        result.Status.Should().Be("Completed");
        result.Message.Should().Contain("1");
        orderManager.CancelAllCallCount.Should().Be(1);
        orderManager.CancelledOrderIds.Should().ContainSingle().Which.Should().Be("ord-live-001");
    }

    /// <summary>
    /// A broker that refuses one cancellation leaves that order working, and the operator has to
    /// be told which one. The endpoint used to report <c>Completed</c> for any sweep that returned
    /// without throwing, so a half-emptied book produced a success ticket.
    /// </summary>
    [Fact]
    public async Task CancelAllOrders_WhenTheBrokerRefusesOne_ReportsPartialAndNamesTheOrderStillWorking()
    {
        var orderManager = new RecordingOrderManager(
            CreateOrderState("ord-live-001", "AAPL", 1m),
            CreateOrderState("ord-live-002", "MSFT", 1m));
        orderManager.RefuseToCancel.Add("ord-live-002");

        await using var app = await CreateAppAsync(services => services.AddSingleton<IOrderManager>(orderManager));

        var client = app.GetTestClient();
        var response = await client.PostAsync("/api/execution/orders/cancel-all", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await ReadActionResultAsync(response);
        result.Status.Should().Be("Partial", "one order survived the sweep");
        result.Message.Should().Contain("ord-live-002", "an operator cannot cancel by hand what the ticket does not name");
        orderManager.CancelledOrderIds.Should().ContainSingle().Which.Should().Be("ord-live-001");
    }

    /// <summary>
    /// When nothing could be cancelled the sweep failed outright, which calls for a different
    /// operator response than a partial one: the kill switch did not fire at all.
    /// </summary>
    [Fact]
    public async Task CancelAllOrders_WhenTheBrokerRefusesEverything_ReportsFailed()
    {
        var orderManager = new RecordingOrderManager(CreateOrderState("ord-live-001", "AAPL", 1m));
        orderManager.RefuseToCancel.Add("ord-live-001");

        await using var app = await CreateAppAsync(services => services.AddSingleton<IOrderManager>(orderManager));

        var client = app.GetTestClient();
        var response = await client.PostAsync("/api/execution/orders/cancel-all", null);

        var result = await ReadActionResultAsync(response);
        result.Status.Should().Be("Failed");
        orderManager.CancelledOrderIds.Should().BeEmpty();
    }

    // ------------------------------------------------------------------ //
    //  POST /api/execution/positions/*                                    //
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task ClosePosition_WhenServicesNotRegistered_Returns503()
    {
        await using var app = await CreateAppAsync(_ => { });

        var client = app.GetTestClient();
        var response = await client.PostAsync("/api/execution/positions/AAPL/close", null);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }


    [Fact]
    public async Task ClosePosition_WhenProductionPhaseDisabled_StillSubmitsPaperClose()
    {
        await using var app = await CreateAppAsync(services =>
        {
            RegisterMinimalOms(
                services,
                new ExecutionPosition("AAPL", 5, 180m, 10m, 0m));
            // The paper gateway fails closed on priceless market orders when no live feed is
            // wired; this test asserts the paper close is submitted, not fill-price realism, so
            // opt into scaffold pricing to exercise the submission path.
            services.AddSingleton(new Meridian.Execution.Adapters.PaperTradingGatewayOptions
            {
                AllowScaffoldMarketFills = true
            });
            services.AddSingleton(new BrokerageConfiguration
            {
                ProductionRoutingPhaseEnabled = false,
                BrokerFlows =
                {
                    ["paper"] = new BrokerFlowFlags { PaperOrderFlowEnabled = true }
                }
            });
        });

        var client = app.GetTestClient();
        var response = await client.PostAsync("/api/execution/positions/AAPL/close", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await ReadActionResultAsync(response);
        result.Status.Should().Be("Accepted");
    }

    [Fact]
    public async Task ClosePosition_WhenSymbolHasNoPosition_ReturnsRejectedActionResult()
    {
        await using var app = await CreateAppAsync(RegisterMinimalOms);

        var client = app.GetTestClient();
        var response = await client.PostAsync("/api/execution/positions/AAPL/close", null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var result = await ReadActionResultAsync(response);
        result.Status.Should().Be("Rejected");
        result.ActionId.Should().NotBeNullOrEmpty();
        result.Message.Should().Contain("AAPL");
    }

    [Fact]
    public async Task ClosePositionByKey_WithPaperPosition_SubmitsOrder()
    {
        await using var app = await CreateAppAsync(services =>
        {
            RegisterMinimalOms(
                services,
                new ExecutionPosition("AAPL", 5, 180m, 10m, 0m));
            // The paper gateway fails closed on priceless market orders when no live feed is
            // wired; this test asserts the close order is submitted, not fill-price realism, so
            // opt into scaffold pricing to exercise the submission path.
            services.AddSingleton(new Meridian.Execution.Adapters.PaperTradingGatewayOptions
            {
                AllowScaffoldMarketFills = true
            });
        });

        var client = app.GetTestClient();
        var response = await client.PostAsync(
            UiApiRoutes.ExecutionPositionActionClose,
            JsonContent(new ExecutionPositionActionRequest("AAPL")));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await ReadActionResultAsync(response);
        result.Status.Should().Be("Accepted");
        result.Message.Should().Contain("AAPL");
    }

    [Fact]
    public async Task ClosePositionByKey_WhenProductionRoutingDisabled_Returns403AndDoesNotSubmit()
    {
        var gateway = new RecordingBrokerageGateway(CreateRobinhoodOptionPosition("opt-close"));
        await using var app = await CreateAppAsync(services =>
        {
            RegisterBrokerageOms(services, gateway);
            services.AddSingleton(new BrokerageConfiguration
            {
                Gateway = "robinhood",
                BrokerFlows = new Dictionary<string, BrokerFlowFlags>(StringComparer.OrdinalIgnoreCase)
                {
                    ["robinhood"] = new() { ProductionOrderRoutingEnabled = false }
                }
            });
        });

        var client = app.GetTestClient();
        var response = await client.PostAsync(
            UiApiRoutes.ExecutionPositionActionClose,
            JsonContent(new ExecutionPositionActionRequest("opt-close", Quantity: 1m)));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var result = await ReadActionResultAsync(response);
        result.Status.Should().Be("Rejected");
        result.Message.Should().Contain("Production order routing is disabled");
        gateway.SubmittedRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task ClosePositionByKey_WhenUserLacksExecuteTrades_ReturnsForbidden()
    {
        await using var app = await CreateAppAsync(
            services => RegisterMinimalOms(
                services,
                new ExecutionPosition("AAPL", 5, 180m, 10m, 0m)),
            UserPermission.ManageOrders);

        var client = app.GetTestClient();
        var response = await client.PostAsync(
            UiApiRoutes.ExecutionPositionActionClose,
            JsonContent(new ExecutionPositionActionRequest("AAPL")));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ClosePosition_WhenBrokerSnapshotHasMultipleMatches_ReturnsAmbiguousResult()
    {
        var gateway = new RecordingBrokerageGateway(
            CreateRobinhoodOptionPosition("opt-1"),
            CreateRobinhoodOptionPosition("opt-2", expiration: new DateOnly(2026, 6, 19), strike: 185m));

        await using var app = await CreateAppAsync(services => RegisterBrokerageOms(services, gateway));

        var client = app.GetTestClient();
        var response = await client.PostAsync("/api/execution/positions/AAPL/close", null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var result = await ReadActionResultAsync(response);
        result.Status.Should().Be("Rejected");
        result.Message.Should().Contain("Use the keyed position action endpoint");
        gateway.SubmittedRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task ClosePositionByKey_WithBrokerOptionPosition_PassesRobinhoodOptionMetadata()
    {
        var gateway = new RecordingBrokerageGateway(CreateRobinhoodOptionPosition("opt-close"));

        await using var app = await CreateAppAsync(services => RegisterBrokerageOms(services, gateway));

        var client = app.GetTestClient();
        var response = await client.PostAsync(
            UiApiRoutes.ExecutionPositionActionClose,
            JsonContent(new ExecutionPositionActionRequest("opt-close", Quantity: 1m)));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await ReadActionResultAsync(response);
        result.Status.Should().Be("Accepted");

        var request = gateway.SubmittedRequests.Should().ContainSingle().Subject;
        using (new AssertionScope())
        {
            request.Symbol.Should().Be("AAPL");
            request.Side.Should().Be(OrderSide.Sell);
            request.Quantity.Should().Be(1m);
            request.Metadata.Should().NotBeNull();
            request.Metadata!["asset_class"].Should().Be("option");
            request.Metadata["option_instrument_url"].Should().Be("https://api.robinhood.com/options/instruments/opt-close/");
            request.Metadata["position_effect"].Should().Be("close");
            request.Metadata["positionKey"].Should().Be("opt-close");
            request.Metadata["positionSource"].Should().Be("Robinhood (test)");
        }
    }

    [Fact]
    public async Task ClosePositionByKey_WithFundAccountScope_PassesScopeToBrokerOrder()
    {
        var fundAccountId = Guid.Parse("53bf0251-17f6-4fb7-8dbe-6fb4966e2749");
        var gateway = new RecordingBrokerageGateway(CreateRobinhoodOptionPosition("opt-close"));

        await using var app = await CreateAppAsync(services =>
        {
            RegisterBrokerageOms(services, gateway);
            RegisterFundAccountScope(services, fundAccountId, isAllowed: true);
        });

        var client = app.GetTestClient();
        var response = await client.PostAsync(
            UiApiRoutes.ExecutionPositionActionClose,
            JsonContent(new ExecutionPositionActionRequest("opt-close", Quantity: 1m, FundAccountId: fundAccountId)));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var request = gateway.SubmittedRequests.Should().ContainSingle().Subject;
        request.FundAccountId.Should().Be(fundAccountId);
    }

    [Fact]
    public async Task ClosePosition_WithFundAccountQuery_PassesScopeToBrokerOrder()
    {
        var fundAccountId = Guid.Parse("53bf0251-17f6-4fb7-8dbe-6fb4966e2749");
        var gateway = new RecordingBrokerageGateway(CreateRobinhoodOptionPosition("opt-close"));

        await using var app = await CreateAppAsync(services =>
        {
            RegisterBrokerageOms(services, gateway);
            RegisterFundAccountScope(services, fundAccountId, isAllowed: true);
        });

        var client = app.GetTestClient();
        var response = await client.PostAsync($"/api/execution/positions/AAPL/close?fundAccountId={fundAccountId:D}", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var request = gateway.SubmittedRequests.Should().ContainSingle().Subject;
        request.FundAccountId.Should().Be(fundAccountId);
    }

    [Fact]
    public async Task ClosePositionByKey_WithUnscopedFundAccount_ReturnsForbiddenAndDoesNotSubmit()
    {
        var fundAccountId = Guid.Parse("53bf0251-17f6-4fb7-8dbe-6fb4966e2749");
        var gateway = new RecordingBrokerageGateway(CreateRobinhoodOptionPosition("opt-close"));

        await using var app = await CreateAppAsync(
            services => RegisterBrokerageOms(services, gateway),
            allowedAccountScopes: []);

        var client = app.GetTestClient();
        var response = await client.PostAsync(
            UiApiRoutes.ExecutionPositionActionClose,
            JsonContent(new ExecutionPositionActionRequest("opt-close", Quantity: 1m, FundAccountId: fundAccountId)));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        gateway.SubmittedRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task ClosePositionByKey_WithBrokerSuppliedAccountMetadata_StripsServerOwnedRoutingKeys()
    {
        var position = CreateRobinhoodOptionPosition("opt-close") with
        {
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["asset_class"] = "option",
                ["option_instrument_url"] = "https://api.robinhood.com/options/instruments/opt-close/",
                ["broker_account_id"] = "attacker-controlled-account",
                ["account_id"] = "attacker-ledger-account",
                ["manualOverrideId"] = "forged-override",
                ["runId"] = ApprovedLiveRunId
            }
        };
        var gateway = new RecordingBrokerageGateway(position);

        await using var app = await CreateAppAsync(services => RegisterBrokerageOms(services, gateway));

        var client = app.GetTestClient();
        var response = await client.PostAsync(
            UiApiRoutes.ExecutionPositionActionClose,
            JsonContent(new ExecutionPositionActionRequest("opt-close", Quantity: 1m)));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var request = gateway.SubmittedRequests.Should().ContainSingle().Subject;
        request.Metadata.Should().NotBeNull();
        request.Metadata!.Should().NotContainKey("broker_account_id");
        request.Metadata.Should().NotContainKey("account_id");
        request.Metadata.Should().NotContainKey("manualOverrideId");
        request.Metadata["asset_class"].Should().Be("option");
        request.Metadata["option_instrument_url"].Should().Be("https://api.robinhood.com/options/instruments/opt-close/");
    }

    [Fact]
    public async Task UpsizePositionByKey_WithBrokerOptionPosition_UsesOpenPositionEffect()
    {
        var gateway = new RecordingBrokerageGateway(CreateRobinhoodOptionPosition("opt-upsize"));

        await using var app = await CreateAppAsync(services => RegisterBrokerageOms(services, gateway));

        var client = app.GetTestClient();
        var response = await client.PostAsync(
            UiApiRoutes.ExecutionPositionActionUpsize,
            JsonContent(new ExecutionPositionActionRequest("opt-upsize", Quantity: 2m)));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await ReadActionResultAsync(response);
        result.Status.Should().Be("Accepted");

        var request = gateway.SubmittedRequests.Should().ContainSingle().Subject;
        request.Side.Should().Be(OrderSide.Buy);
        request.Quantity.Should().Be(2m);
        request.Metadata.Should().NotBeNull();
        request.Metadata!["position_effect"].Should().Be("open");
        request.Metadata["option_instrument_url"].Should().Be("https://api.robinhood.com/options/instruments/opt-upsize/");
    }

    [Fact]
    public async Task SubmitOrder_WhenPaperFlowFlagDisabled_Returns403AndDoesNotSubmit()
    {
        await using var app = await CreateAppAsync(services =>
        {
            RegisterMinimalOms(services);
            services.AddSingleton(new BrokerageConfiguration
            {
                Gateway = "paper",
                BrokerFlows = new Dictionary<string, BrokerFlowFlags>(StringComparer.OrdinalIgnoreCase)
                {
                    ["paper"] = new() { PaperOrderFlowEnabled = false }
                }
            });
        });

        var client = app.GetTestClient();
        var response = await client.PostAsync(
            UiApiRoutes.ExecutionOrderSubmit,
            JsonContent(new ExecutionOrderRequest
            {
                Symbol = "AAPL",
                Side = OrderSide.Buy,
                Type = Meridian.Execution.Sdk.OrderType.Market,
                Quantity = 1m
            }));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var result = await ReadAsync<OrderResult>(response);
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Paper order flow is disabled");
    }

    [Fact]
    public async Task SubmitOrder_WhenBrokerageConfigMissingForBrokerGateway_Returns403AndDoesNotSubmit()
    {
        var gateway = new RecordingBrokerageGateway(CreateRobinhoodOptionPosition("opt-upsize"));
        await using var app = await CreateAppAsync(services =>
        {
            RegisterBrokerageOmsWithoutConfiguration(services, gateway);
        });

        var client = app.GetTestClient();
        var response = await client.PostAsync(
            UiApiRoutes.ExecutionOrderSubmit,
            JsonContent(new ExecutionOrderRequest
            {
                Symbol = "AAPL",
                Side = OrderSide.Buy,
                Type = Meridian.Execution.Sdk.OrderType.Market,
                Quantity = 1m
            }));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var result = await ReadAsync<OrderResult>(response);
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Brokerage configuration is required");
        gateway.SubmittedRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task SubmitOrder_WhenUserLacksExecuteTrades_ReturnsForbidden()
    {
        var gateway = new RecordingBrokerageGateway(CreateRobinhoodOptionPosition("opt-upsize"));
        await using var app = await CreateAppAsync(
            services => RegisterBrokerageOms(services, gateway),
            UserPermission.ViewTrades);

        var client = app.GetTestClient();
        var response = await client.PostAsync(
            UiApiRoutes.ExecutionOrderSubmit,
            JsonContent(new ExecutionOrderRequest
            {
                Symbol = "AAPL",
                Side = OrderSide.Buy,
                Type = Meridian.Execution.Sdk.OrderType.Market,
                Quantity = 1m
            }));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        gateway.SubmittedRequests.Should().BeEmpty();
    }


    [Fact]
    public async Task SubmitOrder_WithUnauthorizedFundAccountScope_Returns403AndDoesNotSubmit()
    {
        var fundAccountId = Guid.Parse("53bf0251-17f6-4fb7-8dbe-6fb4966e2749");
        var gateway = new RecordingBrokerageGateway(CreateRobinhoodOptionPosition("opt-upsize"));

        await using var app = await CreateAppAsync(services =>
        {
            RegisterBrokerageOms(services, gateway);
            RegisterFundAccountScope(services, fundAccountId, isAllowed: false);
        });

        var client = app.GetTestClient();
        var response = await client.PostAsync(
            UiApiRoutes.ExecutionOrderSubmit,
            JsonContent(new ExecutionOrderRequest
            {
                Symbol = "AAPL",
                Side = OrderSide.Buy,
                Type = Meridian.Execution.Sdk.OrderType.Market,
                Quantity = 1m,
                FundAccountId = fundAccountId
            }));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        gateway.SubmittedRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task SubmitOrder_WithClientBrokerAccountMetadata_Returns403AndDoesNotSubmit()
    {
        var gateway = new RecordingBrokerageGateway(CreateRobinhoodOptionPosition("opt-upsize"));
        await using var app = await CreateAppAsync(services => RegisterBrokerageOms(services, gateway));

        var client = app.GetTestClient();
        var response = await client.PostAsync(
            UiApiRoutes.ExecutionOrderSubmit,
            JsonContent(new ExecutionOrderRequest
            {
                Symbol = "912797AB1",
                Side = OrderSide.Buy,
                Type = Meridian.Execution.Sdk.OrderType.Market,
                Quantity = 1000m,
                Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["asset_class"] = "treasury",
                    ["broker_account_id"] = "attacker-account"
                }
            }));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var result = await ReadAsync<OrderResult>(response);
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("server-side");
        gateway.SubmittedRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task SubmitOrder_WithClientLiveReadinessEvidenceMetadata_Returns403AndDoesNotSubmit()
    {
        var gateway = new RecordingBrokerageGateway(CreateRobinhoodOptionPosition("opt-upsize"));
        await using var app = await CreateAppAsync(services => RegisterBrokerageOms(services, gateway));

        var client = app.GetTestClient();
        var response = await client.PostAsync(
            UiApiRoutes.ExecutionOrderSubmit,
            JsonContent(new ExecutionOrderRequest
            {
                Symbol = "AAPL",
                Side = OrderSide.Buy,
                Type = Meridian.Execution.Sdk.OrderType.Market,
                Quantity = 1m,
                Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["runId"] = ApprovedLiveRunId,
                    ["liveReadinessEvidenceReference"] = "forged-readiness-evidence"
                }
            }));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var result = await ReadAsync<OrderResult>(response);
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("server-side");
        gateway.SubmittedRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task SubmitOrder_WithClientAssetClassRoutingMetadata_Returns403AndDoesNotSubmit()
    {
        var gateway = new RecordingBrokerageGateway(CreateRobinhoodOptionPosition("opt-upsize"));
        await using var app = await CreateAppAsync(services => RegisterBrokerageOms(services, gateway));

        var client = app.GetTestClient();
        var response = await client.PostAsync(
            UiApiRoutes.ExecutionOrderSubmit,
            JsonContent(new ExecutionOrderRequest
            {
                Symbol = "AAPL",
                Side = OrderSide.Buy,
                Type = Meridian.Execution.Sdk.OrderType.Market,
                Quantity = 1m,
                Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["asset_class"] = "treasury"
                }
            }));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var result = await ReadAsync<OrderResult>(response);
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("server-side");
        gateway.SubmittedRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task SubmitOrder_WithUnscopedFundAccount_ReturnsForbiddenAndDoesNotSubmit()
    {
        var fundAccountId = Guid.Parse("53bf0251-17f6-4fb7-8dbe-6fb4966e2749");
        var gateway = new RecordingBrokerageGateway(CreateRobinhoodOptionPosition("opt-upsize"));
        await using var app = await CreateAppAsync(
            services => RegisterBrokerageOms(services, gateway),
            allowedAccountScopes: []);

        var client = app.GetTestClient();
        var response = await client.PostAsync(
            UiApiRoutes.ExecutionOrderSubmit,
            JsonContent(new ExecutionOrderRequest
            {
                Symbol = "AAPL",
                Side = OrderSide.Buy,
                Type = Meridian.Execution.Sdk.OrderType.Market,
                Quantity = 1m,
                FundAccountId = fundAccountId
            }));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        gateway.SubmittedRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task SubmitOrder_WhenActorMissing_ReturnsUnauthorizedAndDoesNotSubmit()
    {
        var gateway = new RecordingBrokerageGateway(CreateRobinhoodOptionPosition("opt-upsize"));
        await using var app = await CreateAppAsync(
            services => RegisterBrokerageOms(services, gateway),
            currentUser: null);

        var client = app.GetTestClient();
        var response = await client.PostAsync(
            UiApiRoutes.ExecutionOrderSubmit,
            JsonContent(new ExecutionOrderRequest
            {
                Symbol = "AAPL",
                Side = OrderSide.Buy,
                Type = Meridian.Execution.Sdk.OrderType.Market,
                Quantity = 1m
            }));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        gateway.SubmittedRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task UpdateExecutionCircuitBreaker_WhenUserLacksManageOrders_ReturnsForbidden()
    {
        await using var app = await CreateAppAsync(
            services => services.AddSingleton(new ExecutionOperatorControlService()),
            UserPermission.ExecuteTrades);

        var client = app.GetTestClient();
        var response = await client.PostAsync(
            "/api/execution/controls/circuit-breaker",
            JsonContent(new UpdateExecutionCircuitBreakerRequest(true, "test")));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CreateExecutionSession_WhenUserLacksExecuteTrades_ReturnsForbidden()
    {
        using var artifacts = TestArtifactDirectory.Create(nameof(CreateExecutionSession_WhenUserLacksExecuteTrades_ReturnsForbidden));
        await using var app = await CreateAppAsync(
            services => RegisterSessionServices(services, artifacts.RootPath),
            currentUserPermissions: UserPermission.ViewTrades);

        var client = app.GetTestClient();
        var response = await client.PostAsync(
            UiApiRoutes.ExecutionSessionCreate,
            JsonContent(new CreatePaperSessionRequest(
                StrategyId: "strat-session",
                StrategyName: "Session Strategy",
                InitialCash: 125_000m,
                Symbols: ["AAPL"])));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task SubmitOrder_WhenProductionRoutingEnabledButValidationArtifactsMissing_Returns403()
    {
        var gateway = new RecordingBrokerageGateway(CreateRobinhoodOptionPosition("opt-upsize"));
        await using var app = await CreateAppAsync(services =>
        {
            RegisterBrokerageOms(services, gateway);
            services.AddSingleton(new BrokerageConfiguration
            {
                Gateway = "robinhood",
                LiveExecutionEnabled = true,
                ReadOnlyPhaseEnabled = true,
                PaperTradingPhaseEnabled = true,
                ProductionRoutingPhaseEnabled = true,
                ReadOnlyVerificationPassed = true,
                PaperLifecycleTestsPassed = true,
                ReplayEvidencePassed = true,
                BrokerFlows = new Dictionary<string, BrokerFlowFlags>(StringComparer.OrdinalIgnoreCase)
                {
                    ["robinhood"] = new() { ProductionOrderRoutingEnabled = true }
                },
                ValidationGates = new BrokerValidationGateOptions
                {
                    RequireValidationArtifactsForOrderPlacement = true,
                    ValidationArtifactPath = Path.Combine(Path.GetTempPath(), "missing-validation-artifact.json"),
                    SignoffArtifactPath = Path.Combine(Path.GetTempPath(), "missing-signoff-artifact.json")
                }
            });
        });

        var client = app.GetTestClient();
        var response = await client.PostAsync(
            UiApiRoutes.ExecutionOrderSubmit,
            JsonContent(new ExecutionOrderRequest
            {
                Symbol = "AAPL",
                Side = OrderSide.Buy,
                Type = Meridian.Execution.Sdk.OrderType.Market,
                Quantity = 1m
            }));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var result = await ReadAsync<OrderResult>(response);
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("validation artifact");
        gateway.SubmittedRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task ClosePositionAction_WhenProductionRoutingDisabled_Returns403AndDoesNotSubmit()
    {
        var gateway = new RecordingBrokerageGateway(CreateRobinhoodOptionPosition("opt-close"));
        await using var app = await CreateAppAsync(services =>
        {
            RegisterBrokerageOms(services, gateway);
            services.AddSingleton(new BrokerageConfiguration
            {
                Gateway = "robinhood",
                BrokerFlows = new Dictionary<string, BrokerFlowFlags>(StringComparer.OrdinalIgnoreCase)
                {
                    ["robinhood"] = new() { ProductionOrderRoutingEnabled = false }
                }
            });
        });

        var client = app.GetTestClient();
        var response = await client.PostAsync(
            UiApiRoutes.ExecutionPositionActionClose,
            JsonContent(new ExecutionPositionActionRequest("opt-close", Quantity: 1m)));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var result = await ReadActionResultAsync(response);
        result.Status.Should().Be("Rejected");
        result.Message.Should().Contain("Production order routing is disabled");
        gateway.SubmittedRequests.Should().BeEmpty();
    }


    [Fact]
    public async Task PaperSessionLifecycleEndpoints_PreserveSymbolsAndExposeReplayContinuityAudit()
    {
        using var artifacts = TestArtifactDirectory.Create(nameof(PaperSessionLifecycleEndpoints_PreserveSymbolsAndExposeReplayContinuityAudit));
        await using var app = await CreateAppAsync(
            services => RegisterSessionServices(services, artifacts.RootPath),
            currentUser: "ops-session");

        var client = app.GetTestClient();

        var createResponse = await client.PostAsync(
            UiApiRoutes.ExecutionSessionCreate,
            JsonContent(new CreatePaperSessionRequest(
                StrategyId: "strat-session",
                StrategyName: "Session Strategy",
                InitialCash: 125_000m,
                Symbols: ["AAPL", "MSFT"])));

        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var summary = await ReadAsync<ExecutionServices.PaperSessionSummaryDto>(createResponse);

        var persistence = app.Services.GetRequiredService<ExecutionServices.PaperSessionPersistenceService>();
        await persistence.RecordFillAsync(summary.SessionId, CreateFill("AAPL", 5m, 200m));
        await persistence.RecordOrderUpdateAsync(summary.SessionId, CreateOrderState("order-session-1", "AAPL", 5m));

        var detailResponse = await client.GetAsync(
            UiApiRoutes.ExecutionSessionById.Replace("{sessionId}", summary.SessionId, StringComparison.Ordinal));
        detailResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var detail = await ReadAsync<ExecutionServices.PaperSessionDetailDto>(detailResponse);
        detail.Symbols.Should().Equal("AAPL", "MSFT");

        var closeResponse = await client.PostAsync(
            UiApiRoutes.ExecutionSessionClose.Replace("{sessionId}", summary.SessionId, StringComparison.Ordinal),
            content: null);
        closeResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var closeResult = await ReadActionResultAsync(closeResponse);
        closeResult.Status.Should().Be("Completed");
        closeResult.AuditId.Should().NotBeNullOrWhiteSpace();

        var replayResponse = await client.GetAsync(
            UiApiRoutes.ExecutionSessionReplay.Replace("{sessionId}", summary.SessionId, StringComparison.Ordinal));
        replayResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var replayVerification = await ReadAsync<ExecutionServices.PaperSessionReplayVerificationDto>(replayResponse);

        using (new AssertionScope())
        {
            replayVerification.Summary.SessionId.Should().Be(summary.SessionId);
            replayVerification.Symbols.Should().Equal("AAPL", "MSFT");
            replayVerification.ReplaySource.Should().Be("DurableFillLog");
            replayVerification.IsConsistent.Should().BeTrue();
            replayVerification.MismatchReasons.Should().BeEmpty();
            replayVerification.CurrentPortfolio.Should().NotBeNull();
            replayVerification.ReplayPortfolio.Cash.Should().Be(124_000m);
            replayVerification.ComparedFillCount.Should().Be(1);
            replayVerification.ComparedOrderCount.Should().Be(1);
            replayVerification.ComparedLedgerEntryCount.Should().BeGreaterThanOrEqualTo(0);
            replayVerification.LastPersistedFillAt.Should().NotBeNull();
            replayVerification.LastPersistedOrderUpdateAt.Should().NotBeNull();
            replayVerification.VerificationAuditId.Should().NotBeNullOrWhiteSpace();
        }

        var auditResponse = await client.GetAsync(UiApiRoutes.ExecutionAudit);
        auditResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var audits = await ReadAsync<ExecutionServices.ExecutionAuditEntry[]>(auditResponse);
        audits.Should().Contain(entry => entry.Action == "CreatePaperSession" && entry.Actor == "ops-session" && entry.Metadata!["sessionId"] == summary.SessionId);
        audits.Should().Contain(entry => entry.Action == "ClosePaperSession" && entry.Actor == "ops-session" && entry.Metadata!["sessionId"] == summary.SessionId);
        audits.Should().Contain(entry => entry.Action == "ReplayPaperSession" && entry.Actor == "ops-session" && entry.Metadata!["sessionId"] == summary.SessionId);
    }

    [Fact]
    public async Task PaperSessionEndpoints_AfterRestart_RestoreSessionAndReplayEvidence()
    {
        using var artifacts = TestArtifactDirectory.Create(nameof(PaperSessionEndpoints_AfterRestart_RestoreSessionAndReplayEvidence));

        await using var firstApp = await CreateAppAsync(services => RegisterSessionServices(services, artifacts.RootPath));
        var firstClient = firstApp.GetTestClient();

        var createResponse = await firstClient.PostAsync(
            UiApiRoutes.ExecutionSessionCreate,
            JsonContent(new CreatePaperSessionRequest(
                StrategyId: "strat-restart",
                StrategyName: "Restart Strategy",
                InitialCash: 100_000m,
                Symbols: ["AAPL"])));

        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var summary = await ReadAsync<ExecutionServices.PaperSessionSummaryDto>(createResponse);

        var firstPersistence = firstApp.Services.GetRequiredService<ExecutionServices.PaperSessionPersistenceService>();
        await firstPersistence.RecordFillAsync(summary.SessionId, CreateFill("AAPL", 10m, 150m));
        await firstPersistence.RecordOrderUpdateAsync(summary.SessionId, CreateOrderState("order-restart-1", "AAPL", 10m));

        await using var secondApp = await CreateAppAsync(services => RegisterSessionServices(services, artifacts.RootPath));
        var secondClient = secondApp.GetTestClient();

        var detailResponse = await secondClient.GetAsync(
            UiApiRoutes.ExecutionSessionById.Replace("{sessionId}", summary.SessionId, StringComparison.Ordinal));
        detailResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var detail = await ReadAsync<ExecutionServices.PaperSessionDetailDto>(detailResponse);

        detail.Symbols.Should().Equal("AAPL");
        detail.OrderHistory.Should().ContainSingle(order => order.OrderId == "order-restart-1");

        var replayResponse = await secondClient.GetAsync(
            UiApiRoutes.ExecutionSessionReplay.Replace("{sessionId}", summary.SessionId, StringComparison.Ordinal));
        replayResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var replayVerification = await ReadAsync<ExecutionServices.PaperSessionReplayVerificationDto>(replayResponse);

        using (new AssertionScope())
        {
            replayVerification.IsConsistent.Should().BeTrue();
            replayVerification.ComparedFillCount.Should().Be(1);
            replayVerification.ComparedOrderCount.Should().Be(1);
            replayVerification.LastPersistedFillAt.Should().NotBeNull();
            replayVerification.LastPersistedOrderUpdateAt.Should().NotBeNull();
            replayVerification.VerificationAuditId.Should().NotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public async Task Scenario_SessionCloseReplayAndPromotionReview_BacktestToPaperFlowRemainsContinuousAndAuditable()
    {
        using var artifacts = TestArtifactDirectory.Create(nameof(Scenario_SessionCloseReplayAndPromotionReview_BacktestToPaperFlowRemainsContinuousAndAuditable));
        await using var app = await CreateAppAsync(services =>
        {
            RegisterSessionServices(services, artifacts.RootPath);
            RegisterPromotionServices(services);
        }, currentUser: "ops-promoter");

        var client = app.GetTestClient();

        var createSessionResponse = await client.PostAsync(
            UiApiRoutes.ExecutionSessionCreate,
            JsonContent(new CreatePaperSessionRequest(
                StrategyId: "strat-wave2",
                StrategyName: "Wave2 Continuity",
                InitialCash: 100_000m,
                Symbols: ["AAPL"])));
        createSessionResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var session = await ReadAsync<ExecutionServices.PaperSessionSummaryDto>(createSessionResponse);

        var persistence = app.Services.GetRequiredService<ExecutionServices.PaperSessionPersistenceService>();
        await persistence.RecordFillAsync(session.SessionId, CreateFill("AAPL", quantity: 10m, fillPrice: 101m));

        var replayResponse = await client.GetAsync(
            UiApiRoutes.ExecutionSessionReplay.Replace("{sessionId}", session.SessionId, StringComparison.Ordinal));
        replayResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var replay = await ReadAsync<ExecutionServices.PaperSessionReplayVerificationDto>(replayResponse);

        var evaluateResponse = await client.GetAsync("/api/promotion/evaluate/run-backtest-01");
        evaluateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var evaluation = await ReadAsync<PromotionEvaluationResult>(evaluateResponse);

        var approveResponse = await client.PostAsync(
            "/api/promotion/approve",
            JsonContent(new PromotionApprovalRequest(
                RunId: "run-backtest-01",
                ReviewNotes: "Replay is consistent with durable fill log.",
                ApprovedBy: "ops-promoter",
                ApprovalReason: "Replay source and session continuity verified.",
                ApprovalChecklist: PromotionApprovalChecklist.CreateRequiredFor(RunType.Paper),
                EvidenceReferences: CreatePromotionEvidenceReferences(RunType.Paper))));
        approveResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var approval = await ReadAsync<PromotionDecisionResult>(approveResponse);

        var historyResponse = await client.GetAsync("/api/promotion/history");
        historyResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var history = await ReadAsync<StrategyPromotionRecord[]>(historyResponse);

        var auditResponse = await client.GetAsync(UiApiRoutes.ExecutionAudit);
        auditResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var audits = await ReadAsync<ExecutionServices.ExecutionAuditEntry[]>(auditResponse);

        using (new AssertionScope())
        {
            replay.IsConsistent.Should().BeTrue();
            replay.ReplaySource.Should().Be("DurableFillLog");
            replay.MismatchReasons.Should().BeEmpty();
            replay.ReplayPortfolio.Cash.Should().Be(98_990m);
            replay.ComparedFillCount.Should().Be(1);
            replay.VerificationAuditId.Should().NotBeNullOrWhiteSpace();

            evaluation.IsEligible.Should().BeTrue();
            evaluation.SourceMode.Should().Be(RunType.Backtest);
            evaluation.TargetMode.Should().Be(RunType.Paper);
            evaluation.RequiresHumanApproval.Should().BeFalse();

            approval.Success.Should().BeTrue();
            approval.NewRunId.Should().NotBeNullOrWhiteSpace();
            approval.ApprovedBy.Should().Be("ops-promoter");
            approval.PromotionId.Should().NotBeNullOrWhiteSpace();

            history.Should().ContainSingle(record =>
                record.StrategyId == "strat-wave2" &&
                record.SourceRunType == RunType.Backtest &&
                record.TargetRunType == RunType.Paper &&
                record.ApprovedBy == "ops-promoter" &&
                record.ApprovalChecklist != null &&
                record.ApprovalChecklist.Length > 0);

            audits.Should().Contain(entry =>
                entry.Action == "ReplayPaperSession" &&
                entry.Actor == "ops-promoter" &&
                entry.Outcome == "Completed");

            audits.Should().Contain(entry =>
                entry.Action == "PromotionApproved" &&
                entry.Actor == "ops-promoter" &&
                entry.RunId == "run-backtest-01" &&
                entry.Outcome == "Approved" &&
                entry.CorrelationId == approval.PromotionId &&
                entry.Message == "Replay source and session continuity verified." &&
                entry.Metadata != null &&
                entry.Metadata["sourceRunType"] == nameof(RunType.Backtest) &&
                entry.Metadata["targetRunType"] == nameof(RunType.Paper) &&
                entry.Metadata["targetRunId"] == approval.NewRunId &&
                entry.Metadata["auditReference"] == approval.AuditReference &&
                entry.Metadata["approvalChecklist"].Contains(PromotionApprovalChecklist.Dk1TrustPacketReviewed, StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task Scenario_RiskTriggeredPromotionRejection_DecisionRemainsVisibleWithBlockingRationale()
    {
        using var artifacts = TestArtifactDirectory.Create(nameof(Scenario_RiskTriggeredPromotionRejection_DecisionRemainsVisibleWithBlockingRationale));
        await using var app = await CreateAppAsync(services =>
        {
            RegisterSessionServices(services, artifacts.RootPath);
            RegisterPromotionServices(services, runId: "run-backtest-risk-blocked", sharpeRatio: 0.12d, maxDrawdownPercent: 0.42m, totalReturn: -0.08m);
        }, currentUser: "ops-risk");

        var client = app.GetTestClient();

        var evaluateResponse = await client.GetAsync("/api/promotion/evaluate/run-backtest-risk-blocked");
        evaluateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var evaluation = await ReadAsync<PromotionEvaluationResult>(evaluateResponse);

        var rejectResponse = await client.PostAsync(
            "/api/promotion/reject",
            JsonContent(new PromotionRejectionRequest(
                RunId: "run-backtest-risk-blocked",
                Reason: "Max drawdown exceeded cockpit guardrail and return is negative.")));
        rejectResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var rejection = await ReadAsync<PromotionDecisionResult>(rejectResponse);

        var historyResponse = await client.GetAsync("/api/promotion/history");
        historyResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var history = await ReadAsync<StrategyPromotionRecord[]>(historyResponse);

        using (new AssertionScope())
        {
            evaluation.IsEligible.Should().BeFalse();
            evaluation.Ready.Should().BeTrue();
            evaluation.TargetMode.Should().Be(RunType.Paper);
            evaluation.Reason.Should().NotBeNullOrWhiteSpace();
            evaluation.BlockingReasons.Should().NotBeNull();
            evaluation.BlockingReasons!.Should().Contain(reason =>
                reason.Contains("Sharpe", StringComparison.OrdinalIgnoreCase) ||
                reason.Contains("drawdown", StringComparison.OrdinalIgnoreCase) ||
                reason.Contains("return", StringComparison.OrdinalIgnoreCase));

            rejection.Success.Should().BeTrue();
            rejection.NewRunId.Should().BeNull();
            rejection.Reason.Should().Contain("Promotion rejected");
            rejection.Reason.Should().ContainEquivalentOf("drawdown");
            rejection.AuditReference.Should().NotBeNullOrWhiteSpace();
            rejection.ApprovedBy.Should().Be("ops-risk");

            history.Should().ContainSingle(record =>
                record.Decision == PromotionDecisionKinds.Rejected &&
                record.ApprovedBy == "ops-risk" &&
                record.ApprovalReason == "Max drawdown exceeded cockpit guardrail and return is negative." &&
                !string.IsNullOrWhiteSpace(record.AuditReference));
        }
    }

    [Fact]
    public async Task PromotionApprove_WhenUserLacksManageStrategies_ReturnsForbidden()
    {
        await using var app = await CreateAppAsync(services => RegisterPromotionServices(services), UserPermission.ViewStrategies);
        var client = app.GetTestClient();

        var approveResponse = await client.PostAsync(
            "/api/promotion/approve",
            JsonContent(new PromotionApprovalRequest(
                RunId: "run-backtest-01",
                ApprovedBy: "forged-actor",
                ApprovalReason: "Attempted unauthorized approval.",
                ApprovalChecklist: PromotionApprovalChecklist.CreateRequiredFor(RunType.Paper))));

        approveResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PromotionApprove_WhenActorMissing_ReturnsUnauthorized()
    {
        await using var app = await CreateAppAsync(
            services => RegisterPromotionServices(services),
            currentUserPermissions: UserPermission.ManageStrategies,
            currentUser: null);
        var client = app.GetTestClient();

        var approveResponse = await client.PostAsync(
            "/api/promotion/approve",
            JsonContent(new PromotionApprovalRequest(
                RunId: "run-backtest-01",
                ApprovedBy: "forged-actor",
                ApprovalReason: "Attempted approval without a trusted session actor.",
                ApprovalChecklist: PromotionApprovalChecklist.CreateRequiredFor(RunType.Paper))));

        approveResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PromotionEvaluate_WhenUserLacksStrategyReadPermission_ReturnsForbidden()
    {
        await using var app = await CreateAppAsync(
            services => RegisterPromotionServices(services),
            currentUserPermissions: UserPermission.ViewTrades);
        var client = app.GetTestClient();

        var evaluateResponse = await client.GetAsync("/api/promotion/evaluate/run-backtest-01");
        var historyResponse = await client.GetAsync("/api/promotion/history");

        evaluateResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        historyResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PromotionEndpoints_ExposeTenantScopeMetadataOnEveryRoute()
    {
        await using var app = await CreateAppAsync(services => RegisterPromotionServices(services));
        var promotionEndpoints = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(static source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(static endpoint => endpoint.RoutePattern.RawText?.StartsWith("/api/promotion", StringComparison.Ordinal) == true)
            .ToArray();

        promotionEndpoints.Should().HaveCount(5);
        promotionEndpoints.Should().OnlyContain(endpoint =>
            endpoint.Metadata.GetMetadata<WorkstationTenantScopeMetadata>() != null);
    }

    [Fact]
    public async Task PromotionEndpoints_WithoutTenantAndCompanyScope_ReturnForbidden()
    {
        await using var app = await CreateAppAsync(
            services => RegisterPromotionServices(services),
            currentTenantId: null,
            currentCompanyId: null);
        var client = app.GetTestClient();

        var evaluate = await client.GetAsync("/api/promotion/evaluate/run-backtest-01");
        var approve = await client.PostAsync(
            "/api/promotion/approve",
            JsonContent(new PromotionApprovalRequest("run-backtest-01")));
        var reject = await client.PostAsync(
            "/api/promotion/reject",
            JsonContent(new PromotionRejectionRequest("run-backtest-01", "Not approved.")));
        var history = await client.GetAsync("/api/promotion/history");
        var walkForward = await client.PostAsync(
            "/api/promotion/runs/run-backtest-01/walk-forward-evidence",
            JsonContent(new RecordWalkForwardEvidenceRequest(1.0d, 0.05m, 0.8d, 4)));

        evaluate.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        approve.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        reject.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        history.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        walkForward.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PromotionEndpoints_WithForeignScope_FailClosedWithoutMutation()
    {
        await using var app = await CreateAppAsync(
            services => RegisterPromotionServices(
                services,
                runTenantId: "owner-tenant",
                runCompanyId: "owner-company"),
            currentTenantId: "foreign-tenant",
            currentCompanyId: "foreign-company");
        var client = app.GetTestClient();

        var evaluate = await client.GetAsync("/api/promotion/evaluate/run-backtest-01");
        var approve = await client.PostAsync(
            "/api/promotion/approve",
            JsonContent(new PromotionApprovalRequest(
                "run-backtest-01",
                ApprovedBy: "forged-owner",
                ApprovalReason: "Attempted foreign approval.",
                ApprovalChecklist: PromotionApprovalChecklist.CreateRequiredFor(RunType.Paper),
                EvidenceReferences: CreatePromotionEvidenceReferences(RunType.Paper))));
        var walkForward = await client.PostAsync(
            "/api/promotion/runs/run-backtest-01/walk-forward-evidence",
            JsonContent(new RecordWalkForwardEvidenceRequest(1.0d, 0.05m, 0.8d, 4)));
        var history = await client.GetAsync("/api/promotion/history");
        var historyRecords = await ReadAsync<StrategyPromotionRecord[]>(history);
        var retainedRun = await app.Services
            .GetRequiredService<StrategyRunStore>()
            .GetRunByIdAsync("run-backtest-01");

        evaluate.StatusCode.Should().Be(HttpStatusCode.NotFound);
        approve.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        walkForward.StatusCode.Should().Be(HttpStatusCode.NotFound);
        history.StatusCode.Should().Be(HttpStatusCode.OK);
        historyRecords.Should().BeEmpty();
        retainedRun!.WalkForwardEvidence.Should().BeNull();
    }

    // ------------------------------------------------------------------ //
    //  Helpers                                                            //
    // ------------------------------------------------------------------ //

    private static void RegisterMinimalOms(IServiceCollection services) =>
        RegisterMinimalOms(services, Array.Empty<ExecutionPosition>());

    private static void RegisterMinimalOms(IServiceCollection services, params ExecutionPosition[] positions)
    {
        services.AddSingleton<IExecutionGateway, PaperTradingGateway>();
        services.AddSingleton<IOrderManager>(sp =>
            new OrderManagementSystem(
                sp.GetRequiredService<IExecutionGateway>(),
                NullLogger<OrderManagementSystem>.Instance));
        services.AddSingleton<Meridian.Execution.Models.IPortfolioState>(new StaticPortfolioState(positions));
    }


    private static void RegisterFundAccountScope(IServiceCollection services, Guid fundAccountId, bool isAllowed)
    {
        services.AddSingleton<IAccountQueryService>(new StubAccountQueryService(fundAccountId));
        services.AddSingleton<IScopedAuthorizationService>(new StubScopedAuthorizationService(fundAccountId, isAllowed));
    }

    private static void RegisterBrokerageOms(IServiceCollection services, RecordingBrokerageGateway gateway)
    {
        services.AddSingleton(gateway);
        services.AddSingleton<IExecutionGateway>(sp => sp.GetRequiredService<RecordingBrokerageGateway>());
        services.AddSingleton(CreateEnabledBrokerageConfiguration(gateway.GatewayId));
        services.AddSingleton<ILiveOrderReadinessGate>(_ => new ApprovedLiveOrderReadinessGate(ApprovedLiveRunId));
        services.AddSingleton<Meridian.Execution.Models.IPortfolioState>(new StaticPortfolioState());
        services.AddSingleton<IOrderManager>(sp =>
            new OrderManagementSystem(
                sp.GetRequiredService<IExecutionGateway>(),
                NullLogger<OrderManagementSystem>.Instance,
                brokerageConfiguration: sp.GetService<BrokerageConfiguration>(),
                liveOrderReadinessGate: sp.GetService<ILiveOrderReadinessGate>()));
    }

    private static void RegisterBrokerageOmsWithoutConfiguration(
        IServiceCollection services,
        RecordingBrokerageGateway gateway)
    {
        services.AddSingleton(gateway);
        services.AddSingleton<IExecutionGateway>(sp => sp.GetRequiredService<RecordingBrokerageGateway>());
        services.AddSingleton<Meridian.Execution.Models.IPortfolioState>(new StaticPortfolioState());
        services.AddSingleton<IOrderManager>(sp =>
            new OrderManagementSystem(
                sp.GetRequiredService<IExecutionGateway>(),
                NullLogger<OrderManagementSystem>.Instance,
                brokerageConfiguration: null));
    }

    private static BrokerageConfiguration CreateEnabledBrokerageConfiguration(string gatewayId) =>
        new()
        {
            Gateway = gatewayId,
            LiveExecutionEnabled = true,
            ReadOnlyVerificationPassed = true,
            PaperLifecycleTestsPassed = true,
            ReplayEvidencePassed = true,
            ProductionRoutingPhaseEnabled = true,
            ValidationGates = new BrokerValidationGateOptions
            {
                RequireValidationArtifactsForOrderPlacement = false
            },
            BrokerFlows = new Dictionary<string, BrokerFlowFlags>(StringComparer.OrdinalIgnoreCase)
            {
                [gatewayId] = new()
                {
                    ReadOnlyDataEnabled = true,
                    PaperOrderFlowEnabled = true,
                    ProductionOrderRoutingEnabled = true
                }
            }
        };

    private static void RegisterSessionServices(IServiceCollection services, string rootPath)
    {
        services.AddSingleton(_ => new ExecutionServices.ExecutionAuditTrailService(
            new ExecutionServices.ExecutionAuditTrailOptions(Path.Combine(rootPath, "audit")),
            NullLogger<ExecutionServices.ExecutionAuditTrailService>.Instance));
        services.AddSingleton<ExecutionServices.IPaperSessionStore>(_ => new ExecutionServices.JsonlFilePaperSessionStore(
            Path.Combine(rootPath, "sessions"),
            NullLogger<ExecutionServices.JsonlFilePaperSessionStore>.Instance));
        services.AddSingleton<ExecutionServices.PaperSessionPersistenceService>(sp => new ExecutionServices.PaperSessionPersistenceService(
            NullLogger<ExecutionServices.PaperSessionPersistenceService>.Instance,
            sp.GetRequiredService<ExecutionServices.IPaperSessionStore>(),
            sp.GetRequiredService<ExecutionServices.ExecutionAuditTrailService>()));
    }

    private static void RegisterPromotionServices(
        IServiceCollection services,
        string runId = "run-backtest-01",
        double sharpeRatio = 1.20d,
        decimal maxDrawdownPercent = 0.08m,
        decimal totalReturn = 0.16m,
        string runTenantId = "execution-test-tenant",
        string runCompanyId = "execution-test-company")
    {
        var strategyRepository = new StrategyRunStore();
        strategyRepository
            .RecordRunAsync(CreateCompletedBacktestRun(
                runId: runId,
                strategyId: "strat-wave2",
                strategyName: "Wave2 Continuity",
                sharpeRatio: sharpeRatio,
                maxDrawdownPercent: maxDrawdownPercent,
                totalReturn: totalReturn,
                tenantId: runTenantId,
                companyId: runCompanyId))
            .GetAwaiter()
            .GetResult();

        services.AddSingleton(strategyRepository);
        services.AddSingleton<BacktestToLivePromoter>();
        var promotionArtifactRoot = Path.Combine(
            Path.GetTempPath(),
            "meridian-tests",
            "execution-write",
            Guid.NewGuid().ToString("N"));
        services.AddSingleton<IPromotionRecordStore>(_ => new JsonlPromotionRecordStore(
            Path.Combine(promotionArtifactRoot, "promotions"),
            NullLogger<JsonlPromotionRecordStore>.Instance));
        services.AddSingleton(_ => new ExecutionServices.ExecutionAuditTrailService(
            new ExecutionServices.ExecutionAuditTrailOptions(Path.Combine(promotionArtifactRoot, "audit")),
            NullLogger<ExecutionServices.ExecutionAuditTrailService>.Instance));
        services.AddSingleton<PromotionService>(sp => new PromotionService(
            sp.GetRequiredService<StrategyRunStore>(),
            sp.GetRequiredService<BacktestToLivePromoter>(),
            sp.GetRequiredService<IPromotionRecordStore>(),
            NullLogger<PromotionService>.Instance,
            auditTrail: sp.GetRequiredService<ExecutionServices.ExecutionAuditTrailService>()));
    }

    private static async Task<WebApplication> CreateAppAsync(
        Action<IServiceCollection>? configureServices = null,
        UserPermission currentUserPermissions = UserPermission.ViewTrades | UserPermission.ExecuteTrades | UserPermission.ManageOrders | UserPermission.ManageStrategies,
        string? currentUser = "ops-user",
        IReadOnlyCollection<Guid>? allowedAccountScopes = null,
        string? currentTenantId = "execution-test-tenant",
        string? currentCompanyId = "execution-test-company")
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseTestServer();
        configureServices?.Invoke(builder.Services);
        if (allowedAccountScopes is not null)
        {
            builder.Services.AddSingleton<IScopedAuthorizationService>(
                new TestScopedAuthorizationService(allowedAccountScopes));
        }

        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            if (!string.IsNullOrWhiteSpace(currentUser))
            {
                context.Items[LoginSessionMiddleware.CurrentUserKey] = currentUser;
            }

            context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = currentUserPermissions;
            if (!string.IsNullOrWhiteSpace(currentCompanyId))
            {
                context.Items[LoginSessionMiddleware.CurrentUserCompanyIdKey] = currentCompanyId;
            }

            if (!string.IsNullOrWhiteSpace(currentTenantId))
            {
                context.Items[LoginSessionMiddleware.CurrentTenantIdKey] = currentTenantId;
            }
            await next();
        });

        app.MapExecutionEndpoints(new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        app.MapPromotionEndpoints(new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        await app.StartAsync();
        return app;
    }

    private sealed class TestScopedAuthorizationService : IScopedAuthorizationService
    {
        private readonly HashSet<Guid> _allowedAccountScopes;

        public TestScopedAuthorizationService(IReadOnlyCollection<Guid> allowedAccountScopes)
        {
            _allowedAccountScopes = allowedAccountScopes.ToHashSet();
        }

        public Task<ScopedAuthorizationDecisionDto> AuthorizeAsync(
            string actor,
            UserPermission requiredPermission,
            AccessScopeKindDto scopeKind,
            Guid? scopeId,
            UserPermission globalPermissions,
            CancellationToken ct = default)
        {
            var allowed = scopeKind == AccessScopeKindDto.Account
                && scopeId.HasValue
                && _allowedAccountScopes.Contains(scopeId.Value)
                && (globalPermissions & requiredPermission) == requiredPermission;

            return Task.FromResult(new ScopedAuthorizationDecisionDto(
                allowed,
                actor,
                requiredPermission,
                scopeKind,
                scopeId,
                allowed ? "Test account scope grants access." : "Test account scope denies access."));
        }
    }

    private static async Task<TradingActionResult> ReadActionResultAsync(HttpResponseMessage response) =>
        await ReadAsync<TradingActionResult>(response);

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        var opts = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };
        var result = JsonSerializer.Deserialize<T>(json, opts);
        result.Should().NotBeNull($"expected a {typeof(T).Name} in response body, but got: {json}");
        return result!;
    }

    private static StringContent JsonContent(object payload) =>
        new(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

    private static ExecutionReport CreateFill(string symbol, decimal quantity, decimal fillPrice) => new()
    {
        OrderId = $"fill-{Guid.NewGuid():N}",
        ReportType = ExecutionReportType.Fill,
        Symbol = symbol,
        Side = OrderSide.Buy,
        OrderStatus = Meridian.Execution.Sdk.OrderStatus.Filled,
        OrderQuantity = quantity,
        FilledQuantity = quantity,
        FillPrice = fillPrice,
        Timestamp = DateTimeOffset.UtcNow
    };

    private static OrderState CreateOrderState(string orderId, string symbol, decimal quantity) => new()
    {
        OrderId = orderId,
        Symbol = symbol,
        Side = OrderSide.Buy,
        Type = Meridian.Execution.Sdk.OrderType.Market,
        Quantity = quantity,
        Status = Meridian.Execution.Sdk.OrderStatus.Accepted,
        CreatedAt = DateTimeOffset.UtcNow,
        LastUpdatedAt = DateTimeOffset.UtcNow
    };

    private static Meridian.Strategies.Models.StrategyRunEntry CreateCompletedBacktestRun(
        string runId,
        string strategyId,
        string strategyName,
        double sharpeRatio,
        decimal maxDrawdownPercent,
        decimal totalReturn,
        string tenantId,
        string companyId)
    {
        var now = DateTimeOffset.UtcNow;
        var request = new BacktestRequest(
            From: DateOnly.FromDateTime(now.AddDays(-10).UtcDateTime),
            To: DateOnly.FromDateTime(now.AddDays(-1).UtcDateTime),
            Symbols: ["AAPL"],
            InitialCash: 100_000m,
            DataRoot: "./data");

        var snapshot = new PortfolioSnapshot(
            Timestamp: now.AddDays(-1),
            Date: DateOnly.FromDateTime(now.AddDays(-1).UtcDateTime),
            Cash: 108_000m,
            MarginBalance: 0m,
            LongMarketValue: 8_000m,
            ShortMarketValue: 0m,
            TotalEquity: 116_000m,
            DailyReturn: 0.01m,
            Positions: new Dictionary<string, Position>(),
            Accounts: new Dictionary<string, FinancialAccountSnapshot>(),
            DayCashFlows: []);

        var metrics = new BacktestMetrics(
            InitialCapital: 100_000m,
            FinalEquity: 116_000m,
            GrossPnl: 16_000m,
            NetPnl: 15_200m,
            TotalReturn: totalReturn,
            AnnualizedReturn: 0.20m,
            SharpeRatio: sharpeRatio,
            SortinoRatio: 1.8d,
            CalmarRatio: 1.1d,
            MaxDrawdown: 8_400m,
            MaxDrawdownPercent: maxDrawdownPercent,
            MaxDrawdownRecoveryDays: 7,
            ProfitFactor: 1.7d,
            WinRate: 0.58d,
            TotalTrades: 32,
            WinningTrades: 18,
            LosingTrades: 14,
            TotalCommissions: 750m,
            TotalMarginInterest: 0m,
            TotalShortRebates: 0m,
            Xirr: 0.14d,
            SymbolAttribution: new Dictionary<string, SymbolAttribution>());

        var result = new BacktestResult(
            Request: request,
            Universe: new HashSet<string>(["AAPL"], StringComparer.OrdinalIgnoreCase),
            Snapshots: [snapshot],
            CashFlows: [],
            Fills: [],
            Metrics: metrics,
            Ledger: new global::Meridian.Ledger.Ledger(),
            ElapsedTime: TimeSpan.FromMinutes(7),
            TotalEventsProcessed: 1_200);

        return new Meridian.Strategies.Models.StrategyRunEntry(
            RunId: runId,
            StrategyId: strategyId,
            StrategyName: strategyName,
            RunType: RunType.Backtest,
            StartedAt: now.AddDays(-10),
            EndedAt: now.AddDays(-1),
            Metrics: result,
            PortfolioId: $"{strategyId}-backtest-portfolio",
            LedgerReference: $"{strategyId}-backtest-ledger",
            Engine: "MeridianNative",
            ParameterSet: new Dictionary<string, string>
            {
                ["workstationTenantId"] = tenantId,
                ["workstationCompanyId"] = companyId
            },
            RetainedEvidenceReferences: CreatePromotionRetainedEvidenceReferences(RunType.Paper));
    }

    private static string[] CreatePromotionEvidenceReferences(RunType targetRunType) =>
        PromotionApprovalChecklist
            .CreateRequiredFor(targetRunType)
            .Select(static item => $"{item}:evidence://evidence-vault/{item.ToLowerInvariant()}")
            .ToArray();

    private static string[] CreatePromotionRetainedEvidenceReferences(RunType targetRunType) =>
        CreatePromotionEvidenceReferences(targetRunType)
            .Select(static reference => reference[(reference.IndexOf(':') + 1)..])
            .ToArray();

    private static BrokerPosition CreateRobinhoodOptionPosition(
        string positionId,
        DateOnly? expiration = null,
        decimal strike = 180m) =>
        new()
        {
            PositionId = positionId,
            Symbol = "AAPL",
            UnderlyingSymbol = "AAPL",
            Description = $"AAPL {(expiration ?? new DateOnly(2026, 5, 15)):yyyy-MM-dd} {strike}C",
            Quantity = 1m,
            AverageEntryPrice = 2.10m,
            MarketPrice = 2.45m,
            MarketValue = 245m,
            UnrealizedPnl = 35m,
            AssetClass = "option",
            Expiration = expiration ?? new DateOnly(2026, 5, 15),
            Strike = strike,
            Right = "call",
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["asset_class"] = "option",
                ["option_instrument_url"] = $"https://api.robinhood.com/options/instruments/{positionId}/",
                ["underlying_symbol"] = "AAPL",
                ["right"] = "call",
                ["expiration"] = (expiration ?? new DateOnly(2026, 5, 15)).ToString("yyyy-MM-dd"),
                ["strike"] = strike.ToString("G29"),
                ["runId"] = ApprovedLiveRunId
            }
        };
}

file sealed class ApprovedLiveOrderReadinessGate(string approvedRunId) : ILiveOrderReadinessGate
{
    public Task<LiveOrderReadinessDecision> EvaluateAsync(
        LiveOrderReadinessRequest request,
        CancellationToken ct = default)
    {
        var decision = string.Equals(request.RunId, approvedRunId, StringComparison.Ordinal)
            ? LiveOrderReadinessDecision.Approved($"audit://live/{request.RunId}")
            : LiveOrderReadinessDecision.Rejected($"Run {request.RunId} is not approved for live order routing.");

        return Task.FromResult(decision);
    }
}

file sealed class StaticPortfolioState(params ExecutionPosition[] positions) : Meridian.Execution.Models.IPortfolioState
{
    public decimal Cash => 100_000m;
    public decimal PortfolioValue => 100_000m;
    public decimal UnrealisedPnl { get; } = positions.Sum(position => position.UnrealisedPnl);
    public decimal RealisedPnl { get; } = positions.Sum(position => position.RealisedPnl);
    public IReadOnlyDictionary<string, Meridian.Execution.Sdk.IPosition> Positions { get; } = positions.ToDictionary(
        position => position.Symbol,
        position => (Meridian.Execution.Sdk.IPosition)position,
        StringComparer.OrdinalIgnoreCase);
}

file sealed class RecordingOrderManager(params OrderState[] openOrders) : IOrderManager
{
    private readonly IReadOnlyList<OrderState> _openOrders = openOrders;

    public List<string> CancelledOrderIds { get; } = new();
    public int CancelAllCallCount { get; private set; }

    public Task<OrderResult> PlaceOrderAsync(ExecutionOrderRequest request, CancellationToken ct = default) =>
        Task.FromResult(new OrderResult { Success = true, OrderId = request.ClientOrderId ?? "recorded-order" });

    public Task<OrderResult> CancelOrderAsync(string orderId, CancellationToken ct = default)
    {
        CancelledOrderIds.Add(orderId);
        return Task.FromResult(new OrderResult { Success = true, OrderId = orderId });
    }

    public Task<OrderResult> ModifyOrderAsync(string orderId, OrderModification modification, CancellationToken ct = default) =>
        Task.FromResult(new OrderResult { Success = true, OrderId = orderId });

    public IReadOnlyList<OrderState> GetOpenOrders() => _openOrders;

    public OrderState? GetOrder(string orderId) =>
        _openOrders.FirstOrDefault(order => string.Equals(order.OrderId, orderId, StringComparison.OrdinalIgnoreCase));

    /// <summary>Order ids this double refuses to cancel, standing in for a broker that says no.</summary>
    public HashSet<string> RefuseToCancel { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Task<KillSwitchSweepResult> CancelAllAsync(CancellationToken ct = default)
    {
        CancelAllCallCount++;
        var failures = new List<KillSwitchSweepFailure>();
        var cancelled = 0;
        foreach (var order in _openOrders)
        {
            if (RefuseToCancel.Contains(order.OrderId))
            {
                failures.Add(new KillSwitchSweepFailure(order.OrderId, order.Symbol, "Broker refused the cancellation."));
                continue;
            }

            CancelledOrderIds.Add(order.OrderId);
            cancelled++;
        }

        return Task.FromResult(KillSwitchSweepResult.From(_openOrders.Count, cancelled, failures));
    }

    public IReadOnlyList<OrderState> GetCompletedOrders(int take = 20) => Array.Empty<OrderState>();
}

sealed class RecordingBrokerageGateway(params BrokerPosition[] positions) : IBrokerageGateway
{
    private readonly IReadOnlyList<BrokerPosition> _positions = positions;

    public List<ExecutionOrderRequest> SubmittedRequests { get; } = new();

    public string GatewayId => "robinhood";
    public string BrokerDisplayName => "Robinhood (test)";
    public bool IsConnected { get; private set; }
    public BrokerageCapabilities BrokerageCapabilities { get; } = BrokerageCapabilities.UsEquity();

    public Task ConnectAsync(CancellationToken ct = default)
    {
        IsConnected = true;
        return Task.CompletedTask;
    }

    public Task DisconnectAsync(CancellationToken ct = default)
    {
        IsConnected = false;
        return Task.CompletedTask;
    }

    public Task<ExecutionReport> SubmitOrderAsync(ExecutionOrderRequest request, CancellationToken ct = default)
    {
        SubmittedRequests.Add(request);

        return Task.FromResult(new ExecutionReport
        {
            OrderId = request.ClientOrderId ?? $"test-{SubmittedRequests.Count}",
            ClientOrderId = request.ClientOrderId,
            GatewayOrderId = $"gw-{SubmittedRequests.Count}",
            ReportType = ExecutionReportType.Fill,
            Symbol = request.Symbol,
            Side = request.Side,
            OrderStatus = Meridian.Execution.Sdk.OrderStatus.Filled,
            OrderQuantity = request.Quantity,
            FilledQuantity = request.Quantity,
            FillPrice = 1m,
            Timestamp = DateTimeOffset.UtcNow
        });
    }

    public Task<ExecutionReport> CancelOrderAsync(string orderId, CancellationToken ct = default) =>
        Task.FromResult(new ExecutionReport
        {
            OrderId = orderId,
            ReportType = ExecutionReportType.Cancelled,
            Symbol = string.Empty,
            Side = OrderSide.Buy,
            OrderStatus = Meridian.Execution.Sdk.OrderStatus.Cancelled,
            Timestamp = DateTimeOffset.UtcNow
        });

    public Task<ExecutionReport> ModifyOrderAsync(string orderId, OrderModification modification, CancellationToken ct = default) =>
        Task.FromResult(new ExecutionReport
        {
            OrderId = orderId,
            ReportType = ExecutionReportType.Modified,
            Symbol = string.Empty,
            Side = OrderSide.Buy,
            OrderStatus = Meridian.Execution.Sdk.OrderStatus.Accepted,
            Timestamp = DateTimeOffset.UtcNow
        });

    public async IAsyncEnumerable<ExecutionReport> StreamExecutionReportsAsync([EnumeratorCancellation] CancellationToken ct = default)
    {
        await Task.CompletedTask;
        yield break;
    }

    public Task<AccountInfo> GetAccountInfoAsync(CancellationToken ct = default) =>
        Task.FromResult(new AccountInfo
        {
            AccountId = "acct-1",
            Cash = 100_000m,
            Equity = 100_000m,
            BuyingPower = 100_000m
        });

    public Task<IReadOnlyList<BrokerPosition>> GetPositionsAsync(CancellationToken ct = default) =>
        Task.FromResult(_positions);

    public Task<IReadOnlyList<BrokerOrder>> GetOpenOrdersAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<BrokerOrder>>(Array.Empty<BrokerOrder>());

    public Task<BrokerHealthStatus> CheckHealthAsync(CancellationToken ct = default) =>
        Task.FromResult(BrokerHealthStatus.Healthy("ok"));

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}


sealed class StubScopedAuthorizationService(Guid allowedAccountId, bool isAllowed) : IScopedAuthorizationService
{
    public Task<ScopedAuthorizationDecisionDto> AuthorizeAsync(
        string actor,
        UserPermission requiredPermission,
        AccessScopeKindDto scopeKind,
        Guid? scopeId,
        UserPermission globalPermissions,
        CancellationToken ct = default)
    {
        var allowed = isAllowed
            && scopeKind == AccessScopeKindDto.Account
            && scopeId == allowedAccountId
            && globalPermissions.HasFlag(requiredPermission);

        return Task.FromResult(new ScopedAuthorizationDecisionDto(
            allowed,
            actor,
            requiredPermission,
            scopeKind,
            scopeId,
            allowed ? "Matched test account scope." : "No matching test account scope."));
    }
}

sealed class StubAccountQueryService(Guid accountId) : IAccountQueryService
{
    private readonly AccountSummaryDto _account = new(
        accountId,
        AccountTypeDto.Brokerage,
        EntityId: null,
        FundId: null,
        SleeveId: null,
        VehicleId: null,
        AccountCode: "TEST-BROKERAGE",
        DisplayName: "Test Brokerage Account",
        BaseCurrency: "USD",
        Institution: "Test Broker",
        IsActive: true,
        EffectiveFrom: DateTimeOffset.UtcNow,
        EffectiveTo: null,
        PortfolioId: null,
        LedgerReference: null,
        StrategyId: null,
        RunId: null);

    public Task<AccountSummaryDto?> GetAccountAsync(Guid requestedAccountId, CancellationToken ct = default) =>
        Task.FromResult(requestedAccountId == _account.AccountId ? _account : null);

    public Task<IReadOnlyList<AccountSummaryDto>> ListAccountsAsync(AccountTypeDto? accountType, bool? isActive, string? currency, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<AccountSummaryDto>>([_account]);

    public Task<FundAccountsDto> GetFundAccountsAsync(Guid fundId, CancellationToken ct = default) =>
        Task.FromResult(new FundAccountsDto(fundId, [], [], [_account], []));

    public Task<IReadOnlyList<AccountSettlementInstructionView>> ListSettlementInstructionsAsync(Guid? accountId = null, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<AccountSettlementInstructionView>>([]);

    public Task<IReadOnlyList<AccountBalanceSnapshotDto>> GetBalanceTimelineAsync(Guid accountId, DateOnly? fromDate = null, DateOnly? toDate = null, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<AccountBalanceSnapshotDto>>([]);

    public Task<AccountBalanceSnapshotDto?> GetLatestBalanceSnapshotAsync(Guid accountId, CancellationToken ct = default) =>
        Task.FromResult<AccountBalanceSnapshotDto?>(null);

    public Task<IReadOnlyList<AccountOpenBreakView>> ListOpenBreaksAsync(Guid? accountId = null, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<AccountOpenBreakView>>([]);

    public Task<IReadOnlyList<CustodianPositionLineDto>> GetCustodianPositionsAsync(Guid accountId, DateOnly asOfDate, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<CustodianPositionLineDto>>([]);

    public Task<IReadOnlyList<BankStatementLineDto>> GetBankStatementLinesAsync(Guid accountId, DateOnly? fromDate = null, DateOnly? toDate = null, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<BankStatementLineDto>>([]);

    public Task<IReadOnlyList<AccountReconciliationRunDto>> GetReconciliationRunsAsync(Guid accountId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<AccountReconciliationRunDto>>([]);

    public Task<IReadOnlyList<AccountReconciliationResultDto>> GetReconciliationResultsAsync(Guid reconciliationRunId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<AccountReconciliationResultDto>>([]);

    public Task<IReadOnlyList<AccountSyncHistoryEntryDto>> GetSyncHistoryAsync(Guid accountId, string? capability = null, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<AccountSyncHistoryEntryDto>>([]);

    public Task<AccountSyncHistoryEntryDto?> GetLatestSyncHistoryAsync(Guid accountId, string? capability = null, CancellationToken ct = default) =>
        Task.FromResult<AccountSyncHistoryEntryDto?>(null);

    public Task<AccountReadinessSnapshotDto?> GetReadinessAsync(Guid accountId, CancellationToken ct = default) =>
        Task.FromResult<AccountReadinessSnapshotDto?>(null);

    public Task<IReadOnlyList<MarginSnapshotDto>> GetMarginSnapshotsAsync(Guid accountId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<MarginSnapshotDto>>([]);

    public Task<MarginSnapshotDto?> GetLatestMarginSnapshotAsync(Guid accountId, CancellationToken ct = default) =>
        Task.FromResult<MarginSnapshotDto?>(null);
}
