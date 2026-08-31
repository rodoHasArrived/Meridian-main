using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Xunit;

namespace Meridian.Tests.SecurityMaster;

public sealed class NullCorporateActionOperationsServiceTests
{
    [Fact]
    public async Task ReadOperations_WhenPersistenceIsNotConfigured_ThrowTypedUnavailableError()
    {
        var service = new NullCorporateActionOperationsService();
        var caseId = Guid.Parse("ed899b2d-0c76-4057-b85d-f41e0e87f49a");
        var conflictId = Guid.Parse("621467a2-e59d-42e8-949a-286f07e402f4");
        var proposalId = Guid.Parse("121117ba-d84d-41c2-9973-822e7808c891");
        var scope = new CorporateActionCaseScopeDto("tenant-a", "company-a");
        (string Name, Func<Task> Read)[] reads =
        [
            ("GetSourceProposal", async () => _ = await service.GetSourceProposalAsync(proposalId)),
            ("ListSourceProposals", async () => _ = await service.ListSourceProposalsAsync(null, null, 25)),
            ("ListActionableSourceProposals", async () => _ = await service.ListActionableSourceProposalsAsync(null, 25)),
            ("GetInbox", async () => _ = await service.GetInboxAsync(scope, 25)),
            ("GetCase", async () => _ = await service.GetCaseAsync(caseId, scope.TenantId, scope.CompanyId)),
            ("ListCases", async () => _ = await service.ListCasesAsync(scope.TenantId, scope.CompanyId, null, null, 25)),
            ("GetConflict", async () => _ = await service.GetConflictAsync(caseId, conflictId, scope.TenantId, scope.CompanyId)),
            ("ListConflicts", async () => _ = await service.ListConflictsAsync(caseId, scope.TenantId, scope.CompanyId, null, 25)),
        ];

        foreach (var read in reads)
        {
            var exception = await Assert.ThrowsAsync<CorporateActionOperationException>(read.Read);

            Assert.Equal(CorporateActionProblemCodes.PersistenceUnavailable, exception.Code);
        }
    }
}
