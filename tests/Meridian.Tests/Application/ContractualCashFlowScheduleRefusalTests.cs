using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Meridian.Application.SecurityMaster.CashFlow;
using Meridian.Contracts.CashFlows;
using Meridian.Contracts.SecurityMaster;
using NSubstitute;

namespace Meridian.Tests.Application;

public sealed class ContractualCashFlowScheduleRefusalTests
{
    private static readonly DateTimeOffset Cutoff = new(2026, 2, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly ContractualCashFlowRequest Request = new(new(2026, 1, 1), Cutoff);

    [Theory]
    [InlineData("principalSchedule", "[{\"paymentDate\":\"2026-07-01\",\"amount\":0}]", "SM_SCHEDULE_RANGE_INVALID")]
    [InlineData("factorScheduleEntries", "\"malformed\"", "SM_SCHEDULE_CONTAINER_INVALID")]
    [InlineData("stepSchedule", "[{\"effectiveDate\":\"2026-02-01\",\"rate\":0.02},{\"effectiveDate\":\"2026-02-01\",\"rate\":0.03}]", "SM_SCHEDULE_DUPLICATE_DATE")]
    public async Task Preview_MalformedRetainedSchedulesReturnStructuredBlockers(string property, string schedule, string diagnostic)
    {
        var id = Guid.NewGuid();
        var terms = JsonNode.Parse("""
            {"issueDate":"2026-01-01","maturity":"2027-01-01","par":1000,
             "couponRate":5,"paymentFrequency":"Quarterly","dayCount":"ACT/365"}
            """)!;
        terms[property] = JsonNode.Parse(schedule);
        var security = new SecurityDetailDto(id, "Bond", SecurityStatusDto.Active, "Synthetic schedule", "USD",
            JsonSerializer.SerializeToElement(new { }), JsonSerializer.SerializeToElement(terms), [], [], 1,
            new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), null);
        var query = Substitute.For<ISecurityMasterQueryService>();
        query.GetRecordedByIdAsOfAsync(id, Cutoff, Arg.Any<CancellationToken>()).Returns(security);
        var service = new ContractualCashFlowPreviewService(query, Substitute.For<ISecurityMasterCashFlowService>());

        var result = await service.PreviewSecurityAsync(id, Request);

        result.Should().NotBeNull();
        result!.IsCalculated.Should().BeFalse();
        result.ResolvedContract.Should().BeNull();
        result.Events.Should().BeEmpty();
        var blocker = result.Blockers.Should().ContainSingle().Which;
        blocker.Code.Should().Be("ContractInputsUnavailable");
        blocker.Message.Should().Contain(diagnostic).And.Contain($"assetSpecificTerms.{property}");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Preview_QueryFailuresRemainErrors(bool storageFailure)
    {
        var id = Guid.NewGuid();
        Exception failure = storageFailure ? new IOException("Synthetic storage failure") : new InvalidOperationException("Synthetic runtime failure");
        var query = Substitute.For<ISecurityMasterQueryService>();
        query.GetRecordedByIdAsOfAsync(id, Cutoff, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<SecurityDetailDto?>(failure));
        var service = new ContractualCashFlowPreviewService(query, Substitute.For<ISecurityMasterCashFlowService>());

        Func<Task> preview = () => service.PreviewSecurityAsync(id, Request);

        (await preview.Should().ThrowAsync<Exception>()).Which.Should().BeSameAs(failure);
    }
}
