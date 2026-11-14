using Meridian.Contracts.FundStructure;
using Meridian.Entities.FundStructure;
using Meridian.Storage.FundStructure;
using Xunit;

namespace Meridian.FundStructure.Tests;

public sealed class EntityLifecycleHistoryTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(LegalEntityLifecycleStatusDto.Active, EntityLifecycleOperationDto.GeneralPosting, false, true)]
    [InlineData(LegalEntityLifecycleStatusDto.AmendmentPending, EntityLifecycleOperationDto.GeneralPosting, false, true)]
    [InlineData(LegalEntityLifecycleStatusDto.Restructuring, EntityLifecycleOperationDto.NewAgreement, true, true)]
    [InlineData(LegalEntityLifecycleStatusDto.Draft, EntityLifecycleOperationDto.GeneralPosting, true, false)]
    [InlineData(LegalEntityLifecycleStatusDto.Forming, EntityLifecycleOperationDto.NewAgreement, true, false)]
    [InlineData(LegalEntityLifecycleStatusDto.WindingDown, EntityLifecycleOperationDto.GeneralPosting, true, false)]
    [InlineData(LegalEntityLifecycleStatusDto.WindingDown, EntityLifecycleOperationDto.NewAgreement, true, false)]
    [InlineData(LegalEntityLifecycleStatusDto.WindingDown, EntityLifecycleOperationDto.Settlement, false, false)]
    [InlineData(LegalEntityLifecycleStatusDto.WindingDown, EntityLifecycleOperationDto.Settlement, true, true)]
    [InlineData(LegalEntityLifecycleStatusDto.WindingDown, EntityLifecycleOperationDto.ClosingExpense, true, true)]
    [InlineData(LegalEntityLifecycleStatusDto.WindingDown, EntityLifecycleOperationDto.Tax, true, true)]
    [InlineData(LegalEntityLifecycleStatusDto.WindingDown, EntityLifecycleOperationDto.Revaluation, true, true)]
    [InlineData(LegalEntityLifecycleStatusDto.WindingDown, EntityLifecycleOperationDto.Correction, true, true)]
    [InlineData(LegalEntityLifecycleStatusDto.Dissolved, EntityLifecycleOperationDto.Settlement, true, false)]
    [InlineData(LegalEntityLifecycleStatusDto.Dissolved, EntityLifecycleOperationDto.Correction, true, false)]
    [InlineData(LegalEntityLifecycleStatusDto.Dissolved, EntityLifecycleOperationDto.Consolidation, false, true)]
    [InlineData(LegalEntityLifecycleStatusDto.Merged, EntityLifecycleOperationDto.Read, false, true)]
    public void Eligibility_RequiresGovernedControlledClose(LegalEntityLifecycleStatusDto status, EntityLifecycleOperationDto operation, bool evidence, bool allowed)
    {
        var history = Revision(Guid.NewGuid(), status);
        Assert.Equal(allowed, EntityLifecyclePolicy.Evaluate(history, Start, operation, evidence).Allowed);
    }

    [Fact]
    public void Eligibility_CorrectionExceptionCannotAuthorizeNewObligations()
    {
        var history = Revision(Guid.NewGuid(), LegalEntityLifecycleStatusDto.Dissolved);
        Assert.True(EntityLifecyclePolicy.Evaluate(history, Start, EntityLifecycleOperationDto.Correction, true, true).Allowed);
        Assert.False(EntityLifecyclePolicy.Evaluate(history, Start, EntityLifecycleOperationDto.NewAgreement, true, true).Allowed);
        Assert.False(EntityLifecyclePolicy.Evaluate(history, Start, EntityLifecycleOperationDto.Correction, false, true).Allowed);
    }

    [Fact]
    public void Eligibility_UsesHalfOpenWindowsAndDoesNotGuessPreBaselineStatus()
    {
        var entity = Guid.NewGuid();
        var history = EntityLifecyclePolicy.Seal(Revision(entity, LegalEntityLifecycleStatusDto.Active) with
        {
            Timeline = [new(Guid.NewGuid(), Start, Start.AddDays(1), LegalEntityLifecycleStatusDto.Active, LegalEntityLifecycleEventKindDto.Formation),
                new(Guid.NewGuid(), Start.AddDays(1), null, LegalEntityLifecycleStatusDto.WindingDown, LegalEntityLifecycleEventKindDto.Restructure)]
        });
        Assert.True(EntityLifecyclePolicy.Evaluate(history, Start.AddDays(1).AddTicks(-1), EntityLifecycleOperationDto.GeneralPosting, true).Allowed);
        Assert.False(EntityLifecyclePolicy.Evaluate(history, Start.AddDays(1), EntityLifecycleOperationDto.GeneralPosting, true).Allowed);
        Assert.Equal("lifecycle-history-unknown", EntityLifecyclePolicy.Evaluate(history, Start.AddTicks(-1), EntityLifecycleOperationDto.GeneralPosting, true).ReasonCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Store_RejectsStaleWritesAndPreservesPinnedHistory(bool file)
    {
        var directory = Path.Combine(Path.GetTempPath(), "meridian-lifecycle-" + Guid.NewGuid().ToString("N"));
        try
        {
            IEntityLifecycleStore store = file ? new FileEntityLifecycleStore(directory) : new InMemoryEntityLifecycleStore();
            var first = Revision(Guid.NewGuid(), LegalEntityLifecycleStatusDto.Active);
            await store.AppendAsync(first, 0);
            var next = EntityLifecyclePolicy.Seal(first with { Revision = 2, PreviousHash = first.ContentHash, Reason = "Controlled close", Timeline = [first.Timeline[0] with { Status = LegalEntityLifecycleStatusDto.WindingDown }] });
            await store.AppendAsync(next, 1);
            await Assert.ThrowsAsync<InvalidOperationException>(() => store.AppendAsync(next, 1));
            Assert.Equal(LegalEntityLifecycleStatusDto.Active, (await store.GetRevisionAsync("tenant-a", first.EntityId, 1))!.Timeline[0].Status);
            Assert.Equal(LegalEntityLifecycleStatusDto.WindingDown, (await store.GetLatestAsync("tenant-a", first.EntityId))!.Timeline[0].Status);
            Assert.Null(await store.GetLatestAsync("tenant-b", first.EntityId));
            if (file)
                Assert.Equal(next.ContentHash, (await new FileEntityLifecycleStore(directory).GetLatestAsync("tenant-a", first.EntityId))!.ContentHash);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Store_ReadLeasePreventsStatusChangeUntilPostingCommit(bool file)
    {
        var directory = Path.Combine(Path.GetTempPath(), "meridian-lifecycle-" + Guid.NewGuid().ToString("N"));
        try
        {
            IEntityLifecycleStore store = file ? new FileEntityLifecycleStore(directory) : new InMemoryEntityLifecycleStore();
            var first = Revision(Guid.NewGuid(), LegalEntityLifecycleStatusDto.Active);
            await store.AppendAsync(first, 0);
            await using var reader = await store.AcquireReadLeaseAsync("tenant-a", [first.EntityId]);
            var next = EntityLifecyclePolicy.Seal(first with { Revision = 2, PreviousHash = first.ContentHash, Reason = "Dissolution", Timeline = [first.Timeline[0] with { Status = LegalEntityLifecycleStatusDto.Dissolved }] });
            var writer = store.AppendAsync(next, 1);
            Assert.False(writer.IsCompleted);
            Assert.Equal(1, (await store.GetLatestAsync("tenant-a", first.EntityId))!.Revision);
            await reader.DisposeAsync();
            Assert.Equal(2, (await writer.WaitAsync(TimeSpan.FromSeconds(5))).Revision);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Store_CanceledWriterReleasesAuthorityAndCannotAppend()
    {
        var store = new InMemoryEntityLifecycleStore();
        var first = Revision(Guid.NewGuid(), LegalEntityLifecycleStatusDto.Active);
        await using var reader = await store.AcquireReadLeaseAsync("tenant-a", [first.EntityId]);
        using var cancellation = new CancellationTokenSource();
        var writer = store.AppendAsync(first, 0, cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => writer);
        await reader.DisposeAsync();
        Assert.Null(await store.GetLatestAsync("tenant-a", first.EntityId));
        await store.AppendAsync(first, 0);
    }

    [Fact]
    public async Task Store_RejectsSuccessorCycleAndTamperedPayload()
    {
        var store = new InMemoryEntityLifecycleStore();
        var first = Revision(Guid.NewGuid(), LegalEntityLifecycleStatusDto.Active);
        var second = Revision(Guid.NewGuid(), LegalEntityLifecycleStatusDto.Active);
        await store.AppendAsync(first, 0);
        await store.AppendAsync(second, 0);
        var firstMerged = EntityLifecyclePolicy.Seal(first with { Revision = 2, PreviousHash = first.ContentHash, Timeline = [first.Timeline[0] with { Status = LegalEntityLifecycleStatusDto.Merged, SuccessorEntityId = second.EntityId }] });
        await store.AppendAsync(firstMerged, 1);
        var secondMerged = EntityLifecyclePolicy.Seal(second with { Revision = 2, PreviousHash = second.ContentHash, Timeline = [second.Timeline[0] with { Status = LegalEntityLifecycleStatusDto.Merged, SuccessorEntityId = first.EntityId }] });
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.AppendAsync(secondMerged, 1));
        var unrelated = Revision(Guid.NewGuid(), LegalEntityLifecycleStatusDto.Active);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.AppendAsync(unrelated with { Reason = "Changed after sealing" }, 0));
    }

    private static EntityLifecycleRevisionDto Revision(Guid entity, LegalEntityLifecycleStatusDto status)
        => EntityLifecyclePolicy.Seal(new("tenant-a", entity, 1, Start, "preparer", "Evidenced status", "retained://formation/approved", "", "",
            [new(Guid.NewGuid(), Start, null, status, LegalEntityLifecycleEventKindDto.Formation,
                status == LegalEntityLifecycleStatusDto.Merged ? Guid.NewGuid() : null)]));
}
