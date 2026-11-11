using FluentAssertions;
using Meridian.Ledger;
using Xunit;

namespace Meridian.Tests.Ledger;

/// <summary>
/// Ledger CSV artifacts open in spreadsheets, so operator-supplied text that looks like a formula
/// must be emitted as text while ordinary values keep their existing bytes.
/// </summary>
public sealed class LedgerReportCsvFormulaGuardTests
{
    private static readonly DateTimeOffset GeneratedAtUtc = new(2026, 4, 1, 9, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("=HYPERLINK(\"https://example.invalid\")", "\"'=HYPERLINK(\"\"https://example.invalid\"\")\"")]
    [InlineData("+cmd", "'+cmd")]
    [InlineData("@SUM(A1)", "'@SUM(A1)")]
    [InlineData("-cmd", "'-cmd")]
    [InlineData("  =1+1", "'=1+1")] // the request trims the actor before export
    public void ReportPackCsv_NeutralizesFormulaLookingText(string generatedBy, string expectedCell)
    {
        var pack = BuildPack(generatedBy);

        pack.Artifacts
            .Where(static artifact => artifact.ContentType == "text/csv")
            .Select(static artifact => artifact.Content)
            .Should().Contain(content => content.Contains($"generated-by,{expectedCell}", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("controller")]
    [InlineData("-1250.50")]
    public void ReportPackCsv_LeavesOrdinaryTextAndNegativeNumbersUnchanged(string generatedBy)
    {
        var pack = BuildPack(generatedBy);

        pack.Artifacts
            .Where(static artifact => artifact.ContentType == "text/csv")
            .Select(static artifact => artifact.Content)
            .Should().Contain(content => content.Contains($"generated-by,{generatedBy}\n", StringComparison.Ordinal)
                || content.Contains($"generated-by,{generatedBy}\r\n", StringComparison.Ordinal));
    }

    private static LedgerFinancialReportPack BuildPack(string generatedBy)
        => LedgerReportPackBuilder.Build(
            LedgerReportPackTestData.BuildLedger(),
            LedgerReportPackTestData.BuildScheduledExport().ToReportPackRequest(generatedBy, GeneratedAtUtc));
}
