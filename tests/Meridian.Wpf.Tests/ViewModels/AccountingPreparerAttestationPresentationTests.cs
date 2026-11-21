using Meridian.Contracts.Workstation;
using Meridian.Wpf.Workstation.Models;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed class AccountingPreparerAttestationPresentationTests
{
    private static readonly Guid Journal = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Line = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly LedgerAmountScopeDto ReportScope = new("tenant-a", "company-a", "fund-a",
        Guid.Parse("33333333-3333-4333-8333-333333333333"), Guid.Parse("44444444-4444-4444-8444-444444444444"));

    [Fact]
    public void RetainedReportContributorPreservesOriginalPeriodAndPreparerClassification()
    {
        var proof = Proof();
        var originalScope = proof.AccountingPreparerAttestations![0].SourceScope;

        var details = AccountingPreparerAttestationPresentation.Describe(proof, out var summary, [Journal]);

        details.Should().ContainSingle().Which.Should().Contain(SinglePrincipalAttestationPolicy.Disclosure);
        summary.Should().Contain("Ordinary recorded approval is not satisfied").And.Contain("do not supply Verified Coverage");
        proof.AccountingPreparerAttestations[0].SourceScope.Should().Be(originalScope).And.NotBe(proof.Scope);
        proof.Status.Should().Be(EvidenceStatusDto.Ready);
    }

    [Fact]
    public void ReportPrefixAloneCannotAuthorizeCrossPeriodOrSamePeriodClassification()
    {
        var proof = Proof();
        AccountingPreparerAttestationPresentation.Describe(proof, out _).Single().Should().Contain("unavailable");
        var samePeriod = proof with
        {
            AccountingPreparerAttestations = [proof.AccountingPreparerAttestations![0] with { SourceScope = proof.Scope }]
        };
        AccountingPreparerAttestationPresentation.Describe(samePeriod, out _).Single().Should().Contain("unavailable");
    }

    [Theory]
    [InlineData("tenant")]
    [InlineData("company")]
    [InlineData("fund")]
    [InlineData("book")]
    [InlineData("empty-period")]
    [InlineData("unproven-period")]
    [InlineData("journal")]
    [InlineData("empty-journals")]
    [InlineData("evidence-period")]
    [InlineData("malformed-line")]
    [InlineData("empty-line")]
    [InlineData("side")]
    [InlineData("ledger-record")]
    [InlineData("missing-evidence")]
    public void ReportClassificationRequiresExactContributorScopeAndJournalEvidence(string defect)
    {
        var proof = Proof();
        var decision = proof.AccountingPreparerAttestations![0];
        var source = decision.SourceScope;
        decision = defect switch
        {
            "tenant" => decision with { SourceScope = source with { TenantId = "foreign" } },
            "company" => decision with { SourceScope = source with { CompanyId = "foreign" } },
            "fund" => decision with { SourceScope = source with { FundProfileId = "foreign" } },
            "book" => decision with { SourceScope = source with { LedgerBookId = Guid.NewGuid() } },
            "empty-period" => decision with { SourceScope = source with { PeriodId = Guid.Empty } },
            "unproven-period" => decision with { SourceScope = source with { PeriodId = Guid.NewGuid() } },
            "journal" => decision with { JournalEntryIds = [Guid.NewGuid()] },
            "empty-journals" => decision with { JournalEntryIds = [] },
            _ => decision
        };
        var evidence = proof.Evidence[0];
        evidence = defect switch
        {
            "evidence-period" => evidence with { SourceScope = ReportScope },
            "malformed-line" => evidence with { SourceSubjectId = $"{Journal:D}:not-a-line:debit" },
            "empty-line" => evidence with { SourceSubjectId = $"{Journal:D}:{Guid.Empty:D}:debit" },
            "side" => evidence with { SourceSubjectId = $"{Journal:D}:{Line:D}:total" },
            "ledger-record" => evidence with { Kind = "ledger-record" },
            _ => evidence
        };
        proof = proof with { AccountingPreparerAttestations = [decision], Evidence = defect == "missing-evidence" ? [] : [evidence] };

        AccountingPreparerAttestationPresentation.Describe(proof, out var summary, [Journal])
            .Single().Should().Contain("unavailable");
        summary.Should().Contain("Ordinary recorded approval is not satisfied");
    }

    [Fact]
    public void ContributorJournalMustBelongToTheRetainedSelection()
    {
        AccountingPreparerAttestationPresentation.Describe(Proof(), out _, [Guid.NewGuid()])
            .Single().Should().Contain("unavailable");
    }

    [Fact]
    public void PostedAmountStillRequiresItsExactPeriodAndCannotUseReportContext()
    {
        var proof = Proof() with { SubjectId = $"{Journal:D}:{Line:D}:debit" };
        AccountingPreparerAttestationPresentation.Describe(proof, out _).Single().Should().Contain("unavailable");
        AccountingPreparerAttestationPresentation.Describe(proof, out _, [Journal]).Single().Should().Contain("unavailable");
        var exact = proof with
        {
            AccountingPreparerAttestations = [proof.AccountingPreparerAttestations![0] with { SourceScope = proof.Scope }]
        };
        AccountingPreparerAttestationPresentation.Describe(exact, out _).Single().Should().Contain(SinglePrincipalAttestationPolicy.Disclosure);
    }

    private static LedgerAmountProofDto Proof()
    {
        var source = ReportScope with { PeriodId = Guid.Parse("55555555-5555-4555-8555-555555555555") };
        var decision = new AccountingPreparerAttestationDisclosureDto(1, LedgerAmountApprovalClassification.PreparerAttestation,
            SinglePrincipalAttestationPolicy.Disclosure, source, [Journal], []) { Actor = "principal-a" };
        var evidence = new LedgerAmountProofEvidenceDto("source-a", "retained-manifest", "Original-period source", null,
            "ledger", DateTimeOffset.Parse("2026-11-12T12:00:00Z"), EvidenceStatusDto.Ready)
        { SourceScope = source, SourceSubjectId = $"{Journal:D}:{Line:D}:debit" };
        return new("report:balance-a", ReportScope, 100m, "USD", EvidenceStatusDto.Ready, [evidence], [])
        { AccountingPreparerAttestations = [decision], OrdinaryRecordedApprovalSatisfied = false };
    }
}
