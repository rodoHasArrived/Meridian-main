using Meridian.Application.Integrations;
using Meridian.Contracts.Integrations;
using Meridian.Storage.Integrations;
using System.Globalization;

namespace Meridian.Tests.Application.Integrations;

public sealed class BondHoldingTemplateImportTests
{
    [Theory]
    [InlineData(2, "USD", "false", 1)]
    [InlineData(3, "USD", "false", 1)]
    [InlineData(3, "", "false", 0)]
    [InlineData(3, "USD", "unknown", 0)]
    public async Task WorkflowCsvMapsTypedBondConventionsOrQuarantinesMissingInputs(int revision, string currency, string dirty, int accepted)
    {
        var path = Path.Combine(Path.GetTempPath(), $"meridian-bond-template-{Guid.NewGuid():N}");
        try
        {
            var store = new FileProviderIntegrationManifestStore(path);
            var manifest = new ProviderIntegrationTemplateCatalog().GetManifest("template-fixed-income-security-master-v2", revision)!;
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

    [Theory]
    [InlineData("Fixed")]
    [InlineData("ZeroCoupon")]
    [InlineData("SimpleFloating")]
    [InlineData("CompoundedOvernight")]
    [InlineData("InflationLinked")]
    [InlineData("Amortizing")]
    [InlineData("SignedCoupon")]
    public async Task RevisionThreeRetainsExplicitFamilyReferenceMetadataInActualStaging(string family)
    {
        var path = Path.Combine(Path.GetTempPath(), "meridian-family-template-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new FileProviderIntegrationManifestStore(path);
            var manifest = new ProviderIntegrationTemplateCatalog().GetManifest("template-fixed-income-security-master-v2")!;
            var now = DateTimeOffset.UtcNow;
            await store.SaveManifestAsync(manifest);
            await store.SaveConnectionAsync(new("family-connection", manifest.ProviderId, manifest.ManifestId, "Reviewed reference", "template",
                default, "no-live-credentials", [ProviderCapabilityKindDto.SecurityReferenceData], "reviewer", now, now, null));
            var fields = ReferenceFields(family);
            var result = await new ProviderIntegrationDryRunService(store).RunManualCsvDryRunAsync(new("family-run", manifest.ManifestId,
                "family-connection", ProviderCapabilityKindDto.SecurityReferenceData, "family.csv", Csv(fields), "reviewer", now));
            Assert.Empty(result.Issues);
            Assert.Equal(1, result.RecordsAccepted);
            var security = Assert.Single(await store.ListStagingRecordsAsync(result.SyncRunId)).MappedRecord.GetProperty("security");
            Assert.Equal(fields["coupon_type"], security.GetProperty("couponType").GetString());
            Assert.Equal("2026-01-15", security.GetProperty("issueDate").GetString());
            Assert.Equal("Reviewed issuer", security.GetProperty("issuerName").GetString());
            Assert.Equal("SemiAnnual", security.GetProperty("paymentFrequency").GetString());
            Assert.Equal(100m, security.GetProperty("par").GetDecimal());
            Assert.False(security.GetProperty("isCallable").GetBoolean());
            Assert.Equal("Following", security.GetProperty("settlementConvention").GetString());
            Assert.False(security.TryGetProperty("internalSecurityId", out _));
            if (family is "SimpleFloating" or "CompoundedOvernight")
            {
                Assert.Equal("SOFR", security.GetProperty("floatingIndex").GetString());
                Assert.Equal(-25m, security.GetProperty("spreadBps").GetDecimal());
                Assert.Equal(-2m, security.GetProperty("floorRate").GetDecimal());
                Assert.Equal(-1.25m, security.GetProperty("resetSchedule")[0].GetProperty("fixingRate").GetDecimal());
                Assert.Equal(family == "SimpleFloating" ? "Simple" : "CompoundedOvernight",
                    security.GetProperty("bondWorkflow").GetProperty("floatingMethod").GetString());
            }
            if (family == "CompoundedOvernight")
            {
                var convention = security.GetProperty("bondWorkflow");
                Assert.Equal("WEEKENDS", convention.GetProperty("fixingCalendarId").GetString());
                Assert.Equal(2, convention.GetProperty("lookbackBusinessDays").GetInt32());
                Assert.Equal("InterestPeriod", convention.GetProperty("observationWeighting").GetString());
            }
            if (family == "InflationLinked")
            {
                Assert.Equal("CPI", security.GetProperty("inflationIndex").GetString());
                Assert.Equal(300m, security.GetProperty("inflationBaseIndexValue").GetDecimal());
                Assert.Equal(1.03m, security.GetProperty("inflationIndexRatio").GetDecimal());
                Assert.Equal(3, security.GetProperty("bondWorkflow").GetProperty("lagMonths").GetInt32());
                Assert.True(security.GetProperty("bondWorkflow").GetProperty("interpolateMonthly").GetBoolean());
            }
            if (family == "Amortizing")
            {
                Assert.Equal(25m, security.GetProperty("principalSchedule")[0].GetProperty("amount").GetDecimal());
                Assert.Equal(.75m, security.GetProperty("factorSchedule")[0].GetProperty("factor").GetDecimal());
            }
            if (family is "Fixed" or "ZeroCoupon" or "InflationLinked" or "SignedCoupon")
                Assert.Equal(decimal.Parse(fields["coupon"], CultureInfo.InvariantCulture), security.GetProperty("coupon").GetDecimal());
        }
        finally { if (Directory.Exists(path)) Directory.Delete(path, true); }
    }

    [Fact]
    public async Task RevisionTwoOriginalReplayKeepsItsExactMappingAfterRevisionThreeIsCurrent()
    {
        var path = Path.Combine(Path.GetTempPath(), "meridian-template-replay-" + Guid.NewGuid().ToString("N"));
        try
        {
            var catalog = new ProviderIntegrationTemplateCatalog();
            var original = catalog.GetManifest("template-fixed-income-security-master-v2", 2)!;
            var current = catalog.GetManifest(original.ManifestId)!;
            var store = new FileProviderIntegrationManifestStore(path);
            var now = DateTimeOffset.UtcNow;
            await store.SaveManifestAsync(original);
            await store.SaveConnectionAsync(new("replay-connection", original.ProviderId, original.ManifestId, "Retained reference", "template",
                default, "no-live-credentials", [ProviderCapabilityKindDto.SecurityReferenceData], "reviewer", now, now, null));
            var fields = ReferenceFields("SimpleFloating");
            fields.Remove("payment_calendar_id");
            var first = await new ProviderIntegrationDryRunService(store).RunManualCsvDryRunAsync(new("original-v2", original.ManifestId,
                "replay-connection", ProviderCapabilityKindDto.SecurityReferenceData, "reference.csv", Csv(fields), "reviewer", now));
            var quarantined = Assert.Single(await store.ListQuarantinedRecordsAsync(first.SyncRunId));
            await store.SaveManifestVersionAsync(current);
            Assert.True(await store.CompareExchangeCurrentManifestAsync(original.ManifestId,
                ProviderIntegrationManifestIdentity.Create(original), ProviderIntegrationManifestIdentity.Create(current)));
            var restarted = new FileProviderIntegrationManifestStore(path);
            var replay = await new ProviderIntegrationQuarantineReplayService(restarted).ReplayAsync(new("original-replay", first.SyncRunId,
                original.ManifestId, "replay-connection", ProviderCapabilityKindDto.SecurityReferenceData,
                [quarantined.QuarantineRecordId], "reviewer", now));
            Assert.Equal(1, replay.RecordsRequarantined);
            var row = Assert.Single(await restarted.ListQuarantinedRecordsAsync(replay.ReplaySyncRunId));
            Assert.True(row.MappedRecord.HasValue);
            Assert.False(row.MappedRecord.Value.GetProperty("security").TryGetProperty("couponType", out _));
            var retained = await restarted.GetRawPayloadAsync(replay.ReplaySyncRunId, replay.RawPayloadId);
            Assert.Equal(ProviderIntegrationManifestIdentity.Create(original), retained!.ManifestReference);
            Assert.Equal(3, (await restarted.GetManifestAsync(original.ManifestId))!.ManifestVersion);
        }
        finally { if (Directory.Exists(path)) Directory.Delete(path, true); }
    }

    private static Dictionary<string, string> ReferenceFields(string family)
    {
        var fields = new Dictionary<string, string>
        {
            ["cusip"] = "9128285M8",
            ["description"] = "Reviewed bond",
            ["maturity_date"] = "2028-01-15",
            ["currency"] = "USD",
            ["terms_version"] = "5",
            ["coupon_unit"] = "Percent",
            ["day_count"] = "Actual/360",
            ["payment_calendar_id"] = "WEEKENDS",
            ["schedule_conventions_json"] = """{"accrualAdjusted":false,"couponRateUnit":"Percent","couponCashBasis":"DayCount","paymentCalendars":["WEEKENDS"],"businessDayConvention":"Following"}""",
            ["issue_date"] = "2026-01-15",
            ["issuer_name"] = "Reviewed issuer",
            ["coupon_type"] = "Fixed",
            ["coupon"] = "5",
            ["payment_frequency"] = "SemiAnnual",
            ["principal_face"] = "100",
            ["is_callable"] = "false",
            ["settlement_days"] = "1",
            ["settlement_convention"] = "Following",
            ["settlement_calendar_id"] = "WEEKENDS"
        };
        if (family == "ZeroCoupon")
        { fields["coupon_type"] = "ZeroCoupon"; fields["coupon"] = "0"; }
        if (family == "SignedCoupon")
            fields["coupon"] = "-0.75";
        if (family is "SimpleFloating" or "CompoundedOvernight")
        {
            fields.Remove("coupon");
            fields["coupon_type"] = "Floating";
            fields["floating_index"] = "SOFR";
            fields["spread_bps"] = "-25";
            fields["cap_rate"] = "4";
            fields["floor_rate"] = "-2";
            fields["reset_schedule_json"] = """[{"resetDate":"2026-01-15","fixingRate":-1.25}]""";
            fields["bond_workflow_json"] = family == "SimpleFloating" ? """{"floatingMethod":"Simple"}"""
                : """{"floatingMethod":"CompoundedOvernight","fixingCalendarId":"WEEKENDS","lookbackBusinessDays":2,"observationMethod":"LookbackWithoutShift","observationWeighting":"InterestPeriod","lockoutBusinessDays":0}""";
        }
        if (family == "InflationLinked")
        {
            fields["coupon_type"] = "InflationLinked";
            fields["coupon"] = "1.5";
            fields["inflation_index"] = "CPI";
            fields["inflation_base_index"] = "300";
            fields["inflation_index_ratio"] = "1.03";
            fields["bond_workflow_json"] = """{"lagMonths":3,"interpolateMonthly":true,"couponFloorRatio":0.95,"redemptionFloorRatio":1}""";
        }
        if (family == "Amortizing")
        {
            fields["principal_schedule_json"] = """[{"paymentDate":"2027-01-15","amount":25},{"paymentDate":"2028-01-15","amount":75}]""";
            fields["factor_schedule_json"] = """[{"asOfDate":"2027-01-15","factor":0.75},{"asOfDate":"2028-01-15","factor":0}]""";
        }
        return fields;
    }

    private static string Csv(IReadOnlyDictionary<string, string> fields)
    {
        static string Escape(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
        var columns = fields.ToArray();
        return string.Join(",", columns.Select(pair => Escape(pair.Key))) + "\n"
            + string.Join(",", columns.Select(pair => Escape(pair.Value))) + "\n";
    }
}
