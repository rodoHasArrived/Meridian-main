using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.AccountingSystem;
using Meridian.Contracts.Ledger;
using Meridian.DataIntegration.AccountingSystem.QuickBooks;
using Meridian.FinancialOperations.AccountingSystem;
using Meridian.FinancialOperations.Onboarding;
using Meridian.Ledger;

namespace Meridian.Tests.Ui;

public sealed partial class AccountingSystemIntegrationServiceTests
{
    private const string OnboardingTenant = "onboarding-tenant";
    private const string OnboardingCompany = "onboarding-company";
    private static readonly DateOnly OnboardingDate = new(2026, 1, 31);

    [Fact]
    public async Task OnboardingCapture_MissingRetainedImport_NeverInvokesAnExternalProvider()
    {
        var calls = 0;
        var provider = new TransformingQuickBooksAccountingProvider(detail => { calls++; return detail; });
        var service = new AccountingSystemIntegrationService([provider], CreateMatchedQuickBooksFixtureLedgerStore());
        var result = await service.CaptureRetainedOnboardingAsync("quickbooks-fixture", "default-fund", ExternalGlLedgerBookId,
            "not-retained", "not-retained", "not-retained", OnboardingDate, OnboardingTenant, OnboardingCompany);
        result.Should().BeNull();
        calls.Should().Be(0, "onboarding must not trigger provider imports as a fallback for missing evidence");
    }

    [Theory]
    [InlineData("tenant")]
    [InlineData("company")]
    [InlineData("fund")]
    [InlineData("book")]
    [InlineData("import")]
    [InlineData("date")]
    [InlineData("mapping")]
    public async Task OnboardingCapture_RequiresExactRetainedImportScopeDateAndMapping(string mismatch)
    {
        var calls = 0;
        var provider = new TransformingQuickBooksAccountingProvider(detail => { calls++; return detail; });
        var service = new AccountingSystemIntegrationService([provider], CreateMatchedQuickBooksFixtureLedgerStore());
        var import = await service.ImportAsync(OnboardingImportRequest());
        var profile = await RetainOnboardingMapping(service, CertifiedQuickBooksMappingProfile());
        var version = AccountingSystemIntegrationService.GetOnboardingMappingVersion(profile);
        var result = await service.CaptureRetainedOnboardingAsync("quickbooks-fixture",
            mismatch == "fund" ? "other-fund" : "default-fund",
            mismatch == "book" ? Guid.NewGuid() : ExternalGlLedgerBookId,
            mismatch == "import" ? "other-import" : import.Summary.ImportId,
            mismatch == "mapping" ? "other-profile" : profile.ProfileId, version,
            mismatch == "date" ? OnboardingDate.AddDays(-1) : OnboardingDate,
            mismatch == "tenant" ? "other-tenant" : OnboardingTenant,
            mismatch == "company" ? "other-company" : OnboardingCompany);
        result.Should().BeNull();
        calls.Should().Be(1, "scope misses must not silently import current provider data");
    }

    [Fact]
    public async Task OnboardingCapture_ExplicitMappingWinsOverNewerProfiles_AndChangedVersionFailsClosed()
    {
        // Account mappings apply to retained report balances. The generic fixture has only an
        // activity-period preview, which also treats its unposted zero investment row as missing.
        var provider = new TransformingQuickBooksAccountingProvider(detail => detail with
        {
            Summary = detail.Summary with { TrialBalanceBasis = new(new(2026, 1, 1), [], null) }
        });
        var service = new AccountingSystemIntegrationService([provider], CreateMatchedQuickBooksFixtureLedgerStore());
        var import = await service.ImportAsync(OnboardingImportRequest());
        var exact = await RetainOnboardingMapping(service, CertifiedQuickBooksMappingProfile());
        var version = AccountingSystemIntegrationService.GetOnboardingMappingVersion(exact);
        var newer = await RetainOnboardingMapping(service, exact with
        {
            ProfileId = "newer-profile",
            DisplayName = "Newer differently mapped profile",
            UpdatedAtUtc = exact.UpdatedAtUtc.AddDays(1),
            AccountMappings = exact.AccountMappings.ToDictionary(pair => pair.Key, pair => pair.Key switch
            {
                "Assets:Cash:Operating" => "qbo-1500",
                "Assets:Investments:Public" => "qbo-1000",
                _ => pair.Value
            })
        });
        var retained = await CaptureOnboarding(service, import.Summary.ImportId, exact.ProfileId, version);
        retained.Should().NotBeNull();
        retained!.Mapping.ProfileId.Should().Be(exact.ProfileId);
        retained.MappingVersion.Should().Be(version);
        retained.Reconciliation.BreakCount.Should().Be(0);
        retained.Reconciliation.Rows.Should().ContainSingle(row => row.AccountCode == "Assets:Cash:Operating"
            && row.ExternalDebit == 248750m && row.MeridianDebit == 248750m && row.Variance == 0m);
        var remapped = (await CaptureOnboarding(service, import.Summary.ImportId, newer.ProfileId,
            AccountingSystemIntegrationService.GetOnboardingMappingVersion(newer)))!;
        remapped.Reconciliation.Rows.Should().ContainSingle(row => row.AccountCode == "Assets:Cash:Operating"
            && row.ExternalDebit == 0m && row.MeridianDebit == 248750m && row.Variance == -248750m);
        remapped.Reconciliation.Rows.Should().ContainSingle(row => row.AccountCode == "Assets:Investments:Public"
            && row.ExternalDebit == 248750m && row.MeridianDebit == 0m && row.Variance == 248750m);
        remapped.Reconciliation.BreakCount.Should().Be(2);
        var changed = await RetainOnboardingMapping(service, exact with
        {
            DisplayName = "Revised mapping",
            UpdatedAtUtc = exact.UpdatedAtUtc.AddDays(2)
        });
        AccountingSystemIntegrationService.GetOnboardingMappingVersion(changed).Should().NotBe(version);
        await Assert.ThrowsAsync<InvalidOperationException>(() => CaptureOnboarding(service, import.Summary.ImportId, exact.ProfileId, version));
        retained.Mapping.DisplayName.Should().Be(exact.DisplayName);
        retained.MappingVersion.Should().Be(version);
    }

    [Fact]
    public async Task OnboardingCapture_FullPrecisionInputsSurviveNewImportMappingAndLedgerRecords()
    {
        var delta = 0.001m;
        var provider = new TransformingQuickBooksAccountingProvider(detail => detail with
        {
            Summary = detail.Summary with { ImportId = "exact-subcent-import", ImportedAtUtc = DateTimeOffset.Parse("2026-02-01T00:00:00Z") },
            TrialBalance = detail.TrialBalance.Select(line => line with
            {
                Debit = line.Debit > 0m ? line.Debit + delta : line.Debit,
                Credit = line.Credit > 0m ? line.Credit + delta : line.Credit
            }).ToArray()
        });
        var baseLedger = CreateMatchedQuickBooksFixtureLedgerStore();
        var book = (await baseLedger.GetLedgerBookAsync(ExternalGlLedgerBookId))!;
        var period = (await baseLedger.ListPeriodsAsync(ExternalGlLedgerBookId)).Single();
        var records = (await baseLedger.GetByPeriodAsync(period.PeriodId)).ToList();
        var service = new AccountingSystemIntegrationService([provider], new StaticLedgerJournalStore(book, period, records));
        var firstImport = await service.ImportAsync(OnboardingImportRequest());
        var profile = await RetainOnboardingMapping(service, CertifiedQuickBooksMappingProfile());
        var version = AccountingSystemIntegrationService.GetOnboardingMappingVersion(profile);
        var first = (await CaptureOnboarding(service, firstImport.Summary.ImportId, profile.ProfileId, version))!;
        var frozenJson = JsonSerializer.Serialize(first);
        first.Import.TrialBalance.Should().Contain(line => line.Debit == 248750.001m);

        delta = 0.002m;
        var secondImport = await service.ImportAsync(OnboardingImportRequest());
        // Existing reconciliation hashes round amounts to cents. Onboarding retains the actual decimal payload.
        secondImport.Summary.ContentHash.Should().Be(firstImport.Summary.ContentHash);
        var second = (await CaptureOnboarding(service, secondImport.Summary.ImportId, profile.ProfileId, version))!;
        second.Import.TrialBalance.Should().Contain(line => line.Debit == 248750.002m);
        OnboardingWorkspaceService.HashSourcePayload(JsonSerializer.Serialize(second.Import))
            .Should().NotBe(OnboardingWorkspaceService.HashSourcePayload(JsonSerializer.Serialize(first.Import)));
        var previous = records[0];
        var entry = previous.Entry;
        var changedLines = entry.Lines.Select(line => new LedgerEntry(line.EntryId, line.JournalEntryId, line.Timestamp,
            line.Account, line.Debit > 0 ? line.Debit + 0.001m : 0m,
            line.Credit > 0 ? line.Credit + 0.002m : 0m, line.Description, line.Dimensions, line.Currency)).ToArray();
        records[0] = previous with { Entry = new JournalEntry(entry.JournalEntryId, entry.Timestamp, entry.Description, changedLines, entry.Metadata) };
        var newLedger = (await CaptureOnboarding(service, secondImport.Summary.ImportId, profile.ProfileId, version))!;
        newLedger.Journals[0].Entry.Lines.Should().Contain(line => line.Debit == 248750.001m);
        first.Journals[0].Entry.Lines.Should().Contain(line => line.Debit == 248750m);
        await RetainOnboardingMapping(service, profile with
        {
            DisplayName = "Mapping after readiness capture",
            UpdatedAtUtc = profile.UpdatedAtUtc.AddDays(1)
        });
        // Mutating the caller's imported array is also isolated from the retained capture.
        ((IList<AccountingSystemTrialBalanceLineDto>)firstImport.TrialBalance)[0] = firstImport.TrialBalance[0] with { Debit = 999m };
        JsonSerializer.Serialize(first).Should().Be(frozenJson);
    }

    [Fact]
    public void OnboardingMappingVersion_BindsContentsAndIgnoresAccountDictionaryEnumerationOrder()
    {
        var original = CertifiedQuickBooksMappingProfile();
        var reordered = original with { AccountMappings = original.AccountMappings.Reverse().ToDictionary(pair => pair.Key, pair => pair.Value) };
        AccountingSystemIntegrationService.GetOnboardingMappingVersion(reordered)
            .Should().Be(AccountingSystemIntegrationService.GetOnboardingMappingVersion(original));
        var changed = original with
        {
            AccountMappings = original.AccountMappings.ToDictionary(pair => pair.Key,
                pair => pair.Key == "Assets:Cash:Operating" ? "qbo-1500" : pair.Value)
        };
        AccountingSystemIntegrationService.GetOnboardingMappingVersion(changed)
            .Should().NotBe(AccountingSystemIntegrationService.GetOnboardingMappingVersion(original));
    }

    private static AccountingSystemImportRequestDto OnboardingImportRequest() => new(
        "quickbooks-fixture", "default-fund", ExternalGlLedgerBookId, new(2026, 1, 1), OnboardingDate,
        TenantId: OnboardingTenant, CompanyId: OnboardingCompany);

    private static Task<ExternalGlMappingProfileDto> RetainOnboardingMapping(AccountingSystemIntegrationService service,
        ExternalGlMappingProfileDto profile) => service.UpsertMappingProfileAsync(new(profile, "accounting-ops",
            FundProfileId: "default-fund", LedgerBookId: ExternalGlLedgerBookId,
            EvidenceLinks: [$"approval:external-gl-mapping:{profile.ProfileId}"], TenantId: OnboardingTenant, CompanyId: OnboardingCompany));

    private static Task<AccountingOnboardingCapture?> CaptureOnboarding(AccountingSystemIntegrationService service,
        string importId, string profileId, string version) => service.CaptureRetainedOnboardingAsync(
            "quickbooks-fixture", "default-fund", ExternalGlLedgerBookId, importId, profileId, version,
            OnboardingDate, OnboardingTenant, OnboardingCompany);
}
