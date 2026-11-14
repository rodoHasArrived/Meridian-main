using FluentAssertions;
using Meridian.Contracts.Tenancy;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Shared.Services;

namespace Meridian.Tests.Ui;

public sealed class ClosePublicationWorkerAuthorityTests
{
    private static readonly CloseReadinessScopeDto Scope = new(
        "fund-alpha", Guid.NewGuid(), Guid.NewGuid(), "entity-alpha", "2026-07");

    [Fact]
    public async Task WorkerWithoutRetainedAuthority_CannotUseCallerSuppliedTenantAndCompany()
    {
        var factoryCalls = 0;
        var guard = new ClosePublicationReadinessGuard(() => { factoryCalls++; return null; });

        var blockers = await guard.ValidateAsync(Guid.NewGuid(), 1, Scope, "tenant-alpha", "company-alpha");

        blockers.Should().ContainSingle(blocker => blocker.Code == "CLOSE_TENANT_SCOPE_REQUIRED");
        factoryCalls.Should().Be(0);
    }

    [Fact]
    public async Task WorkerWithMismatchedRetainedAuthority_IsRejectedBeforeReadingEvidence()
    {
        var factoryCalls = 0;
        var guard = new ClosePublicationReadinessGuard(() => { factoryCalls++; return null; });
        using var authority = FundScopeTenantAuthority.Enter("tenant-beta", "retained close publication");

        var blockers = await guard.ValidateAsync(Guid.NewGuid(), 1, Scope, "tenant-alpha", "company-alpha");

        blockers.Should().ContainSingle(blocker => blocker.Code == "CLOSE_TENANT_SCOPE_MISMATCH");
        factoryCalls.Should().Be(0);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("all")]
    [InlineData("  ALL  ")]
    public async Task WorkerWithUnscopedCompany_IsRejectedBeforeReadingEvidence(string? company)
    {
        var factoryCalls = 0;
        var guard = new ClosePublicationReadinessGuard(() => { factoryCalls++; return null; });
        using var authority = FundScopeTenantAuthority.Enter("tenant-alpha", "retained close publication");

        var blockers = await guard.ValidateAsync(Guid.NewGuid(), 1, Scope, "tenant-alpha", company);

        factoryCalls.Should().Be(0);
        blockers.Should().ContainSingle(blocker => blocker.Code == "CLOSE_TENANT_SCOPE_REQUIRED");
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" TENANT-ALPHA ")]
    public async Task WorkerWithMatchingRetainedAuthority_ReachesEvidenceValidation(string? requestedTenant)
    {
        var factoryCalls = 0;
        var guard = new ClosePublicationReadinessGuard(() => { factoryCalls++; return null; });
        using var authority = FundScopeTenantAuthority.Enter("tenant-alpha", "retained close publication");

        var blockers = await guard.ValidateAsync(Guid.NewGuid(), 1, Scope, requestedTenant, "company-alpha");

        factoryCalls.Should().Be(1);
        blockers.Should().ContainSingle(blocker => blocker.Code == "CLOSE_READINESS_UNAVAILABLE",
            "valid worker authority reaches the evidence authority and still cannot close without it");
    }

    [Fact]
    public async Task ShadowBook_CannotHardCloseAsBookOfRecord_BeforeReadingEvidence()
    {
        var factoryCalls = 0;
        var bookOfRecord = new FixedBookOfRecordGuard(isBookOfRecord: false);
        var guard = new ClosePublicationReadinessGuard(() => { factoryCalls++; return null; }, bookOfRecordGuardFactory: () => bookOfRecord);
        using var authority = FundScopeTenantAuthority.Enter("tenant-alpha", "retained close publication");

        var blockers = await guard.ValidateAsync(Guid.NewGuid(), 1, Scope, "tenant-alpha", "company-alpha");

        blockers.Should().ContainSingle(blocker => blocker.Code == "CLOSE_SHADOW_BOOK");
        bookOfRecord.Requests.Should().Equal([("tenant-alpha", (string?)"company-alpha", Scope.LedgerBookId!.Value)]);
        factoryCalls.Should().Be(0);
    }

    [Fact]
    public async Task BookOfRecord_ProceedsToEvidenceValidation()
    {
        var factoryCalls = 0;
        var guard = new ClosePublicationReadinessGuard(() => { factoryCalls++; return null; },
            bookOfRecordGuardFactory: () => new FixedBookOfRecordGuard(isBookOfRecord: true));
        using var authority = FundScopeTenantAuthority.Enter("tenant-alpha", "retained close publication");

        var blockers = await guard.ValidateAsync(Guid.NewGuid(), 1, Scope, "tenant-alpha", "company-alpha");

        factoryCalls.Should().Be(1);
        blockers.Should().ContainSingle(blocker => blocker.Code == "CLOSE_READINESS_UNAVAILABLE");
    }

    private sealed class FixedBookOfRecordGuard(bool isBookOfRecord) : IBookOfRecordGuard
    {
        public List<(string Tenant, string? Company, Guid Book)> Requests { get; } = [];

        public Task<BookOfRecordStatusDto> GetStatusAsync(string tenantId, string? companyId, Guid ledgerBookId, CancellationToken ct = default)
        {
            Requests.Add((tenantId, companyId, ledgerBookId));
            return Task.FromResult(new BookOfRecordStatusDto(ledgerBookId, isBookOfRecord,
                isBookOfRecord ? null : OnboardingShadowStates.Shadow, null, isBookOfRecord ? "Authoritative." : "Shadow book in parallel run."));
        }
    }
}
