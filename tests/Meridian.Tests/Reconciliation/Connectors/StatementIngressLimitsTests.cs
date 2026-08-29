using System.Text;
using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.Reconciliation;
using Meridian.FinancialOperations.Reconciliation.Connectors;
using Meridian.FinancialOperations.Reconciliation.Connectors.Bai2;
using Meridian.FinancialOperations.Reconciliation.Connectors.Camt;
using Meridian.FinancialOperations.Reconciliation.Connectors.Ofx;
using Meridian.Infrastructure.Reconciliation;
using Xunit;

namespace Meridian.Tests.Reconciliation.Connectors;

/// <summary>
/// PRD-010 bounded statement ingress. Before this suite the camt.053 connector decoded the whole
/// payload and built a whole-document <c>XDocument</c>, the BAI2 connector decoded the whole payload
/// and split it on newlines, neither enforced a record limit, and
/// <see cref="StatementImportService"/> copied the source bytes before the connector was even
/// resolved — so a caller-supplied <see cref="StatementSourceDocument"/> sized the parse rather than
/// the operator, and the transport-level upload and CLI caps never covered that seam.
///
/// Every bound is asserted as a refusal with a named issue code, and each format's golden fixture is
/// re-asserted under the real default limits so the bounds did not change what a valid statement
/// parses to.
/// </summary>
public sealed class StatementIngressLimitsTests : IDisposable
{
    // Deliberately tiny so the assertions describe the bound rather than the machine: a test that
    // needed a real 5 MiB payload to prove the byte cap would prove the allocation, not the check.
    private static readonly StatementIngressLimits TightLimits = new(
        MaxDocumentBytes: 512,
        MaxRecords: 2,
        MaxLineBytes: 64,
        MaxNestingDepth: 8);

    private readonly string _root = StatementConnectorTestData.CreateTempRoot("mdc_stmt_ingress");

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // camt.053
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task Camt_DocumentOverByteCap_IsRefusedWithoutParsing()
    {
        var connector = new Camt053StatementConnector(TightLimits);
        var oversize = BuildCamtStatement(entryCount: 200);
        oversize.Length.Should().BeGreaterThan((int)TightLimits.MaxDocumentBytes);

        var result = await connector.ParseAsync(new StatementSourceDocument("big.xml", oversize));

        result.HasErrors.Should().BeTrue();
        result.Records.Should().BeEmpty();
        result.Issues.Should().ContainSingle()
            .Which.Code.Should().Be(StatementIngressLimits.DocumentTooLargeCode);
    }

    [Fact]
    public async Task Camt_RecordsOverCap_AreRefusedRatherThanAccumulated()
    {
        // Byte cap wide enough to admit the document, so the record cap is the bound under test.
        var connector = new Camt053StatementConnector(TightLimits with { MaxDocumentBytes = 1024 * 1024 });
        var document = new StatementSourceDocument("many-entries.xml", BuildCamtStatement(entryCount: 25));

        var result = await connector.ParseAsync(document);

        result.HasErrors.Should().BeTrue();
        result.Records.Should().BeEmpty("a refused document yields no partial canonical rows");
        result.Issues.Should().Contain(issue => issue.Code == StatementIngressLimits.TooManyRecordsCode);
    }

    [Fact]
    public async Task Camt_NestingOverCap_IsRefused()
    {
        var connector = new Camt053StatementConnector(TightLimits with { MaxDocumentBytes = 1024 * 1024 });

        // A single entry whose reference is buried far below the nesting bound. The previous
        // whole-document load would have expanded this before anything could refuse it.
        var nested = new StringBuilder();
        for (var depth = 0; depth < 40; depth++)
        {
            nested.Append("<Wrap>");
        }

        nested.Append("<AcctSvcrRef>DEEP</AcctSvcrRef>");
        for (var depth = 0; depth < 40; depth++)
        {
            nested.Append("</Wrap>");
        }

        var payload = BuildCamtStatement(entryCount: 1, extraEntryXml: nested.ToString());

        var result = await connector.ParseAsync(new StatementSourceDocument("deep.xml", payload));

        result.HasErrors.Should().BeTrue();
        result.Records.Should().BeEmpty();
        result.Issues.Should().Contain(issue => issue.Code == StatementIngressLimits.NestingTooDeepCode);
    }

    [Fact]
    public async Task Camt_GoldenFixture_ParsesIdenticallyUnderDefaultLimits()
    {
        // The streaming rewrite must not change what a valid statement parses to. These are the same
        // figures the pre-existing connector suite asserts against this fixture.
        var connector = new Camt053StatementConnector();
        var document = new StatementSourceDocument(
            "camt053-sample.xml",
            StatementConnectorTestData.ReadFixture("camt053-sample.xml"));

        var result = await connector.ParseAsync(document);

        result.HasErrors.Should().BeFalse();
        result.Records.Should().HaveCount(3);
        result.Records.Should().OnlyContain(record => record.Account == "DE89370400440532013000");
        result.Records.Should().OnlyContain(record => record.Currency == "EUR");

        var balance = result.Records.Single(record => record.Kind == StatementRecordKind.CashBalance);
        balance.CashAmount.Should().Be(12345.67m, "only the closing booked (CLBD) balance is reconciled");
        balance.TradeDate.Should().Be(new DateOnly(2026, 5, 31));

        result.Records.Single(record => record.ExternalTransactionId == "ACCTSVCR-001")
            .CashAmount.Should().Be(2500.00m);
        result.Records.Single(record => record.ExternalTransactionId == "ACCTSVCR-002")
            .CashAmount.Should().Be(-154.33m, "a DBIT entry is negative");
    }

    [Fact]
    public async Task Camt_MultipleStatementsForOneAccount_KeepsItsDiagnostic()
    {
        // The multi-statement guards were written against a whole-document element scan. Streaming
        // resolves them from a bounded first pass, so both diagnostics are re-asserted here.
        var connector = new Camt053StatementConnector();
        var payload = BuildCamtDocument(
            StatementXml("DE89370400440532013000", entryCount: 1),
            StatementXml("DE89370400440532013000", entryCount: 1));

        var result = await connector.ParseAsync(new StatementSourceDocument("two.xml", payload));

        result.HasErrors.Should().BeTrue();
        result.Issues.Should().ContainSingle().Which.Code.Should().Be("CAMT_MULTIPLE_STATEMENTS");
    }

    [Fact]
    public async Task Camt_MultipleAccounts_KeepsItsDiagnostic()
    {
        var connector = new Camt053StatementConnector();
        var payload = BuildCamtDocument(
            StatementXml("DE89370400440532013000", entryCount: 1),
            StatementXml("GB33BUKB20201555555555", entryCount: 1));

        var result = await connector.ParseAsync(new StatementSourceDocument("two.xml", payload));

        result.HasErrors.Should().BeTrue();
        result.Issues.Should().ContainSingle().Which.Code.Should().Be("CAMT_MULTIPLE_ACCOUNTS");
    }

    [Fact]
    public async Task Camt_ElementsAfterTheStatementCloses_AreNotImportedAsStatementContent()
    {
        // Regression: the streaming rewrite latched the statement's depth and never released it, so a
        // wrapper placed after </Stmt> whose children sat at the same depth as the statement's own
        // children was still read as statement content. The element-axis traversal this replaced could
        // not do that, because it only ever walked the Stmt subtree.
        var connector = new Camt053StatementConnector();
        var payload = Encoding.UTF8.GetBytes(
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
            "<Document xmlns=\"urn:iso:std:iso:20022:tech:xsd:camt.053.001.02\"><BkToCstmrStmt>" +
            StatementXml("DE89370400440532013000", entryCount: 1) +
            // Sibling of Stmt. Its Ntry sits at the same depth a real entry would.
            "<Smry><Ntry><Amt Ccy=\"EUR\">999999.99</Amt><CdtDbtInd>CRDT</CdtDbtInd><Sts>BOOK</Sts>" +
            "<BookgDt><Dt>2026-05-10</Dt></BookgDt><AcctSvcrRef>OUTSIDE-STMT</AcctSvcrRef></Ntry></Smry>" +
            "</BkToCstmrStmt></Document>");

        var result = await connector.ParseAsync(new StatementSourceDocument("sibling.xml", payload));

        result.HasErrors.Should().BeFalse();
        result.Records.Should().NotContain(
            record => record.ExternalTransactionId == "OUTSIDE-STMT",
            "an entry outside the Stmt subtree is not statement content");
        result.Records.Should().HaveCount(2, "only the closing balance and the statement's own entry");
    }

    [Fact]
    public async Task Camt_EmptyStatementElement_DoesNotAdoptLaterSiblings()
    {
        // An empty <Stmt/> raises no end element, so the depth latch had nothing to release and the
        // following wrapper's children were read as though they were the statement's.
        var connector = new Camt053StatementConnector();
        var payload = Encoding.UTF8.GetBytes(
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
            "<Document xmlns=\"urn:iso:std:iso:20022:tech:xsd:camt.053.001.02\"><BkToCstmrStmt>" +
            "<Stmt/>" +
            "<Smry><Acct><Id><IBAN>DE89370400440532013000</IBAN></Id><Ccy>EUR</Ccy></Acct>" +
            "<Bal><Tp><CdOrPrtry><Cd>CLBD</Cd></CdOrPrtry></Tp><Amt Ccy=\"EUR\">500.00</Amt>" +
            "<CdtDbtInd>CRDT</CdtDbtInd><Dt><Dt>2026-05-31</Dt></Dt></Bal></Smry>" +
            "</BkToCstmrStmt></Document>");

        var result = await connector.ParseAsync(new StatementSourceDocument("empty-stmt.xml", payload));

        // The statement carries no account of its own, so it is refused rather than silently adopting
        // the sibling's account and balance.
        result.HasErrors.Should().BeTrue();
        result.Records.Should().BeEmpty();
        result.Issues.Should().Contain(issue => issue.Code == "CAMT_MISSING_ACCOUNT_ID");
    }

    [Fact]
    public void DefaultByteCap_MatchesTheStatementImportAllowance_NotTheGeneralUploadCap()
    {
        // Regression: the default was anchored to the general 5 MiB data-upload cap. Statement imports
        // carry their own larger bound because IB Flex XML exports routinely exceed 5 MiB, so anchoring
        // here would have refused every 5-20 MiB statement the endpoint and CLI already accept.
        StatementIngressLimits.Default.MaxDocumentBytes
            .Should().Be(StatementConnectorLimits.MaxFileBytes)
            .And.Be(20L * 1024 * 1024);
    }

    [Fact]
    public async Task Statement_BetweenTheUploadCapAndTheStatementCap_IsStillAccepted()
    {
        // A statement larger than the general 5 MiB data-upload cap but inside the 20 MiB statement
        // allowance must parse, not be refused by the ingress bound.
        var connector = new Bai2StatementConnector();
        // 110,000 detail records is ~5.87 MiB - clear of the 5 MiB general upload cap with margin,
        // and well inside both the 20 MiB byte cap and the 250,000 record cap.
        var payload = BuildBai2Statement(transactionCount: 110_000);
        payload.Length.Should().BeGreaterThan(5 * 1024 * 1024, "the payload must clear the general upload cap");
        payload.Length.Should().BeLessThan((int)StatementIngressLimits.Default.MaxDocumentBytes);

        var result = await connector.ParseAsync(new StatementSourceDocument("large.bai", payload));

        result.Issues.Should().NotContain(issue => issue.Code == StatementIngressLimits.DocumentTooLargeCode);
        result.HasErrors.Should().BeFalse();
    }

    [Fact]
    public async Task Camt_WideButShallowEntry_IsRefusedByTheSubtreeNodeBound()
    {
        // Depth alone did not bound the copy: one shallow Ntry with a very large number of siblings stays
        // inside the nesting bound while expanding, as an XElement graph, far past its own byte size. That
        // is the resource-exhaustion case PRD-010 exists to close, so the subtree carries a node budget.
        var connector = new Camt053StatementConnector(
            TightLimits with { MaxDocumentBytes = 4 * 1024 * 1024, MaxNestingDepth = 64, MaxSubtreeNodes = 500 });

        var wide = new StringBuilder();
        for (var node = 0; node < 2_000; node++)
        {
            wide.Append("<Filler/>");
        }

        var payload = BuildCamtStatement(entryCount: 1, extraEntryXml: wide.ToString());

        var result = await connector.ParseAsync(new StatementSourceDocument("wide.xml", payload));

        result.HasErrors.Should().BeTrue();
        result.Records.Should().BeEmpty();
        result.Issues.Should().Contain(issue => issue.Code == StatementIngressLimits.SubtreeTooLargeCode);
    }

    [Fact]
    public async Task Camt_DeepNesting_StillReportsNestingRatherThanSubtreeSize()
    {
        // The two subtree bounds must stay distinguishable: a deep document reports nesting, not width.
        var connector = new Camt053StatementConnector(
            TightLimits with { MaxDocumentBytes = 1024 * 1024, MaxSubtreeNodes = 50_000 });

        var nested = new StringBuilder();
        for (var depth = 0; depth < 40; depth++)
        {
            nested.Append("<Wrap>");
        }

        nested.Append("<AcctSvcrRef>DEEP</AcctSvcrRef>");
        for (var depth = 0; depth < 40; depth++)
        {
            nested.Append("</Wrap>");
        }

        var result = await connector.ParseAsync(new StatementSourceDocument(
            "deep.xml", BuildCamtStatement(entryCount: 1, extraEntryXml: nested.ToString())));

        result.Issues.Should().Contain(issue => issue.Code == StatementIngressLimits.NestingTooDeepCode);
        result.Issues.Should().NotContain(issue => issue.Code == StatementIngressLimits.SubtreeTooLargeCode);
    }

    [Fact]
    public async Task Validate_RecordsOverCap_AreRefusedForAConnectorThatDoesNotStreamTheBound()
    {
        // camt.053 and BAI2 refuse mid-parse, but every other connector resolves through the same service.
        // The record cap therefore has to hold at the service too, or a format that accumulates rows
        // without counting them could pass a document straight past the configured bound.
        var service = BuildService(
            StatementIngressLimits.Default with { MaxRecords = 2 },
            connectorLimits: StatementIngressLimits.Default);
        var document = new StatementSourceDocument(
            "csv-mixed-kinds.csv",
            StatementConnectorTestData.ReadFixture("csv-mixed-kinds.csv"));

        var validation = await service.ValidateAsync(document, connectorId: null);

        validation.IsValid.Should().BeFalse();
        validation.Errors.Should().ContainSingle().Which.Should().Contain("ingress limit");
    }

    [Fact]
    public async Task Commit_RecordsOverCap_AreRefusedAtTheService()
    {
        var service = BuildService(
            StatementIngressLimits.Default with { MaxRecords = 2 },
            connectorLimits: StatementIngressLimits.Default);
        var document = new StatementSourceDocument(
            "csv-mixed-kinds.csv",
            StatementConnectorTestData.ReadFixture("csv-mixed-kinds.csv"));

        var act = async () => await service.CommitAsync(CommitRequest(document));

        (await act.Should().ThrowAsync<InvalidDataException>())
            .Which.Message.Should().Contain("ingress limit");
    }

    [Fact]
    public async Task Csv_RecordsOverCap_AreRefusedWithoutMappingAnyRow()
    {
        // The connector-side half: CSV received no limits at all before, so it decoded, split, and
        // accumulated every row. A compact CSV inside the byte cap can still carry millions of rows, and
        // the peak allocation is what the bound exists to avoid - rejecting afterwards is too late.
        //
        // This asserted two surviving rows when the guard sat on the record-append loop. The bound now
        // runs during line discovery, ahead of any mapping, so an over-cap file yields no rows at all -
        // a stricter outcome than the one this test originally pinned, and the one the connector should
        // have had from the start.
        var catalog = new StatementMappingProfileCatalog(new FileStatementMappingProfileStore(_root));
        var connector = new CsvStatementConnector(catalog, StatementIngressLimits.Default with { MaxRecords = 2 });
        var document = new StatementSourceDocument(
            "csv-mixed-kinds.csv",
            StatementConnectorTestData.ReadFixture("csv-mixed-kinds.csv"));

        var result = await connector.ParseAsync(document);

        result.Records.Should().BeEmpty("the bound refuses during line discovery, before a single row is mapped");
        result.Issues.Should().Contain(issue => issue.Code == StatementIngressLimits.TooManyRecordsCode);
    }

    [Fact]
    public async Task Camt_AttributeHeavyEntry_IsCountedAgainstTheSubtreeBudget()
    {
        // The node budget counted one node per reader Read(), and attributes are not their own Read().
        // An element carrying a large number of them therefore passed the budget on a single node while
        // still allocating an XAttribute and a name-table entry for each.
        var connector = new Camt053StatementConnector(
            TightLimits with { MaxDocumentBytes = 4 * 1024 * 1024, MaxNestingDepth = 64, MaxSubtreeNodes = 500 });

        var attributes = new StringBuilder("<Bulk");
        for (var index = 0; index < 2_000; index++)
        {
            attributes.Append($" a{index}=\"v\"");
        }

        attributes.Append("/>");

        var result = await connector.ParseAsync(new StatementSourceDocument(
            "attrs.xml", BuildCamtStatement(entryCount: 1, extraEntryXml: attributes.ToString())));

        result.HasErrors.Should().BeTrue();
        result.Records.Should().BeEmpty();
        result.Issues.Should().Contain(issue => issue.Code == StatementIngressLimits.SubtreeTooLargeCode);
    }

    [Fact]
    public async Task Camt_DuplicateAccountElements_AreRefusedRatherThanRestatingIdentity()
    {
        // Pass one keeps the first Acct and the element-axis traversal this replaced took the first too,
        // but pass two was overwriting with the last. A statement whose first account is unauthorized and
        // whose second matches the requested account would then emit every row under the authorized
        // identity and satisfy the import service's account-authority check.
        var connector = new Camt053StatementConnector();
        var payload = Encoding.UTF8.GetBytes(
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
            "<Document xmlns=\"urn:iso:std:iso:20022:tech:xsd:camt.053.001.02\"><BkToCstmrStmt><Stmt>" +
            "<Id>STMT-1</Id>" +
            "<Acct><Id><IBAN>GB33BUKB20201555555555</IBAN></Id><Ccy>EUR</Ccy></Acct>" +
            "<Acct><Id><IBAN>DE89370400440532013000</IBAN></Id><Ccy>EUR</Ccy></Acct>" +
            "<Bal><Tp><CdOrPrtry><Cd>CLBD</Cd></CdOrPrtry></Tp><Amt Ccy=\"EUR\">100.00</Amt>" +
            "<CdtDbtInd>CRDT</CdtDbtInd><Dt><Dt>2026-05-31</Dt></Dt></Bal>" +
            "</Stmt></BkToCstmrStmt></Document>");

        var result = await connector.ParseAsync(new StatementSourceDocument("two-accounts.xml", payload));

        result.HasErrors.Should().BeTrue();
        result.Records.Should().BeEmpty("no row may be emitted under either candidate identity");
        result.Issues.Should().Contain(issue => issue.Code == "CAMT_DUPLICATE_ACCOUNT");
    }

    [Theory]
    [InlineData("header\r\nrow1\rrow2\nrow3", new[] { "header", "row1", "row2", "row3" })]
    [InlineData("a\nb\n", new[] { "a", "b", "" })]
    [InlineData("a\rb", new[] { "a", "b" })]
    [InlineData("", new[] { "" })]
    [InlineData("\n\n", new[] { "", "", "" })]
    [InlineData("\uFEFFa\nb", new[] { "a", "b" })]
    public void BoundedSplitLines_ProducesTheExpectedLines(string content, string[] expected)
    {
        // This asserted equality against the parameterless overload, which now delegates to the bounded
        // implementation - so both sides were the same code and the test would have passed on any
        // mishandling of CRLF, a lone CR, the BOM, blank lines, or the trailing empty segment. It proved
        // nothing. The expectations are now written out independently of the implementation.
        CsvLineSplitter.SplitLines(content, maxLines: int.MaxValue).Should().Equal(expected);
        CsvLineSplitter.SplitLines(content).Should().Equal(expected);
    }

    [Fact]
    public void BoundedSplitLines_StopsAtTheBoundRatherThanMaterializingEveryLine()
    {
        var content = string.Join("\n", Enumerable.Range(0, 5_000).Select(row => $"row{row}"));

        var lines = CsvLineSplitter.SplitLines(content, maxLines: 10);

        lines.Should().HaveCount(11, "the splitter yields one line past the bound so the caller can detect the overflow");
    }

    [Fact]
    public async Task Csv_LineCountOverCap_IsRefusedDuringLineDiscovery()
    {
        // The record cap alone ran after the whole file had been decoded, newline-normalized twice, and
        // split into a full line array, so an over-cap document still paid that allocation first.
        var catalog = new StatementMappingProfileCatalog(new FileStatementMappingProfileStore(_root));
        var connector = new CsvStatementConnector(catalog, StatementIngressLimits.Default with { MaxRecords = 3 });
        // 40 data rows against a cap of 3 - well past the bound on nonblank lines, header included.
        var rows = string.Join("\n", Enumerable.Range(0, 40).Select(row =>
            $"FUND-A,AAPL,1,1.00,-1.00,BUY,2026-06-02,2026-06-04,USD,0,T-{row}"));
        var payload = Encoding.UTF8.GetBytes(
            "account,symbol,quantity,price,cashAmount,activityType,tradeDate,settlementDate,currency,feesCommission,externalTransactionId\n"
            + rows);

        var result = await connector.ParseAsync(new StatementSourceDocument("many-rows.csv", payload));

        result.HasErrors.Should().BeTrue();
        result.Records.Should().BeEmpty("the document is refused before any row is mapped");
        result.Issues.Should().ContainSingle()
            .Which.Code.Should().Be(StatementIngressLimits.TooManyRecordsCode);
    }

    [Fact]
    public async Task Csv_ExactlyTheRecordCapWithHeaderAndTrailingNewline_IsAccepted()
    {
        // Regression for a false positive I introduced: bounding total lines at MaxRecords + 1 refused the
        // ordinary shape, because a header plus exactly MaxRecords rows plus the empty segment a trailing
        // newline leaves is MaxRecords + 2 lines. Acceptance now counts nonblank lines instead.
        var catalog = new StatementMappingProfileCatalog(new FileStatementMappingProfileStore(_root));
        var connector = new CsvStatementConnector(catalog, StatementIngressLimits.Default with { MaxRecords = 4 });
        var rows = string.Join("\n", Enumerable.Range(0, 4).Select(row =>
            $"FUND-A,AAPL,1,1.00,-1.00,BUY,2026-06-02,2026-06-04,USD,0,T-{row}"));
        var payload = Encoding.UTF8.GetBytes(
            "account,symbol,quantity,price,cashAmount,activityType,tradeDate,settlementDate,currency,feesCommission,externalTransactionId\n"
            + rows + "\n");

        var result = await connector.ParseAsync(new StatementSourceDocument("exact.csv", payload));

        result.Issues.Should().NotContain(issue => issue.Code == StatementIngressLimits.TooManyRecordsCode);
        result.Records.Should().HaveCount(4);
    }

    [Fact]
    public async Task Csv_BlankLinesDoNotCountTowardTheRecordBound()
    {
        // Blank lines map to no canonical row, so counting them would refuse ordinary files that merely
        // carry separators or a ragged tail.
        var catalog = new StatementMappingProfileCatalog(new FileStatementMappingProfileStore(_root));
        var connector = new CsvStatementConnector(catalog, StatementIngressLimits.Default with { MaxRecords = 3 });
        var payload = Encoding.UTF8.GetBytes(
            "\n\naccount,symbol,quantity,price,cashAmount,activityType,tradeDate,settlementDate,currency,feesCommission,externalTransactionId\n"
            + "\nFUND-A,AAPL,1,1.00,-1.00,BUY,2026-06-02,2026-06-04,USD,0,T-1\n\n"
            + "FUND-A,MSFT,1,1.00,-1.00,BUY,2026-06-02,2026-06-04,USD,0,T-2\n\n");

        var result = await connector.ParseAsync(new StatementSourceDocument("blanks.csv", payload));

        result.Issues.Should().NotContain(issue => issue.Code == StatementIngressLimits.TooManyRecordsCode);
        result.Records.Should().HaveCount(2);
    }

    [Fact]
    public async Task Csv_SingleOverlongLine_IsRefusedBeforeItsFieldsAreSplit()
    {
        // A line count alone does not bound a CSV: one line can carry the whole document, and splitting
        // its fields then materializes a string and a list entry per delimiter. MaxLineBytes existed but
        // was only ever consulted by BAI2.
        var catalog = new StatementMappingProfileCatalog(new FileStatementMappingProfileStore(_root));
        var connector = new CsvStatementConnector(catalog, StatementIngressLimits.Default with { MaxLineBytes = 256 });
        var wide = string.Join(",", Enumerable.Range(0, 5_000).Select(field => $"f{field}"));
        var payload = Encoding.UTF8.GetBytes("account,symbol,quantity\n" + wide);

        var result = await connector.ParseAsync(new StatementSourceDocument("wide-line.csv", payload));

        result.HasErrors.Should().BeTrue();
        result.Records.Should().BeEmpty();
        result.Issues.Should().ContainSingle()
            .Which.Code.Should().Be(StatementIngressLimits.LineTooLongCode);
    }

    [Fact]
    public void BoundedSplitLines_StopsAtAnOverlongLineWithoutAddingIt()
    {
        var lines = CsvLineSplitter.SplitLines(
            "short\n" + new string('x', 500) + "\nalso-short",
            maxLines: int.MaxValue,
            maxLineLength: 100,
            out var lineTooLong);

        lineTooLong.Should().BeTrue();
        lines.Should().ContainSingle().Which.Should().Be("short", "discovery stops before the overlong line");
    }

    [Fact]
    public async Task Camt_MalformedXml_StillReportsMalformedRatherThanThrowing()
    {
        var connector = new Camt053StatementConnector();
        var payload = Encoding.UTF8.GetBytes("<Document><BkToCstmrStmt><Stmt></BkToCstmrStmt>");

        var result = await connector.ParseAsync(new StatementSourceDocument("bad.xml", payload));

        result.HasErrors.Should().BeTrue();
        result.Issues.Should().Contain(issue => issue.Code == "CAMT_MALFORMED");
    }

    // ---------------------------------------------------------------------------------------------
    // BAI2
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task Bai2_DocumentOverByteCap_IsRefusedWithoutParsing()
    {
        var connector = new Bai2StatementConnector(TightLimits);
        var oversize = BuildBai2Statement(transactionCount: 200);
        oversize.Length.Should().BeGreaterThan((int)TightLimits.MaxDocumentBytes);

        var result = await connector.ParseAsync(new StatementSourceDocument("big.bai", oversize));

        result.HasErrors.Should().BeTrue();
        result.Records.Should().BeEmpty();
        result.Issues.Should().ContainSingle()
            .Which.Code.Should().Be(StatementIngressLimits.DocumentTooLargeCode);
    }

    [Fact]
    public async Task Bai2_LineOverCap_IsRefused()
    {
        var connector = new Bai2StatementConnector(TightLimits with { MaxDocumentBytes = 1024 * 1024 });

        // One 16 record padded past the line bound. Splitting the whole payload on newlines would have
        // materialized this line regardless of its length.
        var padded = "16,115,250000,,BANKREF01,CUSTREF01," + new string('X', 500) + "/";
        var payload = Encoding.UTF8.GetBytes(
            "01,CITIBANK,MERIDIAN,260531,0800,1,,,2/\n" +
            "02,MERIDIAN,CITIBANK,1,260531,,USD,2/\n" +
            "03,0975312468,USD,015,1234567,,/\n" +
            padded + "\n" +
            "49,1234567,3/\n98,1234567,1,3/\n99,1234567,1,5/\n");

        var result = await connector.ParseAsync(new StatementSourceDocument("long-line.bai", payload));

        result.HasErrors.Should().BeTrue();
        result.Records.Should().BeEmpty();
        result.Issues.Should().Contain(issue => issue.Code == StatementIngressLimits.LineTooLongCode);
    }

    [Fact]
    public async Task Bai2_RecordsOverCap_AreRefusedRatherThanAccumulated()
    {
        var connector = new Bai2StatementConnector(
            TightLimits with { MaxDocumentBytes = 1024 * 1024, MaxLineBytes = 64 * 1024 });

        var result = await connector.ParseAsync(
            new StatementSourceDocument("many.bai", BuildBai2Statement(transactionCount: 25)));

        result.HasErrors.Should().BeTrue();
        result.Records.Should().BeEmpty("a refused document yields no partial canonical rows");
        result.Issues.Should().Contain(issue => issue.Code == StatementIngressLimits.TooManyRecordsCode);
    }

    [Fact]
    public async Task Bai2_FileAtExactlyTheRecordCap_IsAcceptedWithItsTrailers()
    {
        // The cap guards canonical rows, not lines. A file holding exactly the permitted number of rows
        // still ends in 49/98/99 trailers, and those must not trip the bound.
        var connector = new Bai2StatementConnector(
            TightLimits with { MaxDocumentBytes = 1024 * 1024, MaxLineBytes = 64 * 1024, MaxRecords = 3 });

        var result = await connector.ParseAsync(
            new StatementSourceDocument("exact.bai", BuildBai2Statement(transactionCount: 2)));

        result.Issues.Should().NotContain(issue => issue.Code == StatementIngressLimits.TooManyRecordsCode);
        result.Records.Should().HaveCount(3, "one closing ledger balance plus two transaction details");
    }

    [Fact]
    public async Task Bai2_GoldenFixture_ParsesIdenticallyUnderDefaultLimits()
    {
        var connector = new Bai2StatementConnector();
        var document = new StatementSourceDocument(
            "bai2-sample.bai",
            StatementConnectorTestData.ReadFixture("bai2-sample.bai"));

        var result = await connector.ParseAsync(document);

        result.HasErrors.Should().BeFalse();
        result.Records.Should().OnlyContain(record => record.Account == "0975312468");
        result.Records.Should().OnlyContain(record => record.Currency == "USD");

        var balance = result.Records.Single(record => record.Kind == StatementRecordKind.CashBalance);
        balance.CashAmount.Should().Be(12345.67m, "BAI2 amounts are minor units scaled to major units");

        var transactions = result.Records.Where(record => record.Kind == StatementRecordKind.Transaction).ToArray();
        transactions.Should().HaveCount(2);
        transactions.Single(record => record.ExternalTransactionId == "CUSTREF01")
            .CashAmount.Should().Be(2500.00m, "type code 115 is a credit");
        transactions.Single(record => record.ExternalTransactionId == "CUSTREF02")
            .CashAmount.Should().Be(-154.33m, "type code 475 is a debit");
    }

    [Fact]
    public async Task Bai2_CarriageReturnLineEndings_ParseTheSameAsNewlineOnly()
    {
        // The previous implementation split a decoded string on '\n' and trimmed each piece. The byte
        // walk has to keep that behaviour for CRLF files, which is what most bank drops actually are.
        var connector = new Bai2StatementConnector();
        var crlf = Encoding.UTF8.GetString(
                StatementConnectorTestData.ReadFixture("bai2-sample.bai"))
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\n", "\r\n", StringComparison.Ordinal);

        var result = await connector.ParseAsync(
            new StatementSourceDocument("crlf.bai", Encoding.UTF8.GetBytes(crlf)));

        result.HasErrors.Should().BeFalse();
        result.Records.Should().HaveCount(3);
    }

    // ---------------------------------------------------------------------------------------------
    // StatementImportService — the byte cap must precede the source-byte copy
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task Commit_OversizeDocument_IsRefusedBeforeTheSourceBytesAreCopied()
    {
        var service = BuildService(TightLimits);
        var document = new StatementSourceDocument("big.bai", BuildBai2Statement(transactionCount: 200));

        var act = async () => await service.CommitAsync(CommitRequest(document));

        (await act.Should().ThrowAsync<InvalidDataException>())
            .Which.Message.Should().Contain("ingress limit");
    }

    [Fact]
    public async Task Validate_OversizeDocument_ReportsTheBoundRatherThanParsing()
    {
        var service = BuildService(TightLimits);
        var document = new StatementSourceDocument("big.bai", BuildBai2Statement(transactionCount: 200));

        var validation = await service.ValidateAsync(document, connectorId: null);

        validation.IsValid.Should().BeFalse();
        validation.RecordCount.Should().Be(0);
        validation.Errors.Should().ContainSingle().Which.Should().Contain("ingress limit");
    }

    [Fact]
    public async Task Preview_OversizeDocument_ReportsTheBoundAsABlockingIssue()
    {
        var service = BuildService(TightLimits);
        var document = new StatementSourceDocument("big.bai", BuildBai2Statement(transactionCount: 200));

        var preview = await service.PreviewAsync(document, connectorId: null);

        preview.Issues.Should().Contain(issue => issue.Code == StatementIngressLimits.DocumentTooLargeCode);
    }

    [Fact]
    public async Task Validate_DocumentWithinTheCap_StillPasses()
    {
        // The guard must refuse only what breaches the bound. A statement well inside it still
        // resolves its connector and validates as before.
        var service = BuildService(StatementIngressLimits.Default);
        var document = new StatementSourceDocument(
            "bai2-sample.bai",
            StatementConnectorTestData.ReadFixture("bai2-sample.bai"));

        var validation = await service.ValidateAsync(document, connectorId: null);

        validation.IsValid.Should().BeTrue();
        validation.RecordCount.Should().Be(3);
        validation.Errors.Should().BeEmpty();
    }

    // ---------------------------------------------------------------------------------------------
    // Harness
    // ---------------------------------------------------------------------------------------------

    private StatementImportService BuildService(
        StatementIngressLimits limits,
        StatementIngressLimits? connectorLimits = null)
    {
        // connectorLimits lets a test give the connectors a looser bound than the service, which is the
        // only way to exercise the service-level cap: a connector that streams the same bound refuses
        // mid-parse and the service check never sees an over-cap result.
        var effectiveConnectorLimits = connectorLimits ?? limits;
        var catalog = new StatementMappingProfileCatalog(new FileStatementMappingProfileStore(_root));
        var registry = new StatementConnectorRegistry(
        [
            new Camt053StatementConnector(effectiveConnectorLimits),
            new Bai2StatementConnector(effectiveConnectorLimits),
            new CsvStatementConnector(catalog, effectiveConnectorLimits)
        ]);

        var statementStore = new JsonCanonicalStatementStore(_root);
        var workflow = StatementRunWorkflowService.CreateEphemeralForTesting(
            statementStore,
            new JsonReconciliationCaseStore(_root),
            new JsonReconciliationBreakStore(_root),
            new CsvBrokerStatementService(statementStore),
            new StatementReconciliationContextAdapter(new StatementReconciliationService()));

        return new StatementImportService(registry, catalog, workflow, _root, limits);
    }

    // ---------------------------------------------------------------------------------------------
    // OFX — the parse has two allocation phases, and a check on the result runs after both
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task Ofx_EntriesOverCap_AreRefusedDuringEntryDiscovery()
    {
        // OfxDocumentParser builds the node tree and then the flattened entry dictionaries, so the
        // service-level record check ran only after both existed. A 250,001-entry OFX file sits well
        // inside the 20 MiB document cap, which is why the bound has to be inside the parse.
        var connector = new OfxStatementConnector(
            Catalog(),
            TightLimits with { MaxDocumentBytes = 1024 * 1024, MaxRecords = 3 });

        var result = await connector.ParseAsync(
            new StatementSourceDocument("many.ofx", BuildOfxStatement(transactionCount: 25)));

        result.HasErrors.Should().BeTrue();
        result.Records.Should().BeEmpty("a refused document yields no partial canonical rows");
        result.Issues.Should().Contain(issue => issue.Code == StatementIngressLimits.TooManyRecordsCode);
    }

    [Fact]
    public async Task Ofx_AtExactlyTheRecordCap_IsAccepted()
    {
        // Guards the off-by-one the CSV path got wrong: the bound must refuse past the cap, not at it.
        var connector = new OfxStatementConnector(
            Catalog(),
            TightLimits with { MaxDocumentBytes = 1024 * 1024, MaxRecords = 5 });

        var result = await connector.ParseAsync(
            new StatementSourceDocument("exact.ofx", BuildOfxStatement(transactionCount: 4)));

        result.Issues.Should().NotContain(issue => issue.Code == StatementIngressLimits.TooManyRecordsCode);
        result.Records.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Ofx_DeeplyNestedAggregates_AreRefusedRatherThanRecursingOverThem()
    {
        // CollectEntries recurses over the node tree, so tree depth is recursion depth. Without a
        // nesting bound a deeply nested document overflows the stack, which no caller can catch —
        // it terminates the process rather than returning a refusal.
        var connector = new OfxStatementConnector(
            Catalog(),
            TightLimits with { MaxDocumentBytes = 1024 * 1024, MaxRecords = 1000 });

        var result = await connector.ParseAsync(
            new StatementSourceDocument("deep.ofx", BuildDeeplyNestedOfx(depth: 200)));

        result.HasErrors.Should().BeTrue();
        result.Issues.Should().Contain(issue => issue.Code == StatementIngressLimits.NestingTooDeepCode);
    }

    [Fact]
    public async Task Ofx_GoldenFixture_ParsesIdenticallyUnderDefaultLimits()
    {
        // The bounds must not change what a real statement parses to.
        var connector = new OfxStatementConnector(Catalog());
        var document = new StatementSourceDocument(
            "ofx-102-bank.ofx",
            StatementConnectorTestData.ReadFixture("ofx-102-bank.ofx"));

        var result = await connector.ParseAsync(document);

        result.HasErrors.Should().BeFalse();
        result.Records.Should().HaveCount(4);
    }

    [Fact]
    public void BoundedOfxParse_StopsOneEntryPastTheBound_SoOverflowIsDetectableByCount()
    {
        var content = Encoding.UTF8.GetString(BuildOfxStatement(transactionCount: 50));

        var parsed = OfxDocumentParser.Parse(
            content, maxEntries: 4, maxDepth: 64, maxNodes: int.MaxValue, out var bound);

        // The bound now fires while the tree is being built, so nothing is flattened at all. That is a
        // stronger outcome than the earlier "one entry past the bound", which only told the caller it had
        // overflowed after every node object already existed.
        bound.Should().Be(OfxParseBound.TooManyEntries);
        parsed.Entries.Should().BeEmpty("the parse stops before entry flattening rather than after it");
    }

    [Fact]
    public void BoundedOfxParse_RefusesBeforeRetainingTheWholeTree()
    {
        // A document of maxEntries + 1 compact entries fits under both the byte cap and the node budget,
        // so counting entries only in CollectEntries let the whole tree be retained first. Counting them
        // during construction is what makes the record bound actually bound allocation.
        var content = Encoding.UTF8.GetString(BuildOfxStatement(transactionCount: 40));

        var parsed = OfxDocumentParser.Parse(
            content, maxEntries: 3, maxDepth: 64, maxNodes: int.MaxValue, out var bound);

        bound.Should().Be(OfxParseBound.TooManyEntries);
        parsed.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task Bai2_MalformedDetailRows_AreBoundedRatherThanAccumulatingWarnings()
    {
        // Every malformed 16 record took a warning branch and returned before the record cap, so a file
        // of them retained one issue object per line without bound. Worse, the issues are warnings: the
        // service could commit a valid closing balance while silently dropping every transaction.
        var connector = new Bai2StatementConnector(
            TightLimits with { MaxDocumentBytes = 1024 * 1024, MaxRecords = 5, MaxLineBytes = 4096 });

        var result = await connector.ParseAsync(
            new StatementSourceDocument("malformed.bai", BuildBai2WithUnparseableAmounts(detailCount: 40)));

        result.HasErrors.Should().BeTrue("an over-cap file is refused, not half-imported");
        result.Records.Should().BeEmpty();
        result.Issues.Should().Contain(issue => issue.Code == StatementIngressLimits.TooManyRecordsCode);
        result.Issues.Count.Should().BeLessThan(40, "diagnostics are bounded, not one per malformed line");
    }

    // ---------------------------------------------------------------------------------------------
    // camt.053 — the first pass must not retain per-statement state
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task Camt_ManyStatements_ReportTheExactCountWithoutRetainingPerStatementState()
    {
        // The scan used to append one list entry per Stmt, so a document of compact <Stmt/> elements
        // allocated in proportion to a count that is certain to be rejected after the second. The
        // diagnostic still has to name the exact number, so the count is kept as an integer.
        var statements = new string[64];
        Array.Fill(statements, "<Stmt/>");
        var connector = new Camt053StatementConnector(
            TightLimits with { MaxDocumentBytes = 1024 * 1024, MaxNestingDepth = 64 });

        var result = await connector.ParseAsync(
            new StatementSourceDocument("many.xml", BuildCamtDocument(statements)));

        var issue = result.Issues.Should().ContainSingle().Subject;
        issue.Code.Should().Be("CAMT_MULTIPLE_STATEMENTS");
        issue.Message.Should().Contain("contains 64 statements", "the exact count survives the bounded scan");
    }

    [Fact]
    public async Task Camt_StatementsForDifferentAccounts_StillReportTheDistinctAccountCount()
    {
        var connector = new Camt053StatementConnector(
            TightLimits with { MaxDocumentBytes = 1024 * 1024, MaxNestingDepth = 64 });

        var result = await connector.ParseAsync(
            new StatementSourceDocument(
                "accounts.xml",
                BuildCamtDocument(
                    StatementXml("DE89370400440532013000", entryCount: 1),
                    StatementXml("FR7630006000011234567890189", entryCount: 1),
                    StatementXml("GB29NWBK60161331926819", entryCount: 1))));

        var issue = result.Issues.Should().ContainSingle().Subject;
        issue.Code.Should().Be("CAMT_MULTIPLE_ACCOUNTS");
        issue.Message.Should().Contain("more than one account");
        issue.Message.Should().NotContain("3 different", "the retained distinct state is capped at two, so the exact count is not substantiable");
    }

    [Fact]
    public async Task Camt_ManyDistinctAccounts_StillChooseTheAccountDiagnosticUnderTheCap()
    {
        // The distinct-account state is capped at the two identifiers needed to pick the branch, so the
        // scan must still select CAMT_MULTIPLE_ACCOUNTS rather than the multi-statement message when far
        // more than two accounts appear.
        var statements = new string[32];
        for (var index = 0; index < statements.Length; index++)
        {
            statements[index] = StatementXml($"DE8937040044053201{index:D4}", entryCount: 0);
        }

        var connector = new Camt053StatementConnector(
            TightLimits with { MaxDocumentBytes = 1024 * 1024, MaxRecords = 100, MaxNestingDepth = 64 });

        var result = await connector.ParseAsync(
            new StatementSourceDocument("many-accounts.xml", BuildCamtDocument(statements)));

        result.Issues.Should().ContainSingle().Which.Code.Should().Be("CAMT_MULTIPLE_ACCOUNTS");
    }

    [Fact]
    public async Task Camt_UndatedClosingBalances_AreBoundedRatherThanAccumulatingWarnings()
    {
        // The third instance of one shape: BAI2 detail rows, then Ntry, now Bal. A CLBD balance with no
        // parseable date is skipped with a warning and never reaches the record cap, so a document of them
        // accumulates issue objects unbounded while one valid balance still lets the import succeed. Both
        // camt row kinds now share a single candidate budget so neither can drift open again.
        var undated = new StringBuilder()
            .Append("<Stmt><Id>STMT-1</Id>")
            .Append("<Acct><Id><IBAN>DE89370400440532013000</IBAN></Id><Ccy>EUR</Ccy></Acct>");

        for (var index = 0; index < 40; index++)
        {
            undated
                .Append("<Bal><Tp><CdOrPrtry><Cd>CLBD</Cd></CdOrPrtry></Tp>")
                .Append("<Amt Ccy=\"EUR\">12345.67</Amt><CdtDbtInd>CRDT</CdtDbtInd></Bal>");
        }

        var connector = new Camt053StatementConnector(
            TightLimits with { MaxDocumentBytes = 1024 * 1024, MaxRecords = 5, MaxNestingDepth = 64 });

        var result = await connector.ParseAsync(
            new StatementSourceDocument("undated.xml", BuildCamtDocument(undated.Append("</Stmt>").ToString())));

        result.HasErrors.Should().BeTrue();
        result.Records.Should().BeEmpty();
        result.Issues.Should().Contain(issue => issue.Code == StatementIngressLimits.TooManyRecordsCode);
        result.Issues.Count.Should().BeLessThan(40, "diagnostics are bounded, not one per skipped balance");
    }

    [Fact]
    public async Task Camt_StatementsWithoutAnAccount_CollapseToOneUnknownIdentity()
    {
        // Statements with no Acct of their own used to contribute a null each, which Distinct collapsed
        // to a single "unknown-account". The set has to behave the same way, or a file of bare <Stmt/>
        // elements would report the multi-account diagnostic instead of the multi-statement one.
        var connector = new Camt053StatementConnector(
            TightLimits with { MaxDocumentBytes = 1024 * 1024, MaxNestingDepth = 64 });

        var result = await connector.ParseAsync(
            new StatementSourceDocument("bare.xml", BuildCamtDocument("<Stmt/>", "<Stmt/>", "<Stmt/>")));

        result.Issues.Should().ContainSingle().Which.Code.Should().Be(
            "CAMT_MULTIPLE_STATEMENTS",
            "three unidentified statements are one unknown account, not three different ones");
    }

    // ---------------------------------------------------------------------------------------------
    // Round seven: bounds that were declared correctly but measured or reported wrongly
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task Csv_BlankLinesOverTheAllocationCap_ReportLineOverflowNotRecordOverflow()
    {
        // Two different claims about a file. Blank lines produce no canonical row but still cost a list
        // entry, so a document can breach the allocation bound while carrying almost no records -
        // reporting that as STATEMENT_TOO_MANY_RECORDS told the operator something untrue. Row numbers
        // are physical line indices, so the blanks cannot simply be dropped to dodge the bound.
        var connector = new CsvStatementConnector(Catalog(), TightLimits with { MaxRecords = 2 });
        var padded = "date,amount,description\n2026-05-01,10.00,one\n" + new string('\n', 32);

        var result = await connector.ParseAsync(
            new StatementSourceDocument("blanks.csv", Encoding.UTF8.GetBytes(padded)));

        var issue = result.Issues.Should().ContainSingle().Subject;
        issue.Code.Should().Be(StatementIngressLimits.TooManyLinesCode);
        issue.Code.Should().NotBe(StatementIngressLimits.TooManyRecordsCode, "one record is not a record overflow");
    }

    [Fact]
    public async Task Csv_MultibyteLineOverTheByteBound_IsRefusedEvenThoughItsCharacterCountIsUnder()
    {
        // MaxLineBytes is a byte bound enforced against a decoded string. A BMP character above U+07FF
        // is three UTF-8 bytes in one UTF-16 unit, so a CJK line measured by character count slips up to
        // 3x past the cap. This line is deliberately under the bound in characters and over it in bytes.
        var connector = new CsvStatementConnector(
            Catalog(),
            TightLimits with { MaxRecords = 100, MaxLineBytes = 600 });
        var wide = "date,amount,description\n2026-05-01,10.00," + new string('\u4e2d', 260) + "\n";

        var result = await connector.ParseAsync(
            new StatementSourceDocument("wide.csv", Encoding.UTF8.GetBytes(wide)));

        Encoding.UTF8.GetByteCount(new string('\u4e2d', 260)).Should().BeGreaterThan(600, "the row is over the byte bound");
        new string('\u4e2d', 260).Length.Should().BeLessThan(600, "but under it measured in characters");
        result.Issues.Should().Contain(issue => issue.Code == StatementIngressLimits.LineTooLongCode);
    }

    [Fact]
    public void BoundedSplitLines_AsciiLineAtTheByteBound_IsStillAccepted()
    {
        // The byte-accurate check must not tighten the ordinary ASCII case, where bytes and characters
        // are the same number.
        var line = new string('a', 64);

        var lines = CsvLineSplitter.SplitLines(line + "\n", maxLines: 10, maxLineLength: 64, out var tooLong);

        tooLong.Should().BeFalse();
        lines.Should().Contain(line);
    }

    [Fact]
    public async Task Ofx_LeafHeavyDocument_IsCountedAgainstTheAllocationBudget()
    {
        // Leaves are retained in a dictionary but were never charged, so a document of uniquely named
        // leaf tags grew the graph without moving the aggregate count - the same hole attributes had in
        // the camt subtree budget.
        var connector = new OfxStatementConnector(
            Catalog(),
            TightLimits with
            {
                MaxDocumentBytes = 4 * 1024 * 1024,
                MaxRecords = 4,
                MaxNestingDepth = 64,
                MaxParseNodes = 100,
            });

        var result = await connector.ParseAsync(
            new StatementSourceDocument("leafy.ofx", BuildLeafHeavyOfx(leafCount: 4000)));

        result.HasErrors.Should().BeTrue();
        result.Issues.Should().Contain(issue => issue.Code == StatementIngressLimits.TooManyNodesCode);
    }

    [Fact]
    public void DefaultNodeBudget_DoesNotScaleWithTheRecordAllowance()
    {
        // It used to be MaxRecords * 32, which made an allocation bound vary with an unrelated knob and,
        // at the default record allowance, put it above what the 20 MiB document cap can even produce -
        // a bound that cannot be reached is not a bound.
        var tightRecords = StatementIngressLimits.Default with { MaxRecords = 10 };
        var looseRecords = StatementIngressLimits.Default with { MaxRecords = 1_000_000 };

        tightRecords.MaxParseNodes.Should().Be(looseRecords.MaxParseNodes);
        StatementIngressLimits.Default.MaxParseNodes.Should().BeLessThan(
            (int)(StatementIngressLimits.Default.MaxDocumentBytes / 4),
            "the budget has to sit below the node count the byte cap alone permits, or it never binds");

        // Below the byte cap is necessary but not sufficient: node count cannot separate one fat hostile
        // entry from many ordinary ones, so the ceiling is set below the legitimate maximum rather than
        // above it. A ceiling above what a real large statement produces is a ceiling that never fires.
        StatementIngressLimits.Default.MaxParseNodes.Should().BeLessThan(
            1_000_000,
            "a ceiling above the legitimate maximum never fires, so this one sits deliberately below it");
    }

    [Fact]
    public async Task Bai2_CrlfLineAtExactlyTheByteBound_IsAcceptedLikeItsLfEquivalent()
    {
        // The CR of a CRLF break is delimiter, not content. Measuring it made the byte ceiling depend on
        // newline convention: the same record was accepted with LF endings and refused with CRLF.
        var lfText = Encoding.UTF8.GetString(BuildBai2Statement(transactionCount: 2));
        var longestLineBytes = lfText.Split('\n').Max(line => Encoding.UTF8.GetByteCount(line));
        var connector = new Bai2StatementConnector(
            TightLimits with
            {
                MaxDocumentBytes = 1024 * 1024,
                MaxRecords = 100,
                MaxLineBytes = longestLineBytes,
            });

        var crlf = await connector.ParseAsync(new StatementSourceDocument(
            "crlf.bai", Encoding.UTF8.GetBytes(lfText.Replace("\n", "\r\n"))));
        var lf = await connector.ParseAsync(new StatementSourceDocument(
            "lf.bai", Encoding.UTF8.GetBytes(lfText)));

        crlf.Issues.Should().NotContain(issue => issue.Code == StatementIngressLimits.LineTooLongCode);
        lf.Issues.Should().NotContain(issue => issue.Code == StatementIngressLimits.LineTooLongCode);
        crlf.Records.Should().HaveCount(lf.Records.Count, "newline convention is not a size difference");
    }

    [Fact]
    public async Task Camt_BalanceBeforeAccount_UsesTheAccountCurrencyNotTheUsdFallback()
    {
        // The other half of the out-of-order fix. Seeding the identity but not the currency left a Bal
        // ahead of Acct, with no Ccy of its own, falling back to USD even though the account says EUR.
        var outOfOrder = new StringBuilder()
            .Append("<Stmt><Id>STMT-1</Id>")
            .Append("<Bal><Tp><CdOrPrtry><Cd>CLBD</Cd></CdOrPrtry></Tp>")
            .Append("<Amt>12345.67</Amt><CdtDbtInd>CRDT</CdtDbtInd>")
            .Append("<Dt><Dt>2026-05-31</Dt></Dt></Bal>")
            .Append("<Acct><Id><IBAN>DE89370400440532013000</IBAN></Id><Ccy>EUR</Ccy></Acct>")
            .Append("</Stmt>")
            .ToString();
        var connector = new Camt053StatementConnector(
            TightLimits with { MaxDocumentBytes = 1024 * 1024, MaxRecords = 100, MaxNestingDepth = 64 });

        var result = await connector.ParseAsync(
            new StatementSourceDocument("currency.xml", BuildCamtDocument(outOfOrder)));

        result.HasErrors.Should().BeFalse();
        result.Records.Should().OnlyContain(record => record.Currency == "EUR");
    }

    [Fact]
    public async Task Camt_BalanceBeforeAccount_StillCarriesTheStatementIdentity()
    {
        // A well-formed-but-malformed statement can place Bal before its own Acct. Pass two used to emit
        // that row with an empty account and learn the identity afterwards, so ValidateAsync called the
        // document valid while CommitAsync rejected the blank-account rows at EnsureParsedAccountAuthority.
        // The identity is now seeded from the first pass, which already resolved it.
        var outOfOrder = new StringBuilder()
            .Append("<Stmt><Id>STMT-1</Id>")
            .Append("<Bal><Tp><CdOrPrtry><Cd>CLBD</Cd></CdOrPrtry></Tp>")
            .Append("<Amt Ccy=\"EUR\">12345.67</Amt><CdtDbtInd>CRDT</CdtDbtInd>")
            .Append("<Dt><Dt>2026-05-31</Dt></Dt></Bal>")
            .Append("<Acct><Id><IBAN>DE89370400440532013000</IBAN></Id><Ccy>EUR</Ccy></Acct>")
            .Append("</Stmt>")
            .ToString();
        var connector = new Camt053StatementConnector(
            TightLimits with { MaxDocumentBytes = 1024 * 1024, MaxRecords = 100, MaxNestingDepth = 64 });

        var result = await connector.ParseAsync(
            new StatementSourceDocument("outoforder.xml", BuildCamtDocument(outOfOrder)));

        result.HasErrors.Should().BeFalse();
        result.Records.Should().NotBeEmpty();
        result.Records.Should().OnlyContain(record => record.Account == "DE89370400440532013000");
    }

    [Fact]
    public async Task Camt_PendingEntries_AreBoundedRatherThanAccumulatingWarnings()
    {
        // The camt twin of the BAI2 finding one round earlier. A pending (PDNG) entry is deliberately
        // skipped with a warning and never reaches the record cap, so a document of them accumulated one
        // issue object per entry unbounded - and because they are warnings the import still succeeded,
        // committing the closing balance while silently dropping every movement. Fixing one connector and
        // not its sibling is what let this survive a round.
        var pending = new StringBuilder()
            .Append("<Stmt><Id>STMT-1</Id>")
            .Append("<Acct><Id><IBAN>DE89370400440532013000</IBAN></Id><Ccy>EUR</Ccy></Acct>")
            .Append("<Bal><Tp><CdOrPrtry><Cd>CLBD</Cd></CdOrPrtry></Tp>")
            .Append("<Amt Ccy=\"EUR\">12345.67</Amt><CdtDbtInd>CRDT</CdtDbtInd>")
            .Append("<Dt><Dt>2026-05-31</Dt></Dt></Bal>");

        for (var index = 0; index < 40; index++)
        {
            pending
                .Append("<Ntry><Amt Ccy=\"EUR\">10.00</Amt><CdtDbtInd>CRDT</CdtDbtInd>")
                .Append("<Sts>PDNG</Sts>")
                .Append("<BookgDt><Dt>2026-05-10</Dt></BookgDt>")
                .Append($"<AcctSvcrRef>PENDING-{index:D4}</AcctSvcrRef></Ntry>");
        }

        var connector = new Camt053StatementConnector(
            TightLimits with { MaxDocumentBytes = 1024 * 1024, MaxRecords = 5, MaxNestingDepth = 64 });

        var result = await connector.ParseAsync(
            new StatementSourceDocument("pending.xml", BuildCamtDocument(pending.Append("</Stmt>").ToString())));

        result.HasErrors.Should().BeTrue("an over-cap file is refused, not committed with the balance alone");
        result.Records.Should().BeEmpty();
        result.Issues.Should().Contain(issue => issue.Code == StatementIngressLimits.TooManyRecordsCode);
        result.Issues.Count.Should().BeLessThan(40, "diagnostics are bounded, not one per skipped entry");
    }

    [Fact]
    public async Task Camt_EntryBeforeBalance_StillEmitsBalanceFirstSoTheCanonicalIdentityIsStable()
    {
        // The element-axis parser ran Elements(statement, "Bal") to completion before iterating any Ntry,
        // so canonical order was balance-first regardless of document order. Streaming in source order
        // changed that silently - and record order feeds RenderCanonicalArtifact, whose hash is half of the
        // retained-evidence uploadId, so the same source bytes would produce a different identity after the
        // upgrade and open a duplicate reconciliation run on re-import.
        var outOfOrder = new StringBuilder()
            .Append("<Stmt><Id>STMT-1</Id>")
            .Append("<Acct><Id><IBAN>DE89370400440532013000</IBAN></Id><Ccy>EUR</Ccy></Acct>")
            .Append("<Ntry><Amt Ccy=\"EUR\">2500.00</Amt><CdtDbtInd>CRDT</CdtDbtInd><Sts>BOOK</Sts>")
            .Append("<BookgDt><Dt>2026-05-10</Dt></BookgDt><ValDt><Dt>2026-05-11</Dt></ValDt>")
            .Append("<AcctSvcrRef>ENTRY-0001</AcctSvcrRef></Ntry>")
            .Append("<Bal><Tp><CdOrPrtry><Cd>CLBD</Cd></CdOrPrtry></Tp>")
            .Append("<Amt Ccy=\"EUR\">12345.67</Amt><CdtDbtInd>CRDT</CdtDbtInd>")
            .Append("<Dt><Dt>2026-05-31</Dt></Dt></Bal>")
            .Append("</Stmt>")
            .ToString();
        var connector = new Camt053StatementConnector(
            TightLimits with { MaxDocumentBytes = 1024 * 1024, MaxRecords = 100, MaxNestingDepth = 64 });

        var result = await connector.ParseAsync(
            new StatementSourceDocument("order.xml", BuildCamtDocument(outOfOrder)));

        result.HasErrors.Should().BeFalse();
        result.Records.Should().HaveCount(2);
        result.Records[0].Kind.Should().Be(
            StatementRecordKind.CashBalance,
            "balances precede entries in canonical order no matter where they sit in the source");
        result.Records[1].Kind.Should().Be(StatementRecordKind.Transaction);
    }

    [Fact]
    public async Task Camt_InformationalBalances_AreNotChargedToTheRecordBudget()
    {
        // OPBD and ITBD balances are filtered out before they can emit a record or a diagnostic, so
        // charging them to the record budget refused an ordinary statement: two informational balances
        // plus one valid closing balance is one canonical row, not three.
        var withInformational = new StringBuilder()
            .Append("<Stmt><Id>STMT-1</Id>")
            .Append("<Acct><Id><IBAN>DE89370400440532013000</IBAN></Id><Ccy>EUR</Ccy></Acct>")
            .Append("<Bal><Tp><CdOrPrtry><Cd>OPBD</Cd></CdOrPrtry></Tp>")
            .Append("<Amt Ccy=\"EUR\">100.00</Amt><CdtDbtInd>CRDT</CdtDbtInd><Dt><Dt>2026-05-01</Dt></Dt></Bal>")
            .Append("<Bal><Tp><CdOrPrtry><Cd>ITBD</Cd></CdOrPrtry></Tp>")
            .Append("<Amt Ccy=\"EUR\">200.00</Amt><CdtDbtInd>CRDT</CdtDbtInd><Dt><Dt>2026-05-15</Dt></Dt></Bal>")
            .Append("<Bal><Tp><CdOrPrtry><Cd>CLBD</Cd></CdOrPrtry></Tp>")
            .Append("<Amt Ccy=\"EUR\">12345.67</Amt><CdtDbtInd>CRDT</CdtDbtInd><Dt><Dt>2026-05-31</Dt></Dt></Bal>")
            .Append("</Stmt>")
            .ToString();
        var connector = new Camt053StatementConnector(
            TightLimits with { MaxDocumentBytes = 1024 * 1024, MaxRecords = 2, MaxNestingDepth = 64 });

        var result = await connector.ParseAsync(
            new StatementSourceDocument("informational.xml", BuildCamtDocument(withInformational)));

        result.Issues.Should().NotContain(issue => issue.Code == StatementIngressLimits.TooManyRecordsCode);
        result.Records.Should().ContainSingle().Which.Kind.Should().Be(StatementRecordKind.CashBalance);
    }

    private static StatementImportCommitRequest CommitRequest(StatementSourceDocument document)
        => new(
            document,
            ConnectorId: null,
            // "broker" and "custodian" are the only kinds NormalizeSourceKind accepts. This said "bank",
            // which throws before the record cap is ever reached - the byte cap happens to be checked
            // ahead of that guard, so the oversize-document tests passed and hid it.
            SourceKind: "broker",
            SourceInstitution: "Citibank",
            FundAccountId: "FUND-A",
            ExternalAccountId: "0975312468",
            PeriodStart: new DateOnly(2026, 5, 1),
            PeriodEnd: new DateOnly(2026, 5, 31),
            ToleranceProfileId: null,
            ImportedBy: "test-operator");

    private static byte[] BuildBai2Statement(int transactionCount)
    {
        var builder = new StringBuilder()
            .Append("01,CITIBANK,MERIDIAN,260531,0800,1,,,2/\n")
            .Append("02,MERIDIAN,CITIBANK,1,260531,,USD,2/\n")
            .Append("03,0975312468,USD,015,1234567,,/\n");

        for (var index = 0; index < transactionCount; index++)
        {
            builder.Append(
                $"16,115,250000,,BANKREF{index:D4},CUSTREF{index:D4},Incoming wire/\n");
        }

        builder.Append("49,1234567,3/\n98,1234567,1,3/\n99,1234567,1,5/\n");
        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    private static byte[] BuildCamtStatement(int entryCount, string extraEntryXml = "")
        => BuildCamtDocument(StatementXml("DE89370400440532013000", entryCount, extraEntryXml));

    private static byte[] BuildCamtDocument(params string[] statements)
    {
        var builder = new StringBuilder()
            .Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>")
            .Append("<Document xmlns=\"urn:iso:std:iso:20022:tech:xsd:camt.053.001.02\">")
            .Append("<BkToCstmrStmt>")
            .Append("<GrpHdr><MsgId>MERIDIAN-CAMT-1</MsgId><CreDtTm>2026-05-31T23:59:00</CreDtTm></GrpHdr>");

        foreach (var statement in statements)
        {
            builder.Append(statement);
        }

        builder.Append("</BkToCstmrStmt></Document>");
        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    private static string StatementXml(string iban, int entryCount, string extraEntryXml = "")
    {
        var builder = new StringBuilder()
            .Append("<Stmt><Id>STMT-1</Id>")
            .Append($"<Acct><Id><IBAN>{iban}</IBAN></Id><Ccy>EUR</Ccy></Acct>")
            .Append("<Bal><Tp><CdOrPrtry><Cd>CLBD</Cd></CdOrPrtry></Tp>")
            .Append("<Amt Ccy=\"EUR\">12345.67</Amt><CdtDbtInd>CRDT</CdtDbtInd>")
            .Append("<Dt><Dt>2026-05-31</Dt></Dt></Bal>");

        for (var index = 0; index < entryCount; index++)
        {
            builder
                .Append("<Ntry><Amt Ccy=\"EUR\">2500.00</Amt><CdtDbtInd>CRDT</CdtDbtInd>")
                .Append("<Sts>BOOK</Sts>")
                .Append("<BookgDt><Dt>2026-05-10</Dt></BookgDt>")
                .Append("<ValDt><Dt>2026-05-11</Dt></ValDt>")
                .Append($"<AcctSvcrRef>ACCTSVCR-{index:D4}</AcctSvcrRef>")
                .Append(extraEntryXml)
                .Append("</Ntry>");
        }

        return builder.Append("</Stmt>").ToString();
    }

    private StatementMappingProfileCatalog Catalog()
        => new(new FileStatementMappingProfileStore(_root));

    private static byte[] BuildOfxStatement(int transactionCount)
    {
        var builder = new StringBuilder()
            .Append("OFXHEADER:100\nDATA:OFXSGML\nVERSION:102\nSECURITY:NONE\n")
            .Append("ENCODING:USASCII\nCHARSET:1252\nCOMPRESSION:NONE\nOLDFILEUID:NONE\nNEWFILEUID:NONE\n\n")
            .Append("<OFX>\n<BANKMSGSRSV1>\n<STMTTRNRS>\n<STMTRS>\n<CURDEF>USD\n")
            .Append("<BANKACCTFROM>\n<ACCTID>FUND-A-CASH\n<ACCTTYPE>CHECKING\n</BANKACCTFROM>\n")
            .Append("<BANKTRANLIST>\n");

        for (var index = 0; index < transactionCount; index++)
        {
            builder
                .Append("<STMTTRN>\n<TRNTYPE>CREDIT\n<DTPOSTED>20260603120000\n")
                .Append("<TRNAMT>100.00\n")
                .Append($"<FITID>OFX-{index:D6}\n<NAME>Wire credit\n</STMTTRN>\n");
        }

        builder.Append("</BANKTRANLIST>\n</STMTRS>\n</STMTTRNRS>\n</BANKMSGSRSV1>\n</OFX>\n");
        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    private static byte[] BuildDeeplyNestedOfx(int depth)
    {
        var builder = new StringBuilder()
            .Append("OFXHEADER:100\nDATA:OFXSGML\nVERSION:102\n\n<OFX>\n");

        for (var level = 0; level < depth; level++)
        {
            builder.Append("<WRAPPER>\n");
        }

        for (var level = 0; level < depth; level++)
        {
            builder.Append("</WRAPPER>\n");
        }

        return Encoding.UTF8.GetBytes(builder.Append("</OFX>\n").ToString());
    }

    private static byte[] BuildLeafHeavyOfx(int leafCount)
    {
        var builder = new StringBuilder()
            .Append("OFXHEADER:100\nDATA:OFXSGML\nVERSION:102\n\n")
            .Append("<OFX>\n<BANKMSGSRSV1>\n<STMTTRNRS>\n<STMTRS>\n");

        for (var index = 0; index < leafCount; index++)
        {
            builder.Append($"<LEAF{index:D6}>value-{index}\n");
        }

        return Encoding.UTF8.GetBytes(builder.Append("</STMTRS>\n</STMTTRNRS>\n</BANKMSGSRSV1>\n</OFX>\n").ToString());
    }


    // Header and trailers identical to BuildBai2Statement, so the only difference under test is that
    // every 16 record carries an amount that cannot parse.
    private static byte[] BuildBai2WithUnparseableAmounts(int detailCount)
    {
        var builder = new StringBuilder()
            .Append("01,CITIBANK,MERIDIAN,260531,0800,1,,,2/\n")
            .Append("02,MERIDIAN,CITIBANK,1,260531,,USD,2/\n")
            .Append("03,0975312468,USD,015,1234567,,/\n");

        for (var index = 0; index < detailCount; index++)
        {
            builder.Append(
                $"16,115,NOT-A-NUMBER,,BANKREF{index:D4},CUSTREF{index:D4},Incoming wire/\n");
        }

        builder.Append("49,1234567,3/\n98,1234567,1,3/\n99,1234567,1,5/\n");
        return Encoding.UTF8.GetBytes(builder.ToString());
    }

}
