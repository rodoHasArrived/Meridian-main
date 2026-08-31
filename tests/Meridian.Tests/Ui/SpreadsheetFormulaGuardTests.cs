using FluentAssertions;
using Meridian.Storage.Export;
using Xunit;

namespace Meridian.Tests.Ui;

public sealed class SpreadsheetFormulaGuardTests
{
    [Theory]
    [InlineData("=SUM(A1:A9)", "'=SUM(A1:A9)")]
    [InlineData("+1+2", "'+1+2")]
    [InlineData("@import", "'@import")]
    [InlineData("  =cmd", "'  =cmd")]
    [InlineData("-cmd|' /C calc'!A0", "'-cmd|' /C calc'!A0")]
    [InlineData("\t=1+1", "'\t=1+1")]
    public void Neutralize_FormulaLookingValues_ArePrefixed(string value, string expected)
    {
        SpreadsheetFormulaGuard.Neutralize(value).Should().Be(expected);
    }

    [Theory]
    [InlineData("-1234.50")]
    [InlineData("-0.01")]
    [InlineData(" -125,000.00")]
    [InlineData("plain text")]
    [InlineData("125.00")]
    [InlineData("")]
    [InlineData("   ")]
    public void Neutralize_SafeValues_AreUnchanged(string value)
    {
        SpreadsheetFormulaGuard.Neutralize(value).Should().Be(value);
    }

    [Theory]
    [InlineData("approved;=1+1", "\"approved;'=1+1\"")]
    [InlineData("reviewed;  @import", "\"reviewed;'  @import\"")]
    [InlineData("plain;second", "\"plain;second\"")]
    [InlineData("line\rreturn", "\"line\rreturn\"")]
    [InlineData("quoted \"value\"", "\"quoted \"\"value\"\"\"")]
    public void EscapeCsvCell_DelimitedOrStructuralText_IsQuotedAndFormulaSafe(
        string value,
        string expected)
    {
        SpreadsheetFormulaGuard.EscapeCsvCell(value).Should().Be(expected);
    }
}
