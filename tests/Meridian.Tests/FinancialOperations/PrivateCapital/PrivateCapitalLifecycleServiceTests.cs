using FluentAssertions;
using Meridian.Contracts.Ledger;
using Meridian.FinancialOperations.PrivateCapital;
using Meridian.Ledger;
using Moq;
using Xunit;

namespace Meridian.Tests.FinancialOperations.PrivateCapital;

public sealed class PrivateCapitalLifecycleServiceTests
{
    private static readonly PrivateCapitalScopeDto Scope = new("tenant-a", "company-a",
        "11111111-1111-1111-1111-111111111111", "fund-a", Guid.Parse("22222222-2222-2222-2222-222222222222"), "USD");
    private static readonly DateOnly Notice = new(2026, 10, 1);
    private static readonly string PeriodId = "33333333-3333-3333-3333-333333333333";

    [Fact]
    public async Task Preview_UsesApprovedEffectiveTermsAndConservesTwoInvestorCall()
    {
        var first = Commitment("c-1", 600_000m);
        var second = Commitment("c-2", 400_000m);
        var unapproved = first with { Revision = 2, TotalCommitment = 9_000_000m, ApprovalStatus = PrivateCapitalCommitmentApprovalStatus.Draft };
        var future = first with { Revision = 3, EffectiveFrom = Notice.AddDays(1), TotalCommitment = 10_000_000m };
        var store = Store([first, second, unapproved, future]);
        var service = new PrivateCapitalLifecycleService(store.Object);

        var preview = await service.PreviewCallAsync(Scope, Request());

        preview.IsReady.Should().BeTrue();
        preview.Call!.Lines.Select(x => x.Amount).Should().Equal(60_000m, 40_000m);
        preview.Call.Lines.Sum(x => x.Amount).Should().Be(100_000m);
        preview.Call.Lines.Single(x => x.CommitmentId == "c-1").CommitmentRevision.Should().Be(1);
        preview.Call.PlanHash.Should().HaveLength(64);
    }

    [Fact]
    public async Task Preview_UsesPostedCallsForCapacityAndDoesNotCountFundingAgain()
    {
        var store = Store([Commitment("c-1", 600_000m), Commitment("c-2", 400_000m)],
        [
            Receipt("issue-1", "c-1", "Issuance", 60_000m),
            Receipt("issue-2", "c-2", "Issuance", 40_000m),
            Receipt("cash-1", "c-1", "Funding", 25_000m),
            Receipt("cash-2", "c-1", "Funding", 35_000m),
            Receipt("cash-3", "c-2", "Funding", 40_000m)
        ]);
        var service = new PrivateCapitalLifecycleService(store.Object);

        var preview = await service.PreviewCallAsync(Scope, Request() with { AmountToCall = 900_000m });

        preview.IsReady.Should().BeTrue();
        preview.Call!.Lines.Select(x => x.Amount).Should().Equal(540_000m, 360_000m);
        preview.Call.Lines.Sum(x => x.UncalledAfter).Should().Be(0m);
        var overcall = await service.PreviewCallAsync(Scope, Request() with { AmountToCall = 900_000.01m });
        overcall.IsReady.Should().BeFalse();
        overcall.Call.Should().BeNull();
    }

    [Fact]
    public async Task Retain_ChangedPostedCapacityInvalidatesReviewedPreview()
    {
        var receipts = new List<PrivateCapitalPostingReceiptDto>();
        var store = Store([Commitment("c-1", 600_000m), Commitment("c-2", 400_000m)], receipts);
        var service = new PrivateCapitalLifecycleService(store.Object);
        var preview = await service.PreviewCallAsync(Scope, Request());
        receipts.Add(Receipt("concurrent", "c-1", "Issuance", 50_000m));

        var act = () => service.CreateCallAsync(Scope, new(Request(), preview.Call!.PlanHash), "reviewer");

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*preview changed*");
        store.Verify(x => x.SaveCallAsync(It.IsAny<PrivateCapitalCallDto>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Preview_LegacyActivityRequiresOpeningReconciliation()
    {
        var store = Store([Commitment("c-1", 600_000m), Commitment("c-2", 400_000m)]);
        store.Setup(x => x.HasUnreconciledLegacyCapitalAsync(Scope, "lp-c-1", It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await new PrivateCapitalLifecycleService(store.Object).PreviewCallAsync(Scope, Request());

        result.IsReady.Should().BeFalse();
        result.Blockers.Should().ContainSingle(x => x.Contains("opening reconciliation", StringComparison.Ordinal));
        result.Call.Should().BeNull();
    }

    [Fact]
    public async Task Retain_ExactRetryReturnsFrozenPlanAfterPostingWithoutReplanning()
    {
        var store = Store([Commitment("c-1", 600_000m), Commitment("c-2", 400_000m)]);
        var service = new PrivateCapitalLifecycleService(store.Object);
        var call = (await service.PreviewCallAsync(Scope, Request())).Call!;
        var posted = call with
        {
            Lines = call.Lines.Select(x => x with { IssuedAmount = x.Amount, IssuanceJournalEntryId = Guid.NewGuid() }).ToArray()
        };
        store.Setup(x => x.GetCallAsync(Scope, "call-1", It.IsAny<CancellationToken>())).ReturnsAsync(posted);

        var result = await service.CreateCallAsync(Scope, new(Request(), call.PlanHash), "operator");

        result.Should().BeSameAs(posted);
        store.Verify(x => x.SaveCallAsync(It.IsAny<PrivateCapitalCallDto>(), It.IsAny<CancellationToken>()), Times.Never);
        var conflictingRetry = () => service.CreateCallAsync(Scope,
            new(Request() with { AmountToCall = 101_000m }, call.PlanHash), "operator");
        await conflictingRetry.Should().ThrowAsync<InvalidOperationException>().WithMessage("*different reviewed request*");
    }

    [Fact]
    public async Task Create_RejectsMissingEvidenceAndAmbiguousEffectiveDates()
    {
        var service = new PrivateCapitalLifecycleService(new Mock<IPrivateCapitalStore>(MockBehavior.Strict).Object);
        var request = new CreatePrivateCapitalCommitmentRequest("c-1", "lp-1", "capital-1", 10_000m,
            Notice, Notice, null, []);

        var missingEvidence = () => service.CreateCommitmentAsync(Scope, request, "operator");
        await missingEvidence.Should().ThrowAsync<ArgumentException>().WithMessage("*evidence*");
        var invalidDate = () => service.CreateCommitmentAsync(Scope,
            request with { EvidenceLinks = ["evidence://terms"], EffectiveFrom = Notice.AddDays(-1) }, "operator");
        await invalidDate.Should().ThrowAsync<ArgumentException>().WithMessage("*date*");
    }

    [Fact]
    public async Task PlanHash_BindsTermsAndAllocationsButExcludesClockAndPostedObservations()
    {
        var store = Store([Commitment("c-1", 600_000m), Commitment("c-2", 400_000m)]);
        var call = (await new PrivateCapitalLifecycleService(store.Object).PreviewCallAsync(Scope, Request())).Call!;
        var observed = call with
        {
            CreatedAtUtc = DateTimeOffset.UtcNow, CreatedBy = "operator",
            Lines = call.Lines.Select(x => x with { IssuedAmount = x.Amount, IssuanceJournalEntryId = Guid.NewGuid() }).ToArray()
        };

        PrivateCapitalCallPlanHash.Compute(observed).Should().Be(call.PlanHash);
        PrivateCapitalCallPlanHash.Compute(call with { DueDate = call.DueDate.AddDays(1) }).Should().NotBe(call.PlanHash);
        PrivateCapitalCallPlanHash.Compute(call with { Lines = call.Lines.Select(x => x with { CommitmentRevision = 2 }).ToArray() })
            .Should().NotBe(call.PlanHash);
    }

    private static Mock<IPrivateCapitalStore> Store(
        IReadOnlyList<PrivateCapitalCommitmentDto> commitments, IReadOnlyList<PrivateCapitalPostingReceiptDto>? receipts = null)
    {
        var store = new Mock<IPrivateCapitalStore>(MockBehavior.Strict);
        store.Setup(x => x.ListCommitmentsAsync(Scope, It.IsAny<CancellationToken>())).ReturnsAsync(commitments);
        store.Setup(x => x.ListPostingReceiptsAsync(Scope, It.IsAny<CancellationToken>())).ReturnsAsync(receipts ?? []);
        store.Setup(x => x.HasUnreconciledLegacyCapitalAsync(Scope, It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        store.Setup(x => x.GetCallAsync(Scope, It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((PrivateCapitalCallDto?)null);
        return store;
    }

    private static PrivateCapitalCommitmentDto Commitment(string id, decimal amount)
        => new(id, Scope, 1, $"lp-{id}", $"capital-{id}", amount,
            new(2026, 1, 1), new(2026, 1, 1), null, true, PrivateCapitalCommitmentApprovalStatus.Approved,
            ["evidence://signed-terms"], "operator", new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), "reviewer", new(2026, 1, 2, 0, 0, 0, TimeSpan.Zero));

    private static PreviewPrivateCapitalCallRequest Request()
        => new("call-1", 100_000m, Notice, Notice.AddDays(14), PeriodId, ["c-2", "c-1"], "Investment funding");

    private static PrivateCapitalPostingReceiptDto Receipt(string id, string commitmentId, string kind, decimal amount)
        => new(id, Scope, commitmentId, "prior-call", $"prior-call:{commitmentId}", kind, amount, Guid.NewGuid(), new(2026, 9, 1));
}
