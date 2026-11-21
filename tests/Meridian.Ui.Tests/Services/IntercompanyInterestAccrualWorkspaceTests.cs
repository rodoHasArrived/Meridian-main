using Meridian.Contracts.Api;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Ui.Services.Services.Accounting;

namespace Meridian.Ui.Tests.Services;

/// <summary>Desktop accrual request state over synthetic shared-API responses; no WPF runtime is involved.</summary>
public sealed class IntercompanyInterestAccrualWorkspaceTests
{
    private static readonly Guid Book = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Interest = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Principal = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid DraftId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly string Digest = new('d', 64);
    private const decimal Unrounded = 4166.666666666666666666666667m;

    [Fact]
    public async Task PreviewSendsSelectionsOnlyAndShowsExactServerFigures()
    {
        var client = new Client();
        using var view = Open(client);
        await view.PreviewCommand.ExecuteAsync(null);
        Assert.Equal(new IntercompanyAccrualRequestDto(Book, Interest, Principal, new(2026, 6, 1), new(2026, 7, 1)), Assert.Single(client.Previews));
        Assert.StartsWith("4166.67 USD interest for 2026-06-01 up to 2026-07-01 · journal date 2026-06-30", view.Summary);
        var slice = Assert.Single(view.Slices);
        Assert.Equal("1000000", slice.Principal);
        Assert.Equal("0.05", slice.AnnualRate);
        Assert.Equal("4166.666666666666666666666667", slice.UnroundedAmount);
        Assert.Equal(["Debit", "Credit"], view.Lines.Select(line => line.Side));
        Assert.Equal("4166.67 USD", view.Lines[0].Amount);
        Assert.Contains($"Evidence digest {Digest}", view.Evidence);
        Assert.Contains("loan-interest:20260601-20260701", view.Evidence);
        Assert.True(view.CanManageLedger);
        Assert.True(view.CreateDraftCommand.CanExecute(null));
        Assert.Empty(client.Drafts);
    }

    [Fact]
    public async Task DraftIsBoundToThePreviewedSelectionAndEvidenceDigestAndIsNotRepeated()
    {
        var client = new Client();
        using var view = Open(client);
        await view.PreviewCommand.ExecuteAsync(null);
        await view.CreateDraftCommand.ExecuteAsync(null);
        var sent = Assert.Single(client.Drafts);
        Assert.Equal(Assert.Single(client.Previews), sent.Accrual);
        Assert.Equal(Digest, sent.ExpectedEvidenceDigest);
        Assert.Contains($"Accrual draft {DraftId:D} · Draft · version 1", view.DraftStatus);
        Assert.Contains("Nothing was approved or posted", view.StatusText);
        Assert.False(view.CreateDraftCommand.CanExecute(null));
    }

    [Fact]
    public async Task ChangingTheSelectionWithdrawsThePreviewSoAStaleAmountCannotBeDrafted()
    {
        var client = new Client();
        using var view = Open(client);
        await view.PreviewCommand.ExecuteAsync(null);
        view.AccrualTo = "2026-06-16";
        Assert.Empty(view.Slices);
        Assert.Empty(view.Lines);
        Assert.Equal("No accrual is previewed.", view.Summary);
        Assert.Equal("", view.Evidence);
        Assert.StartsWith("Selection changed", view.StatusText);
        Assert.False(view.CreateDraftCommand.CanExecute(null));
        await view.CreateDraftCommand.ExecuteAsync(null);
        Assert.Empty(client.Drafts);
    }

    [Fact]
    public async Task UnavailableAccrualShowsTheServerRefusalAndNoAmount()
    {
        var client = new Client
        {
            Preview = request => ApiResponse<IntercompanyAccrualProposalDto>.Ok(new(
                new(IntercompanyAccrualRules.StatusUnavailable, IntercompanyAccrualRules.RuleVersion, null, null, request.AccrualFrom, request.AccrualTo, null, null, "", [], [], []),
                ["Exactly one open source period must contain the last accrued day; no date is moved automatically."]))
        };
        using var view = Open(client);
        await view.PreviewCommand.ExecuteAsync(null);
        Assert.Equal("Accrual unavailable. No amount is estimated.", view.Summary);
        Assert.Contains("Exactly one open source period", Assert.Single(view.Refusals));
        Assert.Empty(view.Slices);
        Assert.False(view.CreateDraftCommand.CanExecute(null));
    }

    [Fact]
    public async Task ReadOnlyAccountMayPreviewButCannotDraft()
    {
        var client = new Client { CanManageLedger = false };
        using var view = Open(client);
        await view.PreviewCommand.ExecuteAsync(null);
        Assert.Single(view.Slices);
        Assert.Contains("requires ledger management permission", view.StatusText);
        Assert.StartsWith("Read-only", view.PermissionNote);
        Assert.False(view.CreateDraftCommand.CanExecute(null));
    }

    [Fact]
    public async Task ChangedInputsConflictKeepsTheSelectionAndRequiresANewPreview()
    {
        var client = new Client { Draft = _ => ApiResponse<ManualJournalEntryDraftDto>.Fail("The approved terms, profile, principal or FX inputs changed after the preview. Preview the accrual again before drafting.", 409) };
        using var view = Open(client);
        await view.PreviewCommand.ExecuteAsync(null);
        await view.CreateDraftCommand.ExecuteAsync(null);
        Assert.Contains("changed after the preview", view.StatusText);
        Assert.Contains("Your input has been retained", view.StatusText);
        Assert.Equal("", view.DraftStatus);
        Assert.Equal("2026-06-01", view.AccrualFrom);
    }

    [Fact]
    public async Task InvalidIdentityOrDateIsRefusedBeforeAnyRequest()
    {
        var client = new Client();
        using var view = Open(client);
        view.InterestProfile = "profile-1";
        await view.PreviewCommand.ExecuteAsync(null);
        Assert.Contains("Loan-interest accounting profile must be a valid nonempty ID", view.StatusText);
        view.InterestProfile = Interest.ToString("D");
        view.AccrualFrom = "01/06/2026";
        await view.PreviewCommand.ExecuteAsync(null);
        Assert.Contains("First accrued day must use yyyy-MM-dd", view.StatusText);
        Assert.Empty(client.Previews);
        Assert.Equal(0, client.CapabilityReads);
    }

    [Fact]
    public async Task SelectionChangeIgnoresTheSupersededPreviewResponse()
    {
        var pending = new TaskCompletionSource<ApiResponse<IntercompanyAccrualProposalDto>>();
        var client = new Client { PreviewHandler = _ => pending.Task };
        using var view = Open(client);
        var preview = view.PreviewCommand.ExecuteAsync(null);
        view.AccrualTo = "2026-06-16";
        Assert.False(view.IsBusy);
        pending.SetResult(ApiResponse<IntercompanyAccrualProposalDto>.Ok(Ready(new(Book, Interest, Principal, new(2026, 6, 1), new(2026, 7, 1)))));
        await preview;
        Assert.Empty(view.Slices);
        Assert.Equal("No accrual is previewed.", view.Summary);
        Assert.False(view.CreateDraftCommand.CanExecute(null));
    }

    [Fact]
    public async Task SessionResetClearsPriorAccountEvidenceSelectionAndPermission()
    {
        var client = new Client();
        using var view = Open(client);
        await view.PreviewCommand.ExecuteAsync(null);
        view.ResetSession();
        Assert.Empty(view.Slices);
        Assert.Empty(view.Lines);
        Assert.False(view.CanManageLedger);
        Assert.Equal(("", "", "", "", ""), (view.SourceBook, view.InterestProfile, view.PrincipalProfile, view.AccrualFrom, view.AccrualTo));
        Assert.False(view.PreviewCommand.CanExecute(null));
    }

    private static IntercompanyInterestAccrualWorkspace Open(Client client) => new(client)
    {
        SourceBook = Book.ToString("D"),
        InterestProfile = Interest.ToString("D"),
        PrincipalProfile = Principal.ToString("D"),
        AccrualFrom = "2026-06-01",
        AccrualTo = "2026-07-01"
    };

    internal static IntercompanyAccrualProposalDto Ready(IntercompanyAccrualRequestDto request)
    {
        var agreement = Guid.NewGuid();
        var revision = Guid.NewGuid();
        var computation = new IntercompanyAccrualPreviewDto(IntercompanyAccrualRules.StatusReady, IntercompanyAccrualRules.RuleVersion, agreement, "USD",
            request.AccrualFrom, request.AccrualTo, 4166.67m, Unrounded, "day-count-v1", [new("WEEKENDS", "calendar-hash")],
            [new(1, request.AccrualFrom, request.AccrualTo, revision, 2, "legal-hash", 1, 1_000_000m, 0.05m, "Actual360", 0.0833333333333333333333333333m, Unrounded, "opening:1")], []);
        var evidence = new IntercompanyAccrualEvidenceDto(IntercompanyAccrualRules.RuleVersion, DraftId, "tenant", request.SourceBookId, Guid.NewGuid(), Guid.NewGuid(),
            request.InterestArrangementId, revision, agreement, 2, "legal-hash", IntercompanyAccrualEvidenceGuard.TransactionId(request.AccrualFrom, request.AccrualTo),
            request.AccrualTo.AddDays(-1), IntercompanyAccountRoleDto.Receivable, "a-int-recv", "a-int-income", "USD", 4166.67m, computation,
            new(request.PrincipalArrangementId, IntercompanyAccountRoleDto.Receivable, ["a-receivable"], ["a-payable"], 1_000_000m, 1, "opening-digest", []));
        return new(computation, [], evidence, Digest, Draft(request.SourceBookId, 0));
    }

    internal static ManualJournalEntryDraftDto Draft(Guid book, int version)
        => new(DraftId, ManualJournalEntryStatusDto.Draft, "profile", book, default, new(2026, 6, 30), null, null, null, "USD", "Loan interest accrual", "system",
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, version,
            [new("line-1", AccountingTemplateLineSideDto.Debit, 4166.67m, "USD", "Assets:Interest receivable"),
             new("line-2", AccountingTemplateLineSideDto.Credit, 4166.67m, "USD", "Income:Interest")], [], [])
        { IntercompanyAccrualEvidenceDigest = Digest };

    private sealed class Client : IWorkstationIntercompanyAgreementApiClient
    {
        public bool CanManageLedger { get; init; } = true;
        public int CapabilityReads { get; private set; }
        public List<IntercompanyAccrualRequestDto> Previews { get; } = [];
        public List<CreateIntercompanyAccrualDraftRequest> Drafts { get; } = [];
        public Func<IntercompanyAccrualRequestDto, ApiResponse<IntercompanyAccrualProposalDto>> Preview { get; init; } = request => ApiResponse<IntercompanyAccrualProposalDto>.Ok(Ready(request));
        public Func<IntercompanyAccrualRequestDto, Task<ApiResponse<IntercompanyAccrualProposalDto>>>? PreviewHandler { get; init; }
        public Func<CreateIntercompanyAccrualDraftRequest, ApiResponse<ManualJournalEntryDraftDto>> Draft { get; init; }
            = request => ApiResponse<ManualJournalEntryDraftDto>.Ok(IntercompanyInterestAccrualWorkspaceTests.Draft(request.Accrual.SourceBookId, 1));

        public Task<ApiResponse<IntercompanyOperationCapabilities>> GetIntercompanyCapabilitiesAsync(CancellationToken ct = default)
        {
            CapabilityReads++;
            return Task.FromResult(ApiResponse<IntercompanyOperationCapabilities>.Ok(new(CanManageLedger: CanManageLedger, CanViewLedger: true)));
        }
        public Task<ApiResponse<IntercompanyAccrualProposalDto>> PreviewInterestAccrualAsync(IntercompanyAccrualRequestDto request, CancellationToken ct = default)
        {
            Previews.Add(request);
            return PreviewHandler?.Invoke(request) ?? Task.FromResult(Preview(request));
        }
        public Task<ApiResponse<ManualJournalEntryDraftDto>> CreateInterestAccrualDraftAsync(CreateIntercompanyAccrualDraftRequest request, CancellationToken ct = default)
        {
            Drafts.Add(request);
            return Task.FromResult(Draft(request));
        }

        public Task<ApiResponse<IntercompanyAgreementListDto>> ListAsync(IntercompanyAgreementQueryDto query, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ApiResponse<IntercompanyAgreementDetailDto>> GetAsync(Guid agreementId, DateOnly asOf, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ApiResponse<IntercompanyAgreementJournalLinesDto>> GetJournalLinesAsync(Guid agreementId, Guid ledgerBookId, DateOnly asOf, string? cursor = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ApiResponse<IntercompanyAgreementCommandResultDto>> ExecuteAsync(string action, IntercompanyAgreementCommandDto command, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
