using Meridian.Contracts.Workstation;
using Meridian.Wpf.Services;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Tests.ViewModels;

/// <summary>
/// The desktop scope picker narrows the shared scope-options payload by the browser picker's rule:
/// an entity keeps the funds holding it and the accounts booked to it or held by those funds; a
/// fund keeps its own accounts; accounts with no stated relationship are left out and counted.
/// </summary>
public sealed class WorkstationScopeOptionsViewModelTests
{
    private static readonly Guid EntityA = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid EntityB = Guid.Parse("00000000-0000-0000-0000-00000000000b");
    private static readonly Guid FundOne = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid FundTwo = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid FundThree = Guid.Parse("00000000-0000-0000-0000-000000000003");
    private static readonly Guid BookedToA = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    private static readonly Guid HeldByFundOne = Guid.Parse("00000000-0000-0000-0000-0000000000a2");
    private static readonly Guid HeldByFundTwo = Guid.Parse("00000000-0000-0000-0000-0000000000b1");
    private static readonly Guid FundThreeBookedToA = Guid.Parse("00000000-0000-0000-0000-0000000000a3");
    private static readonly Guid Orphan = Guid.Parse("00000000-0000-0000-0000-0000000000ff");

    // Fund One holds Entity A; Fund Two holds Entity B; Fund Three holds no entity, but one of its
    // accounts is booked to Entity A, which links it to A as the browser rule does.
    private static WorkstationScopeOptionsDto Options() => new(
        [
            new WorkstationScopeEntityOptionDto(EntityA, "Entity A"),
            new WorkstationScopeEntityOptionDto(EntityB, "Entity B")
        ],
        [
            new WorkstationScopeFundOptionDto(FundOne, "Fund One", [EntityA]),
            new WorkstationScopeFundOptionDto(FundTwo, "Fund Two", [EntityB]),
            new WorkstationScopeFundOptionDto(FundThree, "Fund Three", [])
        ],
        [
            new WorkstationScopeAccountOptionDto(BookedToA, "Booked to A", FundId: null, EntityId: EntityA),
            new WorkstationScopeAccountOptionDto(HeldByFundOne, "Held by Fund One", FundId: FundOne, EntityId: null),
            new WorkstationScopeAccountOptionDto(HeldByFundTwo, "Held by Fund Two", FundId: FundTwo, EntityId: null),
            new WorkstationScopeAccountOptionDto(FundThreeBookedToA, "Fund Three booked to A", FundId: FundThree, EntityId: EntityA),
            new WorkstationScopeAccountOptionDto(Orphan, "No relationship", FundId: null, EntityId: null)
        ]);

    [Fact]
    public void Constrain_ByEntity_KeepsHoldingFundsAndBookedOrHeldAccounts()
    {
        var narrowed = WorkstationScopeOptionsNarrowing.Constrain(Options(), optionsAvailable: true, EntityA, fundId: null);

        narrowed.FundsConstrained.Should().BeTrue();
        narrowed.Funds.Select(fund => fund.FundId).Should().BeEquivalentTo(new[] { FundOne, FundThree });
        narrowed.Accounts.Select(account => account.AccountId).Should().BeEquivalentTo(new[] { BookedToA, HeldByFundOne, FundThreeBookedToA });
        narrowed.AccountsWithoutKnownRelationship.Should().Be(1, "the orphan account cannot be placed under any entity");
    }

    [Fact]
    public void Constrain_ByFund_KeepsOnlyItsAccountsAndCountsAccountsWithoutAFund()
    {
        var narrowed = WorkstationScopeOptionsNarrowing.Constrain(Options(), optionsAvailable: true, entityId: null, FundTwo);

        narrowed.FundsConstrained.Should().BeFalse();
        narrowed.Funds.Should().HaveCount(3);
        narrowed.Accounts.Select(account => account.AccountId).Should().Equal(HeldByFundTwo);
        narrowed.AccountsWithoutKnownRelationship.Should().Be(2, "'Booked to A' and the orphan state no fund");
    }

    [Fact]
    public void Constrain_CountsAccountsWhoseRelationshipIsWithheldSeparately()
    {
        var hiddenFund = Guid.Parse("00000000-0000-0000-0000-0000000000c1");
        var source = Options();
        var options = source with
        {
            Accounts =
            [
                .. source.Accounts,
                new WorkstationScopeAccountOptionDto(hiddenFund, "Fund not visible", FundId: null, EntityId: null, FundWithheld: true, EntityWithheld: true)
            ]
        };

        var byFund = WorkstationScopeOptionsNarrowing.Constrain(options, optionsAvailable: true, entityId: null, FundTwo);
        byFund.AccountsWithoutKnownRelationship.Should().Be(2, "'Booked to A' and the orphan state no fund");
        byFund.AccountsWithWithheldRelationship.Should().Be(1, "the account's fund exists but is not visible to the caller");

        var byEntity = WorkstationScopeOptionsNarrowing.Constrain(options, optionsAvailable: true, EntityA, fundId: null);
        byEntity.AccountsWithoutKnownRelationship.Should().Be(1);
        byEntity.AccountsWithWithheldRelationship.Should().Be(1);
    }

    [Fact]
    public void Constrain_WhenOptionsUnavailable_NarrowsNothing()
    {
        var narrowed = WorkstationScopeOptionsNarrowing.Constrain(Options(), optionsAvailable: false, EntityA, FundOne);

        narrowed.AccountsConstrained.Should().BeFalse();
        narrowed.Accounts.Should().HaveCount(5);
        narrowed.AccountsWithoutKnownRelationship.Should().Be(0);
    }

    [Fact]
    public async Task SelectingEntity_NarrowsFundsAndAccountsAndReportsHiddenAccounts()
    {
        var viewModel = new WorkstationScopeOptionsViewModel(new StubClient(Loaded(Options())));
        await viewModel.InitializeAsync();

        viewModel.Entities.Should().HaveCount(2);
        viewModel.Funds.Should().HaveCount(3);
        viewModel.Accounts.Should().HaveCount(5);
        viewModel.NarrowingText.Should().BeEmpty();

        viewModel.SelectedEntity = viewModel.Entities.Single(entity => entity.EntityId == EntityA);

        viewModel.Funds.Select(fund => fund.FundId).Should().BeEquivalentTo(new[] { FundOne, FundThree });
        viewModel.Accounts.Select(account => account.AccountId).Should().BeEquivalentTo(new[] { BookedToA, HeldByFundOne, FundThreeBookedToA });
        viewModel.NarrowingText.Should().Contain("Showing 3 of 5 accounts").And.Contain("1 account with no stated fund or entity is left out");
        viewModel.SelectedScopeContextKey.Should().Be($"Entity:{EntityA:D}");
    }

    [Fact]
    public async Task SelectingConflictingEntity_ClearsFundAndAccountOutsideIt()
    {
        var viewModel = new WorkstationScopeOptionsViewModel(new StubClient(Loaded(Options())));
        await viewModel.InitializeAsync();
        viewModel.SelectedFund = viewModel.Funds.Single(fund => fund.FundId == FundTwo);
        viewModel.SelectedAccount = viewModel.Accounts.Single(account => account.AccountId == HeldByFundTwo);
        viewModel.SelectedScopeContextKey.Should().Be($"Account:{HeldByFundTwo:D}");

        viewModel.SelectedEntity = viewModel.Entities.Single(entity => entity.EntityId == EntityA);

        viewModel.SelectedFund.Should().BeNull("Fund Two does not hold Entity A");
        viewModel.SelectedAccount.Should().BeNull("the account sits under Fund Two, outside Entity A");
        viewModel.SelectedScopeContextKey.Should().Be($"Entity:{EntityA:D}");
    }

    [Fact]
    public async Task Reload_DropsARetainedAccountThatMovedOutsideTheChosenFund()
    {
        var source = Options();
        var moved = source with
        {
            Accounts = source.Accounts
                .Select(account => account.AccountId == HeldByFundOne ? account with { FundId = FundTwo } : account)
                .ToArray()
        };
        var client = new SequenceClient(Loaded(source), Loaded(moved));
        var viewModel = new WorkstationScopeOptionsViewModel(client);
        await viewModel.InitializeAsync();
        viewModel.SelectedFund = viewModel.Funds.Single(fund => fund.FundId == FundOne);
        viewModel.SelectedAccount = viewModel.Accounts.Single(account => account.AccountId == HeldByFundOne);
        viewModel.SelectedScopeContextKey.Should().Be($"Account:{HeldByFundOne:D}");

        await viewModel.LoadAsync();

        viewModel.SelectedFund!.FundId.Should().Be(FundOne);
        viewModel.SelectedAccount.Should().BeNull("the account now sits under Fund Two, outside the chosen fund");
        viewModel.SelectedScopeContextKey.Should().Be($"Fund:{FundOne:D}");
    }

    [Fact]
    public async Task Reload_DropsARetainedFundThatNoLongerHoldsTheChosenEntity()
    {
        var source = Options();
        var released = source with
        {
            Funds = source.Funds
                .Select(fund => fund.FundId == FundOne ? fund with { EntityIds = [EntityB] } : fund)
                .ToArray(),
            Accounts = source.Accounts.Where(account => account.AccountId != HeldByFundOne).ToArray()
        };
        var client = new SequenceClient(Loaded(source), Loaded(released));
        var viewModel = new WorkstationScopeOptionsViewModel(client);
        await viewModel.InitializeAsync();
        viewModel.SelectedEntity = viewModel.Entities.Single(entity => entity.EntityId == EntityA);
        viewModel.SelectedFund = viewModel.Funds.Single(fund => fund.FundId == FundOne);

        await viewModel.LoadAsync();

        viewModel.SelectedEntity!.EntityId.Should().Be(EntityA);
        viewModel.SelectedFund.Should().BeNull("Fund One no longer holds Entity A");
        viewModel.SelectedScopeContextKey.Should().Be($"Entity:{EntityA:D}");
    }

    [Fact]
    public async Task UnavailableOptions_ShowStatusAndEmptyLists()
    {
        var viewModel = new WorkstationScopeOptionsViewModel(new StubClient(new WorkstationScopeOptionsLoadResult(
            WorkstationScopeOptionsSource.Unavailable,
            new WorkstationScopeOptionsDto([], [], []),
            "Entity, fund and account lists could not be loaded because the workstation host could not be reached.")));

        await viewModel.InitializeAsync();

        viewModel.IsAvailable.Should().BeFalse();
        viewModel.Accounts.Should().BeEmpty();
        viewModel.StatusText.Should().Contain("could not be reached");
    }

    [Fact]
    public async Task ClientThrowing_IsReportedAsUnavailableRatherThanSilentlyEmpty()
    {
        var viewModel = new WorkstationScopeOptionsViewModel(new ThrowingClient());

        await viewModel.InitializeAsync();

        viewModel.IsAvailable.Should().BeFalse();
        viewModel.IsLoading.Should().BeFalse();
        viewModel.StatusText.Should().Contain("could not be loaded");
    }

    private static WorkstationScopeOptionsLoadResult Loaded(WorkstationScopeOptionsDto options)
        => new(WorkstationScopeOptionsSource.ScopeOptionsRoute, options, "Scope options loaded.");

    private sealed class StubClient(WorkstationScopeOptionsLoadResult result) : IWorkstationScopeOptionsApiClient
    {
        public Task<WorkstationScopeOptionsLoadResult> GetScopeOptionsAsync(CancellationToken ct = default)
            => Task.FromResult(result);
    }

    private sealed class SequenceClient(params WorkstationScopeOptionsLoadResult[] results) : IWorkstationScopeOptionsApiClient
    {
        private int _next;

        public Task<WorkstationScopeOptionsLoadResult> GetScopeOptionsAsync(CancellationToken ct = default)
            => Task.FromResult(results[Math.Min(_next++, results.Length - 1)]);
    }

    private sealed class ThrowingClient : IWorkstationScopeOptionsApiClient
    {
        public Task<WorkstationScopeOptionsLoadResult> GetScopeOptionsAsync(CancellationToken ct = default)
            => throw new InvalidOperationException("boom");
    }
}
