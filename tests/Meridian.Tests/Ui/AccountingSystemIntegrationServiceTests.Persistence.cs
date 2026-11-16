using FluentAssertions;
using Meridian.Contracts.AccountingSystem;
using Meridian.DataIntegration.AccountingSystem.QuickBooks;
using Meridian.FinancialOperations.AccountingSystem;
using Meridian.ProviderSdk.AccountingSystem;
using Meridian.Storage.Ledger;
using Xunit;

namespace Meridian.Tests.Ui;

public sealed partial class AccountingSystemIntegrationServiceTests
{
    [Fact]
    public async Task StateStore_RetainsImportsMappingProfilesAndExportPackagesAcrossRestart()
    {
        var directory = Path.Combine(Path.GetTempPath(), "meridian-external-gl-" + Guid.NewGuid().ToString("N"));
        try
        {
            var ledger = CreateMatchedQuickBooksFixtureLedgerStore();
            var first = CreatePersistentService(ledger, directory);
            var profile = CertifiedQuickBooksMappingProfile();
            await first.UpsertMappingProfileAsync(new AccountingSystemMappingProfileUpsertRequestDto(
                profile,
                "accounting-ops",
                FundProfileId: "default-fund",
                LedgerBookId: ExternalGlLedgerBookId,
                EvidenceLinks: ["approval:external-gl-mapping:qbo-default-fund-certified"]));
            var import = await first.ImportAsync(new AccountingSystemImportRequestDto(
                "quickbooks-fixture",
                "default-fund",
                ExternalGlLedgerBookId,
                PeriodStart: new DateOnly(2026, 1, 1),
                PeriodEnd: new DateOnly(2026, 1, 31)));
            var package = await first.CreateExportPackageAsync(new AccountingSystemExportPackageRequestDto(
                "accounting-ops",
                ProviderId: "quickbooks-fixture",
                FundProfileId: "default-fund",
                LedgerBookId: ExternalGlLedgerBookId,
                PeriodStart: new DateOnly(2026, 1, 1),
                PeriodEnd: new DateOnly(2026, 1, 31),
                MappingProfileId: profile.ProfileId,
                RequireBalancedReconciliation: false,
                EvidenceLinks: [ExportControlEvidence(ExternalGlLedgerBookId)]));
            var firstProfiles = await first.ListMappingProfilesAsync("quickbooks-fixture", "default-fund");

            // A new instance over the same directory stands in for the restarted host.
            var restarted = CreatePersistentService(ledger, directory);

            (await restarted.ListMappingProfilesAsync("quickbooks-fixture", "default-fund"))
                .Should().BeEquivalentTo(firstProfiles);
            (await restarted.ListExportPackagesAsync("quickbooks-fixture", "default-fund"))
                .Should().ContainSingle().Which.Should().BeEquivalentTo(package);
            (await restarted.GetLatestImportAsync("quickbooks-fixture", "default-fund", ExternalGlLedgerBookId))
                .Should().BeEquivalentTo(import);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void StateStore_UnreadableRetainedState_FailsClosedInsteadOfStartingEmpty()
    {
        var directory = Path.Combine(Path.GetTempPath(), "meridian-external-gl-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "export-packages.json"), "{ not json");

            var act = () => CreatePersistentService(null, directory);

            act.Should().Throw<InvalidOperationException>().WithMessage("*export-packages*cannot be read*");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static AccountingSystemIntegrationService CreatePersistentService(
        ILedgerJournalStore? ledgerJournalStore, string directory)
        => new(
            new IAccountingSystemProvider[] { new QuickBooksFixtureAccountingProvider() },
            ledgerJournalStore,
            ledgerJournalStore is null ? null : new ActualLedgerFixtureSchedule(ledgerJournalStore),
            new AccountingSystemIntegrationStateStore(directory));
}
