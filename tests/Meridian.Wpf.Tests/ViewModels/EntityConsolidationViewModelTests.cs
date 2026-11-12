using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Ui.Services.Services.Accounting;
using Meridian.Wpf.ViewModels.Accounting;
using Xunit;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed class EntityConsolidationViewModelTests
{
    [Fact]
    public async Task Preview_PreservesServerAmountsAndInvalidatesOnScopeChange()
    {
        var client = new Client();
        var vm = Scope(client);
        await vm.PreviewAsync();
        var row = Assert.Single(vm.Balances);
        Assert.Equal(123.4567890123456789012345678m, row.GrossBalance);
        Assert.Equal(22.0000000000000000000000001m, row.ConsolidatedBalance);
        Assert.Equal(11.0000000000000000000000002m, row.PreviewBalance);
        Assert.True(vm.CreateDraftsCommand.CanExecute(null));
        Assert.Contains("w10-v4", vm.ScopeDetail);
        vm.AsOf = vm.AsOf!.Value.AddDays(1);
        Assert.Empty(vm.Balances);
        Assert.Null(vm.Preview);
        Assert.False(vm.CreateDraftsCommand.CanExecute(null));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task DraftCreation_RequiresServerCapabilityAndNoBlockers(bool allowed, bool blocker)
    {
        var client = new Client { Allowed = allowed, Blocked = blocker };
        var vm = Scope(client);
        await vm.PreviewAsync();
        await vm.CreateDraftsAsync();
        Assert.False(vm.CreateDraftsCommand.CanExecute(null));
        Assert.Equal(0, client.CreateCalls);
    }

    [Fact]
    public async Task LateResponse_CannotRestoreAnOldScope()
    {
        var response = new TaskCompletionSource<ConsolidationViewDto>(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new Client { PendingPreview = response.Task };
        var vm = Scope(client);
        var read = vm.PreviewAsync();
        var oldRequest = client.LastRequest!;
        vm.AsOf = vm.AsOf!.Value.AddDays(1);
        response.SetResult(Client.View(oldRequest, true, false));
        await read;
        Assert.Null(vm.Preview);
        Assert.Empty(vm.Balances);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task RefusedDraftCommand_RequiresFreshPreviewAndNeverClaimsPosting()
    {
        var client = new Client { RefuseCreate = true };
        var vm = Scope(client);
        await vm.PreviewAsync();
        await vm.CreateDraftsAsync();
        Assert.Contains("Unavailable", vm.Status);
        Assert.Null(vm.Preview);
        Assert.Empty(vm.Balances);
        Assert.False(vm.CreateDraftsCommand.CanExecute(null));
        Assert.Equal(1, client.CreateCalls);
    }

    [Fact]
    public void MissingClient_IsExplicitlyUnavailable()
    {
        var vm = new EntityConsolidationViewModel(null);
        Assert.Contains("unavailable", vm.Status);
        Assert.False(vm.LoadChoicesCommand.CanExecute(null));
        Assert.False(vm.PreviewCommand.CanExecute(null));
    }

    private static EntityConsolidationViewModel Scope(Client client)
    {
        var now = DateTimeOffset.UtcNow;
        var vm = new EntityConsolidationViewModel(client)
        {
            SelectedOrganization = new(Guid.NewGuid(), "ORG", "Organization", "USD", true, now.AddYears(-1), null, []),
            SelectedOwnershipRoot = new(Guid.NewGuid(), null, "FUND", "Fund", "USD", true, now.AddYears(-1), null, [], [], [], [], []),
            SelectedEliminationBook = client.Book,
            SelectedPeriod = client.Period,
            AsOf = new DateTime(2026, 6, 30)
        };
        return vm;
    }

    private sealed class Client : IWorkstationEntityConsolidationApiClient
    {
        public bool Allowed { get; init; } = true;
        public bool Blocked { get; init; }
        public bool RefuseCreate { get; init; }
        public int CreateCalls { get; private set; }
        public Task<ConsolidationViewDto>? PendingPreview { get; init; }
        public ConsolidationRequestDto? LastRequest { get; private set; }
        public LedgerBookDto Book { get; } = new(Guid.NewGuid(), "fund", Guid.NewGuid(), FundStructureNodeKindDto.Fund,
            "Elimination book", "USD", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        public LedgerPeriodDto Period => new(Guid.NewGuid(), Book.LedgerBookId, 2026, 6, "June 2026",
            new(2026, 6, 1), new(2026, 6, 30), LedgerPeriodStatusDto.Open, DateTimeOffset.UtcNow, null, 1);
        public Task<OrganizationStructureGraphDto> GetStructureAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<EntityTimelineDto> GetTimelineAsync(Guid organizationId, Guid entityId, DateTimeOffset asOf, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<LedgerPeriodDto>> GetPeriodsAsync(Guid bookId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<LedgerPeriodDto>>([Period]);
        public Task<ConsolidationViewDto> PreviewAsync(ConsolidationRequestDto request, CancellationToken ct = default)
        {
            LastRequest = request;
            return PendingPreview ?? Task.FromResult(View(request, Allowed, Blocked));
        }
        public Task<ConsolidationViewDto> CreateDraftsAsync(ConsolidationRequestDto request, CancellationToken ct = default)
        {
            CreateCalls++;
            if (RefuseCreate) throw new InvalidOperationException("Source evidence changed; repeat preview.");
            return Task.FromResult(View(request, Allowed, Blocked));
        }
        public static ConsolidationViewDto View(ConsolidationRequestDto request, bool allowed, bool blocked)
            => new(request, "fund", "USD", "Approved retained policy", [], [],
                [new("Assets:Receivable", "Asset", 123.4567890123456789012345678m, -11m, -101m,
                    22.0000000000000000000000001m, 11.0000000000000000000000002m, [])], [], [],
                blocked ? ["Required FX evidence is unavailable."] : [], "retained-source-hash", allowed)
            { RuleVersion = "w10-v4", TranslationFingerprint = "retained-rate-set-hash" };
    }
}
