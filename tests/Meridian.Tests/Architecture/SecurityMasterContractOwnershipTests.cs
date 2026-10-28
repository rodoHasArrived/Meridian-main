using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Tests.Architecture;

public sealed class SecurityMasterContractOwnershipTests
{
    [Fact]
    public void Application_DoesNotRedeclareCanonicalSecurityMasterContracts()
    {
        var canonicalContracts = new[]
        {
            typeof(ISecurityMasterService),
            typeof(ISecurityMasterQueryService),
            typeof(ISecurityMasterAmender)
        };
        var applicationContracts = typeof(SecurityMasterService).Assembly.GetExportedTypes()
            .Where(type => type.IsInterface)
            .Select(type => type.Name);

        applicationContracts.Should().NotIntersectWith(canonicalContracts.Select(type => type.Name),
            "all Security Master callers must resolve the same Contracts-owned service types");
    }
}
