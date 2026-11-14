using Meridian.Contracts.Api;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Ui.Services.Services.Accounting;

namespace Meridian.Ui.Tests.Services;

/// <summary>Desktop counterpart queue state over synthetic shared-API responses; no WPF runtime is involved.</summary>
public sealed class IntercompanyCounterpartQueueWorkspaceTests
{
    private static readonly Guid Book = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private const decimal Exact = 1234567890123456.789012345678m;

    [Fact]
    public async Task LoadShowsRetainedRecordsBreaksAndServerPermission()
    {
        var client = new Client { Proposals = [Proposal(IntercompanyProposalStatusDto.NeedsFix, error: "Destination period is closed")] };
        client.Breaks = ApiResponse<IntercompanyBreakDto[]>.Ok([new(client.Proposals[0].Source.ProposalId, Guid.NewGuid(), "TX-1", "DeliveryBlocked",
            new(2026, 10, 1), 44, "Contractual due date", "Destination period is closed", Guid.NewGuid(), null)]);
        using var view = Open(client);
        await view.LoadCommand.ExecuteAsync(null);
        var row = Assert.Single(view.Proposals);
        Assert.Equal("NeedsFix", row.Delivery);
        Assert.Equal("Destination period is closed", row.DeliveryDetail);
        Assert.Equal("Awaiting counterpart draft", row.Target);
        Assert.Contains("1 need repair", view.StatusText);
        var item = Assert.Single(view.Breaks);
        Assert.Equal("Contractual due date from 2026-10-01", item.Aging);
        Assert.Equal(44, item.AgeDays);
        Assert.Contains("reported separately in consolidation", view.BreakStatus);
        Assert.True(view.CanManageLedger);
        Assert.Equal((Book, new DateOnly(2026, 11, 14)), Assert.Single(client.BreakReads));
    }

    [Fact]
    public async Task SourceAmountsKeepEveryServerDecimalDigit()
    {
        var client = new Client { Proposals = [Proposal(IntercompanyProposalStatusDto.Pending, amount: Exact)] };
        using var view = Open(client);
        await view.LoadCommand.ExecuteAsync(null);
        var line = Assert.Single(Assert.Single(view.Proposals).Lines);
        Assert.Equal("1234567890123456.789012345678", line.TransactionDebit);
        Assert.Equal("1234567890123456.789012345678", line.FunctionalDebit);
        Assert.Equal("0", line.TransactionCredit);
        Assert.NotEqual(((decimal)(double)Exact).ToString(System.Globalization.CultureInfo.InvariantCulture), line.TransactionDebit);
    }

    [Fact]
    public async Task BreakOutageReadsAsUnknownRatherThanNoBreaks()
    {
        var client = new Client { Proposals = [Proposal(IntercompanyProposalStatusDto.Pending)], Breaks = ApiResponse<IntercompanyBreakDto[]>.Fail("Break authority unavailable", 503) };
        using var view = Open(client);
        await view.LoadCommand.ExecuteAsync(null);
        Assert.Single(view.Proposals);
        Assert.Empty(view.Breaks);
        Assert.StartsWith("Booking breaks unknown", view.BreakStatus);
        Assert.DoesNotContain("No reported", view.BreakStatus);
    }

    [Fact]
    public async Task ProposalOutageClearsPriorRecordsAndPermission()
    {
        var client = new Client { Proposals = [Proposal(IntercompanyProposalStatusDto.Pending)] };
        using var view = Open(client);
        await view.LoadCommand.ExecuteAsync(null);
        client.ProposalFailure = ApiResponse<IntercompanyProposalDto[]>.Fail("Store unavailable", 503);
        await view.LoadCommand.ExecuteAsync(null);
        Assert.Empty(view.Proposals);
        Assert.False(view.CanManageLedger);
        Assert.Contains("Store unavailable", view.StatusText);
        Assert.Equal("Booking breaks are not loaded.", view.BreakStatus);
    }

    [Fact]
    public async Task ReadOnlyAccountCannotRetryRepriceOrLink()
    {
        var client = new Client { Capabilities = new(CanViewLedger: true), Proposals = [Proposal(IntercompanyProposalStatusDto.NeedsFix), Proposal(IntercompanyProposalStatusDto.Drafted, target: true)] };
        using var view = Open(client);
        await view.LoadCommand.ExecuteAsync(null);
        foreach (var row in view.Proposals)
        {
            view.SelectedProposal = row;
            view.RetryReason = view.LinkReason = view.RepricingReason = "reason";
            view.TargetBook = view.TargetJournal = Guid.NewGuid().ToString("D");
            Assert.False(view.RetryCommand.CanExecute(null));
            Assert.False(view.LinkCommand.CanExecute(null));
            Assert.False(view.PreviewRepricingCommand.CanExecute(null));
            Assert.False(view.ApplyRepricingCommand.CanExecute(null));
        }
        Assert.StartsWith("Read-only", view.PermissionNote);
    }

    [Fact]
    public async Task UnavailablePermissionEndpointFailsClosed()
    {
        var client = new Client { CapabilityFailure = true, Proposals = [Proposal(IntercompanyProposalStatusDto.NeedsFix)] };
        using var view = Open(client);
        await view.LoadCommand.ExecuteAsync(null);
        view.SelectedProposal = Assert.Single(view.Proposals);
        view.RetryReason = "reason";
        Assert.False(view.CanManageLedger);
        Assert.False(view.RetryCommand.CanExecute(null));
    }

    [Fact]
    public async Task RetryRequiresReasonAndLostResponseReusesExactRequestIdentity()
    {
        var client = new Client { Proposals = [Proposal(IntercompanyProposalStatusDto.NeedsFix)], RetryFailure = ApiResponse<IntercompanyProposalDto>.Fail("Service unreachable", 0, true) };
        using var view = Open(client);
        await view.LoadCommand.ExecuteAsync(null);
        view.SelectedProposal = Assert.Single(view.Proposals);
        Assert.False(view.RetryCommand.CanExecute(null));
        view.RetryReason = "  Period reopened  ";
        await view.RetryCommand.ExecuteAsync(null);
        Assert.Contains("Your input has been retained", view.StatusText);
        Assert.Equal("  Period reopened  ", view.RetryReason);
        await view.RetryCommand.ExecuteAsync(null);
        Assert.Equal(2, client.Retries.Count);
        Assert.Equal(client.Retries[0].Request.RequestId, client.Retries[1].Request.RequestId);
        Assert.Equal("Period reopened", client.Retries[0].Request.Reason);
        view.RetryReason = "Different reason";
        await view.RetryCommand.ExecuteAsync(null);
        Assert.NotEqual(client.Retries[0].Request.RequestId, client.Retries[2].Request.RequestId);
        Assert.NotEqual(Guid.Empty, client.Retries[2].Request.RequestId);
    }

    [Fact]
    public async Task SuccessfulRetryReplacesTheRowAndInvalidatesStaleBreaks()
    {
        var client = new Client { Proposals = [Proposal(IntercompanyProposalStatusDto.NeedsFix, attempts: 3)] };
        client.Breaks = ApiResponse<IntercompanyBreakDto[]>.Ok([new(client.Proposals[0].Source.ProposalId, Guid.NewGuid(), "TX-1", "DeliveryBlocked", new(2026, 10, 1), 44, "Source effective date", "blocked", Guid.NewGuid(), null)]);
        using var view = Open(client);
        await view.LoadCommand.ExecuteAsync(null);
        view.SelectedProposal = Assert.Single(view.Proposals);
        view.RetryReason = "Period reopened";
        await view.RetryCommand.ExecuteAsync(null);
        var row = Assert.Single(view.Proposals);
        Assert.Equal(IntercompanyProposalStatusDto.Pending, row.Status);
        Assert.Same(row, view.SelectedProposal);
        Assert.Equal("", view.RetryReason);
        Assert.Empty(view.Breaks);
        Assert.Contains("Reload", view.BreakStatus);
        Assert.Contains("retry was scheduled", view.StatusText);
    }

    [Fact]
    public async Task LinkSendsExactTargetIdentityAndClosesFurtherResolution()
    {
        var client = new Client { Proposals = [Proposal(IntercompanyProposalStatusDto.Pending)] };
        using var view = Open(client);
        await view.LoadCommand.ExecuteAsync(null);
        view.SelectedProposal = Assert.Single(view.Proposals);
        var book = Guid.NewGuid();
        var journal = Guid.NewGuid();
        view.TargetBook = book.ToString("D");
        view.TargetJournal = "not-a-journal";
        view.LinkReason = "Booked manually before delivery";
        await view.LinkCommand.ExecuteAsync(null);
        Assert.Empty(client.Links);
        Assert.Contains("Existing counterpart journal must be a valid nonempty ID", view.StatusText);
        view.TargetJournal = journal.ToString("D");
        await view.LinkCommand.ExecuteAsync(null);
        var sent = Assert.Single(client.Links).Request;
        Assert.Equal((book, journal, "Booked manually before delivery"), (sent.TargetBookId, sent.TargetJournalId, sent.Reason));
        var row = view.SelectedProposal!;
        Assert.True(row.IsResolved);
        Assert.Equal("Resolved", row.Delivery);
        Assert.Equal("Original delivery receipt: Pending", row.DeliveryDetail);
        Assert.Contains($"Resolved counterpart {journal:D}", row.Target);
        Assert.Contains("Resolution reason Booked manually before delivery", row.Provenance);
        view.RetryReason = view.LinkReason = "again";
        view.TargetBook = view.TargetJournal = Guid.NewGuid().ToString("D");
        Assert.False(view.RetryCommand.CanExecute(null));
        Assert.False(view.LinkCommand.CanExecute(null));
        Assert.False(view.PreviewRepricingCommand.CanExecute(null));
    }

    [Fact]
    public async Task ServerRefusalKeepsLinkInputAndShowsTheRetainedReason()
    {
        var client = new Client { Proposals = [Proposal(IntercompanyProposalStatusDto.Drafted, target: true)], LinkFailure = ApiResponse<IntercompanyProposalDto>.Fail("Retain the generated draft's rejection first.", 409) };
        using var view = Open(client);
        await view.LoadCommand.ExecuteAsync(null);
        view.SelectedProposal = Assert.Single(view.Proposals);
        view.TargetBook = view.TargetJournal = Guid.NewGuid().ToString("D");
        view.LinkReason = "Existing posted journal";
        await view.LinkCommand.ExecuteAsync(null);
        Assert.Contains("Retain the generated draft's rejection first.", view.StatusText);
        Assert.Equal("Existing posted journal", view.LinkReason);
        Assert.False(view.SelectedProposal!.IsResolved);
    }

    [Fact]
    public async Task RepricingShowsExactBeforeAndAfterAndAppliesTheReviewedPreview()
    {
        var proposal = Proposal(IntercompanyProposalStatusDto.Drafted, target: true);
        var current = Draft(proposal, 7, 100.10m, 91.000000000000000001m, 1.1m);
        var proposed = Draft(proposal, 7, 123456789012.345678901234m, 91.000000000000000001m, 1356.668010123456789025m) with
        { IntercompanyFx = new(123456789012.345678901234m, "USD", 1356.668010123456789025m, null, false, Guid.Parse("22222222-2222-2222-2222-222222222222"), 4, "fx-policy", "9", "config-hash") };
        var client = new Client { Proposals = [proposal], Preview = ApiResponse<IntercompanyRefreshPreviewDto>.Ok(new(proposed, "fingerprint-1", current)) };
        using var view = Open(client);
        await view.LoadCommand.ExecuteAsync(null);
        view.SelectedProposal = Assert.Single(view.Proposals);
        Assert.False(view.RetryCommand.CanExecute(null));
        view.RepricingReason = "New approved month-end rate";
        Assert.False(view.ApplyRepricingCommand.CanExecute(null));
        await view.PreviewRepricingCommand.ExecuteAsync(null);
        Assert.Equal(2, view.RepricingRows.Count);
        var row = view.RepricingRows[0];
        Assert.Equal("100.10 USD", row.CurrentAmount);
        Assert.Equal("123456789012.345678901234 USD", row.ProposedAmount);
        Assert.Equal("91.000000000000000001 EUR · rate 1356.668010123456789025", row.OriginalEvidence);
        Assert.Equal("Debit", row.Side);
        Assert.Contains("Target draft version 7", view.RepricingStatus);
        Assert.Contains("fingerprint-1", view.RepricingStatus);
        Assert.Contains("revision 4", view.RepricingStatus);
        await view.ApplyRepricingCommand.ExecuteAsync(null);
        var sent = Assert.Single(client.Refreshes).Request;
        Assert.Equal((7, "fingerprint-1", "New approved month-end rate"), (sent.ExpectedDraftVersion, sent.PreviewFingerprint, sent.Reason));
        Assert.Empty(view.RepricingRows);
        Assert.Equal("", view.RepricingReason);
        Assert.Contains("previous review was cleared", view.StatusText);
        Assert.False(view.ApplyRepricingCommand.CanExecute(null));
    }

    [Fact]
    public async Task RepricingWithoutRetainedCurrentDraftCannotBeApplied()
    {
        var proposal = Proposal(IntercompanyProposalStatusDto.Drafted, target: true);
        var client = new Client { Proposals = [proposal], Preview = ApiResponse<IntercompanyRefreshPreviewDto>.Ok(new(Draft(proposal, 2, 10m, 9m, 1.1m), "fingerprint")) };
        using var view = Open(client);
        await view.LoadCommand.ExecuteAsync(null);
        view.SelectedProposal = Assert.Single(view.Proposals);
        view.RepricingReason = "reason";
        await view.PreviewRepricingCommand.ExecuteAsync(null);
        Assert.Contains("did not return the retained current draft", view.RepricingStatus);
        Assert.All(view.RepricingRows, row => Assert.Equal("Not retained", row.CurrentAmount));
        Assert.False(view.ApplyRepricingCommand.CanExecute(null));
    }

    [Fact]
    public async Task PreviewBelongsOnlyToTheProposalItWasRequestedFor()
    {
        var first = Proposal(IntercompanyProposalStatusDto.Drafted, target: true);
        var second = Proposal(IntercompanyProposalStatusDto.Drafted, target: true);
        var client = new Client { Proposals = [first, second], Preview = ApiResponse<IntercompanyRefreshPreviewDto>.Ok(new(Draft(first, 3, 11m, 9m, 1.2m), "fingerprint", Draft(first, 3, 10m, 9m, 1.1m))) };
        using var view = Open(client);
        await view.LoadCommand.ExecuteAsync(null);
        view.SelectedProposal = view.Proposals[0];
        await view.PreviewRepricingCommand.ExecuteAsync(null);
        view.SelectedProposal = view.Proposals[1];
        view.RepricingReason = "reason";
        Assert.Empty(view.RepricingRows);
        Assert.False(view.ApplyRepricingCommand.CanExecute(null));
    }

    [Fact]
    public async Task FailedPreviewShowsNoAmounts()
    {
        var client = new Client { Proposals = [Proposal(IntercompanyProposalStatusDto.Drafted, target: true)], Preview = ApiResponse<IntercompanyRefreshPreviewDto>.Fail("This counterpart requires no foreign-currency repricing.", 409) };
        using var view = Open(client);
        await view.LoadCommand.ExecuteAsync(null);
        view.SelectedProposal = Assert.Single(view.Proposals);
        await view.PreviewRepricingCommand.ExecuteAsync(null);
        Assert.Empty(view.RepricingRows);
        Assert.StartsWith("Repricing preview unavailable", view.RepricingStatus);
        Assert.Contains("requires no foreign-currency repricing", view.StatusText);
    }

    [Fact]
    public async Task ScopeChangeDiscardsEvidenceAndIgnoresTheSupersededResponse()
    {
        var pending = new TaskCompletionSource<ApiResponse<IntercompanyProposalDto[]>>();
        var client = new Client { ProposalHandler = () => pending.Task };
        using var view = Open(client);
        var load = view.LoadCommand.ExecuteAsync(null);
        var other = Guid.NewGuid();
        view.SourceBook = other.ToString("D");
        Assert.False(view.IsBusy);
        pending.SetResult(ApiResponse<IntercompanyProposalDto[]>.Ok([Proposal(IntercompanyProposalStatusDto.Pending)]));
        await load;
        Assert.Empty(view.Proposals);
        Assert.StartsWith("Scope changed", view.StatusText);
        Assert.True(view.LoadCommand.CanExecute(null));
    }

    [Fact]
    public async Task SessionResetClearsPriorAccountEvidenceAndPermission()
    {
        var client = new Client { Proposals = [Proposal(IntercompanyProposalStatusDto.NeedsFix)] };
        using var view = Open(client);
        await view.LoadCommand.ExecuteAsync(null);
        view.SelectedProposal = Assert.Single(view.Proposals);
        view.ResetSession();
        Assert.Empty(view.Proposals);
        Assert.Empty(view.Breaks);
        Assert.Null(view.SelectedProposal);
        Assert.False(view.CanManageLedger);
        Assert.Equal("", view.SourceBook);
        Assert.False(view.LoadCommand.CanExecute(null));
    }

    [Fact]
    public async Task InvalidBookOrDateIsRefusedBeforeAnyRequest()
    {
        var client = new Client();
        using var view = new IntercompanyCounterpartQueueWorkspace(client) { SourceBook = "book-1", AsOf = "2026-11-14" };
        await view.LoadCommand.ExecuteAsync(null);
        Assert.Contains("Source ledger book must be a valid nonempty ID", view.StatusText);
        view.SourceBook = Book.ToString("D");
        view.AsOf = "14/11/2026";
        await view.LoadCommand.ExecuteAsync(null);
        Assert.Contains("As-of date must use yyyy-MM-dd", view.StatusText);
        Assert.Equal(0, client.Reads);
    }

    private static IntercompanyCounterpartQueueWorkspace Open(Client client) => new(client) { SourceBook = Book.ToString("D"), AsOf = "2026-11-14" };

    private static IntercompanyProposalDto Proposal(IntercompanyProposalStatusDto status, bool target = false, string? error = null, int attempts = 1, decimal amount = 91m)
        => new(new(Guid.NewGuid(), "tenant", "company", Guid.NewGuid(), Book, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "TX-1", new(2026, 9, 30), "EUR",
            [new(Guid.NewGuid(), "Due from affiliate", "Asset", "node-1", amount, 0m, "EUR", amount, 0m)])
        { IntercompanyAgreementId = Guid.NewGuid(), IntercompanyAgreementVersion = 2, IntercompanyAgreementPayloadHash = "legal-hash" },
            "source-digest", status, attempts, DateTimeOffset.UnixEpoch, target ? Guid.NewGuid() : null, target ? Guid.NewGuid() : null, error);

    private static ManualJournalEntryDraftDto Draft(IntercompanyProposalDto proposal, int version, decimal amount, decimal original, decimal rate)
        => new(proposal.TargetJournalId!.Value, ManualJournalEntryStatusDto.Draft, "profile", proposal.TargetBookId, default, new(2026, 9, 30), null, null, null, "USD", "Counterpart", "system",
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, version,
            [new("line-1", AccountingTemplateLineSideDto.Debit, amount, "USD", "Expense:Affiliate") { TransactionCurrency = new("EUR", original, rate) },
             new("line-2", AccountingTemplateLineSideDto.Credit, amount, "USD", "Liabilities:Due to affiliate") { TransactionCurrency = new("EUR", original, rate) }], [], [])
        { IntercompanyProposalId = proposal.Source.ProposalId, IntercompanyProposalSourceDigest = proposal.SourceDigest };

    private sealed class Client : IWorkstationIntercompanyAgreementApiClient
    {
        public IntercompanyOperationCapabilities Capabilities { get; init; } = new(CanManageLedger: true, CanViewLedger: true);
        public bool CapabilityFailure { get; init; }
        public IntercompanyProposalDto[] Proposals { get; set; } = [];
        public ApiResponse<IntercompanyProposalDto[]>? ProposalFailure { get; set; }
        public Func<Task<ApiResponse<IntercompanyProposalDto[]>>>? ProposalHandler { get; init; }
        public ApiResponse<IntercompanyBreakDto[]> Breaks { get; set; } = ApiResponse<IntercompanyBreakDto[]>.Ok([]);
        public ApiResponse<IntercompanyProposalDto>? RetryFailure { get; init; }
        public ApiResponse<IntercompanyProposalDto>? LinkFailure { get; init; }
        public ApiResponse<IntercompanyRefreshPreviewDto> Preview { get; init; } = ApiResponse<IntercompanyRefreshPreviewDto>.Fail("unavailable", 503);
        public List<(Guid Book, DateOnly AsOf)> BreakReads { get; } = [];
        public List<(Guid Proposal, RetryIntercompanyProposalRequest Request)> Retries { get; } = [];
        public List<(Guid Proposal, LinkIntercompanyCounterpartRequest Request)> Links { get; } = [];
        public List<(Guid Proposal, RefreshIntercompanyProposalRequest Request)> Refreshes { get; } = [];
        public int Reads { get; private set; }

        public Task<ApiResponse<IntercompanyOperationCapabilities>> GetIntercompanyCapabilitiesAsync(CancellationToken ct = default)
        { Reads++; return Task.FromResult(CapabilityFailure ? ApiResponse<IntercompanyOperationCapabilities>.Fail("unavailable", 503) : ApiResponse<IntercompanyOperationCapabilities>.Ok(Capabilities)); }
        public Task<ApiResponse<IntercompanyProposalDto[]>> GetCounterpartProposalsAsync(Guid sourceBookId, CancellationToken ct = default)
        { Reads++; return ProposalHandler?.Invoke() ?? Task.FromResult(ProposalFailure ?? ApiResponse<IntercompanyProposalDto[]>.Ok(Proposals)); }
        public Task<ApiResponse<IntercompanyBreakDto[]>> GetCounterpartBreaksAsync(Guid sourceBookId, DateOnly asOf, CancellationToken ct = default)
        { Reads++; BreakReads.Add((sourceBookId, asOf)); return Task.FromResult(Breaks); }
        public Task<ApiResponse<IntercompanyProposalDto>> RetryCounterpartProposalAsync(Guid proposalId, RetryIntercompanyProposalRequest request, CancellationToken ct = default)
        {
            Retries.Add((proposalId, request));
            return Task.FromResult(RetryFailure ?? ApiResponse<IntercompanyProposalDto>.Ok(Proposals.Single(x => x.Source.ProposalId == proposalId) with { Status = IntercompanyProposalStatusDto.Pending, LastError = null }));
        }
        public Task<ApiResponse<IntercompanyProposalDto>> LinkCounterpartProposalAsync(Guid proposalId, LinkIntercompanyCounterpartRequest request, CancellationToken ct = default)
        {
            Links.Add((proposalId, request));
            return Task.FromResult(LinkFailure ?? ApiResponse<IntercompanyProposalDto>.Ok(Proposals.Single(x => x.Source.ProposalId == proposalId) with
            { LatestLink = new("tenant", request.RequestId, proposalId, request.TargetBookId, request.TargetJournalId, "controller", request.Reason, DateTimeOffset.UnixEpoch) }));
        }
        public Task<ApiResponse<IntercompanyRefreshPreviewDto>> PreviewCounterpartRefreshAsync(Guid proposalId, CancellationToken ct = default) => Task.FromResult(Preview);
        public Task<ApiResponse<ManualJournalEntryDraftDto>> RefreshCounterpartProposalAsync(Guid proposalId, RefreshIntercompanyProposalRequest request, CancellationToken ct = default)
        { Refreshes.Add((proposalId, request)); return Task.FromResult(ApiResponse<ManualJournalEntryDraftDto>.Ok(Preview.Data!.Draft with { IntercompanyRefreshRequestId = request.RequestId })); }

        public Task<ApiResponse<IntercompanyAgreementListDto>> ListAsync(IntercompanyAgreementQueryDto query, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ApiResponse<IntercompanyAgreementDetailDto>> GetAsync(Guid agreementId, DateOnly asOf, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ApiResponse<IntercompanyAgreementJournalLinesDto>> GetJournalLinesAsync(Guid agreementId, Guid ledgerBookId, DateOnly asOf, string? cursor = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ApiResponse<IntercompanyAgreementCommandResultDto>> ExecuteAsync(string action, IntercompanyAgreementCommandDto command, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
