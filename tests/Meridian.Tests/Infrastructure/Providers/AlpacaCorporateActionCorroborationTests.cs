using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.SecurityMaster;
using Meridian.Core.Config;
using Meridian.Execution.Sdk;
using Meridian.Infrastructure.Adapters.Alpaca;
using Meridian.Infrastructure.Adapters.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Tests.Infrastructure.Providers;

/// <summary>
/// Public Alpaca OpenAPI examples are compared with independent issuer/exchange facts;
/// selected examples also use synthetic account activities through the real adapters.
/// These are not live API captures.
/// Provenance and known date disagreements are recorded in
/// docs/testing/alpaca-corporate-action-coverage.md. No network requests leave the stub handler.
/// </summary>
public sealed class AlpacaCorporateActionCorroborationTests
{
    // Unaltered event objects from alpacahq/cli api/specs/market-data-api.json,
    // blob 6c67f0e7042407832ec32cd5a7de1e8279d698fc, inspected 2026-11-23.
    // The response envelope is synthetic. Dates that disagree with independent sources stay
    // exactly as published in the examples; a successful mapping is not economic acceptance.
    private const string StockDividendExample =
        """{"cusip":"605015106","ex_date":"2023-05-19","id":"3ae94c30-2d37-473a-bf29-5f7b4ab6d3ca","payable_date":"2023-05-05","process_date":"2023-05-19","rate":0.05,"record_date":"2023-05-22","symbol":"MSBC"}""";

    private const string SpinOffExample =
        """{"ex_date":"2023-08-15","id":"82e602f6-35bc-4651-a5dd-f6d88ff37c55","new_cusip":"85237B101","new_rate":1,"new_symbol":"SRM","process_date":"2023-08-15","record_date":"2023-08-15","source_cusip":"48208F105","source_rate":19.35,"source_symbol":"JUPW"}""";

    private const string CashMergerExample =
        """{"acquiree_cusip":"Y2687W108","acquiree_symbol":"GLOP","effective_date":"2023-07-17","id":"3772bbd7-4ad5-44d4-9cc0-f69156a2f8f5","payable_date":"2023-07-17","process_date":"2023-07-17","rate":5.37}""";

    private const string StockMergerExample =
        """{"acquiree_cusip":"53223X107","acquiree_rate":1,"acquiree_symbol":"LSI","acquirer_cusip":"30225T102","acquirer_rate":0.895,"acquirer_symbol":"EXR","effective_date":"2023-07-20","id":"728f8cb2-a00e-4bc7-ad14-d15fe82bbcff","payable_date":"2023-07-20","process_date":"2023-07-20"}""";

    private const string MixedMergerExample =
        """{"acquiree_cusip":"561409103","acquiree_rate":1,"acquiree_symbol":"MLVF","acquirer_cusip":"31931U102","acquirer_rate":0.7733,"acquirer_symbol":"FRBA","cash_rate":7.8,"effective_date":"2023-07-18","id":"e5248356-2c06-42cf-aeb9-1595bd616cdb","payable_date":"2023-07-18","process_date":"2023-07-18"}""";

    private const string RightsDistributionExample =
        """{"ex_date":"2024-04-17","expiration_date":"2024-05-14","id":"69794cfd-0adc-4e11-9211-9210a9cf8932","new_cusip":"454089111","new_symbol":"IFN.RTWI","payable_date":"2024-04-19","process_date":"2024-04-19","rate":1,"record_date":"2024-04-18","source_cusip":"454089103","source_symbol":"IFN"}""";

    [Fact]
    public async Task StockDividend_IssuerRateMatchesBrokerAllocation_PayableDateDisagreementIsRetained()
    {
        // Mission Bancorp's May 2, 2023 release: 5% extra shares, record May 22, payable June 5.
        // https://s205.q4cdn.com/450322562/files/doc_news/2023/Stock-Dividend-Repurchase-Press-Release-2023_Q4.pdf
        var command = await FetchActionAsync("MSBC", "stock_dividends", StockDividendExample);
        var activities = await FetchBrokerActivitiesAsync(
            """
            [{"id":"synthetic-msbc-stock-dividend","activity_type":"DIVSTOCK","date":"2023-06-05",
              "symbol":"MSBC","qty":"100","net_amount":"0","currency":"USD"}]
            """);

        command.ActionType.Should().Be("StockDividend");
        command.DistributionRatio.Should().Be(0.05m, "a 5% stock dividend allocates extra shares rather than replacing shares");
        command.Payload!.Value.GetProperty("stockDividendRate").GetDecimal().Should().Be(0.05m);
        command.RecordDate.Should().Be(new DateOnly(2023, 5, 22));
        var allocation = activities.Should().ContainSingle().Subject;
        allocation.Subtype.Should().Be(BrokerageActivitySubtype.StockDividend);
        allocation.Quantity.Should().Be(2_000m * command.DistributionRatio);
        allocation.NetAmount.Should().Be(0m);

        var issuerPayableDate = new DateOnly(2023, 6, 5);
        command.PayableDate.Should().Be(new DateOnly(2023, 5, 5));
        command.PayableDate.Should().NotBe(issuerPayableDate,
            "the published provider example conflicts with the issuer; intake must preserve the evidence for review");
        AssertReviewOnlyEvidence(command, "stock_dividend", StockDividendExample);
    }

    [Fact]
    public async Task SpinOff_ExchangeRatioMatchesBrokerAllocation_RecordDateDisagreementIsRetained()
    {
        // Nasdaq ECA2023-382: 1 SRM for 19.35 JUPW, ex August 15, record July 7, 2023.
        // https://www.nasdaqtrader.com/TraderNews.aspx?id=ECA2023-382
        var command = await FetchActionAsync("JUPW", "spin_offs", SpinOffExample);
        var activities = await FetchBrokerActivitiesAsync(
            """
            [{"id":"synthetic-jupw-srm-allocation","activity_type":"SPIN","date":"2023-08-15",
              "symbol":"SRM","qty":"100","net_amount":"0","currency":"USD"}]
            """);

        command.ActionType.Should().Be("SpinOff");
        command.DistributionRatio.Should().Be(1m / 19.35m);
        command.ExDate.Should().Be(new DateOnly(2023, 8, 15));
        var terms = command.Payload!.Value;
        terms.GetProperty("sourceSymbol").GetString().Should().Be("JUPW");
        terms.GetProperty("sourceCusip").GetString().Should().Be("48208F105");
        terms.GetProperty("successorSymbol").GetString().Should().Be("SRM");
        terms.GetProperty("successorCusip").GetString().Should().Be("85237B101");
        var allocation = activities.Should().ContainSingle().Subject;
        allocation.Subtype.Should().Be(BrokerageActivitySubtype.Spinoff);
        allocation.Quantity.Should().Be(1_935m * terms.GetProperty("successorRate").GetDecimal()
            / terms.GetProperty("sourceRate").GetDecimal());
        allocation.Symbol.Should().Be("SRM");

        command.RecordDate.Should().Be(new DateOnly(2023, 8, 15));
        command.RecordDate.Should().NotBe(new DateOnly(2023, 7, 7),
            "the public schema example disagrees with Nasdaq and must not overwrite the source record with a guessed correction");
        AssertReviewOnlyEvidence(command, "spin_off", SpinOffExample);
    }

    [Fact]
    public async Task CashMerger_IssuerCashRateMatchesBrokerLegs_UnknownCurrencyStaysUnknown()
    {
        // GasLog Partners' 2023 Form 20-F confirms $5.37 cash per common unit and July 13 closing.
        // https://www.sec.gov/Archives/edgar/data/1598655/000110465924031663/glop-20221231x20f.htm
        var command = await FetchActionAsync("GLOP", "cash_mergers", CashMergerExample);
        var activities = await FetchBrokerActivitiesAsync(
            """
            [{"id":"synthetic-glop-removal","activity_type":"MA","date":"2023-07-17",
              "symbol":"GLOP","qty":"-1000","net_amount":"0","currency":"USD"},
             {"id":"synthetic-glop-cash","activity_type":"MA","date":"2023-07-17",
              "symbol":"GLOP","qty":"0","net_amount":"5370","currency":"USD"}]
            """);

        command.ActionType.Should().Be("MergerAbsorption");
        var cashPerUnit = command.Payload!.Value.GetProperty("cashConsiderationPerUnit").GetDecimal();
        cashPerUnit.Should().Be(5.37m);
        command.ExchangeRatio.Should().BeNull();
        var removal = activities.Single(activity => activity.EventId == "synthetic-glop-removal");
        removal.Quantity.Should().Be(-1_000m, "merger removals carry a signed quantity without a trade side");
        activities.Sum(activity => activity.NetAmount).Should().Be(-removal.Quantity!.Value * cashPerUnit);
        activities.Should().OnlyContain(activity => activity.Subtype == BrokerageActivitySubtype.Merger);

        command.Currency.Should().BeNull("the broker activity's denomination cannot silently fill missing announcement currency");
        command.PreserveUnknownCurrency.Should().BeTrue();
        command.ExDate.Should().Be(new DateOnly(2023, 7, 17));
        command.ExDate.Should().NotBe(new DateOnly(2023, 7, 13), "the source effective date and issuer closing date remain separate evidence");
        command.Payload.Value.GetProperty("dateBasis").GetString().Should().Be("effective_date");
        AssertReviewOnlyEvidence(command, "cash_merger", CashMergerExample);
    }

    [Fact]
    public async Task StockMerger_IssuerExchangeRatioMatchesSignedBrokerLegs()
    {
        // Extra Space's July 20, 2023 8-K: 0.895 EXR for each LSI share, cash in lieu of fractions.
        // https://ir.extraspace.com/static-files/825c268c-3638-4516-98c9-6806c0a3552b
        var command = await FetchActionAsync("LSI", "stock_mergers", StockMergerExample);
        var activities = await FetchBrokerActivitiesAsync(
            """
            [{"id":"synthetic-lsi-removal","activity_type":"MA","date":"2023-07-20",
              "symbol":"LSI","qty":"-1000","net_amount":"0","currency":"USD"},
             {"id":"synthetic-exr-allocation","activity_type":"MA","date":"2023-07-20",
              "symbol":"EXR","qty":"895","net_amount":"0","currency":"USD"}]
            """);

        command.ActionType.Should().Be("MergerAbsorption");
        command.ExchangeRatio.Should().Be(0.895m);
        command.ExDate.Should().Be(new DateOnly(2023, 7, 20));
        command.Payload!.Value.GetProperty("dateBasis").GetString().Should().Be("effective_date");
        command.Payload.Value.GetProperty("sourceSymbol").GetString().Should().Be("LSI");
        command.Payload.Value.GetProperty("successorSymbol").GetString().Should().Be("EXR");
        var removed = activities.Single(activity => activity.Symbol == "LSI");
        var received = activities.Single(activity => activity.Symbol == "EXR");
        removed.Quantity.Should().Be(-1_000m);
        received.Quantity.Should().Be(-removed.Quantity!.Value * command.ExchangeRatio);
        activities.Should().OnlyContain(activity => activity.Subtype == BrokerageActivitySubtype.Merger);
        activities.Sum(activity => activity.NetAmount).Should().Be(0m, "this synthetic holding has no fractional entitlement");
        AssertReviewOnlyEvidence(command, "stock_merger", StockMergerExample);
    }

    [Fact]
    public async Task StockAndCashMerger_ExchangeNoticeMatchesThreeBrokerLegs_ClosingAndSuspensionDatesStayDistinct()
    {
        // Nasdaq ECA2023-401: $7.80 and 0.7733 FRBA per MLVF; closed July 17, suspended July 18.
        // https://www.nasdaqtrader.com/TraderNews.aspx?id=ECA2023-401
        var command = await FetchActionAsync("MLVF", "stock_and_cash_mergers", MixedMergerExample);
        var activities = await FetchBrokerActivitiesAsync(
            """
            [{"id":"synthetic-mlvf-removal","activity_type":"MA","date":"2023-07-18",
              "symbol":"MLVF","qty":"-10000","net_amount":"0","currency":"USD"},
             {"id":"synthetic-frba-allocation","activity_type":"MA","date":"2023-07-18",
              "symbol":"FRBA","qty":"7733","net_amount":"0","currency":"USD"},
             {"id":"synthetic-mlvf-cash","activity_type":"MA","date":"2023-07-18",
              "symbol":"MLVF","qty":"0","net_amount":"78000","currency":"USD"}]
            """);

        command.ActionType.Should().Be("MergerAbsorption");
        command.ExchangeRatio.Should().Be(0.7733m);
        var cashPerUnit = command.Payload!.Value.GetProperty("cashConsiderationPerUnit").GetDecimal();
        cashPerUnit.Should().Be(7.8m);
        command.Amount.Should().BeNull("merger consideration is not a cash dividend");
        var removal = activities.Single(activity => activity.EventId == "synthetic-mlvf-removal");
        removal.Quantity.Should().Be(-10_000m);
        activities.Single(activity => activity.Symbol == "FRBA").Quantity
            .Should().Be(-removal.Quantity!.Value * command.ExchangeRatio);
        activities.Sum(activity => activity.NetAmount).Should().Be(-removal.Quantity.Value * cashPerUnit);
        activities.Should().HaveCount(3).And.OnlyContain(activity => activity.Subtype == BrokerageActivitySubtype.Merger);

        command.ExDate.Should().Be(new DateOnly(2023, 7, 18));
        command.ExDate.Should().NotBe(new DateOnly(2023, 7, 17), "a provider effective date must not be relabelled as issuer closing");
        AssertReviewOnlyEvidence(command, "stock_and_cash_merger", MixedMergerExample);
    }

    [Fact]
    public async Task RightsDistribution_IssuerTermsDistinguishOneRightAllocationFromThreeRightExercise()
    {
        // Official Alpaca OpenAPI example; synthetic response envelope, no broker activity claim.
        // The issuer's June 30, 2024 report confirms one right per held share, record April 18,
        // expiry May 14, and three rights to subscribe for one new share. Distribution and
        // exercise are separate events; the announcement does not prove an account exercised.
        // https://www.sec.gov/Archives/edgar/data/917100/000110465924097701/tm2419201d3_ncsrs.htm
        var command = await FetchActionAsync("IFN", "rights_distributions", RightsDistributionExample);

        command.ActionType.Should().Be(CorporateActionEventTypes.RightsDistribution);
        command.SourceEventId.Should().Be("69794cfd-0adc-4e11-9211-9210a9cf8932");
        command.DistributionRatio.Should().Be(1m, "the issuer allocated one right for each existing share");
        command.DistributionRatio.Should().NotBe(1m / 3m,
            "one new share for three rights describes a later exercise, not the rights distribution");
        var terms = command.Payload!.Value;
        terms.GetProperty(CorporateActionPayloads.RightsPerHeldUnit).GetDecimal().Should().Be(1m);
        (300m * command.DistributionRatio!.Value).Should().Be(300m,
            "a hypothetical 300-share holder receives 300 rights, without implying exercise or a new-share purchase");
        command.RecordDate.Should().Be(new DateOnly(2024, 4, 18));
        terms.GetProperty(CorporateActionPayloads.ExpirationDate).GetString().Should().Be("2024-05-14");
        // Preserve additional provider dates and identifiers without attributing them to the issuer report.
        command.ExDate.Should().Be(new DateOnly(2024, 4, 17));
        command.PayableDate.Should().Be(new DateOnly(2024, 4, 19));
        terms.GetProperty(CorporateActionPayloads.DateBasis).GetString().Should().Be("ex_date");
        terms.GetProperty(CorporateActionPayloads.SourceSymbol).GetString().Should().Be("IFN");
        terms.GetProperty(CorporateActionPayloads.SuccessorSymbol).GetString().Should().Be("IFN.RTWI");
        terms.GetProperty(CorporateActionPayloads.SourceCusip).GetString().Should().Be("454089103");
        terms.GetProperty(CorporateActionPayloads.SuccessorCusip).GetString().Should().Be("454089111");
        terms.TryGetProperty(CorporateActionPayloads.RightsSecurityId, out _).Should().BeFalse();
        terms.TryGetProperty(CorporateActionPayloads.SuccessorSecurityId, out _).Should().BeFalse();
        terms.TryGetProperty(CorporateActionPayloads.UnitsPerRight, out _).Should().BeFalse();
        terms.TryGetProperty(CorporateActionPayloads.ExercisePricePerUnit, out _).Should().BeFalse();
        command.Amount.Should().BeNull("the source distribution does not record a subscription payment");
        command.ExchangeRatio.Should().BeNull();
        AssertReviewOnlyEvidence(command, "rights_distribution", RightsDistributionExample);
    }

    [Theory]
    [InlineData("DIVSTOCK", BrokerageActivitySubtype.StockDividend)]
    [InlineData("SPIN", BrokerageActivitySubtype.Spinoff)]
    [InlineData("MA", BrokerageActivitySubtype.Merger)]
    public async Task BrokerCorporateActionReversal_WithoutTradeSide_PreservesNegativeQuantity(
        string providerCode,
        BrokerageActivitySubtype expectedSubtype)
    {
        var payload = JsonSerializer.Serialize(new[]
        {
            new { id = "synthetic-ca-reversal", activity_type = providerCode, date = "2023-08-15",
                symbol = "SYNTH", qty = "-2.5", net_amount = "0", currency = "USD" }
        });

        var activity = (await FetchBrokerActivitiesAsync(payload)).Should().ContainSingle().Subject;

        activity.Subtype.Should().Be(expectedSubtype);
        activity.Quantity.Should().Be(-2.5m);
        activity.Metadata.Should().NotContainKey("side", "non-trade removals and reversals need no fabricated sell side");
    }

    private static void AssertReviewOnlyEvidence(CorporateActionCommand command, string eventType, string rawPayload)
    {
        command.ReleaseStatus.Should().Be(CorporateActionProviderReleaseStatusDto.ReviewOnly,
            "agreement with a synthetic broker activity or independent public fact never grants accounting acceptance");
        command.SourceEventId.Should().NotBeNullOrWhiteSpace();
        command.SourceEventVersion.Should().StartWith("payload-sha256:");
        command.SourceEvidence.Should().NotBeNull();
        command.SourceEvidence!.ProviderEventType.Should().Be(eventType);
        command.SourceEvidence.RawPayloadJson.Should().Be(rawPayload);
        command.SourceEvidence.RawPayloadSha256.Should().Be(
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawPayload))).ToLowerInvariant());
        command.Payload!.Value.GetProperty("providerEventType").GetString().Should().Be(eventType);
    }

    private static async Task<CorporateActionCommand> FetchActionAsync(string symbol, string group, string rawPayload)
    {
        var response = $$"""{"corporate_actions":{"{{group}}":[{{rawPayload}}]},"next_page_token":null}""";
        var factory = new StubHttpClientFactory(new StubHandler(request =>
        {
            request.Method.Should().Be(HttpMethod.Get);
            request.RequestUri!.Host.Should().Be("data.alpaca.markets");
            request.RequestUri.AbsolutePath.Should().Be("/v1/corporate-actions");
            return response;
        }));
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ALPACA_KEY_ID"] = "synthetic-key",
            ["ALPACA_SECRET_KEY"] = "synthetic-secret"
        }).Build();
        var provider = new AlpacaCorporateActionProvider(factory, configuration,
            NullLogger<AlpacaCorporateActionProvider>.Instance);

        var result = await provider.FetchWithOutcomeAsync(symbol,
            Guid.Parse("11111111-1111-4111-8111-111111111111"));

        result.Status.Should().Be(CorporateActionFetchStatus.Complete);
        return result.Observations.Should().ContainSingle().Subject;
    }

    private static async Task<IReadOnlyList<BrokerageActivityEventDto>> FetchBrokerActivitiesAsync(string activities)
    {
        var factory = new StubHttpClientFactory(new StubHandler(request =>
        {
            request.Method.Should().Be(HttpMethod.Get);
            request.RequestUri!.Host.Should().Be("paper-api.alpaca.markets");
            return request.RequestUri.AbsolutePath switch
            {
                "/v2/account" =>
                    """{"account_number":"SYNTH-CA-001","status":"active","currency":"USD","equity":"100000","cash":"50000","buying_power":"50000"}""",
                "/v2/orders" => "[]",
                "/v2/account/activities" => activities,
                _ => throw new InvalidOperationException("Unexpected endpoint in synthetic corporate-action fixture.")
            };
        }));
        var gateway = new AlpacaBrokerageGateway(factory,
            new AlpacaOptions(KeyId: "synthetic-key", SecretKey: "synthetic-secret", UseSandbox: true),
            NullLogger<AlpacaBrokerageGateway>.Instance);

        var snapshot = await gateway.GetActivitySnapshotAsync("SYNTH-CA-001");

        snapshot.Activities.Should().NotBeNull();
        snapshot.Cursor.Should().NotBeNull();
        snapshot.Cursor!.IsComplete.Should().BeTrue();
        return snapshot.Activities!;
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, string> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(respond(request), Encoding.UTF8, "application/json")
            });
        }
    }
}
