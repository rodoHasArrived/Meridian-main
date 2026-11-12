using Meridian.Application.Integrations;
using Meridian.Contracts.Integrations;
using Meridian.Storage.Integrations;

namespace Meridian.Tests.Application.Integrations;

public sealed class BondHoldingTemplateImportTests
{
    [Theory]
    [InlineData("USD", "false", 1)]
    [InlineData("", "false", 0)]
    [InlineData("USD", "unknown", 0)]
    public async Task VersionTwoCsvMapsTypedBondConventionsOrQuarantinesMissingInputs(string currency, string dirty, int accepted)
    {
        var path = Path.Combine(Path.GetTempPath(), $"meridian-bond-template-{Guid.NewGuid():N}");
        try
        {
            var store = new FileProviderIntegrationManifestStore(path);
            var manifest = new ProviderIntegrationTemplateCatalog().GetManifest("template-fixed-income-security-master-v2")!;
            var now = DateTimeOffset.UtcNow;
            await store.SaveManifestAsync(manifest);
            await store.SaveConnectionAsync(new("synthetic-connection", manifest.ProviderId, manifest.ManifestId, "Synthetic", "template",
                default, "synthetic-no-secret", [ProviderCapabilityKindDto.Holdings], "synthetic", now, now, null));
            var content = "account_id,as_of_date,cusip,original_face,current_face,principal_factor,inflation_ratio,quote_basis,invoice_basis,quote,quote_is_dirty,accrued_interest,market_value,currency,valuation_basis,source_id\n"
                + $"{Guid.NewGuid()},2026-11-12,9128285M8,100,80,0.8,1,PercentOfCurrentFace,Nominal,99,{dirty},0,79.2,{currency},ProviderReported,SYNTHETIC-1\n";
            var result = await new ProviderIntegrationDryRunService(store).RunManualCsvDryRunAsync(new("synthetic-run",
                manifest.ManifestId, "synthetic-connection", ProviderCapabilityKindDto.Holdings, "synthetic.csv", content, "synthetic", now));
            Assert.Equal(accepted, result.RecordsAccepted);
            var rows = await store.ListStagingRecordsAsync(result.SyncRunId);
            if (accepted == 1)
            {
                var bond = Assert.Single(rows).MappedRecord.GetProperty("bond");
                Assert.False(bond.GetProperty("quoteIsDirty").GetBoolean());
                Assert.Equal(.8m, bond.GetProperty("principalFactor").GetDecimal());
            }
            else
                Assert.Empty(rows);
        }
        finally { if (Directory.Exists(path)) Directory.Delete(path, true); }
    }
}
