using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
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
            "SM_OPTION_MULTIPLIER_INVALID");

        report.Issues
            .Where(static issue => issue.Code.StartsWith("SM_OPTION_", StringComparison.Ordinal))
            .Should()
            .OnlyContain(static issue =>
                issue.Severity == SecurityValidationSeverityDto.Error
                && issue.AffectedFields.Count > 0
                && !string.IsNullOrWhiteSpace(issue.SuggestedAction));
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

    [Fact]
    public async Task Scenario_CancelledValidation_StopsBeforeStoreRead()
    {
        var store = Substitute.For<ISecurityMasterStore>();
        var service = new SecurityValidationService(store, AssetClassValidatorRegistry.CreateDefault());
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => service.ValidateSecurityAsync(Guid.NewGuid(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        store.DidNotReceive().LoadAllAsync(Arg.Any<CancellationToken>());
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
}
