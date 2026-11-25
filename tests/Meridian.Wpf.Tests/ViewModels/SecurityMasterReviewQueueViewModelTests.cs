#if WINDOWS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Meridian.Contracts.Api;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Services.Services.SecurityMaster;
using Meridian.Wpf.ViewModels.SecurityMaster;
using Moq;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed class SecurityMasterReviewQueueViewModelTests
{
    private static readonly Guid SecurityA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid DraftRevision = Guid.Parse("d1111111-1111-1111-1111-111111111111");
    private static readonly Guid ApprovedRevision = Guid.Parse("a2222222-2222-2222-2222-222222222222");

    // ── Review inbox ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Inbox_LoadsDraftSubmittedAndApproved_AndShowsRowsAsServed()
    {
        IReadOnlyCollection<SecurityMasterRevisionStateDto>? requested = null;
        var client = new Mock<ISecurityMasterReviewQueueApiClient>();
        client.Setup(c => c.ListPendingRevisionsAsync(It.IsAny<IReadOnlyCollection<SecurityMasterRevisionStateDto>>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyCollection<SecurityMasterRevisionStateDto>, CancellationToken>((states, _) => requested = states)
            .ReturnsAsync(Pending(Revision(DraftRevision, SecurityMasterRevisionStateDto.Draft, 0.4), Revision(ApprovedRevision, SecurityMasterRevisionStateDto.Approved, 72)));
        var inbox = new SecurityMasterReviewInboxViewModel(client.Object);

        await inbox.RefreshCommand.ExecuteAsync(null);

        requested.Should().BeEquivalentTo(
            [SecurityMasterRevisionStateDto.Draft, SecurityMasterRevisionStateDto.Submitted, SecurityMasterRevisionStateDto.Approved],
            "the desktop shows the same population as the browser inbox");
        inbox.Rows.Should().HaveCount(2);
        inbox.Rows[0].Field.Should().Be("EconomicDefinition.Coupon");
        inbox.Rows[0].Waiting.Should().Be("under 1h");
        inbox.Rows[0].CanDiscard.Should().BeTrue();
        inbox.Rows[1].Waiting.Should().Be("3d");
        inbox.Rows[1].CanDiscard.Should().BeFalse("an approved revision is published, not discarded");
        inbox.DiscardCommand.CanExecute(inbox.Rows[1]).Should().BeFalse();
        inbox.StatusText.Should().Be("2 revision(s) awaiting action, oldest first.");
        inbox.IsEmpty.Should().BeFalse();
    }

    [Fact]
    public async Task Inbox_Discard_PostsTheRowsRevisionWithTheBrowserReason_AndReloads()
    {
        var client = new Mock<ISecurityMasterReviewQueueApiClient>();
        client.SetupSequence(c => c.ListPendingRevisionsAsync(It.IsAny<IReadOnlyCollection<SecurityMasterRevisionStateDto>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Pending(Revision(DraftRevision, SecurityMasterRevisionStateDto.Draft, 5)))
            .ReturnsAsync(Pending());
        client.Setup(c => c.DiscardRevisionAsync(SecurityA, DraftRevision, SecurityMasterReviewInboxViewModel.DiscardReason, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponse<SecurityMasterEditResultDto>.Ok(null!));
        var inbox = new SecurityMasterReviewInboxViewModel(client.Object);
        await inbox.RefreshCommand.ExecuteAsync(null);

        await inbox.DiscardCommand.ExecuteAsync(inbox.Rows[0]);

        client.Verify(c => c.DiscardRevisionAsync(SecurityA, DraftRevision, "Discarded from the review inbox.", It.IsAny<CancellationToken>()), Times.Once);
        inbox.Rows.Should().BeEmpty();
        inbox.IsEmpty.Should().BeTrue();
        inbox.StatusText.Should().Be("Discarded revision on EconomicDefinition.Coupon. No revisions are awaiting action.");
        inbox.HasError.Should().BeFalse();
    }

    [Fact]
    public async Task Inbox_RefusedDiscard_ShowsTheServerReason_AfterReloading()
    {
        var client = new Mock<ISecurityMasterReviewQueueApiClient>();
        client.Setup(c => c.ListPendingRevisionsAsync(It.IsAny<IReadOnlyCollection<SecurityMasterRevisionStateDto>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Pending(Revision(DraftRevision, SecurityMasterRevisionStateDto.Submitted, 5)));
        client.Setup(c => c.DiscardRevisionAsync(SecurityA, DraftRevision, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponse<SecurityMasterEditResultDto>.Fail(
                "{\"error\":\"revision-state-conflict\",\"message\":\"The bound approval workflow is already approved.\"}", 409));
        var inbox = new SecurityMasterReviewInboxViewModel(client.Object);
        await inbox.RefreshCommand.ExecuteAsync(null);

        await inbox.DiscardCommand.ExecuteAsync(inbox.Rows[0]);

        inbox.ErrorText.Should().Be("The bound approval workflow is already approved.");
        inbox.Rows.Should().HaveCount(1, "the inbox reloads to show the revision's current state");
    }

    [Fact]
    public async Task Inbox_ForbiddenLoad_SaysSo_AndOpenHandsTheRowToTheEditor()
    {
        SecurityMasterPendingRevisionRow? opened = null;
        var client = new Mock<ISecurityMasterReviewQueueApiClient>();
        client.Setup(c => c.ListPendingRevisionsAsync(It.IsAny<IReadOnlyCollection<SecurityMasterRevisionStateDto>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponse<IReadOnlyList<SecurityMasterPendingRevisionDto>>.Fail(string.Empty, 403));
        var inbox = new SecurityMasterReviewInboxViewModel(client.Object, row => { opened = row; return Task.CompletedTask; });

        await inbox.RefreshCommand.ExecuteAsync(null);
        inbox.ErrorText.Should().Be("You don't have permission to view the Security Master review inbox.");
        inbox.IsLoaded.Should().BeFalse();

        var row = SecurityMasterPendingRevisionRow.From(Revision(ApprovedRevision, SecurityMasterRevisionStateDto.Approved, 2));
        await inbox.OpenCommand.ExecuteAsync(row);
        opened.Should().Be(row);
    }

    // ── Held amendments ───────────────────────────────────────────────────────

    [Fact]
    public async Task HeldAmendments_LoadHeldRows_AndDiscardOnlyHeldOnes()
    {
        var heldId = Guid.Parse("b1111111-1111-1111-1111-111111111111");
        var client = new Mock<ISecurityMasterReviewQueueApiClient>();
        client.SetupSequence(c => c.ListHeldAmendmentsAsync(null, SecurityMasterHeldAmendmentStatusDto.Held, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponse<IReadOnlyList<SecurityMasterHeldAmendmentDto>>.Ok([Held(heldId, SecurityMasterHeldAmendmentStatusDto.Held)]))
            .ReturnsAsync(ApiResponse<IReadOnlyList<SecurityMasterHeldAmendmentDto>>.Ok([]));
        client.Setup(c => c.DiscardHeldAmendmentAsync(heldId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponse<SecurityMasterQueueAck>.Ok(new SecurityMasterQueueAck(204), 204));
        Guid? openedSecurity = null;
        var held = new SecurityMasterHeldAmendmentsViewModel(client.Object, id => { openedSecurity = id; return Task.CompletedTask; });

        await held.RefreshCommand.ExecuteAsync(null);
        held.Rows.Should().ContainSingle();
        var row = held.Rows[0];
        row.SourceSystem.Should().Be("vendor-b");
        row.DominatedFields.Should().Be("EconomicDefinition.Coupon, Identifiers.Isin");
        row.ConflictCount.Should().Be(2);
        row.HeldAt.Should().Be("2026-11-24 09:30 UTC");
        row.CanDiscard.Should().BeTrue();

        await held.OpenSecurityCommand.ExecuteAsync(row);
        openedSecurity.Should().Be(SecurityA);

        await held.DiscardCommand.ExecuteAsync(row);
        held.Rows.Should().BeEmpty();
        held.StatusText.Should().Be("Discarded the held amendment from vendor-b. No held amendments.");

        SecurityMasterHeldAmendmentRow.From(Held(heldId, SecurityMasterHeldAmendmentStatusDto.Discarded)).CanDiscard.Should().BeFalse();
    }

    [Fact]
    public async Task HeldAmendments_FilterChangeClearsRows_AndConflictNamesTheServerReason()
    {
        var heldId = Guid.Parse("b2222222-2222-2222-2222-222222222222");
        var client = new Mock<ISecurityMasterReviewQueueApiClient>();
        client.Setup(c => c.ListHeldAmendmentsAsync(null, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponse<IReadOnlyList<SecurityMasterHeldAmendmentDto>>.Ok([Held(heldId, SecurityMasterHeldAmendmentStatusDto.Held)]));
        client.Setup(c => c.DiscardHeldAmendmentAsync(heldId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponse<SecurityMasterQueueAck>.Fail($"{{\"error\":\"Held amendment '{heldId:D}' is no longer held.\"}}", 409));
        var held = new SecurityMasterHeldAmendmentsViewModel(client.Object);
        await held.RefreshCommand.ExecuteAsync(null);

        await held.DiscardCommand.ExecuteAsync(held.Rows[0]);
        held.ErrorText.Should().Be($"Held amendment '{heldId:D}' is no longer held.");

        held.StatusFilter = SecurityMasterHeldAmendmentStatusDto.Discarded;
        held.Rows.Should().BeEmpty("a changed filter never shows the previous population");
        held.IsLoaded.Should().BeFalse();
        await held.RefreshCommand.ExecuteAsync(null);
        client.Verify(c => c.ListHeldAmendmentsAsync(null, "Discarded", It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── Exception queue ───────────────────────────────────────────────────────

    [Fact]
    public async Task ExceptionQueue_ShowsTheServersSlaVerdict_AndCountsBreaches()
    {
        var client = new Mock<ISecurityMasterReviewQueueApiClient>();
        client.Setup(c => c.ListExceptionCasesAsync(SecurityMasterExceptionCaseStatusDto.Open, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Cases(Case(Guid.NewGuid(), breached: true, assignee: null), Case(Guid.NewGuid(), breached: false, assignee: "bob.ops", hasSecurity: false)));
        var queue = new SecurityMasterExceptionQueueViewModel(client.Object);

        await queue.RefreshCommand.ExecuteAsync(null);

        queue.Rows.Should().HaveCount(2);
        queue.Rows[0].Sla.Should().Be("Breached (due 2026-11-24 12:00 UTC)");
        queue.Rows[0].Assignee.Should().Be("Unassigned");
        queue.Rows[0].Age.Should().Be("30h");
        queue.Rows[1].Sla.Should().Be("Due 2026-11-24 12:00 UTC", "the desktop shows the server's verdict, not a local clock comparison");
        queue.Rows[1].HasSecurity.Should().BeFalse();
        queue.BreachedCount.Should().Be(1);
        queue.StatusText.Should().Be("2 open case(s); 1 past SLA.");
    }

    [Fact]
    public async Task ExceptionQueue_Sync_ReportsCounts_ThenReloads()
    {
        var client = new Mock<ISecurityMasterReviewQueueApiClient>();
        client.Setup(c => c.SyncExceptionCasesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponse<SecurityMasterExceptionSyncResultDto>.Ok(new(3, 4, 1)));
        client.Setup(c => c.ListExceptionCasesAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Cases());
        var queue = new SecurityMasterExceptionQueueViewModel(client.Object);

        await queue.SyncCommand.ExecuteAsync(null);

        queue.StatusText.Should().Be("Sync opened 3, refreshed 4 and closed 1 case(s). No open cases.");
        client.Verify(c => c.ListExceptionCasesAsync("Open", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExceptionQueue_Assign_RequiresAnOpenSelectedCaseAndAnAssignee()
    {
        var caseId = Guid.Parse("c1111111-1111-1111-1111-111111111111");
        var client = new Mock<ISecurityMasterReviewQueueApiClient>();
        client.Setup(c => c.ListExceptionCasesAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Cases(Case(caseId, breached: false, assignee: null)));
        client.Setup(c => c.AssignExceptionCaseAsync(caseId, "carol.ops", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponse<SecurityMasterQueueAck>.Ok(new SecurityMasterQueueAck(204), 204));
        var queue = new SecurityMasterExceptionQueueViewModel(client.Object, defaultAssignee: " carol.ops ");
        await queue.RefreshCommand.ExecuteAsync(null);

        queue.Assignee.Should().Be("carol.ops", "the session actor pre-fills the assignee");
        queue.AssignCommand.CanExecute(null).Should().BeFalse("no case is selected");
        queue.SelectedCase = queue.Rows[0];
        queue.AssignCommand.CanExecute(null).Should().BeTrue();
        queue.Assignee = " ";
        queue.AssignCommand.CanExecute(null).Should().BeFalse("an assignee is required");
        queue.Assignee = "carol.ops";

        await queue.AssignCommand.ExecuteAsync(null);

        client.Verify(c => c.AssignExceptionCaseAsync(caseId, "carol.ops", It.IsAny<CancellationToken>()), Times.Once);
        queue.StatusText.Should().StartWith("Assigned \"Conflicting coupon\" to carol.ops.");
        queue.SelectedCase!.CaseId.Should().Be(caseId, "the selection survives the reload");
    }

    [Fact]
    public async Task ExceptionQueue_AssignRefusedBecauseCaseClosed_ShowsTheServerReason()
    {
        var caseId = Guid.Parse("c2222222-2222-2222-2222-222222222222");
        var client = new Mock<ISecurityMasterReviewQueueApiClient>();
        client.Setup(c => c.ListExceptionCasesAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Cases(Case(caseId, breached: false, assignee: null)));
        client.Setup(c => c.AssignExceptionCaseAsync(caseId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponse<SecurityMasterQueueAck>.Fail($"{{\"error\":\"Case '{caseId:D}' is not open.\"}}", 409));
        var queue = new SecurityMasterExceptionQueueViewModel(client.Object, defaultAssignee: "carol.ops");
        await queue.RefreshCommand.ExecuteAsync(null);
        queue.SelectedCase = queue.Rows.Single();

        await queue.AssignCommand.ExecuteAsync(null);

        queue.ErrorText.Should().Be($"Case '{caseId:D}' is not open.");
    }

    // ── Fixtures ──────────────────────────────────────────────────────────────

    private static ApiResponse<IReadOnlyList<SecurityMasterPendingRevisionDto>> Pending(params SecurityMasterPendingRevisionDto[] rows)
        => ApiResponse<IReadOnlyList<SecurityMasterPendingRevisionDto>>.Ok(rows);

    private static SecurityMasterPendingRevisionDto Revision(Guid revisionId, SecurityMasterRevisionStateDto state, double ageHours)
        => new(revisionId, SecurityA, state, "alice.ops", new DateTimeOffset(2026, 11, 20, 9, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 11, 21, 9, 0, 0, TimeSpan.Zero), "EconomicDefinition.Coupon", "5.25", "Prospectus supplement",
            null, 7, ageHours);

    private static SecurityMasterHeldAmendmentDto Held(Guid heldId, string status)
        => new(heldId, SecurityA, "vendor-b", "{}", ["EconomicDefinition.Coupon", "Identifiers.Isin"], [Guid.NewGuid(), Guid.NewGuid()], 7,
            new DateTimeOffset(2026, 11, 24, 9, 30, 0, TimeSpan.Zero), status);

    private static ApiResponse<IReadOnlyList<SecurityMasterExceptionCaseQueueItemDto>> Cases(params SecurityMasterExceptionCaseQueueItemDto[] rows)
        => ApiResponse<IReadOnlyList<SecurityMasterExceptionCaseQueueItemDto>>.Ok(rows);

    private static SecurityMasterExceptionCaseQueueItemDto Case(Guid caseId, bool breached, string? assignee, bool hasSecurity = true)
        => new(new SecurityMasterExceptionCaseDto(caseId, "conflict:" + caseId.ToString("N"), "Conflict", "SM-CONFLICT", hasSecurity ? SecurityA : null,
                "High", "Conflicting coupon", "vendor-a and vendor-b disagree on the coupon.", SecurityMasterExceptionCaseStatusDto.Open, assignee,
                new DateTimeOffset(2026, 11, 23, 6, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 11, 24, 6, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 11, 24, 12, 0, 0, TimeSpan.Zero), null, null),
            breached, 30);
}
#endif
