using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Core.Serialization;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Meridian.Tests.Application.SecurityMaster;

public sealed class SecurityMasterImportServiceTests
{
    [Fact]
    public async Task ImportAsync_DuringAndAfterRun_UpdatesIngestStatusSnapshot()
    {
        var securityMasterService = new BlockingSecurityMasterService();
        var conflictService = Substitute.For<ISecurityMasterConflictService>();
        conflictService.GetOpenConflictsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<SecurityMasterConflict>>(Array.Empty<SecurityMasterConflict>()));

        var importService = new SecurityMasterImportService(
            securityMasterService,
            new SecurityMasterCsvParser(),
            NullLogger<SecurityMasterImportService>.Instance,
            conflictService);
        var statusService = (ISecurityMasterIngestStatusService)importService;

        var importTask = importService.ImportAsync(BuildJson(), ".json", "test.operator", ct: CancellationToken.None);

        await securityMasterService.Started.WaitAsync(TimeSpan.FromSeconds(5));

        var activeSnapshot = statusService.GetSnapshot();
        activeSnapshot.ActiveImport.Should().NotBeNull();
        activeSnapshot.ActiveImport!.FileExtension.Should().Be(".json");
        activeSnapshot.ActiveImport.Total.Should().Be(1);
        activeSnapshot.ActiveImport.Processed.Should().Be(0);
        activeSnapshot.ActiveImport.Imported.Should().Be(0);
        activeSnapshot.ActiveImport.Skipped.Should().Be(0);
        activeSnapshot.ActiveImport.Failed.Should().Be(0);
        activeSnapshot.LastCompleted.Should().BeNull();

        securityMasterService.Release();
        var result = await importTask;

        result.Imported.Should().Be(1);
        result.Failed.Should().Be(0);
        result.Skipped.Should().Be(0);

        var completedSnapshot = statusService.GetSnapshot();
        completedSnapshot.ActiveImport.Should().BeNull();
        completedSnapshot.LastCompleted.Should().NotBeNull();
        completedSnapshot.LastCompleted!.FileExtension.Should().Be(".json");
        completedSnapshot.LastCompleted.Total.Should().Be(1);
        completedSnapshot.LastCompleted.Processed.Should().Be(1);
        completedSnapshot.LastCompleted.Imported.Should().Be(1);
        completedSnapshot.LastCompleted.Skipped.Should().Be(0);
        completedSnapshot.LastCompleted.Failed.Should().Be(0);
        completedSnapshot.LastCompleted.ConflictsDetected.Should().Be(0);
        completedSnapshot.LastCompleted.ErrorCount.Should().Be(0);
        completedSnapshot.LastCompleted.CompletedAtUtc.Should().BeOnOrAfter(completedSnapshot.LastCompleted.StartedAtUtc);
    }

    /// <summary>
    /// Re-importing a mastered security fails the create at stream version 0. That is a skip, but its
    /// message says "Security stream version conflict" — containing neither "already exists" nor
    /// "duplicate" — so the old message-substring classification counted it as a hard failure.
    /// </summary>
    [Fact]
    public async Task ImportAsync_CountsAnAlreadyMasteredSecurityAsSkipped_NotFailed()
    {
        var service = new ThrowingSecurityMasterService(
            _ => new SecurityMasterStreamVersionConflictException(Guid.NewGuid(), expectedVersion: 0, currentVersion: 3));

        var result = await CreateImportService(service)
            .ImportAsync(BuildJson(), ".json", "test.operator", ct: CancellationToken.None);

        result.Skipped.Should().Be(1);
        result.Failed.Should().Be(0);
        result.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// The mirror of the above: an unrelated failure whose text merely contains "duplicate" is a
    /// real failure. Message text is not the contract.
    /// </summary>
    [Fact]
    public async Task ImportAsync_CountsAnUnrelatedFailureAsFailed_EvenWhenItsMessageSaysDuplicate()
    {
        var service = new ThrowingSecurityMasterService(
            _ => new InvalidOperationException("Upstream feed returned a duplicate payload checksum."));

        var result = await CreateImportService(service)
            .ImportAsync(BuildJson(), ".json", "test.operator", ct: CancellationToken.None);

        result.Failed.Should().Be(1);
        result.Skipped.Should().Be(0);
        result.Errors.Should().ContainSingle();
    }

    /// <summary>
    /// Cancellation is not an import outcome. Swallowing it into the failure tally reported a
    /// partial import as a completed one, with failures the operator never caused.
    /// </summary>
    [Fact]
    public async Task ImportAsync_PropagatesCancellation_RatherThanCountingItAsAFailedRow()
    {
        using var cts = new CancellationTokenSource();
        var service = new ThrowingSecurityMasterService(_ =>
        {
            cts.Cancel();
            return new OperationCanceledException(cts.Token);
        });

        var import = async () => await CreateImportService(service)
            .ImportAsync(BuildJson(), ".json", "test.operator", ct: cts.Token);

        await import.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ImportAsync_JsonImport_ReplacesFileAuthorityWithOneServerIngestStamp()
    {
        var forgedAt = new DateTimeOffset(2001, 2, 3, 4, 5, 6, TimeSpan.Zero);
        var forgedValidTo = forgedAt.AddYears(20);
        var requests = new List<CreateSecurityRequest>
        {
            CreateForgedRequest("FORGE-A", forgedAt, forgedValidTo),
            CreateForgedRequest("FORGE-B", forgedAt.AddDays(1), forgedValidTo.AddDays(1))
        };
        var fileContent = JsonSerializer.Serialize(
            requests,
            SecurityMasterJsonContext.Default.ListCreateSecurityRequest);
        var recordingService = new RecordingSecurityMasterService();
        var beforeImport = DateTimeOffset.UtcNow;

        var result = await CreateImportService(recordingService)
            .ImportAsync(fileContent, ".json", "authenticated.operator", ct: CancellationToken.None);

        var afterImport = DateTimeOffset.UtcNow;
        result.Imported.Should().Be(2);
        recordingService.Requests.Should().HaveCount(2);

        var authorityTimestamp = recordingService.Requests[0].EffectiveFrom;
        authorityTimestamp.Should().BeOnOrAfter(beforeImport);
        authorityTimestamp.Should().BeOnOrBefore(afterImport);
        recordingService.Requests.Should().OnlyContain(request =>
            request.EffectiveFrom == authorityTimestamp
            && request.SourceSystem == "SecurityMasterImport"
            && request.UpdatedBy == "authenticated.operator"
            && request.SourceRecordId == null
            && request.Reason == "Bulk import through Security Master import workflow");

        recordingService.Requests
            .SelectMany(static request => request.Identifiers)
            .Should().OnlyContain(identifier =>
                identifier.ValidFrom == authorityTimestamp
                && identifier.ValidTo == null
                && identifier.Provider == null
                && identifier.NormalizedValue == null
                && identifier.NormalizedProvider == null);
    }

    [Fact]
    public async Task ImportAsync_CsvImport_UsesOneServerIngestStampForEveryRowAndIdentifier()
    {
        const string fileContent =
            "Ticker,Name,AssetClass,Currency,Exchange,ISIN,CUSIP,FIGI\n"
            + "ONE,Security One,Equity,USD,XNAS,US0000000001,000000001,BBG000000001\n"
            + "TWO,Security Two,Equity,USD,XNYS,US0000000002,000000002,BBG000000002";
        var recordingService = new RecordingSecurityMasterService();
        var beforeImport = DateTimeOffset.UtcNow;

        var result = await CreateImportService(recordingService)
            .ImportAsync(fileContent, ".csv", "authenticated.operator", ct: CancellationToken.None);

        var afterImport = DateTimeOffset.UtcNow;
        result.Imported.Should().Be(2);
        recordingService.Requests.Should().HaveCount(2);

        var authorityTimestamp = recordingService.Requests[0].EffectiveFrom;
        authorityTimestamp.Should().BeOnOrAfter(beforeImport);
        authorityTimestamp.Should().BeOnOrBefore(afterImport);
        recordingService.Requests.Should().OnlyContain(request =>
            request.EffectiveFrom == authorityTimestamp
            && request.SourceSystem == "SecurityMasterImport"
            && request.UpdatedBy == "authenticated.operator"
            && request.SourceRecordId == null
            && request.Reason == "Bulk import through Security Master import workflow");
        recordingService.Requests
            .SelectMany(static request => request.Identifiers)
            .Should().OnlyContain(identifier =>
                identifier.ValidFrom == authorityTimestamp
                && identifier.ValidTo == null
                && identifier.Provider == null
                && identifier.NormalizedValue == null
                && identifier.NormalizedProvider == null);
    }

    private static SecurityMasterImportService CreateImportService(ISecurityMasterService service)
        => new(
            service,
            new SecurityMasterCsvParser(),
            NullLogger<SecurityMasterImportService>.Instance);

    private static CreateSecurityRequest CreateForgedRequest(
        string ticker,
        DateTimeOffset effectiveFrom,
        DateTimeOffset validTo)
        => new(
            SecurityId: Guid.NewGuid(),
            AssetClass: "Equity",
            CommonTerms: JsonSerializer.SerializeToElement(new
            {
                displayName = $"Forged {ticker}",
                currency = "USD"
            }),
            AssetSpecificTerms: JsonSerializer.SerializeToElement(new { }),
            Identifiers:
            [
                new SecurityIdentifierDto(
                    SecurityIdentifierKind.Ticker,
                    ticker,
                    true,
                    effectiveFrom,
                    validTo,
                    Provider: "forged-provider",
                    NormalizedValue: "forged-normalized-value",
                    NormalizedProvider: "forged-normalized-provider")
            ],
            EffectiveFrom: effectiveFrom,
            SourceSystem: "forged-authoritative-source",
            UpdatedBy: "forged.operator",
            SourceRecordId: "forged-source-record",
            Reason: "file-supplied reason");

    private sealed class ThrowingSecurityMasterService(Func<CreateSecurityRequest, Exception> failure)
        : ISecurityMasterService
    {
        public Task<SecurityDetailDto> CreateAsync(CreateSecurityRequest request, CancellationToken ct = default)
            => throw failure(request);

        public Task<SecurityDetailDto> AmendTermsAsync(AmendSecurityTermsRequest request, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<SecurityDetailDto> AmendPreferredEquityTermsAsync(Guid securityId, AmendPreferredEquityTermsRequest request, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<SecurityDetailDto> AmendConvertibleEquityTermsAsync(Guid securityId, AmendConvertibleEquityTermsRequest request, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task DeactivateAsync(DeactivateSecurityRequest request, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<SecurityAliasDto> UpsertAliasAsync(UpsertSecurityAliasRequest request, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private sealed class RecordingSecurityMasterService : ISecurityMasterService
    {
        public List<CreateSecurityRequest> Requests { get; } = new();

        public Task<SecurityDetailDto> CreateAsync(CreateSecurityRequest request, CancellationToken ct = default)
        {
            Requests.Add(request);
            return Task.FromResult(CreateSecurityDetail(request.SecurityId));
        }

        public Task<SecurityDetailDto> AmendTermsAsync(AmendSecurityTermsRequest request, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<SecurityDetailDto> AmendPreferredEquityTermsAsync(Guid securityId, AmendPreferredEquityTermsRequest request, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<SecurityDetailDto> AmendConvertibleEquityTermsAsync(Guid securityId, AmendConvertibleEquityTermsRequest request, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task DeactivateAsync(DeactivateSecurityRequest request, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<SecurityAliasDto> UpsertAliasAsync(UpsertSecurityAliasRequest request, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private static string BuildJson()
    {
        var request = new CreateSecurityRequest(
            SecurityId: Guid.NewGuid(),
            AssetClass: "Equity",
            CommonTerms: JsonSerializer.SerializeToElement(new { displayName = "Meridian A" }),
            AssetSpecificTerms: JsonSerializer.SerializeToElement(new { }),
            Identifiers:
            [
                new SecurityIdentifierDto(SecurityIdentifierKind.Ticker, "MERA", true, DateTimeOffset.UtcNow),
                new SecurityIdentifierDto(SecurityIdentifierKind.Isin, "US0000000001", false, DateTimeOffset.UtcNow),
            ],
            EffectiveFrom: DateTimeOffset.UtcNow,
            SourceSystem: "Test",
            UpdatedBy: "Test",
            SourceRecordId: null,
            Reason: null);

        return JsonSerializer.Serialize(new List<CreateSecurityRequest> { request }, SecurityMasterJsonContext.Default.ListCreateSecurityRequest);
    }

    private static SecurityDetailDto CreateSecurityDetail(Guid securityId)
        => new(
            SecurityId: securityId,
            AssetClass: "Equity",
            Status: SecurityStatusDto.Active,
            DisplayName: "Imported Security",
            Currency: "USD",
            CommonTerms: JsonSerializer.SerializeToElement(new { displayName = "Imported Security" }),
            AssetSpecificTerms: JsonSerializer.SerializeToElement(new { }),
            Identifiers: Array.Empty<SecurityIdentifierDto>(),
            Aliases: Array.Empty<SecurityAliasDto>(),
            Version: 1,
            EffectiveFrom: DateTimeOffset.UtcNow,
            EffectiveTo: null);

    private sealed class BlockingSecurityMasterService : ISecurityMasterService
    {
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<SecurityDetailDto> _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private Guid _securityId;

        public Task Started => _started.Task;

        public void Release() => _release.TrySetResult(CreateSecurityDetail(_securityId));

        public async Task<SecurityDetailDto> CreateAsync(CreateSecurityRequest request, CancellationToken ct = default)
        {
            _securityId = request.SecurityId;
            _started.TrySetResult();
            return await _release.Task.WaitAsync(ct);
        }

        public Task<SecurityDetailDto> AmendTermsAsync(AmendSecurityTermsRequest request, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<SecurityDetailDto> AmendPreferredEquityTermsAsync(Guid securityId, AmendPreferredEquityTermsRequest request, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<SecurityDetailDto> AmendConvertibleEquityTermsAsync(Guid securityId, AmendConvertibleEquityTermsRequest request, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task DeactivateAsync(DeactivateSecurityRequest request, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<SecurityAliasDto> UpsertAliasAsync(UpsertSecurityAliasRequest request, CancellationToken ct = default)
            => throw new NotSupportedException();
    }
}
