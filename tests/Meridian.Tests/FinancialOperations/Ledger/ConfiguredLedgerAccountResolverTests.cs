using FluentAssertions;
using Meridian.Contracts.Ledger;
using Meridian.FinancialOperations.Ledger;
using NSubstitute;

namespace Meridian.Tests.FinancialOperations.Ledger;

public sealed class ConfiguredLedgerAccountResolverTests
{
    [Theory]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("archived")]
    [InlineData("wrong-type")]
    [InlineData("wrong-account")]
    [InlineData("wrong-revision")]
    public async Task Resolve_refuses_ambiguous_or_unauthorized_mapping_instead_of_guessing(string failure)
    {
        var bookId = Guid.NewGuid();
        var node = new ChartOfAccountsNodeDto("cash", "Assets:Cash", "Cash", "Asset", FinancialAccountId: "account");
        var chart = failure switch
        {
            "missing" => Array.Empty<ChartOfAccountsNodeDto>(),
            "duplicate" => new[] { node, node with { NodeId = "duplicate" } },
            "archived" => new[] { node with { IsArchived = true } },
            "wrong-type" => new[] { node with { AccountType = "Expense" } },
            "wrong-account" => new[] { node with { FinancialAccountId = "other" } },
            _ => new[] { node }
        };
        var workspace = new AccountingConfigurationWorkspaceDto("fund", bookId, AccountingConfigurationStatusDto.Active,
            failure == "wrong-revision" ? "changed" : "v1", DateTimeOffset.UtcNow, [], chart, [], [], [], [], TenantId: "tenant");
        var config = Substitute.For<IAccountingConfigurationService>();
        config.GetWorkspaceAsync("fund", bookId, Arg.Any<CancellationToken>(), "tenant", null).Returns(workspace);
        var resolver = new ConfiguredLedgerAccountResolver(config);
        var resolve = () => resolver.ResolveAsync(new("tenant", null, "fund", bookId, "Assets:Cash", "Asset", "v1",
            FinancialAccountId: "account"));
        await resolve.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Policy_edit_waits_for_posting_authority_lease_and_then_blocks_further_automatic_posts()
    {
        var date = new DateOnly(2026, 5, 1);
        var policies = new AccountingPolicyService();
        var create = new CreateAccountingPolicyRequest(AccountingBasisKindDto.Primary, "synthetic", "v1", "Synthetic", date,
            RulePack: new("synthetic", "v1", [new("trade-rule", AccountingTreatmentKindDto.General,
                SourceEventType: "execution.trade-fill", RequiresApproval: false, AllowsAutoPosting: true)]));
        await policies.CreatePolicyAsync(create);
        var authority = new AccountingAutomaticPostingPolicyValidator(policies);
        var lease = await authority.AcquireAuthorityAsync("synthetic", "v1", AccountingBasisKindDto.Primary, date, ["execution.trade-fill"]);
        var edit = policies.CreatePolicyAsync(create with
        {
            RulePack = create.RulePack! with
            { Rules = [create.RulePack!.Rules[0] with { RequiresApproval = true, AllowsAutoPosting = false }] }
        });
        edit.IsCompleted.Should().BeFalse("the posting owns policy mutation authority until its transaction finishes");
        await lease.DisposeAsync();
        await edit;
        var automatic = () => authority.AcquireAuthorityAsync("synthetic", "v1", AccountingBasisKindDto.Primary, date, ["execution.trade-fill"]);
        await automatic.Should().ThrowAsync<InvalidOperationException>().WithMessage("*authorizing automatic posting*");
    }
    [Fact]
    public async Task Retained_operator_policy_restores_after_restart_but_never_overwrites_a_changed_version()
    {
        var date = new DateOnly(2026, 5, 1);
        var original = new AccountingPolicyService();
        var snapshot = await original.CreatePolicyAsync(new CreateAccountingPolicyRequest(AccountingBasisKindDto.Primary,
            "retained-synthetic", "v1", "Synthetic", date,
            RulePack: new("retained-synthetic", "v1", [new("trade-rule", AccountingTreatmentKindDto.General,
                SourceEventType: "execution.trade-fill", RequiresApproval: false, AllowsAutoPosting: true)])));
        var restarted = new AccountingPolicyService();
        var validator = new AccountingAutomaticPostingPolicyValidator(restarted);
        await using (await validator.AcquireRetainedAuthorityAsync(snapshot, date, ["execution.trade-fill"]))
        {
            var rule = await validator.ResolveRuleAsync(snapshot.PolicyId, snapshot.Version, snapshot.AccountingBasis,
                date, "execution.trade-fill");
            rule.RuleId.Should().Be("trade-rule");
        }
        await restarted.CreatePolicyAsync(new CreateAccountingPolicyRequest(snapshot.AccountingBasis, snapshot.PolicyId,
            snapshot.Version, "Changed policy under same version", date, RulePack: snapshot.RulePack));
        var overwrite = () => validator.AcquireRetainedAuthorityAsync(snapshot, date, ["execution.trade-fill"]);
        await overwrite.Should().ThrowAsync<InvalidOperationException>().WithMessage("*differs from the immutable operator-authorized snapshot*");
    }

}
