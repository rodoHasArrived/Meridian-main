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

    [Fact]
    public async Task GetProfiles_KeepsCurrentlyEffectiveSupersededVersionSelectableUntilReplacementActivates()
    {
        // Approving a replacement with a FUTURE EffectiveFrom immediately marks the predecessor
        // Superseded, yet write-time governance keeps accepting the predecessor until the
        // replacement activates. The selectable catalog must keep exposing it during that gap;
        // otherwise the creation form offers only a version write-time validation rejects.
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var service = new SecurityAssetProfileGovernanceService();
        var firstDraft = await service.DraftProfileAsync(
            CreateDraftRequest("custom-private-credit", fieldLabel: "NAV date"),
            "settings-admin");
        var firstApproved = await service.ApproveProfileAsync(
            new SecurityAssetProfileApprovalRequestDto(
                "custom-private-credit",
                firstDraft.Profile.Version,
                today.AddDays(-30),
                "AP-001",
                null,
                "Approve first governed version.",
                null),
            "controller");
        var secondDraft = await service.DraftProfileAsync(
            CreateDraftRequest("custom-private-credit", fieldLabel: "Latest NAV date"),
            "settings-admin");
        var secondApproved = await service.ApproveProfileAsync(
            new SecurityAssetProfileApprovalRequestDto(
                "custom-private-credit",
                secondDraft.Profile.Version,
                today.AddDays(30),
                "AP-002",
                null,
                "Approve future-dated replacement.",
                null),
            "controller");

        var selectable = service.GetProfiles()
            .Where(static profile => profile.ProfileId == "custom-private-credit")
            .ToArray();

        selectable.Should().Contain(profile =>
            profile.Version == firstApproved.Profile.Version
            && profile.Status == SecurityAssetProfileStatusDto.Superseded);
        // The future-dated replacement is NOT selectable yet: its effective window has not
        // opened, so write-time governance would reject any record pinned to it today.
        selectable.Should().NotContain(profile =>
            profile.Version == secondApproved.Profile.Version);
    }

    [Fact]
    public async Task GetProfiles_HidesApprovedVersionWhoseEffectiveWindowHasNotOpened()
    {
        // The shared catalog seam backs /api/security-master/asset-profiles for create/amend
        // workflows across ALL consumers, so a freshly approved profile with a future
        // EffectiveFrom (and no predecessor) must not be exposed: write-time validation rejects
        // it with SM_CUSTOM_PROFILE_VERSION_NOT_EFFECTIVE until its window opens.
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var service = new SecurityAssetProfileGovernanceService();
        var draft = await service.DraftProfileAsync(
            CreateDraftRequest("custom-future-approval", fieldLabel: "NAV date"),
            "settings-admin");
        await service.ApproveProfileAsync(
            new SecurityAssetProfileApprovalRequestDto(
                "custom-future-approval",
                draft.Profile.Version,
                today.AddDays(30),
                "AP-001",
                null,
                "Approve future-dated first version.",
                null),
            "controller");

        service.GetProfiles()
            .Should().NotContain(profile => profile.ProfileId == "custom-future-approval");
        service.GetAllProfiles()
            .Should().Contain(profile => profile.ProfileId == "custom-future-approval");
    }

    [Fact]
    public async Task GetProfiles_HidesSupersededVersionWhoseEffectiveWindowHasClosed()
    {
        // Once the replacement's effective window has actually opened, the superseded
        // predecessor's window is closed and it must drop out of the selectable catalog again.
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var service = new SecurityAssetProfileGovernanceService();
        var firstDraft = await service.DraftProfileAsync(
            CreateDraftRequest("custom-expired-window", fieldLabel: "NAV date"),
            "settings-admin");
        var firstApproved = await service.ApproveProfileAsync(
            new SecurityAssetProfileApprovalRequestDto(
                "custom-expired-window",
                firstDraft.Profile.Version,
                today.AddDays(-60),
                "AP-001",
                null,
                "Approve first governed version.",
                null),
            "controller");
        var secondDraft = await service.DraftProfileAsync(
            CreateDraftRequest("custom-expired-window", fieldLabel: "Latest NAV date"),
            "settings-admin");
        await service.ApproveProfileAsync(
            new SecurityAssetProfileApprovalRequestDto(
                "custom-expired-window",
                secondDraft.Profile.Version,
                today.AddDays(-10),
                "AP-002",
                null,
                "Approve already-active replacement.",
                null),
            "controller");

        service.GetProfiles()
            .Where(static profile => profile.ProfileId == "custom-expired-window")
            .Should().NotContain(profile => profile.Version == firstApproved.Profile.Version);

        // A BACKDATED write effective inside the superseded version's historical window must
        // still discover it: write-time governance evaluates against the write's effective date,
        // and the as-of catalog seam exposes the version whose window covered that date.
        service.GetProfiles(today.AddDays(-30))
            .Where(static profile => profile.ProfileId == "custom-expired-window")
            .Should().ContainSingle(profile => profile.Version == firstApproved.Profile.Version);
    }

    [Fact]
    public async Task ApproveProfileAsync_EffectiveDateNotAfterIncumbentStart_IsRejected()
    {
        // Superseding an incumbent with an effective date on or before the incumbent's own
        // EffectiveFrom would assign it an EffectiveTo preceding its start - a corrupt window
        // that strands every write date the incumbent formerly governed.
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var service = new SecurityAssetProfileGovernanceService();
        var firstDraft = await service.DraftProfileAsync(
            CreateDraftRequest("custom-backdated", fieldLabel: "NAV date"),
            "settings-admin");
        await service.ApproveProfileAsync(
            new SecurityAssetProfileApprovalRequestDto(
                "custom-backdated",
                firstDraft.Profile.Version,
                today.AddDays(-30),
                "AP-001",
                null,
                "Approve first governed version.",
                null),
            "controller");
        var secondDraft = await service.DraftProfileAsync(
            CreateDraftRequest("custom-backdated", fieldLabel: "Latest NAV date"),
            "settings-admin");

        var act = async () => await service.ApproveProfileAsync(
            new SecurityAssetProfileApprovalRequestDto(
                "custom-backdated",
                secondDraft.Profile.Version,
                today.AddDays(-45),
                "AP-002",
                null,
                "Approve backdated replacement.",
                null),
            "controller");

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*must be after the incumbent approved version*");
    }

    [Fact]
    public async Task DraftProfileAsync_DuplicateIdentifierKinds_RetainStrictestCloseRequirement()
    {
        // [Cusip optional, Cusip required] must not normalize to optional: the merged preference
        // keeps IsRequiredForClose when any duplicate declared it.
        var service = new SecurityAssetProfileGovernanceService();
        var request = CreateDraftRequest("custom-duplicate-identifiers") with
        {
            IdentifierPreferences =
            [
                new SecurityAssetProfileIdentifierPreferenceDto(
                    SecurityIdentifierKind.Cusip,
                    false,
                    "CUSIP coverage helps vendor joins."),
                new SecurityAssetProfileIdentifierPreferenceDto(
                    SecurityIdentifierKind.Cusip,
                    true,
                    "CUSIP is mandatory for close."),
                new SecurityAssetProfileIdentifierPreferenceDto(
                    SecurityIdentifierKind.InternalCode,
                    true,
                    "Internal identity is required.")
            ]
        };

        var draft = await service.DraftProfileAsync(request, "settings-admin");

        var cusip = draft.Profile.IdentifierPreferences
            .Should().ContainSingle(preference => preference.Kind == SecurityIdentifierKind.Cusip)
            .Subject;
        cusip.IsRequiredForClose.Should().BeTrue();
    }

    [Fact]
    public async Task DraftProfileAsync_DateOrderRuleOverNonDateFields_IsRejected()
    {
        // Runtime date-order validation silently passes when either value is not a date, so a
        // rule over non-Date fields would advertise an ordering control that never enforces.
        var service = new SecurityAssetProfileGovernanceService();
        var request = CreateDraftRequest("custom-bad-date-rule") with
        {
            DateOrderRules =
            [
                new SecurityAssetProfileDateOrderRuleDto(
                    "navDate",
                    "note",
                    "SM_PROFILE_BAD_ORDER",
                    "NAV date must precede the note.")
            ],
            Fields =
            [
                new SecurityAssetProfileFieldDefinitionDto(
                    "navDate",
                    "NAV date",
                    SecurityAssetProfileFieldTypeDto.Date,
                    IsRequired: true,
                    AllowedValues: [],
                    Description: "Latest valuation date.",
                    MinValue: null,
                    MaxValue: null,
                    IsProjected: true,
                    IsSearchable: false),
                new SecurityAssetProfileFieldDefinitionDto(
                    "note",
                    "Note",
                    SecurityAssetProfileFieldTypeDto.Text,
                    IsRequired: false,
                    AllowedValues: [],
                    Description: "Free-text note.",
                    MinValue: null,
                    MaxValue: null,
                    IsProjected: false,
                    IsSearchable: false)
            ]
        };

        var act = async () => await service.DraftProfileAsync(request, "settings-admin");

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*must reference Date-typed profile fields*");
    }

    [Fact]
    public void GetPromotionCandidates_FlagsStructuredCreditForFirstClassFixedIncomePackage()
    {
        var service = new SecurityAssetProfileGovernanceService();

        var candidates = service.GetPromotionCandidates();

        var structuredCredit = candidates.Should()
            .ContainSingle(candidate => candidate.ProfileId == "structured-credit-io-po")
            .Subject;
        structuredCredit.Readiness.Should().Be(SecurityAssetProfilePromotionReadinessDto.ReadyForFirstClassPackage);
        structuredCredit.IsCandidate.Should().BeTrue();
        structuredCredit.RecommendedPackageId.Should().Be("fixed-income.structured-credit");
        structuredCredit.DedicatedBehaviorNeeds.Should().Contain(new[]
        {
            "factor schedule projection",
            "income accrual",
            "typed projection"
        });
        structuredCredit.Signals.Should().Contain(signal => signal.Code == "projection.factor-schedule");
        structuredCredit.PromotionRationale.Should().Contain("without breaking existing Security Master IDs");
    }

    [Fact]
    public async Task GetPromotionCandidates_KeepsLowComplexityProfilesOnWatchlist()
    {
        var service = new SecurityAssetProfileGovernanceService();
        var draft = await service.DraftProfileAsync(CreateLowComplexityDraftRequest(), "settings-admin");
        await service.ApproveProfileAsync(
            new SecurityAssetProfileApprovalRequestDto(
                "custom-watchlist-note",
                draft.Profile.Version,
                new DateOnly(2026, 6, 1),
                "AP-WATCHLIST",
                null,
                "Approve simple watchlist profile.",
                "corr-watchlist-approve"),
            "controller");

        var watchlist = service.GetPromotionCandidates()
            .Single(candidate => candidate.ProfileId == "custom-watchlist-note");

        watchlist.Readiness.Should().Be(SecurityAssetProfilePromotionReadinessDto.Watchlist);
        watchlist.IsCandidate.Should().BeFalse();
        watchlist.Signals.Should().ContainSingle(signal => signal.Code == "profile-watchlist.low-complexity");
        watchlist.PromotionRationale.Should().Contain("should remain a governed custom profile");
    }

    [Fact]
    public void GetAllProfiles_WhenPersistedSnapshotIsCorrupt_ThrowsInvalidOperationException()
    {
        var dataRoot = Path.Combine(Path.GetTempPath(), "meridian-asset-profiles", Guid.NewGuid().ToString("N"));
        var persistencePath = Path.Combine(dataRoot, "governance", "security-asset-profiles.json");
        Directory.CreateDirectory(Path.GetDirectoryName(persistencePath)!);
        File.WriteAllText(persistencePath, "{ not-valid-json");
        var service = new SecurityAssetProfileGovernanceService(new StorageOptions { RootPath = dataRoot });

        var act = () => service.GetAllProfiles();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*security-asset-profiles.json*corrupt*");
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

    private static SecurityAssetProfileDraftRequestDto CreateLowComplexityDraftRequest()
        => new(
            ProfileId: "custom-watchlist-note",
            Name: "Custom Watchlist Note",
            Category: "ReferenceOnly",
            SubType: null,
            Fields:
            [
                new SecurityAssetProfileFieldDefinitionDto(
                    "note",
                    "Note",
                    SecurityAssetProfileFieldTypeDto.Text,
                    IsRequired: true,
                    AllowedValues: [],
                    Description: "Reference-only note.",
                    MinValue: null,
                    MaxValue: null,
                    IsProjected: false,
                    IsSearchable: false)
            ],
            IdentifierPreferences: [],
            LifecycleStates: ["Active"],
            AccountingImpactHints: [],
            DateOrderRules: [],
            RequestedBy: null,
            Rationale: "Stage simple reference-only profile.",
            CorrelationId: "corr-watchlist-draft");

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
