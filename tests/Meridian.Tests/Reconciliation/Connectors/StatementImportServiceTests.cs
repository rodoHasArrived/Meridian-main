using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.Domain.Reconciliation;
using Meridian.FinancialOperations.Reconciliation;
using Meridian.FinancialOperations.Reconciliation.Connectors;
using Meridian.FinancialOperations.Reconciliation.Connectors.Alpaca;
using Meridian.FinancialOperations.Reconciliation.Connectors.IbFlex;
using Meridian.FinancialOperations.Reconciliation.Connectors.Ofx;
using Meridian.Infrastructure.Reconciliation;
using Xunit;

namespace Meridian.Tests.Reconciliation.Connectors;

/// <summary>
/// End-to-end coverage for the statement connector import pipeline: preview (mapping
/// confidence, per-kind breakdown, suggestions, drift), commit through the real statement-run
/// workflow into breaks and reconciliation cases, duplicate idempotency, deterministic
/// canonical artifacts, and scheduled fetch execution.
/// </summary>
public sealed class StatementImportServiceTests : IDisposable
{
    private readonly string _root = StatementConnectorTestData.CreateTempRoot("mdc_stmt_import_svc");
    private readonly StatementMappingProfileCatalog _catalog;
    private readonly StatementConnectorRegistry _registry;
    private readonly FakeFetchingConnector _fetchingConnector;
    private readonly StatementImportService _service;
    private readonly IStatementRunWorkflowService _workflow;

    public StatementImportServiceTests()
    {
        _catalog = new StatementMappingProfileCatalog(new FileStatementMappingProfileStore(_root));
        _fetchingConnector = new FakeFetchingConnector();
        _registry = new StatementConnectorRegistry(
        [
            new CsvStatementConnector(_catalog),
            new OfxStatementConnector(_catalog),
            new IbFlexStatementConnector(_catalog),
            new AlpacaActivityStatementConnector(_catalog, [], []),
            _fetchingConnector
        ]);

        var statementStore = new JsonCanonicalStatementStore(_root);
        _workflow = StatementRunWorkflowService.CreateEphemeralForTesting(
            statementStore,
            new JsonReconciliationCaseStore(_root),
            new JsonReconciliationBreakStore(_root),
            new CsvBrokerStatementService(statementStore),
            new StatementReconciliationContextAdapter(new StatementReconciliationService()));
        _service = new StatementImportService(_registry, _catalog, _workflow, _root);
    }

    private static StatementSourceDocument FixtureDocument(string fileName, string? mappingProfileId = null)
        => new(fileName, StatementConnectorTestData.ReadFixture(fileName), mappingProfileId);

    private StatementImportCommitRequest CommitRequest(
        StatementSourceDocument document,
        string fundAccountId = "FUND-A",
        string externalAccountId = "FUND-A",
        string? connectorId = null)
        => new(
            document,
            connectorId,
            SourceKind: "broker",
            SourceInstitution: "Sample Broker",
            FundAccountId: fundAccountId,
            ExternalAccountId: externalAccountId,
            PeriodStart: new DateOnly(2026, 6, 1),
            PeriodEnd: new DateOnly(2026, 6, 30),
            ToleranceProfileId: null,
            ImportedBy: "test-operator");

    [Fact]
    public async Task Preview_MixedCsv_ReportsMappingsKindBreakdownAndSuggestions()
    {
        var preview = await _service.PreviewAsync(FixtureDocument("csv-mixed-kinds.csv"), connectorId: null);

        preview.ConnectorId.Should().Be(CsvStatementConnector.ConnectorId, "auto-detection picks the CSV catch-all");
        preview.Status.Should().Be("ReadyToImport");
        preview.RecordCount.Should().Be(6);
        preview.KindSummaries.Should().HaveCount(5);
        preview.KindSummaries.Select(summary => summary.Kind).Should().BeEquivalentTo(
            "Position", "CashBalance", "Transaction", "Fee", "Dividend");
        preview.KindSummaries.Single(summary => summary.Kind == "Transaction").RecordCount.Should().Be(2);
        preview.KindSummaries.Single(summary => summary.Kind == "Transaction").SampleRecords.Should().NotBeEmpty();
        preview.ColumnMappings.Should().OnlyContain(mapping => mapping.Confidence == StatementColumnConfidenceDto.Exact);
        preview.ProfileSuggestions.Should().NotBeEmpty();
        preview.ProfileSuggestions[0].ProfileId.Should().Be(StatementMappingProfileRegistry.CanonicalCsvV1ProfileId);
        preview.ProfileSuggestions[0].Score.Should().Be(1.0m);
    }

    [Fact]
    public async Task Preview_FlexXml_AutoDetectsIbFlexConnector()
    {
        var preview = await _service.PreviewAsync(FixtureDocument("ib-flex-sample.xml"), connectorId: null);

        preview.ConnectorId.Should().Be(IbFlexStatementConnector.ConnectorId);
        preview.RecordCount.Should().Be(7);
    }

    [Fact]
    public async Task Preview_UnknownConnectorId_ReportsConnectorNotFound()
    {
        var preview = await _service.PreviewAsync(FixtureDocument("csv-mixed-kinds.csv"), connectorId: "nope");

        preview.Status.Should().Be("NeedsAttention");
        preview.Issues.Should().Contain(issue => issue.Code == "CONNECTOR_NOT_FOUND");
    }

    [Fact]
    public async Task Preview_DriftedLayout_SurfacesFormatDriftWarning()
    {
        var canonical = StatementBuiltInProfiles.All.Single(profile =>
            profile.ProfileId == StatementMappingProfileRegistry.CanonicalCsvV1ProfileId);
        await _catalog.UpsertAsync(canonical with { ProfileId = "acme-clone-v1", IsBuiltIn = false });

        var first = await _service.CommitAsync(CommitRequest(FixtureDocument("csv-mixed-kinds.csv", "acme-clone-v1")));
        first.Duplicate.Should().BeFalse();

        var preview = await _service.PreviewAsync(FixtureDocument("csv-drifted-headers.csv", "acme-clone-v1"), connectorId: null);

        preview.Issues.Should().Contain(issue =>
            issue.Code == "FORMAT_DRIFT" && issue.Severity == StatementParseIssue.WarningSeverity);
    }

    [Fact]
    public async Task Commit_MixedCsv_CreatesRunBreaksAndReconciliationQueueCases()
    {
        var result = await _service.CommitAsync(CommitRequest(FixtureDocument("csv-mixed-kinds.csv")));

        result.Duplicate.Should().BeFalse();
        result.RunId.Should().NotBeNullOrWhiteSpace();
        result.RecordCount.Should().Be(6);
        result.KindSummaries.Should().HaveCount(5);
        // The matcher now reconciles against Meridian's own book. This test wires no internal
        // populations, so every one of the 6 statement rows is correctly unmatched — each becomes a
        // break and a queue case, instead of the old self-matcher fabricating position/near-zero matches.
        result.BreakCount.Should().Be(6);
        result.CaseCount.Should().Be(6);
        result.BreakIds.Should().HaveCount(6);
        result.CaseIds.Should().HaveCount(6);
        result.CaseIds.Should().OnlyContain(caseId => caseId.StartsWith("case:", StringComparison.OrdinalIgnoreCase));
        result.ReconciliationCaseRoutes.Should().HaveCount(6);
        result.ReconciliationCaseRoutes.Should().OnlyContain(route =>
            route.StartsWith($"/accounting/reconciliation/match?runId={Uri.EscapeDataString(result.RunId)}&caseId=", StringComparison.OrdinalIgnoreCase) &&
            route.Contains("&breakId=", StringComparison.OrdinalIgnoreCase));
        result.ReconciliationCaseLinks.Should().HaveCount(6);
        result.CaseIds.Should().Equal(result.ReconciliationCaseLinks.Select(link => link.CaseId));
        result.ReconciliationCaseRoutes.Should().Equal(result.ReconciliationCaseLinks.Select(link => link.Route));
        result.ReconciliationCaseLinks.Should().OnlyContain(link =>
            link.CaseId.StartsWith("case:", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(link.BreakId) &&
            link.Route.Contains($"caseId={Uri.EscapeDataString(link.CaseId)}", StringComparison.OrdinalIgnoreCase) &&
            link.Route.Contains($"breakId={Uri.EscapeDataString(link.BreakId!)}", StringComparison.OrdinalIgnoreCase) &&
            link.Status == "Open" &&
            !string.IsNullOrWhiteSpace(link.Priority) &&
            !string.IsNullOrWhiteSpace(link.Reason) &&
            !string.IsNullOrWhiteSpace(link.SuggestedNextAction));
        result.Status.Should().Be("Imported");

        var run = await _workflow.GetAsync(result.RunId);
        run.Should().NotBeNull();
        run!.Import.NormalizedRowCount.Should().Be(6);
        var rawPath = Path.Combine(_root, result.RetainedSourcePath);
        var canonicalPath = Path.Combine(_root, result.RetainedCanonicalPath);
        run.Import.SourceFileHash.Should().Be(
            Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(rawPath))),
            "the retained source hash is authoritative evidence for the original upload bytes");
        run.Import.CanonicalArtifactHash.Should().Be(
            Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(canonicalPath))),
            "the parse artifact hash is retained separately from the original upload hash");
        run.Cases.Should().HaveCount(6);
        run.Cases.Should().OnlyContain(reconciliationCase => reconciliationCase.Status == "Open");

        File.Exists(Path.Combine(_root, result.RetainedSourcePath)).Should().BeTrue();
        File.Exists(Path.Combine(_root, result.RetainedCanonicalPath)).Should().BeTrue();
        result.RetainedCanonicalEvidencePath.Should().NotBeNull();
        File.Exists(Path.Combine(_root, result.RetainedCanonicalEvidencePath!)).Should().BeTrue();
    }

    [Fact]
    public async Task Commit_MixedParsedAccounts_FailsBeforeEvidenceRetentionOrMatching()
    {
        const string source =
            "account,symbol,quantity,price,cashAmount,activityType,tradeDate\n" +
            "FUND-A,AAPL,1,100,-100,trade,2026-06-02\n" +
            "FUND-B,MSFT,1,200,-200,trade,2026-06-03\n";
        var document = new StatementSourceDocument(
            "mixed-accounts.csv",
            Encoding.UTF8.GetBytes(source),
            ExternalAccountId: "FUND-A");

        var commit = () => _service.CommitAsync(CommitRequest(document));

        await commit.Should()
            .ThrowAsync<InvalidDataException>()
            .WithMessage("*parsed account identity*authorized external account*");
        Directory.Exists(Path.Combine(_root, "reconciliation", "statement-connector-imports"))
            .Should()
            .BeFalse("conflicting account rows must fail before raw or canonical evidence is retained");
        (await _workflow.ListImportsAsync()).Should().BeEmpty(
            "conflicting account rows must fail before matching or casework begins");
    }

    [Fact]
    public async Task Commit_ConnectorOmitsParsedAccount_FailsBeforeEvidenceRetentionOrMatching()
    {
        var connector = new FixedRecordConnector(
            new StatementCanonicalRecord(
                StatementRecordKind.Transaction,
                Account: string.Empty,
                Symbol: "AAPL",
                Quantity: 1m,
                Price: 100m,
                CashAmount: -100m,
                ActivityType: "trade",
                TradeDate: new DateOnly(2026, 6, 2)));
        var service = new StatementImportService(
            new StatementConnectorRegistry([connector]),
            _catalog,
            _workflow,
            _root);
        var document = new StatementSourceDocument(
            "missing-account.fixed",
            "fixed"u8.ToArray(),
            ExternalAccountId: "FUND-A");

        var commit = () => service.CommitAsync(
            CommitRequest(document, connectorId: connector.Descriptor.ConnectorId));

        await commit.Should()
            .ThrowAsync<InvalidDataException>()
            .WithMessage("*missing or conflicting parsed account identity*");
        Directory.Exists(Path.Combine(_root, "reconciliation", "statement-connector-imports"))
            .Should()
            .BeFalse("missing account identity must fail before raw or canonical evidence is retained");
        (await _workflow.ListImportsAsync()).Should().BeEmpty(
            "missing account identity must fail before matching or casework begins");
    }

    [Fact]
    public async Task Commit_CanonicalArtifactQuotesDelimiterAndQuoteCharactersWithoutCollisions()
    {
        const string source =
            "account,symbol,quantity,price,cashAmount,activityType,tradeDate,settlementDate,currency,feesCommission,externalTransactionId\n" +
            "FUND-A,\"BRK,B\",1,500,-500,trade,2026-06-02,,USD,,\"TX,\"\"1\"\"\"\n";
        var document = new StatementSourceDocument("quoted-values.csv", Encoding.UTF8.GetBytes(source));

        var result = await _service.CommitAsync(CommitRequest(document));

        var canonicalPath = Path.Combine(_root, result.RetainedCanonicalPath);
        var artifact = await File.ReadAllTextAsync(canonicalPath);
        artifact.Should().Contain("\"BRK,B\"");
        artifact.Should().Contain("\"TX,\"\"1\"\"\"");
        var run = await _workflow.GetAsync(result.RunId);
        run.Should().NotBeNull();
        run!.Import.NormalizedRowCount.Should().Be(1);
    }

    [Fact]
    public async Task Commit_RendersDeterministicCanonicalArtifact()
    {
        var result = await _service.CommitAsync(CommitRequest(FixtureDocument("csv-mixed-kinds.csv")));

        var artifact = await File.ReadAllTextAsync(Path.Combine(_root, result.RetainedCanonicalPath));
        artifact.Should().Be(
            "account,symbol,quantity,price,cashAmount,activityType,tradeDate,settlementDate,currency,feesCommission,externalTransactionId,activityCategory,activitySubtype,providerActivityCode,relatedTransactionId,orderId,description\n" +
            "FUND-A,AAPL,100,187.25,-18726.05,trade,2026-06-02,2026-06-04,USD,1.05,T-1001,,,,,,\n" +
            "FUND-A,MSFT,-50,412.10,20603.98,trade,2026-06-15,2026-06-17,USD,1.02,T-1002,,,,,,\n" +
            "FUND-A,AAPL,100,190.10,19010.00,position,2026-06-30,,USD,,,,,,,,\n" +
            "FUND-A,,0,0,31247.93,cash,2026-06-30,,USD,,,,,,,,\n" +
            "FUND-A,,0,0,-25.00,fee,2026-06-28,,USD,,F-9001,,,,,,\n" +
            "FUND-A,AAPL,0,0,24.00,dividend,2026-06-10,,USD,,D-7001,,,,,,\n");
    }

    [Fact]
    public async Task Commit_SourceFileNamedCanonicalCsv_RetainsRawAndCanonicalSeparately()
    {
        // A source file literally named "canonical.csv" must not collide with the rendered canonical
        // artifact. The raw evidence is retained under its own subdirectory, so neither file overwrites
        // the other and the original source bytes survive intact.
        var rawContent = StatementConnectorTestData.ReadFixture("csv-mixed-kinds.csv");
        var document = new StatementSourceDocument("canonical.csv", rawContent);

        var result = await _service.CommitAsync(CommitRequest(document));

        var rawPath = Path.Combine(_root, result.RetainedSourcePath);
        var canonicalPath = Path.Combine(_root, result.RetainedCanonicalPath);
        File.Exists(rawPath).Should().BeTrue();
        File.Exists(canonicalPath).Should().BeTrue();
        Path.GetFullPath(rawPath).Should().NotBe(
            Path.GetFullPath(canonicalPath),
            "the raw source and the rendered canonical artifact must be retained at distinct paths");
        (await File.ReadAllBytesAsync(rawPath)).Should().Equal(
            rawContent,
            "the retained raw evidence must be the untouched source bytes, not the canonical rendering");
        (await File.ReadAllTextAsync(canonicalPath)).Should().StartWith(
            "account,symbol,quantity,price,cashAmount,activityType,tradeDate");
    }

    [Theory]
    [InlineData(".", "statement.dat")]
    [InlineData("..", "statement.dat")]
    [InlineData("CON.csv", "statement.dat")]
    [InlineData("../../broker.csv", "broker.csv")]
    [InlineData("normal-statement.csv", "normal-statement.csv")]
    public async Task Commit_RetainedSourceName_IsOnePortablePathSegment(
        string suppliedFileName,
        string expectedRetainedFileName)
    {
        var source = StatementConnectorTestData.ReadFixture("csv-mixed-kinds.csv");
        var document = new StatementSourceDocument(suppliedFileName, source);

        var result = await _service.CommitAsync(CommitRequest(
            document,
            connectorId: CsvStatementConnector.ConnectorId));

        Path.GetFileName(result.RetainedSourcePath).Should().Be(expectedRetainedFileName);
        var retainedPath = Path.GetFullPath(Path.Combine(_root, result.RetainedSourcePath));
        var retainedRoot = Path.GetFullPath(Path.Combine(_root, "reconciliation", "statement-connector-imports"));
        retainedPath.Should().StartWith(
            retainedRoot + Path.DirectorySeparatorChar,
            "the retained raw statement must remain below the connector import root");
        (await File.ReadAllBytesAsync(retainedPath)).Should().Equal(source);
    }

    [Fact]
    public async Task Commit_CapturesCallerBytesOnceBeforeParsingHashingAndRetention()
    {
        var callerBuffer = StatementConnectorTestData.ReadFixture("csv-mixed-kinds.csv").ToArray();
        var expectedSource = callerBuffer.ToArray();
        var connector = new CallerBufferMutatingConnector(
            new CsvStatementConnector(_catalog),
            () => callerBuffer[0] = (byte)'X');
        var service = new StatementImportService(
            new StatementConnectorRegistry([connector]),
            _catalog,
            _workflow,
            _root);
        var document = new StatementSourceDocument("mutable-source.csv", callerBuffer);

        var result = await service.CommitAsync(CommitRequest(
            document,
            connectorId: connector.Descriptor.ConnectorId));

        callerBuffer[0].Should().Be((byte)'X', "the test connector must mutate the caller-owned array");
        var retainedPath = Path.Combine(_root, result.RetainedSourcePath);
        (await File.ReadAllBytesAsync(retainedPath)).Should().Equal(
            expectedSource,
            "parsing, hashing, and retention must all use the immutable entry snapshot");
        var run = await _workflow.GetAsync(result.RunId);
        run.Should().NotBeNull();
        run!.Import.SourceFileHash.Should().Be(
            Convert.ToHexString(SHA256.HashData(expectedSource)));
    }

    [Fact]
    public async Task Commit_RetainedEvidencePathThroughDirectoryLink_IsRejected()
    {
        var reconciliationDirectory = Path.Combine(_root, "reconciliation");
        var retainedImportsLink = Path.Combine(
            reconciliationDirectory,
            "statement-connector-imports");
        var outsideRoot = _root + "-outside";
        Directory.CreateDirectory(reconciliationDirectory);
        Directory.CreateDirectory(outsideRoot);
        try
        {
            try
            {
                Directory.CreateSymbolicLink(retainedImportsLink, outsideRoot);
            }
            catch (Exception ex) when (
                ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
            {
                return;
            }

            var commit = () => _service.CommitAsync(
                CommitRequest(FixtureDocument("csv-mixed-kinds.csv")));

            await commit.Should().ThrowAsync<InvalidOperationException>();
            Directory.EnumerateFileSystemEntries(outsideRoot).Should().BeEmpty(
                "statement retention must not follow a reparse point outside DataRoot");
        }
        finally
        {
            try
            {
                Directory.Delete(outsideRoot, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    [Fact]
    public async Task Commit_ReimportWithChangedMapping_PreservesEachCanonicalArtifact()
    {
        var originalDocument = FixtureDocument("csv-mixed-kinds.csv");
        var first = await _service.CommitAsync(CommitRequest(originalDocument));
        var firstArtifact = await File.ReadAllTextAsync(Path.Combine(_root, first.RetainedCanonicalPath));

        var canonicalProfile = StatementBuiltInProfiles.All.Single(profile =>
            profile.ProfileId == StatementMappingProfileRegistry.CanonicalCsvV1ProfileId);
        var remappedProfile = canonicalProfile with
        {
            ProfileId = "canonical-price-quantity-swapped-v1",
            DisplayName = "Canonical CSV with price and quantity swapped",
            IsBuiltIn = false,
            Fields = canonicalProfile.Fields
                .Select(field => field.CanonicalField switch
                {
                    "Quantity" => field with { SourceColumn = "price" },
                    "Price" => field with { SourceColumn = "quantity" },
                    _ => field
                })
                .ToArray()
        };
        await _catalog.UpsertAsync(remappedProfile);

        var remappedDocument = originalDocument with { MappingProfileId = remappedProfile.ProfileId };
        var second = await _service.CommitAsync(CommitRequest(remappedDocument));
        var secondArtifact = await File.ReadAllTextAsync(Path.Combine(_root, second.RetainedCanonicalPath));

        first.Duplicate.Should().BeFalse();
        second.Duplicate.Should().BeFalse("a changed canonical rendering is a distinct reconciliation run");
        first.RetainedCanonicalPath.Should().NotBe(second.RetainedCanonicalPath);
        firstArtifact.Should().NotBe(secondArtifact);
        (await File.ReadAllTextAsync(Path.Combine(_root, first.RetainedCanonicalPath))).Should().Be(
            firstArtifact,
            "a later import must not replace the normalized evidence referenced by the first run");
    }

    [Fact]
    public async Task Commit_SameStatementTwice_IsIdempotentDuplicate()
    {
        var first = await _service.CommitAsync(CommitRequest(FixtureDocument("csv-mixed-kinds.csv")));
        var second = await _service.CommitAsync(CommitRequest(FixtureDocument("csv-mixed-kinds.csv")));

        second.Duplicate.Should().BeTrue();
        second.RunId.Should().Be(first.RunId, "the duplicate key is the import id");
        second.Status.Should().Be("Duplicate");
        second.BreakCount.Should().Be(first.BreakCount);
        second.CaseCount.Should().Be(first.CaseCount);
        second.BreakIds.Should().Equal(first.BreakIds);
        second.CaseIds.Should().Equal(first.CaseIds);
        second.ReconciliationCaseRoutes.Should().Equal(first.ReconciliationCaseRoutes);
        second.ReconciliationCaseLinks.Should().BeEquivalentTo(first.ReconciliationCaseLinks);
    }

    [Fact]
    public async Task Commit_SameStatementForDifferentLedgerBooks_RetainsDistinctScopedImports()
    {
        var document = FixtureDocument("csv-mixed-kinds.csv");
        var periodId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var firstScope = new StatementAccountingScope(
            "fund-profile-alpha",
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            periodId,
            new DateOnly(2026, 6, 30));
        var secondScope = firstScope with
        {
            LedgerBookId = Guid.Parse("33333333-3333-3333-3333-333333333333")
        };
        var firstRequest = CommitRequest(document) with { AccountingScope = firstScope };
        var secondRequest = CommitRequest(document) with { AccountingScope = secondScope };

        var first = await _service.CommitAsync(firstRequest);
        var second = await _service.CommitAsync(secondRequest);

        first.Duplicate.Should().BeFalse();
        second.Duplicate.Should().BeFalse(
            "identical source bytes bound to different ledger books are different accounting evidence");
        second.RunId.Should().NotBe(first.RunId);
        var imports = await _workflow.ListImportsAsync();
        imports.Should().Contain(import =>
            import.ImportId == first.RunId &&
            import.AccountingScope == firstScope);
        imports.Should().Contain(import =>
            import.ImportId == second.RunId &&
            import.AccountingScope == secondScope);
    }

    [Fact]
    public async Task Scenario_UpgradeAfterCanonicalOnlyImport_ReimportReturnsTheRetainedLegacyRun()
    {
        const string source =
            "account,symbol,quantity,price,cashAmount,activityType,tradeDate\n" +
            "FUND-A,AAPL,1,100,-100,trade,2026-06-02\n";
        // The pre-upgrade run is keyed by the hash of the canonical rendering, so this constant has
        // to be the exact artifact the writer produces today — including every trailing column.
        const string canonical =
            "account,symbol,quantity,price,cashAmount,activityType,tradeDate,settlementDate,currency,feesCommission,externalTransactionId,activityCategory,activitySubtype,providerActivityCode,relatedTransactionId,orderId,description\n" +
            "FUND-A,AAPL,1,100,-100,trade,2026-06-02,,,,,,,,,,\n";
        var document = new StatementSourceDocument("legacy-import.csv", Encoding.UTF8.GetBytes(source));
        var canonicalHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        var rawHash = Convert.ToHexString(SHA256.HashData(document.Content.Span));
        var periodStart = new DateOnly(2026, 6, 1);
        var periodEnd = new DateOnly(2026, 6, 30);
        var legacyRunId = StatementDuplicateKey.Create("FUND-A", periodStart, periodEnd, canonicalHash);
        var hardenedRunId = StatementDuplicateKey.Create(
            "FUND-A",
            periodStart,
            periodEnd,
            rawHash,
            canonicalHash);
        legacyRunId.Should().NotBe(hardenedRunId, "the upgrade scenario must exercise the old canonical-only identity");

        var store = new JsonCanonicalStatementStore(_root);
        await store.SaveImportAsync(
            new CanonicalStatementImport(
                legacyRunId,
                "broker",
                periodEnd,
                DateTimeOffset.UtcNow.AddDays(-1),
                Path.Combine(_root, "legacy", "canonical.csv"),
                canonicalHash,
                RawRowCount: 1,
                NormalizedRowCount: 1)
            {
                SourceInstitution = "Sample Broker",
                FundAccountId = "FUND-A",
                ExternalAccountId = "FUND-A",
                StatementPeriodStart = periodStart,
                StatementPeriodEnd = periodEnd,
                OriginalFileName = document.FileName,
                MappingProfileId = StatementMappingProfileRegistry.CanonicalCsvV1ProfileId,
                ToleranceProfileId = StatementToleranceProfile.DefaultProfileId,
                ImportedBy = "pre-upgrade-operator",
                SourceFileHash = canonicalHash,
                CanonicalArtifactHash = canonicalHash,
                DuplicateKey = legacyRunId
            },
            []);

        var result = await _service.CommitAsync(CommitRequest(document));

        result.Duplicate.Should().BeTrue();
        result.RunId.Should().Be(legacyRunId, "operator links must target the retained pre-upgrade run");
        result.RunId.Should().NotBe(hardenedRunId);
        (await store.ListImportsAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task Commit_OfxAndFlexStatements_LandInTheSameQueue()
    {
        var ofxResult = await _service.CommitAsync(CommitRequest(
            FixtureDocument("ofx-102-bank.ofx"),
            "FUND-OFX",
            "FUND-A-CASH"));
        var flexResult = await _service.CommitAsync(CommitRequest(
            FixtureDocument("ib-flex-sample.xml"),
            "FUND-FLEX",
            "U1234567"));

        ofxResult.RecordCount.Should().Be(4);
        flexResult.RecordCount.Should().Be(7);

        var imports = await _workflow.ListImportsAsync();
        imports.Select(import => import.ImportId).Should().Contain([ofxResult.RunId, flexResult.RunId]);
    }

    [Fact]
    public async Task Commit_BlockingParseErrors_Throw()
    {
        var content = "symbol,quantity,price,cashAmount,activityType,tradeDate\nAAPL,1,10,-10,trade,2026-06-01\n";
        var document = new StatementSourceDocument("missing-account.csv", Encoding.UTF8.GetBytes(content));

        var act = () => _service.CommitAsync(CommitRequest(document));

        await act.Should().ThrowAsync<InvalidDataException>().WithMessage("*Account*");
    }

    [Fact]
    public async Task Commit_UnsupportedSourceKind_Throws()
    {
        var request = CommitRequest(FixtureDocument("csv-mixed-kinds.csv")) with { SourceKind = "local" };

        var act = () => _service.CommitAsync(request);

        await act.Should().ThrowAsync<InvalidDataException>().WithMessage("*broker*custodian*");
    }

    [Fact]
    public async Task FetchDocument_NonFetchingConnector_Throws()
    {
        var act = () => _service.FetchDocumentAsync(new StatementFetchRequest(CsvStatementConnector.ConnectorId, "EXT-001"));

        await act.Should().ThrowAsync<NotSupportedException>();
    }

    [Fact]
    public async Task ScheduleStore_LegacySnapshot_DefaultsSourceKindToBroker()
    {
        var schedulePath = Path.Combine(_root, "reconciliation", "statement-fetch-schedules.json");
        Directory.CreateDirectory(Path.GetDirectoryName(schedulePath)!);
        await File.WriteAllTextAsync(
            schedulePath,
            """
            {
              "version": 1,
              "schedules": [
                {
                  "scheduleId": "legacy-schedule",
                  "connectorId": "fake-fetch",
                  "externalAccountId": "EXT-LEGACY",
                  "fundAccountId": "FUND-LEGACY",
                  "sourceInstitution": "Legacy Broker",
                  "mappingProfileId": null,
                  "toleranceProfileId": "statement-default",
                  "cadenceHours": 24,
                  "enabled": true,
                  "lastRunAtUtc": null,
                  "lastRunStatus": null
                }
              ]
            }
            """);

        var schedule = (await new FileStatementFetchScheduleStore(_root).ListAsync()).Single();

        schedule.SourceKind.Should().Be("broker");
        var ingestion = new RecordingStatementFetchIngestionAuthority(_service);
        var runner = new StatementFetchScheduleRunner(
            new FileStatementFetchScheduleStore(_root),
            _service,
            ingestion);

        var result = await runner.RunScheduleAsync(
            schedule,
            new DateTimeOffset(2026, 7, 1, 6, 0, 0, TimeSpan.Zero));

        result.Should().BeNull();
        _fetchingConnector.LastRequest.Should().BeNull(
            "a legacy unscoped schedule must fail before any remote request");
        ingestion.LastCommand.Should().BeNull(
            "a legacy schedule without tenant/company/book/period authority must fail before ingestion");
        (await new FileStatementFetchScheduleStore(_root).ListAsync())
            .Single()
            .LastRunStatus
            .Should()
            .Be("Failed: InvalidOperationException");
    }

    [Fact]
    public async Task ScheduleStore_AuthorityChanges_ClearUnrelatedRunHistory()
    {
        var scheduleStore = new FileStatementFetchScheduleStore(_root);
        var periodStart = new DateOnly(2026, 6, 1);
        var periodEnd = new DateOnly(2026, 6, 30);
        var original = CreateScopedSchedule("authority-template", periodStart, periodEnd);
        var nextPeriodStart = new DateOnly(2026, 7, 1);
        var nextPeriodEnd = new DateOnly(2026, 7, 31);
        var authorityChanges = new (string Name, StatementFetchSchedule Schedule)[]
        {
            (
                "account",
                original with
                {
                    ExternalAccountId = "EXT-002",
                    FundAccountId = "FUND-SCHED-002"
                }),
            (
                "period",
                original with
                {
                    PeriodStart = nextPeriodStart,
                    PeriodEnd = nextPeriodEnd,
                    AccountingScope = new StatementAccountingScope(
                        "fund-profile-scheduled",
                        Guid.Parse("11111111-1111-1111-1111-111111111111"),
                        Guid.Parse("33333333-3333-3333-3333-333333333333"),
                        nextPeriodEnd)
                }),
            (
                "source",
                original with
                {
                    ConnectorId = "replacement-fetch",
                    SourceInstitution = "Replacement Broker",
                    SourceKind = "broker",
                    MappingProfileId = "replacement-profile"
                })
        };
        var ranAt = new DateTimeOffset(2026, 7, 1, 6, 0, 0, TimeSpan.Zero);

        for (var index = 0; index < authorityChanges.Length; index++)
        {
            var scheduleId = $"authority-change-{index}";
            await scheduleStore.UpsertAsync(original with { ScheduleId = scheduleId });
            await scheduleStore.RecordRunAsync(scheduleId, ranAt, "Imported prior authority");
            await scheduleStore.RecordFailureAsync(
                scheduleId,
                ranAt.AddHours(1),
                "Failed: prior authority");

            var updated = await scheduleStore.UpsertAsync(
                authorityChanges[index].Schedule with { ScheduleId = scheduleId });

            updated.LastRunAtUtc.Should().BeNull(
                $"{authorityChanges[index].Name} changes cannot inherit a successful-run cursor");
            updated.LastAttemptAtUtc.Should().BeNull(
                $"{authorityChanges[index].Name} changes cannot inherit an attempt cadence watermark");
            updated.LastRunStatus.Should().BeNull(
                $"{authorityChanges[index].Name} changes cannot inherit an unrelated outcome");
        }
    }

    [Fact]
    public async Task ScheduleStore_CadenceOnlyEdit_PreservesRunHistory()
    {
        var scheduleStore = new FileStatementFetchScheduleStore(_root);
        var schedule = CreateScopedSchedule(
            "cadence-only",
            new DateOnly(2026, 6, 1),
            new DateOnly(2026, 6, 30));
        var ranAt = new DateTimeOffset(2026, 7, 1, 6, 0, 0, TimeSpan.Zero);
        await scheduleStore.UpsertAsync(schedule);
        await scheduleStore.RecordRunAsync(schedule.ScheduleId, ranAt, "Imported");

        var updated = await scheduleStore.UpsertAsync(schedule with
        {
            CadenceHours = 48,
            Enabled = false
        });

        updated.LastRunAtUtc.Should().Be(ranAt);
        updated.LastAttemptAtUtc.Should().Be(ranAt);
        updated.LastRunStatus.Should().Be("Imported");
    }

    [Fact]
    public async Task ScheduleRunner_RunsDueSchedules_AndRecordsOutcome()
    {
        var scheduleStore = new FileStatementFetchScheduleStore(_root);
        var ingestion = new RecordingStatementFetchIngestionAuthority(_service);
        var runner = new StatementFetchScheduleRunner(scheduleStore, _service, ingestion);
        var periodStart = new DateOnly(2026, 6, 1);
        var periodEnd = new DateOnly(2026, 6, 30);
        var accountingScope = new StatementAccountingScope(
            "fund-profile-scheduled",
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            periodEnd);
        var schedule = await scheduleStore.UpsertAsync(new StatementFetchSchedule(
            ScheduleId: string.Empty,
            ConnectorId: FakeFetchingConnector.FakeConnectorId,
            ExternalAccountId: "EXT-001",
            FundAccountId: "FUND-SCHED",
            SourceInstitution: "Fake Custodian",
            MappingProfileId: null,
            ToleranceProfileId: "statement-default",
            CadenceHours: 24,
            Enabled: true,
            SourceKind: "custodian",
            TenantId: "tenant-scheduled",
            CompanyId: "company-scheduled",
            PeriodStart: periodStart,
            PeriodEnd: periodEnd,
            AccountingScope: accountingScope));

        var now = new DateTimeOffset(2026, 7, 1, 6, 0, 0, TimeSpan.Zero);
        var ran = await runner.RunDueSchedulesAsync(now);

        ran.Should().Be(1);
        var updated = (await scheduleStore.ListAsync()).Single();
        updated.LastRunAtUtc.Should().Be(now);
        updated.LastRunStatus.Should().StartWith("Imported");
        (await _workflow.ListImportsAsync()).Single().Broker.Should().Be("custodian");
        ingestion.LastCommand.Should().NotBeNull();
        ingestion.LastCommand!.TenantId.Should().Be("tenant-scheduled");
        ingestion.LastCommand.CompanyId.Should().Be("company-scheduled");
        ingestion.LastCommand.PeriodStart.Should().Be(periodStart);
        ingestion.LastCommand.PeriodEnd.Should().Be(periodEnd);
        ingestion.LastCommand.AccountingScope.Should().Be(accountingScope);
        ingestion.LastAuthorizationCommand.Should().NotBeNull();
        ingestion.LastAuthorizationCommand!.TenantId.Should().Be("tenant-scheduled");
        ingestion.LastAuthorizationCommand.CompanyId.Should().Be("company-scheduled");
        ingestion.LastAuthorizationCommand.FundAccountId.Should().Be("FUND-SCHED");
        ingestion.LastAuthorizationCommand.ExternalAccountId.Should().Be("EXT-001");
        ingestion.LastAuthorizationCommand.SourceInstitution.Should().Be("Fake Custodian");
        ingestion.LastAuthorizationCommand.AccountingScope.Should().Be(accountingScope);
        _fetchingConnector.LastRequest.Should().NotBeNull();
        _fetchingConnector.LastRequest!.Since.Should().Be(
            new DateTimeOffset(periodStart, TimeOnly.MinValue, TimeSpan.Zero),
            "the remote lower bound comes from the immutable retained period, not cadence or run history");
        _fetchingConnector.LastRequest.UntilExclusive.Should().Be(
            new DateTimeOffset(periodEnd.AddDays(1), TimeOnly.MinValue, TimeSpan.Zero),
            "the exclusive remote upper bound is the day after the immutable inclusive period end");
        _fetchingConnector.LastRequest.Datasets.Should().Be(
            StatementFetchDatasets.Activity,
            "a current portfolio snapshot cannot truthfully represent a retained historical period end");

        // Just ran: not due again until the cadence elapses.
        (await runner.RunDueSchedulesAsync(now.AddHours(1))).Should().Be(0);
        (await runner.RunDueSchedulesAsync(now.AddHours(25))).Should().Be(1);
    }

    [Fact]
    public async Task ScheduleRunner_ReauthorizationFailure_BlocksProviderAccessAndIngestion()
    {
        var scheduleStore = new FileStatementFetchScheduleStore(_root);
        var schedule = await scheduleStore.UpsertAsync(
            CreateScopedSchedule(
                "ownership-revoked",
                new DateOnly(2026, 6, 1),
                new DateOnly(2026, 6, 30)));
        var ingestion = new RecordingStatementFetchIngestionAuthority(
            _service,
            authorizationFailure: new InvalidOperationException("Statement account ownership was revoked."));
        var runner = new StatementFetchScheduleRunner(scheduleStore, _service, ingestion);
        var now = new DateTimeOffset(2026, 7, 1, 6, 0, 0, TimeSpan.Zero);

        var result = await runner.RunScheduleAsync(schedule, now);

        result.Should().BeNull();
        ingestion.LastAuthorizationCommand.Should().NotBeNull();
        ingestion.LastAuthorizationCommand!.TenantId.Should().Be("tenant-scheduled");
        ingestion.LastAuthorizationCommand.CompanyId.Should().Be("company-scheduled");
        ingestion.LastAuthorizationCommand.FundAccountId.Should().Be("FUND-SCHED");
        ingestion.LastAuthorizationCommand.ExternalAccountId.Should().Be("EXT-001");
        ingestion.LastCommand.Should().BeNull(
            "a revoked account must never enter the statement ingestion workflow");
        _fetchingConnector.LastRequest.Should().BeNull(
            "tenant, company, fund-account, and external-account ownership must be reauthorized before provider access");
        (await scheduleStore.ListAsync())
            .Single()
            .LastRunStatus
            .Should()
            .Be("Failed: InvalidOperationException");
    }

    [Fact]
    public async Task ScheduleRunner_FetchFailure_IsRecordedNotThrown()
    {
        var scheduleStore = new FileStatementFetchScheduleStore(_root);
        var runner = new StatementFetchScheduleRunner(
            scheduleStore,
            _service,
            new RecordingStatementFetchIngestionAuthority(_service));
        var periodEnd = new DateOnly(2026, 7, 31);
        await scheduleStore.UpsertAsync(new StatementFetchSchedule(
            ScheduleId: "sched-bad",
            ConnectorId: CsvStatementConnector.ConnectorId,
            ExternalAccountId: "EXT-001",
            FundAccountId: "FUND-SCHED",
            SourceInstitution: "Fake Custodian",
            MappingProfileId: null,
            ToleranceProfileId: "statement-default",
            CadenceHours: 24,
            Enabled: true,
            TenantId: "tenant-scheduled",
            CompanyId: "company-scheduled",
            PeriodStart: new DateOnly(2026, 7, 1),
            PeriodEnd: periodEnd,
            AccountingScope: new StatementAccountingScope(
                "fund-profile-scheduled",
                Guid.Parse("11111111-1111-1111-1111-111111111111"),
                Guid.Parse("22222222-2222-2222-2222-222222222222"),
                periodEnd)));
        var lastSuccessfulRun = new DateTimeOffset(2026, 7, 17, 6, 0, 0, TimeSpan.Zero);
        await scheduleStore.RecordRunAsync("sched-bad", lastSuccessfulRun, "Imported prior run");

        var ran = await runner.RunDueSchedulesAsync(lastSuccessfulRun.AddHours(25));

        ran.Should().Be(1);
        var updated = (await scheduleStore.ListAsync()).Single();
        var failedAt = lastSuccessfulRun.AddHours(25);
        updated.LastRunAtUtc.Should().Be(
            lastSuccessfulRun,
            "a failed fetch must not skip activity after the last successful fetch cursor");
        updated.LastAttemptAtUtc.Should().Be(
            failedAt,
            "failed attempts must advance a separate cadence watermark to avoid minute-by-minute retries");
        updated.LastRunStatus.Should().Be("Failed: NotSupportedException");
        (await runner.RunDueSchedulesAsync(failedAt.AddMinutes(1))).Should().Be(0);
        (await runner.RunDueSchedulesAsync(failedAt.AddHours(24))).Should().Be(1);
    }

    private static StatementFetchSchedule CreateScopedSchedule(
        string scheduleId,
        DateOnly periodStart,
        DateOnly periodEnd)
        => new(
            ScheduleId: scheduleId,
            ConnectorId: FakeFetchingConnector.FakeConnectorId,
            ExternalAccountId: "EXT-001",
            FundAccountId: "FUND-SCHED",
            SourceInstitution: "Fake Custodian",
            MappingProfileId: null,
            ToleranceProfileId: "statement-default",
            CadenceHours: 24,
            Enabled: true,
            SourceKind: "custodian",
            TenantId: "tenant-scheduled",
            CompanyId: "company-scheduled",
            PeriodStart: periodStart,
            PeriodEnd: periodEnd,
            AccountingScope: new StatementAccountingScope(
                "fund-profile-scheduled",
                Guid.Parse("11111111-1111-1111-1111-111111111111"),
                Guid.Parse("22222222-2222-2222-2222-222222222222"),
                periodEnd));

    private sealed class RecordingStatementFetchIngestionAuthority(
        StatementImportService imports,
        Exception? authorizationFailure = null)
        : IStatementFetchIngestionAuthority
    {
        public StatementFetchAuthorizationCommand? LastAuthorizationCommand { get; private set; }
        public StatementFetchIngestionCommand? LastCommand { get; private set; }

        public Task<StatementAccountingScope> AuthorizeAsync(
            StatementFetchAuthorizationCommand command,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            LastAuthorizationCommand = command;
            return authorizationFailure is null
                ? Task.FromResult(command.AccountingScope)
                : Task.FromException<StatementAccountingScope>(authorizationFailure);
        }

        public async Task<StatementImportCommitResultDto> IngestAsync(
            StatementFetchIngestionCommand command,
            CancellationToken ct = default)
        {
            LastCommand = command;
            return await imports.CommitAsync(
                    new StatementImportCommitRequest(
                        command.Document,
                        command.ConnectorId,
                        command.SourceKind,
                        command.SourceInstitution,
                        command.FundAccountId,
                        command.ExternalAccountId,
                        command.PeriodStart,
                        command.PeriodEnd,
                        command.ToleranceProfileId,
                        command.ImportedBy)
                    {
                        AccountingScope = command.AccountingScope
                    },
                    ct)
                .ConfigureAwait(false);
        }
    }

    private sealed class FixedRecordConnector : IStatementConnector
    {
        private readonly IReadOnlyList<StatementCanonicalRecord> _records;

        public FixedRecordConnector(params StatementCanonicalRecord[] records)
        {
            _records = records;
        }

        public StatementConnectorDescriptor Descriptor { get; } = new(
            "fixed-record",
            "Fixed record connector",
            [".fixed"],
            SupportsFileImport: true,
            SupportsRemoteFetch: false,
            RequiresMappingProfile: false,
            DefaultProfileId: null);

        public bool CanHandle(StatementSourceDocument document) => true;

        public Task<StatementParseResult> ParseAsync(
            StatementSourceDocument document,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(new StatementParseResult(
                Descriptor.ConnectorId,
                ProfileId: null,
                DetectedColumns: [],
                ColumnMappings: [],
                Records: _records,
                Issues: [],
                Fingerprint: new StatementFormatFingerprint(
                    new string('A', 64),
                    NormalizedColumns: [],
                    Delimiter: string.Empty)));
        }
    }

    /// <summary>A fetch-capable connector returning a canonical CSV document, for scheduler tests.</summary>
    private sealed class FakeFetchingConnector : IFetchingStatementConnector
    {
        public const string FakeConnectorId = "fake-fetch";

        public StatementFetchRequest? LastRequest { get; private set; }

        public StatementConnectorDescriptor Descriptor { get; } = new(
            FakeConnectorId,
            "Fake fetching connector",
            [".fake"],
            SupportsFileImport: false,
            SupportsRemoteFetch: true,
            RequiresMappingProfile: false,
            DefaultProfileId: null);

        public bool CanHandle(StatementSourceDocument document)
            => Path.GetExtension(document.FileName) == ".fake";

        public Task<StatementSourceDocument> FetchAsync(StatementFetchRequest request, CancellationToken ct = default)
        {
            LastRequest = request;
            const string content =
                "account,symbol,quantity,price,cashAmount,activityType,tradeDate\n" +
                "EXT-001,AAPL,5,100,-500,trade,2026-06-20\n";
            return Task.FromResult(new StatementSourceDocument("fetched.fake", Encoding.UTF8.GetBytes(content)));
        }

        public async Task<StatementParseResult> ParseAsync(StatementSourceDocument document, CancellationToken ct = default)
        {
            // Delegate to the CSV pipeline shape by parsing canonical CSV inline.
            var catalog = new StatementMappingProfileCatalog(new NullProfileStore());
            return await new CsvStatementConnector(catalog).ParseAsync(document, ct).ConfigureAwait(false);
        }

        private sealed class NullProfileStore : IStatementMappingProfileStore
        {
            public Task<IReadOnlyList<StatementMappingProfileDocument>> ListAsync(CancellationToken ct = default)
                => Task.FromResult<IReadOnlyList<StatementMappingProfileDocument>>([]);

            public Task<StatementMappingProfileDocument> UpsertAsync(StatementMappingProfileDocument document, CancellationToken ct = default)
                => Task.FromResult(document);

            public Task<bool> DeleteAsync(string profileId, CancellationToken ct = default)
                => Task.FromResult(false);
        }
    }

    private sealed class CallerBufferMutatingConnector(
        IStatementConnector inner,
        Action mutateCallerBuffer) : IStatementConnector
    {
        public StatementConnectorDescriptor Descriptor { get; } = inner.Descriptor with
        {
            ConnectorId = "caller-buffer-mutation-test",
            DisplayName = "Caller buffer mutation test"
        };

        public bool CanHandle(StatementSourceDocument document) => true;

        public Task<StatementParseResult> ParseAsync(
            StatementSourceDocument document,
            CancellationToken ct = default)
        {
            mutateCallerBuffer();
            return inner.ParseAsync(document, ct);
        }
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
