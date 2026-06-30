using FluentAssertions;
using Meridian.Contracts.Ledger;
using Meridian.Identity.Auth;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Meridian.Tests.Ui;

/// <summary>
/// The fund-profile tenant guard is a defense-in-depth tripwire: it denies a body-supplied fund profile
/// only when accounting history proves it belongs exclusively to other companies, and otherwise always
/// allows (own fund, unknown fund, unattributed history, or unavailable authority) so a legitimate edit
/// is never blocked. The single-company-per-deployment boundary remains the actual control.
/// </summary>
public sealed class AccountingHistoryFundProfileTenantGuardTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task BlankFund_IsAllowed_WithoutConsultingHistory(string? fund)
    {
        var (guard, store) = Build();

        var decision = await guard.EvaluateAsync(Caller(), fund);

        decision.IsAllowed.Should().BeTrue();
        await store.DidNotReceive().ListAsync(
            Arg.Any<string?>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>(), Arg.Any<string?>(), Arg.Any<string?>());
    }

    [Fact]
    public async Task FundWithCallerCompanyHistory_IsAllowed()
    {
        var (guard, store) = Build(Audit(companyId: "company-other"), Audit(companyId: "company-acme"));

        var decision = await guard.EvaluateAsync(Caller(), "fund-x");

        decision.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public async Task FundWithCallerTenantHistory_IsAllowed_EvenWhenCompanyUnattributed()
    {
        var (guard, store) = Build(Audit(companyId: null, tenantId: "tenant-acme"));

        var decision = await guard.EvaluateAsync(Caller(), "fund-x");

        decision.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public async Task FundWithHistoryExclusivelyUnderOtherCompanies_IsDenied()
    {
        var (guard, store) = Build(
            Audit(companyId: "company-other"),
            Audit(companyId: "company-other2", tenantId: "tenant-other"));

        var decision = await guard.EvaluateAsync(Caller(), "fund-x");

        decision.IsAllowed.Should().BeFalse();
    }

    [Fact]
    public async Task UnknownFundWithNoHistory_IsAllowed()
    {
        var (guard, store) = Build();
        store.ListAsync(Arg.Any<string?>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>(), Arg.Any<string?>(), Arg.Any<string?>())
            .Returns(Array.Empty<AccountingActionAuditEventDto>());

        var decision = await guard.EvaluateAsync(Caller(), "fund-new");

        decision.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public async Task FundWithOnlyUnattributedHistory_IsAllowed()
    {
        var (guard, store) = Build(Audit(companyId: null, tenantId: null));

        var decision = await guard.EvaluateAsync(Caller(), "fund-x");

        decision.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public async Task ForeignMatch_IsCaseInsensitiveAndTrimmed()
    {
        var (guard, store) = Build(Audit(companyId: "  COMPANY-ACME  "));

        var decision = await guard.EvaluateAsync(Caller(), "fund-x");

        decision.IsAllowed.Should().BeTrue("the caller's company matches the history ignoring case and whitespace");
    }

    [Fact]
    public async Task AuthorityUnavailable_IsAllowed_DeferringToDeploymentBoundary()
    {
        var (guard, store) = Build();
        store.ListAsync(Arg.Any<string?>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>(), Arg.Any<string?>(), Arg.Any<string?>())
            .ThrowsAsync(new InvalidOperationException("audit store unavailable"));

        var decision = await guard.EvaluateAsync(Caller(), "fund-x");

        decision.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public async Task Cancellation_Propagates_RatherThanSwallowingIntoAllow()
    {
        var (guard, store) = Build();
        store.ListAsync(Arg.Any<string?>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>(), Arg.Any<string?>(), Arg.Any<string?>())
            .ThrowsAsync(new OperationCanceledException());

        await guard.Invoking(g => g.EvaluateAsync(Caller(), "fund-x"))
            .Should().ThrowAsync<OperationCanceledException>();
    }

    // ---- helpers ------------------------------------------------------------------------------

    private static (AccountingHistoryFundProfileTenantGuard Guard, IAccountingActionAuditStore Store) Build(
        params AccountingActionAuditEventDto[] history)
    {
        var store = Substitute.For<IAccountingActionAuditStore>();
        if (history.Length > 0)
        {
            store.ListAsync(Arg.Any<string?>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>(), Arg.Any<string?>(), Arg.Any<string?>())
                .Returns(history);
        }

        var guard = new AccountingHistoryFundProfileTenantGuard(
            store, NullLogger<AccountingHistoryFundProfileTenantGuard>.Instance);
        return (guard, store);
    }

    private static WorkstationTenantContext Caller(string? company = "company-acme", string? tenant = "tenant-acme")
        => new(
            TenantId: tenant,
            CompanyId: company,
            Actor: "ops.analyst",
            RoleProfileName: null,
            Permissions: UserPermission.ModifySecurityMaster);

    private static AccountingActionAuditEventDto Audit(string? companyId, string? tenantId = null, string fund = "fund-x")
        => new(
            AuditEventId: Guid.NewGuid(),
            RecordedAtUtc: DateTimeOffset.UnixEpoch,
            Actor: "actor",
            Action: "configure",
            FundProfileId: fund,
            LedgerBookId: null,
            CorrelationId: null,
            BeforeHash: "before",
            AfterHash: "after",
            ValidationIssues: [],
            EvidenceLinks: [],
            CompanyId: companyId,
            ReportGroupPrincipalIds: null,
            TenantId: tenantId);
}
