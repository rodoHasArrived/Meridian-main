using FluentAssertions;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Ledger;

namespace Meridian.Tests.AssetOperations;

public sealed class AssetAccountingEvidenceSubjectContractTests
{
    private static readonly Guid EventId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid BookId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid PeriodId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly string DraftedFingerprint = new('a', 64);

    [Fact]
    public void PostingApprovalSubjectId_BindsExactEventVersionPeriodBasisApprovalAndDraft()
    {
        var baseline = Subject();

        Subject(eventVersion: 2).Should().NotBe(baseline);
        Subject(fundProfileId: "fund-beta").Should().NotBe(baseline);
        Subject(ledgerBookId: Guid.NewGuid()).Should().NotBe(baseline);
        Subject(periodId: Guid.NewGuid()).Should().NotBe(baseline);
        Subject(accountingBasis: AccountingBasisKindDto.Tax).Should().NotBe(baseline);
        Subject(approvalId: "approval-2").Should().NotBe(baseline);
        Subject(draftedCandidateFingerprint: new string('b', 64)).Should().NotBe(baseline);
        Subject(tenantId: "tenant-beta").Should().NotBe(baseline);
        Subject(companyId: "company-beta").Should().NotBe(baseline);
        AssetAccountingEvidenceSubjects.LegacyPostingApprovalSubjectId(
                EventId,
                "fund-alpha",
                BookId,
                "tenant-alpha",
                "company-alpha")
            .Should().NotBe(baseline);
    }

    [Fact]
    public void PostingApprovalSubjectId_InvalidDraftFingerprint_FailsClosed()
    {
        var act = () => Subject(draftedCandidateFingerprint: "not-a-sha256");

        act.Should().Throw<ArgumentException>()
            .WithMessage("*drafted candidate fingerprint*");
    }

    [Fact]
    public void Validate_CorrectionReusingEventIdAcrossVersions_FailsClosed()
    {
        var effectiveDate = new DateOnly(2026, 6, 30);
        var occurredAtUtc = DateTimeOffset.Parse("2026-06-30T18:00:00Z");
        var securityId = Guid.NewGuid();
        var positionId = Guid.NewGuid();
        var economicEvent = new EconomicEventReferenceDto(
            EventId,
            AssetAccountingEventTypeNames.For(AssetAccountingEventKindDto.Valuation),
            2,
            effectiveDate,
            occurredAtUtc,
            "AssetOperations")
        {
            SecurityId = securityId,
            BookPositionId = positionId
        };
        var lineage = new ProjectionLineageDto(
            Guid.NewGuid(),
            null,
            "valuation",
            "v1",
            "engine-v1",
            "base",
            effectiveDate,
            occurredAtUtc.AddMinutes(1),
            "AssetOperations",
            null,
            economicEvent)
        {
            BookPositionId = positionId
        };
        var spine = new AssetAccountingEventSpineDto(
            EventId,
            AssetAccountingEventKindDto.Valuation,
            2,
            1,
            effectiveDate,
            100m,
            "USD",
            new AssetAccountingEventScopeDto(
                securityId,
                1,
                positionId,
                1,
                BookId,
                PeriodId,
                AccountingBasisKindDto.Gaap,
                "fund-alpha"),
            economicEvent,
            lineage,
            Correction: new AssetAccountingCorrectionReferenceDto(
                EventId,
                1,
                Guid.NewGuid(),
                BookId,
                PeriodId,
                AccountingBasisKindDto.Gaap));

        AssetAccountingEventSpineValidator.Validate(spine)
            .Should().Contain(issue => issue.Contains("different event id", StringComparison.OrdinalIgnoreCase));
    }

    private static string Subject(
        long eventVersion = 1,
        string fundProfileId = "fund-alpha",
        Guid? ledgerBookId = null,
        Guid? periodId = null,
        AccountingBasisKindDto accountingBasis = AccountingBasisKindDto.Gaap,
        string approvalId = "approval-1",
        string? draftedCandidateFingerprint = null,
        string tenantId = "tenant-alpha",
        string companyId = "company-alpha")
        => AssetAccountingEvidenceSubjects.PostingApprovalSubjectId(
            EventId,
            eventVersion,
            fundProfileId,
            ledgerBookId ?? BookId,
            periodId ?? PeriodId,
            accountingBasis,
            approvalId,
            draftedCandidateFingerprint ?? DraftedFingerprint,
            tenantId,
            companyId);
}
