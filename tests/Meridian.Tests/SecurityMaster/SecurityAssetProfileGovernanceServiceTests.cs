using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage;
using Meridian.Storage.SecurityMaster;
using NSubstitute;

namespace Meridian.Tests.SecurityMaster;

public sealed class SecurityAssetProfileGovernanceServiceTests
{
    [Fact]
    public async Task ProfileLifecycle_DraftsApprovesPersistsAndRollsBackVersionLineage()
    {
        var dataRoot = Path.Combine(Path.GetTempPath(), "meridian-asset-profiles", Guid.NewGuid().ToString("N"));
        var service = new SecurityAssetProfileGovernanceService(new StorageOptions { RootPath = dataRoot });

        var draft = await service.DraftProfileAsync(
            CreateDraftRequest("custom-private-credit", fieldLabel: "NAV date"),
            "settings-admin");
        var approved = await service.ApproveProfileAsync(
            new SecurityAssetProfileApprovalRequestDto(
                "custom-private-credit",
                draft.Profile.Version,
                new DateOnly(2026, 6, 1),
                "AP-001",
                null,
                "Approve first governed version.",
                "corr-approve-1"),
            "controller");
        var secondDraft = await service.DraftProfileAsync(
            CreateDraftRequest("custom-private-credit", fieldLabel: "Latest NAV date"),
            "settings-admin");
        var secondApproved = await service.ApproveProfileAsync(
            new SecurityAssetProfileApprovalRequestDto(
                "custom-private-credit",
                secondDraft.Profile.Version,
                new DateOnly(2026, 7, 1),
                "AP-002",
                null,
                "Approve revised label.",
                "corr-approve-2"),
            "controller");

        var reloaded = new SecurityAssetProfileGovernanceService(new StorageOptions { RootPath = dataRoot });
        reloaded.TryGetProfile("custom-private-credit", approved.Profile.Version, out var historical).Should().BeTrue();
        historical.Status.Should().Be(SecurityAssetProfileStatusDto.Superseded);
        reloaded.TryGetLatestApprovedProfile("custom-private-credit", out var latest).Should().BeTrue();
        latest.Version.Should().Be(secondApproved.Profile.Version);

        var rollback = await reloaded.RollbackProfileAsync(
            new SecurityAssetProfileRollbackRequestDto(
                "custom-private-credit",
                approved.Profile.Version,
                new DateOnly(2026, 8, 1),
                "AP-003",
                null,
                "Rollback revised label after controller review.",
                "corr-rollback"),
            "controller");

        rollback.Profile.Version.Should().Be(3);
        rollback.Profile.Status.Should().Be(SecurityAssetProfileStatusDto.Approved);
        rollback.Profile.Fields.Should().ContainSingle(field => field.Label == "NAV date");
        rollback.AuditEvent.EventType.Should().Be("security-asset-profile-rollback-approved");
        rollback.Lineage.Versions.Select(static profile => profile.Version).Should().Contain([1, 2, 3]);
        rollback.Lineage.AuditEvents.Should().Contain(audit => audit.ApprovalReference == "AP-003");
    }

    [Fact]
    public async Task DraftProfileAsync_RejectsReservedFieldKeys()
    {
        var service = new SecurityAssetProfileGovernanceService();
        var request = CreateDraftRequest("bad-profile", fieldKey: "profileVersion");

        var act = async () => await service.DraftProfileAsync(request, "settings-admin");

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*reserved*");
    }

    [Fact]
    public async Task SupersededApprovedProfileVersion_RemainsValidForPinnedSecurityRecords()
    {
        var profileService = new SecurityAssetProfileGovernanceService();
        var firstDraft = await profileService.DraftProfileAsync(
            CreateDraftRequest("custom-private-credit", fieldLabel: "NAV date"),
            "settings-admin");
        var firstApproved = await profileService.ApproveProfileAsync(
            new SecurityAssetProfileApprovalRequestDto(
                "custom-private-credit",
                firstDraft.Profile.Version,
                new DateOnly(2026, 6, 1),
                "AP-001",
                null,
                "Approve first governed version.",
                null),
            "controller");
        var secondDraft = await profileService.DraftProfileAsync(
            CreateDraftRequest("custom-private-credit", fieldLabel: "Latest NAV date"),
            "settings-admin");
        await profileService.ApproveProfileAsync(
            new SecurityAssetProfileApprovalRequestDto(
                "custom-private-credit",
                secondDraft.Profile.Version,
                new DateOnly(2026, 7, 1),
                "AP-002",
                null,
                "Approve replacement version.",
                null),
            "controller");

        profileService.TryGetProfile("custom-private-credit", firstApproved.Profile.Version, out var historical).Should().BeTrue();
        historical.Status.Should().Be(SecurityAssetProfileStatusDto.Superseded);

        var store = Substitute.For<ISecurityMasterStore>();
        var validation = new SecurityValidationService(
            store,
            AssetClassValidatorRegistry.CreateDefault(profileService));
        var record = CreateProfileBackedRecord(firstApproved.Profile.Version);

        var report = validation.ValidateRecord(record, [record], new DateTimeOffset(2026, 6, 15, 0, 0, 0, TimeSpan.Zero));

        report.Issues.Should().NotContain(issue => issue.Code == "SM_CUSTOM_PROFILE_NOT_APPROVED");
    }

    private static SecurityAssetProfileDraftRequestDto CreateDraftRequest(
        string profileId,
        string fieldKey = "navDate",
        string fieldLabel = "NAV date")
        => new(
            ProfileId: profileId,
            Name: "Custom Private Credit",
            Category: "PrivateCredit",
            SubType: "LP Interest",
            Fields:
            [
                new SecurityAssetProfileFieldDefinitionDto(
                    fieldKey,
                    fieldLabel,
                    SecurityAssetProfileFieldTypeDto.Date,
                    IsRequired: true,
                    AllowedValues: [],
                    Description: "Latest valuation date.",
                    MinValue: null,
                    MaxValue: null,
                    IsProjected: true,
                    IsSearchable: true)
            ],
            IdentifierPreferences:
            [
                new SecurityAssetProfileIdentifierPreferenceDto(
                    SecurityIdentifierKind.InternalCode,
                    true,
                    "Private credit profiles require internal identity for close readiness.")
            ],
            LifecycleStates: ["Diligence", "Active", "Exited"],
            AccountingImpactHints:
            [
                SecurityAssetProfileAccountingImpactHintDto.NavBasedValuation,
                SecurityAssetProfileAccountingImpactHintDto.LedgerClassification
            ],
            DateOrderRules: [],
            RequestedBy: null,
            Rationale: "Stage governed private credit profile.",
            CorrelationId: "corr-draft");

    private static SecurityProjectionRecord CreateProfileBackedRecord(int profileVersion)
        => new(
            SecurityId: Guid.NewGuid(),
            AssetClass: "CustomAsset",
            Status: SecurityStatusDto.Active,
            DisplayName: "Custom Private Credit",
            Currency: "USD",
            PrimaryIdentifierKind: "InternalCode",
            PrimaryIdentifierValue: "CPC-1",
            CommonTerms: JsonSerializer.SerializeToElement(new
            {
                displayName = "Custom Private Credit",
                currency = "USD",
                lotSize = 1,
                tickSize = 0.01m
            }),
            AssetSpecificTerms: JsonSerializer.SerializeToElement(new
            {
                schemaVersion = SecurityMasterSchemaVersions.CustomAssetProfileTerms,
                customProfileId = "custom-private-credit",
                profileVersion,
                profileFields = new
                {
                    navDate = "2026-06-10"
                },
                profileApproval = new
                {
                    approvedBy = "controller",
                    approvedAtUtc = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
                    approvalReference = "AP-001"
                }
            }),
            Provenance: JsonSerializer.SerializeToElement(new
            {
                sourceSystem = "test",
                updatedBy = "settings-admin",
                asOf = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero)
            }),
            Version: 1,
            EffectiveFrom: new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
            EffectiveTo: null,
            Identifiers:
            [
                new SecurityIdentifierDto(
                    SecurityIdentifierKind.InternalCode,
                    "CPC-1",
                    true,
                    new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
                    null,
                    null)
            ],
            Aliases: []);
}
