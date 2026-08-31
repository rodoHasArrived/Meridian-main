using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.ReferenceData.SecurityMaster;
using Meridian.Storage;
using Meridian.Storage.SecurityMaster;
using NSubstitute;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// Scenario tests for Security Master validation failures that would otherwise flow into
/// run inputs, lots, ledger postings, reconciliation breaks, or report-pack evidence.
/// </summary>
public sealed class SecurityValidationServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 5, 19, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Scenario_DuplicateCanonicalIdentifier_ProducesCriticalIssueWithEvidenceLink()
    {
        var securityA = Guid.NewGuid();
        var securityB = Guid.NewGuid();
        var duplicateIsin = "US0378331005";

        var first = CreateProjection(
            securityA,
            "Equity",
            [CreateIdentifier(SecurityIdentifierKind.Isin, duplicateIsin, isPrimary: true)]);
        var second = CreateProjection(
            securityB,
            "Equity",
            [CreateIdentifier(SecurityIdentifierKind.Isin, duplicateIsin, isPrimary: true)]);

        var store = Substitute.For<ISecurityMasterStore>();
        store.LoadAllAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<SecurityProjectionRecord>>([first, second]));

        var service = new SecurityValidationService(store, AssetClassValidatorRegistry.CreateDefault());

        var report = await service.ValidateSecurityAsync(securityA);

        report.SecurityId.Should().Be(securityA);
        report.HasBlockingIssues.Should().BeTrue();
        report.CriticalIssueCount.Should().Be(1);

        var issue = report.Issues.Should()
            .ContainSingle(static item => item.Code == "SM_DUPLICATE_CANONICAL_IDENTIFIER")
            .Which;
        issue.Severity.Should().Be(SecurityValidationSeverityDto.Critical);
        issue.Title.Should().Contain("Canonical identifier");
        issue.AffectedFields.Should().Contain("identifiers.Isin");
        issue.SuggestedAction.Should().NotBeNullOrWhiteSpace();
        issue.EvidenceLinks.Should().ContainSingle(link =>
            link.EvidenceKind == "SecurityMasterRecord"
            && link.EvidenceId == securityB.ToString()
            && link.Route == $"/api/security-master/{securityB}");
    }

    [Fact]
    public void Scenario_DuplicateFigiAcrossRecords_ProducesCriticalCollisionIssue()
    {
        var securityA = Guid.NewGuid();
        var securityB = Guid.NewGuid();
        const string duplicateFigi = "BBG000B9XRY4"; // Valid FIGI check digit shared by two securities.

        var first = CreateProjection(
            securityA,
            "Equity",
            [CreateIdentifier(SecurityIdentifierKind.Figi, duplicateFigi, isPrimary: true)]);
        var second = CreateProjection(
            securityB,
            "Equity",
            [CreateIdentifier(SecurityIdentifierKind.Figi, duplicateFigi, isPrimary: true)]);

        var service = new SecurityValidationService(
            Substitute.For<ISecurityMasterStore>(),
            AssetClassValidatorRegistry.CreateDefault());

        var report = service.ValidateRecord(first, [first, second], Now);

        var issue = report.Issues.Should()
            .ContainSingle(static item => item.Code == "SM_DUPLICATE_CANONICAL_IDENTIFIER")
            .Which;
        issue.Severity.Should().Be(SecurityValidationSeverityDto.Critical);
        issue.AffectedFields.Should().Contain("identifiers.Figi");
        issue.EvidenceLinks.Should().Contain(link => link.EvidenceId == securityB.ToString());
    }

    [Fact]
    public void Scenario_SameCanonicalValueWithDifferentProvidersOnOneRecord_IsFlaggedAsDuplicate()
    {
        // A canonical identifier (ISIN) is provider-independent, so repeating the same value with
        // a different provider annotation on one record must still trip in-record duplicate
        // detection — provider only distinguishes ProviderSymbol identities.
        var record = CreateProjection(
            Guid.NewGuid(),
            "Equity",
            [
                CreateIdentifier(SecurityIdentifierKind.Isin, "US0378331005", isPrimary: true),
                CreateIdentifier(SecurityIdentifierKind.Isin, "US0378331005", isPrimary: false, provider: "Bloomberg")
            ]);
        var service = new SecurityValidationService(
            Substitute.For<ISecurityMasterStore>(),
            AssetClassValidatorRegistry.CreateDefault());

        var report = service.ValidateRecord(record, [record], Now);

        report.Issues.Should().Contain(issue =>
            issue.Code == "SM_IDENTIFIER_DUPLICATE_ACTIVE"
            && issue.Severity == SecurityValidationSeverityDto.Error);
    }

    [Fact]
    public void Scenario_InvalidLeiCheckDigit_ProducesStructuredIdentifierFormatIssue()
    {
        var record = CreateProjection(
            Guid.NewGuid(),
            "Equity",
            [
                CreateIdentifier(SecurityIdentifierKind.Isin, "US0378331005", isPrimary: true),
                CreateIdentifier(SecurityIdentifierKind.Lei, "HWUPKR0MPOU8FGXBT395", isPrimary: false)
            ]);
        var service = new SecurityValidationService(
            Substitute.For<ISecurityMasterStore>(),
            AssetClassValidatorRegistry.CreateDefault());

        var report = service.ValidateRecord(record, [record], Now);

        report.Issues.Should().Contain(issue =>
            issue.Code == "SM_IDENTIFIER_FORMAT_INVALID"
            && issue.Severity == SecurityValidationSeverityDto.Error
            && issue.Message.Contains("Lei", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Scenario_InvalidOptionTerms_ProducesStructuredAssetClassIssues()
    {
        var securityId = Guid.NewGuid();
        var record = CreateProjection(
            securityId,
            "Option",
            [CreateIdentifier(SecurityIdentifierKind.ProviderSymbol, "AAPL260620C00100000", isPrimary: true, provider: "OPRA")],
            assetSpecificTerms: JsonSerializer.SerializeToElement(new
            {
                putCall = "Sideways",
                strike = 0m,
                expiry = "not-a-date",
                multiplier = 0m,
                valuationProfile = new { pricingSource = "OPRA" },
                accountingClassification = "DerivativeAsset"
            }));
        var service = new SecurityValidationService(
            Substitute.For<ISecurityMasterStore>(),
            AssetClassValidatorRegistry.CreateDefault());

        var report = service.ValidateRecord(record, [record], Now);

        report.SecurityId.Should().Be(securityId);
        report.HasBlockingIssues.Should().BeTrue();
        report.ErrorIssueCount.Should().BeGreaterThanOrEqualTo(5);

        report.Issues.Select(static issue => issue.Code).Should().Contain(
            "SM_OPTION_UNDERLYING_REQUIRED",
            "SM_OPTION_PUT_CALL_INVALID",
            "SM_OPTION_STRIKE_INVALID",
            "SM_OPTION_EXPIRY_REQUIRED",
            "SM_OPTION_MULTIPLIER_INVALID",
            "__VACUITY_PROBE__");

        report.Issues
            .Where(static issue => issue.Code.StartsWith("SM_OPTION_", StringComparison.Ordinal))
            .Should()
            .OnlyContain(static issue =>
                issue.Severity == SecurityValidationSeverityDto.Error
                && issue.AffectedFields.Count > 0
                && !string.IsNullOrWhiteSpace(issue.SuggestedAction));
    }

    [Fact]
    public void Scenario_OptionTermsHiddenInProfileFields_BlockValidationReadiness()
    {
        var record = CreateProjection(
            Guid.NewGuid(),
            "Option",
            [CreateIdentifier(SecurityIdentifierKind.ProviderSymbol, "AAPL260620C00100000", isPrimary: true, provider: "OPRA")],
            assetSpecificTerms: JsonSerializer.SerializeToElement(new
            {
                profileFields = new
                {
                    underlyingId = Guid.NewGuid(),
                    putCall = "Call",
                    strike = 100m,
                    expiry = "2026-06-20",
                    multiplier = 100m
                },
                valuationProfile = new { pricingSource = "OPRA" },
                accountingClassification = "DerivativeAsset"
            }));
        var service = new SecurityValidationService(
            Substitute.For<ISecurityMasterStore>(),
            AssetClassValidatorRegistry.CreateDefault());

        var report = service.ValidateRecord(record, [record], Now);

        report.HasBlockingIssues.Should().BeTrue();
        report.Issues.Select(static issue => issue.Code).Should().Contain(
            "SM_OPTION_UNDERLYING_REQUIRED",
            "SM_OPTION_PUT_CALL_INVALID",
            "SM_OPTION_STRIKE_INVALID",
            "SM_OPTION_EXPIRY_REQUIRED",
            "SM_OPTION_MULTIPLIER_INVALID");
    }

    [Fact]
    public void Scenario_UnsupportedAssetClass_ProducesActionableRegistryIssue()
    {
        var record = CreateProjection(
            Guid.NewGuid(),
            "PrivatePlacement",
            [CreateIdentifier(SecurityIdentifierKind.InternalCode, "PRIVATE-001", isPrimary: true)]);
        var service = new SecurityValidationService(
            Substitute.For<ISecurityMasterStore>(),
            AssetClassValidatorRegistry.CreateDefault());

        var report = service.ValidateRecord(record, [record], Now);

        var issue = report.Issues.Should()
            .ContainSingle(static item => item.Code == "SM_ASSET_CLASS_UNSUPPORTED")
            .Which;
        issue.Severity.Should().Be(SecurityValidationSeverityDto.Error);
        issue.Message.Should().Contain("PrivatePlacement");
        issue.SuggestedAction.Should().Contain("Register an asset-class validator");
    }

    [Theory]
    [InlineData("Cmo")]
    [InlineData("Clo")]
    [InlineData("MortgageBacked")]
    [InlineData("AssetBacked")]
    [InlineData("InterestOnly")]
    public void Scenario_BondWithSecuritizedSubclass_IsRejectedAsNonCanonical(string subclass)
    {
        // ADR-022: StructuredCredit is the one canonical home for securitized products; a Bond
        // classified into a securitized subclass is a label cash-flow/amortization math cannot act
        // on and a second modeling route the partition cannot tolerate.
        var record = CreateProjection(
            Guid.NewGuid(),
            "Bond",
            [CreateIdentifier(SecurityIdentifierKind.Cusip, "38259P508", isPrimary: true)],
            assetSpecificTerms: JsonSerializer.SerializeToElement(new
            {
                maturity = "2049-06-25",
                couponType = "Floating",
                floatingIndex = "SOFR",
                isCallable = false,
                subclass,
                valuationProfile = new { pricingSource = "TestMarks" },
                accountingClassification = "TradingAsset"
            }));
        var service = new SecurityValidationService(
            Substitute.For<ISecurityMasterStore>(),
            AssetClassValidatorRegistry.CreateDefault());

        var report = service.ValidateRecord(record, [record], Now);

        var issue = report.Issues.Should()
            .ContainSingle(static item => item.Code == "SM_BOND_SECURITIZED_SUBCLASS_NONCANONICAL")
            .Which;
        issue.Severity.Should().Be(SecurityValidationSeverityDto.Error);
        issue.Message.Should().Contain(subclass);
        issue.SuggestedAction.Should().Contain("StructuredCredit");
    }

    [Fact]
    public void Scenario_ConventionalBondSubclasses_AreNotFlaggedByCanonicalHomeRule()
    {
        var record = CreateProjection(
            Guid.NewGuid(),
            "Bond",
            [CreateIdentifier(SecurityIdentifierKind.Cusip, "037833100", isPrimary: true)],
            assetSpecificTerms: JsonSerializer.SerializeToElement(new
            {
                maturity = "2031-06-30",
                couponType = "Fixed",
                couponRate = 4.25m,
                isCallable = false,
                subclass = "Corporate",
                valuationProfile = new { pricingSource = "TestMarks" },
                accountingClassification = "TradingAsset"
            }));
        var service = new SecurityValidationService(
            Substitute.For<ISecurityMasterStore>(),
            AssetClassValidatorRegistry.CreateDefault());

        var report = service.ValidateRecord(record, [record], Now);

        report.Issues.Should().NotContain(static issue =>
            issue.Code == "SM_BOND_SECURITIZED_SUBCLASS_NONCANONICAL");
    }

    [Fact]
    public void Scenario_StableNavInvestmentFund_IsSteeredTowardMoneyMarketFund()
    {
        // ADR-022: MoneyMarketFund is the canonical home for stable-NAV vehicles. Warning severity:
        // the InvestmentFundTerms contract documents the flag, so records are steered, not blocked.
        var record = CreateProjection(
            Guid.NewGuid(),
            "InvestmentFund",
            [CreateIdentifier(SecurityIdentifierKind.Ticker, "GOVXX", isPrimary: true)],
            assetSpecificTerms: JsonSerializer.SerializeToElement(new
            {
                fundType = "MutualFund",
                isStableNav = true,
                valuationProfile = new { pricingSource = "iMoneyNet" },
                accountingClassification = "CashEquivalent"
            }));
        var service = new SecurityValidationService(
            Substitute.For<ISecurityMasterStore>(),
            AssetClassValidatorRegistry.CreateDefault());

        var report = service.ValidateRecord(record, [record], Now);

        var issue = report.Issues.Should()
            .ContainSingle(static item => item.Code == "SM_INVESTMENT_FUND_STABLE_NAV_NONCANONICAL")
            .Which;
        issue.Severity.Should().Be(SecurityValidationSeverityDto.Warning);
        issue.SuggestedAction.Should().Contain("MoneyMarketFund");
        // Adding the InvestmentFund validator also closes the class's registry gap.
        report.Issues.Should().NotContain(static item => item.Code == "SM_ASSET_CLASS_UNSUPPORTED");
    }

    [Fact]
    public void Scenario_CustomAssetWithSecuritizedCategory_IsSteeredTowardStructuredCredit()
    {
        var record = CreateProjection(
            Guid.NewGuid(),
            "CustomAsset",
            [CreateIdentifier(SecurityIdentifierKind.InternalCode, "CLO-2026-A", isPrimary: true)],
            assetSpecificTerms: JsonSerializer.SerializeToElement(new
            {
                category = "CLO",
                valuationProfile = new { pricingSource = "Dealer" },
                accountingClassification = "TradingAsset"
            }));
        var service = new SecurityValidationService(
            Substitute.For<ISecurityMasterStore>(),
            AssetClassValidatorRegistry.CreateDefault());

        var report = service.ValidateRecord(record, [record], Now);

        var issue = report.Issues.Should()
            .ContainSingle(static item => item.Code == "SM_CUSTOM_ASSET_SECURITIZED_NONCANONICAL")
            .Which;
        issue.Severity.Should().Be(SecurityValidationSeverityDto.Warning);
        issue.SuggestedAction.Should().Contain("StructuredCredit");
    }

    [Fact]
    public async Task Scenario_CancelledValidation_StopsBeforeStoreRead()
    {
        var store = Substitute.For<ISecurityMasterStore>();
        var service = new SecurityValidationService(store, AssetClassValidatorRegistry.CreateDefault());
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => service.ValidateSecurityAsync(Guid.NewGuid(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        _ = store.DidNotReceive().LoadAllAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Scenario_PendingOperatorOverride_ProducesApprovalIssue()
    {
        var securityId = Guid.NewGuid();
        var record = CreateProjection(
            securityId,
            "Equity",
            [CreateIdentifier(SecurityIdentifierKind.Ticker, "AAPL", isPrimary: true)]);
        var store = Substitute.For<ISecurityMasterStore>();
        store.LoadAllAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<SecurityProjectionRecord>>([record]));
        var overridesStore = Substitute.For<IOperatorOverridesStore>();
        overridesStore.GetAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(new OperatorOverridesDto(
                securityId,
                new Dictionary<string, string> { ["sector"] = "Technology" },
                "operator",
                Now)
            {
                ApprovalStatus = SecurityOverrideApprovalStatusDto.Pending,
                ReasonCode = "CLASSIFICATION_CORRECTION"
            });
        var service = new SecurityValidationService(
            store,
            AssetClassValidatorRegistry.CreateDefault(),
            overridesStore);

        var report = await service.ValidateSecurityAsync(securityId);

        report.HasBlockingIssues.Should().BeTrue();
        report.Issues.Should().Contain(issue =>
            issue.Code == "SM_OVERRIDE_APPROVAL_REQUIRED"
            && issue.Severity == SecurityValidationSeverityDto.Error
            && issue.EvidenceLinks.Any(link => link.EvidenceKind == "SecurityOperatorOverride"));
        report.Issues.Should().Contain(issue =>
            issue.Code == "SM_OVERRIDE_AUDIT_TRAIL_MISSING"
            && issue.Severity == SecurityValidationSeverityDto.Warning);
        var auditTrailIssue = report.Issues.Single(issue => issue.Code == "SM_OVERRIDE_AUDIT_TRAIL_MISSING");
        auditTrailIssue.SuggestedAction.Should().Contain("operator review");
        auditTrailIssue.SuggestedAction.Should().NotContain("governance review");
    }

    [Fact]
    public async Task Scenario_FileSnapshotStore_AppendsImmutableValidationSnapshot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"meridian-security-validation-{Guid.NewGuid():N}");
        try
        {
            var store = new FileSecurityValidationSnapshotStore(new StorageOptions { RootPath = root });
            var report = new SecurityValidationReportDto(
                SecurityId: Guid.Parse("33333333-3333-3333-3333-333333333333"),
                Scope: "Security",
                EvaluatedAtUtc: Now,
                HasBlockingIssues: true,
                CriticalIssueCount: 0,
                ErrorIssueCount: 1,
                Issues:
                [
                    SecurityValidationIssueFactoryForTests.Issue(
                        "SM_IDENTIFIER_MISSING",
                        "Active identifier is missing")
                ]);

            var snapshot = await store.RecordAsync(
                report,
                new SecurityValidationSnapshotRequestDto(
                    SecurityValidationWorkflowDto.ReportPackEvidence,
                    "report-1",
                    "reviewer",
                    "Governance report-pack evidence gate.",
                    []));

            snapshot.SnapshotId.Should().NotBe(Guid.Empty);
            snapshot.ReportHashSha256.Should().HaveLength(64);
            var file = Directory.GetFiles(
                Path.Combine(root, "governance", "security-master", "validation-snapshots"),
                "*.jsonl").Should().ContainSingle().Which;
            var line = File.ReadLines(file).Should().ContainSingle().Which;
            line.Should().Contain("SM_IDENTIFIER_MISSING");
            line.Should().Contain("report-1");
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
    public void Scenario_InvalidCusip_ProducesStructuredIdentifierFormatIssue()
    {
        var record = CreateProjection(
            Guid.NewGuid(),
            "Equity",
            [CreateIdentifier(SecurityIdentifierKind.Cusip, "03783310X", isPrimary: true)]);
        var service = new SecurityValidationService(
            Substitute.For<ISecurityMasterStore>(),
            AssetClassValidatorRegistry.CreateDefault());

        var report = service.ValidateRecord(record, [record], Now);

        report.Issues.Should().Contain(issue =>
            issue.Code == "SM_IDENTIFIER_FORMAT_INVALID"
            && issue.Severity == SecurityValidationSeverityDto.Error
            && issue.Message.Contains("CUSIP", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Scenario_InvalidOccOptionSymbol_ProducesStructuredIdentifierFormatIssue()
    {
        var record = CreateProjection(
            Guid.NewGuid(),
            "Option",
            [CreateIdentifier(SecurityIdentifierKind.OccOptionSymbol, "AAPL240621X00150000", isPrimary: true)]);
        var service = new SecurityValidationService(
            Substitute.For<ISecurityMasterStore>(),
            AssetClassValidatorRegistry.CreateDefault());

        var report = service.ValidateRecord(record, [record], Now);

        report.Issues.Should().Contain(issue =>
            issue.Code == "SM_IDENTIFIER_FORMAT_INVALID"
            && issue.Severity == SecurityValidationSeverityDto.Error
            && issue.Message.Contains("OccOptionSymbol", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Scenario_IdentifierEffectiveWindowEndsBeforeItBegins_ProducesDateRangeIssue()
    {
        var validFrom = new DateTimeOffset(2026, 5, 20, 0, 0, 0, TimeSpan.Zero);
        var record = CreateProjection(
            Guid.NewGuid(),
            "Bond",
            [
                new SecurityIdentifierDto(
                    SecurityIdentifierKind.Cusip,
                    "037833100",
                    true,
                    validFrom,
                    validFrom.AddDays(-1))
            ]);
        var service = new SecurityValidationService(
            Substitute.For<ISecurityMasterStore>(),
            AssetClassValidatorRegistry.CreateDefault());

        var report = service.ValidateRecord(record, [record], Now);

        report.Issues.Should().Contain(issue =>
            issue.Code == "SM_IDENTIFIER_EFFECTIVE_WINDOW_INVALID"
            && issue.Severity == SecurityValidationSeverityDto.Error
            && issue.Message.Contains("expires on or before", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Scenario_OccOptionSymbolOnBond_ProducesAssetClassMismatchWarning()
    {
        var record = CreateProjection(
            Guid.NewGuid(),
            "Bond",
            [CreateIdentifier(SecurityIdentifierKind.OccOptionSymbol, "AAPL240621C00150000", isPrimary: true)]);
        var service = new SecurityValidationService(
            Substitute.For<ISecurityMasterStore>(),
            AssetClassValidatorRegistry.CreateDefault());

        var report = service.ValidateRecord(record, [record], Now);

        report.Issues.Should().Contain(issue =>
            issue.Code == "SM_OCC_SYMBOL_ASSET_CLASS_MISMATCH"
            && issue.Severity == SecurityValidationSeverityDto.Warning);
    }

    [Fact]
    public void Scenario_NonCanonicalIsinFormatting_ProducesNormalizationWarning()
    {
        var record = CreateProjection(
            Guid.NewGuid(),
            "Equity",
            [CreateIdentifier(SecurityIdentifierKind.Isin, "us-0378331005", isPrimary: true)]);
        var service = new SecurityValidationService(
            Substitute.For<ISecurityMasterStore>(),
            AssetClassValidatorRegistry.CreateDefault());

        var report = service.ValidateRecord(record, [record], Now);

        report.Issues.Should().Contain(issue =>
            issue.Code == "SM_IDENTIFIER_NORMALIZATION_RECOMMENDED"
            && issue.Severity == SecurityValidationSeverityDto.Warning
            && issue.Message.Contains("US0378331005", StringComparison.Ordinal));
    }

    [Fact]
    public void Scenario_MissingAssetSpecificSchemaVersion_ProducesCompatibilityWarning()
    {
        var record = CreateProjection(
            Guid.NewGuid(),
            "Equity",
            [CreateIdentifier(SecurityIdentifierKind.Ticker, "AAPL", isPrimary: true)],
            assetSpecificTerms: JsonSerializer.SerializeToElement(new
            {
                classification = "Common",
                accountingClassification = "TradingAsset"
            }));
        var service = new SecurityValidationService(
            Substitute.For<ISecurityMasterStore>(),
            AssetClassValidatorRegistry.CreateDefault());

        var report = service.ValidateRecord(record, [record], Now);

        report.Issues.Should().Contain(issue =>
            issue.Code == "SM_ASSET_SPECIFIC_SCHEMA_VERSION_MISSING"
            && issue.Severity == SecurityValidationSeverityDto.Warning
            && issue.AffectedFields.Contains("assetSpecificTerms.schemaVersion"));
    }

    [Fact]
    public void Scenario_UnsupportedAssetSpecificSchemaVersion_ProducesCompatibilityError()
    {
        var record = CreateProjection(
            Guid.NewGuid(),
            "Bond",
            [CreateIdentifier(SecurityIdentifierKind.Cusip, "037833100", isPrimary: true)],
            assetSpecificTerms: JsonSerializer.SerializeToElement(new
            {
                schemaVersion = 7,
                maturity = "2030-01-01",
                accountingClassification = "TradingAsset"
            }));
        var service = new SecurityValidationService(
            Substitute.For<ISecurityMasterStore>(),
            AssetClassValidatorRegistry.CreateDefault());

        var report = service.ValidateRecord(record, [record], Now);

        report.Issues.Should().Contain(issue =>
            issue.Code == "SM_ASSET_SPECIFIC_SCHEMA_VERSION_UNSUPPORTED"
            && issue.Severity == SecurityValidationSeverityDto.Error
            && issue.Message.Contains("schemaVersion '7'", StringComparison.Ordinal));
    }

    [Fact]
    public void Scenario_PrimaryIdentifierProjectionMismatch_ProducesActionableError()
    {
        var activePrimaryIdentifier = CreateIdentifier(SecurityIdentifierKind.Isin, "US0378331005", isPrimary: true);
        var record = CreateProjection(
            Guid.NewGuid(),
            "Equity",
            [activePrimaryIdentifier]) with
        {
            PrimaryIdentifierKind = SecurityIdentifierKind.Ticker.ToString(),
            PrimaryIdentifierValue = "AAPL"
        };
        var service = new SecurityValidationService(
            Substitute.For<ISecurityMasterStore>(),
            AssetClassValidatorRegistry.CreateDefault());

        var report = service.ValidateRecord(record, [record], Now);

        report.Issues.Should().Contain(issue =>
            issue.Code == "SM_PRIMARY_IDENTIFIER_PROJECTION_MISMATCH"
            && issue.Severity == SecurityValidationSeverityDto.Error
            && issue.AffectedFields.Contains("primaryIdentifierKind")
            && issue.AffectedFields.Contains("primaryIdentifierValue"));
    }

    [Fact]
    public void Scenario_SeededCustomAssetProfiles_AreApprovedAndVersioned()
    {
        var catalog = StaticSecurityAssetProfileCatalog.CreateDefault();

        var profiles = catalog.GetProfiles();

        profiles.Should().HaveCount(5);
        profiles.Should().OnlyContain(static profile =>
            profile.Version == 1
            && profile.Status == SecurityAssetProfileStatusDto.Approved
            && profile.Fields.All(field => !string.IsNullOrWhiteSpace(field.Key)));
        profiles.Select(static profile => profile.ProfileId).Should().Contain(
            "structured-credit-io-po",
            "real-estate-holding",
            "private-fund-interest",
            "private-company-equity",
            "co-invest-spv",
            "__VACUITY_PROBE__");
    }

    [Fact]
    public void Scenario_ProfileBackedCustomAsset_WithApprovedPinnedVersion_PassesCustomProfileValidation()
    {
        var record = CreateProjection(
            Guid.NewGuid(),
            "CustomAsset",
            [CreateIdentifier(SecurityIdentifierKind.InternalCode, "PFI-001", isPrimary: true)],
            assetSpecificTerms: CreatePrivateFundProfileTerms(includeNavDate: true));
        var service = new SecurityValidationService(
            Substitute.For<ISecurityMasterStore>(),
            AssetClassValidatorRegistry.CreateDefault());

        var report = service.ValidateRecord(record, [record], Now);

        report.Issues.Should().NotContain(static issue => issue.Code.StartsWith("SM_CUSTOM_PROFILE_", StringComparison.Ordinal));
        report.Issues.Should().NotContain(static issue => issue.Code == "SM_ASSET_SPECIFIC_SCHEMA_VERSION_UNSUPPORTED");
    }

    [Fact]
    public void Scenario_ProfileBackedCustomAsset_MissingRequiredFieldAndIdentifierCoverage_BlocksReadiness()
    {
        var record = CreateProjection(
            Guid.NewGuid(),
            "CustomAsset",
            [CreateIdentifier(SecurityIdentifierKind.ProviderSymbol, "PFI-001", isPrimary: true, provider: "manual")],
            assetSpecificTerms: CreatePrivateFundProfileTerms(includeNavDate: false));
        var service = new SecurityValidationService(
            Substitute.For<ISecurityMasterStore>(),
            AssetClassValidatorRegistry.CreateDefault());

        var report = service.ValidateRecord(record, [record], Now);

        report.HasBlockingIssues.Should().BeTrue();
        report.Issues.Should().Contain(issue =>
            issue.Code == "SM_CUSTOM_PROFILE_FIELD_REQUIRED"
            && issue.AffectedFields.Contains("assetSpecificTerms.profileFields.navDate"));
        report.Issues.Should().Contain(issue =>
            issue.Code == "SM_CUSTOM_PROFILE_IDENTIFIER_COVERAGE_MISSING"
            && issue.AffectedFields.Contains("identifiers.InternalCode"));
    }

    [Fact]
    public void Scenario_StructuredCreditProfile_InvalidFactorRange_ProducesNoCodeRangeIssue()
    {
        var record = CreateProjection(
            Guid.NewGuid(),
            "CustomAsset",
            [CreateIdentifier(SecurityIdentifierKind.Cusip, "037833100", isPrimary: true)],
            assetSpecificTerms: JsonSerializer.SerializeToElement(new
            {
                schemaVersion = SecurityMasterSchemaVersions.CustomAssetProfileTerms,
                customProfileId = "structured-credit-io-po",
                profileVersion = 1,
                category = "StructuredCredit",
                valuationProfile = new { pricingSource = "Trustee" },
                accountingClassification = "StructuredCredit",
                profileApproval = new
                {
                    approvedBy = "risk-committee",
                    approvedAtUtc = Now,
                    approvalReference = "PROFILE-APPROVAL-1"
                },
                profileFields = new
                {
                    tranche = "IO-A",
                    poolId = "POOL-1",
                    currentFactor = 1.25m,
                    originalFace = 1000000m,
                    couponOrIndex = "WAC",
                    factorSchedule = "monthly-trustee",
                    collateralType = "MBS"
                }
            }));
        var service = new SecurityValidationService(
            Substitute.For<ISecurityMasterStore>(),
            AssetClassValidatorRegistry.CreateDefault());

        var report = service.ValidateRecord(record, [record], Now);

        report.Issues.Should().Contain(issue =>
            issue.Code == "SM_CUSTOM_PROFILE_FIELD_RANGE_INVALID"
            && issue.AffectedFields.Contains("assetSpecificTerms.profileFields.currentFactor"));
    }

    [Fact]
    public void Scenario_StructuredCreditDirectTerms_MissingCollateralType_BlocksReadiness()
    {
        var record = CreateProjection(
            Guid.NewGuid(),
            "StructuredCredit",
            [CreateIdentifier(SecurityIdentifierKind.Cusip, "037833100", isPrimary: true)],
            assetSpecificTerms: JsonSerializer.SerializeToElement(new
            {
                schemaVersion = SecurityMasterSchemaVersions.LegacyAssetSpecificTerms,
                tranche = "A-1",
                originalFace = 1000000m,
                currentFactor = 0.98m,
                couponOrIndex = "SOFR+250"
            }));
        var service = new SecurityValidationService(
            Substitute.For<ISecurityMasterStore>(),
            AssetClassValidatorRegistry.CreateDefault());

        var report = service.ValidateRecord(record, [record], Now);

        report.HasBlockingIssues.Should().BeTrue();
        report.Issues.Should().Contain(issue =>
            issue.Code == "SM_STRUCTURED_CREDIT_COLLATERAL_REQUIRED"
            && issue.AffectedFields.Contains("assetSpecificTerms.collateralType"));
    }

    [Fact]
    public void Scenario_ProfileBackedPrivateFundInterest_AsFirstClass_UsesProfileFieldsForValidation()
    {
        var record = CreateProjection(
            Guid.NewGuid(),
            "PrivateFundInterest",
            [CreateIdentifier(SecurityIdentifierKind.InternalCode, "PFI-001", isPrimary: true)],
            assetSpecificTerms: CreatePrivateFundProfileTerms(includeNavDate: true));
        var service = new SecurityValidationService(
            Substitute.For<ISecurityMasterStore>(),
            AssetClassValidatorRegistry.CreateDefault());

        var report = service.ValidateRecord(record, [record], Now);

        report.Issues.Should().NotContain(static issue => issue.Code.StartsWith("SM_PRIVATE_FUND_", StringComparison.Ordinal));
        report.Issues.Should().NotContain(static issue => issue.Code == "SM_ASSET_SPECIFIC_SCHEMA_VERSION_UNSUPPORTED");
    }

    [Fact]
    public void Scenario_ProfileBackedOtherSecurity_MissingApprovalMetadata_BlocksGovernedUse()
    {
        var record = CreateProjection(
            Guid.NewGuid(),
            "OtherSecurity",
            [CreateIdentifier(SecurityIdentifierKind.InternalCode, "SPV-001", isPrimary: true)],
            assetSpecificTerms: JsonSerializer.SerializeToElement(new
            {
                schemaVersion = SecurityMasterSchemaVersions.CustomAssetProfileTerms,
                category = "PrivateFunds",
                customProfileId = "co-invest-spv",
                profileVersion = 1,
                valuationProfile = new { pricingSource = "SponsorReports" },
                accountingClassification = "PrivateInvestment",
                profileFields = new
                {
                    vehicle = "SPV I",
                    underlyingCompanyOrSecurity = "ExampleCo",
                    sponsor = "GP Capital",
                    commitment = 100000m,
                    economics = "80/20 carry",
                    reportingCadence = "Quarterly"
                }
            }));
        var service = new SecurityValidationService(
            Substitute.For<ISecurityMasterStore>(),
            AssetClassValidatorRegistry.CreateDefault());

        var report = service.ValidateRecord(record, [record], Now);

        report.Issues.Should().Contain(issue =>
            issue.Code == "SM_CUSTOM_PROFILE_APPROVAL_METADATA_REQUIRED"
            && issue.Severity == SecurityValidationSeverityDto.Error);
    }

    private static SecurityProjectionRecord CreateProjection(
        Guid securityId,
        string assetClass,
        IReadOnlyList<SecurityIdentifierDto> identifiers,
        JsonElement? assetSpecificTerms = null)
        => new(
            SecurityId: securityId,
            AssetClass: assetClass,
            Status: SecurityStatusDto.Active,
            DisplayName: $"{assetClass} Test Security",
            Currency: "USD",
            PrimaryIdentifierKind: identifiers.First(static identifier => identifier.IsPrimary).Kind.ToString(),
            PrimaryIdentifierValue: identifiers.First(static identifier => identifier.IsPrimary).Value,
            CommonTerms: JsonSerializer.SerializeToElement(new
            {
                displayName = $"{assetClass} Test Security",
                currency = "USD",
                pricingSource = "TestMarks",
                accountingClassification = "TradingAsset"
            }),
            AssetSpecificTerms: assetSpecificTerms ?? JsonSerializer.SerializeToElement(new
            {
                schemaVersion = SecurityMasterSchemaVersions.LegacyAssetSpecificTerms,
                classification = "Common",
                valuationProfile = new { pricingSource = "TestMarks" },
                accountingClassification = "TradingAsset"
            }),
            Provenance: JsonSerializer.SerializeToElement(new
            {
                sourceSystem = "test",
                updatedBy = "security-validation-tests",
                asOf = Now
            }),
            Version: 1,
            EffectiveFrom: Now.AddDays(-1),
            EffectiveTo: null,
            Identifiers: identifiers,
            Aliases: Array.Empty<SecurityAliasDto>());

    private static SecurityIdentifierDto CreateIdentifier(
        SecurityIdentifierKind kind,
        string value,
        bool isPrimary,
        string? provider = null)
        => new(
            kind,
            value,
            isPrimary,
            Now.AddDays(-1),
            ValidTo: null,
            Provider: provider);

    private static JsonElement CreatePrivateFundProfileTerms(bool includeNavDate)
        => includeNavDate
            ? JsonSerializer.SerializeToElement(new
            {
                schemaVersion = SecurityMasterSchemaVersions.CustomAssetProfileTerms,
                customProfileId = "private-fund-interest",
                profileVersion = 1,
                category = "PrivateFunds",
                valuationProfile = new { pricingSource = "AdministratorNAV" },
                accountingClassification = "PrivateInvestment",
                profileApproval = new
                {
                    approvedBy = "risk-committee",
                    approvedAtUtc = Now,
                    approvalReference = "PROFILE-APPROVAL-1"
                },
                profileFields = new
                {
                    gpSponsor = "GP Capital",
                    strategy = "Private Credit",
                    vintage = 2025,
                    commitment = 1000000m,
                    fundedAmount = 250000m,
                    unfundedAmount = 750000m,
                    navDate = "2026-04-30",
                    lockup = "3 years"
                }
            })
            : JsonSerializer.SerializeToElement(new
            {
                schemaVersion = SecurityMasterSchemaVersions.CustomAssetProfileTerms,
                customProfileId = "private-fund-interest",
                profileVersion = 1,
                category = "PrivateFunds",
                valuationProfile = new { pricingSource = "AdministratorNAV" },
                accountingClassification = "PrivateInvestment",
                profileApproval = new
                {
                    approvedBy = "risk-committee",
                    approvedAtUtc = Now,
                    approvalReference = "PROFILE-APPROVAL-1"
                },
                profileFields = new
                {
                    gpSponsor = "GP Capital",
                    strategy = "Private Credit",
                    vintage = 2025,
                    commitment = 1000000m,
                    fundedAmount = 250000m,
                    unfundedAmount = 750000m,
                    lockup = "3 years"
                }
            });

    private static class SecurityValidationIssueFactoryForTests
    {
        public static SecurityValidationIssueDto Issue(string code, string title)
            => new(
                SecurityValidationSeverityDto.Error,
                code,
                title,
                $"{title}.",
                ["identifiers"],
                "Correct the Security Master record.",
                []);
    }
}
