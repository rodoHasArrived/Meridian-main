using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster.CashFlow;
using Meridian.Contracts.CashFlows;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage;
using NSubstitute;

namespace Meridian.Tests.Application;

public sealed class BondExerciseNoticeStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-exercise-notices", Guid.NewGuid().ToString("N"));
    private static readonly Guid Security = Guid.Parse("7b1c2d3e-4f50-4a6b-8c9d-0e1f2a3b4c5d");
    private static readonly DateTimeOffset At = new(2026, 10, 15, 9, 0, 0, TimeSpan.Zero);

    private FileBondExerciseNoticeStore Store(DateTimeOffset? now = null) => new(new StorageOptions { RootPath = _root }, new FixedTimeProvider(now ?? At));

    private static readonly DateTimeOffset Cutoff = At.AddDays(1);
    private static readonly ContractualExerciseEntry Entry = new(CashFlowExerciseSide.Call, new DateOnly(2026, 1, 1), null, CashFlowExerciseStyle.European, 101m, "main");

    private static BondExerciseNoticeDto Notice(Guid? id = null) => new(id ?? Guid.NewGuid(), Security, "main", CashFlowExerciseSide.Call,
        new DateOnly(2026, 10, 15), new DateOnly(2026, 12, 1), 101m, 1000m, new string('c', 64), new DateOnly(2026, 10, 16), "issuer-notice-2026-10-15.pdf",
        "recorder", At);

    private static Task<ExerciseNoticeVerification> Verify(FileBondExerciseNoticeStore store, SelectedCashFlowExercise selection, Guid? security = null,
        DateTimeOffset? cutoff = null, ContractualExerciseEntry? entry = null)
        => store.VerifyAsync(security ?? Security, selection, cutoff ?? Cutoff, entry ?? Entry);

    [Fact]
    public async Task Record_IsWriteOnce_DecisionIsOnceByAnotherActor_AndVerificationNeedsAnApprovedMatch()
    {
        var store = Store();
        var notice = Notice();
        (await store.RecordAsync(notice)).Should().Be(notice);
        (await store.RecordAsync(notice)).Should().Be(notice, "an identical retry returns the retained notice");
        (await store.RecordAsync(notice with { RecordedAtUtc = At.AddMinutes(5) })).Should().Be(notice,
            "a retry restating the same evidence under a later clock returns the first retained record, keeping the original recording instant");
        var different = () => store.RecordAsync(notice with { Price = 102m });
        await different.Should().ThrowAsync<InvalidOperationException>().WithMessage("*different evidence*");

        var selection = new SelectedCashFlowExercise(CashFlowExerciseSide.Call, new DateOnly(2026, 1, 1), "main", new DateOnly(2026, 12, 1), new DateOnly(2026, 10, 15), 1000m, false, notice.NoticeId.ToString("D"));
        (await Verify(store, selection)).Should().Match<ExerciseNoticeVerification>(v => !v.Verified && v.Reason.Contains("Recorded"));

        var self = () => store.DecideAsync(Security, notice.NoticeId, true, "recorder", null);
        await self.Should().ThrowAsync<InvalidOperationException>().WithMessage("*cannot approve or reject their own notice*");
        var approved = await Store(At.AddHours(2)).DecideAsync(Security, notice.NoticeId, true, "reviewer", "Matches the trustee notice.");
        approved.ApprovalState.Should().Be(ExerciseNoticeApprovalState.Approved);
        approved.DecidedBy.Should().Be("reviewer");
        approved.DecidedAtUtc.Should().Be(At.AddHours(2));
        var again = () => store.DecideAsync(Security, notice.NoticeId, false, "other", "changed my mind");
        await again.Should().ThrowAsync<InvalidOperationException>().WithMessage("*already been decided*");

        (await Verify(store, selection)).Verified.Should().BeTrue();
        (await Verify(store, selection with { Amount = 999m })).Verified.Should().BeFalse("the stated amount must match");
        (await Verify(store, selection with { ExerciseDate = new DateOnly(2026, 12, 2) })).Verified.Should().BeFalse();
        (await Verify(store, selection with { HypotheticalNotice = true })).Verified.Should().BeFalse();
        (await Verify(store, selection, security: Guid.NewGuid())).Verified.Should().BeFalse("another security has no such notice");
        (await Verify(store, selection with { NoticeEvidenceId = "not-a-guid" })).Reason.Should().Contain("not a notice identity");

        (await Verify(store, selection, cutoff: At.AddMinutes(-1))).Reason.Should().Contain("recorded after the scenario's knowledge cutoff");
        (await Verify(store, selection, cutoff: At.AddHours(1))).Reason.Should().Contain("approved after the scenario's knowledge cutoff",
            "an approval the scenario could not have known about does not retroactively verify it");
        (await Verify(store, selection, cutoff: At.AddHours(2))).Verified.Should().BeTrue("the cutoff is inclusive of the decision instant");

        (await Verify(store, selection, entry: Entry with { CleanPrice = 100m })).Reason.Should().Contain("states price 101 but the contract's exercise entry declares 100");
        (await Verify(store, selection, entry: Entry with { CleanPrice = null })).Reason.Should().Contain("declares no fixed price");
        (await Verify(store, selection, entry: Entry with { MakeWhole = new(MakeWholeFormula.BenchmarkYieldPvV1, "UST-10Y", 50m, 2, DayCountConvention.Actual365) }))
            .Reason.Should().Contain("make-whole");
        (await store.VerifyAsync(Security, selection, Cutoff, entry: null)).Reason.Should().Contain("no exercise entry");
        (await store.ListAsync(Security)).Should().ContainSingle().Which.ApprovalState.Should().Be(ExerciseNoticeApprovalState.Approved);
    }

    [Fact]
    public async Task Rejection_NeedsAReason_AndARejectedNoticeNeverVerifies()
    {
        var store = Store();
        var notice = await store.RecordAsync(Notice());
        var noReason = () => store.DecideAsync(Security, notice.NoticeId, false, "reviewer", " ");
        await noReason.Should().ThrowAsync<ArgumentException>().WithMessage("*needs a reason*");
        var rejected = await store.DecideAsync(Security, notice.NoticeId, false, "reviewer", "Document hash does not match the trustee portal.");
        rejected.ApprovalState.Should().Be(ExerciseNoticeApprovalState.Rejected);
        var selection = new SelectedCashFlowExercise(CashFlowExerciseSide.Call, new DateOnly(2026, 1, 1), "main", new DateOnly(2026, 12, 1), new DateOnly(2026, 10, 15), 1000m, false, notice.NoticeId.ToString("D"));
        (await Verify(store, selection)).Reason.Should().Contain("Rejected");
    }

    [Fact]
    public async Task TwoStoreInstances_SharingTheRoot_RecordOnceAndDecideOnce()
    {
        // Separate instances model separate hosts: the in-process gate is not shared, so only the lock file serialises them.
        var first = Store();
        var second = Store(At.AddHours(2));
        var notice = Notice();
        var records = await Task.WhenAll(Enumerable.Range(0, 8).Select(i => (i % 2 == 0 ? first : second).RecordAsync(notice with { RecordedAtUtc = At.AddSeconds(i) })));
        records.Should().AllBeEquivalentTo(notice, "every concurrent writer sees the one retained record");
        (await first.GetAsync(Security, notice.NoticeId))!.RecordedAtUtc.Should().Be(At);
        Directory.EnumerateFiles(Path.Combine(_root, "exercise-notices", Security.ToString("N")), "*.tmp").Should().BeEmpty();

        var decisions = await Task.WhenAll(Enumerable.Range(0, 6).Select(async i =>
        {
            try
            { await (i % 2 == 0 ? first : second).DecideAsync(Security, notice.NoticeId, i % 3 == 0, "reviewer-" + i, "decision " + i); return true; }
            catch (InvalidOperationException e) when (e.Message.Contains("already been decided")) { return false; }
        }));
        decisions.Count(d => d).Should().Be(1, "exactly one decision is retained across both instances");
        (await second.GetAsync(Security, notice.NoticeId))!.ApprovalState.Should().NotBe(ExerciseNoticeApprovalState.Recorded);
    }

    [Fact]
    public void Validation_RefusesSelfDecisionAndIncompleteEvidence()
    {
        Notice().Validate().Should().BeEmpty();
        (Notice() with { NoticeDate = new DateOnly(2026, 12, 2) }).Validate().Should().Contain(i => i.Contains("cannot follow"));
        (Notice() with { SourceDocumentSha256 = "abc" }).Validate().Should().Contain(i => i.Contains("SHA-256"));
        (Notice() with { ApprovalState = ExerciseNoticeApprovalState.Approved, DecidedBy = "recorder", DecidedAtUtc = At }).Validate().Should().Contain(i => i.Contains("cannot decide their own"));
    }

    [Fact]
    public async Task Preview_UsesApprovedNoticeEvidenceForARetainedExercise_AndBlocksOtherwise()
    {
        var store = Store();
        var notice = await store.RecordAsync(Notice() with { ExerciseDate = new DateOnly(2026, 7, 1), NoticeDate = new DateOnly(2026, 6, 1), ReceivedDate = new DateOnly(2026, 6, 1), Amount = 1000m });
        var query = Substitute.For<ISecurityMasterQueryService>();
        var cutoff = At.AddHours(1);
        query.GetRecordedByIdAsOfAsync(Security, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(new SecurityDetailDto(Security, "Bond", SecurityStatusDto.Active, "Test", "USD",
            JsonSerializer.SerializeToElement(new { }), JsonSerializer.SerializeToElement(new { issueDate = "2026-01-01", maturity = "2027-01-01", par = 1000m, couponRate = 5m, paymentFrequency = "Quarterly", dayCount = "ACT/365" }),
            [], [], 1, new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), null));
        var contract = new ContractualCashFlowContract(new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 1), "USD",
            [new("main", 1000, 3, DayCountConvention.Actual365, .05m)])
        { Exercises = [new(CashFlowExerciseSide.Call, new DateOnly(2026, 7, 1), null, CashFlowExerciseStyle.European, 101, "main")] };
        var exercise = new SelectedCashFlowExercise(CashFlowExerciseSide.Call, new DateOnly(2026, 7, 1), "main", new DateOnly(2026, 7, 1), new DateOnly(2026, 6, 1), 1000m, false, notice.NoticeId.ToString("D"));
        var request = new ContractualCashFlowRequest(new DateOnly(2026, 1, 1), cutoff) { ContractOverride = contract, Exercises = [exercise] };

        var withoutVerifier = await new ContractualCashFlowPreviewService(query, Substitute.For<ISecurityMasterCashFlowService>()).PreviewSecurityAsync(Security, request);
        withoutVerifier!.Blockers.Should().ContainSingle().Which.Code.Should().Be("NoticeEvidenceVerificationUnavailable");

        var service = new ContractualCashFlowPreviewService(query, Substitute.For<ISecurityMasterCashFlowService>(), noticeVerifier: store);
        var unapproved = await service.PreviewSecurityAsync(Security, request);
        unapproved!.Blockers.Should().ContainSingle().Which.Code.Should().Be("NoticeEvidenceUnverified");

        await Store(At.AddHours(1)).DecideAsync(Security, notice.NoticeId, true, "reviewer", null);
        var earlierCutoff = request with { KnowledgeCutoff = At.AddMinutes(30) };
        (await service.PreviewSecurityAsync(Security, earlierCutoff))!.Blockers.Should().ContainSingle()
            .Which.Message.Should().Contain("approved after the scenario's knowledge cutoff");
        var otherPrice = request with { ContractOverride = contract with { Exercises = [contract.Exercises![0] with { CleanPrice = 100m }] } };
        (await service.PreviewSecurityAsync(Security, otherPrice))!.Blockers.Should().ContainSingle().Which.Message.Should().Contain("declares 100");

        var verified = await service.PreviewSecurityAsync(Security, request);
        verified!.Blockers.Should().BeEmpty();
        verified.ExerciseCalculations.Should().ContainSingle().Which.Selection.HypotheticalNotice.Should().BeFalse();
        verified.Events.Should().Contain(e => e.RedeemedPrincipal > 0m && e.Date == new DateOnly(2026, 7, 1));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, true);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
