using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster.CorporateActions;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using NSubstitute;
using Xunit;

namespace Meridian.Tests.SecurityMaster;

public sealed class CorporateActionCalendarServiceTests
{
    private static readonly DateOnly Week = new(2026, 11, 9);
    private static readonly string Hash = new('a', 64);

    [Fact]
    public void PaymentInsideRange_ExDateOutsideRange_PreservesEveryScopedCaseReference()
    {
        var proposal = Proposal(Week.AddDays(-10), Week.AddDays(2));
        var cases = new[] { Case(proposal), Case(proposal) };
        var events = CorporateActionCalendarService.ProjectCalendar(new([proposal], cases, []), Week, Week.AddDays(6), null, null);
        events.Should().ContainSingle().Which.Should().Match<CorporateActionCalendarEventDto>(e =>
            e.DateKind == "PaymentDate" && e.Cases.Count == 2 && e.ProposalId == proposal.ProposalId);
    }

    [Fact]
    public void CorrectionAndWithdrawal_KeepHistoryAndRemoveWithdrawnCalendarMarker()
    {
        var proposal = Proposal(Week.AddMonths(-1), null);
        var processingCase = Case(proposal);
        var first = Deadline(processingCase.CaseId, 2, new(Guid.NewGuid(), "IssuerElection", "Issuer", Date: Week));
        var corrected = Deadline(processingCase.CaseId, 3, CorporateActionDeadlinePolicy.Read(first.Metadata) with
        { Date = Week.AddDays(1), SupersedesEvidenceId = first.EvidenceId });
        var withdrawn = Deadline(processingCase.CaseId, 4, CorporateActionDeadlinePolicy.Read(corrected.Metadata) with
        { SupersedesEvidenceId = corrected.EvidenceId, Withdrawn = true });
        var history = CorporateActionCalendarService.ProjectHistory([first, corrected, withdrawn]);
        history.Should().HaveCount(3);
        history.Should().ContainSingle(e => e.IsCurrent).Which.EvidenceId.Should().Be(withdrawn.EvidenceId);
        CorporateActionCalendarService.ProjectCalendar(new([proposal], [processingCase], [first, corrected, withdrawn]),
            Week, Week.AddDays(6), null, null).Should().BeEmpty();
    }

    [Fact]
    public void AccountFilter_DoesNotPresentAnotherBrokersCutoffAsApplicable()
    {
        var proposal = Proposal(Week.AddMonths(-1), null);
        var processingCase = Case(proposal);
        var evidence = Deadline(processingCase.CaseId, 2, new(Guid.NewGuid(), "BrokerElection", "Broker A", Date: Week, FinancialAccountId: "account-a"));
        var snapshot = new CorporateActionCalendarReadSet([proposal], [processingCase], [evidence]);
        CorporateActionCalendarService.ProjectCalendar(snapshot, Week, Week.AddDays(6), "account-b", null).Should().BeEmpty();
        CorporateActionCalendarService.ProjectCalendar(snapshot, Week, Week.AddDays(6), "account-a", null).Should().ContainSingle();
    }

    [Theory]
    [InlineData("2026-11-12T16:00:00", false)]
    [InlineData("2026-11-12T16:00:00-05:00", true)]
    [InlineData("2026-11-12T21:00:00Z", true)]
    public void TimestampRequiresSourceOffset(string at, bool valid)
        => CorporateActionDeadlinePolicy.TryParseInstant(at, out _).Should().Be(valid);

    [Fact]
    public void JsonCaptureRefusesOffsetFreeTimestamp_AndZoneOffsetDisagreement()
    {
        var json = "{\"seriesId\":\"d7426aca-bda5-49c1-89fd-a69e9a392956\",\"kind\":\"IssuerElection\",\"sourceParty\":\"Issuer\",\"at\":\"2026-11-12T16:00:00\"}";
        var read = () => JsonSerializer.Deserialize<CorporateActionElectionDeadlineDto>(json, CorporateActionDeadlinePolicy.JsonOptions);
        read.Should().Throw<JsonException>();
        CorporateActionDeadlinePolicy.Validate(new(Guid.NewGuid(), "IssuerElection", "Issuer",
            At: new DateTimeOffset(2026, 11, 12, 16, 0, 0, TimeSpan.Zero), TimeZoneId: "America/New_York"))
            .Should().Contain("disagree");
    }

    [Fact]
    public void LegacyDeadlineRemainsReadable_ButNewCaptureRequiresOriginalSourceText()
    {
        var legacy = new CorporateActionElectionDeadlineDto(Guid.NewGuid(), "IssuerElection", "Issuer", Date: Week);
        var metadata = JsonSerializer.SerializeToElement(new CorporateActionDeadlineEvidenceDto(1, legacy), CorporateActionDeadlinePolicy.JsonOptions);
        CorporateActionDeadlinePolicy.Read(metadata).Date.Should().Be(Week);
        CorporateActionDeadlinePolicy.ValidateCapture(legacy, "document://notice", Hash, "Reviewed").Should().Contain("original source text");
    }

    [Fact]
    public void CsvPreview_UsesSameEvidenceValidationAndPreservesQuotedRationale()
    {
        var id = Guid.NewGuid();
        var csv = $"{CorporateActionDeadlinePolicy.CsvHeader}\r\n{id},BrokerElection,Broker,2026-11-12,,,account-a,,document://notice,{Hash},\"Reviewed, notice\",,false,Deadline on 12 November\r\n";
        var request = new CorporateActionDeadlineImportRequestDto(Guid.NewGuid(), 5, "import-1", "tenant", "company", csv);
        var preview = CorporateActionCalendarService.ParseImport(request);
        preview.Errors.Should().BeEmpty();
        preview.Rows.Should().ContainSingle().Which.Rationale.Should().Be("Reviewed, notice");
        CorporateActionCalendarService.ParseImport(request with { Csv = csv.Replace(Hash, "missing") }).Errors
            .Should().Contain(e => e.Contains("SHA-256"));
        CorporateActionCalendarService.ParseImport(request with { PreviewHash = preview.PreviewHash }).PreviewHash.Should().Be(preview.PreviewHash);
        CorporateActionCalendarService.ParseImport(request with { ExpectedVersion = 6 }).PreviewHash.Should().NotBe(preview.PreviewHash);
    }

    [Fact]
    public async Task CalendarPagesBeyond250_AreBoundToRevisionAndAuthenticatedScope()
    {
        var store = Substitute.For<ICorporateActionOperationsStore>();
        var operations = Substitute.For<ICorporateActionOperationsService>();
        var proposals = Enumerable.Range(0, 301).Select(_ => Proposal(Week, null)).ToArray();
        var snapshot = new CorporateActionCalendarReadSet(proposals, [], []);
        store.ReadCalendarAsync("tenant", "company", Week, Week.AddDays(6), null, null, Arg.Any<CancellationToken>()).Returns(snapshot);
        var service = new CorporateActionCalendarService(operations, store);
        var first = await service.GetCalendarAsync("tenant", "company", Week, Week.AddDays(6));
        first.Items.Should().HaveCount(250);
        first.TotalCount.Should().Be(301);
        var last = await service.GetCalendarAsync("tenant", "company", Week, Week.AddDays(6), cursor: first.NextCursor);
        last.Items.Should().HaveCount(51);
        last.NextCursor.Should().BeNull();
        first.Items.Select(e => e.EventId).Intersect(last.Items.Select(e => e.EventId)).Should().BeEmpty();

        store.ReadCalendarAsync("tenant", "company", Week, Week.AddDays(6), null, null, Arg.Any<CancellationToken>())
            .Returns(new CorporateActionCalendarReadSet(proposals.Skip(1).ToArray(), [], []));
        var changed = async () => await service.GetCalendarAsync("tenant", "company", Week, Week.AddDays(6), cursor: first.NextCursor);
        await changed.Should().ThrowAsync<CorporateActionStateConflictException>();
    }

    [Fact]
    public async Task RecordUsesExistingVersionedEvidenceCommand_WithoutTransitioningCase()
    {
        var operations = Substitute.For<ICorporateActionOperationsService>();
        var service = new CorporateActionCalendarService(operations);
        var request = new RecordCorporateActionDeadlineRequestDto(Guid.NewGuid(), 12, "deadline-key", "tenant", "company",
            new(Guid.NewGuid(), "IssuerElection", "Issuer", Date: Week, SourceText: "Election deadline 12 November"), "document://issuer-notice", Hash, "Reviewed the issuer notice");
        await service.RecordAsync(request, "operator", "trace-1");
        await operations.Received(1).AddEvidenceAsync(Arg.Is<AddCorporateActionEvidenceRequestDto>(r =>
            r.CaseId == request.CaseId && r.ExpectedVersion == 12 && r.IdempotencyKey == "deadline-key" &&
            r.EvidenceKind == CorporateActionDeadlinePolicy.EvidenceKind && r.Actor == "operator" && r.EvidenceHash == Hash), Arg.Any<CancellationToken>());
        await operations.DidNotReceive().TransitionCaseAsync(Arg.Any<TransitionCorporateActionCaseRequestDto>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ChangedCsvCannotBeImportedUsingPriorPreviewHash()
    {
        var operations = Substitute.For<ICorporateActionOperationsService>();
        var service = new CorporateActionCalendarService(operations);
        var csv = $"{CorporateActionDeadlinePolicy.CsvHeader}\n{Guid.NewGuid()},IssuerElection,Issuer,2026-11-12,,,,,document://notice,{Hash},Reviewed,,false,Deadline on 12 November";
        var request = new CorporateActionDeadlineImportRequestDto(Guid.NewGuid(), 1, "import-key", "tenant", "company", csv);
        var preview = CorporateActionCalendarService.ParseImport(request);
        var import = async () => await service.ImportAsync(request with { Csv = csv.Replace("Reviewed", "Changed"), PreviewHash = preview.PreviewHash }, "operator", null);
        await import.Should().ThrowAsync<CorporateActionValidationException>();
        await operations.DidNotReceive().AddEvidenceAsync(Arg.Any<AddCorporateActionEvidenceRequestDto>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PartialCsvRetry_ReplaysOriginalRowKeysAndVersionsBeforeResuming()
    {
        var operations = Substitute.For<ICorporateActionOperationsService>();
        var service = new CorporateActionCalendarService(operations);
        var processingCase = Case(Proposal(Week, null));
        var calls = new List<AddCorporateActionEvidenceRequestDto>();
        var failedOnce = false;
        operations.AddEvidenceAsync(Arg.Any<AddCorporateActionEvidenceRequestDto>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var row = call.ArgAt<AddCorporateActionEvidenceRequestDto>(0);
            calls.Add(row);
            if (row.IdempotencyKey.EndsWith(":row:3") && !failedOnce)
            {
                failedOnce = true;
                throw new CorporateActionPersistenceUnavailableException("Retained result must be inspected before retry.");
            }
            var replayed = calls.Count(r => r.IdempotencyKey == row.IdempotencyKey) > 1;
            return new CorporateActionEvidenceMutationResultDto(processingCase with { Version = row.ExpectedVersion + 1 },
                new CorporateActionEvidenceDto(Guid.NewGuid(), row.CaseId, row.EvidenceKind, row.EvidenceReference,
                    row.EvidenceHash, row.Description, row.Metadata, row.ExpectedVersion + 1, row.Actor, DateTimeOffset.UtcNow), replayed);
        });
        string Row() => $"{Guid.NewGuid()},IssuerElection,Issuer,2026-11-12,,,,,document://notice,{Hash},Reviewed,,false,Deadline 12 November";
        var request = new CorporateActionDeadlineImportRequestDto(processingCase.CaseId, 1, "stable-import", "tenant", "company",
            $"{CorporateActionDeadlinePolicy.CsvHeader}\n{Row()}\n{Row()}");
        request = request with { PreviewHash = CorporateActionCalendarService.ParseImport(request).PreviewHash };
        var partial = await service.ImportAsync(request, "operator", "first-http-trace");
        partial.Complete.Should().BeFalse();
        partial.CommittedCount.Should().Be(1);
        partial.FailedRow.Should().Be(3);
        var resumed = await service.ImportAsync(request, "operator", "retry-http-trace");
        resumed.Complete.Should().BeTrue();
        resumed.Results[0].Replayed.Should().BeTrue();
        calls.Select(r => r.IdempotencyKey).Should().Equal("stable-import:row:2", "stable-import:row:3", "stable-import:row:2", "stable-import:row:3");
        calls.Select(r => r.ExpectedVersion).Should().Equal(1, 2, 1, 2);
    }

    private static CorporateActionSourceProposalDto Proposal(DateOnly exDate, DateOnly? payDate)
    {
        var security = Guid.NewGuid();
        var action = new CorporateActionDto(Guid.NewGuid(), security, "Dividend", exDate, payDate,
            0.25m, "USD", null, null, null, null, null, null, null);
        return new(Guid.NewGuid(), security, new("provider", Guid.NewGuid().ToString(), "v1", DateTimeOffset.UtcNow), action, 1,
            CorporateActionEconomicFingerprint.Compute(action), "Observed", 1, null, null, null, "source", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
    }
    private static CorporateActionProcessingCaseDto Case(CorporateActionSourceProposalDto proposal)
        => new(Guid.NewGuid(), proposal.ProposalId, proposal.ProposedAction.CorpActId, proposal.SecurityId, new("tenant", "company"),
            "Detected", 1, null, null, null, "operator", DateTimeOffset.UtcNow, "operator", DateTimeOffset.UtcNow);
    private static CorporateActionEvidenceDto Deadline(Guid caseId, long version, CorporateActionElectionDeadlineDto value)
        => new(Guid.NewGuid(), caseId, CorporateActionDeadlinePolicy.EvidenceKind, "document://notice", Hash, "Reviewed notice",
            JsonSerializer.SerializeToElement(new CorporateActionDeadlineEvidenceDto(1, value), CorporateActionDeadlinePolicy.JsonOptions), version, "operator", DateTimeOffset.UtcNow);
}
