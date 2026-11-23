using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Storage.Operations;
using Moq;

namespace Meridian.Tests.FinancialOperations.Intercompany;

public sealed class IntercompanyAgreementWorkQueueAdversarialTests
{
    [Theory]
    [InlineData("")]
    [InlineData("not-an-authority-digest")]
    public async Task CurrentFlagCannotReplaceMissingSourceFingerprint(string fingerprint)
    {
        using var fixture = new IntercompanyAgreementWorkQueueFixture();
        fixture.Monitor.Setup(service => service.EvaluateAsync(It.IsAny<IntercompanyAgreementCallContext>(),
                It.IsAny<string>(), It.IsAny<IntercompanyObligationMonitorRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IntercompanyAgreementCallContext context, string company,
                IntercompanyObligationMonitorRequestDto scope, CancellationToken _) =>
                fixture.Observation(context, company, scope) with { SourceFingerprint = fingerprint });

        await Assert.ThrowsAsync<IntercompanyAgreementWorkQueueUnavailableException>(() => fixture.GetAsync());

        Assert.Empty(await fixture.History.ReadAsync(new() { CaseType = "intercompany-agreement-work" }));
    }

    [Fact]
    public async Task ConcurrentExactCommandRetriesReturnOneDurableReceipt()
    {
        using var fixture = new IntercompanyAgreementWorkQueueFixture();
        var queue = await fixture.GetAsync();
        var request = new IntercompanyAgreementWorkQueueObserveRequestDto(Guid.NewGuid(), queue.Cases[0].CaseId,
            queue.SourceFingerprint, "Track the same obligation from concurrent clients");
        var first = fixture.CreateService(new FileOperationalCaseHistoryStore(fixture.Root));
        var second = fixture.CreateService(new FileOperationalCaseHistoryStore(fixture.Root));

        var results = await Task.WhenAll(
            first.ObserveAsync(fixture.Context, fixture.Company, fixture.Scope, request),
            second.ObserveAsync(fixture.Context, fixture.Company, fixture.Scope, request));

        Assert.Equal(results[0].Version, results[1].Version);
        Assert.Equal(results[0].RecordHash, results[1].RecordHash);
        Assert.Single(await fixture.History.ReadAsync(new() { CaseType = "intercompany-agreement-work" }));
    }

    [Fact]
    public async Task DifferentActorCannotReuseRetainedCommandIdentity()
    {
        using var fixture = new IntercompanyAgreementWorkQueueFixture();
        var queue = await fixture.GetAsync();
        var request = new IntercompanyAgreementWorkQueueObserveRequestDto(Guid.NewGuid(), queue.Cases[0].CaseId,
            queue.SourceFingerprint, "Track the obligation");
        await fixture.Service.ObserveAsync(fixture.Context, fixture.Company, fixture.Scope, request);

        await Assert.ThrowsAsync<IntercompanyAgreementWorkQueueConflictException>(() =>
            fixture.Service.ObserveAsync(new(fixture.Context.TenantId, "different-operator"),
                fixture.Company, fixture.Scope, request));

        Assert.Single(await fixture.History.ReadAsync(new() { CaseType = "intercompany-agreement-work" }));
    }

    [Fact]
    public async Task FormerPartyGrantIsRequiredEvenAfterLegalPartiesChange()
    {
        using var fixture = new IntercompanyAgreementWorkQueueFixture();
        await fixture.TrackAsync();
        var replacementParty = Guid.NewGuid();
        var revision = fixture.Detail.ApplicableRevision! with
        {
            Terms = fixture.Detail.ApplicableRevision.Terms with { PartyTwoId = replacementParty }
        };
        fixture.Detail = fixture.Detail with
        {
            PartyTwoId = replacementParty, ApplicableRevision = revision, Revisions = [revision]
        };
        fixture.Findings = [];
        fixture.Access.Setup(authority => authority.CanAccessEntitiesAsync(It.IsAny<IntercompanyAgreementCallContext>(),
                It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IntercompanyAgreementCallContext _, IReadOnlyList<Guid> ids, CancellationToken _) =>
                !ids.Contains(fixture.CounterpartyId));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.GetAsync());

        Assert.Single(await fixture.History.ReadAsync(new() { CaseType = "intercompany-agreement-work" }));
    }

    [Fact]
    public async Task SourceChangeBetweenInitialCaptureAndAppendRefusesTheAction()
    {
        using var fixture = new IntercompanyAgreementWorkQueueFixture();
        var tracked = await fixture.TrackAsync();
        var queue = await fixture.GetAsync();
        var captures = 0;
        fixture.Monitor.Setup(service => service.EvaluateAsync(It.IsAny<IntercompanyAgreementCallContext>(),
                It.IsAny<string>(), It.IsAny<IntercompanyObligationMonitorRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IntercompanyAgreementCallContext context, string company,
                IntercompanyObligationMonitorRequestDto scope, CancellationToken _) =>
            {
                if (++captures == 2)
                    fixture.SourceTag = "source-changed-before-append";
                return fixture.Observation(context, company, scope);
            });
        var request = new IntercompanyAgreementWorkQueueUpdateRequestDto(Guid.NewGuid(), tracked.CaseId,
            IntercompanyAgreementWorkItemActionDto.AssignOwner, tracked.Version, tracked.RecordHash!,
            queue.SourceFingerprint, "Assign the obligation", Owner: "case-owner");

        await Assert.ThrowsAsync<IntercompanyAgreementWorkQueueConflictException>(() =>
            fixture.Service.UpdateAsync(fixture.Context, fixture.Company, fixture.Scope, request));

        Assert.Equal(2, captures);
        Assert.Single(await fixture.History.ReadAsync(new() { CaseType = "intercompany-agreement-work" }));
    }

    [Fact]
    public async Task InterleavedCaseEventsUseTheirOwnPredecessorWithinTheGlobalSequence()
    {
        using var fixture = new IntercompanyAgreementWorkQueueFixture();
        var first = await fixture.TrackAsync();
        var originalFinding = fixture.Findings[0];
        fixture.Findings = [originalFinding, originalFinding with { DueDate = originalFinding.DueDate!.Value.AddDays(60) }];
        var second = await fixture.TrackAsync();

        first = await fixture.UpdateAsync(first, IntercompanyAgreementWorkItemActionDto.AssignOwner, owner: "first-owner");
        second = await fixture.UpdateAsync(second, IntercompanyAgreementWorkItemActionDto.AssignOwner, owner: "second-owner");

        Assert.Equal(3, first.Version);
        Assert.Equal(4, second.Version);
        Assert.Equal(new long[] { 1, 3 }, first.History.Select(record => record.Version));
        Assert.Equal(new long[] { 2, 4 }, second.History.Select(record => record.Version));
        var restarted = fixture.CreateService(new FileOperationalCaseHistoryStore(fixture.Root));
        var queue = await restarted.GetAsync(fixture.Context, fixture.Company, fixture.Scope);
        Assert.Equal("first-owner", queue.Cases.Single(item => item.CaseId == first.CaseId).Owner);
        Assert.Equal("second-owner", queue.Cases.Single(item => item.CaseId == second.CaseId).Owner);
    }

    [Fact]
    public async Task CommandIdentityCannotBeReusedForANewObligationBoundary()
    {
        using var fixture = new IntercompanyAgreementWorkQueueFixture();
        var queue = await fixture.GetAsync();
        var request = new IntercompanyAgreementWorkQueueObserveRequestDto(Guid.NewGuid(), queue.Cases[0].CaseId,
            queue.SourceFingerprint, "Track the obligation");
        await fixture.Service.ObserveAsync(fixture.Context, fixture.Company, fixture.Scope, request);
        fixture.Findings = [fixture.Findings[0] with { DueDate = fixture.Findings[0].DueDate!.Value.AddDays(90) }];
        fixture.SourceTag = "amended-obligation-boundary";
        queue = await fixture.GetAsync();
        var newCase = queue.Cases.Single(item => !item.IsTracked);

        await Assert.ThrowsAsync<IntercompanyAgreementWorkQueueConflictException>(() =>
            fixture.Service.ObserveAsync(fixture.Context, fixture.Company, fixture.Scope,
                request with { CaseId = newCase.CaseId, ExpectedSourceFingerprint = queue.SourceFingerprint }));

        Assert.Single(await fixture.History.ReadAsync(new() { CaseType = "intercompany-agreement-work" }));
    }
}
