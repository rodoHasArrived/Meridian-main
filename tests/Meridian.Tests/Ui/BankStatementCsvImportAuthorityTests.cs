using System.Text;
using FluentAssertions;
using Meridian.Ui.Shared.Services;

namespace Meridian.Tests.Ui;

public sealed class BankStatementCsvImportAuthorityTests
{
    [Fact]
    public void Parse_RetainsExplicitSequenceAndStatementRevision()
    {
        var csv = """
            transaction_date,amount,currency,transaction_type,description,closing_balance,line_sequence,statement_revision
            2026-10-07,10,USD,Deposit,Final,100,20,2
            2026-10-07,10,USD,Deposit,Earlier,90,10,2
            """;
        var result = BankStatementCsvImportMapper.Parse(Encoding.UTF8.GetBytes(csv), Guid.NewGuid(), Guid.NewGuid());

        result.Issues.Should().BeEmpty();
        result.StatementRevision.Should().Be(2);
        result.Lines.Select(line => line.LineSequence).Should().Equal(20, 10);
    }

    [Theory]
    [InlineData("1,2", "1,2")]
    [InlineData("1,2", "2,1")]
    [InlineData("0,2", "2,2")]
    public void Parse_RejectsAmbiguousOrInvalidAuthority(string first, string second)
    {
        var csv = $"""
            transaction_date,amount,currency,transaction_type,description,line_sequence,statement_revision
            2026-10-07,10,USD,Deposit,First,{first}
            2026-10-07,10,USD,Deposit,Second,{second}
            """;
        var result = BankStatementCsvImportMapper.Parse(Encoding.UTF8.GetBytes(csv), Guid.NewGuid(), Guid.NewGuid());

        result.Issues.Should().Contain(issue => issue.Severity == "Error");
    }
}
