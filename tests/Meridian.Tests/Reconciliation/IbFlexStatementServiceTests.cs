using FluentAssertions;
using Meridian.Domain.Reconciliation;
using Meridian.FinancialOperations.Reconciliation;
using Meridian.Infrastructure.Reconciliation;
using Xunit;

namespace Meridian.Tests.Reconciliation;

public sealed class IbFlexStatementServiceTests : IDisposable
{
    private const string SampleFlexXml = """
        <FlexQueryResponse queryName="MeridianDaily" type="AF">
          <FlexStatements count="1">
            <FlexStatement accountId="U1234567" fromDate="20260601" toDate="20260630" period="Month" whenGenerated="20260701;043000">
              <Trades>
                <Trade accountId="U1234567" symbol="AAPL" tradeDate="20260615" quantity="100" tradePrice="201.35" netCash="-20136.00" ibCommission="-1.00" currency="USD" buySell="BUY" tradeID="1000001" assetCategory="STK" />
                <Trade accountId="U1234567" symbol="MSFT" tradeDate="2026-06-16" quantity="-50" tradePrice="500.10" proceeds="25005.00" ibCommission="-1.25" currency="USD" buySell="SELL" tradeID="1000002" assetCategory="STK" />
              </Trades>
              <OpenPositions>
                <OpenPosition accountId="U1234567" symbol="AAPL" position="100" markPrice="205.10" positionValue="20510" reportDate="20260630" currency="USD" />
              </OpenPositions>
              <CashTransactions>
                <CashTransaction accountId="U1234567" symbol="AAPL" amount="24.00" type="Dividends" dateTime="20260610;120000" currency="USD" description="AAPL CASH DIVIDEND" />
                <CashTransaction accountId="U1234567" symbol="" amount="-3.50" type="Other Fees" reportDate="20260630" currency="USD" description="MARKET DATA FEE" />
              </CashTransactions>
            </FlexStatement>
          </FlexStatements>
        </FlexQueryResponse>
        """;

    private readonly string _tempDir;
    private readonly JsonCanonicalStatementStore _store;
    private readonly IbFlexBrokerStatementService _service;

    public IbFlexStatementServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"meridian-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _store = new JsonCanonicalStatementStore(_tempDir);
        _service = new IbFlexBrokerStatementService(_store);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempDir, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort cleanup of the per-test temp directory.
        }
    }

    private string WriteFlexFile(string content, string fileName = "flex-report.xml")
    {
        var path = Path.Combine(_tempDir, fileName);
        File.WriteAllText(path, content);
        return path;
    }

    private static BrokerStatementImportRequest MakeRequest(string path) =>
        new BrokerStatementImportRequest("ib-flex", path, new DateOnly(2026, 6, 30)) with
        {
            FundAccountId = "FUND-1",
            ExternalAccountId = "U1234567",
            StatementPeriodStart = new DateOnly(2026, 6, 1),
            StatementPeriodEnd = new DateOnly(2026, 6, 30)
        };

    [Fact]
    public async Task Validate_WellFormedFlexReport_CountsAllSections()
    {
        var path = WriteFlexFile(SampleFlexXml);

        var result = await _service.ValidateAsync(MakeRequest(path));

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
        result.RowCount.Should().Be(5); // 2 trades + 1 position + 2 cash
    }

    [Fact]
    public async Task Validate_MissingFile_Fails()
    {
        var result = await _service.ValidateAsync(MakeRequest(Path.Combine(_tempDir, "missing.xml")));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(static e => e.Contains("not found"));
    }

    [Fact]
    public async Task Validate_NonFlexXml_Fails()
    {
        var path = WriteFlexFile("<SomethingElse><Row /></SomethingElse>");

        var result = await _service.ValidateAsync(MakeRequest(path));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(static e => e.Contains("FlexQueryResponse"));
    }

    [Fact]
    public async Task Validate_MalformedXml_Fails()
    {
        var path = WriteFlexFile("<FlexQueryResponse><Unclosed>");

        var result = await _service.ValidateAsync(MakeRequest(path));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(static e => e.Contains("well-formed"));
    }

    [Fact]
    public async Task Import_MapsTradesPositionsAndCashToCanonicalRows()
    {
        var path = WriteFlexFile(SampleFlexXml);

        var result = await _service.ImportAsync(MakeRequest(path));

        result.Rows.Should().HaveCount(5);
        result.Import.NormalizedRowCount.Should().Be(5);

        var buy = result.Rows[0];
        buy.ActivityType.Should().Be("trade");
        buy.Account.Should().Be("U1234567");
        buy.Symbol.Should().Be("AAPL");
        buy.Quantity.Should().Be(100m);
        buy.Price.Should().Be(201.35m);
        buy.CashAmount.Should().Be(-20136.00m); // netCash preferred
        buy.TradeDate.Should().Be(new DateOnly(2026, 6, 15)); // yyyyMMdd format

        var sell = result.Rows[1];
        sell.Quantity.Should().Be(-50m);
        sell.CashAmount.Should().Be(25005.00m); // proceeds fallback
        sell.TradeDate.Should().Be(new DateOnly(2026, 6, 16)); // yyyy-MM-dd format

        var position = result.Rows[2];
        position.ActivityType.Should().Be("position");
        position.Quantity.Should().Be(100m);
        position.Price.Should().Be(205.10m);
        position.CashAmount.Should().Be(0m);
        position.TradeDate.Should().Be(new DateOnly(2026, 6, 30));

        var dividend = result.Rows[3];
        dividend.ActivityType.Should().Be("cash");
        dividend.CashAmount.Should().Be(24.00m);
        dividend.TradeDate.Should().Be(new DateOnly(2026, 6, 10)); // date part of dateTime

        var fee = result.Rows[4];
        fee.ActivityType.Should().Be("fee"); // "Other Fees" keeps fee semantics
        fee.CashAmount.Should().Be(-3.50m);
        fee.TradeDate.Should().Be(new DateOnly(2026, 6, 30)); // reportDate fallback
    }

    [Fact]
    public async Task Import_AssignsUniqueRowChecksums()
    {
        var path = WriteFlexFile(SampleFlexXml);

        var result = await _service.ImportAsync(MakeRequest(path));

        result.Rows.Select(static r => r.RawChecksum).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task Import_FlexReportWithNoSupportedRows_Throws()
    {
        var path = WriteFlexFile("""
            <FlexQueryResponse queryName="Empty" type="AF">
              <FlexStatements count="1">
                <FlexStatement accountId="U1" fromDate="20260601" toDate="20260630" />
              </FlexStatements>
            </FlexQueryResponse>
            """);

        var import = async () => await _service.ImportAsync(MakeRequest(path));

        await import.Should().ThrowAsync<InvalidDataException>()
            .WithMessage("*no Trade, OpenPosition, or CashTransaction rows*");
    }

    [Fact]
    public async Task Import_SameFileTwice_ThrowsDuplicate()
    {
        var path = WriteFlexFile(SampleFlexXml);

        await _service.ImportAsync(MakeRequest(path));
        var secondImport = async () => await _service.ImportAsync(MakeRequest(path));

        await secondImport.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*already imported*");
    }

    [Fact]
    public async Task Import_ProducesCanonicalRowsWithCurrencyAndExternalIds()
    {
        var path = WriteFlexFile(SampleFlexXml);
        var imported = await _service.ImportAsync(MakeRequest(path));

        imported.Rows.Should().HaveCount(5);
        // Rows are emitted trades first, then open positions, then cash transactions.
        imported.Rows[2].ActivityType.Should().Be("position");
        imported.Rows[2].Symbol.Should().Be("AAPL");
        // Currency and the broker transaction identifier now flow through to the canonical row so
        // the reconciliation engine can normalize FX and match on external id.
        imported.Rows.Should().OnlyContain(row => row.Currency == "USD");
        imported.Rows[0].ExternalTransactionId.Should().Be("1000001");
    }

    [Theory]
    [InlineData("ib-flex")]
    [InlineData("ibflex")]
    [InlineData("IBKR")]
    [InlineData("InteractiveBrokers")]
    [InlineData("interactive-brokers")]
    public void IsIbFlexSource_RecognizesAliases(string broker)
    {
        IbFlexBrokerStatementService.IsIbFlexSource(broker).Should().BeTrue();
    }

    [Theory]
    [InlineData("samplebroker")]
    [InlineData("custodian")]
    [InlineData(null)]
    public void IsIbFlexSource_RejectsOtherSources(string? broker)
    {
        IbFlexBrokerStatementService.IsIbFlexSource(broker).Should().BeFalse();
    }

    [Fact]
    public async Task Router_SendsFlexBrokerToFlexService_AndCsvBrokerToCsvService()
    {
        var router = new RoutingBrokerStatementService(
            new CsvBrokerStatementService(_store),
            _service);

        // Flex broker alias with a Flex file → validates via the XML parser.
        var flexPath = WriteFlexFile(SampleFlexXml);
        var flexResult = await router.ValidateAsync(MakeRequest(flexPath));
        flexResult.IsValid.Should().BeTrue();
        flexResult.RowCount.Should().Be(5);

        // CSV broker with a CSV file → validates via the CSV parser.
        var csvPath = Path.Combine(_tempDir, "statement.csv");
        File.WriteAllText(csvPath, "account,symbol,quantity,price,cashAmount,activityType,tradeDate\nA1,SPY,10,500,0,position,2026-06-30\n");
        var csvRequest = new BrokerStatementImportRequest("samplebroker", csvPath, new DateOnly(2026, 6, 30));
        var csvResult = await router.ValidateAsync(csvRequest);
        csvResult.IsValid.Should().BeTrue();
        csvResult.RowCount.Should().Be(1);
    }

    [Fact]
    public async Task Router_RoutesGenericBrokerWithXmlExtensionToFlexService()
    {
        var router = new RoutingBrokerStatementService(
            new CsvBrokerStatementService(_store),
            _service);
        var path = WriteFlexFile(SampleFlexXml);
        var request = new BrokerStatementImportRequest("broker", path, new DateOnly(2026, 6, 30));

        var result = await router.ValidateAsync(request);

        result.IsValid.Should().BeTrue();
        result.RowCount.Should().Be(5);
    }

    [Fact]
    public async Task StatementReconciliationService_ValidatesAndIngestsFlexSourceKind()
    {
        var service = new StatementReconciliationService();
        var path = WriteFlexFile(SampleFlexXml);

        // Validation must accept the flex source kind instead of rejecting it or applying
        // CSV header checks, even when a mapping profile is supplied.
        var message = await service.ValidateAsync("ib-flex", path, StatementMappingProfileRegistry.IbFlexV1ProfileId, CancellationToken.None);
        message.Should().Contain("ib-flex");

        var import = await service.ImportAsync("IBKR", path, StatementMappingProfileRegistry.IbFlexV1ProfileId, CancellationToken.None);
        import.SourceKind.Should().Be("ib-flex");
        import.RowCount.Should().Be(5);
        import.SourceRows.Should().HaveCount(5);
        import.SourceRows[0].RawSnapshot.Should().ContainKey("symbol");
    }

    [Fact]
    public async Task StatementReconciliationService_CaseIntakeMatchesFlexRows()
    {
        var service = new StatementReconciliationService();
        var path = WriteFlexFile(SampleFlexXml);

        var intake = await service.CreateExternalStatementCasesAsync("ib-flex", path, CancellationToken.None);

        // Intake must process real Flex rows, not short-circuit to zero counts.
        intake.RowCount.Should().Be(5);
        (intake.MatchCount + intake.Cases.Count).Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task StatementReconciliationService_RejectsNonFlexXmlForFlexSourceKind()
    {
        var service = new StatementReconciliationService();
        var path = WriteFlexFile("<NotAFlexReport />", "other.xml");

        var validate = async () => await service.ValidateAsync("ib-flex", path, null, CancellationToken.None);

        await validate.Should().ThrowAsync<InvalidDataException>().WithMessage("*not an IB Flex Query report*");
    }

    [Fact]
    public async Task StatementRunWorkflow_ImportsFlexStatementEndToEnd()
    {
        var workflow = new StatementRunWorkflowService(
            _store,
            new JsonReconciliationCaseStore(_tempDir),
            new JsonReconciliationBreakStore(_tempDir),
            new RoutingBrokerStatementService(new CsvBrokerStatementService(_store), _service),
            new StatementReconciliationContextAdapter(new StatementReconciliationService()));
        var path = WriteFlexFile(SampleFlexXml);
        var request = new StatementRunRequest(
            Broker: "ib-flex",
            SourceInstitution: "Interactive Brokers",
            FundAccountId: "FUND-1",
            ExternalAccountId: "U1234567",
            StatementPeriodStart: new DateOnly(2026, 6, 1),
            StatementPeriodEnd: new DateOnly(2026, 6, 30),
            SourcePath: path,
            OriginalFileName: "flex-report.xml",
            MappingProfileId: StatementMappingProfileRegistry.IbFlexV1ProfileId,
            ToleranceProfileId: "statement-default",
            ImportedBy: "test",
            SourceFileHash: string.Empty);

        var result = await workflow.CreateAsync(request);

        result.Import.NormalizedRowCount.Should().Be(5);
        result.Import.Broker.Should().Be("ib-flex");
    }

    [Fact]
    public async Task StatementReconciliationService_RoutesGenericBrokerXmlThroughFlexValidation()
    {
        var service = new StatementReconciliationService();
        var path = WriteFlexFile(SampleFlexXml);

        // A canonical 'broker' kind with a Flex .xml file must validate as Flex, matching where
        // RoutingBrokerStatementService will send the import — not fail CSV header checks.
        var message = await service.ValidateAsync("broker", path, null, CancellationToken.None);
        message.Should().Contain("broker");

        var import = await service.ImportAsync("broker", path, null, CancellationToken.None);
        import.RowCount.Should().Be(5);
    }

    [Fact]
    public async Task StatementRunWorkflow_ImportsFlexStatementWithGenericBrokerKind()
    {
        var workflow = new StatementRunWorkflowService(
            _store,
            new JsonReconciliationCaseStore(_tempDir),
            new JsonReconciliationBreakStore(_tempDir),
            new RoutingBrokerStatementService(new CsvBrokerStatementService(_store), _service),
            new StatementReconciliationContextAdapter(new StatementReconciliationService()));
        var path = WriteFlexFile(SampleFlexXml, "generic-broker-report.xml");
        var request = new StatementRunRequest(
            Broker: "broker",
            SourceInstitution: "Interactive Brokers",
            FundAccountId: "FUND-2",
            ExternalAccountId: "U1234567",
            StatementPeriodStart: new DateOnly(2026, 6, 1),
            StatementPeriodEnd: new DateOnly(2026, 6, 30),
            SourcePath: path,
            OriginalFileName: "generic-broker-report.xml",
            MappingProfileId: string.Empty,
            ToleranceProfileId: "statement-default",
            ImportedBy: "test",
            SourceFileHash: string.Empty);

        var result = await workflow.CreateAsync(request);

        result.Import.NormalizedRowCount.Should().Be(5);
    }

    [Fact]
    public void MappingProfileRegistry_RegistersIbFlexProfile()
    {
        var registry = StatementMappingProfileRegistry.Defaults;

        var profile = registry.Resolve(StatementMappingProfileRegistry.IbFlexV1ProfileId);
        profile.DisplayName.Should().Contain("Interactive Brokers");
        profile.MapActivityType("Dividends").Should().Be("cash");
        profile.MapActivityType("BUY").Should().Be("trade");

        registry.ResolveForSourceKind("ib-flex").ProfileId
            .Should().Be(StatementMappingProfileRegistry.IbFlexV1ProfileId);
        registry.ListProfiles().Should().Contain(static p =>
            p.ProfileId == StatementMappingProfileRegistry.IbFlexV1ProfileId);
    }
}
