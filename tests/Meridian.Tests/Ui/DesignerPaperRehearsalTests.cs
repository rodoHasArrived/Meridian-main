using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Meridian.Backtesting;
using Meridian.Backtesting.Engine;
using Meridian.Backtesting.Sdk;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Operations;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.Core.Serialization;
using Meridian.Domain.Events;
using Meridian.Execution;
using Meridian.Execution.Events;
using Meridian.Execution.Live;
using Meridian.Execution.Services;
using Meridian.Identity;
using Meridian.Identity.Auth;
using Meridian.Platform.Scheduling;
using Meridian.PortfolioRecords.Accounts;
using Meridian.Storage.Operations;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Live;
using Meridian.Strategies.Live.Designer;
using Meridian.Strategies.Models;
using Meridian.Strategies.Promotions;
using Meridian.Strategies.Services;
using Meridian.Strategies.Storage;
using Meridian.Ui.Shared.Endpoints;
using Meridian.Ui.Shared.Evidence;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;
using ExecutionSdk = Meridian.Execution.Sdk;
using LiveCache = Meridian.Execution.Adapters.LiveMarketDataCache;
using PaperOrderGateway = Meridian.Execution.Adapters.PaperTradingGateway;

namespace Meridian.Tests.Ui;

public sealed class DesignerPaperRehearsalTests
{
    private const string Tenant = "rehearsal-tenant";
    private const string Company = "rehearsal-company";
    private const string Symbol = "TESTX";
    private static readonly Guid AccountId = Guid.Parse("da6b82e6-2dd9-4733-8e77-a079e25be637");
    private static readonly StrategyRunReadScope ReadScope = new(Tenant, Company);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public async Task ScopedDesigner_ReplaysRealEngineAndRetainsReviewedSimulatedPaperEvidence()
    {
        var root = Directory.CreateTempSubdirectory("meridian-designer-rehearsal-");
        try
        {
            await RehearseAsync(root.FullName);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData(HistoricalPriceBasis.Unknown, false)]
    [InlineData(HistoricalPriceBasis.SplitAdjusted, null)]
    [InlineData(HistoricalPriceBasis.DividendAdjusted, null)]
    [InlineData(HistoricalPriceBasis.FullyAdjusted, null)]
    public async Task DesignerReplay_RequiresExplicitRawPricesBeforeStartingTheEngine(
        HistoricalPriceBasis priceBasis, bool? legacyAdjusted)
    {
        var root = Directory.CreateTempSubdirectory("meridian-designer-price-basis-");
        try
        {
            var from = new DateOnly(2024, 1, 2);
            await WriteBarsAsync(root.FullName, from, priceBasis, legacyAdjusted);
            var history = new FileOperationalCaseHistoryStore(root.FullName);
            var repository = new StrategyRunStore(history);
            var designService = new StrategyDesignService();
            var document = designService.GetTemplates().Single(item => item.TemplateId == "equity-price-filter")
                .Document with
            { Universe = [Symbol] };
            var evidence = new FileEvidenceArtifactStore(root.FullName, NullLogger<FileEvidenceArtifactStore>.Instance);
            var sourceEvidence = await evidence.WriteManifestAsync(
                new EvidencePacketDto(new EvidenceSubjectDto(document.DocumentId, "strategy", document.Name, "Strategy", null, "Strategy"),
                    DateTimeOffset.UtcNow, [], [],
                    new EvidenceCompletenessDto(100, EvidenceStatusDto.Ready, [], [], [], [], []), [], []),
                new EvidencePacketExportRequest("fixture-author", "Retained synthetic replay input")
                {
                    TenantId = Tenant,
                    Scope = Company
                });
            var reference = EvidenceVaultReference.CanonicalPrefix + sourceEvidence.VaultIdentity!.VaultId;
            var actions = Substitute.For<ISecurityCorporateActionsQueryService>();
            actions.GetCorporateActionsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IReadOnlyList<CorporateActionDto>>([]));
            var engineInvocations = 0;
            var service = new DesignerBacktestService(root.FullName, _ =>
            {
                engineInvocations++;
                throw new InvalidOperationException("Unverified raw data must be blocked before engine creation.");
            }, repository, evidence, designService, new TradingCalendar(), BuildSecurities(), actions);

            var run = () => service.RunAsync(document, new StrategyRunRepositoryScope(Tenant, Company),
                "fixture-operator", AccountId, from, from.AddDays(3),
                new StrategyRunEvidenceLoop(["Use explicitly raw historical prices."], [reference], [], [], [], []), default);

            await run.Should().ThrowAsync<DesignerBacktestBlockedException>().WithMessage("*unadjusted daily bar*");
            engineInvocations.Should().Be(0, "a legacy false adjustment flag cannot establish raw replay prices");
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    private static async Task RehearseAsync(string root)
    {
        var from = new DateOnly(2024, 1, 2);
        await WriteBarsAsync(root, from);
        var history = new FileOperationalCaseHistoryStore(root);
        var repository = new StrategyRunStore(history);
        var designs = new ScopedStrategyDesignStore(history);
        var designService = new StrategyDesignService();
        var template = designService.GetTemplates().Single(item => item.TemplateId == "equity-price-filter");
        var document = template.Document with { Universe = [Symbol] };
        var evidence = new FileEvidenceArtifactStore(root, NullLogger<FileEvidenceArtifactStore>.Instance);
        var sourceEvidence = await evidence.WriteManifestAsync(
            new EvidencePacketDto(new EvidenceSubjectDto(document.DocumentId, "strategy", document.Name, "Strategy", null, "Strategy"),
                DateTimeOffset.UtcNow, [], [],
                new EvidenceCompletenessDto(100, EvidenceStatusDto.Ready, [], [], [], [], []), [], []),
            new EvidencePacketExportRequest("fixture-author", "Retained synthetic replay input")
            {
                TenantId = Tenant,
                Scope = Company
            });
        var reference = EvidenceVaultReference.CanonicalPrefix + sourceEvidence.VaultIdentity!.VaultId;
        var reviews = new JsonlPromotionRecordStore(
            new PromotionRecordStoreOptions(Path.Combine(root, "reviews")), NullLogger<JsonlPromotionRecordStore>.Instance);
        var promoter = new BacktestToLivePromoter();
        var promotion = new PromotionService(repository, promoter, reviews, NullLogger<PromotionService>.Instance,
            scopedDesigns: designs);
        var securities = BuildSecurities();
        var actions = Substitute.For<ISecurityCorporateActionsQueryService>();
        actions.GetCorporateActionsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<CorporateActionDto>>([]));

        await using var audit = new ExecutionAuditTrailService(
            new ExecutionAuditTrailOptions(Path.Combine(root, "audit")), NullLogger<ExecutionAuditTrailService>.Instance);
        await using var sessions = new PaperSessionPersistenceService(
            NullLogger<PaperSessionPersistenceService>.Instance,
            new JsonlFilePaperSessionStore(Path.Combine(root, "paper"), NullLogger<JsonlFilePaperSessionStore>.Instance), audit,
            resolveAccountCurrency: static (id, _) => Task.FromResult<string?>(id == AccountId ? "USD" : null));
        var cache = new LiveCache();
        var hub = new LiveMarketEventHub();
        await using var gateway = new PaperTradingGateway(NullLogger<PaperTradingGateway>.Instance, liveFeed: cache);
        var portfolio = new PaperTradingPortfolio(100_000m);
        var canonicalPublisher = Substitute.For<ITradeEventPublisher>();
        await using var oms = new OrderManagementSystem(gateway, NullLogger<OrderManagementSystem>.Instance,
            portfolioState: portfolio, auditTrail: audit, sessionPersistence: sessions,
            tradeEventPublisher: canonicalPublisher, orderIntentHistory: history, options: new OrderManagementSystemOptions
            {
                PaperIntentScope = new PaperOrderIntentScope { TenantId = Tenant, CompanyId = Company, FundAccountId = AccountId }
            })
        {
            BindNativePaperSessions = true
        };
        await oms.StartAsync();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddMutationRateLimiter();
        builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
        builder.Services.AddSingleton<IWorkstationTenantContextAccessor, HttpContextWorkstationTenantContextAccessor>();
        builder.Services.AddSingleton<IOperationalCaseHistoryStore>(history);
        builder.Services.AddSingleton<IStrategyRepository>(repository);
        builder.Services.AddSingleton<IPromotionRecordStore>(reviews);
        builder.Services.AddSingleton<ISecurityIdentityQueryService>(securities);
        builder.Services.AddSingleton(evidence);
        builder.Services.AddSingleton<IEvidenceArtifactStore>(evidence);
        builder.Services.AddSingleton(audit);
        builder.Services.AddSingleton(sessions);
        builder.Services.AddSingleton<ExecutionSdk.IOrderManager>(oms);
        builder.Services.AddSingleton<IAccountQueryService>(BuildAccountQuery());
        builder.Services.AddSingleton<IScopedAuthorizationService, RehearsalAccountAuthorization>();
        builder.Services.AddSingleton(designs);
        builder.Services.AddSingleton(designService);
        builder.Services.AddSingleton(promotion);
        builder.Services.AddSingleton<PortfolioReadService>();
        builder.Services.AddSingleton<LedgerReadService>();
        builder.Services.AddSingleton<StrategyRunReadService>();
        builder.Services.AddSingleton<CashFlowProjectionService>();
        builder.Services.AddSingleton<ReconciliationProjectionService>();
        builder.Services.AddSingleton<IReconciliationRunRepository>(new FileReconciliationRunRepository(
            Path.Combine(root, "reconciliation"), NullLogger<FileReconciliationRunRepository>.Instance));
        builder.Services.AddSingleton<IReconciliationRunService, ReconciliationRunService>();
        builder.Services.AddSingleton<StrategyRunContinuityService>();
        builder.Services.AddSingleton<StrategyRunReviewPacketService>();
        builder.Services.AddEvidenceWorkflowFabric();
        builder.Services.AddMeridianBacktesting();
        builder.Services.AddSingleton(sp => new DesignerBacktestService(root,
            sp.GetRequiredService<Func<BacktestRequest, BacktestEngine>>(), repository, evidence, designService,
            new TradingCalendar(), securities, actions));

        await using var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.Items[LoginSessionMiddleware.CurrentUserKey] = context.Request.Headers.ContainsKey("Rehearsal-Report-Only")
                ? "rehearsal-reporter" : "rehearsal-operator";
            context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] =
                UserPermission.ManageStrategies | UserPermission.ViewStrategies | UserPermission.ManageOrders | UserPermission.ViewTrades |
                UserPermission.ViewReporting | UserPermission.ManageReporting;
            context.Items[LoginSessionMiddleware.CurrentTenantIdKey] = Tenant;
            context.Items[LoginSessionMiddleware.CurrentUserCompanyIdKey] =
                context.Request.Headers.ContainsKey("Rehearsal-Other-Company") ? "other-company" : Company;
            await next();
        });
        app.UseRateLimiter();
        app.MapWorkstationEndpoints(Json);
        app.MapExecutionEndpoints(Json);
        app.MapEvidenceEndpoints(Json);
        app.MapRiskEndpoints(Json);
        app.MapPromotionEndpoints(Json);
        await app.StartAsync();

        using var client = app.GetTestClient();
        const string designer = "/api/workstation/strategy/designer";
        var draftResponse = await client.PostAsJsonAsync($"{designer}/drafts", new StrategyDesignDraftSaveRequest(document), Json);
        await AssertSuccessAsync(draftResponse);
        var saved = (await draftResponse.Content.ReadFromJsonAsync<StrategyDesignDraftSaveResponse>(Json))!.Document;
        saved.Revision.Should().NotBeNullOrWhiteSpace();
        var reopened = await client.GetFromJsonAsync<StrategyDesignDocument>($"{designer}/drafts/{saved.DocumentId}", Json);
        reopened!.Revision.Should().Be(saved.Revision);
        (await client.PostAsJsonAsync($"{designer}/drafts", new StrategyDesignDraftSaveRequest(document), Json))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
        using var otherCompany = app.GetTestClient();
        otherCompany.DefaultRequestHeaders.Add("Rehearsal-Other-Company", "1");
        (await otherCompany.GetAsync($"{designer}/drafts/{saved.DocumentId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await otherCompany.GetAsync("/api/risk/rules")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var request = new StrategyDesignRunBacktestRequest(saved)
        {
            FundAccountId = AccountId,
            From = from,
            To = from.AddDays(3),
            OperatorAcceptanceCriteria = ["Buy ten shares at the next available bar after price exceeds 100."],
            RetainedEvidenceReferences = [reference]
        };
        (await client.PostAsJsonAsync($"{designer}/run-backtest", request with { To = from.AddDays(6) }, Json))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var runResponse = await client.PostAsJsonAsync($"{designer}/run-backtest", request, Json);
        await AssertSuccessAsync(runResponse);
        var outcome = (await runResponse.Content.ReadFromJsonAsync<StrategyDesignRunBacktestResponse>(Json))!;
        outcome.Success.Should().BeTrue();
        outcome.DataProvenanceToken.Should().Be("simulated");
        var sourceRun = (await repository.GetRunByIdAsync(outcome.RunId!))!;
        outcome.ProviderProvenance.Should().NotBeNull();
        outcome.ProviderProvenance!.ReceiptId.Should().Be(sourceRun.ProviderProvenance!.ReceiptId);
        var fill = sourceRun.Metrics!.Fills.Should().ContainSingle().Subject;
        fill.FillPrice.Should().Be(102m);
        fill.FilledQuantity.Should().Be(10);
        fill.Commission.Should().Be(1m);
        sourceRun.Metrics.Metrics.NetPnl.Should().Be(10m * (104m - 102m) - 1m);

        // The same synthetic evidence is explicitly refused by ordinary promotion.
        var denied = await promotion.EvaluateAsync(sourceRun.RunId, ReadScope);
        denied.IsEligible.Should().BeFalse();
        denied.Reason.Should().Contain("Simulated");
        var paper = await RetainSyntheticReviewAsync(sourceRun, repository, reviews, promoter, reference);
        var source = new DesignerDocumentLiveSource(null, designService, scopedDesigns: designs);
        var catalog = LiveStrategyCatalog.CreateDefault();
        catalog.RegisterFallback(source.SelectorParameterKey,
            (LiveStrategyCreationContext context, out ILiveStrategy? runtime, out string? reason) =>
            {
                var found = source.TryCreate(context, out var strategy, out reason);
                runtime = found ? new BacktestStrategyLiveAdapter(context.StrategyId, strategy!) : null;
                return found;
            });
        await using var engine = new LiveTradingEngine(catalog, hub, cache, new PaperOrderGateway(
                NullLogger<PaperOrderGateway>.Instance, liveFeed: cache), oms, portfolio, repository,
            new LiveTradingEngineOptions { Enabled = true },
            auditTrail: audit, designerPaper: new DesignerPaperExecution(repository, new SyntheticReviewAuthority(reviews), sessions));

        var launch = await engine.TryLaunchAsync(paper);
        launch.Launched.Should().BeTrue(launch.Reason);
        var sessionId = PaperSessionPersistenceService.GetStrategyRunSessionId(paper.RunId);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var orderEvidence = new PaperOrderEvidenceReader(history);
        long tick = 0;
        while (true)
        {
            var trade = new Trade(DateTimeOffset.UtcNow, Symbol, 102m, 1000, AggressorSide.Buy, ++tick);
            cache.RecordTrade(Symbol, trade);
            hub.Publish(new LiveMarketEvent(trade.Timestamp, Symbol, trade));
            var receipts = await orderEvidence.ReadForRunAsync(paper.RunId, AccountId, outcome.StrategyVersionSha256!, timeout.Token);
            if (sessions.GetSession(sessionId)?.FillCount == 1 &&
                receipts.Any(receipt => receipt.Admission?.OrderState?.Status == ExecutionSdk.OrderStatus.Filled))
                break;
            await Task.Delay(20, timeout.Token);
        }
        var stopped = await engine.StopRunWithOutcomeAsync(paper.RunId, timeout.Token);
        stopped.Should().NotBeNull();
        stopped!.State.Should().Be(OperationTerminalState.Succeeded,
            string.Join(" ", stopped.Issues.Select(static issue => issue.Message)));
        var session = sessions.GetSession(sessionId)!;
        session.Summary.FundAccountId.Should().Be(AccountId);
        session.Summary.JournalCurrency.Should().Be("USD");
        session.Portfolio!.Cash.Should().Be(100_000m - 10m * 102m - 1m);
        session.Portfolio.Positions.Should().ContainSingle().Which.Quantity.Should().Be(10);
        session.OrderHistory.Should().Contain(order => order.IntentId.HasValue && order.FilledQuantity == 10m);
        sessions.GetLedger(sessionId)!.Journal.Should().Contain(entry =>
            entry.Metadata.FillId.HasValue && entry.Metadata.FinancialAccountId == AccountId.ToString("D") &&
            entry.Metadata.Tags != null && entry.Metadata.Tags.ContainsKey("runId") && entry.Metadata.Tags["runId"] == paper.RunId);
        var retainedOrders = await orderEvidence.ReadForRunAsync(paper.RunId, AccountId, outcome.StrategyVersionSha256!, timeout.Token);
        var retainedOrder = retainedOrders.Should().ContainSingle().Which.Admission!;
        retainedOrder.AuthorityDecision!.Origin.Should().Be(ExecutionSdk.OrderCommandOrigin.StrategyRun);
        retainedOrder.OrderState!.StrategyArtifactId.Should().BeNull();
        await canonicalPublisher.DidNotReceive().PublishAsync(Arg.Any<TradeExecutedEvent>());
        (await client.GetAsync($"/api/execution/sessions/{sessionId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync($"/api/execution/sessions/{sessionId}/reconciliation")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var packetRoute = $"/api/workstation/evidence/subjects/strategy-run/{sourceRun.RunId}";
        using var reporter = app.GetTestClient();
        reporter.DefaultRequestHeaders.Add("Rehearsal-Report-Only", "1");
        (await reporter.GetAsync($"{packetRoute}/packet")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var packetResponse = await client.GetAsync($"{packetRoute}/packet");
        await AssertSuccessAsync(packetResponse);
        var packet = (await packetResponse.Content.ReadFromJsonAsync<EvidencePacketDto>(Json))!;
        packet.Nodes.Should().Contain(node => node.Kind == "strategy-version" &&
            node.Summary.Contains(outcome.StrategyVersionSha256!, StringComparison.Ordinal));
        packet.Nodes.Should().Contain(node => node.Kind == "paper-review" &&
            node.Summary.Contains("fixture-reviewer", StringComparison.Ordinal));
        packet.Nodes.Should().ContainSingle(node => node.Kind == "simulated-accounting-handoff");
        packet.Nodes.Should().Contain(node => node.Kind == "paper-reconciliation" && node.Status == EvidenceStatusDto.Ready);
        var exported = await client.PostAsJsonAsync($"{packetRoute}/export-manifest",
            new EvidencePacketExportRequest(null, "Simulated Designer paper rehearsal"), Json);
        await AssertSuccessAsync(exported);
        var retainedPacket = (await exported.Content.ReadFromJsonAsync<EvidencePacketExportResponse>(Json))!;
        var vaultRoute = $"/workstation/evidence/vault/{retainedPacket.VaultIdentity!.VaultId}";
        (await reporter.GetAsync(vaultRoute)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await reporter.GetAsync(retainedPacket.ManifestRoute)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await AssertSuccessAsync(await client.GetAsync(retainedPacket.ManifestRoute));
        var openedPacket = await client.GetAsync(vaultRoute);
        await AssertSuccessAsync(openedPacket);
        if (Environment.GetEnvironmentVariable("MERIDIAN_DESIGNER_REHEARSAL_PACKET") is { Length: > 0 } outputPath)
        {
            await using var destination = File.Create(outputPath);
            await openedPacket.Content.CopyToAsync(destination);
        }

        var manual = new ExecutionSdk.OrderRequest
        {
            IntentId = Guid.NewGuid(),
            FundAccountId = AccountId,
            Symbol = Symbol,
            Side = ExecutionSdk.OrderSide.Buy,
            Quantity = 1,
            Type = ExecutionSdk.OrderType.Market
        };
        (await client.PostAsJsonAsync($"/api/execution/orders/submit?paperSessionId={sessionId}", manual, Json))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.PostAsJsonAsync("/api/execution/orders/submit?paperSessionId=selected-session", manual, Json))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var concurrent = await Task.WhenAll(
            client.PostAsJsonAsync("/api/execution/orders/submit", manual, Json),
            client.PostAsJsonAsync("/api/execution/orders/submit", manual, Json));
        foreach (var response in concurrent)
            await AssertSuccessAsync(response);
        var submitted = (await concurrent[0].Content.ReadFromJsonAsync<ExecutionSdk.OrderResult>(Json))!;
        var replayResponse = await client.PostAsJsonAsync("/api/execution/orders/submit", manual, Json);
        await AssertSuccessAsync(replayResponse);
        var retainedManual = (await replayResponse.Content.ReadFromJsonAsync<ExecutionSdk.OrderResult>(Json))!;
        retainedManual.OrderId.Should().Be(submitted.OrderId);
        retainedManual.AuthorityDecision!.Origin.Should().Be(ExecutionSdk.OrderCommandOrigin.OperatorTicket);
        (await client.PostAsJsonAsync("/api/execution/orders/submit", manual with { Quantity = 2 }, Json))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await otherCompany.PostAsJsonAsync("/api/execution/orders/submit", manual, Json))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        portfolio.Positions[Symbol].Quantity.Should().Be(11);
    }

    private static async Task AssertSuccessAsync(HttpResponseMessage response) =>
        response.IsSuccessStatusCode.Should().BeTrue(await response.Content.ReadAsStringAsync());

    private static async Task WriteBarsAsync(string root, DateOnly from,
        HistoricalPriceBasis priceBasis = HistoricalPriceBasis.Raw, bool? legacyAdjusted = null)
    {
        var directory = Directory.CreateDirectory(Path.Combine(root, Symbol));
        var prices = new[] { 99m, 101m, 102m, 104m };
        var lines = prices.Select((price, index) =>
        {
            var bar = new HistoricalBar(Symbol, from.AddDays(index), price, price, price, price,
                1_000_000, "synthetic-fixture", index + 1, IsAdjusted: legacyAdjusted, PriceBasis: priceBasis);
            return JsonSerializer.Serialize(MarketEvent.HistoricalBar(bar.ToTimestampUtc(), Symbol, bar,
                "synthetic-fixture", index + 1), MarketDataJsonContext.HighPerformanceOptions);
        });
        await File.WriteAllLinesAsync(Path.Combine(directory.FullName, $"{Symbol}_bars_{from:yyyy-MM-dd}.jsonl"), lines);
    }

    private static ISecurityIdentityQueryService BuildSecurities()
    {
        var securities = Substitute.For<ISecurityIdentityQueryService>();
        securities.GetByIdentifierAsync(SecurityIdentifierKind.Ticker, Symbol, Arg.Any<string?>(),
                Arg.Any<CancellationToken>(), Arg.Any<DateTimeOffset?>())
            .Returns(Task.FromResult<SecurityDetailDto?>(new SecurityDetailDto(
                Guid.Parse("8d3dce19-5a0f-4e97-a6db-50b9f46fb71c"), "Equity", SecurityStatusDto.Active,
                "Synthetic equity", "USD", JsonSerializer.SerializeToElement(new { }),
                JsonSerializer.SerializeToElement(new { }), [], [], 1, DateTimeOffset.UnixEpoch, null)));
        return securities;
    }

    private static IAccountQueryService BuildAccountQuery()
    {
        var accounts = Substitute.For<IAccountQueryService>();
        accounts.GetAccountAsync(AccountId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<AccountSummaryDto?>(new AccountSummaryDto(AccountId, AccountTypeDto.Brokerage,
                null, null, null, null, "SIM", "Synthetic account", "USD", null, true,
                DateTimeOffset.UnixEpoch, null, null, null, null, null)));
        return accounts;
    }

    private static async Task<StrategyRunEntry> RetainSyntheticReviewAsync(
        StrategyRunEntry source, IStrategyRepository runs, IPromotionRecordStore reviews,
        BacktestToLivePromoter promoter, string evidence)
    {
        source.DataProvenanceToken.Should().Be("simulated");
        var checklist = PromotionApprovalChecklist.CreateRequiredFor(RunType.Paper);
        var decision = promoter.CreatePromotionRecord(source.Metrics!, source.StrategyId, source.StrategyName,
            RunType.Backtest, RunType.Paper, source.RunId, Guid.NewGuid().ToString("N"),
            PromotionDecisionKinds.Approved, approvedBy: "fixture-reviewer",
            approvalReason: "Approved for this isolated synthetic rehearsal.",
            approvalChecklist: checklist, evidenceReferences: checklist.Select(key => $"{key}:{evidence}").ToArray(),
            auditReference: "synthetic-paper-review-" + source.RunId) with
        {
            StrategyVersionSha256 = DesignerStrategyVersion.Read(source),
            SourceInputHashSha256 = source.InputHashSha256
        };
        await using var reservation = await reviews.ReserveFirstDecisionAsync(decision);
        var target = PromotionService.CreatePromotedRun(source, reservation.Record);
        target = target with { InputHashSha256 = StrategyRunEntry.ComputeAccountBoundInputHash(target) };
        await runs.RecordRunAsync(target);
        return target;
    }

    private sealed class SyntheticReviewAuthority(IPromotionRecordStore reviews) : IDesignerPaperReviewAuthority
    {
        public async Task<StrategyPromotionRecord?> FindAsync(StrategyRunEntry source, string targetRunId, CancellationToken ct) =>
            (await reviews.LoadAllAsync(ct)).SingleOrDefault(review => review.SourceRunId == source.RunId
                && review.TargetRunId == targetRunId && review.ApprovedBy == "fixture-reviewer");
    }

    private sealed class RehearsalAccountAuthorization : IScopedAuthorizationService
    {
        public Task<ScopedAuthorizationDecisionDto> AuthorizeAsync(string actor, UserPermission requiredPermission,
            AccessScopeKindDto scopeKind, Guid? scopeId, UserPermission globalPermissions, CancellationToken ct = default) =>
            Task.FromResult(new ScopedAuthorizationDecisionDto(
                actor == "rehearsal-operator" && requiredPermission != UserPermission.ExecuteTrades &&
                scopeKind == AccessScopeKindDto.Account && scopeId == AccountId,
                actor, requiredPermission, scopeKind, scopeId, "Synthetic fixture account authority."));
    }
}
