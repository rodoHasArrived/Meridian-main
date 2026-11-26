using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.AccountingSystem;
using Meridian.Contracts.Ledger;
using Meridian.DataIntegration.AccountingSystem.QuickBooks;
using Meridian.FinancialOperations.AccountingSystem;
using Meridian.ProviderSdk.AccountingSystem;
using Xunit;

namespace Meridian.Tests.Ui;

public sealed partial class AccountingSystemIntegrationServiceTests
{
    [Fact]
    public async Task PersistentInstances_RefreshMappingsAndPreserveOtherInstancesChangesAcrossRestart()
    {
        using var directory = new ReviewStateDirectory();
        var ledger = CreateMatchedQuickBooksFixtureLedgerStore();
        var first = CreatePersistentService(ledger, directory.Path);
        var second = CreatePersistentService(ledger, directory.Path);
        var profile = CertifiedQuickBooksMappingProfile();
        await RetainReviewMapping(first, profile);
        await RetainReviewMapping(second, profile with { ProfileId = "second-reviewed-profile" });
        (await first.ListMappingProfilesAsync()).Should().HaveCount(2);
        (await CreatePersistentService(ledger, directory.Path).ListMappingProfilesAsync()).Should().HaveCount(2);
    }

    [Fact]
    public async Task PreparedPackage_PinsMappingContentsAndRefusesCertificationAfterAnotherInstanceChangesThem()
    {
        using var directory = new ReviewStateDirectory();
        var ledger = CreateMatchedQuickBooksFixtureLedgerStore();
        var first = CreatePersistentService(ledger, directory.Path);
        var second = CreatePersistentService(ledger, directory.Path);
        var profile = CertifiedQuickBooksMappingProfile();
        await RetainReviewMapping(first, profile);
        var package = await PrepareReviewPackage(first, profile.ProfileId);
        package.MappingContentHash.Should().NotBeNullOrWhiteSpace();
        await RetainReviewMapping(second, profile with { DisplayName = "Reviewed mapping revision" });
        var certify = () => first.CertifyExportPackageAsync(new(package.ExportPackageId, "controller", "Stale review",
            [ExportCertificationEvidence(package)]));
        await certify.Should().ThrowAsync<InvalidOperationException>().WithMessage("*ExternalGlReviewedMappingChanged*");
        var restored = (await first.ListExportPackagesAsync()).Should().ContainSingle().Which;
        restored.MappingContentHash.Should().Be(package.MappingContentHash);
        restored.Certification!.State.Should().Be(AccountingCertificationStateDto.ReadyForReview);
    }

    [Fact]
    public async Task MappingChangedAfterValidation_RefusesCertificationAtTheRetainedCommitBoundary()
    {
        using var directory = new ReviewStateDirectory();
        var ledger = CreateMatchedQuickBooksFixtureLedgerStore();
        var validator = new PausingReviewValidator();
        var first = new AccountingSystemIntegrationService([validator], ledger,
            stateStore: new AccountingSystemIntegrationStateStore(directory.Path));
        var second = CreatePersistentService(ledger, directory.Path);
        var profile = CertifiedQuickBooksMappingProfile();
        await RetainReviewMapping(first, profile);
        var package = await PrepareReviewPackage(first, profile.ProfileId);
        validator.Pause = true;
        var certification = first.CertifyExportPackageAsync(new(package.ExportPackageId, "controller", "Concurrent review",
            [ExportCertificationEvidence(package)]));
        try
        {
            await validator.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await RetainReviewMapping(second, profile with { DisplayName = "Concurrent reviewed revision" });
        }
        finally { validator.Continue.TrySetResult(); }
        var outcome = () => certification;
        await outcome.Should().ThrowAsync<InvalidOperationException>().WithMessage("*review dependency changed*");
        (await first.ListExportPackagesAsync()).Should().ContainSingle().Which.Certification!.State
            .Should().Be(AccountingCertificationStateDto.ReadyForReview);
    }

    [Fact]
    public async Task HistoricalUnpinnedPackage_RequiresRenewedReviewBeforeCertification()
    {
        var service = CreateService(CreateMatchedQuickBooksFixtureLedgerStore());
        var profile = CertifiedQuickBooksMappingProfile();
        await RetainReviewMapping(service, profile);
        var package = await PrepareReviewPackage(service, profile.ProfileId);
        RetainExportPackage(service, package with { MappingContentHash = null });
        var certify = () => service.CertifyExportPackageAsync(new(package.ExportPackageId, "controller", "Historical review",
            [ExportCertificationEvidence(package)]));
        await certify.Should().ThrowAsync<InvalidOperationException>().WithMessage("*ExternalGlReviewedMappingUnpinned*");
    }

    [Fact]
    public void HistoricalManifest_AbsentMappingPinPreservesTheV1DigestAndPayloadShape()
    {
        var package = new ExternalGlExportPackageDto("package", "fixture", "fund",
            Guid.Parse("11111111-1111-1111-1111-111111111111"), new(2026, 1, 1), new(2026, 1, 31),
            DateTimeOffset.Parse("2026-02-01T00:00:00Z"), "operator", false, "Disabled", [], [],
            MappingProfileId: "mapping", ReconciliationId: "reconciliation", TenantId: "tenant", CompanyId: "company",
            ReconciliationSnapshotHash: new string('a', 64));
        var hashMethod = typeof(AccountingSystemIntegrationService).GetMethod("ComputeExportPackageHash", BindingFlags.NonPublic | BindingFlags.Static)!;
        object[] arguments = [package, AccountingCertificationStateDto.ReadyForReview, Array.Empty<string>(),
            Array.Empty<AccountingConfigurationValidationIssueDto>(), ExternalGlExportReconciliationSafeguardStateDto.Ready,
            Array.Empty<string>(), false, "Disabled"];
        var oldDigest = (string)hashMethod.Invoke(null, arguments)!;
        // Golden digest from the pre-pin v1 field layout and the fixed fixture above.
        oldDigest.Should().Be("4a81d57f1d517b459d372949e2ed8420f183d4ea7fcfdd76694dc09d97b2d335");
        arguments[0] = package with { MappingContentHash = new string('b', 64) };
        ((string)hashMethod.Invoke(null, arguments)!).Should().NotBe(oldDigest);
        var manifestMethod = typeof(AccountingSystemIntegrationService).GetMethod("BuildExportPackageManifest", BindingFlags.NonPublic | BindingFlags.Static)!;
        var legacy = (ExternalGlExportPackageManifestDto)manifestMethod.Invoke(null,
            [package, Array.Empty<AccountingConfigurationValidationIssueDto>()])!;
        using var json = JsonDocument.Parse(legacy.Payload);
        json.RootElement.TryGetProperty("mappingContentHash", out _).Should().BeFalse();
    }

    private static Task<ExternalGlMappingProfileDto> RetainReviewMapping(AccountingSystemIntegrationService service,
        ExternalGlMappingProfileDto profile)
        => service.UpsertMappingProfileAsync(new(profile, "accounting-ops", FundProfileId: "default-fund",
            LedgerBookId: ExternalGlLedgerBookId, EvidenceLinks: [$"approval:external-gl-mapping:{profile.ProfileId}"]));

    private static async Task<ExternalGlExportPackageDto> PrepareReviewPackage(AccountingSystemIntegrationService service, string profileId)
    {
        await service.ImportAsync(new("quickbooks-fixture", "default-fund", ExternalGlLedgerBookId,
            new(2026, 1, 1), new(2026, 1, 31)));
        return await service.CreateExportPackageAsync(new("accounting-ops", "quickbooks-fixture", "default-fund",
            ExternalGlLedgerBookId, new(2026, 1, 1), new(2026, 1, 31), profileId,
            RequireBalancedReconciliation: false, EvidenceLinks: [ExportControlEvidence(ExternalGlLedgerBookId)]));
    }

    private sealed class PausingReviewValidator : IAccountingSystemProvider, IAccountingSystemExportValidator
    {
        private readonly QuickBooksFixtureAccountingProvider _fixture = new();
        public bool Pause { get; set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Continue { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string ProviderId => _fixture.ProviderId;
        public string DisplayName => _fixture.DisplayName;
        public AccountingSystemProviderCapabilities Capabilities => _fixture.Capabilities;
        public Task<AccountingSystemImportDetailDto> ImportAsync(AccountingSystemImportRequestDto request, CancellationToken ct = default)
            => _fixture.ImportAsync(request, ct);
        public async Task<IReadOnlyList<AccountingConfigurationValidationIssueDto>> ValidateExportAsync(
            AccountingSystemExportValidationContext context, CancellationToken ct = default)
        {
            if (Pause)
            {
                Entered.TrySetResult();
                await Continue.Task.WaitAsync(ct);
            }
            return [];
        }
    }

    private sealed class ReviewStateDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "meridian-gl-review-" + Guid.NewGuid().ToString("N"));
        public ReviewStateDirectory() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
