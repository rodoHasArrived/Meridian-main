using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.CashFlows;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;

namespace Meridian.Tests.SecurityMaster;

[Trait("Category", "Integration")]
public sealed class ContractualCashFlowCompanionPostgresTests(SecurityMasterDatabaseFixture fixture)
    : IClassFixture<SecurityMasterDatabaseFixture>
{
    [SecurityMasterDatabaseFact]
    public async Task Readable_companion_survives_jsonb_storage_and_fresh_reader_without_changing_write_authority()
    {
        var issue = new DateOnly(2026, 1, 1);
        var contract = new ContractualCashFlowContract(issue, issue.AddYears(1), "USD",
            [new("main", 1000, 3, DayCountConvention.Actual365, .05m)]);
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        foreach (var (assetClass, version) in new[] { ("Bond", 4), ("CertificateOfDeposit", 4), ("StructuredCredit", 5), ("Swap", 3), ("DirectLoan", 2) })
        {
            var id = Guid.NewGuid();
            var at = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var payload = JsonSerializer.SerializeToElement(new
            {
                schemaVersion = 1,
                assetClassTermsVersion = version,
                maturity = "2027-01-01",
                cashFlowContract = contract
            }, json);
            var code = "CF" + id.ToString("N");
            var record = new SecurityProjectionRecord(id, assetClass, SecurityStatusDto.Active, "Contract companion", "USD", "InternalCode", code,
                JsonSerializer.SerializeToElement(new { displayName = "Contract companion", currency = "USD" }), payload,
                JsonSerializer.SerializeToElement(new { sourceSystem = "test" }), 1, at, null,
                [new SecurityIdentifierDto(SecurityIdentifierKind.InternalCode, code, true, at)], []);
            await new PostgresSecurityMasterStore(fixture.Options).UpsertProjectionAsync(record);
            var read = await new PostgresSecurityMasterStore(fixture.Options).GetDetailAsync(id);
            read.Should().NotBeNull();
            var decoded = SecurityContractualCashFlowTerms.Read(read!);
            decoded.Should().NotBeNull();
            ContractualCashFlowEngine.Hash(decoded).Should().Be(ContractualCashFlowEngine.Hash(contract));
            var calculation = ContractualCashFlowEngine.Calculate(decoded!, new(issue, at));
            calculation.Blockers.Should().BeEmpty();
            calculation.Events.Sum(e => e.ScheduledPrincipal).Should().Be(1000);
            var write = () => SecurityTermsEnvelope.EnsureWritableAsAssetSpecificTerms(assetClass, read!.AssetSpecificTerms);
            write.Should().Throw<InvalidOperationException>();
        }
    }
}
