using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Ledger;
using Meridian.Storage.Ledger;

namespace Meridian.Tests.Storage;

public sealed class IntercompanyAccountBindingRulesTests
{
    [Fact]
    public void EffectiveResolution_RetainsOriginalThenSuccessorWithoutMutatingHistory()
    {
        var initial = Binding();
        var renamed = Successor(initial) with
        {
            AccountPath = "Assets:Group Receivables",
            AccountName = "Group Receivables",
            HistoricalNames = [initial.AccountName],
            EffectiveFrom = new DateOnly(2026, 7, 1)
        };
        IntercompanyAccountBindingRules.ValidateAppend(renamed, initial.RevisionId, [initial]);
        IntercompanyAccountBindingRules.Resolve([initial, renamed], new DateOnly(2026, 6, 30))
            .Should().ContainSingle().Which.RevisionId.Should().Be(initial.RevisionId);
        IntercompanyAccountBindingRules.Resolve([initial, renamed], new DateOnly(2026, 7, 1))
            .Should().ContainSingle().Which.RevisionId.Should().Be(renamed.RevisionId);
        initial.AccountName.Should().Be("Intercompany Receivable");
    }

    [Fact]
    public void EffectiveWindow_IsHalfOpenAndDoesNotReviveSupersededBinding()
    {
        var initial = Binding();
        var successor = Successor(initial) with { EffectiveTo = new DateOnly(2026, 8, 1) };
        IntercompanyAccountBindingRules.Resolve([initial, successor], new DateOnly(2026, 8, 1)).Should().BeEmpty();
        IntercompanyAccountBindingRules.Resolve([initial], initial.EffectiveFrom.AddDays(-1)).Should().BeEmpty();
    }

    [Fact]
    public void RenameCannotEraseAnOldNameOrReuseItOnAnotherNode()
    {
        var initial = Binding();
        var renamed = Successor(initial) with { AccountName = "Group Receivable", HistoricalNames = [] };
        var losingAlias = () => IntercompanyAccountBindingRules.ValidateAppend(renamed, initial.RevisionId, [initial]);
        losingAlias.Should().Throw<InvalidOperationException>().WithMessage("*retain all previous account names*");

        var otherNode = Binding() with { ChartAccountNodeId = "other-receivable", HistoricalNames = [initial.AccountName] };
        var reuse = () => IntercompanyAccountBindingRules.ValidateAppend(otherNode, null, [initial]);
        reuse.Should().Throw<InvalidOperationException>().WithMessage("*historical account name*");
    }

    [Fact]
    public void ConcurrentSuccessorMustNameTheCurrentRevision()
    {
        var initial = Binding();
        var successor = Successor(initial);
        var competing = Successor(initial);
        var stale = () => IntercompanyAccountBindingRules.ValidateAppend(competing, initial.RevisionId, [initial, successor]);
        stale.Should().Throw<InvalidOperationException>().WithMessage("*revision changed*");
    }

    [Fact]
    public void StableNodeCannotBeAssignedASecondBindingEvenUnderAnotherRole()
    {
        var initial = Binding();
        var conflicting = Binding() with { Role = IntercompanyAccountRoleDto.Expense, AccountType = "Expense", AccountName = "Cost" };
        var append = () => IntercompanyAccountBindingRules.ValidateAppend(conflicting, null, [initial]);
        append.Should().Throw<InvalidOperationException>().WithMessage("*stable chart node*");
    }

    [Fact]
    public void SuccessorCannotChangeRoleOrBackdateBeforeRetainedAuthority()
    {
        var initial = Binding();
        var changedRole = Successor(initial) with { Role = IntercompanyAccountRoleDto.Payable, AccountType = "Liability" };
        var change = () => IntercompanyAccountBindingRules.ValidateAppend(changedRole, initial.RevisionId, [initial]);
        change.Should().Throw<InvalidOperationException>().WithMessage("*cannot change*role*");
        var backdated = Successor(initial) with { EffectiveFrom = initial.EffectiveFrom.AddDays(-1) };
        var backdate = () => IntercompanyAccountBindingRules.ValidateAppend(backdated, initial.RevisionId, [initial]);
        backdate.Should().Throw<InvalidOperationException>().WithMessage("*cannot precede*");
    }

    [Theory]
    [InlineData(IntercompanyAccountRoleDto.Receivable, "Liability")]
    [InlineData(IntercompanyAccountRoleDto.Payable, "Asset")]
    [InlineData(IntercompanyAccountRoleDto.Revenue, "Expense")]
    [InlineData(IntercompanyAccountRoleDto.TranslationReserve, "Revenue")]
    public void InvalidRoleTypePairFailsClosed(IntercompanyAccountRoleDto role, string accountType)
    {
        var binding = Binding() with { Role = role, AccountType = accountType };
        var append = () => IntercompanyAccountBindingRules.ValidateAppend(binding, null, []);
        append.Should().Throw<InvalidOperationException>().WithMessage("*incompatible*");
    }

    [Fact]
    public void ForeignCompanyHistoryCannotBeUsedAsAuthority()
    {
        var initial = Binding();
        var successor = Successor(initial) with { CompanyId = "foreign-company" };
        var append = () => IntercompanyAccountBindingRules.ValidateAppend(successor, initial.RevisionId, [initial]);
        append.Should().Throw<InvalidOperationException>().WithMessage("*same owning book, tenant and company*");
    }

    [Fact]
    public void LegacyChartJsonOmitsUnassignedRole()
    {
        var chart = new ChartOfAccountsNodeDto("cash", "Assets:Cash", "Cash", "Asset");
        var json = JsonSerializer.Serialize(chart);
        json.Should().NotContain("IntercompanyRole");
        JsonSerializer.Serialize(chart with { IntercompanyRole = IntercompanyAccountRoleDto.Receivable })
            .Should().Contain("IntercompanyRole");
    }

    internal static IntercompanyAccountBindingDto Binding()
        => new(Guid.NewGuid(), Guid.NewGuid(), 1, BookId, "ic-receivable", IntercompanyAccountRoleDto.Receivable,
            "Assets:Intercompany Receivable", "Intercompany Receivable", "Asset", new DateOnly(2026, 1, 1), null,
            [], "tenant-a", "company-a", "operator", DateTimeOffset.Parse("2026-01-01T12:00:00Z"), "Initial governed binding");

    internal static IntercompanyAccountBindingDto Successor(IntercompanyAccountBindingDto initial)
        => initial with
        {
            RevisionId = Guid.NewGuid(),
            RevisionNumber = initial.RevisionNumber + 1,
            PreviousRevisionId = initial.RevisionId,
            RecordedAt = initial.RecordedAt.AddDays(1),
            EffectiveFrom = initial.EffectiveFrom.AddDays(1),
            Reason = "Reviewed account mapping successor"
        };

    private static readonly Guid BookId = Guid.Parse("23863b67-0acd-4329-a759-62ed3ab59e9a");
}
