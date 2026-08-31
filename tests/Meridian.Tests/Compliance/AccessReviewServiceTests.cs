using FluentAssertions;
using Meridian.Audit.Compliance;
using Meridian.Identity;
using Meridian.Identity.Auth;
using Meridian.Storage;
using Meridian.Tests.Infrastructure;

namespace Meridian.Tests.Compliance;

/// <summary>
/// Guards the dormant-access failure mode where an assessment is described as completed
/// remediation even though the authoritative role state did not change.
/// </summary>
public sealed class AccessReviewServiceTests : TempDirectoryTestBase
{
    private static readonly DateTimeOffset Now = new(2026, 8, 5, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AssessDormantPermissions_DormantActor_ReportsCandidatesWithoutMutation()
    {
        var roles = new FakeRoleStore(["TreasuryOperator", "OverrideApprover"]);
        var service = CreateService(roles);

        var assessment = await service.AssessDormantPermissionsAsync(
            "treasury-ops-1",
            "compliance-lead",
            Now.AddDays(-120));

        assessment.IsDormant.Should().BeTrue();
        assessment.CandidateRoles.Should().Equal("OverrideApprover", "TreasuryOperator");
        assessment.Reason.Should().Contain("Assessment only")
            .And.Contain("no permissions were changed");
        roles.MutationCount.Should().Be(0);
        service.GetReviews().Should().BeEmpty(
            "an assessment is not evidence that remediation was applied");
    }

    [Fact]
    public async Task ApplyDormantPermissionRemediation_DormantActor_MutatesAndVerifiesAllRoles()
    {
        var roles = new FakeRoleStore(["TreasuryOperator", "OverrideApprover"]);
        var service = CreateService(roles);

        var review = await service.ApplyDormantPermissionRemediationAsync(
            "treasury-ops-1",
            "compliance-lead",
            Now.AddDays(-120));

        review.Outcome.Should().Be(AccessReviewOutcome.RemediationApplied);
        review.RemovedRoles.Should().Equal("OverrideApprover", "TreasuryOperator");
        review.RolesAfter.Should().BeEmpty();
        review.Reason.Should().Contain("applied and verified");
        roles.MutationCount.Should().Be(1);
    }

    [Fact]
    public async Task ApplyDormantPermissionRemediation_ActiveActor_DoesNotMutateOrClaimRemoval()
    {
        var roles = new FakeRoleStore(["TreasuryOperator"]);
        var service = CreateService(roles);

        var review = await service.ApplyDormantPermissionRemediationAsync(
            "treasury-ops-1",
            "compliance-lead",
            Now.AddDays(-30));

        review.Outcome.Should().Be(AccessReviewOutcome.NoActionRequired);
        review.RemovedRoles.Should().BeEmpty();
        review.RolesAfter.Should().Equal("TreasuryOperator");
        review.Reason.Should().Contain("No remediation was applied");
        roles.MutationCount.Should().Be(0);
    }

    [Fact]
    public async Task ApplyDormantPermissionRemediation_MutationNoOps_RecordsFailureWithoutRemovalClaim()
    {
        var roles = new FakeRoleStore(["TreasuryOperator"])
        {
            RemovalMode = FakeRemovalMode.NoOp
        };
        var service = CreateService(roles);

        var review = await service.ApplyDormantPermissionRemediationAsync(
            "treasury-ops-1",
            "compliance-lead",
            Now.AddDays(-120));

        review.Outcome.Should().Be(AccessReviewOutcome.RemediationFailed);
        review.RemovedRoles.Should().BeEmpty();
        review.RolesAfter.Should().Equal("TreasuryOperator");
        review.Reason.Should().Be(
            "Remediation failed verification: no dormant roles were removed.");
    }

    [Fact]
    public async Task ApplyDormantPermissionRemediation_MutationFailsAfterPartialChange_ReportsVerifiedPartialState()
    {
        var roles = new FakeRoleStore(["RoleA", "RoleB"])
        {
            RemovalMode = FakeRemovalMode.RemoveFirstThenThrow
        };
        var service = CreateService(roles);

        var review = await service.ApplyDormantPermissionRemediationAsync(
            "treasury-ops-1",
            "compliance-lead",
            Now.AddDays(-120));

        review.Outcome.Should().Be(AccessReviewOutcome.RemediationPartiallyApplied);
        review.RemovedRoles.Should().Equal("RoleA");
        review.RolesAfter.Should().Equal("RoleB");
        review.Reason.Should().Contain("partially applied and verified")
            .And.Contain("1 of 2");
        review.FailureCode.Should().Be("mutation:InvalidOperationException");
    }

    [Fact]
    public async Task ApplyDormantPermissionRemediation_ReadbackFails_ClaimsNoRemoval()
    {
        var roles = new FakeRoleStore(["TreasuryOperator"])
        {
            FailVerificationRead = true
        };
        var service = CreateService(roles);

        var review = await service.ApplyDormantPermissionRemediationAsync(
            "treasury-ops-1",
            "compliance-lead",
            Now.AddDays(-120));

        review.Outcome.Should().Be(AccessReviewOutcome.VerificationFailed);
        review.RolesAfter.Should().BeNull();
        review.RemovedRoles.Should().BeEmpty(
            "the service cannot claim a removal without authoritative readback");
        review.Reason.Should().Contain("could not be verified")
            .And.Contain("no role removal is claimed");
    }

    [Fact]
    public async Task UserAccountRoleStore_DormantPrivilegedAccount_DemotesDisablesAndAuditsMutation()
    {
        var accountStore = new FileUserAccountStore(new StorageOptions { RootPath = TestDataRoot });
        await accountStore.UpsertAsync(
            new UserAccountUpsertRequestDto(
                Username: "treasury-ops-1",
                Role: nameof(UserRole.Controller),
                RoleProfileName: null,
                PermissionNames: null,
                NewPassword: "Valid-test-password-123!",
                PasswordHash: null,
                IsDisabled: false,
                PasswordResetRequired: false,
                RequestedBy: "identity-admin",
                Rationale: "Create deterministic dormant-access test account."),
            actor: "identity-admin");
        var service = new AccessReviewService(
            new UserAccountAccessRoleAssignmentStore(accountStore),
            new FixedTimeProvider(Now));

        var review = await service.ApplyDormantPermissionRemediationAsync(
            "treasury-ops-1",
            "compliance-lead",
            Now.AddDays(-120));

        review.Outcome.Should().Be(AccessReviewOutcome.RemediationApplied);
        review.RemovedRoles.Should().Equal(nameof(UserRole.Controller));
        var account = (await accountStore.GetAccountsAsync()).Single();
        account.Role.Should().Be(nameof(UserRole.ReadOnly));
        account.IsDisabled.Should().BeTrue();
        (await accountStore.GetAuditEventsAsync())
            .Should().Contain(evt =>
                evt.Actor == "compliance-lead" &&
                evt.Username == "treasury-ops-1" &&
                evt.IsDisabled &&
                evt.Role == nameof(UserRole.ReadOnly));
    }

    [Fact]
    public async Task GetReviews_MultipleAppliedRuns_RetainsAccurateOutcomesInOrder()
    {
        var roles = new FakeRoleStore(["RoleA"]);
        var service = CreateService(roles);

        var applied = await service.ApplyDormantPermissionRemediationAsync(
            "actor-a",
            "compliance-lead",
            Now.AddDays(-365));
        roles.SetRoles(["RoleB"]);
        var noAction = await service.ApplyDormantPermissionRemediationAsync(
            "actor-a",
            "compliance-lead",
            Now.AddDays(-1));

        service.GetReviews().Should().ContainInOrder(applied, noAction);
        service.GetReviews().Select(review => review.Outcome)
            .Should().Equal(
                AccessReviewOutcome.RemediationApplied,
                AccessReviewOutcome.NoActionRequired);
    }

    private static AccessReviewService CreateService(FakeRoleStore roles)
        => new(roles, new FixedTimeProvider(Now));

    private enum FakeRemovalMode
    {
        RemoveAll,
        NoOp,
        RemoveFirstThenThrow
    }

    private sealed class FakeRoleStore(IEnumerable<string> initialRoles)
        : IAccessRoleAssignmentStore
    {
        private readonly object _gate = new();
        private List<string> _roles = initialRoles.ToList();
        private int _readCount;

        public FakeRemovalMode RemovalMode { get; init; }

        public bool FailVerificationRead { get; init; }

        public int MutationCount { get; private set; }

        public Task<IReadOnlyList<string>> GetAssignedRolesAsync(
            string actorId,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            lock (_gate)
            {
                _readCount++;
                if (FailVerificationRead && _readCount > 1)
                {
                    throw new IOException("Simulated authoritative readback failure.");
                }

                return Task.FromResult<IReadOnlyList<string>>(_roles.ToArray());
            }
        }

        public Task RemoveRolesAsync(
            string actorId,
            IReadOnlyList<string> roles,
            string performedBy,
            string correlationId,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            lock (_gate)
            {
                MutationCount++;
                if (RemovalMode == FakeRemovalMode.NoOp)
                {
                    return Task.CompletedTask;
                }

                var removals = RemovalMode == FakeRemovalMode.RemoveFirstThenThrow
                    ? roles.Take(1)
                    : roles;
                _roles = _roles
                    .Except(removals, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (RemovalMode == FakeRemovalMode.RemoveFirstThenThrow)
                {
                    throw new InvalidOperationException("Simulated failure after partial mutation.");
                }

                return Task.CompletedTask;
            }
        }

        public void SetRoles(IEnumerable<string> roles)
        {
            lock (_gate)
            {
                _roles = roles.ToList();
                _readCount = 0;
            }
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
