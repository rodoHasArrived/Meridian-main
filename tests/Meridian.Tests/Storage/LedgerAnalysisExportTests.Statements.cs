using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using Apache.Arrow;
using Apache.Arrow.Ipc;
using FluentAssertions;
using Meridian.Ledger;
using Meridian.Storage.Export;
using Parquet;

namespace Meridian.Tests.Storage;

public sealed partial class LedgerAnalysisExportTests
{
    [Theory]
    [InlineData(ExportFormat.Csv)]
    [InlineData(ExportFormat.Xlsx)]
    [InlineData(ExportFormat.Parquet)]
    [InlineData(ExportFormat.Arrow)]
    public async Task FinancialStatements_RoundTripExactPeriodIncomeAndScopedAsOfBalances(ExportFormat format)
    {
        const decimal earned = 0.1234567890123456789012345678m;
        const decimal expense = 0.0000000000000000000000000001m;
        var ledger = new Meridian.Ledger.Ledger();
        var cash = LedgerAccounts.CashAccount("account-a");
        var income = new LedgerAccount("Revenue:Fees", LedgerAccountType.Revenue, FinancialAccountId: "account-a");
        var costs = new LedgerAccount("Expenses:Fees", LedgerAccountType.Expense, FinancialAccountId: "account-a");
        PostStatementActivity(ledger, Start.AddDays(-1), cash, income, 0.2m);
        PostStatementActivity(ledger, Start, cash, income, 0.1m);
        PostStatementActivity(ledger, Start.AddDays(1), cash, income, earned);
        PostStatementActivity(ledger, AsOf, costs, cash, expense);
        PostStatementActivity(ledger, AsOf.AddTicks(1), cash, income, 99m);
        PostStatementActivity(ledger, Start.AddDays(2), LedgerAccounts.CashAccount("account-b"),
            income with { FinancialAccountId = "account-b" }, 900m);

        var result = await new AnalysisExportService(_root).ExportLedgerAsync(new(ledger, Start, AsOf,
            _root, format, FinancialAccountId: " ACCOUNT-A "));

        var period = await ReadStatementTableAsync(result, "income-statement", format);
        var incomeRow = period.Rows.Single(row => row["Path"] == "Revenue:Fees");
        incomeRow["DirectBalance"].Should().Be("0.1234567890123456789012345678");
        incomeRow["PeriodStart"].Should().Be(Start.ToString("O", CultureInfo.InvariantCulture));
        incomeRow["AsOf"].Should().Be(AsOf.ToString("O", CultureInfo.InvariantCulture));
        period.Rows.Should().OnlyContain(row => row["SelectedFinancialAccountId"] == "ACCOUNT-A");
        period.Rows.Should().NotContain(row => row["FinancialAccountId"] == "account-b");
        period.Rows.Single(row => row["Path"] == "Expenses:Fees")["DirectBalance"]
            .Should().Be("0.0000000000000000000000000001");

        var balanceSheet = await ReadStatementTableAsync(result, "balance-sheet", format);
        balanceSheet.Rows.Single(row => row["Account"] == "Cash")["DirectBalance"]
            .Should().Be("0.4234567890123456789012345677");
        var summary = (await ReadStatementTableAsync(result, "financial-summary", format)).Rows.Single();
        summary["TotalAssets"].Should().Be("0.4234567890123456789012345677");
        summary["AsOfUnclosedNetIncome"].Should().Be("0.4234567890123456789012345677");
        summary["PeriodNetIncome"].Should().Be("0.1234567890123456789012345677");
        summary["AccountingEquationVariance"].Should().Be("0");
        ledger.JournalEntryCount.Should().Be(6, "exporting must not mutate the source");
    }

    [Theory]
    [InlineData(ExportFormat.Csv)]
    [InlineData(ExportFormat.Xlsx)]
    [InlineData(ExportFormat.Parquet)]
    [InlineData(ExportFormat.Arrow)]
    public async Task EmptyFinancialStatements_RetainSchemasAndZeroSummary(ExportFormat format)
    {
        var result = await new AnalysisExportService(_root).ExportLedgerAsync(new(
            new Meridian.Ledger.Ledger(), Start, AsOf, _root, format));

        var balanceSheet = await ReadStatementTableAsync(result, "balance-sheet", format);
        balanceSheet.Columns.Should().Contain(["AsOf", "Path", "DirectBalance", "AggregateBalance"]);
        balanceSheet.Rows.Should().BeEmpty();
        var income = await ReadStatementTableAsync(result, "income-statement", format);
        income.Columns.Should().Contain(["PeriodStart", "AsOf", "SelectedFinancialAccountId", "DirectBalance"]);
        income.Rows.Should().BeEmpty();
        var summary = (await ReadStatementTableAsync(result, "financial-summary", format)).Rows.Single();
        summary["TotalAssets"].Should().Be("0");
        summary["EndingEquity"].Should().Be("0");
        summary["AsOfUnclosedNetIncome"].Should().Be("0");
        summary["PeriodNetIncome"].Should().Be("0");
    }

    [Fact]
    public async Task IncomeExport_PreservesPeriodResultAfterClosing_AndSeparatesAsOfEquity()
    {
        var ledger = new Meridian.Ledger.Ledger();
        PostStatementActivity(ledger, Start.AddDays(-1), LedgerAccounts.Cash, LedgerAccounts.CashInterestIncome, 400m);
        PostStatementActivity(ledger, Start.AddDays(1), LedgerAccounts.Cash, LedgerAccounts.CashInterestIncome, 100m);
        PostStatementActivity(ledger, Start.AddDays(2), LedgerAccounts.CommissionExpense, LedgerAccounts.Cash, 20m);
        ledger.PostLines(AsOf, "Synthetic close",
            PeriodCloseProjector.ProjectFrom(ledger, "2026-01", AsOf, "synthetic-controller").JournalLines);

        var result = await new AnalysisExportService(_root).ExportLedgerAsync(new(ledger, Start, AsOf, _root));

        var summary = (await ReadStatementTableAsync(result, "financial-summary", ExportFormat.Csv)).Rows.Single();
        summary["PeriodRevenue"].Should().Be("100");
        summary["PeriodExpenses"].Should().Be("20");
        summary["PeriodNetIncome"].Should().Be("80");
        summary["AsOfUnclosedNetIncome"].Should().Be("0");
        summary["TotalEquity"].Should().Be("480");
        summary["EndingEquity"].Should().Be("480");
        summary["AccountingEquationVariance"].Should().Be("0");
    }

    [Fact]
    public async Task FinancialStatements_RejectConflictingChartTypesBeforeCreatingArtifacts()
    {
        var ledger = new Meridian.Ledger.Ledger();
        PostStatementActivity(ledger, Start.AddDays(1), LedgerAccounts.Cash,
            new LedgerAccount("Fees", LedgerAccountType.Revenue), 100m);
        PostStatementActivity(ledger, Start.AddDays(2),
            new LedgerAccount("fees", LedgerAccountType.Expense), LedgerAccounts.Cash, 40m);
        var output = Path.Combine(_root, "conflicting-output");
        var exporter = new AnalysisExportService(_root);

        Func<Task> act = async () => await exporter.ExportLedgerAsync(new(ledger, Start, AsOf, output));

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*conflicting account types*");
        Directory.Exists(output).Should().BeFalse("projection must fail before any output artifact is created");
        ledger.JournalEntryCount.Should().Be(2);
    }

    private static void PostStatementActivity(Meridian.Ledger.Ledger ledger, DateTimeOffset timestamp,
        LedgerAccount debit, LedgerAccount credit, decimal amount)
        => ledger.PostLines(timestamp, "Synthetic financial statement",
            [(debit, amount, 0m), (credit, 0m, amount)]);

    private static async Task<(string[] Columns, List<Dictionary<string, string>> Rows)> ReadStatementTableAsync(
        ExportResult result, string name, ExportFormat format)
    {
        var file = result.Files.Single(file => file.RelativePath == $"{name}.{format.ToString().ToLowerInvariant()}");
        string[] columns;
        var values = new List<string[]>();
        switch (format)
        {
            case ExportFormat.Csv:
                var lines = await File.ReadAllLinesAsync(file.Path);
                columns = lines[0].Split(',');
                values.AddRange(lines.Skip(1).Select(line => line.Split(',')));
                break;
            case ExportFormat.Xlsx:
                using (var zip = ZipFile.OpenRead(file.Path))
                using (var stream = zip.GetEntry("xl/worksheets/sheet1.xml")!.Open())
                {
                    var sheet = await XDocument.LoadAsync(stream, LoadOptions.None, CancellationToken.None);
                    XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
                    values.AddRange(sheet.Descendants(ns + "row")
                        .Select(row => row.Elements(ns + "c").Select(cell => cell.Value).ToArray()));
                    columns = values[0];
                    values.RemoveAt(0);
                }
                break;
            case ExportFormat.Parquet:
                using (var stream = File.OpenRead(file.Path))
                using (var reader = await ParquetReader.CreateAsync(stream))
                using (var group = reader.OpenRowGroupReader(0))
                {
                    var fields = reader.Schema.GetDataFields();
                    columns = fields.Select(field => field.Name).ToArray();
                    var data = new List<string[]>();
                    foreach (var field in fields)
                    {
                        field.ClrType.Should().Be(typeof(string), "accounting Parquet retains exact decimal text");
                        data.Add((string[])(await group.ReadColumnAsync(field)).Data);
                    }
                    for (var row = 0; row < group.RowCount; row++)
                        values.Add(data.Select(column => column[row]).ToArray());
                }
                break;
            case ExportFormat.Arrow:
                using (var stream = File.OpenRead(file.Path))
                using (var reader = new ArrowFileReader(stream))
                using (var batch = await reader.ReadNextRecordBatchAsync())
                {
                    ArgumentNullException.ThrowIfNull(batch);
                    columns = batch.Schema.FieldsList.Select(field => field.Name).ToArray();
                    for (var row = 0; row < batch.Length; row++)
                        values.Add(columns.Select(column => ((StringArray)batch.Column(column)).GetString(row) ?? "").ToArray());
                }
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(format));
        }

        file.RecordCount.Should().Be(values.Count);
        return (columns, values.Select(row => columns.Select((column, index) => (column, row[index]))
            .ToDictionary(cell => cell.column, cell => cell.Item2, StringComparer.Ordinal)).ToList());
    }
}
