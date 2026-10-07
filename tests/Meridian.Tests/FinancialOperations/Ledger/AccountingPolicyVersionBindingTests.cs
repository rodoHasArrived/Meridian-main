using Meridian.Contracts.Ledger;
using Meridian.FinancialOperations.Ledger;
using Meridian.Ledger;

namespace Meridian.Tests.FinancialOperations.Ledger;

public sealed class AccountingPolicyVersionBindingTests
{
    private static readonly DateOnly Date = new(2026, 6, 30);
    private static readonly Guid NodeId = Guid.Parse("aaaaaaaa-1111-1111-1111-111111111111");
    private static readonly Guid SourceId = Guid.Parse("bbbbbbbb-2222-2222-2222-222222222222");

    [Fact]
    public async Task ResolvePolicy_ExplicitVersionPreservesBindingWhileUnpinnedQueryUsesExistingRanking()
    {
        var policies = new AccountingPolicyService();
        var retained = await policies.CreatePolicyAsync(Policy("v1"));
        var preferred = await policies.CreatePolicyAsync(Policy("v2") with { IsDefault = true });
        var query = Query();

        Assert.Same(preferred, await policies.ResolvePolicyAsync(query));
        Assert.Same(retained, await policies.ResolvePolicyAsync(query with { PolicyVersion = "v1" }));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("future")]
    [InlineData("expired")]
    [InlineData("basis")]
    [InlineData("fund")]
    [InlineData("node")]
    [InlineData("instrument")]
    [InlineData("source")]
    public async Task ResolvePolicy_ExplicitVersionStillRequiresApplicableDateAndScopeWithoutFallback(string mismatch)
    {
        var policies = new AccountingPolicyService();
        var requested = Policy("v1");
        requested = mismatch switch
        {
            "missing" => requested with { Version = "another-version" },
            "future" => requested with { EffectiveFrom = Date.AddDays(1) },
            "expired" => requested with { EffectiveTo = Date.AddDays(-1) },
            "basis" => requested with { AccountingBasis = AccountingBasisKindDto.Tax },
            "fund" => requested with { FundProfileId = "another-fund" },
            "node" => requested with { FundStructureNodeId = Guid.NewGuid() },
            "instrument" => requested with { InstrumentId = "another-instrument" },
            "source" => requested with { SourceEventId = Guid.NewGuid() },
            _ => throw new ArgumentOutOfRangeException(nameof(mismatch))
        };
        await policies.CreatePolicyAsync(requested);
        var applicableOtherVersion = await policies.CreatePolicyAsync(Policy("v2") with { IsDefault = true });

        Assert.Same(applicableOtherVersion, await policies.ResolvePolicyAsync(Query()));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            policies.ResolvePolicyAsync(Query() with { PolicyVersion = "v1" }));
    }

    [Fact]
    public async Task BuildDraft_ExplicitVersionIsUsedByRuleSelectionAndProjectedWrite()
    {
        var policies = new AccountingPolicyService();
        await policies.CreatePolicyAsync(Policy("v1"));
        await policies.CreatePolicyAsync(Policy("v2") with { IsDefault = true });
        var service = new AccountingJournalDraftService(policies, new AccountingBasisProjectionService(policies));

        var draft = await service.BuildDraftAsync(new AccountingJournalDraftRequest(
            Guid.NewGuid(), Guid.NewGuid(), new DateTimeOffset(2026, 6, 30, 0, 0, 0, TimeSpan.Zero), "Pinned policy draft",
            [new(new LedgerAccount("Cash", LedgerAccountType.Asset), 25m, 0m),
                new(new LedgerAccount("Payable", LedgerAccountType.Liability), 0m, 25m)],
            EffectiveDate: Date, PolicyId: "version-bound", RuleId: "version-bound.general",
            LedgerBookId: Guid.NewGuid(), EvidenceLinks: ["evidence://policy/version-bound"], PolicyVersion: "v1"));

        Assert.True(draft.CanSubmitForApproval);
        Assert.False(draft.CanPostWithoutAdditionalApproval);
        Assert.Equal("v1", draft.Policy.Version);
        Assert.NotNull(draft.Write);
        Assert.Equal("v1", draft.Write.AccountingPolicyVersion);
        Assert.Equal("version-bound", draft.Write.AccountingPolicyId);
    }

    private static AccountingPolicyQuery Query() => new(AccountingBasisKindDto.Primary, Date,
        "version-bound", "fund", NodeId, "instrument", SourceId);

    private static CreateAccountingPolicyRequest Policy(string version) => new(
        AccountingBasisKindDto.Primary, "version-bound", version, "Version-bound policy", Date.AddMonths(-1),
        RulePack: new AccountingPolicyRulePackDto("version-bound.rules", "v1",
            [new AccountingPolicyRuleDto("version-bound.general", AccountingTreatmentKindDto.General,
                RuleVersion: "v1", RequiresEvidence: true, RequiresApproval: true, AllowsAutoPosting: false)]));
}
