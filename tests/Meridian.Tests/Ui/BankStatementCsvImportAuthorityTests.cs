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
            transaction_date,amount,currency,transaction_type,description,closing_balance,line_sequence,statement_revision,statement_date
            2026-10-07,10,USD,Deposit,Final,100,20,2,2026-10-07
            2026-10-07,10,USD,Deposit,Earlier,90,10,2,2026-10-07
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

    [Fact]
    public void Parse_RevisionedStatementWithoutStatementDate_IsRefused()
    {
        // A correction that re-dates its final row must not derive a new statement identity.
        var csv = """
            transaction_date,amount,currency,transaction_type,description,line_sequence,statement_revision
            2026-10-06,10,USD,Deposit,Corrected,1,2
            """;
        var result = BankStatementCsvImportMapper.Parse(Encoding.UTF8.GetBytes(csv), Guid.NewGuid(), Guid.NewGuid());

        result.Issues.Should().Contain(issue => issue.Severity == "Error" && issue.Field == "statement_date");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Parse_RevisionedStatement_KeepsDeclaredStatementDateIndependentOfValueDates(bool declaredByCaller)
    {
        var statementDate = new DateOnly(2026, 10, 7);
        var csv = declaredByCaller
            ? """
              transaction_date,amount,currency,transaction_type,description,line_sequence,statement_revision
              2026-10-06,10,USD,Deposit,Corrected,1,2
              """
            : """
              transaction_date,amount,currency,transaction_type,description,line_sequence,statement_revision,statement_date
              2026-10-06,10,USD,Deposit,Corrected,1,2,2026-10-07
              """;
        var result = BankStatementCsvImportMapper.Parse(Encoding.UTF8.GetBytes(csv), Guid.NewGuid(), Guid.NewGuid(),
            declaredByCaller ? statementDate : null);

        result.Issues.Should().BeEmpty();
        result.StatementDate.Should().Be(statementDate);
        result.StatementRevision.Should().Be(2);
    }

    [Fact]
    public void BuildBatchId_CorrectedDeclaredStatementDate_IsADistinctBatch()
    {
        var bytes = Encoding.UTF8.GetBytes("""
            transaction_date,amount,currency,transaction_type,description
            2026-10-06,10,USD,Deposit,Wire
            """);
        var account = Guid.NewGuid();
        var legacy = BankStatementCsvImportMapper.BuildBatchId(bytes, account, "Bank");

        BankStatementCsvImportMapper.BuildBatchId(bytes, account, "Bank", null).Should().Be(legacy);
        BankStatementCsvImportMapper.BuildBatchId(bytes, account, "Bank", new DateOnly(2026, 10, 6)).Should().Be(legacy,
            "a declared date matching the file keeps duplicate detection against earlier imports");
        var corrected = BankStatementCsvImportMapper.BuildBatchId(bytes, account, "Bank", new DateOnly(2026, 10, 7));
        corrected.Should().NotBe(legacy, "a corrected statement header must not reuse the original batch");
        BankStatementCsvImportMapper.BuildBatchId(bytes, account, "Bank", new DateOnly(2026, 10, 7)).Should().Be(corrected);
    }
}
