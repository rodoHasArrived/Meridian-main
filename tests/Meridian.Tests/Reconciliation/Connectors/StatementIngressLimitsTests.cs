using System.Text;
using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.Reconciliation;
using Meridian.FinancialOperations.Reconciliation.Connectors;
using Meridian.FinancialOperations.Reconciliation.Connectors.Bai2;
using Meridian.FinancialOperations.Reconciliation.Connectors.Camt;
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

    private StatementImportService BuildService(StatementIngressLimits limits)
    {
        var catalog = new StatementMappingProfileCatalog(new FileStatementMappingProfileStore(_root));
        var registry = new StatementConnectorRegistry(
        [
            new Camt053StatementConnector(limits),
            new Bai2StatementConnector(limits),
            new CsvStatementConnector(catalog)
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

    private static StatementImportCommitRequest CommitRequest(StatementSourceDocument document)
        => new(
            document,
            ConnectorId: null,
            SourceKind: "bank",
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
}
