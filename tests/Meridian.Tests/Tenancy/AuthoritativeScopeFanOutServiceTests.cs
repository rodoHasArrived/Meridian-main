using FluentAssertions;
using Meridian.Application.Tenancy;
using Meridian.Contracts.Tenancy;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meridian.Tests.Tenancy;

/// <summary>
/// Composition rules for the fan-out authority. The behaviour under test is the one the corporate
/// action decision boundary depends on: an incomplete answer must never be presentable as a
/// complete affected set, because a caller acting on it would apply a decision to some affected
/// tenants and silently miss the rest.
/// </summary>
public sealed class AuthoritativeScopeFanOutServiceTests
{
    private static readonly Guid SecurityId = Guid.Parse("2f3a9b1c-5d47-4a86-9f10-6c0b2e7d8a53");
    private static readonly DateOnly EffectiveDate = new(2026, 8, 14);

    private static ScopeFanOutRequest Request() =>
        new(SecurityId, [new ScopeFanOutIdentifier("Ticker", "MRDN")], EffectiveDate);

    private static AuthoritativeScopeAssignment Assignment(
        string tenantId,
        string companyId,
        string? fundProfileId = null,
        string authorityId = "test-authority") =>
        new(tenantId, companyId, authorityId, DateTimeOffset.UnixEpoch, FundProfileId: fundProfileId);

    private static AuthoritativeScopeFanOutService Service(params IScopeAssignmentProvider[] providers) =>
        new(providers, NullLogger<AuthoritativeScopeFanOutService>.Instance);

    [Fact]
    public async Task ResolveAffectedScopes_WithNoProviders_IsNotAuthoritative()
    {
        var result = await Service().ResolveAffectedScopesAsync(Request());

        result.IsAuthoritative.Should().BeFalse();
        result.Scopes.Should().BeEmpty();
        result.Blockers.Should().Contain(AuthoritativeScopeFanOutService.NoProvidersBlocker);
    }

    [Fact]
    public async Task ResolveAffectedScopes_WithNoIdentifiers_IsNotAuthoritative()
    {
        var service = Service(new StubProvider(ScopeAssignmentProviderResult.Authoritative([])));

        var result = await service.ResolveAffectedScopesAsync(
            new ScopeFanOutRequest(SecurityId, [], EffectiveDate));

        result.IsAuthoritative.Should().BeFalse();
        result.Blockers.Should().Contain(AuthoritativeScopeFanOutService.MissingIdentifiersBlocker);
    }

    [Fact]
    public async Task ResolveAffectedScopes_WhenEveryProviderIsComplete_IsAuthoritative()
    {
        var service = Service(
            new StubProvider(ScopeAssignmentProviderResult.Authoritative(
                [Assignment("tenant-a", "company-a", "fund-1")])),
            new StubProvider(ScopeAssignmentProviderResult.Authoritative(
                [Assignment("tenant-b", "company-b", "fund-2")])));

        var result = await service.ResolveAffectedScopesAsync(Request());

        result.IsAuthoritative.Should().BeTrue();
        result.Blockers.Should().BeEmpty();
        result.Scopes.Should().HaveCount(2);
    }

    [Fact]
    public async Task ResolveAffectedScopes_WhenOneProviderIsIncomplete_DegradesTheWholeResult()
    {
        var service = Service(
            new StubProvider(ScopeAssignmentProviderResult.Authoritative(
                [Assignment("tenant-a", "company-a", "fund-1")])),
            new StubProvider(ScopeAssignmentProviderResult.NotAuthoritative("holdings store unavailable")));

        var result = await service.ResolveAffectedScopesAsync(Request());

        result.IsAuthoritative.Should().BeFalse();
        result.Blockers.Should().Contain("holdings store unavailable");
    }

    [Fact]
    public async Task ResolveAffectedScopes_WhenAProviderThrows_DegradesInsteadOfImplyingCompleteness()
    {
        var service = Service(
            new StubProvider(ScopeAssignmentProviderResult.Authoritative(
                [Assignment("tenant-a", "company-a", "fund-1")])),
            new ThrowingProvider());

        var result = await service.ResolveAffectedScopesAsync(Request());

        result.IsAuthoritative.Should().BeFalse();
        result.Blockers.Should().ContainMatch("*affected scopes are unknown*");
    }

    [Fact]
    public async Task ResolveAffectedScopes_DeduplicatesTheSameScopeAssertedTwice()
    {
        var service = Service(
            new StubProvider(ScopeAssignmentProviderResult.Authoritative(
                [Assignment("tenant-a", "company-a", "fund-1", "authority-one")])),
            new StubProvider(ScopeAssignmentProviderResult.Authoritative(
                [Assignment("tenant-a", "company-a", "fund-1", "authority-two")])));

        var result = await service.ResolveAffectedScopesAsync(Request());

        result.IsAuthoritative.Should().BeTrue();
        result.Scopes.Should().ContainSingle();
    }

    [Fact]
    public async Task ResolveOwnedScopes_NarrowsToTheCallerWhenTheFullResolutionIsComplete()
    {
        var service = Service(new StubProvider(ScopeAssignmentProviderResult.Authoritative(
        [
            Assignment("tenant-a", "company-a", "fund-1"),
            Assignment("tenant-b", "company-b", "fund-2"),
        ])));

        var result = await service.ResolveOwnedScopesAsync("tenant-a", "company-a", Request());

        result.IsAuthoritative.Should().BeTrue();
        result.Scopes.Should().ContainSingle()
            .Which.FundProfileId.Should().Be("fund-1");
    }

    [Fact]
    public async Task ResolveOwnedScopes_DoesNotNarrowAnIncompleteResolution()
    {
        // Narrowing here would be the dangerous move: the caller would receive an empty, confident
        // looking answer for their own tenant while the wider fan-out was never established.
        var service = Service(
            new StubProvider(ScopeAssignmentProviderResult.NotAuthoritative("holdings store unavailable")));

        var result = await service.ResolveOwnedScopesAsync("tenant-a", "company-a", Request());

        result.IsAuthoritative.Should().BeFalse();
        result.Blockers.Should().Contain("holdings store unavailable");
    }

    [Theory]
    [InlineData("", "company-a")]
    [InlineData("tenant-a", "")]
    [InlineData("   ", "   ")]
    public async Task ResolveOwnedScopes_WithoutResolvableScope_IsRejectedRatherThanDefaulted(
        string tenantId,
        string companyId)
    {
        var service = Service(new StubProvider(ScopeAssignmentProviderResult.Authoritative(
            [Assignment("tenant-a", "company-a", "fund-1")])));

        var result = await service.ResolveOwnedScopesAsync(tenantId, companyId, Request());

        result.IsAuthoritative.Should().BeFalse();
        result.Scopes.Should().BeEmpty();
    }

    [Fact]
    public void IsOwnedBy_MatchesCaseInsensitivelyAfterTrimming()
    {
        var assignment = Assignment(" Tenant-A ", "Company-A");

        assignment.IsOwnedBy("tenant-a", "company-a").Should().BeTrue();
        assignment.IsOwnedBy("tenant-b", "company-a").Should().BeFalse();
        assignment.IsOwnedBy("tenant-a", null).Should().BeFalse();
    }

    private sealed class StubProvider : IScopeAssignmentProvider
    {
        private readonly ScopeAssignmentProviderResult _result;

        public StubProvider(ScopeAssignmentProviderResult result)
        {
            _result = result;
        }

        public string AuthorityId => "stub";

        public Task<ScopeAssignmentProviderResult> ResolveAsync(
            ScopeFanOutRequest request,
            CancellationToken ct = default)
            => Task.FromResult(_result);
    }

    private sealed class ThrowingProvider : IScopeAssignmentProvider
    {
        public string AuthorityId => "throwing";

        public Task<ScopeAssignmentProviderResult> ResolveAsync(
            ScopeFanOutRequest request,
            CancellationToken ct = default)
            => throw new InvalidOperationException("holdings query exploded");
    }
}
