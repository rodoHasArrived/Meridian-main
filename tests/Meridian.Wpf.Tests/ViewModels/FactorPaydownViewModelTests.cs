using System.Text.Json;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Ledger;
using Meridian.Wpf.Services;
using Meridian.Wpf.ViewModels.Accounting;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed class FactorPaydownViewModelTests
{
    private static readonly Guid BookId = Guid.NewGuid();
    private static readonly Guid SecurityId = Guid.NewGuid();
    private static readonly Guid PositionId = Guid.NewGuid();
    private static readonly Guid PeriodId = Guid.NewGuid();

    [Fact]
    public async Task PreparationRequiresExplicitConfirmationAndCreatesOnlyCandidate()
    {
        var client = new Client();
        var view = Create(client);
        await view.PrepareCommand.ExecuteAsync(null);
        view.PreviewText.Should().Contain("Principal: 100 USD");
        view.CanCreateDraft.Should().BeFalse();
        view.Confirmed = true;
        view.CanCreateDraft.Should().BeTrue();
        await view.CreateDraftCommand.ExecuteAsync(null);
        client.Drafts.Should().ContainSingle().Which.ExpectedPreparationFingerprint.Should().Be("review-fingerprint");
        view.StatusText.Should().Contain("Draft ").And.Contain("submit for review").And.Contain("Typed approval and posting are unavailable").And.Contain("Nothing has posted");
        view.CanCreateDraft.Should().BeFalse();
    }

    [Fact]
    public async Task RetainedServerDraft_SubmitsExactReturnedIdentityAndVersionWithoutPosting()
    {
        var client = new Client();
        var view = Create(client);
        await view.PrepareCommand.ExecuteAsync(null);
        view.Confirmed = true;
        await view.CreateDraftCommand.ExecuteAsync(null);
        view.RetainedDrafts.Should().ContainSingle();
        var selected = view.SelectedRetainedDraft!;
        selected.Version.Should().Be(7);
        view.CanSubmitRetainedDraft.Should().BeTrue();
        await view.SubmitRetainedDraftCommand.ExecuteAsync(null);
        var submission = client.Submissions.Should().ContainSingle().Which;
        submission.JournalEntryId.Should().Be(selected.JournalEntryId);
        submission.Version.Should().Be(7);
        submission.LedgerBookId.Should().Be(BookId);
        view.SelectedRetainedDraft!.Status.Should().Be(ManualJournalEntryStatusDto.Submitted);
        view.CanSubmitRetainedDraft.Should().BeFalse();
        view.StatusText.Should().Contain("does not approve or post");
    }

    [Fact]
    public async Task RetainedDraftReload_DiscardsLateResponseAfterFundSwitch()
    {
        var pending = new TaskCompletionSource<ManualJournalEntryWorkbenchDto>();
        var client = new Client { ReadDrafts = (_, _, _) => pending.Task };
        var view = Create(client);
        var load = view.ReloadRetainedDraftsCommand.ExecuteAsync(null);
        view.SetScope("foreign", Guid.NewGuid());
        pending.SetResult(new ManualJournalEntryWorkbenchDto("fund-a", BookId, DateTimeOffset.UnixEpoch, [], [], [], []));
        await load;
        view.RetainedDrafts.Should().BeEmpty();
        view.SelectedRetainedDraft.Should().BeNull();
        view.CanSubmitRetainedDraft.Should().BeFalse();
    }

    [Fact]
    public async Task ChangedInputInvalidatesReviewAndConfirmation()
    {
        var view = Create(new Client());
        await view.PrepareCommand.ExecuteAsync(null);
        view.Confirmed = true;
        view.Fields.Single(field => field.Key == "next").Value = "0.8";
        view.CanCreateDraft.Should().BeFalse();
        view.Confirmed.Should().BeFalse();
        view.PreviewText.Should().StartWith("No current server preparation");
    }

    [Fact]
    public async Task ScopeChangeDiscardsLatePreparation()
    {
        var pending = new TaskCompletionSource<FactorPaydownPreparationDto>();
        var view = Create(new Client { Preparation = (_, _) => pending.Task });
        var loading = view.PrepareCommand.ExecuteAsync(null);
        view.SetScope("other-fund", Guid.NewGuid());
        pending.SetResult(Preview());
        await loading;
        view.PreviewText.Should().StartWith("No current server preparation");
        view.CanCreateDraft.Should().BeFalse();
        view.ScopeText.Should().Contain("other-fund");
    }

    [Fact]
    public async Task ForeignScopeCannotBecomeReviewable()
    {
        var foreign = Preview() with { Instruction = Preview().Instruction with { Scope = Scope() with { FundProfileId = "foreign" } } };
        var view = Create(new Client { Preparation = (_, _) => Task.FromResult(foreign) });
        await view.PrepareCommand.ExecuteAsync(null);
        view.StatusText.Should().Contain("does not match");
        view.Confirmed = true;
        view.CanCreateDraft.Should().BeFalse();
    }

    [Fact]
    public async Task ServiceFailureShowsRefusalAndRetryCanRecover()
    {
        var client = new Client { Preparation = (_, _) => throw new InvalidOperationException("Period is locked") };
        var view = Create(client);
        await view.PrepareCommand.ExecuteAsync(null);
        view.StatusText.Should().Contain("Period is locked");
        view.IsBusy.Should().BeFalse();
        client.Preparation = (_, _) => Task.FromResult(Preview());
        await view.PrepareCommand.ExecuteAsync(null);
        view.PreviewText.Should().Contain("Principal:");
    }

    [Fact]
    public async Task MalformedIdentifiersNeverCallServer()
    {
        var client = new Client();
        var view = Create(client);
        view.Fields.Single(field => field.Key == "security").Value = "bad";
        await view.PrepareCommand.ExecuteAsync(null);
        client.Preparations.Should().Be(0);
        view.CanCreateDraft.Should().BeFalse();
    }

    [Fact]
    public async Task MissingRetainedEvidenceNeverCallsServer()
    {
        var client = new Client();
        var view = Create(client);
        view.Fields.Single(field => field.Key == "evidence").Value = "[]";
        await view.PrepareCommand.ExecuteAsync(null);
        client.Preparations.Should().Be(0);
        view.StatusText.Should().Contain("Exact retained factor evidence is required");
    }

    [Fact]
    public async Task PreviewPreservesCurrencyPrecisionBeyondTwoPlaces()
    {
        var preview = Preview();
        preview = preview with { Instruction = preview.Instruction with { PrincipalPaydown = 100.1234m, Currency = "CLF" } };
        var view = Create(new Client { Preparation = (_, _) => Task.FromResult(preview) });
        await view.PrepareCommand.ExecuteAsync(null);
        view.PreviewText.Should().Contain("100.1234 CLF");
    }

    [Fact]
    public async Task FailedDraftRetryKeepsExactReviewedRequestAndFingerprint()
    {
        var client = new Client { FailDraftOnce = true };
        var view = Create(client);
        await view.PrepareCommand.ExecuteAsync(null);
        view.Confirmed = true;
        await view.CreateDraftCommand.ExecuteAsync(null);
        view.StatusText.Should().Contain("Check retained history");
        await view.CreateDraftCommand.ExecuteAsync(null);
        client.Drafts.Should().HaveCount(2);
        client.Drafts[0].Should().Be(client.Drafts[1]);
    }

    [Fact]
    public async Task CancelInvalidatesPreparedReview()
    {
        var view = Create(new Client());
        await view.PrepareCommand.ExecuteAsync(null);
        view.Confirmed = true;
        view.Cancel();
        view.CanCreateDraft.Should().BeFalse();
        view.PreviewText.Should().StartWith("No current server preparation");
    }

    private static FactorPaydownViewModel Create(Client client)
    {
        var view = new FactorPaydownViewModel(client);
        view.SetScope("fund-a", BookId);
        var values = new Dictionary<string, string>
        {
            ["security"] = SecurityId.ToString(), ["position"] = PositionId.ToString(), ["period"] = PeriodId.ToString(),
            ["periodVersion"] = "1", ["date"] = "2026-10-28", ["prior"] = "1", ["next"] = "0.9",
            ["domain"] = "custodian", ["source"] = "factor-20261028", ["hash"] = new('a', 64),
            ["occurred"] = "2026-10-28T00:00:00Z", ["cash"] = "Assets:Cash", ["carrying"] = "Assets:MBS",
            ["gain"] = "Income:Realized Gain", ["loss"] = "Expenses:Realized Loss", ["evidence"] = JsonSerializer.Serialize(new[] { new RetainedEvidenceIdentityDto("factor-evidence", "evidence://factor", new('a', 64),
                "custodian", "factor-20261028", "Reviewed", "reviewer", DateTimeOffset.UnixEpoch, new(2026, 10, 28), 1,
                DateTimeOffset.UnixEpoch, "retainer", "FactorObservation", "factor-20261028") }, new JsonSerializerOptions(JsonSerializerDefaults.Web))
        };
        foreach (var field in view.Fields) field.Value = values[field.Key];
        return view;
    }

    private static AssetAccountingEventScopeDto Scope() => new(SecurityId, 1, PositionId, 1, BookId, PeriodId, default, "fund-a");

    private static FactorPaydownPreparationDto Preview() => new(
        new OpenLotFactorPaydownInstructionDto(null!, null!, Scope(), 1, "hash", 1m, .9m, 100m, "USD", []),
        null!, null!, new Dictionary<string, string> { ["lot.principal"] = "100.00" }, "review-fingerprint");

    private sealed class Client : IFactorPaydownApiClient
    {
        public Func<PrepareFactorPaydownRequestDto, CancellationToken, Task<FactorPaydownPreparationDto>> Preparation { get; set; }
            = (_, _) => Task.FromResult(Preview());
        public int Preparations { get; private set; }
        public bool FailDraftOnce { get; set; }
        public List<CreateFactorPaydownDraftRequestDto> Drafts { get; } = [];
        public List<ManualJournalEntryDraftDto> ServerDrafts { get; } = [];
        public List<SubmitManualJournalEntryApprovalRequest> Submissions { get; } = [];
        public Func<string, Guid, CancellationToken, Task<ManualJournalEntryWorkbenchDto>>? ReadDrafts { get; set; }
        public Task<ManualJournalEntryWorkbenchDto> GetRetainedDraftsAsync(string fundProfileId, Guid bookId, CancellationToken ct)
            => ReadDrafts?.Invoke(fundProfileId, bookId, ct) ?? Task.FromResult(new ManualJournalEntryWorkbenchDto(
                fundProfileId, bookId, DateTimeOffset.UnixEpoch, [], [], ServerDrafts, []));
        public Task<ManualJournalEntryDraftDto> SubmitForReviewAsync(SubmitManualJournalEntryApprovalRequest request, CancellationToken ct)
        {
            Submissions.Add(request);
            var index = ServerDrafts.FindIndex(draft => draft.JournalEntryId == request.JournalEntryId);
            var submitted = ServerDrafts[index] with { Status = ManualJournalEntryStatusDto.Submitted, Version = request.Version + 1 };
            ServerDrafts[index] = submitted;
            return Task.FromResult(submitted);
        }
        public Task<FactorPaydownPreparationDto> PrepareAsync(PrepareFactorPaydownRequestDto request, CancellationToken ct)
        {
            Preparations++;
            return Preparation(request, ct);
        }
        public Task<AssetAccountingPostingCandidateDto> CreateDraftAsync(CreateFactorPaydownDraftRequestDto request, CancellationToken ct)
        {
            Drafts.Add(request);
            if (FailDraftOnce && Drafts.Count == 1) throw new IOException("Connection interrupted");
            var spine = new AssetAccountingEventSpineDto(Guid.NewGuid(), default, 1, 1, new(2026, 10, 28), 100m, "USD", Scope(), null!, null!)
                { FactorPaydown = Preview().Instruction };
            var candidate = new PostingRuleJournalCandidateResultDto(null!, "rule", "1", [], null, Guid.NewGuid(),
                100m, 100m, 0m, true, false, true, false, [], []);
            var authority = new AssetAccountingPostingCandidateDto(spine, candidate);
            ServerDrafts.Add(new ManualJournalEntryDraftDto(candidate.JournalEntryId!.Value, ManualJournalEntryStatusDto.Draft,
                "fund-a", BookId, default, new(2026, 10, 28), PeriodId.ToString("D"), null, null, "USD", "Factor paydown",
                "operator", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, 7, [], [], []) { AssetAccountingDraft = authority });
            return Task.FromResult(authority);
        }
    }
}
