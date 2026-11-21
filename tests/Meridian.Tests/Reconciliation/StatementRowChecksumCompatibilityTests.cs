using Meridian.DataIntegration.Reconciliation;
using Meridian.Domain.Reconciliation;
using Meridian.Storage.Archival;
using Xunit;

namespace Meridian.Tests.Reconciliation;

public sealed class StatementRowChecksumCompatibilityTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ImportedRowsKeepRetainedUppercaseChecksumsAndDuplicateKeys(bool flex)
    {
        const string csvRow = "A1,\"MÜN,ABC\",1,12.50,-12.50,\"OTHER \"\"Case\"\"\",2026-01-02,,USD,,\"TX,é\"";
        const string xmlRow = """<Trade accountId="A1" symbol="MÜN" tradeDate="20260102" quantity="1" tradePrice="12.50" netCash="-12.50" currency="USD" description="Retain  Case é" />""";
        var content = flex
            ? """<FlexQueryResponse><FlexStatements><FlexStatement accountId="A1" fromDate="20260101" toDate="20260131"><Trades>"""
                + xmlRow + "</Trades></FlexStatement></FlexStatements></FlexQueryResponse>"
            : "account,symbol,quantity,price,cashAmount,activityType,tradeDate,settlementDate,currency,feesCommission,externalTransactionId\n"
                + csvRow + "\n";
        var expectedRowChecksum = flex
            ? "5C5D17C7A12E66DED27B21A598787FDFDD2131BA375B062CC4F19A6FF30F5A30"
            : "AEF97C4390FF6A1B8B461305594669E6B654BDA0C55B6A18479ECA865B644715";
        var expectedSourceChecksum = flex
            ? "41f890e4dfbe3267b06a0f963c4623b557aadb3bb9e02233818d6151b064f242"
            : "f788ed9ca158effa69656ecacc9ad3849be8632aa79ddade2d8fc6c724f8b15c";
        var expectedDuplicateKey = flex
            ? "C159EC9DF12A765661E92852F4E369C8A999E080748B55E9FC54CC87EDC112A0"
            : "2553F7C06FA55EB6714FB434A550FB7ED137A5E7C6670B241CB1B48E9B93A666";
        var root = Path.Combine(Path.GetTempPath(), $"meridian-checksum-compatibility-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, flex ? "statement.xml" : "statement.csv");
            await File.WriteAllTextAsync(path, content);
            var store = new JsonCanonicalStatementStore(root, new AtomicFileWriterAdapter());
            IBrokerStatementService service = flex ? new IbFlexBrokerStatementService(store) : new CsvBrokerStatementService(store);
            var request = new BrokerStatementImportRequest(flex ? "ib-flex" : "samplebroker", path, new DateOnly(2026, 1, 31))
            {
                FundAccountId = "FUND-1",
                ExternalAccountId = "A1",
                StatementPeriodStart = new DateOnly(2026, 1, 1),
                StatementPeriodEnd = new DateOnly(2026, 1, 31)
            };

            var imported = await service.ImportAsync(request);
            var retained = await store.GetImportAsync(expectedDuplicateKey);

            Assert.Equal(expectedRowChecksum, Assert.Single(imported.Rows).RawChecksum);
            Assert.Equal(expectedSourceChecksum, imported.Import.SourceFileHash);
            Assert.Equal(expectedDuplicateKey, imported.Import.DuplicateKey);
            Assert.Equal(expectedDuplicateKey, imported.Import.ImportId);
            Assert.NotNull(retained);
            Assert.Equal(expectedRowChecksum, Assert.Single(retained.Rows).RawChecksum);
            await Assert.ThrowsAsync<StatementAlreadyImportedException>(() => service.ImportAsync(request));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
