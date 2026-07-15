using FluentAssertions;
using Meridian.Contracts.Ledger;
using Meridian.FinancialOperations.AccountingClose;
using Meridian.Ui.Shared.Services;
using NSubstitute;

namespace Meridian.Tests.Ui;

public sealed class AutomatedJournalCapitalAccountReconciliationResolverTests
{
    private static readonly Guid BookId = Guid.Parse("ad197f30-e086-4b23-a3e6-e96d56011627");
    private static readonly DateTimeOffset EvaluatedAt = new(2026, 8, 1, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ResolveAsync_CertifiedExactScopePackages_DerivesNavCapitalAndHighWaterHistory()
    {
        var packages = Substitute.For<IAccountingReportPackageService>();
        var may = Package("2026-05", nav: 1_050_000m, openingCapital: 900_000m, endingCapital: 1_050_000m, recordedAt: EvaluatedAt.AddMonths(-2));
        var june = Package("2026-06", nav: 1_000_000m, openingCapital: 1_050_000m, endingCapital: 1_000_000m, recordedAt: EvaluatedAt.AddMonths(-1));
        var july = Package("2026-07", nav: 1_100_000m, openingCapital: 1_000_000m, endingCapital: 1_100_000m, recordedAt: EvaluatedAt.AddHours(-1));
        packages.ListPackagesAsync(
                "fund-alpha", "2026-07", BookId, Arg.Any<LedgerDimensionSetDto?>(),
                "tenant-alpha", "company-alpha", Arg.Any<CancellationToken>())
            .Returns([july]);
        packages.ListPackagesAsync(
                "fund-alpha", null, BookId, Arg.Any<LedgerDimensionSetDto?>(),
                "tenant-alpha", "company-alpha", Arg.Any<CancellationToken>())
            .Returns([july, june, may]);
        var resolver = new AccountingReportPackageCapitalAccountReconciliationResolver(packages);

        var result = await resolver.ResolveAsync(Scope());

        result.Should().NotBeNull();
        result!.IsReconciled.Should().BeTrue();
        result.ReconciledBeginningNav.Should().Be(1_000_000m);
        result.ReconciledEndingNavBeforeFees.Should().Be(1_100_000m);
        result.ReconciledHighWaterMark.Should().Be(1_050_000m);
        result.CapitalAccountOpeningBalance.Should().Be(1_000_000m);
        result.CapitalAccountEndingBalanceBeforeFees.Should().Be(1_100_000m);
        result.ReviewedBy.Should().Be("fund-controller");
        result.ConfidenceScore.Should().Be(1m);
        result.EvidenceLinks.Should().NotBeEmpty();
        result.SourceVersion.Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [Fact]
    public async Task ResolveAsync_DraftOrWrongEntityPackage_FailsClosed()
    {
        var packages = Substitute.For<IAccountingReportPackageService>();
        var draft = Package(
            "2026-07",
            1_100_000m,
            1_000_000m,
            1_100_000m,
            EvaluatedAt.AddHours(-1),
            AccountingCertificationStateDto.Draft,
            entityId: "entity-other");
        packages.ListPackagesAsync(
                "fund-alpha", "2026-07", BookId, Arg.Any<LedgerDimensionSetDto?>(),
                "tenant-alpha", "company-alpha", Arg.Any<CancellationToken>())
            .Returns([draft]);
        var resolver = new AccountingReportPackageCapitalAccountReconciliationResolver(packages);

        var result = await resolver.ResolveAsync(Scope());

        result.Should().BeNull();
        await packages.DidNotReceive().ListPackagesAsync(
            "fund-alpha", null, BookId, Arg.Any<LedgerDimensionSetDto?>(),
            "tenant-alpha", "company-alpha", Arg.Any<CancellationToken>());
    }

    private static AutomatedJournalCapitalAccountReconciliationScope Scope()
        => new(
            "tenant-alpha",
            "company-alpha",
            "fund-alpha",
            BookId,
            "entity-alpha",
            "2026-07",
            "USD",
            EvaluatedAt);

    private static AccountingReportPackageBundleDto Package(
        string periodId,
        decimal nav,
        decimal openingCapital,
        decimal endingCapital,
        DateTimeOffset recordedAt,
        AccountingCertificationStateDto state = AccountingCertificationStateDto.Certified,
        string entityId = "entity-alpha")
    {
        var packageId = $"package-{periodId}";
        var dimensions = new LedgerDimensionSetDto(
            FundId: "fund-alpha",
            EntityId: entityId,
            BookId: BookId.ToString("D"));
        var evidence = new[] { $"evidence://accounting-report-package/{packageId}/certification" };
        var certification = new ReportCertificationDto(
            $"certification-{periodId}",
            state,
            "fund-controller",
            recordedAt,
            "Certified fund NAV and investor capital statements.",
            evidence);
        var financialStatements = new FinancialStatementPackageDto(
            packageId,
            "fund-alpha",
            BookId,
            periodId,
            state,
            [$"statement-{periodId}"],
            evidence,
            certification,
            Dimensions: dimensions);
        var investorStatement = new InvestorCapitalStatementDto(
            $"investor-statement-{periodId}",
            "fund-alpha",
            BookId,
            "capital-account-alpha",
            "investor-alpha",
            periodId,
            dimensions,
            openingCapital,
            Contributions: endingCapital - openingCapital,
            Distributions: 0m,
            RealizedGainLoss: 0m,
            EndingCapital: endingCapital,
            Currency: "USD",
            CertificationState: state,
            EvidenceLinks: evidence);
        var realized = new RealizedGainLossReportDto(
            $"realized-{periodId}",
            "fund-alpha",
            BookId,
            periodId,
            dimensions,
            0m,
            "USD",
            state,
            evidence);
        var navPackage = new NavPackageDto(
            $"nav-{periodId}",
            "fund-alpha",
            BookId,
            periodId,
            dimensions,
            nav,
            "USD",
            state,
            evidence,
            certification);
        return new AccountingReportPackageBundleDto(
            financialStatements,
            [investorStatement],
            realized,
            navPackage,
            certification,
            TenantId: "tenant-alpha",
            CompanyId: "company-alpha");
    }
}
