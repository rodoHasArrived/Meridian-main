using FluentAssertions;
using Meridian.Contracts.Ledger;
using Meridian.FinancialOperations.Ledger;

namespace Meridian.Tests.FinancialOperations.Ledger;

public sealed class AccountingBasisProjectionSetServiceTests
{
    [Fact]
    public async Task BuildProjectionSet_UsesLedgerBookAggregateForEachAccountingBasis()
    {
        var candidateService = new CapturingPostingCandidateService();
        var service = new AccountingBasisProjectionSetService(candidateService);
        var sourceEventId = Guid.Parse("aaaaaaaa-1111-2222-3333-bbbbbbbbbbbb");
        var gaapBookId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var taxBookId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var gaapPeriodId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var taxPeriodId = Guid.Parse("44444444-4444-4444-4444-444444444444");

        var result = await service.BuildProjectionSetAsync(new AccountingBasisProjectionSetRequestDto(
            FundProfileId: "fund-alpha",
            SourceEventType: "CustodianInterestAccrual",
            EventAmount: 125.44m,
            Currency: "usd",
            EffectiveDate: new DateOnly(2026, 5, 31),
            Actor: "controller@meridian.local",
            SourceEventId: sourceEventId,
            AccountingTimestamp: DateTimeOffset.Parse("2026-05-31T21:00:00Z"),
            Description: "Accrue custodian interest from retained source event",
            Targets:
            [
                new AccountingBasisProjectionTargetDto(
                    AccountingBasisKindDto.Gaap,
                    gaapBookId,
                    gaapPeriodId,
                    PolicyId: "gaap-accrual-v1",
                    TreatmentKind: AccountingTreatmentKindDto.Accrual,
                    Dimensions: new LedgerDimensionSetDto(FundId: "fund-alpha", EntityId: "entity-master")),
                new AccountingBasisProjectionTargetDto(
                    AccountingBasisKindDto.Tax,
                    taxBookId,
                    taxPeriodId,
                    PolicyId: "tax-cash-v1",
                    TreatmentKind: AccountingTreatmentKindDto.General,
                    Dimensions: new LedgerDimensionSetDto(FundId: "fund-alpha", EntityId: "tax-entity"))
            ],
            CorrelationId: Guid.Parse("55555555-5555-5555-5555-555555555555"),
            EvidenceLinks: ["provider://custodian/interest-accruals/2026-05"]));

        result.SourceEventId.Should().Be(sourceEventId);
        result.Currency.Should().Be("USD");
        result.Items.Should().HaveCount(2);
        result.Items.Should().OnlyContain(item => item.Status == "ReadyForApproval");
        result.Items.Should().Contain(item =>
            item.AccountingBasis == AccountingBasisKindDto.Gaap &&
            item.LedgerBookId == gaapBookId &&
            item.PeriodId == gaapPeriodId &&
            item.Candidate.PostingCommand!.AggregateId == gaapBookId &&
            item.Candidate.PostingCommand.SourceEventId == sourceEventId);
        result.Items.Should().Contain(item =>
            item.AccountingBasis == AccountingBasisKindDto.Tax &&
            item.LedgerBookId == taxBookId &&
            item.PeriodId == taxPeriodId &&
            item.Candidate.PostingCommand!.AggregateId == taxBookId &&
            item.Candidate.PostingCommand.SourceEventId == sourceEventId);

        candidateService.Requests.Should().HaveCount(2);
        candidateService.Requests.Should().Contain(request =>
            request.AccountingBasis == AccountingBasisKindDto.Gaap &&
            request.AggregateId == gaapBookId &&
            request.LedgerBookId == gaapBookId &&
            request.PeriodId == gaapPeriodId &&
            request.SourceEventId == sourceEventId);
        candidateService.Requests.Should().Contain(request =>
            request.AccountingBasis == AccountingBasisKindDto.Tax &&
            request.AggregateId == taxBookId &&
            request.LedgerBookId == taxBookId &&
            request.PeriodId == taxPeriodId &&
            request.SourceEventId == sourceEventId);
    }

    private sealed class CapturingPostingCandidateService : IAccountingPostingCandidateService
    {
        public List<PostingRuleJournalCandidateRequestDto> Requests { get; } = [];

        public Task<PostingRuleJournalCandidateResultDto> BuildCandidateAsync(
            PostingRuleJournalCandidateRequestDto request,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            Requests.Add(request);

            var generatedLines = new[]
            {
                new GeneratedPostingLineDto(
                    "debit-cash",
                    "Assets:Cash",
                    AccountingTemplateLineSideDto.Debit,
                    "source-amount",
                    request.EventAmount,
                    request.Currency,
                    request.Dimensions),
                new GeneratedPostingLineDto(
                    "credit-income",
                    "Income:Interest",
                    AccountingTemplateLineSideDto.Credit,
                    "source-amount",
                    request.EventAmount,
                    request.Currency,
                    request.Dimensions)
            };
            var dryRun = new RuleDryRunResultDto(
                request.FundProfileId,
                request.LedgerBookId,
                request.SourceEventType,
                request.EffectiveDate,
                request.EventAmount,
                request.Currency,
                IsPostingBalanced: true,
                SelectedRuleId: "posting.interest-accrual",
                RuleMatches:
                [
                    new AccountingRuleDryRunMatchDto(
                        "posting.interest-accrual",
                        "Interest accrual",
                        "v1",
                        Priority: 100,
                        IsMatched: true,
                        Explanations: ["matched source event"],
                        ValidationIssues: [])
                ],
                GeneratedLines: [],
                ValidationIssues: [],
                GeneratedPostingLines: generatedLines);
            var command = new AccountingPostingCommandDto(
                Guid.NewGuid(),
                request.AggregateId,
                request.PeriodId,
                request.EffectiveDate,
                request.AccountingTimestamp,
                $"projection:{request.LedgerBookId:N}:{request.SourceEventId?.ToString("N") ?? "none"}",
                SourceEventId: request.SourceEventId,
                CorrelationId: request.CorrelationId,
                SourceEventType: request.SourceEventType,
                ApprovalState: AccountingPostingApprovalStateDto.Pending,
                Evidence: request.EvidenceLinks.Select(link => new AccountingPostingEvidenceReferenceDto(
                    link,
                    link,
                    AccountingPostingEvidenceKindDto.Source,
                    "TestProvider",
                    request.AccountingTimestamp,
                    request.Actor)).ToArray(),
                LedgerBookId: request.LedgerBookId);

            return Task.FromResult(new PostingRuleJournalCandidateResultDto(
                dryRun,
                "posting.interest-accrual",
                "v1",
                generatedLines,
                command,
                Guid.NewGuid(),
                request.EventAmount,
                request.EventAmount,
                0m,
                IsBalanced: true,
                HasBlockingIssues: false,
                CanSubmitForApproval: true,
                CanPostWithoutAdditionalApproval: false,
                request.EvidenceLinks,
                Issues: []));
        }
    }
}
