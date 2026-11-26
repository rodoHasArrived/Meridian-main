using Meridian.Contracts.Api;
using Meridian.Contracts.FundStructure;
using Meridian.Ui.Services.Services.Accounting;

namespace Meridian.Ui.Tests.Services;

/// <summary>Desktop maker-checker fixing state over synthetic shared-API responses; no WPF runtime is involved.</summary>
public sealed class IntercompanyRateFixingWorkspaceTests
{
    private static readonly DateOnly Date = new(2026, 5, 28);
    private static readonly Guid FixingId = Guid.Parse("e1111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task LoadShowsExactRatesAndStatus_AndProposeSendsTheTypedValueAgainstTheLoadedVersion()
    {
        var client = new Client { Current = Fixing(approved: 0.0425000000000000000000000001m, pending: null, version: 2) };
        using var view = await LoadAsync(client);

        var row = Assert.Single(view.Fixings);
        Assert.Equal(("0.0425000000000000000000000001", "Approved · version 2"), (row.Approved, row.StatusText));
        Assert.Equal(("SOFR", Date, Date), Assert.Single(client.Reads));

        view.FixingDate = "2026-05-28";
        view.RateFraction = "0.0426";
        view.SourceReference = "Synthetic publisher, republished";
        view.Justification = "Publisher correction.";
        await view.ProposeCommand.ExecuteAsync(null);

        var sent = Assert.Single(client.Proposals);
        Assert.Equal(("SOFR", Date, 0.0426m, 2L), (sent.IndexReference, sent.FixingDate, sent.RateFraction, sent.ExpectedVersion));
        Assert.Contains("sets no accrual rate until someone else approves it", view.StatusText);
    }

    [Fact]
    public async Task ApproveSendsTheExactPendingDigestAndVersion_AndRejectNeedsAReason()
    {
        var client = new Client { Current = Fixing(approved: 0.0425m, pending: 0.0426m, version: 3) };
        using var view = await LoadAsync(client);
        view.SelectedFixing = view.Fixings[0];
        Assert.Contains("Correction awaiting approval", view.SelectedFixing.StatusText);

        Assert.False(view.RejectCommand.CanExecute(null));
        await view.ApproveCommand.ExecuteAsync(null);

        var (id, decision) = Assert.Single(client.Decisions);
        Assert.Equal((FixingId, IntercompanyRateFixingActionDto.Approve, 3L, "pending-digest", (string?)null),
            (id, decision.Action, decision.ExpectedVersion, decision.ExpectedValueDigest, decision.Reason));
    }

    [Fact]
    public async Task ARefusedDecisionKeepsItsIdentityForAnIdenticalRetryOnly()
    {
        var client = new Client
        {
            Current = Fixing(approved: null, pending: 0.0425m, version: 1),
            DecisionResult = _ => ApiResponse<IntercompanyRateFixingDto>.Fail("A fixing is approved by someone other than the person who proposed it.", 409)
        };
        using var view = await LoadAsync(client);
        view.SelectedFixing = view.Fixings[0];
        view.Reason = "Wrong publication.";

        await view.RejectCommand.ExecuteAsync(null);
        await view.RejectCommand.ExecuteAsync(null);
        view.Reason = "Wrong publication date.";
        await view.RejectCommand.ExecuteAsync(null);

        Assert.Equal(client.Decisions[0].Request.RequestId, client.Decisions[1].Request.RequestId);
        Assert.NotEqual(client.Decisions[1].Request.RequestId, client.Decisions[2].Request.RequestId);
        Assert.Contains("someone other than", view.StatusText);
    }

    [Fact]
    public async Task ReadOnlyOperatorsSeeFixingsWithoutActions_AndChangingScopeOrSigningOutClears()
    {
        var client = new Client { CanManageLedger = false, Current = Fixing(approved: null, pending: 0.0425m, version: 1) };
        using var view = await LoadAsync(client);
        view.SelectedFixing = view.Fixings[0];
        view.FixingDate = "2026-05-28";
        view.RateFraction = "0.05";
        view.SourceReference = "s";
        view.Justification = "j";

        Assert.False(view.ApproveCommand.CanExecute(null));
        Assert.False(view.ProposeCommand.CanExecute(null));
        Assert.Contains("Read-only", view.PermissionNote);

        view.IndexReference = "SONIA";
        Assert.Empty(view.Fixings);
        view.ResetSession();
        Assert.Equal(("", "", ""), (view.IndexReference, view.From, view.To));
        Assert.False(view.CanManageLedger);
    }

    [Fact]
    public void FloatingAccrualSlicesNameTheirApprovedFixing_AndFixedSlicesSayFixed()
    {
        var fixedSlice = new IntercompanyAccrualSliceDto(1, Date, Date.AddDays(3), Guid.NewGuid(), 1, "hash", 1, 100m, 0.05m, "ACT/360", 0.01m, 0.05m, "evidence");
        var floating = fixedSlice with { Fixing = new(FixingId, "SOFR", Date, 0.0425m, 125m, 2, "digest") };

        Assert.Equal("Fixed", new IntercompanyInterestAccrualSliceRow(fixedSlice).Fixing);
        Assert.Equal("SOFR 2026-05-28 · 0.0425 + 125 bps", new IntercompanyInterestAccrualSliceRow(floating).Fixing);
    }

    private static async Task<IntercompanyRateFixingWorkspace> LoadAsync(Client client)
    {
        var view = new IntercompanyRateFixingWorkspace(client) { IndexReference = "SOFR", From = "2026-05-28", To = "2026-05-28" };
        await view.LoadCommand.ExecuteAsync(null);
        return view;
    }

    private static IntercompanyRateFixingDto Fixing(decimal? approved, decimal? pending, long version)
    {
        static IntercompanyRateFixingValueDto Value(decimal rate, string digest)
            => new(rate, "Synthetic publisher", "Synthetic fixture", "rates-maker", DateTimeOffset.UnixEpoch, digest);
        return new(FixingId, "tenant-a", "SOFR", Date, version, approved is { } a ? Value(a, "approved-digest") : null,
            pending is { } p ? Value(p, "pending-digest") : null,
            [new(Guid.NewGuid(), Guid.NewGuid(), IntercompanyRateFixingActionDto.Propose, "rates-maker", DateTimeOffset.UnixEpoch, "approved-digest")]);
    }

    private sealed class Client : IWorkstationIntercompanyAgreementApiClient
    {
        public bool CanManageLedger { get; init; } = true;
        public IntercompanyRateFixingDto Current { get; set; } = null!;
        public Func<DecideIntercompanyRateFixingRequest, ApiResponse<IntercompanyRateFixingDto>>? DecisionResult { get; init; }
        public List<(string Index, DateOnly From, DateOnly To)> Reads { get; } = [];
        public List<ProposeIntercompanyRateFixingRequest> Proposals { get; } = [];
        public List<(Guid Id, DecideIntercompanyRateFixingRequest Request)> Decisions { get; } = [];

        public Task<ApiResponse<IntercompanyOperationCapabilities>> GetIntercompanyCapabilitiesAsync(CancellationToken ct = default)
            => Task.FromResult(ApiResponse<IntercompanyOperationCapabilities>.Ok(new(CanManageLedger: CanManageLedger, CanViewLedger: true)));
        public Task<ApiResponse<IntercompanyRateFixingDto[]>> GetRateFixingsAsync(string indexReference, DateOnly from, DateOnly to, CancellationToken ct = default)
        {
            Reads.Add((indexReference, from, to));
            return Task.FromResult(ApiResponse<IntercompanyRateFixingDto[]>.Ok([Current]));
        }
        public Task<ApiResponse<IntercompanyRateFixingDto>> ProposeRateFixingAsync(ProposeIntercompanyRateFixingRequest request, CancellationToken ct = default)
        {
            Proposals.Add(request);
            return Task.FromResult(ApiResponse<IntercompanyRateFixingDto>.Ok(Current));
        }
        public Task<ApiResponse<IntercompanyRateFixingDto>> DecideRateFixingAsync(Guid fixingId, DecideIntercompanyRateFixingRequest request, CancellationToken ct = default)
        {
            Decisions.Add((fixingId, request));
            return Task.FromResult(DecisionResult?.Invoke(request) ?? ApiResponse<IntercompanyRateFixingDto>.Ok(Current));
        }

        public Task<ApiResponse<IntercompanyAgreementListDto>> ListAsync(IntercompanyAgreementQueryDto query, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ApiResponse<IntercompanyAgreementDetailDto>> GetAsync(Guid agreementId, DateOnly asOf, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ApiResponse<IntercompanyAgreementJournalLinesDto>> GetJournalLinesAsync(Guid agreementId, Guid ledgerBookId, DateOnly asOf, string? cursor = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ApiResponse<IntercompanyAgreementCommandResultDto>> ExecuteAsync(string action, IntercompanyAgreementCommandDto command, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
