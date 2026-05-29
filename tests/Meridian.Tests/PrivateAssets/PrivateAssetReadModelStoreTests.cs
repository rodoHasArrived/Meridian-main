using FluentAssertions;
using Meridian.Application.PrivateAssets;
using Meridian.Contracts.PrivateAssets;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Tests.PrivateAssets;

public sealed class PrivateAssetReadModelStoreTests
{
    [Fact]
    public async Task InMemoryStore_ProjectsPrivateAssetReadModel_WithTypedEvidenceAndNoWarnings()
    {
        var store = new InMemoryPrivateAssetReadModelStore(new PrivateAssetReadModelValidator(staleNavDays: 120));
        var asset = BuildAsset(
            evidenceLinks:
            [
                new PrivateAssetEvidenceLinkDto(
                    PrivateAssetEvidenceKindDto.ImportedStatement,
                    "statement-2026-q1",
                    "Q1 imported capital account statement",
                    "vault://private-assets/statement-2026-q1",
                    new DateOnly(2026, 4, 15)),
                new PrivateAssetEvidenceLinkDto(
                    PrivateAssetEvidenceKindDto.CapitalNotice,
                    "capital-notice-2026-05",
                    "May 2026 capital call notice",
                    "vault://private-assets/capital-notice-2026-05",
                    new DateOnly(2026, 5, 1)),
                new PrivateAssetEvidenceLinkDto(
                    PrivateAssetEvidenceKindDto.K1,
                    "k1-2025",
                    "2025 K-1",
                    "vault://private-assets/k1-2025",
                    new DateOnly(2026, 3, 15)),
                new PrivateAssetEvidenceLinkDto(
                    PrivateAssetEvidenceKindDto.SideLetter,
                    "side-letter",
                    "Most favored nation side letter",
                    "vault://private-assets/side-letter",
                    new DateOnly(2024, 1, 15)),
                new PrivateAssetEvidenceLinkDto(
                    PrivateAssetEvidenceKindDto.SubscriptionDocument,
                    "subscription-docs",
                    "Executed subscription package",
                    "vault://private-assets/subscription-docs",
                    new DateOnly(2024, 1, 10)),
                new PrivateAssetEvidenceLinkDto(
                    PrivateAssetEvidenceKindDto.ValuationMemo,
                    "valuation-memo-q1",
                    "Q1 valuation memo",
                    "vault://private-assets/valuation-memo-q1",
                    new DateOnly(2026, 4, 20))
            ],
            commitmentSchedule:
            [
                new PrivateAssetCommitmentScheduleEntryDto(new DateOnly(2026, 6, 30), 250_000m, "USD", "Expected Q2 call", "capital-notice-2026-05")
            ]);

        await store.UpsertAsync(asset, new DateOnly(2026, 5, 29));

        var readModel = await store.GetAsync("pa-001", new DateOnly(2026, 5, 29));

        readModel.Should().NotBeNull();
        readModel!.Asset.AssetType.Should().Be(PrivateAssetTypeDto.PrivateEquityFund);
        readModel.Asset.EvidenceLinks.Select(static link => link.Kind).Should().Contain([
            PrivateAssetEvidenceKindDto.ImportedStatement,
            PrivateAssetEvidenceKindDto.CapitalNotice,
            PrivateAssetEvidenceKindDto.K1,
            PrivateAssetEvidenceKindDto.SideLetter,
            PrivateAssetEvidenceKindDto.SubscriptionDocument,
            PrivateAssetEvidenceKindDto.ValuationMemo]);
        readModel.Warnings.Should().BeEmpty();
    }

    [Fact]
    public async Task InMemoryStore_EmitsValidationWarnings_ForMissingAndStalePrivateAssetInputs()
    {
        var store = new InMemoryPrivateAssetReadModelStore(new PrivateAssetReadModelValidator(staleNavDays: 120));
        var asset = BuildAsset(
            owningEntityId: " ",
            valuationDate: new DateOnly(2025, 12, 31),
            evidenceLinks: [],
            commitmentSchedule: []);

        await store.UpsertAsync(asset, new DateOnly(2026, 5, 29));

        var readModel = await store.GetAsync("pa-001", new DateOnly(2026, 5, 29));

        readModel.Should().NotBeNull();
        readModel!.Warnings.Select(static warning => warning.Code).Should().Contain([
            PrivateAssetValidationWarningCodeDto.StaleNav,
            PrivateAssetValidationWarningCodeDto.MissingOwnerEntity,
            PrivateAssetValidationWarningCodeDto.MissingCommitmentSchedule,
            PrivateAssetValidationWarningCodeDto.MissingEvidence]);
        readModel.Warnings.Should().NotContain(static warning => warning.Code == PrivateAssetValidationWarningCodeDto.MissingValuationDate);
    }

    [Fact]
    public void Validator_EmitsMissingValuationDateWarning_WhenNavDateIsAbsent()
    {
        var validator = new PrivateAssetReadModelValidator(staleNavDays: 120);
        var asset = BuildAsset(valuationDate: null) with { ValuationDate = null };

        var warnings = validator.Validate(asset, new DateOnly(2026, 5, 29));

        warnings.Select(static warning => warning.Code).Should().Contain(PrivateAssetValidationWarningCodeDto.MissingValuationDate);
        warnings.Select(static warning => warning.Code).Should().NotContain(PrivateAssetValidationWarningCodeDto.StaleNav);
    }

    [Fact]
    public async Task FileStore_PersistsAndFiltersReadModels_ByOwningEntity()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-private-assets-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new FilePrivateAssetReadModelStore(
                root,
                NullLogger<FilePrivateAssetReadModelStore>.Instance,
                new PrivateAssetReadModelValidator(staleNavDays: 120));
            await store.UpsertAsync(BuildAsset(privateAssetId: "pa-001", assetName: "Alpha PE", owningEntityId: "entity-a"), new DateOnly(2026, 5, 29));
            await store.UpsertAsync(BuildAsset(privateAssetId: "pa-002", assetName: "Beta VC", owningEntityId: "entity-b", assetType: PrivateAssetTypeDto.VentureFund), new DateOnly(2026, 5, 29));

            var reloaded = new FilePrivateAssetReadModelStore(
                root,
                NullLogger<FilePrivateAssetReadModelStore>.Instance,
                new PrivateAssetReadModelValidator(staleNavDays: 120));

            var rows = await reloaded.ListAsync("entity-b", new DateOnly(2026, 5, 29));

            rows.Should().ContainSingle();
            rows[0].Asset.PrivateAssetId.Should().Be("pa-002");
            rows[0].Asset.AssetType.Should().Be(PrivateAssetTypeDto.VentureFund);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task InMemoryStore_PropagatesCancellation_BeforeMutation()
    {
        var store = new InMemoryPrivateAssetReadModelStore();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => store.UpsertAsync(BuildAsset(), new DateOnly(2026, 5, 29), cts.Token);

        act.Should().Throw<OperationCanceledException>();
        var rows = await store.ListAsync(asOfDate: new DateOnly(2026, 5, 29));
        rows.Should().BeEmpty();
    }

    [Fact]
    public void PrivateAssetTypeCatalog_CoversSupportedPrivateAssetTypes()
    {
        Enum.GetNames<PrivateAssetTypeDto>().Should().BeEquivalentTo([
            nameof(PrivateAssetTypeDto.PrivateEquityFund),
            nameof(PrivateAssetTypeDto.VentureFund),
            nameof(PrivateAssetTypeDto.PrivateCredit),
            nameof(PrivateAssetTypeDto.RealEstate),
            nameof(PrivateAssetTypeDto.DirectCompanyInvestment),
            nameof(PrivateAssetTypeDto.Collectible),
            nameof(PrivateAssetTypeDto.OperatingBusiness),
            nameof(PrivateAssetTypeDto.Other)]);
    }

    private static PrivateAssetDto BuildAsset(
        string privateAssetId = "pa-001",
        string assetName = "Alpha PE Fund I",
        PrivateAssetTypeDto assetType = PrivateAssetTypeDto.PrivateEquityFund,
        string? owningEntityId = "entity-a",
        DateOnly? valuationDate = null,
        IReadOnlyList<PrivateAssetEvidenceLinkDto>? evidenceLinks = null,
        IReadOnlyList<PrivateAssetCommitmentScheduleEntryDto>? commitmentSchedule = null)
        => new(
            PrivateAssetId: privateAssetId,
            AssetName: assetName,
            AssetType: assetType,
            OwningEntityId: owningEntityId,
            ManagerOrSponsor: "Alpha Sponsor",
            CommitmentAmount: 1_000_000m,
            CalledCapital: 600_000m,
            UnfundedCommitment: 400_000m,
            DistributionsToDate: 125_000m,
            LatestNav: 750_000m,
            ValuationDate: valuationDate ?? new DateOnly(2026, 3, 31),
            ValuationSource: "Manager statement",
            IRR: 0.124m,
            MOIC: 1.35m,
            TVPI: 1.46m,
            DPI: 0.21m,
            Currency: "USD",
            EvidenceLinks: evidenceLinks ??
            [
                new PrivateAssetEvidenceLinkDto(
                    PrivateAssetEvidenceKindDto.ValuationMemo,
                    "valuation-memo-q1",
                    "Q1 valuation memo",
                    "vault://private-assets/valuation-memo-q1",
                    new DateOnly(2026, 4, 20))
            ],
            CommitmentSchedule: commitmentSchedule ??
            [
                new PrivateAssetCommitmentScheduleEntryDto(new DateOnly(2026, 6, 30), 250_000m, "USD", "Expected Q2 call", "valuation-memo-q1")
            ]);
}
