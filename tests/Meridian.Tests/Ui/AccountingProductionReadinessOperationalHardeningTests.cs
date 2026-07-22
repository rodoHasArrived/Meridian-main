using FluentAssertions;
using Meridian.Contracts.AccountingSystem;
using Meridian.Contracts.AssetOperations;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Meridian.Tests.Ui;

public sealed class AccountingProductionReadinessOperationalHardeningTests
{
    private const string TenantId = "tenant-alpha";
    private const string CompanyId = "company-alpha";
    private const string FundProfileId = "default-fund";
    private static readonly Guid LedgerBookId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

    [Fact]
    public async Task ProductionReadinessService_AllBooleansAndEndpointRoutesWithoutTypedEvidence_RemainsBlocked()
    {
        var services = new ServiceCollection();
        services.AddSingleton<AccountingProductionReadinessService>();
        await using var provider = services.BuildServiceProvider();

        var readiness = await provider.GetRequiredService<AccountingProductionReadinessService>()
            .AssessAsync(AllBooleanRequest() with
            {
                LedgerBookWorkflowEvidenceLinks =
                [
                    "https://example.invalid/tenant-alpha/company-alpha/default-fund/ledger-book/posting-rules",
                    $"evidence://ledger-book/{LedgerBookId:D}/workflow-certification/full"
                ],
                DimensionalReportingEvidenceLinks =
                [
                    $"evidence://ledger-book/{LedgerBookId:D}/dimensions/full/dimension-scope/arbitrary"
                ],
                TenantAdministrationEvidenceLinks =
                [
                    $"evidence://tenant-admin/{TenantId}/{CompanyId}/ledger-book/{LedgerBookId:D}/tenant-admin/full"
                ]
            });

        readiness.Status.Should().Be(AccountingProductionReadinessStatusDto.Blocked);
        readiness.LedgerBookWorkflows.Should().NotBeNull();
        readiness.LedgerBookWorkflows!.CompletedControlCount.Should().Be(1);
        readiness.LedgerBookWorkflows.HasRetainedEvidence.Should().BeFalse();
        readiness.DimensionalReporting.Should().NotBeNull();
        readiness.DimensionalReporting!.CompletedControlCount.Should().Be(1);
        readiness.DimensionalReporting.HasRetainedEvidence.Should().BeFalse();
        readiness.TenantAdministration.Should().NotBeNull();
        readiness.TenantAdministration!.CompletedControlCount.Should().Be(2);
        readiness.TenantAdministration.HasRetainedEvidence.Should().BeFalse();
        readiness.Components
            .Where(component => component.Route is not null)
            .Should()
            .OnlyContain(component => !component.EvidenceReferences.Contains(component.Route!));
    }

    [Fact]
    public async Task ProductionReadinessService_CompleteTypedEvidenceAndScopedPassedArtifacts_CompleteEvidenceControls()
    {
        var workflow = WorkflowArtifact();
        var dimensional = DimensionalArtifact();
        var tenantAdministration = TenantAdministrationArtifact();
        var retainedEvidence = new[]
        {
            CompleteEvidence(AccountingProductionCertificationEvidenceSubjectTypes.WorkflowArtifact, workflow.CertificationId, "workflow"),
            CompleteEvidence(AccountingProductionCertificationEvidenceSubjectTypes.DimensionalArtifact, dimensional.CertificationId, "dimensional"),
            CompleteEvidence(AccountingProductionCertificationEvidenceSubjectTypes.TenantAdministrationArtifact, tenantAdministration.CertificationId, "tenant-admin")
        };
        var services = new ServiceCollection();
        services.AddSingleton<AccountingProductionReadinessService>();
        await using var provider = services.BuildServiceProvider();

        var readiness = await provider.GetRequiredService<AccountingProductionReadinessService>()
            .AssessAsync(new AccountingProductionReadinessRequestDto(
                FundProfileId: FundProfileId,
                LedgerBookId: LedgerBookId,
                TenantId: TenantId,
                CompanyId: CompanyId,
                WorkflowCertificationArtifacts: [workflow],
                DimensionalCertificationArtifacts: [dimensional],
                TenantAdminCertificationArtifacts: [tenantAdministration],
                RetainedEvidence: retainedEvidence));

        readiness.LedgerBookWorkflows!.CompletedControlCount.Should().Be(10);
        readiness.DimensionalReporting!.CompletedControlCount.Should().Be(10);
        readiness.TenantAdministration!.CompletedControlCount.Should().Be(23);
        readiness.Issues.Should().NotContain(issue =>
            issue.Code.EndsWith("-evidence-missing", StringComparison.OrdinalIgnoreCase) ||
            issue.Code.EndsWith("-evidence-scope-mismatch", StringComparison.OrdinalIgnoreCase) ||
            issue.Code.EndsWith("-book-evidence-missing", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(nameof(RetainedEvidenceIdentityDto.EvidenceId))]
    [InlineData(nameof(RetainedEvidenceIdentityDto.EvidenceUri))]
    [InlineData(nameof(RetainedEvidenceIdentityDto.ContentHashSha256))]
    [InlineData(nameof(RetainedEvidenceIdentityDto.SourceSystem))]
    [InlineData(nameof(RetainedEvidenceIdentityDto.SourceReference))]
    [InlineData(nameof(RetainedEvidenceIdentityDto.ReviewStatus))]
    [InlineData(nameof(RetainedEvidenceIdentityDto.ReviewedBy))]
    [InlineData(nameof(RetainedEvidenceIdentityDto.ReviewedAtUtc))]
    [InlineData(nameof(RetainedEvidenceIdentityDto.EffectiveDate))]
    [InlineData(nameof(RetainedEvidenceIdentityDto.EvidenceVersion))]
    [InlineData(nameof(RetainedEvidenceIdentityDto.RetainedAtUtc))]
    [InlineData(nameof(RetainedEvidenceIdentityDto.RetainedBy))]
    [InlineData(nameof(RetainedEvidenceIdentityDto.SubjectType))]
    [InlineData(nameof(RetainedEvidenceIdentityDto.SubjectId))]
    public async Task CertificationProfileStore_EachIncompleteRetainedEvidenceField_IsRejected(string fieldName)
    {
        var artifact = WorkflowArtifact([AccountingWorkflowCertificationLaneKindDto.PostingRules]);
        var complete = CompleteEvidence(
            AccountingProductionCertificationEvidenceSubjectTypes.WorkflowArtifact,
            artifact.CertificationId,
            "workflow");
        var incomplete = fieldName switch
        {
            nameof(RetainedEvidenceIdentityDto.EvidenceId) => complete with { EvidenceId = " " },
            nameof(RetainedEvidenceIdentityDto.EvidenceUri) => complete with { EvidenceUri = " " },
            nameof(RetainedEvidenceIdentityDto.ContentHashSha256) => complete with { ContentHashSha256 = " " },
            nameof(RetainedEvidenceIdentityDto.SourceSystem) => complete with { SourceSystem = " " },
            nameof(RetainedEvidenceIdentityDto.SourceReference) => complete with { SourceReference = " " },
            nameof(RetainedEvidenceIdentityDto.ReviewStatus) => complete with { ReviewStatus = "Pending" },
            nameof(RetainedEvidenceIdentityDto.ReviewedBy) => complete with { ReviewedBy = " " },
            nameof(RetainedEvidenceIdentityDto.ReviewedAtUtc) => complete with { ReviewedAtUtc = default },
            nameof(RetainedEvidenceIdentityDto.EffectiveDate) => complete with { EffectiveDate = default },
            nameof(RetainedEvidenceIdentityDto.EvidenceVersion) => complete with { EvidenceVersion = 0 },
            nameof(RetainedEvidenceIdentityDto.RetainedAtUtc) => complete with { RetainedAtUtc = default },
            nameof(RetainedEvidenceIdentityDto.RetainedBy) => complete with { RetainedBy = " " },
            nameof(RetainedEvidenceIdentityDto.SubjectType) => complete with { SubjectType = " " },
            nameof(RetainedEvidenceIdentityDto.SubjectId) => complete with { SubjectId = " " },
            _ => throw new ArgumentOutOfRangeException(nameof(fieldName), fieldName, null)
        };
        var store = new InMemoryAccountingProductionCertificationProfileStore();

        var act = () => store.UpsertAsync(ProfileRequest(artifact, [incomplete]));

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*retained evidence is incomplete*");
    }

    [Theory]
    [InlineData("arbitrary-uri")]
    [InlineData("synthesized-uri")]
    [InlineData("legacy-full-token")]
    public async Task CertificationProfileStore_ArbitraryOrSynthesizedEvidenceCannotCertify(string scenario)
    {
        var artifact = WorkflowArtifact([AccountingWorkflowCertificationLaneKindDto.PostingRules]);
        var evidence = CompleteEvidence(
            AccountingProductionCertificationEvidenceSubjectTypes.WorkflowArtifact,
            artifact.CertificationId,
            "workflow");
        var request = scenario switch
        {
            "arbitrary-uri" => ProfileRequest(artifact, [], ["https://example.invalid/claims/production-ready"]),
            "synthesized-uri" => ProfileRequest(artifact, [evidence with
            {
                EvidenceUri = $"evidence://retained-production-profile/{artifact.CertificationId}"
            }]),
            "legacy-full-token" => ProfileRequest(artifact, [evidence with
            {
                EvidenceUri = $"evidence://accounting-production/{artifact.CertificationId}/workflow-certification/full"
            }]),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null)
        };
        var store = new InMemoryAccountingProductionCertificationProfileStore();

        var act = () => store.UpsertAsync(request);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task CertificationProfileStore_CompleteTypedEvidenceAndMatchingControlScope_IsRetained()
    {
        var artifact = WorkflowArtifact([AccountingWorkflowCertificationLaneKindDto.PostingRules]);
        var evidence = CompleteEvidence(
            AccountingProductionCertificationEvidenceSubjectTypes.WorkflowArtifact,
            artifact.CertificationId,
            "workflow");
        var store = new InMemoryAccountingProductionCertificationProfileStore();

        var retained = await store.UpsertAsync(ProfileRequest(artifact, [evidence]));

        retained.RetainedEvidence.Should().ContainSingle().Which.Should().Be(evidence);
        retained.WorkflowCertificationArtifacts.Should().ContainSingle().Which.Should().Be(artifact);
    }

    private static AccountingProductionReadinessRequestDto AllBooleanRequest()
        => new(
            FundProfileId: FundProfileId,
            LedgerBookId: LedgerBookId,
            TenantId: TenantId,
            CompanyId: CompanyId,
            TenantScopeConfigured: true,
            AdminRoleProfileConfigured: true,
            ScopedAccessPoliciesConfigured: true,
            ReportingGroupsConfigured: true,
            AccountingAdminSurfaceConfigured: true,
            BrowserAccountingAdminSurfaceConfigured: true,
            WpfAccountingAdminSurfaceConfigured: true,
            ChartAdministrationStudioConfigured: true,
            RuleTestPromotionStudioConfigured: true,
            CloseSetupStudioConfigured: true,
            ProviderMappingStudioConfigured: true,
            TenantCompanyReportGroupSetupStudioConfigured: true,
            AuditReviewToolingConfigured: true,
            BulkImportExportSafeguardsConfigured: true,
            PerformanceValidationConfigured: true,
            DisasterRecoveryRunbookConfigured: true,
            LedgerBookAdministrationStudioConfigured: true,
            PostingRuleAuthoringStudioConfigured: true,
            ApprovalQueueStudioConfigured: true,
            DimensionMappingStudioConfigured: true,
            ImplementationSandboxConfigured: true,
            PostingRulesLedgerBookNativeCertified: true,
            JournalLifecycleLedgerBookNativeCertified: true,
            CloseReportingLedgerBookNativeCertified: true,
            ClosePlanConfigurationLedgerBookNativeCertified: true,
            ExternalGlLedgerBookNativeCertified: true,
            ReconciliationLedgerBookNativeCertified: true,
            DirectLendingLedgerBookNativeCertified: true,
            StrategyLedgerReadLedgerBookNativeCertified: true,
            PeriodReportDimensionQueriesCertified: true,
            CrossPeriodReportDimensionQueriesCertified: true,
            JournalQueryDimensionFiltersCertified: true,
            ExternalExportDimensionMappingCertified: true,
            LedgerLineDimensionsPersistedCertified: true,
            TrialBalanceDimensionFiltersCertified: true,
            ReportPackageDimensionProvenanceCertified: true);

    private static AccountingWorkflowCertificationArtifactDto WorkflowArtifact(
        IReadOnlyList<AccountingWorkflowCertificationLaneKindDto>? lanes = null)
        => new(
            CertificationId: "workflow-certification-production-v3",
            Status: AccountingCertificationArtifactStatusDto.Certified,
            TenantId: TenantId,
            CompanyId: CompanyId,
            FundProfileId: FundProfileId,
            LedgerBookId: LedgerBookId,
            CertifiedBy: "controller@meridian.local",
            CertifiedAtUtc: DateTimeOffset.Parse("2026-07-21T18:00:00Z"),
            SourceService: "AccountingCertificationRunner",
            Lanes: (lanes ?? Enum.GetValues<AccountingWorkflowCertificationLaneKindDto>())
                .Select(kind => new AccountingWorkflowCertificationLaneDto(
                    kind,
                    AccountingCertificationArtifactLaneStatusDto.Passed))
                .ToArray());

    private static AccountingDimensionalCertificationArtifactDto DimensionalArtifact()
        => new(
            CertificationId: "dimensional-certification-production-v4",
            Status: AccountingCertificationArtifactStatusDto.Certified,
            TenantId: TenantId,
            CompanyId: CompanyId,
            FundProfileId: FundProfileId,
            LedgerBookId: LedgerBookId,
            DimensionScopeEvidenceKey: "canonical-production-v4",
            CertifiedBy: "controller@meridian.local",
            CertifiedAtUtc: DateTimeOffset.Parse("2026-07-21T18:00:00Z"),
            SourceService: "AccountingCertificationRunner",
            Lanes: Enum.GetValues<AccountingDimensionalCertificationLaneKindDto>()
                .Select(kind => new AccountingDimensionalCertificationLaneDto(
                    kind,
                    AccountingCertificationArtifactLaneStatusDto.Passed))
                .ToArray());

    private static AccountingTenantAdminCertificationArtifactDto TenantAdministrationArtifact()
        => new(
            CertificationId: "tenant-administration-certification-production-v2",
            Status: AccountingCertificationArtifactStatusDto.Certified,
            TenantId: TenantId,
            CompanyId: CompanyId,
            FundProfileId: FundProfileId,
            LedgerBookId: LedgerBookId,
            CertifiedBy: "controller@meridian.local",
            CertifiedAtUtc: DateTimeOffset.Parse("2026-07-21T18:00:00Z"),
            SourceService: "AccountingCertificationRunner",
            Lanes: Enum.GetValues<AccountingTenantAdminCertificationLaneKindDto>()
                .Select(kind => new AccountingTenantAdminCertificationLaneDto(
                    kind,
                    AccountingCertificationArtifactLaneStatusDto.Passed))
                .ToArray());

    private static RetainedEvidenceIdentityDto CompleteEvidence(
        string subjectType,
        string certificationId,
        string suffix)
        => new(
            EvidenceId: $"retained-accounting-certification-{suffix}-v3",
            EvidenceUri: $"evidence://accounting-certification/{suffix}/v3",
            ContentHashSha256: new string(suffix[0] is 'd' ? 'b' : suffix[0] is 't' ? 'c' : 'a', 64),
            SourceSystem: "GovernedEvidenceVault",
            SourceReference: $"vault://accounting-certification/{suffix}/v3",
            ReviewStatus: RetainedEvidenceIdentityValidator.AcceptedReviewStatus,
            ReviewedBy: "controller@meridian.local",
            ReviewedAtUtc: DateTimeOffset.Parse("2026-07-21T18:30:00Z"),
            EffectiveDate: new DateOnly(2026, 7, 21),
            EvidenceVersion: 3,
            RetainedAtUtc: DateTimeOffset.Parse("2026-07-21T18:50:00Z"),
            RetainedBy: "evidence-retention@meridian.local",
            SubjectType: subjectType,
            SubjectId: certificationId);

    private static AccountingProductionCertificationProfileUpsertRequestDto ProfileRequest(
        AccountingWorkflowCertificationArtifactDto artifact,
        IReadOnlyList<RetainedEvidenceIdentityDto> retainedEvidence,
        IReadOnlyList<string>? legacyEvidence = null)
        => new(
            new AccountingProductionCertificationProfileDto(
                FundProfileId: FundProfileId,
                LedgerBookId: LedgerBookId,
                PostingRulesLedgerBookNativeCertified: true,
                JournalLifecycleLedgerBookNativeCertified: false,
                CloseReportingLedgerBookNativeCertified: false,
                ExternalGlLedgerBookNativeCertified: false,
                PeriodReportDimensionQueriesCertified: false,
                CrossPeriodReportDimensionQueriesCertified: false,
                JournalQueryDimensionFiltersCertified: false,
                ExternalExportDimensionMappingCertified: false,
                UpdatedAtUtc: DateTimeOffset.Parse("2026-07-21T19:00:00Z"),
                UpdatedBy: "controller@meridian.local",
                EvidenceReferences: legacyEvidence,
                TenantId: TenantId,
                CompanyId: CompanyId,
                WorkflowCertificationArtifacts: [artifact]),
            Actor: "controller@meridian.local",
            RetainedEvidence: retainedEvidence);
}
