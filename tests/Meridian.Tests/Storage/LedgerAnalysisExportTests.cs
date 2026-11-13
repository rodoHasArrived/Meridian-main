using System.IO.Compression;
using Apache.Arrow;
using Apache.Arrow.Ipc;
using FluentAssertions;
using Meridian.Ledger;
using Meridian.Storage.Export;
using Meridian.Storage.Packaging;
using Meridian.Backtesting.Portfolio;
using Meridian.Backtesting.Sdk;
using Meridian.Ui.Shared.Services;
using Parquet;
using Xunit;

namespace Meridian.Tests.Storage;

public sealed class LedgerAnalysisExportTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-ledger-export-" + Guid.NewGuid().ToString("N"));
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset AsOf = Start.AddDays(30);

    [Theory]
    [InlineData(ExportFormat.Csv)]
    [InlineData(ExportFormat.Xlsx)]
    [InlineData(ExportFormat.Parquet)]
    [InlineData(ExportFormat.Arrow)]
    public async Task EmptyJournal_PreservesAccountingSchemaInEveryPhysicalFormat(ExportFormat format)
    {
        var result = await new AnalysisExportService(_root).ExportLedgerAsync(new(
            new Meridian.Ledger.Ledger(), Start, AsOf, _root, format, OpenLots: [], RealizedLots: []));
        result.Success.Should().BeTrue();
        result.Warnings.Should().BeEmpty();
        var file = result.Files.Single(file => file.RelativePath.StartsWith("journal.", StringComparison.Ordinal));
        file.RecordCount.Should().Be(0);
        switch (format)
        {
            case ExportFormat.Csv:
                (await File.ReadAllTextAsync(file.Path)).Should().Contain("JournalId").And.Contain("Debit");
                break;
            case ExportFormat.Xlsx:
                using (var zip = ZipFile.OpenRead(file.Path))
                using (var reader = new StreamReader(zip.GetEntry("xl/worksheets/sheet1.xml")!.Open()))
                    (await reader.ReadToEndAsync()).Should().Contain("JournalId").And.Contain("Debit");
                break;
            case ExportFormat.Parquet:
                using (var stream = File.OpenRead(file.Path))
                using (var reader = await ParquetReader.CreateAsync(stream))
                    reader.Schema.GetDataFields().Select(field => field.Name).Should().Contain("JournalId").And.Contain("Debit");
                break;
            case ExportFormat.Arrow:
                using (var stream = File.OpenRead(file.Path))
                using (var reader = new ArrowFileReader(stream))
                using (var batch = await reader.ReadNextRecordBatchAsync())
                {
                    batch!.Length.Should().Be(0);
                    batch.Schema.FieldsList.Select(field => field.Name).Should().Contain("JournalId").And.Contain("Debit");
                }
                break;
        }
    }

    [Fact]
    public async Task Csv_RetainsExactDecimals_FirstDayDisposals_AndShortLotDirection()
    {
        var ledger = CreateLedger();
        var request = new LedgerExportRequest(ledger, Start, AsOf, _root, FinancialAccountId: " account-a ",
            OpenLots: [new("open", "XYZ", "account-a", new DateOnly(2025, 1, 1), 1m, 100m, 80m, AsOf, IsShort: true)],
            RealizedLots: [new("sold", "XYZ", "account-a", new DateOnly(2025, 1, 1), new DateOnly(2026, 1, 1),
                1m, 1234567890.123456789012345678m, 100m, "ShortTerm", SoldAt: Start.AddHours(12))]);
        var result = await new AnalysisExportService(_root).ExportLedgerAsync(request);
        var realized = await File.ReadAllTextAsync(result.Files.Single(file => file.RelativePath == "realized-gains.csv").Path);
        realized.Should().Contain("1234567890.123456789012345678").And.Contain("sold");
        var open = await File.ReadAllTextAsync(result.Files.Single(file => file.RelativePath == "open-lot-pnl.csv").Path);
        open.Should().Contain(",20,");
        result.Files.Single(file => file.RelativePath == "journal.csv").RecordCount.Should().Be(2);
        ledger.JournalEntryCount.Should().Be(1);
    }

    [Fact]
    public async Task Csv_PreservesTinySignedDecimalsWithoutFormulaEscaping()
    {
        var ledger = new Meridian.Ledger.Ledger();
        const decimal amount = 0.0000000000000000000000000001m;
        ledger.PostLines(Start.AddSeconds(1), "Tiny expense",
            [(LedgerAccounts.CommissionExpense, amount, 0m), (LedgerAccounts.Cash, 0m, amount)]);

        var result = await new AnalysisExportService(_root).ExportLedgerAsync(new(ledger, Start, AsOf, _root));
        var csv = await File.ReadAllTextAsync(result.Files.Single(file => file.RelativePath == "cash-flow.csv").Path);

        csv.Should().Contain(",-0.0000000000000000000000000001").And.NotContain("'");
    }

    [Fact]
    public async Task Csv_NeutralizesFormulaDescriptionsWithoutChangingBookedAmounts()
    {
        var result = await new AnalysisExportService(_root).ExportLedgerAsync(new(CreateLedger("=1+2"), Start, AsOf, _root));
        var csv = await File.ReadAllTextAsync(result.Files.Single(file => file.RelativePath == "journal.csv").Path);
        csv.Should().Contain("'=1+2").And.Contain(",100,0,");
    }

    [Fact]
    public async Task MissingLotEvidence_IsReportedUnavailableInsteadOfEmptySuccess()
    {
        var result = await new AnalysisExportService(_root).ExportLedgerAsync(new(CreateLedger(), Start, AsOf, _root));
        result.Warnings.Should().HaveCount(2);
        result.Files.Should().NotContain(file => file.RelativePath.StartsWith("open-lot-pnl", StringComparison.Ordinal));
        (await File.ReadAllTextAsync(result.Files.Single(file => file.RelativePath == "availability.csv").Path))
            .Should().Contain("Unavailable");
    }

    [Fact]
    public async Task SnapshotMapper_ExportsCompositeBasisAndFractionalRealizationsWithoutInventingTaxParcels()
    {
        var ledger = new Meridian.Ledger.Ledger();
        var portfolio = new SimulatedPortfolio(10_000m, new FixedCommissionModel(0m), 0d, 0d, ledger, Start);
        portfolio.ProcessFill(new(Guid.NewGuid(), Guid.NewGuid(), "XYZ", 3L, 100m, 0m, Start.AddHours(1)));
        portfolio.ProcessFill(new(Guid.NewGuid(), Guid.NewGuid(), "XYZ", 1L, 200m, 0m, Start.AddHours(2)));
        portfolio.ApplyAssetEvent(new(Start.AddDays(1), "XYZ", AssetEventType.Split, PositionFactor: 0.5m, ReferencePrice: 250m));
        // The second action leaves a half-unit entitlement with exact source basis relieved.
        portfolio.ApplyAssetEvent(new(Start.AddDays(2), "XYZ", AssetEventType.Split, PositionFactor: 0.75m, ReferencePrice: 300m));
        var snapshot = portfolio.TakeSnapshot(AsOf, DateOnly.FromDateTime(AsOf.UtcDateTime));
        var result = new BacktestResult(new(DateOnly.FromDateTime(Start.UtcDateTime), DateOnly.FromDateTime(AsOf.UtcDateTime)),
            new HashSet<string> { "XYZ" }, [snapshot], [], [], null!, ledger, TimeSpan.Zero, 0);
        var request = BacktestLedgerExportMapper.Build(result, Start.AddTicks(-1), AsOf, _root, ExportFormat.Csv);
        request.OpenLots!.Sum(lot => lot.CostBasis).Should().Be(
            ledger.GetBalance(LedgerAccounts.Securities("XYZ", BacktestDefaults.DefaultBrokerageAccountId)));
        var exported = await new AnalysisExportService(_root).ExportLedgerAsync(request);
        var corporateActions = await File.ReadAllTextAsync(exported.Files.Single(file => file.RelativePath == "corporate-action-realizations.csv").Path);
        corporateActions.Should().Contain("cash_in_lieu").And.Contain("acquisition parcel dates unavailable");
        exported.Warnings.Should().Contain(message => message.Contains("acquisition parcel dates", StringComparison.Ordinal));

        var account = snapshot.Accounts.Values.Single();
        var incomplete = snapshot with
        {
            Accounts = new Dictionary<string, FinancialAccountSnapshot>
            { [account.AccountId] = account with { OpenLots = [] } }
        };
        Action map = () => BacktestLedgerExportMapper.Build(result with { Snapshots = [incomplete] },
            Start, AsOf, _root, ExportFormat.Csv);
        map.Should().Throw<InvalidOperationException>().WithMessage("*full position quantity*");
    }

    [Theory]
    [InlineData(1, 80)]
    [InlineData(1, 120)]
    [InlineData(-1, 80)]
    [InlineData(-1, 120)]
    public void SimulatedDisposalCashFlows_ClassifyActualCashAsInvesting_ForLongAndShortGainsAndLosses(int direction, int closePrice)
    {
        var ledger = new Meridian.Ledger.Ledger();
        var portfolio = new SimulatedPortfolio(10_000m, new FixedCommissionModel(0m), 0d, 0d, ledger, Start);
        portfolio.ProcessFill(new(Guid.NewGuid(), Guid.NewGuid(), "XYZ", direction * 2L, 100m, 0m, Start.AddHours(1)));
        portfolio.ProcessFill(new(Guid.NewGuid(), Guid.NewGuid(), "XYZ", direction * -2L, closePrice, 0m, Start.AddHours(2)));
        var cash = LedgerFinancialStatementBuilder.BuildForPeriod(ledger, Start.AddTicks(-1), AsOf).CashFlow!;
        cash.IsReconciled.Should().BeTrue();
        cash.OperatingCashFlow.Should().Be(0m);
        cash.InvestingCashFlow.Should().Be(direction * 2m * (closePrice - 100m));
        cash.FinancingCashFlow.Should().Be(10_000m);
        cash.EndingCash.Should().Be(portfolio.Cash);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public void FractionalCorporateActionCashFlows_PreserveActualLongAndShortCashImpact(int direction)
    {
        var ledger = new Meridian.Ledger.Ledger();
        var portfolio = new SimulatedPortfolio(10_000m, new FixedCommissionModel(0m), 0d, 0d, ledger, Start);
        portfolio.ProcessFill(new(Guid.NewGuid(), Guid.NewGuid(), "XYZ", direction * 3L, 100m, 0m, Start.AddHours(1)));
        portfolio.ApplyAssetEvent(new(Start.AddDays(1), "XYZ", AssetEventType.Split,
            PositionFactor: 0.5m, ReferencePrice: 180m));
        var cash = LedgerFinancialStatementBuilder.BuildForPeriod(ledger, Start.AddTicks(-1), AsOf).CashFlow!;
        cash.IsReconciled.Should().BeTrue();
        cash.OperatingCashFlow.Should().Be(0m);
        cash.InvestingCashFlow.Should().Be(direction * -210m);
        cash.EndingCash.Should().Be(portfolio.Cash);
    }

    [Fact]
    public void ReturnOfCapitalCashFlows_KeepBasisRecoveryAndExcessGainInActualInvestingCash()
    {
        var ledger = new Meridian.Ledger.Ledger();
        var portfolio = new SimulatedPortfolio(10_000m, new FixedCommissionModel(0m), 0d, 0d, ledger, Start);
        portfolio.ProcessFill(new(Guid.NewGuid(), Guid.NewGuid(), "XYZ", 2L, 100m, 0m, Start.AddHours(1)));
        portfolio.ApplyAssetEvent(new(Start.AddDays(1), "XYZ", AssetEventType.ReturnOfCapital, CashPerShare: 110m));
        var cash = LedgerFinancialStatementBuilder.BuildForPeriod(ledger, Start.AddTicks(-1), AsOf).CashFlow!;
        cash.IsReconciled.Should().BeTrue();
        cash.OperatingCashFlow.Should().Be(0m);
        cash.InvestingCashFlow.Should().Be(20m);
        cash.EndingCash.Should().Be(portfolio.Cash);
    }

    [Fact]
    public async Task PortablePackage_RefusesLedgerEvidenceWithoutAccountingValidatorBeforePublishingOrImport()
    {
        var source = Path.Combine(_root, "source");
        var configured = PortableLedgerPackaging.CreatePackager(source);
        var bareTransport = new PortableDataPackager(source);
        var payload = PortableLedgerSnapshot.CapturePayload(CreateLedger());
        var output = Path.Combine(_root, "unconfigured-output");

        var unconfiguredCreate = await bareTransport.CreatePackageAsync(new()
        {
            Name = "unconfigured",
            Ledger = payload,
            OutputDirectory = output
        });
        unconfiguredCreate.Success.Should().BeFalse();
        unconfiguredCreate.Error.Should().Contain("validation is not configured");
        Directory.Exists(output).Should().BeFalse();

        var package = await configured.CreatePackageAsync(new()
        {
            Name = "requires-accounting",
            Ledger = payload,
            OutputDirectory = _root,
            IncludeLoaderScripts = false,
            IncludeDataDictionary = false
        });
        package.Success.Should().BeTrue(package.Error);
        var validation = await bareTransport.ValidatePackageAsync(package.PackagePath!);
        validation.IsValid.Should().BeFalse();
        validation.Error.Should().Contain("validation is not configured");
        var destination = Path.Combine(_root, "unconfigured-import");
        var import = await bareTransport.ImportPackageAsync(package.PackagePath!, destination, validateChecksums: false);
        import.Success.Should().BeFalse();
        import.Error.Should().Contain("validation is not configured");
        Directory.Exists(destination).Should().BeFalse();
    }

    [Fact]
    public async Task PortablePackage_RefusesSemanticallyChangedLedgerEvenWhenFileHashMatches()
    {
        var payload = PortableLedgerSnapshot.CapturePayload(CreateLedger());
        var packager = PortableLedgerPackaging.CreatePackager(Path.Combine(_root, "source"));
        var altered = System.Text.Json.Nodes.JsonNode.Parse(payload.Json)!;
        // Keep valid JSON and balanced journals; only the independently retained balance digest
        // disagrees. Matching the transport hash must never substitute for accounting replay.
        var falseChecksum = new string('0', 64);
        altered[nameof(PortableLedgerSnapshot.BalanceChecksumSha256)] = falseChecksum;
        var package = await packager.CreatePackageAsync(new()
        {
            Name = "altered-ledger",
            Ledger = payload,
            OutputDirectory = _root
        });
        package.Success.Should().BeTrue(package.Error);
        var alteredJson = altered.ToJsonString();
        using (var archive = ZipFile.Open(package.PackagePath!, ZipArchiveMode.Update))
        {
            var manifestEntry = archive.GetEntry("manifest.json")!;
            System.Text.Json.Nodes.JsonNode manifest;
            using (var reader = new StreamReader(manifestEntry.Open()))
                manifest = System.Text.Json.Nodes.JsonNode.Parse(await reader.ReadToEndAsync())!;
            manifest["ledgerSnapshotSha256"] = Meridian.Contracts.Integrity.Sha256Digest.ComputeUtf8(alteredJson);
            manifest["ledgerBalanceChecksumSha256"] = falseChecksum;
            archive.GetEntry("metadata/ledger.json")!.Delete();
            await using (var ledgerStream = archive.CreateEntry("metadata/ledger.json").Open())
                await ledgerStream.WriteAsync(System.Text.Encoding.UTF8.GetBytes(alteredJson));
            manifestEntry.Delete();
            await using var manifestStream = archive.CreateEntry("manifest.json").Open();
            await manifestStream.WriteAsync(System.Text.Encoding.UTF8.GetBytes(manifest.ToJsonString()));
        }
        var destination = Path.Combine(_root, "invalid-balance-import");
        var result = await packager.ImportPackageAsync(package.PackagePath!, destination, validateChecksums: false);
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("account-balance checksum mismatch");
        Directory.Exists(destination).Should().BeFalse();
    }

    [Theory]
    [InlineData("metadata/ledger.json")]
    [InlineData("metadata/./ledger.json")]
    [InlineData("metadata/part/../ledger.json")]
    [InlineData("metadata\\ledger.json")]
    [InlineData("METADATA/ledger.json")]
    [InlineData("metadata/ledger.json.")]
    [InlineData("metadata/ledger.json ")]
    [InlineData("metadata/ledger.json::$DATA")]
    [InlineData("manifest.json")]
    [InlineData("./manifest.json")]
    [InlineData("MANIFEST.JSON")]
    public async Task PortablePackage_RefusesDuplicateOrAliasedAuthoritiesBeforeImport(string alias)
    {
        var packager = PortableLedgerPackaging.CreatePackager(Path.Combine(_root, "source"));
        var package = await packager.CreatePackageAsync(new()
        {
            Name = "aliased-authority",
            Ledger = PortableLedgerSnapshot.CapturePayload(CreateLedger()),
            OutputDirectory = _root,
            IncludeLoaderScripts = false,
            IncludeDataDictionary = false
        });
        package.Success.Should().BeTrue(package.Error);
        using (var archive = ZipFile.Open(package.PackagePath!, ZipArchiveMode.Update))
        {
            await using var contents = archive.CreateEntry(alias).Open();
            await contents.WriteAsync("{}"u8.ToArray());
        }
        var destination = Path.Combine(_root, "aliased-import");
        var result = await packager.ImportPackageAsync(package.PackagePath!, destination, validateChecksums: false);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("duplicate or aliased reserved entry");
        Directory.Exists(destination).Should().BeFalse();
    }

    [Fact]
    public async Task PortablePackage_RestoresLedgerAndRefusesTamperingBeforeImport()
    {
        var packager = PortableLedgerPackaging.CreatePackager(Path.Combine(_root, "source"));
        var ledger = CreateLedger();
        var result = await packager.CreatePackageAsync(new()
        {
            Name = "ledger",
            Ledger = PortableLedgerSnapshot.CapturePayload(ledger),
            OutputDirectory = _root,
            IncludeLoaderScripts = true,
            IncludeDataDictionary = false
        });
        result.Success.Should().BeTrue(result.Error);
        using (var archive = ZipFile.OpenRead(result.PackagePath!))
        {
            archive.Entries.Count(entry => entry.FullName == "manifest.json").Should().Be(1);
            await using var manifestStream = archive.GetEntry("manifest.json")!.Open();
            using var manifest = await System.Text.Json.JsonDocument.ParseAsync(manifestStream);
            manifest.RootElement.GetProperty("ledgerSnapshotSha256").GetString()
                .Should().Be(result.Manifest!.LedgerSnapshotSha256);
            manifest.RootElement.GetProperty("ledgerBalanceChecksumSha256").GetString()
                .Should().Be(result.Manifest.LedgerBalanceChecksumSha256);
            manifest.RootElement.GetProperty("supplementaryFiles").EnumerateArray()
                .Select(item => item.GetProperty("path").GetString())
                .Should().Contain(["README.md", "scripts/load_data.py", "scripts/load_data.R"]);
        }
        var restored = await packager.ReadLedgerAsync(result.PackagePath!);
        restored!.GetBalance(LedgerAccounts.CashAccount("account-a")).Should().Be(100m);
        restored.Journal.Should().BeEquivalentTo(ledger.Journal, options => options.WithStrictOrdering(),
            "journal and line identities, metadata and every operational dimension must survive portable replay");
        restored.TrialBalance(lineDimensions: ledger.Journal.Single().Lines[0].Dimensions)
            .Should().BeEquivalentTo(ledger.TrialBalance(), "portable replay must rebuild the retained dimensional scope");
        (await packager.ValidatePackageAsync(result.PackagePath!)).IsValid.Should().BeTrue();
        var successfulImport = await packager.ImportPackageAsync(result.PackagePath!, Path.Combine(_root, "verified-import"));
        successfulImport.Success.Should().BeTrue(successfulImport.Error);
        using (var archive = ZipFile.Open(result.PackagePath!, ZipArchiveMode.Update))
        {
            archive.GetEntry("metadata/ledger.json")!.Delete();
            await using var stream = archive.CreateEntry("metadata/ledger.json").Open();
            await stream.WriteAsync("{}"u8.ToArray());
        }
        var destination = Path.Combine(_root, "import");
        var imported = await packager.ImportPackageAsync(result.PackagePath!, destination, validateChecksums: false);
        imported.Success.Should().BeFalse();
        imported.Error.Should().Contain("checksum");
        Directory.Exists(destination).Should().BeFalse();
    }

    private static Meridian.Ledger.Ledger CreateLedger(string description = "Synthetic capital")
    {
        var ledger = new Meridian.Ledger.Ledger();
        var id = Guid.NewGuid();
        var time = Start.AddHours(1);
        var dimensions = new LedgerLineDimensionSet(FundId: "synthetic-fund", EntityId: "synthetic-entity")
        {
            AccountingArrangementId = "synthetic-arrangement",
            AccountingArrangementRevisionId = "synthetic-arrangement-revision-2",
            ChartAccountNodeId = "synthetic-chart-node",
            AccountBindingRevisionId = "synthetic-binding-revision-3",
            IntercompanyTransactionId = "synthetic-intercompany-transaction"
        };
        ledger.Post(new(id, time, description, [
            new(Guid.NewGuid(), id, time, LedgerAccounts.CashAccount("account-a"), 100m, 0m, description, dimensions),
            new(Guid.NewGuid(), id, time, LedgerAccounts.CapitalAccountFor("account-a"), 0m, 100m, description, dimensions)],
            new(ActivityType: "synthetic-capital", FinancialAccountId: "account-a", IdempotencyKey: $"synthetic:{id:N}",
                Tags: new Dictionary<string, string> { ["source"] = "synthetic-portable-fixture" })));
        return ledger;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
