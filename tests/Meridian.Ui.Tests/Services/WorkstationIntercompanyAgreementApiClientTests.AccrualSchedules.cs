using System.Net;
using System.Text.Json;
using Meridian.Contracts.Ledger;
using Meridian.Ui.Services;
using Meridian.Ui.Services.Services.Accounting;

namespace Meridian.Ui.Tests.Services;

public sealed partial class WorkstationIntercompanyAgreementApiClientTests
{
    private const string ScheduleRoute = "/api/ledger/intercompany/accrual-schedules";

    [Fact]
    public async Task AccrualScheduleListingIsScopedToTheBookAndRefusesAnotherBooksSchedule()
    {
        var book = Guid.NewGuid();
        var mine = IntercompanyAccrualScheduleWorkspaceTests.Schedule(Guid.NewGuid(), book, IntercompanyAccrualScheduleStatusDto.Active, 2);
        var foreign = IntercompanyAccrualScheduleWorkspaceTests.Schedule(Guid.NewGuid(), Guid.NewGuid(), IntercompanyAccrualScheduleStatusDto.Active, 2);
        var answers = new Queue<IntercompanyAccrualScheduleDto[]>([[mine], [mine, foreign]]);
        using var api = new ApiClientService(new Factory((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal(ScheduleRoute, request.RequestUri!.AbsolutePath);
            Assert.Equal($"?ledgerBookId={book:D}", request.RequestUri.Query);
            return Task.FromResult(Response(answers.Dequeue()));
        }));
        var client = new WorkstationIntercompanyAgreementApiClient(api);
        var listed = await client.GetAccrualSchedulesAsync(book);
        Assert.True(listed.Success, listed.ErrorMessage);
        Assert.Equal(IntercompanyAccrualScheduleStatusDto.Active, Assert.Single(listed.Data!).Status);
        var mixed = await client.GetAccrualSchedulesAsync(book);
        Assert.False(mixed.Success);
        Assert.Equal(502, mixed.StatusCode);
    }

    [Fact]
    public async Task AccrualScheduleDecisionSendsTheExactVersionAndRefusesAnAnswerWithoutItsRequest()
    {
        var book = Guid.NewGuid();
        var scheduleId = Guid.NewGuid();
        var request = new DecideIntercompanyAccrualScheduleRequest(Guid.NewGuid(), IntercompanyAccrualScheduleActionDto.Disable, 3, "Loan repaid");
        var retained = IntercompanyAccrualScheduleWorkspaceTests.Schedule(scheduleId, book, IntercompanyAccrualScheduleStatusDto.Active, 3);
        var recorded = retained with
        {
            Version = 4,
            Status = IntercompanyAccrualScheduleStatusDto.Disabled,
            Events = [.. retained.Events, new(Guid.NewGuid(), request.RequestId, request.Action, "checker", DateTimeOffset.UnixEpoch, request.Reason)]
        };
        var answers = new Queue<IntercompanyAccrualScheduleDto>([recorded, retained with { Version = 4 }]);
        using var api = new ApiClientService(new Factory(async (message, ct) =>
        {
            Assert.Equal(HttpMethod.Post, message.Method);
            Assert.Equal($"{ScheduleRoute}/{scheduleId:D}/decisions", message.RequestUri!.AbsolutePath);
            Assert.Equal($"?ledgerBookId={book:D}", message.RequestUri.Query);
            var body = JsonDocument.Parse(await message.Content!.ReadAsStringAsync(ct)).RootElement;
            Assert.Equal(request.RequestId, body.GetProperty("requestId").GetGuid());
            Assert.Equal("Disable", body.GetProperty("action").GetString());
            Assert.Equal(3, body.GetProperty("expectedVersion").GetInt64());
            Assert.False(body.TryGetProperty("actor", out _));
            return Response(answers.Dequeue());
        }));
        var client = new WorkstationIntercompanyAgreementApiClient(api);
        var decided = await client.DecideAccrualScheduleAsync(scheduleId, book, request);
        Assert.True(decided.Success, decided.ErrorMessage);
        Assert.Equal(IntercompanyAccrualScheduleStatusDto.Disabled, decided.Data!.Status);
        var unrecorded = await client.DecideAccrualScheduleAsync(scheduleId, book, request);
        Assert.False(unrecorded.Success);
        Assert.Equal(502, unrecorded.StatusCode);
    }

    [Fact]
    public async Task AccrualScheduleDueIsADatedReadAndARunPostsWithoutABody()
    {
        var book = Guid.NewGuid();
        var scheduleId = Guid.NewGuid();
        var asOf = new DateOnly(2026, 11, 17);
        using var api = new ApiClientService(new Factory(async (message, ct) =>
        {
            if (message.Method == HttpMethod.Get)
            {
                Assert.Equal($"{ScheduleRoute}/{scheduleId:D}/due", message.RequestUri!.AbsolutePath);
                Assert.Equal($"?ledgerBookId={book:D}&asOf=2026-11-17", message.RequestUri.Query);
                return Response(new IntercompanyAccrualScheduleRunDto(scheduleId, asOf, false,
                    [new(new(2026, 10, 1), new(2026, 11, 1), IntercompanyAccrualScheduleOutcomeDto.Due, Guid.NewGuid(), null, null)]));
            }
            Assert.Equal($"{ScheduleRoute}/{scheduleId:D}/run", message.RequestUri!.AbsolutePath);
            Assert.Equal($"?ledgerBookId={book:D}", message.RequestUri.Query);
            Assert.True(message.Content is null || (await message.Content.ReadAsStringAsync(ct)).Length == 0);
            // A run answering as a dry run, or for another schedule, is not this run's outcome.
            return Response(new IntercompanyAccrualScheduleRunDto(scheduleId, asOf, false, []));
        }));
        var client = new WorkstationIntercompanyAgreementApiClient(api);
        var due = await client.GetAccrualScheduleDueAsync(scheduleId, book, asOf);
        Assert.True(due.Success, due.ErrorMessage);
        Assert.Equal(IntercompanyAccrualScheduleOutcomeDto.Due, Assert.Single(due.Data!.Windows).Outcome);
        var run = await client.RunAccrualScheduleAsync(scheduleId, book);
        Assert.False(run.Success);
        Assert.Equal(502, run.StatusCode);
    }

    [Fact]
    public async Task AccrualScheduleProposalConflictSurfacesTheServerRefusal()
    {
        using var api = new ApiClientService(new Factory((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Conflict)
        { Content = new StringContent("This book already has a proposed or active schedule for the profile") })));
        var response = await new WorkstationIntercompanyAgreementApiClient(api).ProposeAccrualScheduleAsync(
            new(Guid.NewGuid(), Guid.NewGuid(), IntercompanyAccrualScheduleKindDto.LoanInterest, Guid.NewGuid(), Guid.NewGuid(), "Monthly"));
        Assert.False(response.Success);
        Assert.Equal(409, response.StatusCode);
    }
}
