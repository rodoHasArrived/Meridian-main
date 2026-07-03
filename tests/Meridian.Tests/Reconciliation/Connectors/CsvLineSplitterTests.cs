using FluentAssertions;
using Meridian.FinancialOperations.Reconciliation.Connectors;
using Xunit;

namespace Meridian.Tests.Reconciliation.Connectors;

public sealed class CsvLineSplitterTests
{
    [Fact]
    public void Split_QuotedFieldWithEmbeddedDelimiter_KeepsFieldIntact()
    {
        var values = CsvLineSplitter.Split("\"ACME, LLC\",AAPL,\"1,000\",187.25");

        values.Should().Equal("ACME, LLC", "AAPL", "1,000", "187.25");
    }

    [Fact]
    public void Split_EscapedQuotes_Unescapes()
    {
        var values = CsvLineSplitter.Split("\"He said \"\"buy\"\"\",100");

        values.Should().Equal("He said \"buy\"", "100");
    }

    [Fact]
    public void Split_CustomDelimiter_SplitsOnDelimiterOnly()
    {
        var values = CsvLineSplitter.Split("FUND-B;VTI;25;265.40", ';');

        values.Should().Equal("FUND-B", "VTI", "25", "265.40");
    }

    [Fact]
    public void SplitLines_StripsBomAndNormalizesLineEndings()
    {
        var lines = CsvLineSplitter.SplitLines("﻿header\r\nrow1\rrow2\nrow3");

        lines.Should().Equal("header", "row1", "row2", "row3");
    }

    [Fact]
    public void SplitLines_QuotedFieldWithEmbeddedNewline_KeepsLogicalRecordIntact()
    {
        var lines = CsvLineSplitter.SplitLines("account,memo,amount\r\nFUND-A,\"line one\r\nline two\",10\nFUND-B,ok,20");

        lines.Should().Equal(
            "account,memo,amount",
            "FUND-A,\"line one\r\nline two\",10",
            "FUND-B,ok,20");
    }
}
