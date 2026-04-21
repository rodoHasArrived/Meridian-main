using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Application.Services;
using Meridian.Contracts.SecurityMaster;
using Meridian.Ledger;
using NSubstitute;
using Xunit;

namespace Meridian.Tests.Application.Services;

public sealed class ReportGenerationServiceTests
{
    [Fact]
    public async Task GenerateAsync_WithMissingSecurityMetadata_UsesDeterministicUnclassifiedFallback()
    {
        var securityMaster = Substitute.For<ISecurityMasterQueryService>();
        securityMaster
            .GetByIdentifierAsync(SecurityIdentifierKind.Ticker, "UNKN", null, Arg.Any<CancellationToken>())
            .Returns(new SecurityDetailDto(
                SecurityId: Guid.NewGuid(),
                AssetClass: null!,
                Status: SecurityStatusDto.Active,
                DisplayName: null!,
                Currency: null!,
                CommonTerms: JsonSerializer.SerializeToElement(new { }),
                AssetSpecificTerms: JsonSerializer.SerializeToElement(new { }),
                Identifiers: [],
                Aliases: [],
                Version: 1,
                EffectiveFrom: DateTimeOffset.UtcNow.AddDays(-10),
                EffectiveTo: null));

        var service = new ReportGenerationService(securityMaster);
        var book = BuildFundLedger("fund-report-1", ("UNKN", 125m));

        var report = await service.GenerateAsync(new ReportRequest(
            FundId: "fund-report-1",
            AsOf: new DateTimeOffset(2026, 4, 20, 0, 0, 0, TimeSpan.Zero),
            FundLedger: book));

        report.TrialBalance.Should().ContainSingle();
        var row = report.TrialBalance.Single();
        row.Symbol.Should().Be("UNKN");
        row.AssetClass.Should().BeNull();
        row.DisplayName.Should().BeNull();
        row.Currency.Should().BeNull();

        report.AssetClassSections.Should().ContainSingle();
        report.AssetClassSections[0].AssetClass.Should().Be("Unclassified");
        report.AssetClassSections[0].Rows.Should().ContainSingle(r => r.Symbol == "UNKN");
    }

    [Fact]
    public async Task GenerateAsync_PreservesIdentifierMappedRows_AndNullHandlingAcrossMultipleSymbols()
    {
        var securityMaster = Substitute.For<ISecurityMasterQueryService>();
        securityMaster
            .GetByIdentifierAsync(SecurityIdentifierKind.Ticker, "AAPL", null, Arg.Any<CancellationToken>())
            .Returns(BuildDetail("Equity", "Apple Inc.", "USD"));
        securityMaster
            .GetByIdentifierAsync(SecurityIdentifierKind.Ticker, "CUSIP_037833100", null, Arg.Any<CancellationToken>())
            .Returns(BuildDetail("Equity", "Apple Legacy Line", null));

        var service = new ReportGenerationService(securityMaster);
        var book = BuildFundLedger(
            "fund-report-2",
            ("AAPL", 300m),
            ("CUSIP_037833100", 50m));

        var report = await service.GenerateAsync(new ReportRequest(
            FundId: "fund-report-2",
            AsOf: new DateTimeOffset(2026, 4, 20, 0, 0, 0, TimeSpan.Zero),
            FundLedger: book));

        report.TrialBalance.Should().HaveCount(2);
        report.TrialBalance.Should().Contain(row =>
            row.Symbol == "AAPL" &&
            row.AssetClass == "Equity" &&
            row.DisplayName == "Apple Inc." &&
            row.Currency == "USD");
        report.TrialBalance.Should().Contain(row =>
            row.Symbol == "CUSIP_037833100" &&
            row.AssetClass == "Equity" &&
            row.DisplayName == "Apple Legacy Line" &&
            row.Currency is null);
    }

    private static FundLedgerBook BuildFundLedger(string fundId, params (string Symbol, decimal Balance)[] rows)
    {
        var book = new FundLedgerBook(fundId);
        var asOf = new DateTimeOffset(2026, 4, 20, 12, 0, 0, TimeSpan.Zero);

        foreach (var (symbol, balance) in rows)
        {
            var securityAccount = new LedgerAccount($"Security:{symbol}", LedgerAccountType.Asset, symbol);
            var offsetAccount = new LedgerAccount($"Offset:{symbol}", LedgerAccountType.Equity);
            book.FundLedger.PostLines(asOf, $"seed-{symbol}", [(securityAccount, balance, 0m), (offsetAccount, 0m, balance)]);
        }

        return book;
    }

    private static SecurityDetailDto BuildDetail(string assetClass, string displayName, string? currency)
        => new(
            SecurityId: Guid.NewGuid(),
            AssetClass: assetClass,
            Status: SecurityStatusDto.Active,
            DisplayName: displayName,
            Currency: currency,
            CommonTerms: JsonSerializer.SerializeToElement(new { }),
            AssetSpecificTerms: JsonSerializer.SerializeToElement(new { }),
            Identifiers: [],
            Aliases: [],
            Version: 1,
            EffectiveFrom: DateTimeOffset.UtcNow.AddDays(-5),
            EffectiveTo: null);
}
