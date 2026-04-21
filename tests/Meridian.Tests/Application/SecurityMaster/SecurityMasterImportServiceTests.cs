using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Core.Serialization;
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

        var importTask = importService.ImportAsync(BuildJson(), ".json", ct: CancellationToken.None);

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
