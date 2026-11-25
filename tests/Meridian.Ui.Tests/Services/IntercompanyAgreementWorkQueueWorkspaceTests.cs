using Meridian.Contracts.Api;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Ui.Services.Services.Accounting;

namespace Meridian.Ui.Tests.Services;

public sealed class IntercompanyAgreementWorkQueueWorkspaceTests
{
    private static readonly Guid Book = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Agreement = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateOnly AsOf = new(2026, 11, 23);
    private static readonly DateTimeOffset Seen = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task LoadIsReadOnly_AndTrackPinsTheDisplayedSourceAndCaseVersion()
    {
        var client = new Client { Queue = Queue(Item(tracked: false)) };
        using var view = await Open(client);
        Assert.Null(client.LastObservation);
        Assert.Null(client.LastUpdate);
        Assert.False(view.ObserveCommand.CanExecute(null));
        view.SelectedCase = Assert.Single(view.Cases);
        view.Reason = "Track the upcoming expiry";
        Assert.True(view.ObserveCommand.CanExecute(null));

        await view.ObserveCommand.ExecuteAsync(null);

        Assert.Equal(("case-a", "source-current", 0, (string?)null),
            (client.LastObservation!.CaseId, client.LastObservation.ExpectedSourceFingerprint, client.LastObservation.ExpectedVersion, client.LastObservation.ExpectedRecordHash));
        Assert.Equal("Track the upcoming expiry", client.LastObservation.Reason);
        Assert.NotEqual(Guid.Empty, client.LastObservation.CommandId);
        Assert.Equal((Book, AsOf, 30), (client.LastScope!.LedgerBookId, client.LastScope.AsOf, client.LastScope.ExpiryHorizonDays));
        Assert.True(Assert.Single(view.Cases).Item.IsTracked);
        Assert.Contains("No legal terms or journal changed", view.StatusText);
    }

    [Fact]
    public async Task AssignmentUsesExactVersionHashAndServerSource_AndResolutionRequiresEvidence()
    {
        var client = new Client();
        using var view = await Open(client);
        view.SelectedCase = Assert.Single(view.Cases);
        view.Owner = "review-operator";
        view.Reason = "Assign renewal follow-up";
        await view.UpdateCommand.ExecuteAsync(null);
        Assert.Equal(4, client.LastUpdate!.ExpectedVersion);
        Assert.Equal("case-hash-4", client.LastUpdate.ExpectedRecordHash);
        Assert.Equal("source-current", client.LastUpdate.ExpectedSourceFingerprint);
        Assert.Equal("review-operator", client.LastUpdate.Owner);
        Assert.Equal(IntercompanyAgreementWorkItemActionDto.AssignOwner, client.LastUpdate.Action);

        view.Action = IntercompanyAgreementWorkItemActionDto.Resolve;
        view.Reason = "Reviewed amendment retained";
        Assert.False(view.UpdateCommand.CanExecute(null));
        view.EvidenceReferences = "retained:amendment-7\nretained:review-8";
        Assert.True(view.UpdateCommand.CanExecute(null));
        await view.UpdateCommand.ExecuteAsync(null);
        Assert.Equal(new[] { "retained:amendment-7", "retained:review-8" }, client.LastUpdate!.EvidenceReferences);
    }

    [Theory]
    [InlineData("Partial")]
    [InlineData("Stale")]
    [InlineData("Unavailable")]
    public async Task IncompleteObservationCannotResolveOrModifyRetainedMissingCase(string status)
    {
        var client = new Client { Queue = Queue(Item() with { IsCurrentlyObserved = false }) with { Status = status, IsComplete = false } };
        using var view = await Open(client);
        view.SelectedCase = Assert.Single(view.Cases);
        view.Reason = "Retain follow-up";
        view.EvidenceReferences = "retained:review";
        Assert.Equal("Open", view.SelectedCase.State);
        Assert.Equal("Not observed at this cutoff", view.SelectedCase.Observation);
        Assert.False(view.ObserveCommand.CanExecute(null));
        Assert.False(view.UpdateCommand.CanExecute(null));
        Assert.Contains("complete current source observation", view.PermissionText);
    }

    [Fact]
    public async Task ServerPermissionIsRequired_AndUnknownExposureNeverUsesDeclaredCap()
    {
        var client = new Client
        {
            Queue = Queue(Item()) with
            {
                CanManage = false,
                Guarantees = [new(Agreement, "G-1", "Synthetic guarantee", 2, "legal-hash", Guid.NewGuid(), "Guarantor", Guid.NewGuid(), "Obligor", "USD", "bank-reference", 12345678901234567890.12345678m,
            new(2026, 1, 1), new(2027, 1, 1), null, IntercompanyAgreementStatusDto.Active, null, "Unavailable", "No reviewed underlying exposure mapping.")]
            }
        };
        using var view = await Open(client);
        view.SelectedCase = Assert.Single(view.Cases);
        view.Reason = "Reason";
        Assert.False(view.UpdateCommand.CanExecute(null));
        Assert.False(view.ObserveCommand.CanExecute(null));
        Assert.Equal("12345678901234567890.12345678 USD", Assert.Single(view.Guarantees).Cap);
        Assert.Equal("Unavailable", Assert.Single(view.Guarantees).Exposure);
    }

    [Fact]
    public async Task ChangedScopeRejectsLateReadAndImmediatelyClearsHistoryAndPermission()
    {
        var pending = new TaskCompletionSource<ApiResponse<IntercompanyAgreementWorkQueueDto>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new Client { Deferred = pending.Task };
        using var view = new IntercompanyAgreementWorkQueueWorkspace(client) { LedgerBook = Book.ToString("D"), AsOf = "2026-11-23" };
        var load = view.LoadCommand.ExecuteAsync(null);
        view.AsOf = "2026-11-24";
        pending.SetResult(ApiResponse<IntercompanyAgreementWorkQueueDto>.Ok(Queue(Item())));
        await load;
        Assert.Empty(view.Cases);
        Assert.Empty(view.Guarantees);
        Assert.False(view.CanManage);
        Assert.False(view.IsBusy);
        Assert.DoesNotContain("case-hash-4", view.SelectedHistory);
    }

    [Fact]
    public async Task ConflictingActionRetainsInputButRequiresFreshLoad_AndUnchangedRetryKeepsCommandIdentity()
    {
        var client = new Client { MutationFailure = ApiResponse<IntercompanyAgreementWorkItemDto>.Fail("Case changed", 409) };
        using var view = await Open(client);
        view.SelectedCase = Assert.Single(view.Cases);
        view.Reason = "Keep this reason";
        view.Owner = "operator-b";
        await view.UpdateCommand.ExecuteAsync(null);
        var first = client.LastUpdate!.CommandId;
        Assert.False(view.UpdateCommand.CanExecute(null));
        Assert.Equal("Keep this reason", view.Reason);
        await view.LoadCommand.ExecuteAsync(null);
        Assert.Equal("Keep this reason", view.Reason);
        Assert.True(view.UpdateCommand.CanExecute(null));
        await view.UpdateCommand.ExecuteAsync(null);
        Assert.Equal(first, client.LastUpdate!.CommandId);
    }

    [Fact]
    public async Task ResetSessionClearsOldBookHistoryAndAssignments()
    {
        using var view = await Open(new Client());
        view.SelectedCase = Assert.Single(view.Cases);
        view.Owner = "operator-a";
        view.ResetSession();
        Assert.Equal("", view.LedgerBook);
        Assert.Equal("", view.Owner);
        Assert.Empty(view.Cases);
        Assert.False(view.CanManage);
        Assert.False(view.UpdateCommand.CanExecute(null));
    }

    [Fact]
    public async Task ConfirmedActionRemainsConfirmedWhenItsAuthorityReloadFails()
    {
        var client = new Client();
        using var view = await Open(client);
        view.SelectedCase = Assert.Single(view.Cases);
        view.Reason = "Assign renewal follow-up";
        client.ReadFailure = ApiResponse<IntercompanyAgreementWorkQueueDto>.Fail("Source unavailable", 503);

        await view.UpdateCommand.ExecuteAsync(null);

        Assert.NotNull(client.LastUpdate);
        Assert.Contains("Case action retained at version 5.", view.StatusText);
        Assert.Contains("current queue could not be reloaded", view.StatusText);
        Assert.Empty(view.Cases);
        Assert.False(view.UpdateCommand.CanExecute(null));
    }

    [Fact]
    public async Task FailedReloadWithdrawsAuthorityButPreservesUnsentReasonAndEvidence()
    {
        var client = new Client { MutationFailure = ApiResponse<IntercompanyAgreementWorkItemDto>.Fail("Case changed", 409) };
        using var view = await Open(client);
        view.SelectedCase = Assert.Single(view.Cases);
        view.Reason = "Review supporting amendment";
        view.Note = "Pending operator review";
        view.EvidenceReferences = "retained:amendment-12";
        await view.UpdateCommand.ExecuteAsync(null);
        client.ReadFailure = ApiResponse<IntercompanyAgreementWorkQueueDto>.Fail("Source unavailable", 503);

        await view.LoadCommand.ExecuteAsync(null);

        Assert.Empty(view.Cases);
        Assert.False(view.CanManage);
        Assert.False(view.UpdateCommand.CanExecute(null));
        Assert.Equal("Review supporting amendment", view.Reason);
        Assert.Equal("Pending operator review", view.Note);
        Assert.Equal("retained:amendment-12", view.EvidenceReferences);

        client.ReadFailure = null;
        await view.LoadCommand.ExecuteAsync(null);
        Assert.Equal("case-a", view.SelectedCase!.Item.CaseId);
        Assert.Equal("Review supporting amendment", view.Reason);
        Assert.Equal("retained:amendment-12", view.EvidenceReferences);
    }

    private static async Task<IntercompanyAgreementWorkQueueWorkspace> Open(Client client)
    {
        var view = new IntercompanyAgreementWorkQueueWorkspace(client) { LedgerBook = Book.ToString("D"), AsOf = "2026-11-23" };
        await view.LoadCommand.ExecuteAsync(null);
        return view;
    }
    private static IntercompanyAgreementWorkItemDto Item(bool tracked = true) => new("case-a", Book, Agreement, 2,
        "intercompany.agreement.expiring", "Warning", "Loan coverage ends on 2026-12-01.", "Review amendment or wind-down.", new(2026, 12, 1), AsOf,
        true, tracked, IntercompanyAgreementWorkItemStateDto.Open, null, tracked ? Seen : null, tracked ? Seen : null,
        tracked ? 3 : null, 0, "source-current", tracked ? 4 : 0, tracked ? "case-hash-4" : null, []);
    private static IntercompanyAgreementWorkQueueDto Queue(IntercompanyAgreementWorkItemDto item) =>
        new(AsOf, 30, Seen.AddDays(3), "Current", true, "source-current", [item], [], []) { LedgerBookId = Book, FundProfileId = "synthetic-fund", CanManage = true };

    private sealed class Client : IWorkstationIntercompanyAgreementApiClient
    {
        public IntercompanyAgreementWorkQueueDto Queue { get; set; } = IntercompanyAgreementWorkQueueWorkspaceTests.Queue(Item());
        public IntercompanyObligationMonitorRequestDto? LastScope { get; private set; }
        public IntercompanyAgreementWorkQueueObserveRequestDto? LastObservation { get; private set; }
        public IntercompanyAgreementWorkQueueUpdateRequestDto? LastUpdate { get; private set; }
        public Task<ApiResponse<IntercompanyAgreementWorkQueueDto>>? Deferred { get; init; }
        public ApiResponse<IntercompanyAgreementWorkItemDto>? MutationFailure { get; init; }
        public ApiResponse<IntercompanyAgreementWorkQueueDto>? ReadFailure { get; set; }
        public Task<ApiResponse<IntercompanyAgreementWorkQueueDto>> GetAgreementWorkQueueAsync(IntercompanyObligationMonitorRequestDto scope, CancellationToken ct = default)
        { LastScope = scope; return Deferred ?? Task.FromResult(ReadFailure ?? ApiResponse<IntercompanyAgreementWorkQueueDto>.Ok(Queue)); }
        public Task<ApiResponse<IntercompanyAgreementWorkItemDto>> ObserveAgreementWorkQueueAsync(IntercompanyObligationMonitorRequestDto scope, IntercompanyAgreementWorkQueueObserveRequestDto request, CancellationToken ct = default)
        {
            LastScope = scope;
            LastObservation = request;
            var item = Item();
            Queue = Queue with { Cases = [item] };
            return Task.FromResult(ApiResponse<IntercompanyAgreementWorkItemDto>.Ok(item));
        }
        public Task<ApiResponse<IntercompanyAgreementWorkItemDto>> UpdateAgreementWorkQueueAsync(IntercompanyObligationMonitorRequestDto scope, IntercompanyAgreementWorkQueueUpdateRequestDto request, CancellationToken ct = default)
        {
            LastScope = scope;
            LastUpdate = request;
            var item = Queue.Cases[0] with { Version = Queue.Cases[0].Version + 1, RecordHash = "updated-hash", Owner = request.Owner };
            if (MutationFailure is null)
                Queue = Queue with { Cases = [item] };
            return Task.FromResult(MutationFailure ?? ApiResponse<IntercompanyAgreementWorkItemDto>.Ok(item));
        }
        public Task<ApiResponse<IntercompanyAgreementListDto>> ListAsync(IntercompanyAgreementQueryDto query, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ApiResponse<IntercompanyAgreementDetailDto>> GetAsync(Guid agreementId, DateOnly asOf, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ApiResponse<IntercompanyAgreementJournalLinesDto>> GetJournalLinesAsync(Guid agreementId, Guid ledgerBookId, DateOnly asOf, string? cursor = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ApiResponse<IntercompanyAgreementCommandResultDto>> ExecuteAsync(string action, IntercompanyAgreementCommandDto command, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
