using System.Text;
using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.Execution.Sdk;
using Meridian.FinancialOperations.Reconciliation;
using Meridian.FinancialOperations.Reconciliation.Connectors;
using Meridian.FinancialOperations.Reconciliation.Connectors.Alpaca;
using Meridian.FinancialOperations.Reconciliation.Connectors.Bai2;
using Meridian.FinancialOperations.Reconciliation.Connectors.Camt;
using Meridian.FinancialOperations.Reconciliation.Connectors.IbFlex;
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
    public async Task Csv_RecordsOverCap_AreRefused()
    {
        // The connector-side half: CSV received no limits at all before, so it decoded, split, and
        // accumulated every row. A compact CSV inside the byte cap can still carry millions of rows, and
        // the peak allocation is what the bound exists to avoid - rejecting afterwards is too late.
        //
        // This briefly asserted that an over-cap file yielded NO rows, because the guard was moved to line
        // discovery on the argument that stricter was better. That guard predicted one record per nonblank
        // line, which is false whenever MapRecord rejects a row, so it refused valid files; it is gone and
        // the assertion is back to the bounded-append outcome. Allocation is still bounded - by the raw
        // line cap and by the append loop - so nothing is given up by not predicting.
        var catalog = new StatementMappingProfileCatalog(new FileStatementMappingProfileStore(_root));
        var connector = new CsvStatementConnector(catalog, StatementIngressLimits.Default with { MaxRecords = 2 });
        var document = new StatementSourceDocument(
            "csv-mixed-kinds.csv",
            StatementConnectorTestData.ReadFixture("csv-mixed-kinds.csv"));

        var result = await connector.ParseAsync(document);

        result.Records.Should().HaveCount(2, "the append loop stops at the cap rather than predicting from line count");
        result.Issues.Should().Contain(issue => issue.Code == StatementIngressLimits.TooManyRecordsCode);
    }

    [Fact]
    public async Task Csv_MalformedRows_AreNotCountedAsRecordsTheyNeverBecome()
    {
        // One valid row and three malformed ones, against a cap of three: the parse retains a single
        // record and three diagnostics, both inside their bounds. The removed precheck counted five
        // nonblank lines - header included - against MaxRecords + 1 and refused the file outright.
        var connector = new CsvStatementConnector(
            Catalog(),
            TightLimits with { MaxDocumentBytes = 1024 * 1024, MaxRecords = 3, MaxLineBytes = 4096 });
        var csv = "account,symbol,quantity,price,cashAmount,activityType,tradeDate\n"
            + "ACC-1,AAPL,10,100.00,-1000.00,trade,2026-05-01\n"
            + "ACC-1,AAPL,10,100.00,-1000.00,trade,not-a-date\n"
            + "ACC-1,AAPL,10,100.00,-1000.00,trade,also-bad\n"
            + "ACC-1,AAPL,10,100.00,-1000.00,trade,still-bad\n";

        var result = await connector.ParseAsync(
            new StatementSourceDocument("mixed.csv", Encoding.UTF8.GetBytes(csv)));

        result.Issues.Should().NotContain(issue => issue.Code == StatementIngressLimits.TooManyRecordsCode);
        result.Records.Should().ContainSingle("only the well-formed row becomes a canonical record");
    }

    [Fact]
    public async Task Csv_MalformedRows_AreNotChargedToTheLineBudgetThroughTheRecordCap()
    {
        // The sibling above proved rejected rows are not charged to MaxRecords directly. They were still
        // charged to it indirectly: the line budget was MaxRecords * 2 + 4, so at MaxRecords = 1 a header,
        // one valid row and five unparseable ones is seven lines against a cap of six and the file was
        // refused as STATEMENT_TOO_MANY_LINES - while the parse it would have produced retains exactly one
        // record and five diagnostics, both inside their own bounds. The line budget is MaxDocumentLines
        // now, so nothing about a rejected row reaches the record allowance by any route.
        var connector = new CsvStatementConnector(
            Catalog(),
            TightLimits with { MaxDocumentBytes = 1024 * 1024, MaxRecords = 1, MaxLineBytes = 4096 });
        var csv = "account,symbol,quantity,price,cashAmount,activityType,tradeDate\n"
            + "ACC-1,AAPL,10,100.00,-1000.00,trade,2026-05-01\n"
            + "ACC-1,AAPL,10,100.00,-1000.00,trade,not-a-date\n"
            + "ACC-1,AAPL,10,100.00,-1000.00,trade,also-bad\n"
            + "ACC-1,AAPL,10,100.00,-1000.00,trade,still-bad\n"
            + "ACC-1,AAPL,10,100.00,-1000.00,trade,bad-again\n"
            + "ACC-1,AAPL,10,100.00,-1000.00,trade,worse-yet\n";

        var result = await connector.ParseAsync(
            new StatementSourceDocument("mixed-tight.csv", Encoding.UTF8.GetBytes(csv)));

        result.Issues.Should().NotContain(issue => issue.Code == StatementIngressLimits.TooManyLinesCode);
        result.Issues.Should().NotContain(issue => issue.Code == StatementIngressLimits.TooManyRecordsCode);
        result.Records.Should().ContainSingle("only the well-formed row becomes a canonical record");
    }

    [Fact]
    public void CsvLineBudget_IsItsOwnLimitRatherThanARecordMultiple()
    {
        // Pins the separation itself, the way the BAI2 twin does: the line budget must not move when the
        // record allowance does, or the derivation creeps back in.
        var tightRecords = StatementIngressLimits.Default with { MaxRecords = 10 };
        var looseRecords = StatementIngressLimits.Default with { MaxRecords = 1_000_000 };

        tightRecords.MaxDocumentLines.Should().Be(looseRecords.MaxDocumentLines);
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
        //
        // This expected TOO_MANY_RECORDS while a nonblank-line precheck predicted one record per line and
        // fired ahead of the raw-line cap. That precheck refused valid files - a rejected row becomes no
        // record - so it is gone, and at these deliberately tight limits the raw-line cap is what catches
        // this file: forty-one lines against a hardLineCap of ten. TOO_MANY_LINES is the claim that is
        // true of the document; the parser has not mapped a row and cannot honestly say how many records
        // it would produce. Record overflow still reports itself as record overflow whenever the line cap
        // does not dominate, which is the ordinary case at real limits - Csv_RecordsOverCap_AreRefused
        // covers exactly that, and at the default cap a 300,000-row file stays far inside hardLineCap.
        var catalog = new StatementMappingProfileCatalog(new FileStatementMappingProfileStore(_root));
        var connector = new CsvStatementConnector(
            catalog,
            StatementIngressLimits.Default with { MaxRecords = 3, MaxDocumentLines = 10 });
        // 41 lines against a line budget of 10. The budget is set explicitly because it is no longer
        // derived from MaxRecords - deriving it charged rows the mapper rejects to the record allowance.
        var rows = string.Join("\n", Enumerable.Range(0, 40).Select(row =>
            $"FUND-A,AAPL,1,1.00,-1.00,BUY,2026-06-02,2026-06-04,USD,0,T-{row}"));
        var payload = Encoding.UTF8.GetBytes(
            "account,symbol,quantity,price,cashAmount,activityType,tradeDate,settlementDate,currency,feesCommission,externalTransactionId\n"
            + rows);

        var result = await connector.ParseAsync(new StatementSourceDocument("many-rows.csv", payload));

        result.HasErrors.Should().BeTrue();
        result.Records.Should().BeEmpty("the document is refused before any row is mapped");
        result.Issues.Should().ContainSingle()
            .Which.Code.Should().Be(StatementIngressLimits.TooManyLinesCode);
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
    public async Task Commit_OversizeDocument_CarriesTheStableCodeInItsMessage()
    {
        // Preview and validate return the issue objects, so a caller can route on issue.Code. Commit
        // reports by throwing, and the message carried only prose - so the same document produced an
        // actionable STATEMENT_DOCUMENT_TOO_LARGE from one path and an unclassifiable sentence from the
        // other. The code is the part a client can branch on, so it belongs in the text when the text is
        // all that survives the seam.
        var service = BuildService(TightLimits);
        var document = new StatementSourceDocument("big.bai", BuildBai2Statement(transactionCount: 200));

        var act = async () => await service.CommitAsync(CommitRequest(document));

        (await act.Should().ThrowAsync<InvalidDataException>())
            .Which.Message.Should().Contain(StatementIngressLimits.DocumentTooLargeCode);
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
    public async Task Validate_OversizeDocument_CarriesTheStableCodeInItsErrorText()
    {
        // StatementImportValidationResult.Errors is a list of strings, not issue objects - so validate is
        // lossy in exactly the way commit was, and fixing only commit left the CLI's validate path unable
        // to identify the bound. Preview is the one path that returns the code as its own field.
        var service = BuildService(TightLimits);
        var document = new StatementSourceDocument("big.bai", BuildBai2Statement(transactionCount: 200));

        var validation = await service.ValidateAsync(document, connectorId: null);

        validation.Errors.Should().ContainSingle()
            .Which.Should().Contain(StatementIngressLimits.DocumentTooLargeCode);
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
    // StatementImportService — the record cap must count evidence rows, not just canonical records
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void TotalRetainedRows_CountsEveryRetainedCollection_NotJustRecords()
    {
        // Records was the only collection the cap ever looked at. Five sibling collections are
        // retained just as durably, so the total is what the bound has to be expressed in.
        var parse = EvidenceHeavyParse(recordCount: 1, taxLotCount: 4, snapshotCount: 3);

        parse.Records.Should().HaveCount(1);
        parse.TotalRetainedRows.Should().Be(8);
    }

    [Fact]
    public void TotalRetainedRows_TreatsAbsentCollectionsAsEmpty()
    {
        // The five evidence collections are optional and default to null; a connector that fills none
        // of them must total exactly its record count rather than throwing.
        var parse = EvidenceHeavyParse(recordCount: 3, taxLotCount: 0, snapshotCount: 0);

        parse.AccountSnapshots.Should().BeNull();
        parse.TaxLots.Should().BeNull();
        parse.TotalRetainedRows.Should().Be(3);
    }

    [Fact]
    public async Task Commit_ParseUnderTheRecordCapButOverItInEvidenceRows_IsRefused()
    {
        // The regression this closes: one canonical record and a flood of evidence rows passed a cap
        // written as Records.Count, and every evidence row was still serialized into the retained
        // artifact. Six retained rows against a cap of two must refuse.
        var service = BuildServiceWith(
            TightLimits with { MaxDocumentBytes = 1024 * 1024, MaxRecords = 2 },
            new EvidenceHeavyConnector(recordCount: 1, taxLotCount: 5));
        var document = new StatementSourceDocument("evidence.heavy", "irrelevant"u8.ToArray());

        var act = async () => await service.CommitAsync(CommitRequest(document));

        (await act.Should().ThrowAsync<InvalidDataException>())
            .Which.Message.Should().Contain("above the ingress limit");
    }

    [Fact]
    public async Task Preview_ParseOverTheCapInEvidenceRowsAlone_ReportsTheRecordBound()
    {
        var service = BuildServiceWith(
            TightLimits with { MaxDocumentBytes = 1024 * 1024, MaxRecords = 2 },
            new EvidenceHeavyConnector(recordCount: 1, taxLotCount: 5));
        var document = new StatementSourceDocument("evidence.heavy", "irrelevant"u8.ToArray());

        var preview = await service.PreviewAsync(document, connectorId: null);

        preview.Issues.Should().Contain(issue => issue.Code == StatementIngressLimits.TooManyRecordsCode);
    }

    [Fact]
    public async Task Preview_ParseWithEvidenceRowsInsideTheCap_ReportsNoRecordBound()
    {
        // The cap counts more than it used to, so it must not now refuse what it used to allow: six
        // retained rows against a cap of ten stays inside the bound. Asserted through Preview rather
        // than Commit because a commit that clears the cap goes on to the account-authority guard,
        // which rejects this synthetic identity - the test would then pass for the wrong reason.
        var service = BuildServiceWith(
            TightLimits with { MaxDocumentBytes = 1024 * 1024, MaxRecords = 10 },
            new EvidenceHeavyConnector(recordCount: 1, taxLotCount: 5));
        var document = new StatementSourceDocument("evidence.heavy", "irrelevant"u8.ToArray());

        var preview = await service.PreviewAsync(document, connectorId: null);

        preview.Issues.Should().NotContain(issue => issue.Code == StatementIngressLimits.TooManyRecordsCode);
    }

    [Fact]
    public async Task Commit_IbFlexSnapshotsBeyondTheCap_AreRefusedByTheSharedTotal()
    {
        // A real connector on the same seam. This Flex report holds one trade and five
        // AccountInformation anchors, so Records.Count is 1 while the retained total is at least 6. The
        // connector's own 100,000-row guard is far above a document this small; the service bound is
        // what has to catch it, which is the point of expressing the cap on the parse result.
        var service = BuildServiceWith(
            TightLimits with { MaxDocumentBytes = 1024 * 1024, MaxRecords = 2 },
            new IbFlexStatementConnector(Catalog()));
        var document = new StatementSourceDocument("ib-flex-many-accounts.xml", BuildIbFlexWithAccountInformation(5));

        var act = async () => await service.CommitAsync(CommitRequest(document));

        (await act.Should().ThrowAsync<InvalidDataException>())
            .Which.Message.Should().Contain("above the ingress limit");
    }

    [Fact]
    public async Task Parse_IbFlexAccountInformationAnchors_AreRetainedAsSnapshots()
    {
        // Establishes the count the cap is acting on: the anchors really do become retained snapshot
        // DTOs that Records never sees, so the previous cap could not have observed them.
        var connector = new IbFlexStatementConnector(Catalog());
        var document = new StatementSourceDocument("ib-flex-many-accounts.xml", BuildIbFlexWithAccountInformation(5));

        var result = await connector.ParseAsync(document);

        result.HasErrors.Should().BeFalse();
        result.Records.Should().HaveCount(1);
        result.AccountSnapshots.Should().HaveCount(5);

        // Not an exact total: the connector fills all five evidence collections, so the single trade
        // also yields an activity event and a cursor. Pinning a literal here would pin the connector's
        // evidence shape rather than the claim under test, which is that the anchors are retained rows
        // the old Records.Count cap could not see.
        result.TotalRetainedRows.Should().BeGreaterThan(result.Records.Count);
        result.TotalRetainedRows.Should().BeGreaterThanOrEqualTo(result.Records.Count + result.AccountSnapshots!.Count);
    }

    [Fact]
    public async Task Bai2_MalformedDetails_AreNotChargedToTheRecordBudget()
    {
        // Both row kinds must share one budget - a file could otherwise hold MaxRecords balances AND
        // MaxRecords details against two independent counters - but the budget has to count rows the
        // parse retains, not rows it attempts. This document retains exactly one: the closing balance.
        // Its two malformed 16 details take a warning branch and append nothing, so at a cap of one it
        // must import. Charging them as candidates refused it, reporting a record overflow against a
        // file that produced a single record.
        var connector = new Bai2StatementConnector(
            TightLimits with { MaxDocumentBytes = 1024 * 1024, MaxRecords = 1, MaxLineBytes = 4096 });

        var result = await connector.ParseAsync(
            new StatementSourceDocument("shared-budget.bai", BuildBai2WithUnparseableAmounts(detailCount: 2)));

        result.Issues.Should().NotContain(issue => issue.Code == StatementIngressLimits.TooManyRecordsCode);
        result.Records.Should().ContainSingle().Which.Kind.Should().Be(StatementRecordKind.CashBalance);
        result.Issues.Should().Contain(issue => issue.Code == "BAI2_BAD_AMOUNT", "the malformed details are still reported");
    }

    [Fact]
    public async Task Bai2_BalanceAndDetailsInsideTheSharedBudget_StillParse()
    {
        // The shared budget must not refuse what fits. The same document under a cap of ten parses its
        // closing balance and reports the malformed details as warnings, exactly as before.
        var connector = new Bai2StatementConnector(
            TightLimits with { MaxDocumentBytes = 1024 * 1024, MaxRecords = 10, MaxLineBytes = 4096 });

        var result = await connector.ParseAsync(
            new StatementSourceDocument("within-budget.bai", BuildBai2WithUnparseableAmounts(detailCount: 2)));

        result.Issues.Should().NotContain(issue => issue.Code == StatementIngressLimits.TooManyRecordsCode);
    }

    [Fact]
    public async Task IbFlex_RowLimit_ComesFromTheConfiguredLimitsRatherThanAPrivateConstant()
    {
        // The connector carried its own 100,000-row ceiling while the module README documents the
        // shared StatementIngressLimits as the one place a deployment raises a cap - so raising
        // MaxRecords left a legitimate Flex report refused at row 100,001 by a number nothing could
        // configure. Proven from the cheap side: a lowered cap must refuse a six-row report, which a
        // hardcoded 100,000 never would.
        var connector = new IbFlexStatementConnector(
            Catalog(),
            limits: TightLimits with { MaxDocumentBytes = 1024 * 1024, MaxRecords = 2 });

        var result = await connector.ParseAsync(
            new StatementSourceDocument("ib-flex-tight.xml", BuildIbFlexWithAccountInformation(5)));

        result.HasErrors.Should().BeTrue();
        result.Issues.Should().Contain(issue => issue.Code == "ROW_LIMIT_EXCEEDED");
    }

    [Fact]
    public async Task IbFlex_WithoutExplicitLimits_UsesTheSharedDefault()
    {
        // The limits argument is optional so existing composition keeps working; omitting it must give
        // the shared default rather than no bound at all. Six retained rows are far inside 250,000.
        var connector = new IbFlexStatementConnector(Catalog());

        var result = await connector.ParseAsync(
            new StatementSourceDocument("ib-flex-default.xml", BuildIbFlexWithAccountInformation(5)));

        result.HasErrors.Should().BeFalse();
        result.Issues.Should().NotContain(issue => issue.Code == "ROW_LIMIT_EXCEEDED");
    }

    [Fact]
    public async Task Camt_ManyShallowElementsOutsideTheStatement_BreachTheNodeBudget()
    {
        // MaxParseNodes existed from the start and only OFX ever charged it. Depth bounds how deep the
        // document goes and MaxSubtreeNodes bounds one materialized subtree, but neither bounds how many
        // nodes the scan walks - so uniquely named shallow elements outside the statement were read by
        // both passes, with the reader's name table retaining every distinct name string.
        var connector = new Camt053StatementConnector(
            TightLimits with { MaxDocumentBytes = 4 * 1024 * 1024, MaxNestingDepth = 64, MaxParseNodes = 200 });

        var result = await connector.ParseAsync(
            new StatementSourceDocument("node-flood.xml", BuildCamtWithTrailingNoise(elementCount: 400)));

        result.HasErrors.Should().BeTrue();
        result.Issues.Should().Contain(issue => issue.Code == StatementIngressLimits.TooManyNodesCode);
    }

    [Fact]
    public async Task Camt_DocumentInsideTheNodeBudget_StillParses()
    {
        // The node bound must not refuse an ordinary statement: the golden fixture parses under a budget
        // far below the 500,000 default but comfortably above what one real statement walks.
        // MaxRecords is raised too: TightLimits caps records at 2 and this statement carries a closing
        // balance plus three entries, so without it the record cap refuses the document and the test
        // passes or fails for a reason that has nothing to do with the node budget.
        var connector = new Camt053StatementConnector(
            TightLimits with
            {
                MaxDocumentBytes = 4 * 1024 * 1024,
                MaxRecords = 100,
                MaxNestingDepth = 64,
                MaxParseNodes = 10_000
            });

        var result = await connector.ParseAsync(
            new StatementSourceDocument("camt.xml", BuildCamtStatement(entryCount: 3)));

        result.Issues.Should().NotContain(issue => issue.Code == StatementIngressLimits.TooManyNodesCode);
        result.Records.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Camt_SubtreeRoots_AreChargedToTheDocumentBudgetOnlyOnce()
    {
        // The outer walk charges every element it reads, including the Acct, Bal and Ntry roots it then
        // hands to TryReadBoundedSubtree - whose own first Read() lands on that same element and charged
        // it again. Each subtree root was therefore billed twice against MaxParseNodes, so a document
        // whose real node total sits inside the budget could still be refused.
        //
        // This statement charges 132 nodes when the roots are double-billed and 127 when they are not,
        // so a budget of 129 separates the two: it fails before the fix and passes after. The per-subtree
        // budget is untouched - a subtree really does contain its own root.
        var connector = new Camt053StatementConnector(
            TightLimits with
            {
                MaxDocumentBytes = 4 * 1024 * 1024,
                MaxRecords = 100,
                MaxNestingDepth = 64,
                MaxParseNodes = 129
            });

        var result = await connector.ParseAsync(
            new StatementSourceDocument("camt-roots.xml", BuildCamtStatement(entryCount: 3)));

        result.Issues.Should().NotContain(issue => issue.Code == StatementIngressLimits.TooManyNodesCode);
        result.Records.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Camt_ManyAttributesOutsideTheStatement_AreChargedToTheNodeBudget()
    {
        // Read() visits element nodes, never their attributes. TryReadBoundedSubtree charges attributes
        // for the Acct, Bal, and Ntry subtrees it materializes, but those are the only elements handed to
        // it - every other element in the document is read by the outer walk alone, which charged exactly
        // one node per Read(). So an attribute flood parked outside the statement grew the reader's name
        // table one uncounted XName and value string at a time while the budget saw three nodes.
        var connector = new Camt053StatementConnector(
            TightLimits with
            {
                MaxDocumentBytes = 4 * 1024 * 1024,
                MaxRecords = 100,
                MaxNestingDepth = 64,
                MaxParseNodes = 300
            });

        var result = await connector.ParseAsync(
            new StatementSourceDocument(
                "camt-attr-flood.xml", BuildCamtWithAttributeHeavyNoise(attributeCount: 600)));

        result.HasErrors.Should().BeTrue();
        result.Issues.Should().Contain(issue => issue.Code == StatementIngressLimits.TooManyNodesCode);
    }

    [Fact]
    public async Task Camt_OrdinaryAttributeUse_IsNotRefusedByTheNodeBudget()
    {
        // The negative control for the charge above, under the same budget and the same document shape.
        // Counting the attribute axis must not refuse a statement carrying the attributes a real camt
        // document has - the namespace declaration, the XML declaration's pseudo-attributes, and Ccy on
        // every amount. A charge that refuses ordinary attribute use is not a tighter bound, it is a
        // parser that rejects valid statements.
        var connector = new Camt053StatementConnector(
            TightLimits with
            {
                MaxDocumentBytes = 4 * 1024 * 1024,
                MaxRecords = 100,
                MaxNestingDepth = 64,
                MaxParseNodes = 300
            });

        var result = await connector.ParseAsync(
            new StatementSourceDocument(
                "camt-ordinary-attrs.xml", BuildCamtWithAttributeHeavyNoise(attributeCount: 4)));

        result.Issues.Should().NotContain(issue => issue.Code == StatementIngressLimits.TooManyNodesCode);
        result.Records.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Bai2_TightRecordCap_DoesNotRefuseALineHeavyDocument()
    {
        // The other half of the decoupling asserted by Bai2_LineBudget_IsItsOwnLimitRatherThanARecordMultiple,
        // which proves MaxDocumentLines still bites under a loose record cap. This proves the converse: a
        // tight record cap must not refuse a line-heavy file. The line budget was once MaxRecords * 2 + 4,
        // a formula lifted from CsvStatementConnector where one line is one record. BAI2's 88 continuation
        // lines produce no record at all, so deriving the line allowance from the record allowance refused
        // legal statements - at MaxRecords = 2 this forty-six-line file was capped at eight lines while
        // retaining a single closing balance. Until the budgets were separated this test asserted the
        // refusal as correct, which is why it kept passing while the bound was wrong.
        var connector = new Bai2StatementConnector(
            TightLimits with { MaxDocumentBytes = 1024 * 1024, MaxRecords = 2, MaxLineBytes = 4096 });

        var result = await connector.ParseAsync(
            new StatementSourceDocument("unknown.bai", BuildBai2WithUnknownRecordTypes(unknownCount: 40)));

        result.Issues.Should().NotContain(issue => issue.Code == StatementIngressLimits.TooManyLinesCode);
        result.Records.Should().ContainSingle().Which.Kind.Should().Be(StatementRecordKind.CashBalance);
    }

    [Fact]
    public async Task IbFlex_TradesChargeBothTheRecordAndTheActivityEvent()
    {
        // Each trade appends a canonical record AND an activity event, but the guard charged one. Two
        // trades retain four rows plus the cursor, so a cap of three must refuse - under the previous
        // one-charge-per-iteration counter this document passed at two.
        var connector = new IbFlexStatementConnector(
            Catalog(),
            limits: TightLimits with { MaxDocumentBytes = 1024 * 1024, MaxRecords = 3 });

        var result = await connector.ParseAsync(
            new StatementSourceDocument("ib-flex-trades.xml", BuildIbFlexWithTrades(tradeCount: 2)));

        result.HasErrors.Should().BeTrue();
        result.Issues.Should().Contain(issue => issue.Code == "ROW_LIMIT_EXCEEDED");
    }

    [Fact]
    public async Task IbFlex_DocumentSize_ComesFromTheConfiguredLimits()
    {
        // The byte ceiling was a private 32 MiB constant three lines from the row ceiling I had already
        // converted. A configured cap below the document size must now refuse it.
        var connector = new IbFlexStatementConnector(
            Catalog(),
            limits: TightLimits with { MaxDocumentBytes = 64, MaxRecords = 1000 });

        var result = await connector.ParseAsync(
            new StatementSourceDocument("ib-flex-big.xml", BuildIbFlexWithTrades(tradeCount: 2)));

        result.HasErrors.Should().BeTrue();
        result.Issues.Should().Contain(issue => issue.Code == StatementIngressLimits.DocumentTooLargeCode);
    }

    [Fact]
    public async Task IbFlex_ManyTinyElements_AreRefusedBeforeTheTreeIsMaterialized()
    {
        // MaxCharactersInDocument bounds the characters read, not the object graph built from them, so a
        // permitted payload of many tiny elements expanded into a much larger XDocument with nothing to
        // stop it. The pre-scan refuses before any XElement exists.
        var connector = new IbFlexStatementConnector(
            Catalog(),
            limits: TightLimits with { MaxDocumentBytes = 1024 * 1024, MaxRecords = 1000, MaxParseNodes = 20 });

        var result = await connector.ParseAsync(
            new StatementSourceDocument("ib-flex-nodes.xml", BuildIbFlexWithAccountInformation(50)));

        result.HasErrors.Should().BeTrue();
        result.Issues.Should().Contain(issue => issue.Code == StatementIngressLimits.TooManyNodesCode);
    }

    [Fact]
    public async Task IbFlex_GoldenFixture_ParsesInsideTheNodeBudget()
    {
        // The pre-scan must not refuse a real report. MaxRecords is raised alongside MaxParseNodes here
        // deliberately: TightLimits caps records at 2, and a negative control that trips a different
        // bound proves nothing about the one under test.
        var connector = new IbFlexStatementConnector(
            Catalog(),
            limits: TightLimits with { MaxDocumentBytes = 1024 * 1024, MaxRecords = 1000, MaxParseNodes = 100_000 });

        var result = await connector.ParseAsync(
            new StatementSourceDocument(
                "ib-flex-sample.xml",
                StatementConnectorTestData.ReadFixture("ib-flex-sample.xml")));

        result.Issues.Should().NotContain(issue => issue.Code == StatementIngressLimits.TooManyNodesCode);
        result.Records.Should().NotBeEmpty();
    }

    [Fact]
    public async Task IbFlex_ManyUnmappableRows_AreRefusedByTheDiagnosticBudget()
    {
        // The loops charge two objects per row - the record and the activity they expect it to produce -
        // but a row rejected for its date produces no record while retaining its ROW_INVALID_DATE error,
        // and a row carrying an activity code no profile maps retains an UNKNOWN_ACTIVITY_CODE warning
        // too. The dedupe on that warning is per distinct code, so distinct codes defeat it. Issues are
        // retained in the parse result and projected into the preview exactly like records, so the row
        // charge undercounted what the document keeps by up to a factor of two.
        var connector = new IbFlexStatementConnector(
            Catalog(),
            limits: TightLimits with
            {
                MaxDocumentBytes = 1024 * 1024,
                MaxRecords = 1000,
                MaxDiagnostics = 20
            });

        var result = await connector.ParseAsync(
            new StatementSourceDocument(
                "ib-flex-unmappable.xml", BuildIbFlexCashTransactions(count: 40, unmappable: true)));

        result.HasErrors.Should().BeTrue();
        result.Issues.Should().Contain(issue => issue.Code == StatementIngressLimits.TooManyDiagnosticsCode);
    }

    [Fact]
    public async Task IbFlex_AFewUnmappableRows_AreNotRefusedByTheDiagnosticBudget()
    {
        // Same document shape and same budget as the refusal above, with five rows instead of forty: the
        // bound has to be proportional to what is retained, not triggered by the shape. A statement with
        // a handful of unmapped codes is an ordinary mapping-profile gap the operator fixes from these
        // very warnings - refusing it would destroy the diagnostics that explain it.
        var connector = new IbFlexStatementConnector(
            Catalog(),
            limits: TightLimits with
            {
                MaxDocumentBytes = 1024 * 1024,
                MaxRecords = 1000,
                MaxDiagnostics = 20
            });

        var result = await connector.ParseAsync(
            new StatementSourceDocument(
                "ib-flex-few-unmappable.xml", BuildIbFlexCashTransactions(count: 5, unmappable: true)));

        result.Issues.Should().NotContain(issue => issue.Code == StatementIngressLimits.TooManyDiagnosticsCode);
    }

    [Fact]
    public async Task IbFlex_ManyCleanRows_AreNotRefusedByTheDiagnosticBudget()
    {
        // The sharpest control: the same forty rows and the same budget as the refusal, differing only in
        // data quality. The dates parse under the profile's yyyyMMdd format and the rows share one
        // activity code, so the dedupe leaves at most one warning for the whole file. If this were
        // refused, the bound would be reacting to document size - which MaxRecords already governs -
        // rather than to retained diagnostics.
        var connector = new IbFlexStatementConnector(
            Catalog(),
            limits: TightLimits with
            {
                MaxDocumentBytes = 1024 * 1024,
                MaxRecords = 1000,
                MaxDiagnostics = 20
            });

        var result = await connector.ParseAsync(
            new StatementSourceDocument(
                "ib-flex-clean.xml", BuildIbFlexCashTransactions(count: 40, unmappable: false)));

        result.Issues.Should().NotContain(issue => issue.Code == StatementIngressLimits.TooManyDiagnosticsCode);
        result.Records.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Bai2_ManyMalformedTransactions_AreRefusedByTheDiagnosticBudget()
    {
        // A 16 detail with an unparseable amount takes a warning branch and produces no record, so the
        // record cap never sees it. The diagnostic ceiling was documented as a whole-parse bound while
        // this connector could still retain one warning per malformed row up to MaxRecords - ten times
        // the ceiling. MaxRecords is deliberately large here so the refusal can only come from the
        // diagnostic budget.
        var connector = new Bai2StatementConnector(
            TightLimits with
            {
                MaxDocumentBytes = 1024 * 1024,
                MaxRecords = 1000,
                MaxLineBytes = 4096,
                MaxDiagnostics = 10
            });

        var result = await connector.ParseAsync(
            new StatementSourceDocument("bad-details.bai", BuildBai2WithMalformedTransactions(count: 40)));

        result.HasErrors.Should().BeTrue();
        result.Issues.Should().Contain(issue => issue.Code == StatementIngressLimits.TooManyDiagnosticsCode);
        result.Issues.Should().NotContain(issue => issue.Code == StatementIngressLimits.TooManyRecordsCode);
    }

    [Fact]
    public async Task Bai2_AFewMalformedTransactions_AreNotRefusedByTheDiagnosticBudget()
    {
        // The control at the same budget: three bad details are an ordinary data-quality problem the
        // operator resolves from the warnings themselves, and the valid closing balance must still
        // import. Refusing here would destroy the diagnostics that explain the file.
        var connector = new Bai2StatementConnector(
            TightLimits with
            {
                MaxDocumentBytes = 1024 * 1024,
                MaxRecords = 1000,
                MaxLineBytes = 4096,
                MaxDiagnostics = 10
            });

        var result = await connector.ParseAsync(
            new StatementSourceDocument("few-bad-details.bai", BuildBai2WithMalformedTransactions(count: 3)));

        result.Issues.Should().NotContain(issue => issue.Code == StatementIngressLimits.TooManyDiagnosticsCode);
        result.Records.Should().ContainSingle().Which.Kind.Should().Be(StatementRecordKind.CashBalance);
    }

    [Fact]
    public async Task Bai2_OneDiagnosticPastTheBudget_IsStillRefused()
    {
        // The in-loop guard sits at the candidate charge, which runs before that row's own warning, so a
        // file whose last row takes the count to MaxDiagnostics + 1 leaves the loop with no later
        // iteration to catch it. Eleven malformed details against a budget of ten is exactly that case:
        // without the post-loop check this returns eleven warnings and a valid balance, and the import
        // service accepts it because it bounds retained rows and does not count issues.
        var connector = new Bai2StatementConnector(
            TightLimits with
            {
                MaxDocumentBytes = 1024 * 1024,
                MaxRecords = 1000,
                MaxLineBytes = 4096,
                MaxDiagnostics = 10
            });

        var result = await connector.ParseAsync(
            new StatementSourceDocument("eleven-bad.bai", BuildBai2WithMalformedTransactions(count: 11)));

        result.HasErrors.Should().BeTrue();
        result.Issues.Should().Contain(issue => issue.Code == StatementIngressLimits.TooManyDiagnosticsCode);
    }

    [Fact]
    public async Task Bai2_ExactlyTheDiagnosticBudget_IsAccepted()
    {
        // The other side of that boundary, and the guard against fixing the off-by-one by refusing one
        // diagnostic too early: ten malformed details against a budget of ten must still import the
        // valid closing balance.
        var connector = new Bai2StatementConnector(
            TightLimits with
            {
                MaxDocumentBytes = 1024 * 1024,
                MaxRecords = 1000,
                MaxLineBytes = 4096,
                MaxDiagnostics = 10
            });

        var result = await connector.ParseAsync(
            new StatementSourceDocument("ten-bad.bai", BuildBai2WithMalformedTransactions(count: 10)));

        result.Issues.Should().NotContain(issue => issue.Code == StatementIngressLimits.TooManyDiagnosticsCode);
        result.Records.Should().ContainSingle().Which.Kind.Should().Be(StatementRecordKind.CashBalance);
    }

    [Fact]
    public async Task Alpaca_OverTheByteCap_IsRefusedBeforeDeserializing()
    {
        // This connector carried no ingress limits at all, so a direct in-process caller could hand it a
        // document of any size and JsonSerializer.Deserialize would build the whole object graph.
        var connector = new AlpacaActivityStatementConnector(
            Catalog(), [], [], TightLimits with { MaxDocumentBytes = 64, MaxRecords = 1000 });

        var result = await connector.ParseAsync(
            new StatementSourceDocument("big.json", BuildAlpacaSnapshot(cashTransactionCount: 5)));

        result.HasErrors.Should().BeTrue();
        result.Issues.Should().Contain(issue => issue.Code == StatementIngressLimits.DocumentTooLargeCode);
    }

    [Fact]
    public async Task Alpaca_ManyUnmappedActivityCodes_AreRefusedByTheDiagnosticBudget()
    {
        // ResolveActivity dedupes UNKNOWN_ACTIVITY_CODE per distinct code, so distinct transaction types
        // defeat the dedupe and retain one warning each with nothing bounding them.
        var connector = new AlpacaActivityStatementConnector(
            Catalog(),
            [],
            [],
            TightLimits with { MaxDocumentBytes = 1024 * 1024, MaxRecords = 1000, MaxDiagnostics = 10 });

        var result = await connector.ParseAsync(
            new StatementSourceDocument("many-codes.json", BuildAlpacaSnapshot(cashTransactionCount: 40)));

        result.HasErrors.Should().BeTrue();
        result.Issues.Should().Contain(issue => issue.Code == StatementIngressLimits.TooManyDiagnosticsCode);
    }

    [Fact]
    public async Task Alpaca_AFewUnmappedActivityCodes_AreNotRefused()
    {
        // The control at the same budget: a handful of unmapped codes is an ordinary mapping-profile gap
        // the operator closes from these warnings, and the rows must still import. The INVALID_SNAPSHOT
        // assertion is deliberate - it makes a malformed fixture report itself rather than surfacing as
        // an empty record set that looks like a bound firing.
        var connector = new AlpacaActivityStatementConnector(
            Catalog(),
            [],
            [],
            TightLimits with { MaxDocumentBytes = 1024 * 1024, MaxRecords = 1000, MaxDiagnostics = 10 });

        var result = await connector.ParseAsync(
            new StatementSourceDocument("few-codes.json", BuildAlpacaSnapshot(cashTransactionCount: 3)));

        result.Issues.Should().NotContain(issue => issue.Code == "INVALID_SNAPSHOT");
        result.Issues.Should().NotContain(issue => issue.Code == StatementIngressLimits.TooManyDiagnosticsCode);
        result.Records.Should().NotBeEmpty();
    }

    [Fact]
    public async Task IbFlex_RejectedRows_AreNotChargedForRecordsTheyNeverProduced()
    {
        // The loops used to charge two rows per cash transaction before mapping it, but MapRecord returns
        // null for an unparseable date, so a rejected row retains only its activity DTO. Two such rows
        // plus the cursor is three retained rows - exactly the cap - yet the precharge reached four on
        // the second row and refused a document that was inside the bound.
        var connector = new IbFlexStatementConnector(
            Catalog(),
            limits: TightLimits with { MaxDocumentBytes = 1024 * 1024, MaxRecords = 3 });

        var result = await connector.ParseAsync(
            new StatementSourceDocument(
                "ib-flex-rejected.xml", BuildIbFlexCashTransactions(count: 2, unmappable: true)));

        result.Issues.Should().NotContain(issue => issue.Code == "ROW_LIMIT_EXCEEDED");
    }

    [Fact]
    public async Task Alpaca_ManyJsonMembersOnOneActivity_AreRefusedBeforeDeserializing()
    {
        // The byte cap bounds the document, not the graph built from it, and MaxRecords sees one activity
        // however many members it carries. Deserialize would materialize every metadata pair first, so
        // the bound has to be checked while walking tokens - the JSON analogue of the IB Flex pre-scan.
        var connector = new AlpacaActivityStatementConnector(
            Catalog(),
            [],
            [],
            TightLimits with { MaxDocumentBytes = 1024 * 1024, MaxRecords = 1000, MaxParseNodes = 500 });

        var result = await connector.ParseAsync(
            new StatementSourceDocument(
                "metadata-flood.json",
                BuildAlpacaSnapshot(cashTransactionCount: 1, metadataProperties: 2000)));

        result.HasErrors.Should().BeTrue();
        result.Issues.Should().Contain(issue => issue.Code == StatementIngressLimits.TooManyNodesCode);
    }

    [Fact]
    public async Task Alpaca_OrdinaryMetadata_IsNotRefusedByTheTokenBudget()
    {
        // The control at the same budget and the same document shape: metadata is a normal part of an
        // Alpaca snapshot, and a few entries must not be mistaken for a flood.
        var connector = new AlpacaActivityStatementConnector(
            Catalog(),
            [],
            [],
            TightLimits with { MaxDocumentBytes = 1024 * 1024, MaxRecords = 1000, MaxParseNodes = 500 });

        var result = await connector.ParseAsync(
            new StatementSourceDocument(
                "ordinary-metadata.json",
                BuildAlpacaSnapshot(cashTransactionCount: 3, metadataProperties: 4)));

        result.Issues.Should().NotContain(issue => issue.Code == "INVALID_SNAPSHOT");
        result.Issues.Should().NotContain(issue => issue.Code == StatementIngressLimits.TooManyNodesCode);
        result.Records.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Alpaca_RetainedRowsOverTheCap_AreRefusedInsideTheConnector()
    {
        // This connector enforced no record cap of its own. The byte cap, the token budget and the depth
        // bound all pass comfortably here - two small activities - and only StatementImportService
        // refused the result afterwards, which left every direct ParseAsync caller with an over-limit
        // parse and allocated the whole graph before the refusal either way.
        //
        // The count that matters is TotalRetainedRows, not Records.Count: two rich activities are two
        // canonical records AND two retained activity events, so four retained rows. A cap of three
        // separates the two readings - it passes if only records are counted, and refuses if evidence is
        // counted too, which is what the service has always counted.
        var connector = new AlpacaActivityStatementConnector(
            Catalog(),
            [],
            [],
            TightLimits with { MaxDocumentBytes = 1024 * 1024, MaxRecords = 3 });

        var result = await connector.ParseAsync(
            new StatementSourceDocument("rows.json", BuildAlpacaSnapshotWithRichActivities(activityCount: 2)));

        result.HasErrors.Should().BeTrue();
        result.Issues.Should().Contain(issue => issue.Code == StatementIngressLimits.TooManyRecordsCode);
        result.Issues.Should().NotContain(issue => issue.Code == "INVALID_SNAPSHOT");
    }

    [Fact]
    public async Task Alpaca_EvidenceAloneOverTheCap_IsRefusedBeforeAnyRecordIsBuilt()
    {
        // The five evidence collections are materialized by Deserialize, so their retention is already
        // decided before the record loops run. Charging them up front means a snapshot whose evidence
        // alone breaches the cap is refused without building a single canonical record.
        var connector = new AlpacaActivityStatementConnector(
            Catalog(),
            [],
            [],
            TightLimits with { MaxDocumentBytes = 1024 * 1024, MaxRecords = 1 });

        var result = await connector.ParseAsync(
            new StatementSourceDocument("evidence.json", BuildAlpacaSnapshotWithRichActivities(activityCount: 2)));

        result.HasErrors.Should().BeTrue();
        result.Issues.Should().Contain(issue => issue.Code == StatementIngressLimits.TooManyRecordsCode);
        result.Records.Should().BeEmpty();
    }

    [Fact]
    public async Task Alpaca_ObjectsOverTheEntryBudget_AreRefusedBeforeDeserializing()
    {
        // The row cap is charged after Deserialize has already built the object graph, and the token
        // budget cannot stand in for it: an empty JSON object costs two tokens, so a hundred thousand of
        // them fit inside a 500,000-token budget and inside the byte cap while still materializing a
        // hundred thousand DTOs. One JSON object is at most one materialized object, so counting them
        // during the pre-scan bounds exactly what the deserializer will allocate.
        //
        // The record allowance is left wide open so only the entry budget can fire - proving the two are
        // separate knobs. A snapshot of two rich activities carries four objects: the root, the activity
        // wrapper, and one per activity.
        var connector = new AlpacaActivityStatementConnector(
            Catalog(),
            [],
            [],
            TightLimits with
            {
                MaxDocumentBytes = 1024 * 1024,
                MaxRecords = 1000,
                MaxDocumentEntries = 3
            });

        var result = await connector.ParseAsync(
            new StatementSourceDocument("objects.json", BuildAlpacaSnapshotWithRichActivities(activityCount: 2)));

        result.HasErrors.Should().BeTrue();
        result.Records.Should().BeEmpty("a refused document yields no partial canonical rows");
        result.Issues.Should().Contain(issue => issue.Code == StatementIngressLimits.TooManyEntriesCode);
        result.Issues.Should().NotContain(issue => issue.Code == StatementIngressLimits.TooManyRecordsCode);
    }

    [Fact]
    public async Task Alpaca_OrdinaryObjectCount_IsNotRefusedByTheEntryBudget()
    {
        // The control at the same shape: an ordinary snapshot is a handful of objects and must not be
        // mistaken for a flood, the same way ordinary metadata is not a token flood.
        var connector = new AlpacaActivityStatementConnector(
            Catalog(),
            [],
            [],
            TightLimits with
            {
                MaxDocumentBytes = 1024 * 1024,
                MaxRecords = 1000,
                MaxDocumentEntries = 10
            });

        var result = await connector.ParseAsync(
            new StatementSourceDocument("ordinary.json", BuildAlpacaSnapshotWithRichActivities(activityCount: 2)));

        result.Issues.Should().NotContain(issue => issue.Code == StatementIngressLimits.TooManyEntriesCode);
        result.Issues.Should().NotContain(issue => issue.Code == "INVALID_SNAPSHOT");
        result.Records.Should().HaveCount(2);
    }

    [Fact]
    public async Task Alpaca_ExactlyAtTheRetainedRowCap_StillParses()
    {
        // The boundary control, and the one that catches an off-by-one: a document retaining exactly the
        // permitted number of rows must import. The service refuses on TotalRetainedRows > MaxRecords, so
        // the connector has to use the same strict comparison or it refuses documents the seam accepts.
        //
        // The TotalRetainedRows assertion is the point of the test as much as the parse is: it pins the
        // connector's running count to the property the service bounds, so the two cannot drift into
        // meaning different things under the same limit.
        var connector = new AlpacaActivityStatementConnector(
            Catalog(),
            [],
            [],
            TightLimits with { MaxDocumentBytes = 1024 * 1024, MaxRecords = 4 });

        var result = await connector.ParseAsync(
            new StatementSourceDocument("exact.json", BuildAlpacaSnapshotWithRichActivities(activityCount: 2)));

        result.Issues.Should().NotContain(issue => issue.Code == StatementIngressLimits.TooManyRecordsCode);
        result.Issues.Should().NotContain(issue => issue.Code == "INVALID_SNAPSHOT");
        result.Records.Should().HaveCount(2);
        result.TotalRetainedRows.Should().Be(4);
    }

    [Fact]
    public async Task Ofx_OverTheByteCap_IsRefusedBeforeDecoding()
    {
        // camt.053, BAI2 and IB Flex all refuse an oversize document at the top of ParseAsync; OFX
        // decoded the whole payload into a UTF-16 string first. StatementImportService checks the cap,
        // so only direct ParseAsync callers were exposed - but that is public connector API.
        var connector = new OfxStatementConnector(
            Catalog(),
            TightLimits with { MaxDocumentBytes = 256, MaxRecords = 1000 });

        var result = await connector.ParseAsync(
            new StatementSourceDocument("big.ofx", BuildOfxStatement(transactionCount: 5)));

        result.HasErrors.Should().BeTrue();
        result.Issues.Should().Contain(issue => issue.Code == StatementIngressLimits.DocumentTooLargeCode);
    }

    [Fact]
    public async Task Ofx_UnderTheByteCap_StillParses()
    {
        // The control: the new pre-decode refusal must not refuse an ordinary statement.
        var connector = new OfxStatementConnector(
            Catalog(),
            TightLimits with { MaxDocumentBytes = 1024 * 1024, MaxRecords = 1000 });

        var result = await connector.ParseAsync(
            new StatementSourceDocument("ok.ofx", BuildOfxStatement(transactionCount: 3)));

        result.Issues.Should().NotContain(issue => issue.Code == StatementIngressLimits.DocumentTooLargeCode);
        result.Records.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Csv_OverTheByteCap_IsRefusedBeforeDecoding()
    {
        // The same omission as OFX, in the sibling that shares its shape.
        var connector = new CsvStatementConnector(
            Catalog(),
            TightLimits with { MaxDocumentBytes = 64, MaxRecords = 1000, MaxLineBytes = 4096 });
        var csv = "account,symbol,quantity,price,cashAmount,activityType,tradeDate\n"
            + "ACC-1,AAPL,10,100.00,-1000.00,trade,2026-05-01\n"
            + "ACC-1,AAPL,5,101.00,-505.00,trade,2026-05-02\n";

        var result = await connector.ParseAsync(
            new StatementSourceDocument("big.csv", Encoding.UTF8.GetBytes(csv)));

        result.HasErrors.Should().BeTrue();
        result.Issues.Should().Contain(issue => issue.Code == StatementIngressLimits.DocumentTooLargeCode);
    }

    [Fact]
    public async Task Csv_UnderTheByteCap_StillParses()
    {
        // The control for the CSV half.
        // The canonical CSV profile marks account, symbol, quantity, price, cashAmount, activityType and
        // tradeDate all Required, so a header of only date/amount/description maps no row at all. The
        // blank-line test upstream uses that shorter header and never notices, because it refuses during
        // line discovery before any row is mapped - so its fixture cannot be borrowed for a control that
        // has to produce records.
        var connector = new CsvStatementConnector(
            Catalog(),
            TightLimits with { MaxDocumentBytes = 1024 * 1024, MaxRecords = 1000, MaxLineBytes = 4096 });
        var csv = "account,symbol,quantity,price,cashAmount,activityType,tradeDate\n"
            + "ACC-1,AAPL,10,100.00,-1000.00,trade,2026-05-01\n"
            + "ACC-1,AAPL,5,101.00,-505.00,trade,2026-05-02\n";

        var result = await connector.ParseAsync(
            new StatementSourceDocument("ok.csv", Encoding.UTF8.GetBytes(csv)));

        result.Issues.Should().NotContain(issue => issue.Code == StatementIngressLimits.DocumentTooLargeCode);
        result.Records.Should().NotBeEmpty();
    }

    [Fact]
    public async Task IbFlex_RealReport_IsNotRefusedByTheDiagnosticBudget()
    {
        // The bound must never fire on a clean report. Same tight budget, against the golden fixture.
        var connector = new IbFlexStatementConnector(
            Catalog(),
            limits: TightLimits with
            {
                MaxDocumentBytes = 1024 * 1024,
                MaxRecords = 1000,
                MaxParseNodes = 100_000,
                MaxDiagnostics = 20
            });

        var result = await connector.ParseAsync(
            new StatementSourceDocument(
                "ib-flex-sample.xml",
                StatementConnectorTestData.ReadFixture("ib-flex-sample.xml")));

        result.Issues.Should().NotContain(issue => issue.Code == StatementIngressLimits.TooManyDiagnosticsCode);
        result.Records.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Bai2_MinimalValidFileWithTrailingNewline_IsNotRefusedByTheRawLineCap()
    {
        // The regression: the cursor walk visits one final zero-length segment when the payload ends with
        // a newline, and charging it made acceptance depend on the newline. At MaxRecords 1 the cap is 6
        // and this file's six substantive records exactly fill it, so the trailing segment pushed it to 7.
        var connector = new Bai2StatementConnector(
            TightLimits with { MaxDocumentBytes = 1024 * 1024, MaxRecords = 1, MaxLineBytes = 4096 });

        var result = await connector.ParseAsync(
            new StatementSourceDocument("minimal.bai", BuildBai2Statement(transactionCount: 0)));

        result.Issues.Should().NotContain(issue => issue.Code == StatementIngressLimits.TooManyLinesCode);
        result.Records.Should().ContainSingle().Which.Kind.Should().Be(StatementRecordKind.CashBalance);
    }

    [Fact]
    public async Task Bai2_MinimalValidFileWithoutTrailingNewline_ParsesIdentically()
    {
        // The other half of the same claim: acceptance must not depend on how the file ends.
        var connector = new Bai2StatementConnector(
            TightLimits with { MaxDocumentBytes = 1024 * 1024, MaxRecords = 1, MaxLineBytes = 4096 });
        var withNewline = BuildBai2Statement(transactionCount: 0);
        var withoutNewline = withNewline[..^1];

        var result = await connector.ParseAsync(
            new StatementSourceDocument("minimal-no-eol.bai", withoutNewline));

        result.Issues.Should().NotContain(issue => issue.Code == StatementIngressLimits.TooManyLinesCode);
        result.Records.Should().ContainSingle();
    }

    [Fact]
    public async Task IbFlex_ManyAttributesOnOneElement_AreChargedToTheNodeBudget()
    {
        // ReadAsync visits the element node, not its attributes, so counting reader nodes alone left an
        // attribute-heavy document far below the budget while the tree still allocated an XAttribute plus
        // a name and value string for each one. This document is six elements and two hundred attributes:
        // node-only counting sees single digits.
        var connector = new IbFlexStatementConnector(
            Catalog(),
            limits: TightLimits with { MaxDocumentBytes = 1024 * 1024, MaxRecords = 1000, MaxParseNodes = 30 });

        var result = await connector.ParseAsync(
            new StatementSourceDocument("ib-flex-attrs.xml", BuildIbFlexWithManyAttributes(attributeCount: 200)));

        result.HasErrors.Should().BeTrue();
        result.Issues.Should().Contain(issue => issue.Code == StatementIngressLimits.TooManyNodesCode);
    }

    [Fact]
    public async Task Bai2_MinimalTransactionOnlyStatement_IsNotRefusedByTheLineBudget()
    {
        // The round-20 regression. The raw-line cap was MaxRecords * 2 + 4, copied from CSV where it is
        // correct. A BAI2 envelope is six lines before any record exists, so at MaxRecords 1 a legal
        // 01/02/03/16/49/98/99 statement is seven lines against a cap of six - and the gap widened as
        // MaxRecords grew, because envelope lines scale with accounts and groups rather than records.
        var connector = new Bai2StatementConnector(
            TightLimits with { MaxDocumentBytes = 1024 * 1024, MaxRecords = 1, MaxLineBytes = 4096 });

        var result = await connector.ParseAsync(
            new StatementSourceDocument("minimal-txn.bai", BuildBai2TransactionOnlyStatement()));

        result.Issues.Should().NotContain(issue => issue.Code == StatementIngressLimits.TooManyLinesCode);
        result.Records.Should().ContainSingle().Which.Kind.Should().Be(StatementRecordKind.Transaction);
    }

    [Fact]
    public async Task Bai2_LineBudget_IsItsOwnLimitRatherThanARecordMultiple()
    {
        // The bound still exists and still bites: it is MaxDocumentLines, not a MaxRecords derivation.
        var connector = new Bai2StatementConnector(
            TightLimits with { MaxDocumentBytes = 1024 * 1024, MaxRecords = 1000, MaxLineBytes = 4096, MaxDocumentLines = 8 });

        var result = await connector.ParseAsync(
            new StatementSourceDocument("many-unknown.bai", BuildBai2WithUnknownRecordTypes(unknownCount: 40)));

        result.HasErrors.Should().BeTrue();
        result.Issues.Should().Contain(issue => issue.Code == StatementIngressLimits.TooManyLinesCode);
    }

    [Fact]
    public async Task IbFlex_NonRetainingCandidates_DoNotConsumeTheRetainedBudget()
    {
        // Charging OpenLot candidates rather than appends let a document of elements that build no tax lot
        // exhaust the allowance and then be refused for rows it never kept. Twenty empty OpenLots against a
        // cap of three: the trade's two rows plus the cursor fit, and the empty lots must cost nothing.
        var connector = new IbFlexStatementConnector(
            Catalog(),
            limits: TightLimits with { MaxDocumentBytes = 1024 * 1024, MaxRecords = 3 });

        var result = await connector.ParseAsync(
            new StatementSourceDocument("ib-flex-empty-lots.xml", BuildIbFlexWithEmptyOpenLots(openLotCount: 20)));

        result.Issues.Should().NotContain(issue => issue.Code == "ROW_LIMIT_EXCEEDED");
        result.Records.Should().ContainSingle();
    }

    [Fact]
    public async Task IbFlex_DeeplyNestedDocument_IsRefusedByThePreScan()
    {
        // The pre-scan counted nodes and attributes but never compared Depth with MaxNestingDepth, which
        // both camt scan loops do, so a compact but very deep document was still materialized.
        var connector = new IbFlexStatementConnector(
            Catalog(),
            limits: TightLimits with { MaxDocumentBytes = 1024 * 1024, MaxRecords = 1000, MaxNestingDepth = 8, MaxParseNodes = 500_000 });

        var result = await connector.ParseAsync(
            new StatementSourceDocument("ib-flex-deep.xml", BuildDeeplyNestedFlex(depth: 40)));

        result.HasErrors.Should().BeTrue();
        result.Issues.Should().Contain(issue => issue.Code == StatementIngressLimits.NestingTooDeepCode);
    }

    [Fact]
    public async Task Camt_ManySubtreesEachInsideTheSubtreeBound_BreachTheDocumentBudget()
    {
        // TryReadBoundedSubtree consumes a subtree while the outer reader advances to its end element, so
        // the document counter charged one node per subtree. N entries each comfortably inside
        // MaxSubtreeNodes walked far more than MaxParseNodes while every per-subtree check passed.
        var connector = new Camt053StatementConnector(
            TightLimits with
            {
                MaxDocumentBytes = 4 * 1024 * 1024,
                MaxRecords = 10_000,
                MaxNestingDepth = 64,
                MaxSubtreeNodes = 50_000,
                MaxParseNodes = 200
            });

        var result = await connector.ParseAsync(
            new StatementSourceDocument("camt-many-subtrees.xml", BuildCamtStatement(entryCount: 200)));

        result.HasErrors.Should().BeTrue();
        result.Issues.Should().Contain(issue => issue.Code == StatementIngressLimits.TooManyNodesCode);
    }

    [Fact]
    public async Task Preview_OverTheRecordCap_ReturnsWithoutProjectingTheParse()
    {
        // Preview added the cap issue and then still grouped every record and projected every snapshot.
        // It now returns immediately, like commit and validate.
        var service = BuildServiceWith(
            TightLimits with { MaxDocumentBytes = 1024 * 1024, MaxRecords = 2 },
            new EvidenceHeavyConnector(recordCount: 1, taxLotCount: 5));
        var document = new StatementSourceDocument("evidence.heavy", "irrelevant"u8.ToArray());

        var preview = await service.PreviewAsync(document, connectorId: null);

        preview.Issues.Should().Contain(issue => issue.Code == StatementIngressLimits.TooManyRecordsCode);
        preview.RecordCount.Should().Be(0, "the refused parse is not projected");
        preview.KindSummaries.Should().BeEmpty();
    }

    // ---------------------------------------------------------------------------------------------
    // Harness
    // ---------------------------------------------------------------------------------------------

    // Builds a service around one explicit connector rather than the built-in three, so a test can
    // hand the service a parse result of a shape no real file format produces on demand.
    private StatementImportService BuildServiceWith(
        StatementIngressLimits limits,
        params IStatementConnector[] connectors)
    {
        var catalog = new StatementMappingProfileCatalog(new FileStatementMappingProfileStore(_root));
        var registry = new StatementConnectorRegistry(connectors);
        var statementStore = new JsonCanonicalStatementStore(_root);
        var workflow = StatementRunWorkflowService.CreateEphemeralForTesting(
            statementStore,
            new JsonReconciliationCaseStore(_root),
            new JsonReconciliationBreakStore(_root),
            new CsvBrokerStatementService(statementStore),
            new StatementReconciliationContextAdapter(new StatementReconciliationService()));

        return new StatementImportService(registry, catalog, workflow, _root, limits);
    }

    private static StatementParseResult EvidenceHeavyParse(int recordCount, int taxLotCount, int snapshotCount)
        => new(
            ConnectorId: EvidenceHeavyConnector.Id,
            ProfileId: null,
            DetectedColumns: [],
            ColumnMappings: [],
            Records: Enumerable.Range(0, recordCount)
                .Select(index => new StatementCanonicalRecord(
                    StatementRecordKind.Transaction,
                    Account: "FUND-A",
                    Symbol: "AAPL",
                    Quantity: 1m,
                    Price: 100m,
                    CashAmount: -100m,
                    ActivityType: "BUY",
                    TradeDate: new DateOnly(2026, 5, 10),
                    Currency: "USD",
                    ExternalTransactionId: $"TXN-{index:D4}"))
                .ToArray(),
            Issues: [],
            Fingerprint: new StatementFormatFingerprint(Sha256: new string('0', 64), NormalizedColumns: [], Delimiter: ","),
            AccountSnapshots: snapshotCount == 0
                ? null
                : Enumerable.Range(0, snapshotCount)
                    .Select(index => new BrokerageAccountSnapshotDto(
                        ProviderId: EvidenceHeavyConnector.Id,
                        AccountId: $"ACCT-{index:D4}",
                        AsOf: new DateTimeOffset(2026, 5, 31, 0, 0, 0, TimeSpan.Zero),
                        Currency: "USD",
                        Status: "active",
                        MarginRegime: BrokerageMarginRegime.Cash,
                        Cash: 0m,
                        Equity: 0m,
                        BuyingPower: 0m))
                    .ToArray(),
            TaxLots: taxLotCount == 0
                ? null
                : Enumerable.Range(0, taxLotCount)
                    .Select(index => new BrokerageTaxLotSnapshotDto(
                        LotId: $"LOT-{index:D4}",
                        Symbol: "AAPL",
                        AcquiredDate: new DateOnly(2026, 5, 1),
                        Quantity: 1m,
                        CostBasis: 100m,
                        Currency: "USD"))
                    .ToArray());

    // A Flex report whose bulk is AccountInformation anchors rather than trades: one canonical record
    // and accountCount retained snapshots, which is the shape the record cap used to miss entirely.
    // A camt document whose single valid statement is followed by many uniquely named shallow elements.
    // Unique names matter: the reader's name table retains each distinct string.
    private static byte[] BuildCamtWithTrailingNoise(int elementCount)
    {
        var builder = new StringBuilder()
            .Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>")
            .Append("<Document xmlns=\"urn:iso:std:iso:20022:tech:xsd:camt.053.001.02\">")
            .Append("<BkToCstmrStmt>")
            .Append("<GrpHdr><MsgId>MERIDIAN-CAMT-1</MsgId><CreDtTm>2026-05-31T23:59:00</CreDtTm></GrpHdr>")
            .Append(StatementXml("DE89370400440532013000", entryCount: 1));

        for (var index = 0; index < elementCount; index++)
        {
            builder.Append($"<Noise{index:D6}>x</Noise{index:D6}>");
        }

        return Encoding.UTF8.GetBytes(builder.Append("</BkToCstmrStmt></Document>").ToString());
    }

    private static byte[] BuildCamtWithAttributeHeavyNoise(int attributeCount)
    {
        // The attribute-bearing element sits after Stmt closes, so only the outer walk ever reads it. A
        // direct child of Stmt would be handed to TryReadBoundedSubtree, whose own attribute charge would
        // mask whether the outer loop counts them at all.
        var builder = new StringBuilder()
            .Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>")
            .Append("<Document xmlns=\"urn:iso:std:iso:20022:tech:xsd:camt.053.001.02\">")
            .Append("<BkToCstmrStmt>")
            .Append("<GrpHdr><MsgId>MERIDIAN-CAMT-1</MsgId><CreDtTm>2026-05-31T23:59:00</CreDtTm></GrpHdr>")
            .Append(StatementXml("DE89370400440532013000", entryCount: 1))
            .Append("<Noise000000");

        for (var index = 0; index < attributeCount; index++)
        {
            builder.Append($" pad{index:D6}=\"x\"");
        }

        return Encoding.UTF8.GetBytes(
            builder.Append(" />").Append("</BkToCstmrStmt></Document>").ToString());
    }

    // Valid BAI2 envelope carrying record types the switch does not recognize, which is what makes them
    // interesting: they never charge a balance or detail candidate.
    private static byte[] BuildAlpacaSnapshot(int cashTransactionCount, int metadataProperties = 0)
    {
        // AlpacaStatementSnapshotJsonContext uses JsonSerializerDefaults.Web, so the property names are
        // camelCase. Every transaction type is distinct, so ResolveActivity's per-code dedupe cannot
        // collapse the warnings into one.
        var builder = new StringBuilder()
            .Append("{\"providerId\":\"alpaca\",\"accountId\":\"ACC-1\",")
            .Append("\"retrievedAt\":\"2026-06-30T00:00:00+00:00\",")
            .Append("\"activity\":{\"providerId\":\"alpaca\",\"accountId\":\"ACC-1\",")
            .Append("\"retrievedAt\":\"2026-06-30T00:00:00+00:00\",")
            .Append("\"orders\":[],\"fills\":[],\"cashTransactions\":[");

        for (var index = 0; index < cashTransactionCount; index++)
        {
            if (index > 0)
            {
                builder.Append(',');
            }

            builder
                .Append($"{{\"transactionId\":\"T{index:D6}\",\"transactionType\":\"UNMAPPED-{index:D6}\",")
                .Append("\"amount\":10.00,\"currency\":\"USD\",")
                .Append("\"postedAt\":\"2026-06-01T00:00:00+00:00\"}");
        }

        builder.Append(']');

        if (metadataProperties > 0)
        {
            // One activity event - a single retained row as far as MaxRecords is concerned - carrying an
            // open-ended Metadata dictionary. This is the shape the byte cap cannot see: the members are
            // compact, so hundreds of thousands of them fit well inside it.
            builder
                .Append(",\"activities\":[{\"eventId\":\"E1\",\"providerCode\":\"CSD\",")
                .Append("\"category\":\"Cash\",\"subtype\":\"CashDeposit\",")
                .Append("\"effectiveAt\":\"2026-06-01T00:00:00+00:00\",")
                .Append("\"currency\":\"USD\",\"netAmount\":10.00,\"metadata\":{");

            for (var index = 0; index < metadataProperties; index++)
            {
                if (index > 0)
                {
                    builder.Append(',');
                }

                builder.Append($"\"k{index:D6}\":\"v\"");
            }

            builder.Append("}}]");
        }

        return Encoding.UTF8.GetBytes(builder.Append("},\"portfolio\":null}").ToString());
    }

    private static byte[] BuildAlpacaSnapshotWithRichActivities(int activityCount)
    {
        // Rich activities are retained twice over: ParseSnapshotAsync appends one canonical record per
        // activity, and the same list is returned as StatementParseResult.ActivityEvents. N activities
        // are therefore 2N retained rows - the output multiplicity a pre-scan element count cannot know,
        // which is why the cap is charged on the append rather than predicted from the payload.
        var builder = new StringBuilder()
            .Append("{\"providerId\":\"alpaca\",\"accountId\":\"ACC-1\",")
            .Append("\"retrievedAt\":\"2026-06-30T00:00:00+00:00\",")
            .Append("\"activity\":{\"providerId\":\"alpaca\",\"accountId\":\"ACC-1\",")
            .Append("\"retrievedAt\":\"2026-06-30T00:00:00+00:00\",")
            .Append("\"orders\":[],\"fills\":[],\"cashTransactions\":[],\"activities\":[");

        for (var index = 0; index < activityCount; index++)
        {
            if (index > 0)
            {
                builder.Append(',');
            }

            builder
                .Append($"{{\"eventId\":\"E{index:D6}\",\"providerCode\":\"CSD\",")
                .Append("\"category\":\"Cash\",\"subtype\":\"CashDeposit\",")
                .Append("\"effectiveAt\":\"2026-06-01T00:00:00+00:00\",")
                .Append("\"currency\":\"USD\",\"netAmount\":10.00}");
        }

        return Encoding.UTF8.GetBytes(builder.Append("]},\"portfolio\":null}").ToString());
    }

    private static byte[] BuildBai2WithMalformedTransactions(int count)
    {
        // Each 16 detail carries an unparseable amount, so it takes the BAI2_BAD_AMOUNT warning branch:
        // one retained diagnostic and no canonical record. The 03 balance is valid, so the file still
        // produces exactly one record however many bad details follow it.
        var builder = new StringBuilder()
            .Append("01,CITIBANK,MERIDIAN,260531,0800,1,,,2/\n")
            .Append("02,MERIDIAN,CITIBANK,1,260531,,USD,2/\n")
            .Append("03,0975312468,USD,015,1234567,,/\n");

        for (var index = 0; index < count; index++)
        {
            builder.Append("16,409,not-an-amount,,,/\n");
        }

        builder.Append("49,1234567,3/\n98,1234567,1,3/\n99,1234567,1,5/\n");
        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    private static byte[] BuildBai2WithUnknownRecordTypes(int unknownCount)
    {
        var builder = new StringBuilder()
            .Append("01,CITIBANK,MERIDIAN,260531,0800,1,,,2/\n")
            .Append("02,MERIDIAN,CITIBANK,1,260531,,USD,2/\n")
            .Append("03,0975312468,USD,015,1234567,,/\n");

        for (var index = 0; index < unknownCount; index++)
        {
            builder.Append("88,continuation/\n");
        }

        builder.Append("49,1234567,3/\n98,1234567,1,3/\n99,1234567,1,5/\n");
        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    // A Flex report of nothing but trades, so the retained count is exactly two per trade plus the cursor.
    // One Trade element carrying attributeCount extra attributes. Few elements, many attributes - the
    // shape that a node-only budget cannot see.
    // 01/02/03/16/49/98/99 — a legal statement whose only record is a transaction, with no closing
    // balance. Seven lines: the shape the MaxRecords-derived line cap refused.
    private static byte[] BuildBai2TransactionOnlyStatement()
        => Encoding.UTF8.GetBytes(
            "01,CITIBANK,MERIDIAN,260531,0800,1,,,2/\n"
            + "02,MERIDIAN,CITIBANK,1,260531,,USD,2/\n"
            + "03,0975312468,USD,,,,/\n"
            + "16,115,250000,,BANKREF0001,CUSTREF0001,Incoming wire/\n"
            + "49,250000,2/\n98,250000,1,3/\n99,250000,1,5/\n");

    // A Flex report whose OpenLot elements carry no attributes, so BuildTaxLot returns null for each and
    // nothing is retained by them.
    private static byte[] BuildIbFlexWithEmptyOpenLots(int openLotCount)
    {
        var builder = new StringBuilder()
            .Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>")
            .Append("<FlexQueryResponse queryName=\"Meridian Daily Statement\" type=\"AF\"><FlexStatements count=\"1\">")
            .Append("<FlexStatement accountId=\"U1234567\" fromDate=\"2026-06-01\" toDate=\"2026-06-30\"><Trades>")
            .Append("<Trade accountId=\"U1234567\" symbol=\"AAPL\" quantity=\"100\" tradePrice=\"187.25\" ")
            .Append("netCash=\"-18725\" tradeDate=\"20260602\" tradeID=\"7001001\" currency=\"USD\" buySell=\"BUY\" />")
            .Append("</Trades><OpenPositions />");

        for (var index = 0; index < openLotCount; index++)
        {
            builder.Append("<OpenLot />");
        }

        return Encoding.UTF8.GetBytes(
            builder.Append("</FlexStatement></FlexStatements></FlexQueryResponse>").ToString());
    }

    // Well-formed Flex XML nested far past any sane statement, but compact enough to stay inside the byte
    // and node budgets, so only the depth check can refuse it.
    private static byte[] BuildDeeplyNestedFlex(int depth)
    {
        var builder = new StringBuilder()
            .Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>")
            .Append("<FlexQueryResponse queryName=\"Meridian Daily Statement\" type=\"AF\"><FlexStatements count=\"1\">")
            .Append("<FlexStatement accountId=\"U1234567\" fromDate=\"2026-06-01\" toDate=\"2026-06-30\">");

        for (var level = 0; level < depth; level++)
        {
            builder.Append("<Wrap>");
        }

        builder.Append("<Leaf />");

        for (var level = 0; level < depth; level++)
        {
            builder.Append("</Wrap>");
        }

        return Encoding.UTF8.GetBytes(
            builder.Append("</FlexStatement></FlexStatements></FlexQueryResponse>").ToString());
    }

    private static byte[] BuildIbFlexWithManyAttributes(int attributeCount)
    {
        var builder = new StringBuilder()
            .Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>")
            .Append("<FlexQueryResponse queryName=\"Meridian Daily Statement\" type=\"AF\"><FlexStatements count=\"1\">")
            .Append("<FlexStatement accountId=\"U1234567\" fromDate=\"2026-06-01\" toDate=\"2026-06-30\"><Trades>")
            .Append("<Trade accountId=\"U1234567\" symbol=\"AAPL\" quantity=\"100\" tradePrice=\"187.25\" ")
            .Append("netCash=\"-18725\" tradeDate=\"20260602\" tradeID=\"7001001\" currency=\"USD\" buySell=\"BUY\"");

        for (var index = 0; index < attributeCount; index++)
        {
            builder.Append($" pad{index:D6}=\"x\"");
        }

        return Encoding.UTF8.GetBytes(
            builder.Append(" /></Trades></FlexStatement></FlexStatements></FlexQueryResponse>").ToString());
    }

    private static byte[] BuildIbFlexCashTransactions(int count, bool unmappable)
    {
        // When unmappable, every row is rejected for its date AND carries an activity code no profile
        // maps, and those codes are distinct so reportedUnknownActivityCodes cannot collapse them into
        // one warning: two retained diagnostics per row and no canonical record for any of them. When
        // not, the rows share a single code, so the dedupe leaves at most one warning for the file.
        var builder = new StringBuilder()
            .Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>")
            .Append("<FlexQueryResponse queryName=\"Meridian Daily Statement\" type=\"AF\"><FlexStatements count=\"1\">")
            .Append("<FlexStatement accountId=\"U1234567\" fromDate=\"2026-06-01\" toDate=\"2026-06-30\"><CashTransactions>");

        for (var index = 0; index < count; index++)
        {
            var type = unmappable ? $"UNMAPPED-{index:D6}" : "DEPOSIT";
            var when = unmappable ? "not-a-date" : "20260602";
            builder.Append(
                $"<CashTransaction accountId=\"U1234567\" type=\"{type}\" amount=\"10.00\" " +
                $"dateTime=\"{when}\" currency=\"USD\" transactionID=\"C{index:D6}\" />");
        }

        return Encoding.UTF8.GetBytes(
            builder.Append("</CashTransactions></FlexStatement></FlexStatements></FlexQueryResponse>").ToString());
    }

    private static byte[] BuildIbFlexWithTrades(int tradeCount)
    {
        var builder = new StringBuilder()
            .Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>")
            .Append("<FlexQueryResponse queryName=\"Meridian Daily Statement\" type=\"AF\"><FlexStatements count=\"1\">")
            .Append("<FlexStatement accountId=\"U1234567\" fromDate=\"2026-06-01\" toDate=\"2026-06-30\"><Trades>");

        for (var index = 0; index < tradeCount; index++)
        {
            builder.Append(
                $"<Trade accountId=\"U1234567\" symbol=\"AAPL\" quantity=\"100\" tradePrice=\"187.25\" " +
                $"netCash=\"-18725\" tradeDate=\"20260602\" settleDateTarget=\"20260604\" ibCommission=\"-1.05\" " +
                $"tradeID=\"700100{index}\" currency=\"USD\" buySell=\"BUY\" />");
        }

        return Encoding.UTF8.GetBytes(
            builder.Append("</Trades></FlexStatement></FlexStatements></FlexQueryResponse>").ToString());
    }

    private static byte[] BuildIbFlexWithAccountInformation(int accountCount)
    {
        var builder = new StringBuilder()
            .Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>")
            .Append("<FlexQueryResponse queryName=\"Meridian Daily Statement\" type=\"AF\"><FlexStatements count=\"1\">")
            .Append("<FlexStatement accountId=\"U1234567\" fromDate=\"2026-06-01\" toDate=\"2026-06-30\">")
            .Append("<Trades><Trade accountId=\"U1234567\" symbol=\"AAPL\" quantity=\"100\" tradePrice=\"187.25\" ")
            .Append("netCash=\"-18725\" tradeDate=\"20260602\" settleDateTarget=\"20260604\" ibCommission=\"-1.05\" ")
            .Append("tradeID=\"7001001\" currency=\"USD\" buySell=\"BUY\" /></Trades>");

        for (var index = 0; index < accountCount; index++)
        {
            builder.Append(
                $"<AccountInformation accountId=\"U123456{index}\" currency=\"USD\" accountType=\"Cash\" />");
        }

        return Encoding.UTF8.GetBytes(
            builder.Append("</FlexStatement></FlexStatements></FlexQueryResponse>").ToString());
    }

    // Returns a parse result directly so the service-level cap can be exercised on a shape that has
    // more evidence rows than canonical records - no real fixture is needed to state that bound.
    private sealed class EvidenceHeavyConnector(int recordCount, int taxLotCount) : IStatementConnector
    {
        internal const string Id = "evidence-heavy";

        public StatementConnectorDescriptor Descriptor { get; } = new(
            ConnectorId: Id,
            DisplayName: "Evidence-heavy test connector",
            FileExtensions: [".heavy"],
            SupportsFileImport: true,
            SupportsRemoteFetch: false,
            RequiresMappingProfile: false,
            DefaultProfileId: null);

        public bool CanHandle(StatementSourceDocument document)
            => document.FileName.EndsWith(".heavy", StringComparison.OrdinalIgnoreCase);

        public Task<StatementParseResult> ParseAsync(StatementSourceDocument document, CancellationToken ct = default)
            => Task.FromResult(EvidenceHeavyParse(recordCount, taxLotCount, snapshotCount: 0));
    }

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
    public async Task Ofx_RecordsOverCap_AreRefusedOnTheAppend()
    {
        // The service-level record check ran only after the node tree and the flattened entry
        // dictionaries both existed, and an entry-heavy OFX file sits well inside the 20 MiB document
        // cap - so the connector needs its own bound. It is charged where a record is appended; the
        // aggregates themselves are bounded separately by MaxDocumentEntries.
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
    public async Task Ofx_NestedExactlyAtTheDepthLimit_IsAccepted()
    {
        // The guard compared stack.Count directly, but the stack carries the synthetic OFX-ROOT pushed
        // before the walk, so its Count is one more than the depth the document declares. That refused a
        // document nested at exactly MaxNestingDepth - one level earlier than the camt and Flex guards,
        // which accept reader.Depth == MaxNestingDepth. This test fails before that fix.
        var connector = new OfxStatementConnector(
            Catalog(),
            TightLimits with { MaxDocumentBytes = 1024 * 1024, MaxRecords = 1000, MaxNestingDepth = 8 });

        var result = await connector.ParseAsync(
            new StatementSourceDocument("exact-depth.ofx", BuildDeeplyNestedOfx(depth: 8)));

        result.Issues.Should().NotContain(issue => issue.Code == StatementIngressLimits.NestingTooDeepCode);
    }

    [Fact]
    public async Task Ofx_NestedOneLevelPastTheDepthLimit_IsRefused()
    {
        // The other half of the boundary: correcting the off-by-one must not stop the bound biting.
        var connector = new OfxStatementConnector(
            Catalog(),
            TightLimits with { MaxDocumentBytes = 1024 * 1024, MaxRecords = 1000, MaxNestingDepth = 8 });

        var result = await connector.ParseAsync(
            new StatementSourceDocument("past-depth.ofx", BuildDeeplyNestedOfx(depth: 9)));

        result.HasErrors.Should().BeTrue();
        result.Issues.Should().Contain(issue => issue.Code == StatementIngressLimits.NestingTooDeepCode);
    }

    [Fact]
    public async Task Alpaca_NestingOverTheConfiguredDepth_ReportsNestingRatherThanInvalidSnapshot()
    {
        // The scan reader was built with System.Text.Json's default 64-level ceiling rather than the
        // configured one, so its own CurrentDepth check could never report the named diagnostic: the
        // reader threw first, the catch deferred to Deserialize, and the operator saw INVALID_SNAPSHOT -
        // which says nothing about a depth bound they can configure. Both the reader and the deserializer
        // are now built from MaxNestingDepth.
        var connector = new AlpacaActivityStatementConnector(
            Catalog(),
            [],
            [],
            TightLimits with { MaxDocumentBytes = 1024 * 1024, MaxRecords = 1000, MaxNestingDepth = 3 });

        var result = await connector.ParseAsync(
            new StatementSourceDocument("deep.json", BuildAlpacaSnapshot(cashTransactionCount: 1)));

        result.HasErrors.Should().BeTrue();
        result.Issues.Should().Contain(issue => issue.Code == StatementIngressLimits.NestingTooDeepCode);
        result.Issues.Should().NotContain(issue => issue.Code == "INVALID_SNAPSHOT");
    }

    [Fact]
    public async Task Alpaca_OrdinaryNesting_IsNotRefusedByTheDepthLimit()
    {
        // The control: the same snapshot under the suite's ordinary depth allowance still parses.
        var connector = new AlpacaActivityStatementConnector(
            Catalog(),
            [],
            [],
            TightLimits with { MaxDocumentBytes = 1024 * 1024, MaxRecords = 1000, MaxNestingDepth = 8 });

        var result = await connector.ParseAsync(
            new StatementSourceDocument("ordinary-depth.json", BuildAlpacaSnapshot(cashTransactionCount: 1)));

        result.Issues.Should().NotContain(issue => issue.Code == StatementIngressLimits.NestingTooDeepCode);
        result.Issues.Should().NotContain(issue => issue.Code == "INVALID_SNAPSHOT");
        result.Records.Should().NotBeEmpty();
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
        // Every malformed 16 record takes a warning branch and appends nothing, so a file of them retained
        // one issue object per line without bound. The bound is MaxDiagnostics, not MaxRecords: these rows
        // produce diagnostics, so the diagnostic ceiling is the one that owns them, and it reports a code
        // that describes the file truthfully. Charging them to the record cap refused documents whose
        // canonical rows sat well inside it, which is the defect this suite found ten times over.
        var connector = new Bai2StatementConnector(
            TightLimits with
            {
                MaxDocumentBytes = 1024 * 1024,
                MaxRecords = 5,
                MaxLineBytes = 4096,
                MaxDiagnostics = 5
            });

        var result = await connector.ParseAsync(
            new StatementSourceDocument("malformed.bai", BuildBai2WithUnparseableAmounts(detailCount: 40)));

        result.HasErrors.Should().BeTrue("an over-cap file is refused, not half-imported");
        result.Records.Should().BeEmpty();
        result.Issues.Should().Contain(issue => issue.Code == StatementIngressLimits.TooManyDiagnosticsCode);
        result.Issues.Should().NotContain(
            issue => issue.Code == StatementIngressLimits.TooManyRecordsCode,
            "a file that retained no record did not overflow the record cap");
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
        // A CLBD balance with no parseable date is skipped with a warning and appends nothing, so a
        // document of them accumulates issue objects unbounded. MaxDiagnostics is the ceiling that owns
        // them; the record cap is charged where a record is appended and never sees these rows at all.
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
            TightLimits with
            {
                MaxDocumentBytes = 1024 * 1024,
                MaxRecords = 5,
                MaxNestingDepth = 64,
                MaxDiagnostics = 5
            });

        var result = await connector.ParseAsync(
            new StatementSourceDocument("undated.xml", BuildCamtDocument(undated.Append("</Stmt>").ToString())));

        result.HasErrors.Should().BeTrue();
        result.Records.Should().BeEmpty();
        result.Issues.Should().Contain(issue => issue.Code == StatementIngressLimits.TooManyDiagnosticsCode);
        result.Issues.Should().NotContain(
            issue => issue.Code == StatementIngressLimits.TooManyRecordsCode,
            "a document that retained no record did not overflow the record cap");
        result.Issues.Count.Should().BeLessThan(40, "diagnostics are bounded, not one per skipped balance");
    }

    [Fact]
    public async Task Camt_SkippedEntries_AreNotChargedToTheRecordBudget()
    {
        // The control for the correction above, and the false refusal it fixes. One valid closing balance
        // and three pending entries retain exactly one canonical record, so a cap of one must import it.
        // Charging the entries as candidates made this document refuse as STATEMENT_TOO_MANY_RECORDS -
        // a bound reporting an overflow of rows the parse never produced.
        var skipped = new StringBuilder()
            .Append("<Stmt><Id>STMT-1</Id>")
            .Append("<Acct><Id><IBAN>DE89370400440532013000</IBAN></Id><Ccy>EUR</Ccy></Acct>")
            .Append("<Bal><Tp><CdOrPrtry><Cd>CLBD</Cd></CdOrPrtry></Tp>")
            .Append("<Amt Ccy=\"EUR\">12345.67</Amt><CdtDbtInd>CRDT</CdtDbtInd>")
            .Append("<Dt><Dt>2026-05-31</Dt></Dt></Bal>");

        for (var index = 0; index < 3; index++)
        {
            skipped
                .Append("<Ntry><Amt Ccy=\"EUR\">10.00</Amt><CdtDbtInd>CRDT</CdtDbtInd>")
                .Append("<Sts>PDNG</Sts><BookgDt><Dt>2026-05-10</Dt></BookgDt>")
                .Append($"<AcctSvcrRef>PENDING-{index:D4}</AcctSvcrRef></Ntry>");
        }

        var connector = new Camt053StatementConnector(
            TightLimits with { MaxDocumentBytes = 1024 * 1024, MaxRecords = 1, MaxNestingDepth = 64 });

        var result = await connector.ParseAsync(
            new StatementSourceDocument("skipped.xml", BuildCamtDocument(skipped.Append("</Stmt>").ToString())));

        result.Issues.Should().NotContain(issue => issue.Code == StatementIngressLimits.TooManyRecordsCode);
        result.Records.Should().ContainSingle().Which.Kind.Should().Be(StatementRecordKind.CashBalance);
        result.Issues.Should().Contain(
            issue => issue.Code == "CAMT_ENTRY_NOT_BOOKED",
            "the pending entries are still reported, they are just not charged as records");
    }

    [Fact]
    public async Task Ofx_AggregatesOverTheEntryBudget_ReportEntryOverflowNotRecordOverflow()
    {
        // The parser flattens one dictionary per aggregate before any of them is mapped, so that
        // allocation genuinely needs a ceiling ahead of the record cap - but it is a different count, and
        // reporting it as record overflow told the operator something untrue about their file. It is now
        // MaxDocumentEntries with its own code, the same separation MaxDocumentLines and MaxDiagnostics
        // already have. The record allowance is left wide open here so only the entry budget can fire.
        var connector = new OfxStatementConnector(
            Catalog(),
            TightLimits with
            {
                MaxDocumentBytes = 1024 * 1024,
                MaxRecords = 1000,
                MaxDocumentEntries = 3
            });

        var result = await connector.ParseAsync(
            new StatementSourceDocument("aggregates.ofx", BuildOfxStatement(transactionCount: 25)));

        result.HasErrors.Should().BeTrue();
        result.Records.Should().BeEmpty("a refused document yields no partial canonical rows");
        result.Issues.Should().Contain(issue => issue.Code == StatementIngressLimits.TooManyEntriesCode);
        result.Issues.Should().NotContain(issue => issue.Code == StatementIngressLimits.TooManyRecordsCode);
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
        var connector = new CsvStatementConnector(
            Catalog(),
            TightLimits with { MaxRecords = 2, MaxDocumentLines = 8 });
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
        // MaxDocumentBytes is raised because the subject here is the LINE bound: TightLimits caps the
        // document at 512 bytes and this row alone is ~780, so the byte cap the CSV connector now
        // enforces before decoding would refuse the file first and the test would pass without ever
        // reaching the bound it exists to prove.
        var connector = new CsvStatementConnector(
            Catalog(),
            TightLimits with { MaxDocumentBytes = 1024 * 1024, MaxRecords = 100, MaxLineBytes = 600 });
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
            TightLimits with
            {
                MaxDocumentBytes = 1024 * 1024,
                MaxRecords = 5,
                MaxNestingDepth = 64,
                MaxDiagnostics = 5
            });

        var result = await connector.ParseAsync(
            new StatementSourceDocument("pending.xml", BuildCamtDocument(pending.Append("</Stmt>").ToString())));

        result.HasErrors.Should().BeTrue("an over-cap file is refused, not committed with the balance alone");
        result.Records.Should().BeEmpty();
        result.Issues.Should().Contain(issue => issue.Code == StatementIngressLimits.TooManyDiagnosticsCode);
        result.Issues.Should().NotContain(
            issue => issue.Code == StatementIngressLimits.TooManyRecordsCode,
            "a document that retained no record did not overflow the record cap");
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
