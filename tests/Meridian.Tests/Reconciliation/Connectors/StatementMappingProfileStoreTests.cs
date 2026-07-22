using FluentAssertions;
using Meridian.FinancialOperations.Reconciliation;
using Meridian.FinancialOperations.Reconciliation.Connectors;
using Xunit;

namespace Meridian.Tests.Reconciliation.Connectors;

public sealed class StatementMappingProfileStoreTests : IDisposable
{
    private readonly string _root = StatementConnectorTestData.CreateTempRoot("mdc_stmt_profiles");

    private static StatementMappingProfileDocument CustomProfile(string profileId = "acme-custodian-csv-v1") => new(
        SchemaVersion: 1,
        ProfileId: profileId,
        DisplayName: "Acme Custodian CSV",
        Format: "csv",
        Csv: new StatementProfileCsvOptions(),
        Culture: null,
        DateFormats: ["yyyy-MM-dd"],
        Fields:
        [
            new("Account", "Acct", null, Required: true),
            new("ActivityType", "Code", null, Required: true),
            new("TradeDate", "Date", null, Required: true),
            new("CashAmount", "Net")
        ],
        ActivityCodes: [new("BOT", "trade")]);

    [Fact]
    public async Task Upsert_RoundTripsAcrossStoreInstances()
    {
        var store = new FileStatementMappingProfileStore(_root);
        await store.UpsertAsync(CustomProfile());

        var reloaded = new FileStatementMappingProfileStore(_root);
        var profiles = await reloaded.ListAsync();

        profiles.Should().ContainSingle(profile => profile.ProfileId == "acme-custodian-csv-v1");
        profiles.Single().IsBuiltIn.Should().BeFalse();
    }

    [Fact]
    public async Task Upsert_BuiltInProfileId_Throws()
    {
        var store = new FileStatementMappingProfileStore(_root);

        var act = () => store.UpsertAsync(CustomProfile(StatementMappingProfileRegistry.CanonicalCsvV1ProfileId));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*built in*");
    }

    [Fact]
    public async Task Upsert_InvalidDocument_Throws()
    {
        var store = new FileStatementMappingProfileStore(_root);
        var invalid = CustomProfile() with { Fields = [new("Account", "Acct", null, true)] };

        var act = () => store.UpsertAsync(invalid);

        await act.Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public async Task Delete_RemovesOperatorProfile_AndRejectsBuiltIns()
    {
        var store = new FileStatementMappingProfileStore(_root);
        await store.UpsertAsync(CustomProfile());

        (await store.DeleteAsync("acme-custodian-csv-v1")).Should().BeTrue();
        (await store.DeleteAsync("acme-custodian-csv-v1")).Should().BeFalse();

        var act = () => store.DeleteAsync(StatementBuiltInProfiles.IbFlexV1ProfileId);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Load_UnsupportedSnapshotVersion_Throws()
    {
        var store = new FileStatementMappingProfileStore(_root);
        await store.UpsertAsync(CustomProfile());
        var path = Path.Combine(_root, "reconciliation", "statement-mapping-profiles.json");
        var json = await File.ReadAllTextAsync(path);
        await File.WriteAllTextAsync(path, json.Replace("\"version\": 1", "\"version\": 99"));

        var act = () => new FileStatementMappingProfileStore(_root).ListAsync();

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*version 99*");
    }

    [Fact]
    public async Task Catalog_MergesBuiltInsWithOperatorProfiles()
    {
        var catalog = new StatementMappingProfileCatalog(new FileStatementMappingProfileStore(_root));
        await catalog.UpsertAsync(CustomProfile());

        var profiles = await catalog.ListAsync();

        profiles.Select(profile => profile.ProfileId).Should().Contain(
        [
            StatementMappingProfileRegistry.CanonicalCsvV1ProfileId,
            StatementMappingProfileRegistry.SampleBrokerCsvV1ProfileId,
            StatementBuiltInProfiles.OfxBankV1ProfileId,
            StatementBuiltInProfiles.IbFlexV1ProfileId,
            StatementBuiltInProfiles.AlpacaActivityV1ProfileId,
            "acme-custodian-csv-v1"
        ]);
        (await catalog.FindAsync("ACME-CUSTODIAN-CSV-V1")).Should().NotBeNull("lookup is case-insensitive");
    }

    [Fact]
    public async Task Catalog_BuildRegistry_IncludesOperatorProfilesPresentAtBuildTime()
    {
        var catalog = new StatementMappingProfileCatalog(new FileStatementMappingProfileStore(_root));
        await catalog.UpsertAsync(CustomProfile());

        var registry = catalog.BuildRegistry();

        registry.Resolve("acme-custodian-csv-v1").DisplayName.Should().Be("Acme Custodian CSV");
        registry.Resolve(StatementMappingProfileRegistry.CanonicalCsvV1ProfileId).Should().NotBeNull();
    }

    [Fact]
    public async Task Catalog_DriftDetection_ReportsAddedAndRemovedColumns()
    {
        var catalog = new StatementMappingProfileCatalog(new FileStatementMappingProfileStore(_root));
        await catalog.UpsertAsync(CustomProfile());
        var fingerprintV1 = new StatementFormatFingerprint("hash1", ["acct", "code", "date", "net"], ",");

        // No fingerprint recorded yet: no drift signal.
        var profile = await catalog.FindAsync("acme-custodian-csv-v1");
        StatementMappingProfileCatalog.CheckDrift(profile!, fingerprintV1).Should().BeNull();

        await catalog.RecordAcceptedFingerprintAsync("acme-custodian-csv-v1", fingerprintV1);
        profile = await catalog.FindAsync("acme-custodian-csv-v1");
        StatementMappingProfileCatalog.CheckDrift(profile!, fingerprintV1).Should().BeNull("same columns are not drift");

        var drifted = new StatementFormatFingerprint("hash2", ["acct", "code", "date", "netamount"], ",");
        var issue = StatementMappingProfileCatalog.CheckDrift(profile!, drifted);

        issue.Should().NotBeNull();
        issue!.Code.Should().Be("FORMAT_DRIFT");
        issue.Severity.Should().Be(StatementParseIssue.WarningSeverity);
        issue.Message.Should().Contain("netamount").And.Contain("net");
    }

    [Fact]
    public async Task Catalog_RecordAcceptedFingerprint_IgnoresBuiltIns()
    {
        var catalog = new StatementMappingProfileCatalog(new FileStatementMappingProfileStore(_root));
        var fingerprint = new StatementFormatFingerprint("hash", ["a"], ",");

        await catalog.RecordAcceptedFingerprintAsync(StatementBuiltInProfiles.IbFlexV1ProfileId, fingerprint);

        var builtIn = await catalog.FindAsync(StatementBuiltInProfiles.IbFlexV1ProfileId);
        builtIn!.LastAcceptedFingerprint.Should().BeNull();
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
