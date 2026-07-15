using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// The null operator-overrides store is the fallback wired when Security Master is not configured.
/// Its read returns the empty overlay (green today); its mutations fail loudly. Track C adds
/// <c>RecordApprovalDecisionAsync</c>, which must fail the same way its sibling <c>PatchAsync</c>
/// does (InvalidOperationException). The decision test is RED until Track C adds that member.
/// </summary>
public sealed class NullOperatorOverridesStoreTests
{
    [Fact]
    public async Task GetAsync_ReturnsNull_WhenSecurityMasterNotConfigured()
    {
        var store = new NullOperatorOverridesStore();

        var result = await store.GetAsync(Guid.NewGuid());

        result.Should().BeNull();
    }

    [Fact]
    public async Task RecordApprovalDecisionAsync_ThrowsInvalidOperation_WhenSecurityMasterNotConfigured()
    {
        var store = new NullOperatorOverridesStore();

        var act = async () => await store.RecordApprovalDecisionAsync(
            Guid.NewGuid(),
            new OperatorOverrideDecisionRequest(SecurityOverrideApprovalStatusDto.Approved, "reviewer-1"));

        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
