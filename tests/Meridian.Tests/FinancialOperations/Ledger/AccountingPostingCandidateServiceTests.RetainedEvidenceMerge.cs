using FluentAssertions;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Ledger;

namespace Meridian.Tests.FinancialOperations.Ledger;

public sealed partial class AccountingPostingCandidateServiceTests
{
    [Fact]
    public async Task BuildCandidateAsync_RetainedEvidenceReplacesTheBareLinkForItsUri()
    {
        // The asset spine links every retained record's URI, and the draft turns each link into a
        // bare reference keyed by that URI. Retained ids are not URIs, so without collapsing the
        // bare reference the posting command carries an incomplete duplicate that the ledger's
        // asset-accounting evidence validator refuses.
        const string SourceUri = "provider://custodian/interest-accruals/2026-05";
        var retained = new RetainedEvidenceIdentityDto(
            "custodian-interest-statement-2026-05",
            SourceUri,
            new string('c', 64),
            "Custodian",
            "statement:custodian-interest:2026-05",
            RetainedEvidenceIdentityValidator.AcceptedReviewStatus,
            "fund-controller",
            DateTimeOffset.Parse("2026-05-31T18:00:00Z"),
            new DateOnly(2026, 5, 31),
            EvidenceVersion: 1,
            DateTimeOffset.Parse("2026-05-31T18:05:00Z"),
            "retention-service",
            AssetAccountingEvidenceSubjects.Event,
            "custodian-interest:2026-05");
        var ledgerBookId = Guid.NewGuid();
        var service = await CreateSeededCandidateServiceAsync(ledgerBookId: ledgerBookId);
        var instrumentId = Guid.NewGuid();
        var sourceEventId = Guid.NewGuid();
        var aggregateId = ledgerBookId;
        var periodId = Guid.NewGuid();

        var result = await service.BuildCandidateAsync(new PostingRuleJournalCandidateRequestDto(
            "fund-alpha",
            "CustodianInterestAccrual",
            125.44m,
            "usd",
            new DateOnly(2026, 5, 31),
            "controller@meridian.local",
            aggregateId,
            periodId,
            DateTimeOffset.Parse("2026-05-31T21:00:00Z"),
            "Accrue custodian interest from retained source event",
            AccountingBasis: AccountingBasisKindDto.Gaap,
            LedgerBookId: ledgerBookId,
            Dimensions: new LedgerDimensionSetDto(
                FundId: "fund-alpha",
                EntityId: "entity-master",
                InstrumentId: instrumentId,
                OrganizationId: "tenant-alpha",
                PortfolioId: "portfolio-income",
                BookId: ledgerBookId.ToString("D"),
                AccountId: "account-custodian-interest",
                CustomerId: "customer-custodian",
                VendorId: "vendor-bny",
                ProjectId: "project-interest-accrual",
                ExternalGlDimensions: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Department"] = "InvestmentOps"
                }),
            CounterpartyId: "custodian-bny",
            InstrumentSymbol: "TBILL",
            CorrelationId: Guid.NewGuid(),
            SourceEventId: sourceEventId,
            PolicyId: "gaap-accrual-v1",
            TreatmentKind: AccountingTreatmentKindDto.Accrual,
            TreasuryContext: new TreasuryLedgerContextDto(
                EffectiveDate: new DateOnly(2026, 5, 31),
                IdempotencyKey: "custodian-interest:fund-alpha:202605",
                FundEventId: "fund-event:fund-alpha:interest-accrual:202605",
                FundEventType: "InterestAccrual",
                CapitalAccountId: "capital-account:fund-alpha:master",
                InvestorId: "investor:fund-alpha:master",
                PaymentIntentId: "payment:fund-alpha:interest-accrual:202605",
                SettlementReference: "settlement:fund-alpha:interest-accrual:202605"),
            EvidenceLinks: [SourceUri])
        {
            RetainedEvidence = [retained]
        });

        var evidence = result.PostingCommand!.Evidence.Where(item => item.Uri == SourceUri).ToArray();
        evidence.Should().ContainSingle("the typed retained record replaces the bare link for the same URI");
        evidence[0].EvidenceId.Should().Be(retained.EvidenceId);
        evidence[0].ContentHash.Should().Be(retained.ContentHashSha256);
        evidence[0].EvidenceVersion.Should().Be(1);
        evidence[0].SubjectType.Should().Be(AssetAccountingEvidenceSubjects.Event);
    }

    [Fact]
    public async Task BuildCandidateWriteAsync_GenericRequestNamingASecurityGetsNoSecurityMasterLineage()
    {
        // Security Master provenance and lineage are stamped only under Asset Accounting Event Spine
        // authority, after the spine server-resolves the record Active at its exact version. A generic
        // candidate that merely names an instrument must not obtain the tags the ledger guard trusts.
        var ledgerBookId = Guid.NewGuid();
        var service = await CreateSeededCandidateServiceAsync(ledgerBookId: ledgerBookId);
        var instrumentId = Guid.NewGuid();
        var sourceEventId = Guid.NewGuid();
        var aggregateId = ledgerBookId;
        var periodId = Guid.NewGuid();

        var result = await service.BuildCandidateWriteAsync(new PostingRuleJournalCandidateRequestDto(
            "fund-alpha",
            "CustodianInterestAccrual",
            125.44m,
            "usd",
            new DateOnly(2026, 5, 31),
            "controller@meridian.local",
            aggregateId,
            periodId,
            DateTimeOffset.Parse("2026-05-31T21:00:00Z"),
            "Accrue custodian interest from retained source event",
            AccountingBasis: AccountingBasisKindDto.Gaap,
            LedgerBookId: ledgerBookId,
            Dimensions: new LedgerDimensionSetDto(
                FundId: "fund-alpha",
                EntityId: "entity-master",
                InstrumentId: instrumentId,
                OrganizationId: "tenant-alpha",
                PortfolioId: "portfolio-income",
                BookId: ledgerBookId.ToString("D"),
                AccountId: "account-custodian-interest",
                CustomerId: "customer-custodian",
                VendorId: "vendor-bny",
                ProjectId: "project-interest-accrual",
                ExternalGlDimensions: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Department"] = "InvestmentOps"
                }),
            CounterpartyId: "custodian-bny",
            InstrumentSymbol: "TBILL",
            CorrelationId: Guid.NewGuid(),
            SourceEventId: sourceEventId,
            PolicyId: "gaap-accrual-v1",
            TreatmentKind: AccountingTreatmentKindDto.Accrual,
            TreasuryContext: new TreasuryLedgerContextDto(
                EffectiveDate: new DateOnly(2026, 5, 31),
                IdempotencyKey: "custodian-interest:fund-alpha:202605",
                FundEventId: "fund-event:fund-alpha:interest-accrual:202605",
                FundEventType: "InterestAccrual",
                CapitalAccountId: "capital-account:fund-alpha:master",
                InvestorId: "investor:fund-alpha:master",
                PaymentIntentId: "payment:fund-alpha:interest-accrual:202605",
                SettlementReference: "settlement:fund-alpha:interest-accrual:202605"),
            EvidenceLinks: ["provider://custodian/interest-accruals/2026-05"]));

        result.Write.Should().NotBeNull();
        var tags = result.Write!.Entry.Metadata.Tags;
        (tags?.ContainsKey("securityMasterProvenance") ?? false).Should().BeFalse();
        (tags?.ContainsKey("securityMasterLineage") ?? false).Should().BeFalse();
    }
}
