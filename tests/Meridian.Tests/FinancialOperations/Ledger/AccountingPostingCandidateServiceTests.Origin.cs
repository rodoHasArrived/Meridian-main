using FluentAssertions;
using Meridian.Contracts.Operations;
using Meridian.Contracts.Workstation;

namespace Meridian.Tests.FinancialOperations.Ledger;

public sealed partial class AccountingPostingCandidateServiceTests
{
    [Theory]
    [InlineData(OperationsActionOriginDto.AutomationSuggestion)]
    [InlineData(OperationsActionOriginDto.AssistantDraft)]
    [InlineData(OperationsActionOriginDto.AutomationAssistant)]
    [InlineData((OperationsActionOriginDto)254)]
    [InlineData((OperationsActionOriginDto)255)]
    public async Task PostCandidateAsync_NonHumanOriginCannotPostReviewedCandidate(OperationsActionOriginDto origin)
    {
        var (_, store, service, request) = await CreateActorReplayHarnessAsync();

        var post = () => service.PostCandidateAsync(request with { ActionOrigin = origin });

        await post.Should().ThrowAsync<HumanOperatorRequiredException>();
        store.Appended.Should().BeEmpty();
    }
}
