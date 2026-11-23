using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster.CorporateActions;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.SecurityMaster;
using Meridian.Infrastructure.Adapters.Alpaca;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using NSubstitute;
using Xunit;

namespace Meridian.Tests.SecurityMaster;

[Trait("Category", "Integration")]
public sealed class CorporateActionProviderEvidencePostgresTests(SecurityMasterDatabaseFixture fixture)
    : IClassFixture<SecurityMasterDatabaseFixture>
{
    [SecurityMasterDatabaseFact]
    public async Task AlpacaIngest_AllEndpointFamilies_RetainsSeventeenReviewOnlyRevisionsAcrossRetryAmendmentAndRestart()
    {
        // Schema-conformant synthetic records for all 16 Market Data endpoint families. Field
        // roles follow alpacahq/cli api/specs/market-data-api.json, inspected 2026-11-23.
        // Distinct event dates keep unrelated same-security merger facts out of one consensus group.
        string[] endpointTypes =
        [
            "cash_dividend", "forward_split", "reverse_split", "stock_dividend", "spin_off",
            "cash_merger", "stock_merger", "stock_and_cash_merger", "unit_split", "redemption",
            "name_change", "worthless_removal", "rights_distribution", "partial_call", "reorganization",
            "capital_gains_distribution",
        ];
        var securityId = Guid.NewGuid();
        var ticker = "CA" + securityId.ToString("N")[..10].ToUpperInvariant();
        var projectionStore = new PostgresSecurityMasterStore(fixture.Options);
        var effectiveFrom = DateTimeOffset.UtcNow.AddDays(-1);
        await projectionStore.UpsertProjectionAsync(new SecurityProjectionRecord(
            securityId, "Equity", SecurityStatusDto.Active, "Synthetic Alpaca corporate-action issuer", "USD",
            "Ticker", ticker, JsonSerializer.SerializeToElement(new { currency = "USD" }),
            SecurityTermsEnvelope.StampDeclaredFormat(JsonSerializer.SerializeToElement(new { schemaVersion = 1 })),
            JsonSerializer.SerializeToElement(new { sourceSystem = "synthetic-test" }), 1,
            effectiveFrom, null,
            [new SecurityIdentifierDto(SecurityIdentifierKind.Ticker, ticker, true, effectiveFrom)], []));

        var stockDividendId = Guid.NewGuid().ToString("D");
        string StockDividend(decimal rate) => JsonSerializer.Serialize(new
        {
            id = stockDividendId, symbol = ticker, cusip = "000000001", rate,
            ex_date = "2024-06-03", process_date = "2024-06-10",
            record_date = "2024-06-03", payable_date = "2024-06-10",
            additional_source_fact = "Preserve this original provider field",
        });
        var originalStockDividend = StockDividend(0.05m);
        var currentStockDividend = originalStockDividend;
        var spinOff = JsonSerializer.Serialize(new
        {
            id = Guid.NewGuid().ToString("D"), source_symbol = ticker, source_cusip = "000000001",
            new_symbol = "SYNCHILD", new_cusip = "000000002", source_rate = 4m, new_rate = 1m,
            ex_date = "2024-06-04", process_date = "2024-06-10",
        });
        var cashMerger = JsonSerializer.Serialize(new
        {
            id = Guid.NewGuid().ToString("D"), acquiree_symbol = ticker, acquiree_cusip = "000000001",
            rate = 2.5m, effective_date = "2024-06-05", process_date = "2024-06-10",
        });
        var stockMerger = JsonSerializer.Serialize(new
        {
            id = Guid.NewGuid().ToString("D"), acquiree_symbol = ticker, acquiree_cusip = "000000001",
            acquirer_symbol = "SYNACQ", acquirer_cusip = "000000003", acquiree_rate = 1m, acquirer_rate = 0.5m,
            effective_date = "2024-06-06", process_date = "2024-06-10",
        });
        var mixedMerger = JsonSerializer.Serialize(new
        {
            id = Guid.NewGuid().ToString("D"), acquiree_symbol = ticker, acquiree_cusip = "000000001",
            acquirer_symbol = "SYNMIX", acquirer_cusip = "000000004", acquiree_rate = 1m,
            acquirer_rate = 0.7m, cash_rate = 1.25m, effective_date = "2024-06-07", process_date = "2024-06-10",
        });
        var cashDividend = JsonSerializer.Serialize(new
        {
            id = Guid.NewGuid().ToString("D"), symbol = ticker, cusip = "000000001", rate = 0.25m,
            currency = "USD", special = false, foreign = false,
            ex_date = "2024-06-08", process_date = "2024-06-10",
        });
        var forwardSplit = JsonSerializer.Serialize(new
        {
            id = Guid.NewGuid().ToString("D"), symbol = ticker, cusip = "000000001",
            old_rate = 1m, new_rate = 4m, ex_date = "2024-06-09", process_date = "2024-06-10",
        });
        var reverseSplit = JsonSerializer.Serialize(new
        {
            id = Guid.NewGuid().ToString("D"), symbol = ticker, old_cusip = "000000001", new_cusip = "000000011",
            old_rate = 4m, new_rate = 1m, ex_date = "2024-06-10", process_date = "2024-06-10",
        });
        var unitSplit = JsonSerializer.Serialize(new
        {
            id = Guid.NewGuid().ToString("D"), old_symbol = ticker, old_cusip = "000000001", old_rate = 1m,
            new_symbol = "SYNUNIT", new_cusip = "000000005", new_rate = 1m,
            alternate_symbol = "SYNWARRANT", alternate_cusip = "000000006", alternate_rate = 0.3333m,
            effective_date = "2024-06-11", process_date = "2024-06-11",
        });
        var redemption = JsonSerializer.Serialize(new
        {
            id = Guid.NewGuid().ToString("D"), symbol = ticker, cusip = "000000001", rate = 0.141134m,
            payable_date = "2024-06-12", process_date = "2024-06-12",
        });
        var nameChange = JsonSerializer.Serialize(new
        {
            id = Guid.NewGuid().ToString("D"), old_symbol = ticker, old_cusip = "000000001",
            new_symbol = "SYNRENAMED", new_cusip = "000000007", process_date = "2024-06-13",
        });
        var worthlessRemoval = JsonSerializer.Serialize(new
        {
            id = Guid.NewGuid().ToString("D"), symbol = ticker, cusip = "000000001", process_date = "2024-06-14",
        });
        var rightsDistribution = JsonSerializer.Serialize(new
        {
            id = Guid.NewGuid().ToString("D"), source_symbol = ticker, source_cusip = "000000001",
            new_symbol = "SYNRIGHTS", new_cusip = "000000008", rate = 1m,
            ex_date = "2024-06-15", record_date = "2024-06-16", payable_date = "2024-06-17",
            process_date = "2024-06-17", expiration_date = "2024-07-15",
        });
        var partialCall = JsonSerializer.Serialize(new
        {
            id = Guid.NewGuid().ToString("D"), symbol = ticker, cusip = "000000001", process_date = "2024-06-18",
            record_date = "2024-06-17", lottery_date = "2024-06-18", lottery_type = "original",
            results_publication_date = "2024-06-19", payable_date = "2024-06-20", price = 25m, dividend_rate = 0.25m,
        });
        var reorganization = JsonSerializer.Serialize(new
        {
            id = Guid.NewGuid().ToString("D"), symbol = ticker, cusip = "000000001", currency = "USD",
            cash_rate = 0.5m, effective_date = "2024-06-19", process_date = "2024-06-20",
            stock_movements = new[]
            {
                new { symbol = "SYNREORGA", cusip = "000000009", source_rate = 1m, new_rate = 0.6m },
                new { symbol = "SYNREORGB", cusip = "000000010", source_rate = 1m, new_rate = 0.2m },
            },
        });
        var capitalGains = JsonSerializer.Serialize(new
        {
            id = Guid.NewGuid().ToString("D"), symbol = ticker, cusip = "000000001", currency = "USD",
            long_term_rate = 0.45855m, short_term_rate = 0.11019m,
            ex_date = "2024-06-20", record_date = "2024-06-21", process_date = "2024-06-21", payable_date = "2024-06-24",
        });
        using var handler = new SourceResponseHandler(() =>
            $$"""
            {"corporate_actions":{
              "cash_dividends":[{{cashDividend}}],"forward_splits":[{{forwardSplit}}],"reverse_splits":[{{reverseSplit}}],
              "stock_dividends":[{{currentStockDividend}}],"spin_offs":[{{spinOff}}],"cash_mergers":[{{cashMerger}}],
              "stock_mergers":[{{stockMerger}}],"stock_and_cash_mergers":[{{mixedMerger}}],"unit_splits":[{{unitSplit}}],
              "redemptions":[{{redemption}}],"name_changes":[{{nameChange}}],"worthless_removals":[{{worthlessRemoval}}],
              "rights_distributions":[{{rightsDistribution}}],"partial_calls":[{{partialCall}}],
              "reorganizations":[{{reorganization}}],"capital_gains_distributions":[{{capitalGains}}]
            },"next_page_token":null}
            """);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ALPACA_KEY_ID"] = "synthetic-key",
            ["ALPACA_SECRET_KEY"] = "synthetic-secret",
        }).Build();
        var provider = new AlpacaCorporateActionProvider(new SourceHttpClientFactory(handler), configuration,
            NullLogger<AlpacaCorporateActionProvider>.Instance);
        var store = new PostgresCorporateActionOperationsStore(fixture.Options);
        var operations = new CorporateActionOperationsService(store);
        var eventStore = new PostgresSecurityMasterEventStore(fixture.Options, NullLogger<PostgresSecurityMasterEventStore>.Instance);
        var commandService = Substitute.For<ISecurityMasterCorporateActionCommandService>();
        var orchestrator = new CorporateActionIngestOrchestrator([provider], projectionStore, eventStore,
            commandService, NullLogger<CorporateActionIngestOrchestrator>.Instance, operations);
        var ingestRequest = new CorporateActionIngestRequest([ticker], MinimumSourcesToApply: 1, Actor: "synthetic-ingest");

        var first = await orchestrator.IngestAsync(ingestRequest);
        first.Errors.Should().BeEmpty();
        first.Staged.Should().Be(16);
        first.Applied.Should().Be(0);
        first.DuplicatesSkipped.Should().Be(0);
        first.Proposals.Should().OnlyContain(proposal => !proposal.AutoApplied
            && proposal.ProposalState == CorporateActionSourceProposalStates.ReviewRequired);
        var coverage = first.FetchOutcomes.Should().ContainSingle().Subject.Coverage!;
        coverage.Types.Should().HaveCount(16).And.OnlyContain(type =>
            type.Status == CorporateActionTypeCoverageStatus.Complete && type.Observations == 1);
        coverage.Types.Select(type => type.ProviderEventType).Should().BeEquivalentTo(endpointTypes);
        var typesQuery = handler.Requests.Should().ContainSingle().Subject.Query.TrimStart('?').Split('&')
            .Single(parameter => parameter.StartsWith("types=", StringComparison.Ordinal));
        Uri.UnescapeDataString(typesQuery["types=".Length..]).Split(',').Should().BeEquivalentTo(endpointTypes);
        var repeated = await orchestrator.IngestAsync(ingestRequest);
        repeated.Errors.Should().BeEmpty();
        repeated.Staged.Should().Be(0);
        repeated.Applied.Should().Be(0);
        repeated.DuplicatesSkipped.Should().Be(16);

        currentStockDividend = StockDividend(0.1m);
        var changed = await orchestrator.IngestAsync(ingestRequest);
        changed.Errors.Should().BeEmpty();
        changed.Staged.Should().Be(1);
        changed.Applied.Should().Be(0);
        changed.DuplicatesSkipped.Should().Be(15);
        commandService.ReceivedCalls().Should().BeEmpty("source intake must not call the canonical append service");

        var reopenedStore = new PostgresCorporateActionOperationsStore(fixture.Options);
        var reopenedOperations = new CorporateActionOperationsService(reopenedStore);
        var retained = await reopenedOperations.ListSourceProposalsAsync(securityId, null, 20);
        retained.Should().HaveCount(17);
        retained.Should().OnlyContain(proposal =>
            proposal.ProviderIdentity.ReleaseStatus == CorporateActionProviderReleaseStatusDto.ReviewOnly
            && !proposal.ActionAvailability!.CanAccept
            && proposal.AcceptedCorporateActionId == null
            && proposal.InitialCaseId == null);
        retained.Select(proposal => proposal.ProposedAction.SourceEvidence!.ProviderEventType).Distinct()
            .Should().BeEquivalentTo(endpointTypes);
        foreach (var proposal in retained)
        {
            var source = proposal.ProposedAction.SourceEvidence!;
            source.RawPayloadSha256.Should().Be(Sha256Digest.ComputeUtf8(source.RawPayloadJson));
            using var raw = JsonDocument.Parse(source.RawPayloadJson);
            raw.RootElement.GetProperty("id").GetString().Should().Be(proposal.ProviderIdentity.SourceEventId);
        }
        var original = retained.Single(proposal => proposal.ProviderIdentity.SourceEventId == stockDividendId
            && proposal.State == CorporateActionSourceProposalStates.Superseded);
        var amendment = retained.Single(proposal => proposal.ProviderIdentity.SourceEventId == stockDividendId
            && proposal.State == CorporateActionSourceProposalStates.ReviewRequired);
        original.ProposedAction.SourceEvidence!.RawPayloadJson.Should().Be(originalStockDividend);
        amendment.ProposedAction.SourceEvidence!.RawPayloadJson.Should().Be(currentStockDividend);
        original.ProposedAction.DistributionRatio.Should().Be(0.05m);
        amendment.ProposedAction.DistributionRatio.Should().Be(0.1m);
        amendment.ProviderIdentity.SourceEventVersion.Should().NotBe(original.ProviderIdentity.SourceEventVersion);
        amendment.SupersedesProposalId.Should().Be(original.ProposalId);
        var reopenedEvents = new PostgresSecurityMasterEventStore(fixture.Options, NullLogger<PostgresSecurityMasterEventStore>.Instance);
        (await reopenedEvents.LoadCorporateActionsAsync(securityId)).Should().BeEmpty();

        await using var connection = new NpgsqlConnection(fixture.Options.ConnectionString);
        await connection.OpenAsync();
        await using var countCases = connection.CreateCommand();
        countCases.CommandText = $"select count(*) from \"{fixture.Options.Schema}\".corporate_action_processing_cases where security_id = @security_id";
        countCases.Parameters.AddWithValue("security_id", securityId);
        ((long)(await countCases.ExecuteScalarAsync())!).Should().Be(0);
    }

    [SecurityMasterDatabaseFact]
    public async Task SourceRevision_RestartPreservesRawTextTypedTermsAndIdempotentReviewOnlyHistory()
    {
        var securityId = Guid.NewGuid();
        var projectionStore = new PostgresSecurityMasterStore(fixture.Options);
        var effectiveFrom = DateTimeOffset.UtcNow.AddDays(-1);
        await projectionStore.UpsertProjectionAsync(new SecurityProjectionRecord(
            securityId, "Equity", SecurityStatusDto.Active, "Synthetic retained evidence issuer", "USD",
            "Ticker", "EVID", JsonSerializer.SerializeToElement(new { currency = "USD" }),
            SecurityTermsEnvelope.StampDeclaredFormat(JsonSerializer.SerializeToElement(new { schemaVersion = 1 })),
            JsonSerializer.SerializeToElement(new { sourceSystem = "synthetic-test" }), 1,
            effectiveFrom, null,
            [new SecurityIdentifierDto(SecurityIdentifierKind.Ticker, "EVID", true, effectiveFrom)], []));
        var store = new PostgresCorporateActionOperationsStore(fixture.Options);
        var service = new CorporateActionOperationsService(store);
        var request = CorporateActionRetainedEvidenceTests.Request(securityId);

        var original = await service.RecordSourceProposalAsync(request);
        const string equivalentRaw = """{"extra":{"vendor_note":"Retain me"},"rate":5e-2,"id":"native-event"}""";
        var equivalentRequest = request with
        {
            ProposedAction = request.ProposedAction with
            {
                CorpActId = Guid.NewGuid(),
                SourceEvidence = CorporateActionRetainedEvidenceTests.Evidence(equivalentRaw),
            },
        };
        var replay = await service.RecordSourceProposalAsync(equivalentRequest);
        replay.ProposalId.Should().Be(original.ProposalId);
        replay.ProposedAction.SourceEvidence.Should().Be(original.ProposedAction.SourceEvidence);

        var amendmentRequest = request with
        {
            ProposedAction = request.ProposedAction with
            {
                CorpActId = Guid.NewGuid(),
                DistributionRatio = 0.1m,
                SourceEvidence = CorporateActionRetainedEvidenceTests.Evidence(equivalentRaw.Replace("5e-2", "0.1")),
            },
            ProviderIdentity = request.ProviderIdentity with
            {
                SourceEventVersion = "v2:alpaca-rest-v2",
                ObservedAtUtc = request.ProviderIdentity.ObservedAtUtc.AddMinutes(1),
                EvidenceHash = new string('b', 64),
                EvidenceReference = "alpaca://corporate-actions/v1/spin_off/native-event/versions/alpaca-rest-v2/amendment",
            },
        };
        var amendment = await service.RecordSourceProposalAsync(amendmentRequest);
        amendment.SupersedesProposalId.Should().Be(original.ProposalId);
        amendment.EconomicFingerprint.Should().NotBe(original.EconomicFingerprint);

        var reopenedStore = new PostgresCorporateActionOperationsStore(fixture.Options);
        var reopenedService = new CorporateActionOperationsService(reopenedStore);
        var reloadedOriginal = await reopenedService.GetSourceProposalAsync(original.ProposalId);
        var reloadedAmendment = await reopenedService.GetSourceProposalAsync(amendment.ProposalId);
        reloadedOriginal!.State.Should().Be(CorporateActionSourceProposalStates.Superseded);
        reloadedOriginal.ProposedAction.SourceEvidence.Should().Be(original.ProposedAction.SourceEvidence);
        reloadedAmendment!.ProposedAction.SourceEvidence.Should().Be(amendmentRequest.ProposedAction.SourceEvidence);
        reloadedAmendment.ProposedAction.DistributionRatio.Should().Be(0.1m);
        reloadedAmendment.State.Should().Be(CorporateActionSourceProposalStates.ReviewRequired);
        reloadedAmendment.ActionAvailability!.CanAccept.Should().BeFalse();
        reloadedAmendment.AcceptedCorporateActionId.Should().BeNull();
        reloadedAmendment.InitialCaseId.Should().BeNull();

        (await reopenedService.RecordSourceProposalAsync(amendmentRequest)).ProposalId.Should().Be(amendment.ProposalId);
        (await reopenedService.RecordSourceProposalAsync(request)).ProposalId.Should().Be(original.ProposalId);
        (await reopenedStore.ListSourceProposalsAsync(securityId, null, 10)).Should().HaveCount(2);
        var eventStore = new PostgresSecurityMasterEventStore(fixture.Options, NullLogger<PostgresSecurityMasterEventStore>.Instance);
        (await eventStore.LoadCorporateActionsAsync(securityId)).Should().BeEmpty();
    }

    private sealed class SourceHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class SourceResponseHandler(Func<string> response) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            request.Method.Should().Be(HttpMethod.Get);
            request.RequestUri!.Host.Should().Be("data.alpaca.markets");
            request.RequestUri.AbsolutePath.Should().Be("/v1/corporate-actions");
            Requests.Add(request.RequestUri);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response(), Encoding.UTF8, "application/json"),
            });
        }
    }
}
