using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster.CorporateActions;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Tenancy;
using NSubstitute;
using Xunit;

namespace Meridian.Tests.Tenancy;

/// <summary>
/// The policy that stands between a globally observed provider fact and the per-scope casework it
/// creates. Every refusal here is a case where applying the decision would either act on another
/// tenant's behalf or leave part of the affected set un-cased.
/// </summary>
public sealed class CorporateActionScopeFanOutGateTests
{
    private static readonly Guid SecurityId = Guid.Parse("2f3a9b1c-5d47-4a86-9f10-6c0b2e7d8a53");
    private static readonly DateOnly RecordDate = new(2026, 8, 14);

    private static AuthoritativeScopeAssignment Assignment(
        string tenantId = "tenant-a",
        string companyId = "company-a",
        string fundProfileId = "fund-1") =>
        new(
            tenantId,
            companyId,
            "test-authority",
            DateTimeOffset.UnixEpoch,
            FundProfileId: fundProfileId,
            FinancialAccountId: "account-1",
            LedgerBookId: "book-1",
            FunctionalCurrency: "USD");

    private sealed class Fixture
    {
        public IAuthoritativeScopeFanOutService FanOut { get; } =
            Substitute.For<IAuthoritativeScopeFanOutService>();

        public ISecurityMasterQueryService Securities { get; } =
            Substitute.For<ISecurityMasterQueryService>();

        public CorporateActionScopeFanOutGate Gate => new(FanOut, Securities);

        public Fixture()
        {
            WithSecurityIdentifiers(new SecurityIdentifierDto(
                SecurityIdentifierKind.Cusip,
                "037833100",
                IsPrimary: true,
                ValidFrom: DateTimeOffset.UnixEpoch));
        }

        public void WithSecurityIdentifiers(params SecurityIdentifierDto[] identifiers)
            => Securities.GetByIdAsync(SecurityId, Arg.Any<CancellationToken>())
                .Returns(new SecurityDetailDto(
                    SecurityId,
                    AssetClass: "Equity",
                    Status: SecurityStatusDto.Active,
                    DisplayName: "Meridian Corp",
                    Currency: "USD",
                    CommonTerms: default(JsonElement),
                    AssetSpecificTerms: default(JsonElement),
                    Identifiers: identifiers,
                    Aliases: [],
                    Version: 1,
                    EffectiveFrom: DateTimeOffset.UnixEpoch,
                    EffectiveTo: null));

        public void WithUnknownSecurity()
            => Securities.GetByIdAsync(SecurityId, Arg.Any<CancellationToken>())
                .Returns((SecurityDetailDto?)null);

        public void WithFanOut(ScopeFanOutResult result)
            => FanOut.ResolveAffectedScopesAsync(Arg.Any<ScopeFanOutRequest>(), Arg.Any<CancellationToken>())
                .Returns(result);
    }

    private static Task<CorporateActionScopeFanOutDecision> Decide(Fixture fixture) =>
        fixture.Gate.ResolveDecisionScopeAsync(SecurityId, RecordDate, "tenant-a", "company-a");

    [Fact]
    public async Task Decide_WhenExactlyOneOwnedScopeIsAffected_ResolvesTheServerOwnedNarrowScope()
    {
        var fixture = new Fixture();
        fixture.WithFanOut(ScopeFanOutResult.Authoritative([Assignment()]));

        var decision = await Decide(fixture);

        decision.IsPermitted.Should().BeTrue();
        decision.Refusal.Should().Be(CorporateActionScopeFanOutRefusal.None);
        decision.ResolvedScope.Should().NotBeNull();
        decision.ResolvedScope!.TenantId.Should().Be("tenant-a");
        decision.ResolvedScope.FundProfileId.Should().Be("fund-1");
        decision.ResolvedScope.FinancialAccountId.Should().Be("account-1");
        decision.ResolvedScope.LedgerBookId.Should().Be("book-1");
    }

    [Fact]
    public async Task Decide_WhenTheFanOutIsNotAuthoritative_RefusesAndCarriesTheStatedReason()
    {
        var fixture = new Fixture();
        fixture.WithFanOut(ScopeFanOutResult.NotAuthoritative("holdings store unavailable"));

        var decision = await Decide(fixture);

        decision.IsPermitted.Should().BeFalse();
        decision.Refusal.Should().Be(CorporateActionScopeFanOutRefusal.NotAuthoritative);
        decision.Blockers.Should().Contain("holdings store unavailable");
    }

    [Fact]
    public async Task Decide_WhenTheFactReachesAnotherTenant_RefusesWithoutNamingThatTenant()
    {
        var fixture = new Fixture();
        fixture.WithFanOut(ScopeFanOutResult.Authoritative(
        [
            Assignment(),
            Assignment("tenant-b", "company-b", "fund-2"),
        ]));

        var decision = await Decide(fixture);

        decision.IsPermitted.Should().BeFalse();
        decision.Refusal.Should().Be(CorporateActionScopeFanOutRefusal.ForeignScope);
        decision.Blockers.Should().NotContainMatch("*tenant-b*");
        decision.Blockers.Should().NotContainMatch("*fund-2*");
    }

    [Fact]
    public async Task Decide_WhenSeveralOwnedScopesAreAffected_RefusesRatherThanCasingPartOfTheSet()
    {
        var fixture = new Fixture();
        fixture.WithFanOut(ScopeFanOutResult.Authoritative(
        [
            Assignment(fundProfileId: "fund-1"),
            Assignment(fundProfileId: "fund-2"),
        ]));

        var decision = await Decide(fixture);

        decision.IsPermitted.Should().BeFalse();
        decision.Refusal.Should().Be(CorporateActionScopeFanOutRefusal.MultiScope);
        decision.Blockers.Should().Contain(CorporateActionScopeFanOutGate.MultiScopeBlocker);
    }

    [Fact]
    public async Task Decide_WhenNoScopeHoldsTheSecurity_RefusesBecauseThereIsNoCaseToOpen()
    {
        var fixture = new Fixture();
        fixture.WithFanOut(ScopeFanOutResult.Authoritative([]));

        var decision = await Decide(fixture);

        decision.IsPermitted.Should().BeFalse();
        decision.Refusal.Should().Be(CorporateActionScopeFanOutRefusal.NoAffectedScope);
        decision.Blockers.Should().Contain(CorporateActionScopeFanOutGate.NoAffectedScopeBlocker);
    }

    [Fact]
    public async Task Decide_WhenTheSecurityIsUnknown_Refuses()
    {
        var fixture = new Fixture();
        fixture.WithUnknownSecurity();

        var decision = await Decide(fixture);

        decision.IsPermitted.Should().BeFalse();
        decision.Blockers.Should().Contain(CorporateActionScopeFanOutGate.UnknownSecurityBlocker);
    }

    [Fact]
    public async Task Decide_WhenTheSecurityCarriesNoIdentifiers_RefusesWithoutAskingForAFanOut()
    {
        var fixture = new Fixture();
        fixture.WithSecurityIdentifiers();

        var decision = await Decide(fixture);

        decision.IsPermitted.Should().BeFalse();
        decision.Blockers.Should().Contain(CorporateActionScopeFanOutGate.NoIdentifiersBlocker);
        await fixture.FanOut.DidNotReceive().ResolveAffectedScopesAsync(
            Arg.Any<ScopeFanOutRequest>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("", "company-a")]
    [InlineData("tenant-a", "")]
    public async Task Decide_WithoutResolvableScope_IsRejectedRatherThanDefaulted(
        string tenantId,
        string companyId)
    {
        var fixture = new Fixture();
        fixture.WithFanOut(ScopeFanOutResult.Authoritative([Assignment()]));

        var decision = await fixture.Gate.ResolveDecisionScopeAsync(
            SecurityId, RecordDate, tenantId, companyId);

        decision.IsPermitted.Should().BeFalse();
        decision.ResolvedScope.Should().BeNull();
    }
}
