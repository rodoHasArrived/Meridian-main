using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Api;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Tenancy;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.Ledger;
using Meridian.Identity.Auth;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Meridian.Ui.Shared.Evidence;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Meridian.Tests.Ui;

public sealed partial class WorkstationEndpointsTests
{
    [Fact]
    public async Task AssetAccountingApproval_AuthenticatedIndependentCommandRetainsCanonicalReceiptAndPostsOnlyThroughCanonicalService()
    {
        using var fixture = await AssetApprovalFixture.CreateAsync();
        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton<IManualJournalEntryWorkbenchService>(fixture.Workbench);
            services.AddSingleton<IManualJournalEntryLifecycleService>(fixture.Workbench);
        }, currentUserPermissions: UserPermission.AdminMaintenance, currentUserCompanyId: AssetApprovalFixture.Company,
            currentUserName: AssetApprovalFixture.Reviewer, mapLedgerApi: true);
        var command = fixture.Action(JournalEntryLifecycleActionDto.Approve) with
        { Actor = AssetApprovalFixture.Reviewer, TenantId = "spoofed-tenant", CompanyId = "spoofed-company" };

        using var response = await app.GetTestClient().PostAsJsonAsync(UiApiRoutes.LedgerManualJournalEntryLifecycleAction, command, ServerJsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var approved = (await fixture.ReadAsync())!;
        approved.Status.Should().Be(ManualJournalEntryStatusDto.Approved);
        approved.AssetAccountingApproval.Should().NotBeNull();
        var approval = approved.AssetAccountingApproval!;
        approval.Approver.Should().Be(AssetApprovalFixture.Reviewer);
        approval.CandidateFingerprint.Should().Be(fixture.Retained.Spine.DraftedCandidateFingerprint);
        var receiptBytes = Convert.FromBase64String(approval.CanonicalContentBase64);
        Sha256Digest.Compute(receiptBytes).Should().Be(approval.ContentHashSha256);
        Encoding.UTF8.GetString(receiptBytes).Should().Contain(AssetApprovalFixture.Reviewer)
            .And.Contain(fixture.Retained.Spine.EventId.ToString("D"))
            .And.Contain(fixture.Retained.Spine.DraftedCandidateFingerprint!);
        var post = fixture.Action(JournalEntryLifecycleActionDto.Post, approved);

        var posted = await fixture.Workbench.ApplyLifecycleActionAsync(post);
        var retry = await fixture.Restart().ApplyLifecycleActionAsync(post);

        posted.JournalEntry.Status.Should().Be(ManualJournalEntryStatusDto.Posted);
        retry.JournalEntry.JournalEntryId.Should().Be(posted.JournalEntry.JournalEntryId);
        retry.JournalEntry.AssetAccountingApproval.Should().BeEquivalentTo(approval);
        fixture.PostRequests.Should().NotBeEmpty();
        foreach (var request in fixture.PostRequests)
        {
            request.Candidate.Should().BeEquivalentTo(fixture.Retained.Spine.DraftedCandidate);
            request.Candidate.AssetLotMutation!.FactorPaydown.Should().BeEquivalentTo(fixture.Retained.Spine.FactorPaydown);
            request.Actor.Should().Be(approval.Approver);
            request.ApprovalId.Should().Be(approval.ApprovalId);
            var evidence = request.ApprovalEvidence.Should().ContainSingle().Subject;
            evidence.ReviewedBy.Should().Be(approval.Approver);
            evidence.ContentHashSha256.Should().Be(approval.ContentHashSha256);
            evidence.SubjectId.Should().Be(approval.SubjectId);
            evidence.SubjectType.Should().Be(AssetAccountingEvidenceSubjects.PostingApproval);
        }
        fixture.PostRequests.Select(request => AssetAccountingEventSpineValidator.CanonicalPayloadFingerprint(request)).Distinct().Should().ContainSingle();
        await fixture.Ledger.DidNotReceive().AppendAsync(Arg.Any<LedgerJournalEntryWrite>(), Arg.Any<CancellationToken>());
        fixture.Published.Should().ContainSingle("the exact approval receipt is reused on retry");
        fixture.Published[0].TenantId.Should().Be(AssetApprovalFixture.Company);
        fixture.Published[0].Scope.Should().Be(AssetApprovalFixture.Company);
        Convert.FromBase64String(fixture.Published[0].ContentBase64!).Should().Equal(receiptBytes);
    }

    [Fact]
    public async Task AssetAccountingApproval_NonCertificationPrincipalCannotApprove()
    {
        using var fixture = await AssetApprovalFixture.CreateAsync();
        await using var app = await CreateAppAsync(services =>
            services.AddSingleton<IManualJournalEntryLifecycleService>(fixture.Workbench),
            currentUserPermissions: UserPermission.ManageLedgerReports, currentUserCompanyId: AssetApprovalFixture.Company,
            currentUserName: AssetApprovalFixture.Reviewer, mapLedgerApi: true);

        using var response = await app.GetTestClient().PostAsJsonAsync(UiApiRoutes.LedgerManualJournalEntryLifecycleAction,
            fixture.Action(JournalEntryLifecycleActionDto.Approve), ServerJsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await fixture.ReadAsync())!.AssetAccountingApproval.Should().BeNull();
        fixture.Published.Should().BeEmpty();
        fixture.PostRequests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("preparer")]
    [InlineData("stale-version")]
    [InlineData("foreign-scope")]
    [InlineData("source-drift")]
    public async Task AssetAccountingApproval_RejectsUnauthoritativeApprovalWithoutRetainingReceipt(string reason)
    {
        using var fixture = await AssetApprovalFixture.CreateAsync();
        var command = fixture.Action(JournalEntryLifecycleActionDto.Approve);
        if (reason == "preparer")
            command = command with { Actor = "ops-user" };
        if (reason == "stale-version")
            command = command with { Version = command.Version - 1 };
        if (reason == "foreign-scope")
            command = command with { TenantId = "other-company", CompanyId = "other-company" };
        if (reason == "source-drift")
            fixture.SourceDrift = true;

        var approve = () => fixture.Workbench.ApplyLifecycleActionAsync(command);

        await approve.Should().ThrowAsync<InvalidOperationException>();
        (await fixture.ReadAsync())!.AssetAccountingApproval.Should().BeNull();
        fixture.Published.Should().BeEmpty();
        fixture.PostRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task AssetAccountingApproval_ExactApproveRetryAfterRestartReturnsSameBytesAndTimestamp()
    {
        using var fixture = await AssetApprovalFixture.CreateAsync();
        var command = fixture.Action(JournalEntryLifecycleActionDto.Approve);

        var first = await fixture.Workbench.ApplyLifecycleActionAsync(command);
        var second = await fixture.Restart().ApplyLifecycleActionAsync(command);

        second.JournalEntry.AssetAccountingApproval.Should().BeEquivalentTo(first.JournalEntry.AssetAccountingApproval);
        second.JournalEntry.Version.Should().Be(first.JournalEntry.Version);
        fixture.Published.Should().BeEmpty("approval records the authenticated decision without requiring a publication side effect");
    }

    [Theory]
    [InlineData("different-poster")]
    [InlineData("tampered-bytes")]
    [InlineData("unavailable-bytes")]
    [InlineData("foreign-artifact")]
    public async Task AssetAccountingApproval_PostRefusesUnverifiedReceiptBeforeCanonicalPosting(string reason)
    {
        using var fixture = await AssetApprovalFixture.CreateAsync();
        var approved = (await fixture.Workbench.ApplyLifecycleActionAsync(fixture.Action(JournalEntryLifecycleActionDto.Approve))).JournalEntry;
        fixture.TamperOpen = reason == "tampered-bytes";
        fixture.OpenUnavailable = reason == "unavailable-bytes";
        fixture.ForeignIdentity = reason == "foreign-artifact";
        var command = fixture.Action(JournalEntryLifecycleActionDto.Post, approved);
        if (reason == "different-poster")
            command = command with { Actor = "another-independent-controller" };

        var post = () => fixture.Workbench.ApplyLifecycleActionAsync(command);

        await post.Should().ThrowAsync<InvalidOperationException>();
        fixture.PostRequests.Should().BeEmpty();
        (await fixture.ReadAsync())!.Status.Should().Be(ManualJournalEntryStatusDto.Approved);
    }

    [Fact]
    public async Task AssetAccountingApproval_PublicationResponseFailureReusesVerifiedArtifactOnRetry()
    {
        using var fixture = await AssetApprovalFixture.CreateAsync();
        var approved = (await fixture.Workbench.ApplyLifecycleActionAsync(fixture.Action(JournalEntryLifecycleActionDto.Approve))).JournalEntry;
        fixture.FailPublicationResponse = true;
        var command = fixture.Action(JournalEntryLifecycleActionDto.Post, approved);
        var interrupted = () => fixture.Workbench.ApplyLifecycleActionAsync(command);

        await interrupted.Should().ThrowAsync<IOException>();
        fixture.PostRequests.Should().BeEmpty();
        var posted = await fixture.Restart().ApplyLifecycleActionAsync(command);

        posted.JournalEntry.Status.Should().Be(ManualJournalEntryStatusDto.Posted);
        fixture.Published.Should().ContainSingle("publication already committed the exact scoped receipt bytes");
        fixture.PostRequests.Should().NotBeEmpty();
    }

    [Fact]
    public async Task AssetAccountingApproval_CanonicalCommitResponseFailureRecoversExactIntentAfterRestart()
    {
        using var fixture = await AssetApprovalFixture.CreateAsync();
        var approved = (await fixture.Workbench.ApplyLifecycleActionAsync(fixture.Action(JournalEntryLifecycleActionDto.Approve))).JournalEntry;
        fixture.FailCanonicalResponse = true;
        var command = fixture.Action(JournalEntryLifecycleActionDto.Post, approved);
        var interrupted = () => fixture.Workbench.ApplyLifecycleActionAsync(command);

        await interrupted.Should().ThrowAsync<IOException>();
        fixture.Committed.Should().NotBeNull("the canonical atomic posting completed before its response was lost");
        var posted = await fixture.Restart().ApplyLifecycleActionAsync(command);

        posted.JournalEntry.Status.Should().Be(ManualJournalEntryStatusDto.Posted);
        posted.PostedJournal!.JournalEntryId.Should().Be(fixture.Committed!.PostedJournal.JournalEntryId);
        fixture.PostRequests.Should().HaveCountGreaterThanOrEqualTo(2);
        fixture.PostRequests.Select(request => AssetAccountingEventSpineValidator.CanonicalPayloadFingerprint(request)).Distinct().Should().ContainSingle();
        await fixture.Ledger.DidNotReceive().AppendAsync(Arg.Any<LedgerJournalEntryWrite>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AssetAccountingApproval_MissingSourceAuthorityCannotGrantApproval()
    {
        using var fixture = await AssetApprovalFixture.CreateAsync();

        var approve = () => fixture.Restart(includeSource: false).ApplyLifecycleActionAsync(fixture.Action(JournalEntryLifecycleActionDto.Approve));

        await approve.Should().ThrowAsync<InvalidOperationException>();
        (await fixture.ReadAsync())!.AssetAccountingApproval.Should().BeNull();
        fixture.Published.Should().BeEmpty();
    }

    [Fact]
    public async Task AssetAccountingApproval_MissingVaultCannotPostAnApprovedDecision()
    {
        using var fixture = await AssetApprovalFixture.CreateAsync();
        var approved = (await fixture.Workbench.ApplyLifecycleActionAsync(fixture.Action(JournalEntryLifecycleActionDto.Approve))).JournalEntry;

        var post = () => fixture.Restart(includeVault: false).ApplyLifecycleActionAsync(fixture.Action(JournalEntryLifecycleActionDto.Post, approved));

        await post.Should().ThrowAsync<InvalidOperationException>();
        fixture.PostRequests.Should().BeEmpty();
        (await fixture.ReadAsync())!.Status.Should().Be(ManualJournalEntryStatusDto.Approved);
    }

    [Fact]
    public async Task AssetAccountingApproval_PeriodVersionDriftCannotGrantApproval()
    {
        using var fixture = await AssetApprovalFixture.CreateAsync();
        var periodId = fixture.Retained.Spine.Scope.PeriodId;
        var period = await fixture.Ledger.GetPeriodAsync(periodId);
        fixture.Ledger.GetPeriodAsync(periodId, Arg.Any<CancellationToken>()).Returns(period! with { Version = period.Version + 1 });

        var approve = () => fixture.Workbench.ApplyLifecycleActionAsync(fixture.Action(JournalEntryLifecycleActionDto.Approve));

        await approve.Should().ThrowAsync<InvalidOperationException>();
        (await fixture.ReadAsync())!.AssetAccountingApproval.Should().BeNull();
        fixture.Published.Should().BeEmpty();
    }

    [Fact]
    public async Task AssetAccountingApproval_ChangedRetainedReceiptBytesCannotBecomePostingEvidence()
    {
        using var fixture = await AssetApprovalFixture.CreateAsync();
        var approved = (await fixture.Workbench.ApplyLifecycleActionAsync(fixture.Action(JournalEntryLifecycleActionDto.Approve))).JournalEntry;
        await fixture.SaveRawAsync(approved with
        {
            AssetAccountingApproval = approved.AssetAccountingApproval! with
            { CanonicalContentBase64 = Convert.ToBase64String("forged approval bytes"u8.ToArray()) }
        });

        var post = () => fixture.Workbench.ApplyLifecycleActionAsync(fixture.Action(JournalEntryLifecycleActionDto.Post, approved));

        await post.Should().ThrowAsync<InvalidOperationException>();
        fixture.Published.Should().BeEmpty();
        fixture.PostRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task AssetAccountingApproval_PendingCanonicalIntentRefusesOtherMutationsUntilExactRetry()
    {
        using var fixture = await AssetApprovalFixture.CreateAsync();
        var approved = (await fixture.Workbench.ApplyLifecycleActionAsync(fixture.Action(JournalEntryLifecycleActionDto.Approve))).JournalEntry;
        fixture.FailCanonicalResponse = true;
        var original = fixture.Action(JournalEntryLifecycleActionDto.Post, approved);
        var interrupted = () => fixture.Workbench.ApplyLifecycleActionAsync(original);
        await interrupted.Should().ThrowAsync<IOException>();
        var pending = (await fixture.ReadAsync())!;
        pending.AssetAccountingPostIntent.Should().NotBeNull();
        pending.Status.Should().Be(ManualJournalEntryStatusDto.Approved);
        foreach (var action in new[] { JournalEntryLifecycleActionDto.Validate, JournalEntryLifecycleActionDto.Submit, JournalEntryLifecycleActionDto.Reject })
        {
            var mutation = () => fixture.Restart().ApplyLifecycleActionAsync(fixture.Action(action, pending));
            await mutation.Should().ThrowAsync<InvalidOperationException>();
        }
        var exact = await fixture.Restart().ApplyLifecycleActionAsync(original);
        exact.JournalEntry.Status.Should().Be(ManualJournalEntryStatusDto.Posted);
    }

    [Fact]
    public async Task AssetAccountingApproval_CompletedReceiptCannotRecreateAMissingCanonicalJournal()
    {
        using var fixture = await AssetApprovalFixture.CreateAsync();
        var approved = (await fixture.Workbench.ApplyLifecycleActionAsync(fixture.Action(JournalEntryLifecycleActionDto.Approve))).JournalEntry;
        var command = fixture.Action(JournalEntryLifecycleActionDto.Post, approved);
        await fixture.Workbench.ApplyLifecycleActionAsync(command);
        var before = fixture.PostRequests.Count;
        fixture.MissingCommittedJournal = true;

        var retry = () => fixture.Restart().ApplyLifecycleActionAsync(command);

        await retry.Should().ThrowAsync<InvalidOperationException>();
        fixture.PostRequests.Should().HaveCount(before, "recovery must refuse missing financial facts before invoking a potentially publishing target");
    }

    [Theory]
    [InlineData(UserPermission.AdminMaintenance, true)]
    [InlineData(UserPermission.ManageLedgerReports, false)]
    public async Task AssetAccountingApproval_CapabilityEndpointBindsCallerAuthorityToExactRetainedDraft(UserPermission permission, bool canApprove)
    {
        using var fixture = await AssetApprovalFixture.CreateAsync();
        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton(fixture.Books);
            services.AddSingleton<IManualJournalEntryWorkbenchService>(fixture.Workbench);
        }, currentUserPermissions: permission, currentUserCompanyId: AssetApprovalFixture.Company,
            currentUserName: AssetApprovalFixture.Reviewer, mapLedgerApi: true);

        using var response = await app.GetTestClient().GetAsync(fixture.CapabilityRoute());

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var capability = json.RootElement;
        capability.GetProperty("protocolVersion").GetInt32().Should().Be(1);
        capability.GetProperty("authenticatedActor").GetString().Should().Be(AssetApprovalFixture.Reviewer);
        capability.GetProperty("journalEntryId").GetGuid().Should().Be(fixture.Submitted.JournalEntryId);
        capability.GetProperty("draftVersion").GetInt32().Should().Be(fixture.Submitted.Version);
        capability.GetProperty("ledgerBookId").GetGuid().Should().Be(fixture.Retained.Spine.Scope.LedgerBookId);
        capability.GetProperty("periodId").GetGuid().Should().Be(fixture.Retained.Spine.Scope.PeriodId);
        capability.GetProperty("candidateFingerprint").GetString().Should().Be(fixture.Retained.Spine.DraftedCandidateFingerprint);
        capability.GetProperty("canApprove").GetBoolean().Should().Be(canApprove);
        capability.GetProperty("canPost").GetBoolean().Should().BeFalse("this exact retained draft has not been independently approved");
    }

    [Theory]
    [InlineData("foreign-book")]
    [InlineData("foreign-fund")]
    [InlineData("foreign-owner")]
    public async Task AssetAccountingApproval_CapabilityEndpointRejectsForeignOwnershipBeforeWorkbenchOrEvidenceReads(string scenario)
    {
        using var fixture = await AssetApprovalFixture.CreateAsync();
        fixture.Configuration.ClearReceivedCalls();
        var owner = Substitute.For<IFundProfileTenancyRegistry>();
        owner.ResolveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new FundProfileOwnership(
            fixture.Retained.Spine.Scope.FundProfileId, "foreign-company", "foreign-company"));
        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton(fixture.Books);
            services.AddSingleton<IManualJournalEntryWorkbenchService>(fixture.Workbench);
            if (scenario == "foreign-owner")
                services.AddSingleton(owner);
        }, currentUserPermissions: UserPermission.AdminMaintenance, currentUserCompanyId: AssetApprovalFixture.Company,
            currentUserName: AssetApprovalFixture.Reviewer, mapLedgerApi: true);
        var route = fixture.CapabilityRoute(
            scenario == "foreign-book" ? Guid.NewGuid() : null,
            scenario == "foreign-fund" ? "foreign-fund" : null);

        using var response = await app.GetTestClient().GetAsync(route);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        fixture.Configuration.ReceivedCalls().Should().BeEmpty("foreign ownership must fail before loading the workbench");
        fixture.Published.Should().BeEmpty();
        fixture.PostRequests.Should().BeEmpty();
    }

    [Theory]
    [InlineData(AssetApprovalFixture.Reviewer, true)]
    [InlineData("another-independent-controller", false)]
    public async Task AssetAccountingApproval_PendingCapabilitiesExposeOriginalPostCommandOnlyToItsAuthenticatedOwner(string actor, bool canPost)
    {
        using var fixture = await AssetApprovalFixture.CreateAsync();
        var approved = (await fixture.Workbench.ApplyLifecycleActionAsync(fixture.Action(JournalEntryLifecycleActionDto.Approve))).JournalEntry;
        var original = fixture.Action(JournalEntryLifecycleActionDto.Post, approved);
        fixture.FailCanonicalResponse = true;
        var interrupted = () => fixture.Workbench.ApplyLifecycleActionAsync(original);
        await interrupted.Should().ThrowAsync<IOException>();
        var pending = (await fixture.ReadAsync())!;
        pending.AssetAccountingPostIntent!.OriginalCommandHash.Should().NotBeNullOrWhiteSpace();
        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton(fixture.Books);
            services.AddSingleton<IManualJournalEntryWorkbenchService>(fixture.Restart());
        }, currentUserPermissions: UserPermission.AdminMaintenance, currentUserCompanyId: AssetApprovalFixture.Company,
            currentUserName: actor, mapLedgerApi: true);

        using var response = await app.GetTestClient().GetAsync(fixture.CapabilityRoute());

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var capability = json.RootElement;
        capability.GetProperty("hasPendingPost").GetBoolean().Should().BeTrue();
        capability.GetProperty("canApprove").GetBoolean().Should().BeFalse();
        capability.GetProperty("canPost").GetBoolean().Should().Be(canPost);
        var command = capability.GetProperty("pendingPostCommand");
        if (canPost)
        {
            command.GetProperty("version").GetInt32().Should().Be(original.Version);
            command.GetProperty("version").GetInt32().Should().BeLessThan(pending.Version,
                "retry must preserve the original command version rather than use the checkpoint version");
            command.GetProperty("correlationId").GetString().Should().Be(original.CorrelationId);
            command.GetProperty("actor").GetString().Should().Be(original.Actor);
        }
        else
            command.ValueKind.Should().Be(JsonValueKind.Null,
            "another independent operator cannot execute or receive the retained posting command");
    }

    [Fact]
    public async Task AssetAccountingApproval_CapabilitiesRemainBlockedWhenLegacyCompositionHasNoAssetSource()
    {
        using var fixture = await AssetApprovalFixture.CreateAsync();
        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton(fixture.Books);
            services.AddSingleton<IManualJournalEntryWorkbenchService>(fixture.Restart(includeSource: false));
        }, currentUserPermissions: UserPermission.AdminMaintenance, currentUserCompanyId: AssetApprovalFixture.Company,
            currentUserName: AssetApprovalFixture.Reviewer, mapLedgerApi: true);

        using var response = await app.GetTestClient().GetAsync(fixture.CapabilityRoute());

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("canApprove").GetBoolean().Should().BeFalse();
        json.RootElement.GetProperty("approveBlockedReason").GetString().Should().Contain("unavailable");
        json.RootElement.GetProperty("canPost").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task AssetAccountingApproval_FinalWorkbenchSaveFailureRecoversOnlyTheCommittedCanonicalOutcome()
    {
        using var fixture = await AssetApprovalFixture.CreateAsync();
        var approved = (await fixture.Workbench.ApplyLifecycleActionAsync(fixture.Action(JournalEntryLifecycleActionDto.Approve))).JournalEntry;
        var original = fixture.Action(JournalEntryLifecycleActionDto.Post, approved);
        fixture.FailFinalMirrorSave = true;
        var interrupted = () => fixture.Workbench.ApplyLifecycleActionAsync(original);

        await interrupted.Should().ThrowAsync<IOException>();
        fixture.Committed.Should().NotBeNull();
        (await fixture.ReadAsync())!.Status.Should().Be(ManualJournalEntryStatusDto.Approved);
        var recovered = await fixture.Restart().ApplyLifecycleActionAsync(original);

        recovered.JournalEntry.Status.Should().Be(ManualJournalEntryStatusDto.Posted);
        recovered.PostedJournal!.JournalEntryId.Should().Be(fixture.Committed!.PostedJournal.JournalEntryId);
        fixture.Published.Should().ContainSingle();
        fixture.PostRequests.Select(request => AssetAccountingEventSpineValidator.CanonicalPayloadFingerprint(request))
            .Distinct().Should().ContainSingle();
        await fixture.Ledger.DidNotReceive().AppendAsync(Arg.Any<LedgerJournalEntryWrite>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AssetAccountingApproval_RejectsCallerFinancialLinesBeforeGrantingApproval()
    {
        using var fixture = await AssetApprovalFixture.CreateAsync();
        var command = fixture.Action(JournalEntryLifecycleActionDto.Approve) with
        { RebookLines = [fixture.Submitted.Lines[0] with { Amount = 1m }] };

        var approve = () => fixture.Workbench.ApplyLifecycleActionAsync(command);

        await approve.Should().ThrowAsync<InvalidOperationException>();
        (await fixture.ReadAsync())!.AssetAccountingApproval.Should().BeNull();
        fixture.PostRequests.Should().BeEmpty();
    }

    [Theory]
    [InlineData(JournalEntryLifecycleActionDto.Approve)]
    [InlineData(JournalEntryLifecycleActionDto.Post)]
    public async Task AssetAccountingApproval_AuthenticatedActorSwitchRefusesStaleBrowserCommandWithoutSideEffects(JournalEntryLifecycleActionDto action)
    {
        using var fixture = await AssetApprovalFixture.CreateAsync();
        var before = fixture.Submitted;
        if (action == JournalEntryLifecycleActionDto.Post)
            before = (await fixture.Workbench.ApplyLifecycleActionAsync(fixture.Action(JournalEntryLifecycleActionDto.Approve))).JournalEntry;
        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton<IManualJournalEntryWorkbenchService>(fixture.Workbench);
            services.AddSingleton<IManualJournalEntryLifecycleService>(fixture.Workbench);
        }, currentUserPermissions: UserPermission.AdminMaintenance, currentUserCompanyId: AssetApprovalFixture.Company,
            currentUserName: AssetApprovalFixture.Reviewer, mapLedgerApi: true);
        var staleCommand = fixture.Action(action, before) with { Actor = "old-session-actor" };

        using var response = await app.GetTestClient().PostAsJsonAsync(UiApiRoutes.LedgerManualJournalEntryLifecycleAction,
            staleCommand, ServerJsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict, await response.Content.ReadAsStringAsync());
        var after = (await fixture.ReadAsync())!;
        after.Status.Should().Be(before.Status);
        after.Version.Should().Be(before.Version);
        after.AssetAccountingApproval.Should().BeEquivalentTo(before.AssetAccountingApproval);
        after.AssetAccountingPostIntent.Should().BeNull();
        fixture.Published.Should().BeEmpty();
        fixture.PostRequests.Should().BeEmpty();
    }

    private sealed class FinalMirrorFaultStore(IManualJournalEntryDraftStore inner,
        Func<IReadOnlyList<ManualJournalEntryDraftDto>, bool> fail) : IManualJournalEntryDraftStore
    {
        public Task<IReadOnlyList<string>> ListFundProfileIdsAsync(CancellationToken ct = default) => inner.ListFundProfileIdsAsync(ct);
        public Task<IReadOnlyList<ManualJournalEntryDraftDto>> ListAsync(string fundProfileId, Guid? ledgerBookId = null,
            CancellationToken ct = default, string? tenantId = null, string? companyId = null)
            => inner.ListAsync(fundProfileId, ledgerBookId, ct, tenantId, companyId);
        public Task<ManualJournalEntryDraftDto?> GetAsync(string fundProfileId, Guid journalEntryId,
            CancellationToken ct = default, string? tenantId = null, string? companyId = null)
            => inner.GetAsync(fundProfileId, journalEntryId, ct, tenantId, companyId);
        public Task SaveAsync(ManualJournalEntryDraftDto draft, CancellationToken ct = default) => SaveBatchAsync([draft], ct);
        public Task SaveBatchAsync(IReadOnlyList<ManualJournalEntryDraftDto> drafts, CancellationToken ct = default)
        {
            if (fail(drafts))
                throw new IOException("Canonical posting completed before the workbench mirror save failed.");
            return inner.SaveBatchAsync(drafts, ct);
        }
    }

    private sealed class AssetApprovalFixture : IDisposable
    {
        public const string Company = "company-alpha";
        public const string Reviewer = "independent-controller";
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "meridian-asset-approval-" + Guid.NewGuid().ToString("N"));
        private readonly FileManualJournalEntryDraftStore _drafts;
        private readonly IAccountingConfigurationService _configuration;
        private readonly IAccountingActionAuditStore _audit = new InMemoryAccountingActionAuditStore();
        private readonly FileEvidenceArtifactStore _vault;
        private readonly IEvidenceArtifactStore _evidence = Substitute.For<IEvidenceArtifactStore>();
        private readonly IAccountingPostingCandidatePostService _poster = Substitute.For<IAccountingPostingCandidatePostService>();
        private readonly IAssetAccountingEventSpineService _source = Substitute.For<IAssetAccountingEventSpineService>();
        public List<EvidenceVaultIntakeRequestDto> Published { get; } = [];
        public List<PostPostingRuleJournalCandidateRequestDto> PostRequests { get; } = [];
        public AssetAccountingPostingCandidateDto Retained { get; }
        public ILedgerJournalStore Ledger { get; }
        public ILedgerBookService Books { get; } = Substitute.For<ILedgerBookService>();
        public IAccountingConfigurationService Configuration => _configuration;
        public ManualJournalEntryWorkbenchService Workbench { get; }
        public ManualJournalEntryDraftDto Submitted { get; private set; } = null!;
        public PostedPostingRuleJournalCandidateResultDto? Committed { get; private set; }
        public bool SourceDrift { get; set; }
        public bool TamperOpen { get; set; }
        public bool OpenUnavailable { get; set; }
        public bool ForeignIdentity { get; set; }
        public bool FailPublicationResponse { get; set; }
        public bool FailCanonicalResponse { get; set; }
        public bool MissingCommittedJournal { get; set; }
        public bool FailFinalMirrorSave { get; set; }

        private AssetApprovalFixture(IAccountingConfigurationService configuration, ILedgerJournalStore ledger,
            AssetAccountingPostingCandidateDto retained)
        {
            Directory.CreateDirectory(_directory);
            _configuration = configuration;
            Ledger = Substitute.For<ILedgerJournalStore, ILedgerPostingIdentityCollisionLookup>();
            Ledger.GetPeriodAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(call =>
                ledger.GetPeriodAsync(call.Arg<Guid>(), call.Arg<CancellationToken>()));
            ((ILedgerPostingIdentityCollisionLookup)Ledger).FindPostingIdentityCollisionsAsync(
                Arg.Any<LedgerPostingIdentity>(), Arg.Any<CancellationToken>()).Returns(_ =>
                    Committed is null || MissingCommittedJournal ? [] : new[] { CommittedJournal() });
            Retained = retained;
            var scope = retained.Spine.Scope;
            Books.GetBookAsync(scope.LedgerBookId, Arg.Any<CancellationToken>()).Returns(new LedgerBookDto(
                scope.LedgerBookId, scope.FundProfileId, Guid.NewGuid(), FundStructureNodeKindDto.Fund,
                "Retained GAAP book", retained.Spine.Currency, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
                AccountingBasis: scope.AccountingBasis));
            _drafts = new FileManualJournalEntryDraftStore(Path.Combine(_directory, "drafts.json"));
            _vault = new FileEvidenceArtifactStore(_directory, NullLogger<FileEvidenceArtifactStore>.Instance);
            _source.GetLatestAsync(Arg.Any<Guid>(), Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns(_ =>
                SourceDrift ? retained.Spine with { Scope = retained.Spine.Scope with { ExpectedSecurityVersion = 999 } } : retained.Spine);
            _evidence.WriteIntakeArtifactAsync(Arg.Any<EvidenceVaultIntakeRequestDto>(), Arg.Any<CancellationToken>()).Returns(async call =>
            {
                var request = call.Arg<EvidenceVaultIntakeRequestDto>();
                Published.Add(request);
                var result = await _vault.WriteIntakeArtifactAsync(request, call.Arg<CancellationToken>());
                if (FailPublicationResponse)
                { FailPublicationResponse = false; throw new IOException("Publication committed before response failure."); }
                return ForeignIdentity ? result with { VaultIdentity = result.VaultIdentity with { TenantId = "other-company" } } : result;
            });
            _evidence.FindByLinkageAsync(Arg.Any<EvidenceVaultLookupRequestDto>(), Arg.Any<CancellationToken>()).Returns(async call =>
            {
                var found = await _vault.FindByLinkageAsync(call.Arg<EvidenceVaultLookupRequestDto>(), call.Arg<CancellationToken>());
                return ForeignIdentity ? found.Select(identity => identity with { TenantId = "other-company" }).ToArray() : found;
            });
            _evidence.TryGetVaultIdentityAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(async call =>
            {
                var identity = await _vault.TryGetVaultIdentityAsync(call.ArgAt<string>(0), call.ArgAt<string>(1), call.ArgAt<string>(2), call.Arg<CancellationToken>());
                return ForeignIdentity && identity is not null ? identity with { TenantId = "other-company" } : identity;
            });
            _evidence.VerifyRetainedContentAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call =>
                _vault.VerifyRetainedContentAsync(call.ArgAt<string>(0), call.ArgAt<string>(1), call.ArgAt<string>(2), call.Arg<CancellationToken>()));
            _evidence.TryOpenDocumentContentAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(async call =>
            {
                if (OpenUnavailable)
                    return null;
                var opened = await _vault.TryOpenDocumentContentAsync(call.ArgAt<string>(0), call.ArgAt<string>(1), call.ArgAt<string>(2), call.ArgAt<string>(3), call.Arg<CancellationToken>());
                if (!TamperOpen || opened is null)
                    return opened;
                await opened.Content.DisposeAsync();
                return opened with { Content = new MemoryStream("tampered approval receipt"u8.ToArray()) };
            });
            _poster.PostCandidateAsync(Arg.Any<PostPostingRuleJournalCandidateRequestDto>(), Arg.Any<CancellationToken>()).Returns(call =>
            {
                var request = call.Arg<PostPostingRuleJournalCandidateRequestDto>();
                PostRequests.Add(request);
                var replay = Committed is not null;
                Committed ??= BuildCanonicalPostResult(request, retained);
                if (FailCanonicalResponse)
                { FailCanonicalResponse = false; throw new IOException("Atomic canonical posting committed before response failure."); }
                return Task.FromResult(Committed with { WasReplay = replay });
            });
            Workbench = Restart();
        }

        public ManualJournalEntryWorkbenchService Restart(bool includeSource = true, bool includeVault = true) => new(
            new FinalMirrorFaultStore(new FileManualJournalEntryDraftStore(Path.Combine(_directory, "drafts.json")), drafts =>
            {
                if (!FailFinalMirrorSave || !drafts.Any(draft => draft.Status == ManualJournalEntryStatusDto.Posted))
                    return false;
                FailFinalMirrorSave = false;
                return true;
            }),
            _configuration, _audit, journalStore: Ledger,
            mutationRecovery: new FileManualJournalMutationRecoveryStore(Path.Combine(_directory, "mutations")),
            evidenceArtifacts: includeVault ? _evidence : null, assetCandidatePosting: _poster, assetAccountingSource: includeSource ? _source : null);

        public string CapabilityRoute(Guid? ledgerBookId = null, string? fundProfileId = null)
        {
            var path = UiApiRoutes.WithParam(UiApiRoutes.LedgerFactorPaydownDraftCapabilities,
                "ledgerBookId", (ledgerBookId ?? Retained.Spine.Scope.LedgerBookId).ToString("D"));
            path = UiApiRoutes.WithParam(path, "journalEntryId", Submitted.JournalEntryId.ToString("D"));
            return path + "?fundProfileId=" + Uri.EscapeDataString(fundProfileId ?? Retained.Spine.Scope.FundProfileId);
        }

        public Task SaveRawAsync(ManualJournalEntryDraftDto draft) => _drafts.SaveAsync(draft);

        public Task<ManualJournalEntryDraftDto?> ReadAsync() => _drafts.GetAsync(Retained.Spine.Scope.FundProfileId,
            Retained.Candidate.JournalEntryId!.Value, tenantId: Company, companyId: Company);

        public JournalEntryLifecycleActionRequestDto Action(JournalEntryLifecycleActionDto action, ManualJournalEntryDraftDto? draft = null)
        {
            draft ??= Submitted;
            return new(draft.JournalEntryId, draft.FundProfileId, action, Reviewer, draft.Version,
                Notes: "Reviewed the exact retained factor-paydown source and generated accounting effects.",
                CorrelationId: "asset-test-" + action, EvidenceLinks: draft.EvidenceLinks,
                LedgerBookId: draft.LedgerBookId, TenantId: Company, CompanyId: Company);
        }

        public static async Task<AssetApprovalFixture> CreateAsync()
        {
            var original = FactorPreparationFixture();
            var services = new ServiceCollection();
            original.Register(services);
            using var provider = services.BuildServiceProvider();
            var r = original.Request;
            var preparation = await original.Service.PrepareAsync(new FactorPaydownPreparationRequest(r.LedgerBookId,
                r.SecurityId, r.BookPositionId, r.PeriodId, r.ExpectedPeriodVersion, r.AccountingBasis, r.FundProfileId,
                r.EffectiveDate, r.PriorFactor, r.NewFactor, r.SourceDomain, r.SourceEntityId, r.SourceContentHash,
                r.OccurredAtUtc, r.FactorEvidence, r.CashAccountPath, r.CarryingAccountPath, r.RealizedGainAccountPath,
                r.RealizedLossAccountPath, Company, Company), "ops-user", DateTimeOffset.UtcNow);
            var retained = FactorDraftedCandidate(FactorProjectedSpine(preparation.ProjectRequest), "ops-user", "Reviewed paydown");
            var source = retained.Spine.DraftedCandidate! with
            {
                ExpectedPeriodVersion = r.ExpectedPeriodVersion,
                SourceEventId = retained.Spine.EventId,
                EconomicEvent = retained.Spine.EconomicEvent,
                ProjectionLineage = retained.Spine.ProjectionLineage,
                BookPositionId = r.BookPositionId
            };
            retained = retained with
            {
                Spine = retained.Spine with
                {
                    DraftedCandidate = source,
                    DraftedCandidateFingerprint = AssetAccountingEventSpineValidator.CanonicalPayloadFingerprint(source),
                    Stages = [new AssetAccountingStageEvidenceDto(AssetAccountingLifecycleStageDto.Drafted,
                    DateTimeOffset.UtcNow.AddMinutes(-1), "ops-user", retained.Spine.RetainedEvidence)]
                }
            };
            var fixture = new AssetApprovalFixture(provider.GetRequiredService<IAccountingConfigurationService>(), original.Periods, retained);
            var saved = await fixture.Workbench.SaveAssetAccountingDraftAsync(retained, "ops-user", CancellationToken.None);
            fixture.Submitted = await fixture.Workbench.SubmitApprovalAsync(new SubmitManualJournalEntryApprovalRequest(
                saved.JournalEntryId, saved.FundProfileId, "ops-user", saved.Version,
                CorrelationId: "asset-test-submit", LedgerBookId: saved.LedgerBookId, TenantId: Company, CompanyId: Company));
            return fixture;
        }

        private LedgerJournalEntryRecord CommittedJournal()
        {
            var posted = Committed!.PostedJournal;
            var at = posted.PostedAtUtc!.Value;
            var description = Retained.Spine.DraftedCandidate!.Description;
            var lines = Retained.Candidate.GeneratedPostingLines.Select(line => new LedgerEntry(Guid.NewGuid(),
                posted.JournalEntryId, at, new LedgerAccount(line.AccountPath, LedgerAccountType.Asset),
                line.Side == AccountingTemplateLineSideDto.Debit ? line.Amount : 0m,
                line.Side == AccountingTemplateLineSideDto.Credit ? line.Amount : 0m, description)).ToArray();
            var entry = new JournalEntry(posted.JournalEntryId, at, description, lines,
                new JournalEntryMetadata(LedgerBook: posted.LedgerBookId.ToString("D"), IdempotencyKey: posted.IdempotencyKey));
            return new(entry, posted.AggregateId, posted.PeriodId, posted.CommandId, posted.CorrelationId, 1, at,
                posted.AccountingBasis, SourceEventId: posted.SourceEventId);
        }

        private static PostedPostingRuleJournalCandidateResultDto BuildCanonicalPostResult(PostPostingRuleJournalCandidateRequestDto request,
            AssetAccountingPostingCandidateDto retained)
        {
            var now = DateTimeOffset.UtcNow;
            var scope = retained.Spine.Scope;
            var id = retained.Candidate.JournalEntryId!.Value;
            var lines = retained.Candidate.GeneratedPostingLines.Select(line => new PostedJournalImpactLineDto(Guid.NewGuid(),
                line.AccountPath, line.Side == AccountingTemplateLineSideDto.Debit ? line.Amount : 0m,
                line.Side == AccountingTemplateLineSideDto.Credit ? line.Amount : 0m, line.Currency, line.Description, line.Dimensions)).ToArray();
            return new(retained.Candidate, new PostedLedgerJournalEntryResultDto(id, scope.LedgerBookId, scope.AccountingBasis,
                scope.PeriodId, scope.LedgerBookId, Guid.NewGuid(), retained.Spine.EventId, null, PostedAtUtc: now))
            {
                TaxLotMutationBatchId = Guid.NewGuid(),
                JournalImpact = new PostedJournalImpactDto(id, scope.LedgerBookId, scope.PeriodId, scope.AccountingBasis,
                    now, JournalPostingStatusDto.Posted, retained.Spine.Currency,
                    retained.Candidate.TotalDebits, retained.Candidate.TotalCredits, lines),
                LifecycleStages = [new AssetAccountingStageEvidenceDto(AssetAccountingLifecycleStageDto.Approved,
                    request.ApprovalEvidence.Max(e => e.RetainedAtUtc > e.ReviewedAtUtc ? e.RetainedAtUtc : e.ReviewedAtUtc),
                    request.Actor, request.ApprovalEvidence, request.ApprovalId),
                    new AssetAccountingStageEvidenceDto(AssetAccountingLifecycleStageDto.Posted, now, request.Actor,
                        request.ApprovalEvidence, retained.Spine.EventId.ToString("D"))]
            };
        }

        public void Dispose() => Directory.Delete(_directory, true);
    }
}
