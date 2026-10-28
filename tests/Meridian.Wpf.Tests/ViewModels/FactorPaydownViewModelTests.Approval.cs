using Meridian.Contracts.Integrity;
using System.Text;
using Meridian.Contracts.Ledger;
using Meridian.Wpf.ViewModels.Accounting;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed partial class FactorPaydownViewModelTests
{
    [Fact]
    public async Task Approval_MissingOrUnknownServerProtocolRemainsReadOnly()
    {
        var (view, client) = await SubmittedView();
        await view.ReloadApprovalStateCommand.ExecuteAsync(null);
        view.ApprovalStateText.Should().Contain("does not report");
        view.ApprovalNotes = "Reviewed source and lot relief.";
        view.ApprovalConfirmed = true;
        view.CanApproveRetainedDraft.Should().BeFalse();
        client.Capabilities = (_, _, _, _) => Task.FromResult<AssetAccountingDraftCapabilitiesDto?>(
            Capabilities(view.SelectedRetainedDraft!) with { ProtocolVersion = 2 });
        await view.ReloadApprovalStateCommand.ExecuteAsync(null);
        view.ApprovalConfirmed = true;
        view.PostingConfirmed = true;
        view.CanApproveRetainedDraft.Should().BeFalse();
        view.CanPostRetainedDraft.Should().BeFalse();
        client.Actions.Should().BeEmpty();
    }

    [Fact]
    public async Task Approval_RequiresServerPermissionRationaleAndConfirmationThenShowsRetainedProof()
    {
        var (view, client) = await SubmittedView();
        client.Capabilities = (_, _, _, _) => Task.FromResult<AssetAccountingDraftCapabilitiesDto?>(
            Capabilities(view.SelectedRetainedDraft!, approval: view.SelectedRetainedDraft!.Status == ManualJournalEntryStatusDto.Approved ? Receipt() : null));
        await view.ReloadApprovalStateCommand.ExecuteAsync(null);
        view.ApprovalNotes = "Independent review of factor source and principal allocation.";
        view.CanApproveRetainedDraft.Should().BeFalse();
        view.ApprovalConfirmed = true;
        view.CanApproveRetainedDraft.Should().BeTrue();
        client.Apply = (command, _) => Task.FromResult(ActionResult(view.SelectedRetainedDraft!, command.Action));
        await view.ApproveRetainedDraftCommand.ExecuteAsync(null);
        client.Actions.Should().ContainSingle().Which.Action.Should().Be(JournalEntryLifecycleActionDto.Approve);
        view.SelectedRetainedDraft!.Status.Should().Be(ManualJournalEntryStatusDto.Approved);
        view.ApprovalProofText.Should().Contain("independent-reviewer").And.Contain(Receipt().ContentHashSha256);
        view.CanPostRetainedDraft.Should().BeFalse();
        view.PostingConfirmed = true;
        view.CanPostRetainedDraft.Should().BeTrue();
    }

    [Fact]
    public async Task Approval_BadReceiptDigestAndDifferentApproverPermissionBlockPosting()
    {
        var (view, client) = await SubmittedView(ManualJournalEntryStatusDto.Approved);
        client.Capabilities = (_, _, _, _) => Task.FromResult<AssetAccountingDraftCapabilitiesDto?>(
            Capabilities(view.SelectedRetainedDraft!, Receipt() with { ContentHashSha256 = new('0', 64) }));
        await view.ReloadApprovalStateCommand.ExecuteAsync(null);
        view.PostingConfirmed = true;
        view.CanPostRetainedDraft.Should().BeFalse();
        view.ApprovalStateText.Should().Contain("does not verify");
        client.Capabilities = (_, _, _, _) => Task.FromResult<AssetAccountingDraftCapabilitiesDto?>(
            Capabilities(view.SelectedRetainedDraft!, Receipt()) with { CanPost = false, PostBlockedReason = "Only the retained independent approver may post." });
        await view.ReloadApprovalStateCommand.ExecuteAsync(null);
        view.PostingConfirmed = true;
        view.CanPostRetainedDraft.Should().BeFalse();
        view.ApprovalStateText.Should().Contain("Only the retained independent approver");
        client.Actions.Should().BeEmpty();
    }

    [Fact]
    public async Task Approval_UncertainRetryPreservesCommandVersionCorrelationAndRationale()
    {
        var (view, client) = await SubmittedView();
        client.Capabilities = (_, _, _, _) => Task.FromResult<AssetAccountingDraftCapabilitiesDto?>(Capabilities(view.SelectedRetainedDraft!));
        client.Apply = (_, _) => throw new IOException("Connection lost");
        await view.ReloadApprovalStateCommand.ExecuteAsync(null);
        view.ApprovalNotes = "Independent retained review.";
        view.ApprovalConfirmed = true;
        await view.ApproveRetainedDraftCommand.ExecuteAsync(null);
        var command = client.Actions.Single();
        view.CanApproveRetainedDraft.Should().BeFalse();
        view.ApprovalNotes = "Changed after an uncertain outcome";
        view.ApprovalNotes.Should().Be(command.Notes);
        await view.ReloadApprovalStateCommand.ExecuteAsync(null);
        view.ApprovalConfirmed = true;
        await view.ApproveRetainedDraftCommand.ExecuteAsync(null);
        client.Actions.Should().HaveCount(2);
        client.Actions[1].Should().BeSameAs(command);
        command.Version.Should().Be(7);
        Guid.TryParse(command.CorrelationId, out _).Should().BeTrue();
    }

    [Fact]
    public async Task Posting_ReplaysOriginalServerCommandAcrossAdvancedCheckpointAndShowsCanonicalResult()
    {
        var (view, client) = await SubmittedView(ManualJournalEntryStatusDto.Approved);
        var draft = view.SelectedRetainedDraft!;
        var original = new JournalEntryLifecycleActionRequestDto(draft.JournalEntryId, draft.FundProfileId,
            JournalEntryLifecycleActionDto.Post, "independent-reviewer", 4, "Exact retained rationale", "original-command", ["evidence://original"], LedgerBookId: BookId);
        client.Capabilities = (_, _, _, _) => Task.FromResult<AssetAccountingDraftCapabilitiesDto?>(
            Capabilities(view.SelectedRetainedDraft!, Receipt()) with
            { HasPendingPost = view.SelectedRetainedDraft!.Status != ManualJournalEntryStatusDto.Posted,
                PendingPostCommand = view.SelectedRetainedDraft.Status == ManualJournalEntryStatusDto.Posted ? null : original,
                PostedJournal = view.SelectedRetainedDraft.Status == ManualJournalEntryStatusDto.Posted ? CanonicalJournal(draft.JournalEntryId) : null });
        client.Apply = (command, _) => Task.FromResult(ActionResult(draft, command.Action, CanonicalJournal(draft.JournalEntryId)));
        await view.ReloadApprovalStateCommand.ExecuteAsync(null);
        view.PendingCommandText.Should().Contain("original-command").And.Contain("version 4");
        view.PostingConfirmed = true;
        await view.PostRetainedDraftCommand.ExecuteAsync(null);
        client.Actions.Should().ContainSingle().Which.Should().BeSameAs(original);
        view.CanonicalPostingText.Should().Contain(draft.JournalEntryId.ToString("D")).And.Contain("Canonical journal");
        view.SelectedRetainedDraft!.Status.Should().Be(ManualJournalEntryStatusDto.Posted);
        view.CanPostRetainedDraft.Should().BeFalse();
    }

    [Fact]
    public async Task Posting_AbsentCanonicalReceiptNeverClaimsPostingSucceeded()
    {
        var (view, client) = await SubmittedView(ManualJournalEntryStatusDto.Approved);
        client.Capabilities = (_, _, _, _) => Task.FromResult<AssetAccountingDraftCapabilitiesDto?>(Capabilities(view.SelectedRetainedDraft!, Receipt()));
        client.Apply = (command, _) => Task.FromResult(ActionResult(view.SelectedRetainedDraft!, command.Action));
        await view.ReloadApprovalStateCommand.ExecuteAsync(null);
        view.PostingConfirmed = true;
        await view.PostRetainedDraftCommand.ExecuteAsync(null);
        view.StatusText.Should().Contain("requires review").And.Contain("no canonical posting receipt");
        view.CanonicalPostingText.Should().StartWith("No canonical posting receipt");
        view.CanPostRetainedDraft.Should().BeFalse();
    }

    [Fact]
    public async Task Approval_StaleVersionAndIncompletePendingPostStateRemainReadOnly()
    {
        var (view, client) = await SubmittedView();
        client.Capabilities = (_, _, _, _) => Task.FromResult<AssetAccountingDraftCapabilitiesDto?>(
            Capabilities(view.SelectedRetainedDraft!) with { DraftVersion = 8 });
        await view.ReloadApprovalStateCommand.ExecuteAsync(null);
        view.ApprovalNotes = "Review";
        view.ApprovalConfirmed = true;
        view.CanApproveRetainedDraft.Should().BeFalse();
        view.ApprovalStateText.Should().Contain("version differs");
        client.Capabilities = (_, _, _, _) => Task.FromResult<AssetAccountingDraftCapabilitiesDto?>(
            Capabilities(view.SelectedRetainedDraft!) with { HasPendingPost = true, PendingPostCommand = null });
        await view.ReloadApprovalStateCommand.ExecuteAsync(null);
        view.PostingConfirmed = true;
        view.CanApproveRetainedDraft.Should().BeFalse();
        view.CanPostRetainedDraft.Should().BeFalse();
        view.ApprovalStateText.Should().Contain("pending-post state is incomplete");
    }

    [Fact]
    public async Task Posting_ReloadedCanonicalReceiptIsVisibleWithoutEnablingAnotherPost()
    {
        var (view, client) = await SubmittedView(ManualJournalEntryStatusDto.Posted);
        var posted = CanonicalJournal(view.SelectedRetainedDraft!.JournalEntryId);
        client.Capabilities = (_, _, _, _) => Task.FromResult<AssetAccountingDraftCapabilitiesDto?>(
            Capabilities(view.SelectedRetainedDraft!, Receipt()) with { PostedJournal = posted });
        await view.ReloadApprovalStateCommand.ExecuteAsync(null);
        view.CanonicalPostingText.Should().Contain(posted.JournalEntryId.ToString("D"));
        view.PostingConfirmed = true;
        view.CanPostRetainedDraft.Should().BeFalse();
        client.Actions.Should().BeEmpty();
    }

    [Fact]
    public async Task Approval_ProtocolWithoutAuthenticatedPrincipalRemainsReadOnly()
    {
        var (view, client) = await SubmittedView();
        client.Capabilities = (_, _, _, _) => Task.FromResult<AssetAccountingDraftCapabilitiesDto?>(
            Capabilities(view.SelectedRetainedDraft!) with { AuthenticatedActor = null });
        await view.ReloadApprovalStateCommand.ExecuteAsync(null);
        view.ApprovalNotes = "Review";
        view.ApprovalConfirmed = true;
        view.PostingConfirmed = true;
        view.CanApproveRetainedDraft.Should().BeFalse();
        view.CanPostRetainedDraft.Should().BeFalse();
        view.ApprovalStateText.Should().Contain("did not report the authenticated principal");
        client.Actions.Should().BeEmpty();
    }

    [Fact]
    public async Task Approval_PrincipalChangeDiscardsUncertainCacheDespiteUnchangedDisplayAndReviewText()
    {
        var (view, client) = await SubmittedView();
        var actor = "principal:one";
        client.Capabilities = (_, _, _, _) => Task.FromResult<AssetAccountingDraftCapabilitiesDto?>(
            Capabilities(view.SelectedRetainedDraft!) with { AuthenticatedActor = actor });
        client.Apply = (_, _) => throw new IOException("Connection lost");
        await view.ReloadApprovalStateCommand.ExecuteAsync(null);
        view.ApprovalNotes = "Same displayed reviewer and retained review text.";
        view.ApprovalConfirmed = true;
        await view.ApproveRetainedDraftCommand.ExecuteAsync(null);
        var first = client.Actions.Single();
        first.Actor.Should().Be("principal:one");
        actor = "principal:two";
        await view.ReloadApprovalStateCommand.ExecuteAsync(null);
        view.ApprovalNotes.Should().BeEmpty();
        view.PendingCommandText.Should().StartWith("No pending");
        view.ApprovalConfirmed.Should().BeFalse();
        view.ApprovalNotes = first.Notes!;
        view.ApprovalConfirmed = true;
        await view.ApproveRetainedDraftCommand.ExecuteAsync(null);
        client.Actions.Should().HaveCount(2);
        client.Actions[1].Actor.Should().Be("principal:two");
        client.Actions[1].CorrelationId.Should().NotBe(first.CorrelationId);
    }

    [Fact]
    public async Task Approval_ResponseFromPreviousPrincipalCannotPublishNewSessionAuthority()
    {
        var (view, client) = await SubmittedView();
        var actor = "principal:one";
        client.Capabilities = (_, _, _, _) => Task.FromResult<AssetAccountingDraftCapabilitiesDto?>(
            Capabilities(view.SelectedRetainedDraft!, Receipt()) with { AuthenticatedActor = actor });
        await view.ReloadApprovalStateCommand.ExecuteAsync(null);
        view.ApprovalNotes = "Independent review.";
        view.ApprovalConfirmed = true;
        client.Apply = (command, _) =>
        {
            var result = ActionResult(view.SelectedRetainedDraft!, command.Action);
            actor = "principal:two";
            return Task.FromResult(result);
        };
        await view.ApproveRetainedDraftCommand.ExecuteAsync(null);
        view.SelectedRetainedDraft.Should().BeNull();
        view.StatusText.Should().Contain("authenticated principal changed");
        view.CanApproveRetainedDraft.Should().BeFalse();
        view.CanPostRetainedDraft.Should().BeFalse();
    }

    [Fact]
    public async Task Approval_LateCapabilityReadCannotAuthorizeNewScope()
    {
        var (view, client) = await SubmittedView();
        var draft = view.SelectedRetainedDraft!;
        var pending = new TaskCompletionSource<AssetAccountingDraftCapabilitiesDto?>();
        client.Capabilities = (_, _, _, _) => pending.Task;
        var read = view.ReloadApprovalStateCommand.ExecuteAsync(null);
        view.SetScope("foreign", Guid.NewGuid());
        pending.SetResult(Capabilities(draft));
        await read;
        view.ApprovalConfirmed = true;
        view.PostingConfirmed = true;
        view.SelectedRetainedDraft.Should().BeNull();
        view.CanApproveRetainedDraft.Should().BeFalse();
        view.CanPostRetainedDraft.Should().BeFalse();
    }

    private static async Task<(FactorPaydownViewModel View, Client Client)> SubmittedView(ManualJournalEntryStatusDto status = ManualJournalEntryStatusDto.Submitted)
    {
        var client = new Client();
        var view = Create(client);
        await view.PrepareCommand.ExecuteAsync(null);
        view.Confirmed = true;
        await view.CreateDraftCommand.ExecuteAsync(null);
        client.ServerDrafts[0] = client.ServerDrafts[0] with { Status = status };
        await view.ReloadRetainedDraftsCommand.ExecuteAsync(null);
        return (view, client);
    }

    private static AssetAccountingDraftCapabilitiesDto Capabilities(ManualJournalEntryDraftDto draft, AssetAccountingApprovalReceiptDto? approval = null)
        => new(1, draft.JournalEntryId, draft.Version, draft.FundProfileId, BookId, PeriodId, "candidate-fingerprint",
            draft.Status == ManualJournalEntryStatusDto.Submitted, draft.Status == ManualJournalEntryStatusDto.Approved,
            "Independent authorized reviewer required.", "Retained approver required.", approval, false, null, null, "independent-reviewer");

    private static AssetAccountingApprovalReceiptDto Receipt()
    {
        var bytes = Encoding.UTF8.GetBytes("{\"review\":\"retained independent approval\"}");
        return new("approval-1", "independent-reviewer", DateTimeOffset.UnixEpoch.AddDays(1), "candidate-fingerprint", "exact-approval-subject",
            Convert.ToBase64String(bytes), Sha256Digest.Compute(bytes));
    }

    private static PostedLedgerJournalEntryResultDto CanonicalJournal(Guid id)
        => new(id, BookId, default, PeriodId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 42,
            DateTimeOffset.UnixEpoch.AddDays(2), "canonical-idempotency");

    private static JournalEntryLifecycleActionResultDto ActionResult(ManualJournalEntryDraftDto draft, JournalEntryLifecycleActionDto action,
        PostedLedgerJournalEntryResultDto? posted = null)
    {
        var status = action == JournalEntryLifecycleActionDto.Approve ? ManualJournalEntryStatusDto.Approved : ManualJournalEntryStatusDto.Posted;
        var next = draft with { Status = status, Version = draft.Version + 1 };
        return new(next, new("transition-1", draft.Status, status, action, "independent-reviewer", DateTimeOffset.UnixEpoch.AddDays(1)), [], posted);
    }
}
