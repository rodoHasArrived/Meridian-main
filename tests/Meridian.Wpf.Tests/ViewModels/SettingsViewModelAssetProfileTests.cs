using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Api;
using Meridian.Contracts.SecurityMaster;
using Meridian.Wpf.Services;
using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed class SettingsViewModelAssetProfileTests
{
    [Fact]
    public void RefreshAssetProfilesCommand_ShouldLoadApprovedProfilesAndFieldInputs()
    {
        WpfTestThread.Run(async () =>
        {
            var profile = BuildApprovedProfile();
            var client = new StubSecurityAssetProfileWorkflowClient
            {
                Profiles = [profile]
            };
            var viewModel = CreateViewModel(client);

            await viewModel.RefreshAssetProfilesCommand.ExecuteAsync(null);

            viewModel.AssetProfileRows.Should().ContainSingle();
            viewModel.SelectedAssetProfileRow.Should().NotBeNull();
            viewModel.SelectedAssetProfileRow!.ProfileId.Should().Be(profile.ProfileId);
            viewModel.AssetProfileFieldInputs.Should().HaveCount(profile.Fields.Count);
            viewModel.AssetProfileStatusText.Should().Contain("Loaded 1 approved");
            viewModel.ApprovedAssetProfileCount.Should().Be(1);
        });
    }

    [Fact]
    public void CreateProfileBackedSecurityCommand_ShouldPostCustomAssetPinnedToSelectedProfile()
    {
        WpfTestThread.Run(async () =>
        {
            var profile = BuildApprovedProfile();
            var client = new StubSecurityAssetProfileWorkflowClient
            {
                Profiles = [profile]
            };
            var viewModel = CreateViewModel(client);
            await viewModel.RefreshAssetProfilesCommand.ExecuteAsync(null);

            viewModel.ProfileBackedSecurityDisplayName = "Flagship LP Side Pocket";
            viewModel.ProfileBackedSecurityInternalCode = "ALT-LP-001";
            viewModel.ProfileBackedSecurityCurrency = "usd";
            viewModel.AssetProfileFieldInputs.Single(input => input.Key == "commitmentAmount").Value = "1250000.50";
            viewModel.AssetProfileFieldInputs.Single(input => input.Key == "isEvergreen").Value = "true";
            viewModel.AssetProfileFieldInputs.Single(input => input.Key == "valuationMethod").Value = "NAV";

            viewModel.CreateProfileBackedSecurityCommand.CanExecute(null).Should().BeTrue();
            await viewModel.CreateProfileBackedSecurityCommand.ExecuteAsync(null);

            client.CreateSecurityRequest.Should().NotBeNull();
            var request = client.CreateSecurityRequest!;
            request.AssetClass.Should().Be("CustomAsset");
            request.SourceSystem.Should().Be("Meridian.Settings.AssetProfiles");
            request.SourceRecordId.Should().Be($"asset-profile:{profile.ProfileId}:v{profile.Version}");
            request.CommonTerms.GetProperty("displayName").GetString().Should().Be("Flagship LP Side Pocket");
            request.CommonTerms.GetProperty("currency").GetString().Should().Be("USD");
            request.Identifiers.Should().ContainSingle(identifier =>
                identifier.Kind == SecurityIdentifierKind.InternalCode
                && identifier.Value == "ALT-LP-001"
                && identifier.IsPrimary);

            var assetTerms = request.AssetSpecificTerms;
            assetTerms.GetProperty("schemaVersion").GetInt32().Should().Be(SecurityMasterSchemaVersions.CustomAssetProfileTerms);
            assetTerms.GetProperty("customProfileId").GetString().Should().Be(profile.ProfileId);
            assetTerms.GetProperty("profileVersion").GetInt32().Should().Be(profile.Version);
            assetTerms.GetProperty("profileApproval").GetProperty("approvalReference").GetString()
                .Should().Be($"profile:{profile.ProfileId}:v{profile.Version}");

            var fields = assetTerms.GetProperty("profileFields");
            fields.GetProperty("commitmentAmount").GetDecimal().Should().Be(1250000.50m);
            fields.GetProperty("isEvergreen").GetBoolean().Should().BeTrue();
            fields.GetProperty("valuationMethod").GetString().Should().Be("NAV");
            viewModel.AssetProfileStatusText.Should().Contain("Security created");
        });
    }

    [Fact]
    public void AssetProfileFieldInputs_ShouldExposeTypedGuidedFormSurfaces()
    {
        WpfTestThread.Run(async () =>
        {
            var profile = BuildApprovedProfile();
            var client = new StubSecurityAssetProfileWorkflowClient
            {
                Profiles = [profile]
            };
            var viewModel = CreateViewModel(client);
            await viewModel.RefreshAssetProfilesCommand.ExecuteAsync(null);

            var commitment = viewModel.AssetProfileFieldInputs.Single(input => input.Key == "commitmentAmount");
            commitment.IsTextEntryField.Should().BeTrue();
            commitment.InputHint.Should().Be("Decimal (0 to any)");

            var evergreen = viewModel.AssetProfileFieldInputs.Single(input => input.Key == "isEvergreen");
            evergreen.IsBooleanField.Should().BeTrue();
            evergreen.BoolValue.Should().BeFalse();
            evergreen.BoolValue = true;
            evergreen.Value.Should().Be("true");

            var valuation = viewModel.AssetProfileFieldInputs.Single(input => input.Key == "valuationMethod");
            valuation.IsEnumField.Should().BeTrue();
            valuation.IsTextEntryField.Should().BeFalse();
            valuation.AllowedValues.Should().Equal("NAV", "Cost");

            var dateInput = new SettingsAssetProfileFieldInput(
                new SecurityAssetProfileFieldDefinitionDto(
                    "navDate", "NAV date", SecurityAssetProfileFieldTypeDto.Date,
                    true, [], null, null, null, true, false),
                static () => { });
            dateInput.IsDateField.Should().BeTrue();
            dateInput.DateValue = new DateTime(2026, 4, 30);
            dateInput.Value.Should().Be("2026-04-30");
            dateInput.DateValue.Should().Be(new DateTime(2026, 4, 30));
        });
    }

    [Fact]
    public void CreateProfileBackedSecurityCommand_ShouldBlockValuesOutsideDeclaredFieldRange()
    {
        WpfTestThread.Run(async () =>
        {
            var profile = BuildApprovedProfile();
            var client = new StubSecurityAssetProfileWorkflowClient
            {
                Profiles = [profile]
            };
            var viewModel = CreateViewModel(client);
            await viewModel.RefreshAssetProfilesCommand.ExecuteAsync(null);

            viewModel.ProfileBackedSecurityDisplayName = "Flagship LP Side Pocket";
            viewModel.ProfileBackedSecurityInternalCode = "ALT-LP-001";
            viewModel.ProfileBackedSecurityCurrency = "USD";
            // commitmentAmount declares MinValue = 0; a negative entry must be rejected before post.
            viewModel.AssetProfileFieldInputs.Single(input => input.Key == "commitmentAmount").Value = "-5";
            viewModel.AssetProfileFieldInputs.Single(input => input.Key == "valuationMethod").Value = "NAV";

            await viewModel.CreateProfileBackedSecurityCommand.ExecuteAsync(null);

            client.CreateSecurityRequest.Should().BeNull();
            viewModel.AssetProfileStatusText.Should().Contain("Commitment amount must be at least 0");
        });
    }

    [Fact]
    public void GovernanceCommands_ShouldUseSharedDraftApproveRollbackAndLineageRequests()
    {
        WpfTestThread.Run(async () =>
        {
            var profile = BuildApprovedProfile();
            var lineage = BuildLineage(profile);
            var client = new StubSecurityAssetProfileWorkflowClient
            {
                Profiles = [profile],
                Lineage = lineage,
                DraftResponse = BuildGovernanceResult(profile with
                {
                    Version = 2,
                    Status = SecurityAssetProfileStatusDto.Draft,
                    ChangeReason = "Drafted"
                }),
                ApproveResponse = BuildGovernanceResult(profile with
                {
                    Version = 2,
                    Status = SecurityAssetProfileStatusDto.Approved,
                    ChangeReason = "Approved"
                }),
                RollbackResponse = BuildGovernanceResult(profile)
            };
            var viewModel = CreateViewModel(client);
            await viewModel.RefreshAssetProfilesCommand.ExecuteAsync(null);

            viewModel.AssetProfileDraftProfileId = profile.ProfileId;
            viewModel.AssetProfileDraftName = "LP Interest revised";
            viewModel.AssetProfileDraftCategory = "Alternatives";
            viewModel.AssetProfileDraftRationale = "Governed profile update";
            await viewModel.DraftAssetProfileCommand.ExecuteAsync(null);

            client.DraftRequest.Should().NotBeNull();
            client.DraftRequest!.ProfileId.Should().Be(profile.ProfileId);
            client.DraftRequest.Fields.Should().HaveCount(profile.Fields.Count);
            client.DraftRequest.IdentifierPreferences.Should().HaveCount(profile.IdentifierPreferences.Count);

            await viewModel.ApproveAssetProfileCommand.ExecuteAsync(null);

            client.ApproveRequest.Should().NotBeNull();
            client.ApproveRequest!.ProfileId.Should().Be(profile.ProfileId);
            client.ApproveRequest.Version.Should().Be(2);
            client.ApproveRequest.Rationale.Should().Be("Governed profile update");

            await viewModel.LoadAssetProfileLineageCommand.ExecuteAsync(null);

            client.LineageProfileId.Should().Be(profile.ProfileId);
            viewModel.AssetProfileLineageSummaryText.Should().Contain("version");

            viewModel.AssetProfileRollbackVersion = "1";
            await viewModel.RollbackAssetProfileCommand.ExecuteAsync(null);

            client.RollbackRequest.Should().NotBeNull();
            client.RollbackRequest!.ProfileId.Should().Be(profile.ProfileId);
            client.RollbackRequest.TargetVersion.Should().Be(1);
        });
    }

    private static SettingsViewModel CreateViewModel(ISecurityAssetProfileWorkflowClient client)
        => new(
            ConfigService.Instance,
            NotificationService.Instance,
            StatusService.Instance,
            assetProfileClient: client);

    private static SecurityAssetProfileDefinitionDto BuildApprovedProfile()
        => new(
            "lp-interest",
            1,
            "LP Interest",
            "Alternatives",
            "Limited Partnership",
            SecurityAssetProfileStatusDto.Approved,
            [
                new SecurityAssetProfileFieldDefinitionDto(
                    "commitmentAmount",
                    "Commitment amount",
                    SecurityAssetProfileFieldTypeDto.Decimal,
                    true,
                    [],
                    "Committed capital amount.",
                    0,
                    null,
                    true,
                    true),
                new SecurityAssetProfileFieldDefinitionDto(
                    "isEvergreen",
                    "Evergreen",
                    SecurityAssetProfileFieldTypeDto.Boolean,
                    false,
                    [],
                    null,
                    null,
                    null,
                    false,
                    false),
                new SecurityAssetProfileFieldDefinitionDto(
                    "valuationMethod",
                    "Valuation method",
                    SecurityAssetProfileFieldTypeDto.Enum,
                    true,
                    ["NAV", "Cost"],
                    null,
                    null,
                    null,
                    true,
                    true)
            ],
            [
                new SecurityAssetProfileIdentifierPreferenceDto(
                    SecurityIdentifierKind.InternalCode,
                    true,
                    "Desktop-created custom assets need an internal operating code.")
            ],
            ["Active", "Exited"],
            [SecurityAssetProfileAccountingImpactHintDto.NavBasedValuation],
            [],
            DateOnly.FromDateTime(DateTime.UtcNow.Date),
            null,
            "governance",
            DateTimeOffset.UtcNow.AddHours(-1),
            "Approved baseline");

    private static SecurityAssetProfileLineageDto BuildLineage(SecurityAssetProfileDefinitionDto profile)
        => new(
            profile.ProfileId,
            [profile],
            [
                new SecurityAssetProfileGovernanceAuditEventDto(
                    "audit-1",
                    "Approved",
                    DateTimeOffset.UtcNow,
                    "governance",
                    profile.ChangeReason,
                    "test-correlation",
                    profile.ProfileId,
                    profile.Version,
                    profile.Status,
                    null,
                    $"profile:{profile.ProfileId}:v{profile.Version}")
            ]);

    private static SecurityAssetProfileGovernanceResultDto BuildGovernanceResult(SecurityAssetProfileDefinitionDto profile)
        => new(
            profile,
            BuildLineage(profile),
            new SecurityAssetProfileGovernanceAuditEventDto(
                $"audit-{profile.Version}",
                profile.Status.ToString(),
                DateTimeOffset.UtcNow,
                "governance",
                profile.ChangeReason,
                "test-correlation",
                profile.ProfileId,
                profile.Version,
                profile.Status,
                profile.Version > 1 ? profile.Version - 1 : null,
                $"profile:{profile.ProfileId}:v{profile.Version}"));

    private sealed class StubSecurityAssetProfileWorkflowClient : ISecurityAssetProfileWorkflowClient
    {
        public IReadOnlyList<SecurityAssetProfileDefinitionDto> Profiles { get; init; } = [];
        public SecurityAssetProfileLineageDto? Lineage { get; init; }
        public SecurityAssetProfileGovernanceResultDto? DraftResponse { get; init; }
        public SecurityAssetProfileGovernanceResultDto? ApproveResponse { get; init; }
        public SecurityAssetProfileGovernanceResultDto? RollbackResponse { get; init; }
        public SecurityAssetProfileDraftRequestDto? DraftRequest { get; private set; }
        public SecurityAssetProfileApprovalRequestDto? ApproveRequest { get; private set; }
        public SecurityAssetProfileRollbackRequestDto? RollbackRequest { get; private set; }
        public CreateSecurityRequest? CreateSecurityRequest { get; private set; }
        public string? LineageProfileId { get; private set; }

        public Task<IReadOnlyList<SecurityAssetProfileDefinitionDto>> GetProfilesAsync(CancellationToken ct = default)
            => Task.FromResult(Profiles);

        public Task<SecurityAssetProfileLineageDto?> GetLineageAsync(string profileId, CancellationToken ct = default)
        {
            LineageProfileId = profileId;
            return Task.FromResult(Lineage);
        }

        public Task<ApiResponse<SecurityAssetProfileGovernanceResultDto>> DraftProfileAsync(
            SecurityAssetProfileDraftRequestDto request,
            CancellationToken ct = default)
        {
            DraftRequest = request;
            return Task.FromResult(ApiResponse<SecurityAssetProfileGovernanceResultDto>.Ok(
                DraftResponse ?? BuildGovernanceResult(Profiles[0] with { Status = SecurityAssetProfileStatusDto.Draft })));
        }

        public Task<ApiResponse<SecurityAssetProfileGovernanceResultDto>> ApproveProfileAsync(
            SecurityAssetProfileApprovalRequestDto request,
            CancellationToken ct = default)
        {
            ApproveRequest = request;
            return Task.FromResult(ApiResponse<SecurityAssetProfileGovernanceResultDto>.Ok(
                ApproveResponse ?? BuildGovernanceResult(Profiles[0])));
        }

        public Task<ApiResponse<SecurityAssetProfileGovernanceResultDto>> RollbackProfileAsync(
            SecurityAssetProfileRollbackRequestDto request,
            CancellationToken ct = default)
        {
            RollbackRequest = request;
            return Task.FromResult(ApiResponse<SecurityAssetProfileGovernanceResultDto>.Ok(
                RollbackResponse ?? BuildGovernanceResult(Profiles[0])));
        }

        public Task<ApiResponse<SecurityDetailDto>> CreateSecurityAsync(CreateSecurityRequest request, CancellationToken ct = default)
        {
            CreateSecurityRequest = request;
            var detail = new SecurityDetailDto(
                request.SecurityId,
                request.AssetClass,
                SecurityStatusDto.Active,
                request.CommonTerms.GetProperty("displayName").GetString() ?? string.Empty,
                request.CommonTerms.GetProperty("currency").GetString() ?? string.Empty,
                request.CommonTerms,
                request.AssetSpecificTerms,
                request.Identifiers,
                [],
                1,
                request.EffectiveFrom,
                null);

            return Task.FromResult(ApiResponse<SecurityDetailDto>.Ok(detail));
        }
    }
}
