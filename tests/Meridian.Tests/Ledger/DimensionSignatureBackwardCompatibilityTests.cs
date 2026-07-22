using System.Reflection;
using FluentAssertions;
using Meridian.Contracts.Ledger;
using Meridian.FinancialOperations.AccountingClose;
using Meridian.FinancialOperations.AccountingSystem;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Meridian.Ui.Shared.Endpoints;
using Meridian.Ui.Shared.Services;
using Xunit;

namespace Meridian.Tests.Ledger;

public sealed class DimensionSignatureBackwardCompatibilityTests
{
    private static readonly Guid InstrumentId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid LedgerBookId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid PositionA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid PositionB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public void LegacyDimensionSignatures_RemainByteForByteStable_WhenPositionIsAbsent()
    {
        var dto = BuildDto();
        var domain = BuildDomain();

        Invoke<string>(typeof(TrialBalanceProjectionService), "DimensionSignature", dto)
            .Should().Be(string.Join("|",
                "FUND-ALPHA", "ENTITY-ALPHA", "SLEEVE-ALPHA", "STRATEGY-ALPHA",
                "INVESTOR-ALPHA", "CAPITAL-ALPHA", InstrumentId.ToString("D"), "LOT-ALPHA",
                "COST-ALPHA", "COUNTERPARTY-ALPHA", "ORG-ALPHA", "PORTFOLIO-ALPHA",
                "BOOK-ALPHA", "ACCOUNT-ALPHA", "CUSTOMER-ALPHA", "VENDOR-ALPHA",
                "PROJECT-ALPHA", "DEPARTMENT=INVESTMENTS"));

        Invoke<string>(typeof(AccountingSystemIntegrationService), "FormatDimensionsForHash", dto)
            .Should().Be(string.Join("\u001e",
                "fund-alpha", "entity-alpha", "sleeve-alpha", "strategy-alpha",
                "investor-alpha", "capital-alpha", InstrumentId.ToString("D"), "lot-alpha",
                "cost-alpha", "counterparty-alpha", "Department=Investments", "org-alpha",
                "portfolio-alpha", "book-alpha", "account-alpha", "customer-alpha",
                "vendor-alpha", "project-alpha"));

        var legacyPipeSignature = string.Join("|",
            "fund-alpha", "entity-alpha", "sleeve-alpha", "strategy-alpha",
            "investor-alpha", "capital-alpha", InstrumentId.ToString("D"), "lot-alpha",
            "cost-alpha", "counterparty-alpha", "org-alpha", "portfolio-alpha",
            "book-alpha", "account-alpha", "customer-alpha", "vendor-alpha",
            "project-alpha", "Department=Investments");

        Invoke<string>(typeof(AutomatedJournalIntakeRunner), "DimensionKey", domain)
            .Should().Be(legacyPipeSignature);
        Invoke<string>(typeof(LedgerEndpoints), "BuildDimensionSignature", dto)
            .Should().Be(legacyPipeSignature);

        Invoke<string>(typeof(PostgresLedgerBookService), "BuildDimensionsKey", dto)
            .Should().Be(string.Join("\u001e",
                "fund-alpha", "entity-alpha", "sleeve-alpha", "strategy-alpha",
                "investor-alpha", "capital-alpha", InstrumentId.ToString("D"), "lot-alpha",
                "cost-alpha", "counterparty-alpha", "org-alpha", "portfolio-alpha",
                "book-alpha", "account-alpha", "customer-alpha", "vendor-alpha",
                "project-alpha", "Department=Investments"));

        Invoke<string>(typeof(PeriodCloseProjector), "DimensionKey", domain)
            .Should().Be(string.Join("|",
                "fund-alpha", "entity-alpha", "sleeve-alpha", "strategy-alpha",
                "investor-alpha", "capital-alpha", InstrumentId.ToString("D"), "lot-alpha",
                "cost-alpha", "counterparty-alpha", "org-alpha", "portfolio-alpha",
                "book-alpha", "account-alpha", "customer-alpha", "vendor-alpha",
                "project-alpha") + "|Department=Investments;");
    }

    [Fact]
    public void PositionDimension_DistinguishesEveryDeterministicSignature()
    {
        var legacyDto = BuildDto();
        var positionDtoA = legacyDto with { PositionId = PositionA };
        var positionDtoB = legacyDto with { PositionId = PositionB };
        var legacyDomain = BuildDomain();
        var positionDomainA = legacyDomain with { PositionId = PositionA };
        var positionDomainB = legacyDomain with { PositionId = PositionB };

        AssertDistinct(typeof(TrialBalanceProjectionService), "DimensionSignature", legacyDto, positionDtoA, positionDtoB);
        AssertDistinct(typeof(AccountingSystemIntegrationService), "FormatDimensionsForHash", legacyDto, positionDtoA, positionDtoB);
        AssertDistinct(typeof(LedgerEndpoints), "BuildDimensionSignature", legacyDto, positionDtoA, positionDtoB);
        AssertDistinct(typeof(PostgresLedgerBookService), "BuildDimensionsKey", legacyDto, positionDtoA, positionDtoB);
        AssertDistinct(typeof(AutomatedJournalIntakeRunner), "DimensionKey", legacyDomain, positionDomainA, positionDomainB);
        AssertDistinct(typeof(PeriodCloseProjector), "DimensionKey", legacyDomain, positionDomainA, positionDomainB);
    }

    [Fact]
    public void ReportHashes_PreserveLegacyBytes_AndDistinguishPositions()
    {
        var legacy = BuildDto();
        var positionA = legacy with { PositionId = PositionA };
        var positionB = legacy with { PositionId = PositionB };

        var legacyScopeHash = Invoke<string>(
            typeof(AccountingReportPackageService),
            "BuildDimensionScopeHash",
            legacy);
        var positionScopeHashA = Invoke<string>(
            typeof(AccountingReportPackageService),
            "BuildDimensionScopeHash",
            positionA);
        var positionScopeHashB = Invoke<string>(
            typeof(AccountingReportPackageService),
            "BuildDimensionScopeHash",
            positionB);

        legacyScopeHash.Should().Be("786e01693897");
        positionScopeHashA.Should().NotBe(legacyScopeHash).And.NotBe(positionScopeHashB);

        var legacyArtifactHash = ComputeArtifactHash(legacy);
        var positionArtifactHashA = ComputeArtifactHash(positionA);
        var positionArtifactHashB = ComputeArtifactHash(positionB);

        legacyArtifactHash.Should().Be("e262e18b27e22800bec140eba98fe49cd38b6a97ac4d5d93af6e8f2de6d50bd9");
        positionArtifactHashA.Should().NotBe(legacyArtifactHash).And.NotBe(positionArtifactHashB);
    }

    private static string ComputeArtifactHash(LedgerDimensionSetDto dimensions)
        => Invoke<string>(
            typeof(AccountingReportPackageService),
            "ComputeArtifactHash",
            "pkg-legacy",
            "financial-statements",
            "Legacy Statement",
            "pdf",
            "2026-03",
            "statement-legacy",
            LedgerBookId,
            dimensions,
            AccountingCertificationStateDto.Draft,
            DateTimeOffset.Parse("2026-04-01T12:00:00Z"),
            new[] { "evidence:b", "evidence:a" });

    private static void AssertDistinct(
        Type declaringType,
        string methodName,
        object legacy,
        object positionA,
        object positionB)
    {
        var legacySignature = Invoke<string>(declaringType, methodName, legacy);
        var positionSignatureA = Invoke<string>(declaringType, methodName, positionA);
        var positionSignatureB = Invoke<string>(declaringType, methodName, positionB);

        positionSignatureA.Should().NotBe(legacySignature).And.NotBe(positionSignatureB);
    }

    private static T Invoke<T>(Type declaringType, string methodName, params object?[] arguments)
    {
        var method = declaringType.GetMethod(
            methodName,
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        method.Should().NotBeNull($"{declaringType.FullName}.{methodName} is a guarded deterministic signature producer");
        return (T)method!.Invoke(null, arguments)!;
    }

    private static LedgerDimensionSetDto BuildDto()
        => new(
            FundId: "fund-alpha",
            EntityId: "entity-alpha",
            SleeveId: "sleeve-alpha",
            StrategyId: "strategy-alpha",
            InvestorId: "investor-alpha",
            CapitalAccountId: "capital-alpha",
            InstrumentId: InstrumentId,
            TaxLotId: "lot-alpha",
            CostCenterId: "cost-alpha",
            CounterpartyId: "counterparty-alpha",
            ExternalGlDimensions: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Department"] = "Investments"
            },
            OrganizationId: "org-alpha",
            PortfolioId: "portfolio-alpha",
            BookId: "book-alpha",
            AccountId: "account-alpha",
            CustomerId: "customer-alpha",
            VendorId: "vendor-alpha",
            ProjectId: "project-alpha");

    private static LedgerLineDimensionSet BuildDomain()
        => new(
            FundId: "fund-alpha",
            EntityId: "entity-alpha",
            SleeveId: "sleeve-alpha",
            StrategyId: "strategy-alpha",
            InvestorId: "investor-alpha",
            CapitalAccountId: "capital-alpha",
            InstrumentId: InstrumentId,
            TaxLotId: "lot-alpha",
            CostCenterId: "cost-alpha",
            CounterpartyId: "counterparty-alpha",
            ExternalGlDimensions: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Department"] = "Investments"
            },
            OrganizationId: "org-alpha",
            PortfolioId: "portfolio-alpha",
            BookId: "book-alpha",
            AccountId: "account-alpha",
            CustomerId: "customer-alpha",
            VendorId: "vendor-alpha",
            ProjectId: "project-alpha");
}
