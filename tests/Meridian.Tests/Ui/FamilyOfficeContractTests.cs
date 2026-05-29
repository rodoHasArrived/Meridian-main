using System.Text.Json;
using Meridian.Ui.Shared.Contracts;
using Meridian.Ui.Shared.Serialization;
using Xunit;

namespace Meridian.Tests.Ui;

public sealed class FamilyOfficeContractTests
{
    [Fact]
    public void FamilyOfficeJsonContext_ShouldSerializeOverviewWithFinancialEvidenceMetadata()
    {
        var overview = new FamilyOfficeOverviewDto(
            FamilyOfficeId: "sfo-001",
            DisplayName: "Meridian Family Office",
            BaseCurrency: "USD",
            AsOfDate: new DateOnly(2026, 5, 29),
            BalanceSheet: new FamilyBalanceSheetDto(
                FamilyOfficeId: "sfo-001",
                BaseCurrency: "USD",
                TotalAssets: 125_000_000m,
                TotalLiabilities: 5_000_000m,
                NetWorth: 120_000_000m,
                LiquidAssets: 30_000_000m,
                MarketableSecurities: 60_000_000m,
                PrivateAssets: 35_000_000m,
                RealAssets: 10_000_000m,
                CashAndEquivalents: 8_000_000m,
                UnfundedCommitments: 12_500_000m,
                SourceSystem: "family-ledger",
                SourceDocumentId: "packet-2026-05",
                AsOfDate: new DateOnly(2026, 5, 29),
                ValuationDate: new DateOnly(2026, 5, 28),
                EvidenceCompleteness: "Complete",
                ReconciliationStatus: "Matched",
                LastReviewedBy: "controller@example.com",
                LastReviewedAtUtc: DateTimeOffset.Parse("2026-05-29T15:00:00Z")),
            Entities:
            [
                new FamilyEntityDto(
                    EntityId: "entity-trust",
                    DisplayName: "Trust A",
                    EntityType: "Trust",
                    Jurisdiction: "DE",
                    TaxResidency: "US",
                    BaseCurrency: "USD",
                    ParentEntityId: null,
                    OwnershipPercent: null,
                    IsOperatingEntity: false,
                    EvidenceLinks: [])
            ],
            OwnershipGraph: new FamilyOwnershipGraphDto(
                AsOfDate: new DateOnly(2026, 5, 29),
                Nodes: [new FamilyOwnershipNodeDto("entity-trust", "Trust A", "Trust", "entity-trust", "DE", "USD")],
                Edges: [],
                EvidenceLinks: []),
            Accounts:
            [
                new FamilyAccountSummaryDto(
                    AccountId: "acct-1",
                    EntityId: "entity-trust",
                    DisplayName: "Main Custody",
                    AccountType: "Custody",
                    Custodian: "Example Custodian",
                    ProviderAccountId: "EXT-1",
                    Currency: "USD",
                    CashBalance: 8_000_000m,
                    MarketValue: 60_000_000m,
                    AccruedIncome: 125_000m,
                    TotalEquity: 68_125_000m,
                    SourceSystem: "custodian-feed",
                    SourceDocumentId: "statement-1",
                    AsOfDate: new DateOnly(2026, 5, 29),
                    ValuationDate: new DateOnly(2026, 5, 29),
                    EvidenceCompleteness: "Complete",
                    ReconciliationStatus: "Matched",
                    LastReviewedBy: "ops@example.com",
                    LastReviewedAtUtc: DateTimeOffset.Parse("2026-05-29T14:30:00Z"))
            ],
            PublicAssets: [],
            PrivateAssets: [],
            CapitalCommitments: [],
            RecentCapitalActivity: [],
            Readiness: new FamilyOfficeReadinessDto(
                Status: "Ready",
                EvidenceCompleteness: "Complete",
                ReconciliationStatus: "Matched",
                OpenExceptionCount: 0,
                ItemsNeedingReviewCount: 0,
                GeneratedAtUtc: DateTimeOffset.Parse("2026-05-29T15:05:00Z"),
                LastReviewedBy: "controller@example.com",
                LastReviewedAtUtc: DateTimeOffset.Parse("2026-05-29T15:00:00Z"),
                Blockers: [],
                EvidenceLinks: []),
            EvidenceLinks:
            [
                new FamilyOfficeEvidenceLinkDto(
                    EvidenceId: "evidence-1",
                    SourceSystem: "family-ledger",
                    SourceDocumentId: "packet-2026-05",
                    Label: "May close packet",
                    EvidenceType: "ClosePacket",
                    Route: "/workstation/reporting/evidence/evidence-1",
                    CapturedAtUtc: DateTimeOffset.Parse("2026-05-29T14:00:00Z"),
                    AsOfDate: new DateOnly(2026, 5, 29),
                    ValuationDate: new DateOnly(2026, 5, 28),
                    CompletenessStatus: "Complete")
            ]);

        var json = JsonSerializer.Serialize(overview, FamilyOfficeJsonContext.Default.FamilyOfficeOverviewDto);
        var roundTripped = JsonSerializer.Deserialize(json, FamilyOfficeJsonContext.Default.FamilyOfficeOverviewDto);

        Assert.Contains("\"sourceSystem\":\"family-ledger\"", json, StringComparison.Ordinal);
        Assert.Contains("\"evidenceCompleteness\":\"Complete\"", json, StringComparison.Ordinal);
        Assert.Contains("\"reconciliationStatus\":\"Matched\"", json, StringComparison.Ordinal);
        Assert.NotNull(roundTripped);
        Assert.Equal("sfo-001", roundTripped!.FamilyOfficeId);
        Assert.Equal("family-ledger", roundTripped.BalanceSheet.SourceSystem);
        Assert.Equal("packet-2026-05", roundTripped.BalanceSheet.SourceDocumentId);
        Assert.Equal(new DateOnly(2026, 5, 28), roundTripped.BalanceSheet.ValuationDate);
        Assert.Equal("controller@example.com", roundTripped.BalanceSheet.LastReviewedBy);
        Assert.Single(roundTripped.Accounts);
        Assert.Equal("custodian-feed", roundTripped.Accounts[0].SourceSystem);
    }
}
