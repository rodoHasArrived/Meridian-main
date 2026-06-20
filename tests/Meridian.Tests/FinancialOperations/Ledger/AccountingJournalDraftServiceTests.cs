using FluentAssertions;
using Meridian.Contracts.Ledger;
using Meridian.FinancialOperations.Ledger;
using Meridian.Ledger;

namespace Meridian.Tests.FinancialOperations.Ledger;

/// <summary>
/// Guards the accounting-close scenario where retained source evidence must produce a balanced,
/// approval-gated journal draft before any ledger posting can occur.
/// </summary>
public sealed class AccountingJournalDraftServiceTests
{
    [Fact]
    public async Task Scenario_AccountingClose_SourceBackedAccrualDraft_ReturnsGovernedWrite()
    {
        var policyService = new AccountingPolicyService();
        await policyService.CreatePolicyAsync(new CreateAccountingPolicyRequest(
            AccountingBasisKindDto.Gaap,
            PolicyId: "gaap-accrual-v1",
            Version: "v1",
            DisplayName: "GAAP accrual treatment",
            EffectiveFrom: new DateOnly(2026, 1, 1),
            RulePack: new AccountingPolicyRulePackDto(
                "gaap-accrual-rules",
                "v1",
                [
                    new AccountingPolicyRuleDto(
                        "accrual.interest-income",
                        AccountingTreatmentKindDto.Accrual,
                        RuleVersion: "v1",
                        SourceEventType: "CustodianInterestAccrual",
                        RequiresEvidence: true,
                        RequiresApproval: true,
                        AllowsAutoPosting: false,
                        Description: "Accrue custodian interest income from retained source evidence.")
                ])));
        var service = CreateService(policyService);
        var aggregateId = Guid.NewGuid();
        var periodId = Guid.NewGuid();
        var sourceEventId = Guid.NewGuid();

        var result = await service.BuildDraftAsync(new AccountingJournalDraftRequest(
            aggregateId,
            periodId,
            DateTimeOffset.Parse("2026-05-31T21:00:00Z"),
            "Accrue custodian interest for May close",
            [
                new AccountingJournalDraftLineRequest(
                    new LedgerAccount("Accrued Interest Receivable", LedgerAccountType.Asset),
                    Debit: 125.44m,
                    Credit: 0m),
                new AccountingJournalDraftLineRequest(
                    new LedgerAccount("Interest Income", LedgerAccountType.Revenue),
                    Debit: 0m,
                    Credit: 125.44m)
            ],
            AccountingBasis: AccountingBasisKindDto.Gaap,
            EffectiveDate: new DateOnly(2026, 5, 31),
            SourceEventId: sourceEventId,
            FundProfileId: "fund-alpha",
            PolicyId: "gaap-accrual-v1",
            TreatmentKind: AccountingTreatmentKindDto.Accrual,
            SourceEventType: "CustodianInterestAccrual",
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

        result.Policy.PolicyId.Should().Be("gaap-accrual-v1");
        result.Policy.RulePack!.Rules.Should().ContainSingle(rule => rule.RuleId == "accrual.interest-income");
        result.Rule!.RuleId.Should().Be("accrual.interest-income");
        result.IsBalanced.Should().BeTrue();
        result.HasCriticalIssues.Should().BeFalse();
        result.CanSubmitForApproval.Should().BeTrue();
        result.CanPostWithoutAdditionalApproval.Should().BeFalse();
        result.Write.Should().NotBeNull();
        result.Write!.AggregateId.Should().Be(aggregateId);
        result.Write.PeriodId.Should().Be(periodId);
        result.Write.AccountingBasis.Should().Be(AccountingBasisKindDto.Gaap);
        result.Write.AccountingPolicyId.Should().Be("gaap-accrual-v1");
        result.Write.RuleId.Should().Be("accrual.interest-income");
        result.Write.RuleVersion.Should().Be("v1");
        result.Write.SourceEventId.Should().Be(sourceEventId);
        result.Write.Entry.Metadata.EffectiveDate.Should().Be(new DateOnly(2026, 5, 31));
        result.Write.Entry.Metadata.IdempotencyKey.Should().Be("custodian-interest:fund-alpha:202605");
        result.Write.Entry.Metadata.FundEventId.Should().Be("fund-event:fund-alpha:interest-accrual:202605");
        result.Write.Entry.Metadata.CapitalAccountId.Should().Be("capital-account:fund-alpha:master");
        result.Write.Entry.Metadata.PaymentIntentId.Should().Be("payment:fund-alpha:interest-accrual:202605");
        result.Write.Entry.Metadata.EvidenceReferences.Should().ContainSingle(evidence =>
            evidence.Uri == "provider://custodian/interest-accruals/2026-05" &&
            evidence.Kind == AccountingPostingEvidenceKindDto.Source.ToString());
        result.Write.PostingCommand.Should().NotBeNull();
        result.Write.PostingCommand!.AggregateId.Should().Be(aggregateId);
        result.Write.PostingCommand.PeriodId.Should().Be(periodId);
        result.Write.PostingCommand.SourceEventId.Should().Be(sourceEventId);
        result.Write.PostingCommand.IdempotencyKey.Should().Be("custodian-interest:fund-alpha:202605");
        result.Write.PostingCommand.EffectiveDate.Should().Be(new DateOnly(2026, 5, 31));
        result.Write.PostingCommand.ApprovalState.Should().Be(AccountingPostingApprovalStateDto.Pending);
        result.Write.PostingCommand.Evidence.Should().ContainSingle(evidence =>
            evidence.Uri == "provider://custodian/interest-accruals/2026-05" &&
            evidence.Kind == AccountingPostingEvidenceKindDto.Source);
        result.ValidationIssues.Should().Contain(issue => issue.Code == "JOURNAL_DRAFT_APPROVAL_REQUIRED");
        result.EvidenceLinks.Should().ContainSingle("provider://custodian/interest-accruals/2026-05");
    }

    [Fact]
    public async Task Scenario_AccountingClose_UnbalancedDraft_BlocksLedgerWrite()
    {
        var service = CreateService(new AccountingPolicyService());

        var result = await service.BuildDraftAsync(new AccountingJournalDraftRequest(
            Guid.NewGuid(),
            Guid.NewGuid(),
            DateTimeOffset.Parse("2026-05-31T21:00:00Z"),
            "Draft interest adjustment",
            [
                new AccountingJournalDraftLineRequest(
                    new LedgerAccount("Cash", LedgerAccountType.Asset),
                    Debit: 100m,
                    Credit: 0m),
                new AccountingJournalDraftLineRequest(
                    new LedgerAccount("Interest Income", LedgerAccountType.Revenue),
                    Debit: 0m,
                    Credit: 90m)
            ],
            EvidenceLinks: ["provider://custodian/interest-adjustment/2026-05"]));

        result.IsBalanced.Should().BeFalse();
        result.HasCriticalIssues.Should().BeTrue();
        result.Write.Should().BeNull();
        result.CanSubmitForApproval.Should().BeFalse();
        result.ValidationIssues.Should().Contain(issue =>
            issue.Code == "JOURNAL_DRAFT_UNBALANCED" &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);
    }

    [Fact]
    public async Task CreatePolicyAsync_TypedRulePack_NormalizesAndPreservesRules()
    {
        var service = new AccountingPolicyService();

        var policy = await service.CreatePolicyAsync(new CreateAccountingPolicyRequest(
            AccountingBasisKindDto.Primary,
            PolicyId: "intercompany-v1",
            Version: "v2",
            DisplayName: "Intercompany treatment",
            EffectiveFrom: new DateOnly(2026, 6, 1),
            RulePack: new AccountingPolicyRulePackDto(
                " intercompany.rules ",
                " v2 ",
                [
                    new AccountingPolicyRuleDto(
                        " intercompany.due-to-from ",
                        AccountingTreatmentKindDto.Intercompany,
                        RuleVersion: "",
                        SourceEventType: " IntercompanyTransfer ",
                        Tags: [" intercompany ", "elimination", "intercompany"])
                ],
                Description: " Intercompany draft routing ")));

        policy.RulePack.Should().NotBeNull();
        policy.RulePack!.RulePackId.Should().Be("intercompany.rules");
        policy.RulePack.RulePackVersion.Should().Be("v2");
        policy.RulePack.Description.Should().Be("Intercompany draft routing");
        policy.RulePack.Rules.Should().ContainSingle(rule =>
            rule.RuleId == "intercompany.due-to-from" &&
            rule.RuleVersion == "v2" &&
            rule.SourceEventType == "IntercompanyTransfer" &&
            rule.Tags!.SequenceEqual(new[] { "intercompany", "elimination" }));
    }

    [Fact]
    public async Task BuildDraftAsync_CancelledToken_ThrowsOperationCanceledException()
    {
        var service = CreateService(new AccountingPolicyService());
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = async () => await service.BuildDraftAsync(new AccountingJournalDraftRequest(
            Guid.NewGuid(),
            Guid.NewGuid(),
            DateTimeOffset.Parse("2026-05-31T21:00:00Z"),
            "Cancelled draft",
            [
                new AccountingJournalDraftLineRequest(
                    new LedgerAccount("Cash", LedgerAccountType.Asset),
                    Debit: 1m,
                    Credit: 0m),
                new AccountingJournalDraftLineRequest(
                    new LedgerAccount("Capital Account", LedgerAccountType.Equity),
                    Debit: 0m,
                    Credit: 1m)
            ],
            EvidenceLinks: ["provider://source/cancelled"]),
            cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private static AccountingJournalDraftService CreateService(AccountingPolicyService policyService)
        => new(policyService, new AccountingBasisProjectionService(policyService));
}
