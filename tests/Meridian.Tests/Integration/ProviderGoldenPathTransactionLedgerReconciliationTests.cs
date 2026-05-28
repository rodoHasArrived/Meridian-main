using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Meridian.Application.Pipeline;
using Meridian.Contracts.Domain.Enums;
using Meridian.Ledger;
using Xunit;
using LedgerBook = Meridian.Ledger.Ledger;

namespace Meridian.Tests.Integration;

[Trait("Category", "Integration")]
public sealed class ProviderGoldenPathTransactionLedgerReconciliationTests
{
    private const string SchemaVersion = "providerGoldenPathScenario/v1";

    private static readonly string FixturesRoot = ResolveFixturesRoot();

    private static readonly Regex ForbiddenText = new(
        @"(?i)(AKIA[0-9A-Z]{16}|-----BEGIN|APCA-API-KEY-ID|APCA-API-SECRET-KEY|api[_-]?key|secret[_-]?key|client[_-]?secret|access[_-]?token|refresh[_-]?token|bearer\s+[a-z0-9._-]+)",
        RegexOptions.Compiled);

    public static IEnumerable<object[]> ProviderGoldenPathFixtures()
    {
        foreach (var fixture in Directory.GetFiles(FixturesRoot, "*partial-fill-ledger-break.json", SearchOption.AllDirectories).OrderBy(f => f))
            yield return new object[] { fixture };
    }

    public static IEnumerable<object[]> GeneratedPartialFillSplits()
    {
        yield return new object[] { "alpaca", new[] { 25m, 75m } };
        yield return new object[] { "alpaca", new[] { 1m, 2m, 3m, 94m } };
        yield return new object[] { "interactive-brokers", new[] { 50m, 25m, 25m } };
        yield return new object[] { "interactive-brokers", new[] { 10m, 10m, 30m, 50m } };
    }

    public static IEnumerable<object[]> GeneratedScenarioMatrix()
    {
        var start = DateTimeOffset.Parse("2026-04-11T14:30:00Z");

        yield return new object[]
        {
            new ProviderGoldenPathScenarioOptions("alpaca", "AAPL", 100m, 190.10m, 0m, 99m, [25m, 75m], start)
        };
        yield return new object[]
        {
            new ProviderGoldenPathScenarioOptions("alpaca", "MSFT", 250m, 411.25m, 0m, 249m, [50m, 75m, 125m], start)
        };
        yield return new object[]
        {
            new ProviderGoldenPathScenarioOptions("interactive-brokers", "AAPL", 100m, 190.20m, 1.25m, 99m, [40m, 60m], start)
        };
        yield return new object[]
        {
            new ProviderGoldenPathScenarioOptions("interactive-brokers", "SPY", 1000m, 520.42m, 4.75m, 998m, [100m, 125m, 250m, 525m], start)
        };
    }

    [Theory]
    [MemberData(nameof(ProviderGoldenPathFixtures))]
    public void FixtureSchema_ProviderGoldenPath_AllExpectedSectionsArePresent(string fixturePath)
    {
        using var document = LoadFixture(fixturePath);
        var root = document.RootElement;

        root.GetProperty("schemaVersion").GetString().Should().Be(SchemaVersion);
        var metadata = root.GetProperty("metadata");
        metadata.GetProperty("provider").GetString().Should().NotBeNullOrWhiteSpace();
        metadata.GetProperty("scenarioName").GetString().Should().NotBeNullOrWhiteSpace();
        metadata.GetProperty("generatedAtUtc").GetDateTimeOffset().Should().BeAfter(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        metadata.GetProperty("noLiveNetwork").GetBoolean().Should().BeTrue();

        var sourceDocs = metadata.GetProperty("sourceDocs").EnumerateArray().Select(e => e.GetString()).ToList();
        sourceDocs.Should().NotBeEmpty();
        sourceDocs.Should().OnlyContain(url => url is not null && (url.Contains("docs.alpaca.markets", StringComparison.OrdinalIgnoreCase) || url.Contains("interactivebrokers.com/campus", StringComparison.OrdinalIgnoreCase)));

        root.GetProperty("providerMessages").ValueKind.Should().Be(JsonValueKind.Object);
        root.GetProperty("expectedCanonicalEvents").ValueKind.Should().Be(JsonValueKind.Object);
        root.GetProperty("executionLifecycle").GetProperty("states").GetArrayLength().Should().BeGreaterThanOrEqualTo(3);
        root.GetProperty("executionLifecycle").GetProperty("fills").GetArrayLength().Should().BeGreaterThanOrEqualTo(2);
        root.GetProperty("expectedLedger").GetProperty("lines").GetArrayLength().Should().BeGreaterThanOrEqualTo(2);
        root.GetProperty("statementRows").GetArrayLength().Should().BeGreaterThanOrEqualTo(2);
        root.GetProperty("expectedBreaks").GetArrayLength().Should().Be(1);
        AssertEvidenceIdsAreExplicit(root.GetProperty("expectedEvidence"));
    }

    [Theory]
    [MemberData(nameof(ProviderGoldenPathFixtures))]
    public void FixtureSecurity_ProviderGoldenPath_ContainsNoCredentialsOrLiveEndpoints(string fixturePath)
    {
        var json = File.ReadAllText(fixturePath);

        ForbiddenText.IsMatch(json).Should().BeFalse($"[{Path.GetFileName(fixturePath)}] must not contain credentials or credential-shaped fields");
        json.Should().NotContain("paper-api.alpaca.markets", because: "fixtures must not point at live or paper trading endpoints");
        json.Should().NotContain("api.alpaca.markets", because: "fixtures must not point at live or paper trading endpoints");
        json.Should().NotContain("localhost:7496", because: "IBKR fixtures must stay offline and not target TWS/Gateway");
        json.Should().NotContain("localhost:7497", because: "IBKR fixtures must stay offline and not target TWS/Gateway");
    }

    [Fact]
    public void Scenario_AlpacaAaplPartialFill_LedgerAndReconciliationEvidenceRetained()
    {
        using var document = LoadProviderFixture("alpaca");
        var root = document.RootElement;

        root.GetProperty("metadata").GetProperty("provider").GetString().Should().Be("alpaca");
        var canonical = root.GetProperty("expectedCanonicalEvents");
        canonical.GetProperty("trade").GetProperty("symbol").GetString().Should().Be("AAPL");
        canonical.GetProperty("trade").GetProperty("venue").GetString().Should().Be("V");
        canonical.GetProperty("trade").GetProperty("conditions").EnumerateArray().Select(e => e.GetString()).Should().Contain("@");
        canonical.GetProperty("trade").GetProperty("tape").GetString().Should().Be("C");
        canonical.GetProperty("quote").GetProperty("bidPrice").GetDecimal().Should().Be(189.95m);
        canonical.GetProperty("quote").GetProperty("askPrice").GetDecimal().Should().Be(190.05m);
        canonical.GetProperty("fill").GetProperty("commission").GetDecimal().Should().Be(0m);

        AssertExecutionLifecycle(root);
        var ledger = ReplayExpectedLedger(root);
        AssertPortfolioAndBreak(root, ledger);
    }

    [Fact]
    public void Scenario_IbkrAaplSmartOrder_LedgerAndReconciliationEvidenceRetained()
    {
        using var document = LoadProviderFixture("interactive-brokers");
        var root = document.RootElement;

        root.GetProperty("metadata").GetProperty("provider").GetString().Should().Be("interactive-brokers");
        var contract = root.GetProperty("providerMessages").GetProperty("contract");
        contract.GetProperty("symbol").GetString().Should().Be("AAPL");
        contract.GetProperty("secType").GetString().Should().Be("STK");
        contract.GetProperty("exchange").GetString().Should().Be("SMART");
        contract.GetProperty("currency").GetString().Should().Be("USD");
        AssertIbkrOrderCallbacksPreserveMonotonicity(root);

        var canonical = root.GetProperty("expectedCanonicalEvents");
        canonical.GetProperty("contract").GetProperty("venue").GetString().Should().Be("SMART");
        canonical.GetProperty("fill").GetProperty("commission").GetDecimal().Should().Be(1.25m);

        AssertExecutionLifecycle(root);
        var ledger = ReplayExpectedLedger(root);
        AssertPortfolioAndBreak(root, ledger);
    }

    [Theory]
    [MemberData(nameof(GeneratedPartialFillSplits))]
    public void ProviderGoldenPath_PropertyProbe_GeneratedPartialFillsPreserveLedgerBalance(string provider, decimal[] splitQuantities)
    {
        using var document = LoadProviderFixture(provider);
        var root = document.RootElement;
        var lifecycle = root.GetProperty("executionLifecycle");
        var totalQuantity = lifecycle.GetProperty("finalQuantity").GetDecimal();
        splitQuantities.Sum().Should().Be(totalQuantity);

        var generated = ProviderGoldenPathScenarioGenerator.Create(new ProviderGoldenPathScenarioOptions(
            ProviderId: provider,
            Symbol: "AAPL",
            Quantity: totalQuantity,
            AveragePrice: lifecycle.GetProperty("averagePrice").GetDecimal(),
            Commission: lifecycle.GetProperty("commission").GetDecimal(),
            StatementQuantity: root.GetProperty("statementRows").EnumerateArray().Single(row => row.GetProperty("rowType").GetString() == "position").GetProperty("quantity").GetDecimal(),
            FillSplits: splitQuantities,
            Start: DateTimeOffset.Parse("2026-04-11T14:30:00Z")));
        var generatedLedger = ProviderGoldenPathScenarioGenerator.ReplayLedger(generated);

        var debits = generatedLedger.Journal.Single().Lines.Sum(line => line.Debit);
        var credits = generatedLedger.Journal.Single().Lines.Sum(line => line.Credit);
        debits.Should().Be(credits);
        generatedLedger.GetBalance(new LedgerAccount("Securities", LedgerAccountType.Asset, "AAPL")).Should().Be(decimal.Round(totalQuantity * generated.AveragePrice, 2, MidpointRounding.AwayFromZero));

        generated.ExpectedBreak.Materiality.Should().Be(1m);
        generated.Fills.Sum(fill => fill.Quantity).Should().Be(totalQuantity);
    }

    [Theory]
    [MemberData(nameof(GeneratedScenarioMatrix))]
    public void ProviderGoldenPath_GeneratedScenarioMatrix_PreservesLedgerBalanceAndBreakEvidence(ProviderGoldenPathScenarioOptions options)
    {
        var scenario = ProviderGoldenPathScenarioGenerator.Create(options);
        var ledger = ProviderGoldenPathScenarioGenerator.ReplayLedger(scenario);

        scenario.Fills.Sum(fill => fill.Quantity).Should().Be(options.Quantity);
        scenario.Fills.Sum(fill => fill.Commission).Should().Be(options.Commission);
        scenario.LedgerLines.Sum(line => line.Debit).Should().Be(scenario.LedgerLines.Sum(line => line.Credit));
        ledger.Journal.Single().Lines.Sum(line => line.Debit).Should().Be(ledger.Journal.Single().Lines.Sum(line => line.Credit));

        var expectedNotional = decimal.Round(options.Quantity * options.AveragePrice, 2, MidpointRounding.AwayFromZero);
        ledger.GetBalance(new LedgerAccount("Securities", LedgerAccountType.Asset, options.Symbol)).Should().Be(expectedNotional);
        scenario.ExpectedBreak.Category.Should().Be("PositionQuantityMismatch");
        scenario.ExpectedBreak.Materiality.Should().Be(Math.Abs(options.Quantity - options.StatementQuantity));
        scenario.ExpectedBreak.EvidenceIds.Should().Contain(id => id.Contains(".ledger.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ProviderGoldenPath_HighTickPressureIngestion_GeneratedTicksRemainOrderedAndLedgerInvariant()
    {
        const int eventCount = 5_000;
        var scenario = ProviderGoldenPathScenarioGenerator.Create(new ProviderGoldenPathScenarioOptions(
            ProviderId: "alpaca",
            Symbol: "AAPL",
            Quantity: 1_000m,
            AveragePrice: 190.10m,
            Commission: 0m,
            StatementQuantity: 999m,
            FillSplits: [100m, 150m, 250m, 500m],
            Start: DateTimeOffset.Parse("2026-04-11T14:30:00Z")));
        var events = ProviderGoldenPathScenarioGenerator.GenerateMarketEvents(scenario, eventCount);
        var sink = new InMemoryStorageSink();

        await using var pipeline = new EventPipeline(
            sink,
            capacity: eventCount + 128,
            batchSize: 512,
            enablePeriodicFlush: false);

        foreach (var marketEvent in events)
        {
            var publishable = marketEvent;
            pipeline.TryPublish(in publishable).Should().BeTrue();
        }

        await pipeline.FlushAsync();

        sink.StoredEvents.Should().HaveCount(eventCount);
        sink.StoredEvents.Select(evt => evt.Symbol).Distinct().Should().Equal("AAPL");
        sink.StoredEvents.Select(evt => evt.Source).Distinct().Should().Equal("alpaca");
        sink.StoredEvents.Count(evt => evt.Type == MarketEventType.Trade).Should().Be(eventCount / 2);
        sink.StoredEvents.Count(evt => evt.Type == MarketEventType.BboQuote).Should().Be(eventCount / 2);
        sink.StoredEvents.Select(evt => evt.Sequence).OrderBy(sequence => sequence).Should().Equal(Enumerable.Range(1, eventCount).Select(i => (long)i));

        var ledger = ProviderGoldenPathScenarioGenerator.ReplayLedger(scenario);
        ledger.Journal.Single().Lines.Sum(line => line.Debit).Should().Be(ledger.Journal.Single().Lines.Sum(line => line.Credit));
        scenario.ExpectedBreak.Materiality.Should().Be(1m);
    }

    private static void AssertExecutionLifecycle(JsonElement root)
    {
        var lifecycle = root.GetProperty("executionLifecycle");
        var finalQuantity = lifecycle.GetProperty("finalQuantity").GetDecimal();
        var submittedQuantity = lifecycle.GetProperty("submittedQuantity").GetDecimal();
        var fills = lifecycle.GetProperty("fills").EnumerateArray().ToList();
        var states = lifecycle.GetProperty("states").EnumerateArray().ToList();

        submittedQuantity.Should().Be(finalQuantity);
        fills.Sum(fill => fill.GetProperty("quantity").GetDecimal()).Should().Be(finalQuantity);
        states.Select(state => state.GetProperty("state").GetString()).Should().Contain("partial_fill");
        states.Last().GetProperty("state").GetString().Should().Be("fill");
        states.Last().GetProperty("filledQuantity").GetDecimal().Should().Be(finalQuantity);
    }

    private static LedgerBook ReplayExpectedLedger(JsonElement root)
    {
        var expectedLedger = root.GetProperty("expectedLedger");
        var lines = expectedLedger.GetProperty("lines").EnumerateArray().Select(line =>
        {
            var accountName = line.GetProperty("account").GetString()!;
            var accountType = Enum.Parse<LedgerAccountType>(line.GetProperty("accountType").GetString()!, ignoreCase: true);
            var symbol = line.GetProperty("symbol").ValueKind == JsonValueKind.Null ? null : line.GetProperty("symbol").GetString();
            return (
                account: new LedgerAccount(accountName, accountType, symbol),
                debit: line.GetProperty("debit").GetDecimal(),
                credit: line.GetProperty("credit").GetDecimal());
        }).ToList();

        var debitTotal = lines.Sum(line => line.debit);
        var creditTotal = lines.Sum(line => line.credit);
        debitTotal.Should().Be(expectedLedger.GetProperty("debitTotal").GetDecimal());
        creditTotal.Should().Be(expectedLedger.GetProperty("creditTotal").GetDecimal());
        debitTotal.Should().Be(creditTotal);

        var ledger = new LedgerBook();
        ledger.PostLines(
            DateTimeOffset.Parse(expectedLedger.GetProperty("timestamp").GetString()!),
            expectedLedger.GetProperty("description").GetString()!,
            lines);

        ledger.JournalEntryCount.Should().Be(1);
        ledger.TotalLedgerEntryCount.Should().Be(lines.Count);
        ledger.Journal.Single().Lines.Sum(line => line.Debit).Should().Be(ledger.Journal.Single().Lines.Sum(line => line.Credit));
        return ledger;
    }

    private static void AssertPortfolioAndBreak(JsonElement root, LedgerBook ledger)
    {
        var expectedLedger = root.GetProperty("expectedLedger");
        var portfolio = expectedLedger.GetProperty("portfolio");
        var symbol = portfolio.GetProperty("symbol").GetString()!;
        var expectedQuantity = portfolio.GetProperty("quantity").GetDecimal();
        var securityNotional = ledger.GetBalance(new LedgerAccount("Securities", LedgerAccountType.Asset, symbol));

        expectedQuantity.Should().Be(100m);
        securityNotional.Should().BeGreaterThan(0m);

        var statementPosition = root.GetProperty("statementRows")
            .EnumerateArray()
            .Single(row => row.GetProperty("rowType").GetString() == "position");
        statementPosition.GetProperty("quantity").GetDecimal().Should().NotBe(expectedQuantity);

        var breakElement = root.GetProperty("expectedBreaks").EnumerateArray().Single();
        breakElement.GetProperty("category").GetString().Should().Be("PositionQuantityMismatch");
        breakElement.GetProperty("severity").GetString().Should().Be("High");
        breakElement.GetProperty("expectedQuantity").GetDecimal().Should().Be(expectedQuantity);
        breakElement.GetProperty("actualQuantity").GetDecimal().Should().Be(statementPosition.GetProperty("quantity").GetDecimal());
        breakElement.GetProperty("materiality").GetDecimal().Should().Be(Math.Abs(expectedQuantity - statementPosition.GetProperty("quantity").GetDecimal()));
        breakElement.GetProperty("evidenceIds").EnumerateArray().Select(e => e.GetString()).Should().Contain(id => id!.StartsWith("ledger.", StringComparison.Ordinal));
    }

    private static void AssertIbkrOrderCallbacksPreserveMonotonicity(JsonElement root)
    {
        var callbacks = root.GetProperty("providerMessages").GetProperty("orderCallbacks").EnumerateArray().ToList();
        callbacks.First().GetProperty("callback").GetString().Should().Be("nextValidId");
        var nextValidId = callbacks.First().GetProperty("orderId").GetInt64();
        callbacks.Single(callback => callback.TryGetProperty("method", out var method) && method.GetString() == "EClient.placeOrder")
            .GetProperty("orderId")
            .GetInt64()
            .Should()
            .Be(nextValidId);

        var sequences = callbacks.Select(callback => callback.GetProperty("eventSequence").GetInt32()).ToList();
        sequences.Should().BeInAscendingOrder();
        sequences.Distinct().Should().HaveCount(sequences.Count);

        var orderIds = callbacks.Where(callback => callback.TryGetProperty("orderId", out _)).Select(callback => callback.GetProperty("orderId").GetInt64()).Distinct().ToList();
        orderIds.Should().ContainSingle().Which.Should().Be(nextValidId);
    }

    private static void AssertEvidenceIdsAreExplicit(JsonElement evidence)
    {
        foreach (var property in evidence.EnumerateObject())
            property.Value.GetString().Should().NotBeNullOrWhiteSpace($"expectedEvidence.{property.Name} must be explicit");
    }

    private static JsonDocument LoadProviderFixture(string provider)
    {
        var fixtures = ProviderGoldenPathFixtures()
            .Select(args => (string)args[0])
            .Where(path =>
            {
                using var document = LoadFixture(path);
                return document.RootElement.GetProperty("metadata").GetProperty("provider").GetString() == provider;
            })
            .ToList();

        fixtures.Should().ContainSingle($"there should be one {provider} provider golden-path fixture");
        return LoadFixture(fixtures.Single());
    }

    private static JsonDocument LoadFixture(string fixturePath)
    {
        return JsonDocument.Parse(File.ReadAllText(fixturePath));
    }

    private static string ResolveFixturesRoot()
    {
        var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        var candidate = new DirectoryInfo(assemblyDir);

        while (candidate is not null && !File.Exists(Path.Combine(candidate.FullName, "Meridian.sln")))
            candidate = candidate.Parent;

        var root = Path.Combine(
            candidate?.FullName ?? assemblyDir,
            "tests",
            "Meridian.Tests",
            "Infrastructure",
            "Providers",
            "Fixtures");

        Directory.Exists(root).Should().BeTrue("provider fixtures directory must exist");
        return root;
    }
}
