using System.Net;
using System.Net.Http;
using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.Contracts.Ledger;
using Meridian.Ui.Services;
using Meridian.Wpf.Services;

namespace Meridian.Wpf.Tests.Services;

public sealed partial class FactorPaydownApiClientTests
{
    [Fact]
    public async Task CapabilityReadUsesExactSelectedBookDraftAndFund()
    {
        var book = Guid.NewGuid();
        var draft = Guid.NewGuid();
        var capability = new AssetAccountingDraftCapabilitiesDto(1, draft, 7, "fund-a", book, Guid.NewGuid(), "candidate",
            false, false, "Independent reviewer required", "Retained approver required", null, false, null, null, "principal:1");
        using var api = new ApiClientService(new Factory(new Handler(message =>
        {
            message.Method.Should().Be(HttpMethod.Get);
            message.RequestUri!.AbsolutePath.Should().Be($"/api/ledger/books/{book:D}/factor-paydowns/drafts/{draft:D}/capabilities");
            message.RequestUri.Query.Should().Contain("fundProfileId=fund-a");
            return new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(JsonSerializer.Serialize(capability, DesktopJsonOptions.Api)) };
        })));
        var result = await new FactorPaydownApiClient(api).GetCapabilitiesAsync("fund-a", book, draft, default);
        result!.CanApprove.Should().BeFalse();
        result.CanPost.Should().BeFalse();
        result.DraftVersion.Should().Be(7);
        result.AuthenticatedActor.Should().Be("principal:1");
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.NotImplemented)]
    public async Task OlderServerReturnsNoApprovalOrPostingSupport(HttpStatusCode status)
    {
        using var api = new ApiClientService(new Factory(new Handler(_ => new HttpResponseMessage(status)
            { Content = new StringContent("unsupported") })));
        var result = await new FactorPaydownApiClient(api).GetCapabilitiesAsync("fund-a", Guid.NewGuid(), Guid.NewGuid(), default);
        result.Should().BeNull();
    }

    [Fact]
    public async Task LifecycleTransportPreservesOriginalPendingCommandAndReturnsCanonicalJournal()
    {
        var book = Guid.NewGuid();
        var id = Guid.NewGuid();
        var period = Guid.NewGuid();
        var pending = new JournalEntryLifecycleActionRequestDto(id, "fund-a", JournalEntryLifecycleActionDto.Post,
            "independent-reviewer", 4, "Original retained rationale", "original-correlation", ["evidence://original"], LedgerBookId: book);
        var draft = new ManualJournalEntryDraftDto(id, ManualJournalEntryStatusDto.Posted, "fund-a", book, default,
            new(2026, 10, 28), period.ToString("D"), null, null, "USD", "Factor paydown", "preparer",
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, 8, [], [], []);
        var posted = new PostedLedgerJournalEntryResultDto(id, book, default, period, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var response = new JournalEntryLifecycleActionResultDto(draft,
            new("transition", ManualJournalEntryStatusDto.Approved, ManualJournalEntryStatusDto.Posted,
                JournalEntryLifecycleActionDto.Post, "independent-reviewer", DateTimeOffset.UnixEpoch), [], posted);
        JournalEntryLifecycleActionRequestDto? sent = null;
        using var api = new ApiClientService(new Factory(new AsyncHandler(async message =>
        {
            message.Method.Should().Be(HttpMethod.Post);
            message.RequestUri!.AbsolutePath.Should().Be(UiApiRoutes.LedgerManualJournalEntryLifecycleAction);
            sent = JsonSerializer.Deserialize<JournalEntryLifecycleActionRequestDto>(await message.Content!.ReadAsStringAsync(), DesktopJsonOptions.Api);
            return Json(response);
        })));
        var result = await new FactorPaydownApiClient(api).ApplyLifecycleAsync(pending, default);
        sent.Should().BeEquivalentTo(pending);
        result.PostedJournal!.JournalEntryId.Should().Be(id);
        result.PostedJournal.CommandId.Should().Be(posted.CommandId);
    }

    private sealed class AsyncHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => respond(request);
    }
}
