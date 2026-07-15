using System.Text.Json;
using FluentAssertions;
using Meridian.Reporting;
using Meridian.Ui.Shared.Services;

namespace Meridian.Tests.Reporting;

public sealed class ReportingSecureDistributionTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 7, 14, 16, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AccessGrant_IssuesRandomOpaqueSecretAndPersistsOnlyHash()
    {
        var clock = new MutableTimeProvider(FixedNow);
        var store = new InMemoryAccessGrantStore();
        var service = new ReportingAccessGrantService(store, clock);
        var request = new ReportingAccessGrantIssueRequest(
            "tenant-a",
            "board-recipient",
            "package-1",
            FixedNow.AddHours(1),
            ArtifactIds: ["board-pack.pdf"],
            MaxUses: 1);

        var first = await service.IssueAsync(request);
        var second = await service.IssueAsync(request);

        first.Token.Should().HaveLength(64).And.NotBe(second.Token);
        first.GrantId.Should().NotBe(second.GrantId);
        var retained = (await store.GetAsync(first.GrantId))!;
        retained.TokenHashSha256.Should().HaveLength(64).And.NotBe(first.Token);
        JsonSerializer.Serialize(retained).Should().NotContain(first.Token);

        var valid = await service.ValidateAsync(new ReportingAccessGrantValidationRequest(
            first.GrantId,
            first.Token,
            "tenant-a",
            "board-recipient",
            "package-1",
            "board-pack.pdf"));
        var exhausted = await service.ValidateAsync(new ReportingAccessGrantValidationRequest(
            first.GrantId,
            first.Token,
            "tenant-a",
            "board-recipient",
            "package-1",
            "board-pack.pdf"));

        valid.Status.Should().Be(ReportingAccessGrantValidationStatus.Valid);
        valid.Grant!.UseCount.Should().Be(1);
        exhausted.Status.Should().Be(ReportingAccessGrantValidationStatus.UseLimitExceeded);
    }

    [Fact]
    public async Task AccessGrant_EnforcesTokenTenantAudiencePackageArtifactExpiryAndRevocation()
    {
        var clock = new MutableTimeProvider(FixedNow);
        var store = new InMemoryAccessGrantStore();
        var service = new ReportingAccessGrantService(store, clock);
        var secret = await service.IssueAsync(new ReportingAccessGrantIssueRequest(
            "tenant-a",
            "investor-42",
            "package-1",
            FixedNow.AddMinutes(30),
            AllowPackageRead: false,
            ArtifactIds: ["statement.pdf"],
            MaxUses: 2));

        (await ValidateAsync(Token: new string('0', 64))).Should().Be(ReportingAccessGrantValidationStatus.TokenMismatch);
        (await ValidateAsync(TenantId: "tenant-b")).Should().Be(ReportingAccessGrantValidationStatus.TenantMismatch);
        (await ValidateAsync(Audience: "investor-43")).Should().Be(ReportingAccessGrantValidationStatus.AudienceMismatch);
        (await ValidateAsync(PackageId: "package-2")).Should().Be(ReportingAccessGrantValidationStatus.PackageMismatch);
        (await ValidateAsync(ArtifactId: null)).Should().Be(ReportingAccessGrantValidationStatus.ArtifactOutOfScope);
        (await ValidateAsync(ArtifactId: "other.pdf")).Should().Be(ReportingAccessGrantValidationStatus.ArtifactOutOfScope);
        (await ValidateAsync()).Should().Be(ReportingAccessGrantValidationStatus.Valid);

        clock.Advance(TimeSpan.FromMinutes(31));
        (await ValidateAsync()).Should().Be(ReportingAccessGrantValidationStatus.Expired);

        var revocable = await service.IssueAsync(new ReportingAccessGrantIssueRequest(
            "tenant-a",
            "investor-42",
            "package-2",
            clock.GetUtcNow().AddHours(1)));
        (await service.RevokeAsync(revocable.GrantId, "tenant-a", "controller", "recipient removed")).Should().BeTrue();
        var revoked = await service.ValidateAsync(new ReportingAccessGrantValidationRequest(
            revocable.GrantId,
            revocable.Token,
            "tenant-a",
            "investor-42",
            "package-2"));
        revoked.Status.Should().Be(ReportingAccessGrantValidationStatus.Revoked);

        async Task<ReportingAccessGrantValidationStatus> ValidateAsync(
            string? Token = null,
            string TenantId = "tenant-a",
            string Audience = "investor-42",
            string PackageId = "package-1",
            string? ArtifactId = "statement.pdf")
        {
            var result = await service.ValidateAsync(new ReportingAccessGrantValidationRequest(
                secret.GrantId,
                Token ?? secret.Token,
                TenantId,
                Audience,
                PackageId,
                ArtifactId,
                ConsumeUse: false));
            return result.Status;
        }
    }

    [Fact]
    public async Task Dispatcher_QueuesIdempotentlyAndPortalRemainsSentUntilAccessReceipt()
    {
        var clock = new MutableTimeProvider(FixedNow);
        var store = new InMemoryDeliveryStore();
        var dispatcher = CreateDispatcher(store, [new SecurePortalReportingDeliveryTransport(clock)], clock);
        var request = BuildQueueRequest("secure-portal");

        var queued = await dispatcher.QueueAsync(request);
        var duplicate = await dispatcher.QueueAsync(request);
        var dispatched = (await dispatcher.DispatchDueAsync("worker-a")).Should().ContainSingle().Subject;

        queued.State.Should().Be(ReportingDeliveryState.Queued);
        duplicate.JobId.Should().Be(queued.JobId);
        dispatched.State.Should().Be(ReportingDeliveryState.Sent);
        dispatched.State.Should().NotBe(ReportingDeliveryState.Delivered);
        dispatched.AttemptCount.Should().Be(1);
        dispatched.Receipts.Should().ContainSingle(receipt => receipt.Kind == ReportingDeliveryReceiptKind.Published);

        var accessedReceipt = new ReportingDeliveryReceipt(
            "portal-access-1",
            ReportingDeliveryReceiptKind.Accessed,
            clock.GetUtcNow().AddMinutes(2),
            "secure-portal",
            "portal:package-1");
        var delivered = await dispatcher.AppendReceiptAsync(dispatched.JobId, "tenant-a", accessedReceipt);
        var duplicateReceipt = await dispatcher.AppendReceiptAsync(dispatched.JobId, "tenant-a", accessedReceipt);

        delivered.State.Should().Be(ReportingDeliveryState.Delivered);
        duplicateReceipt.Receipts.Should().HaveCount(2);
    }

    [Fact]
    public async Task Dispatcher_MissingTransportBlocksFailClosed()
    {
        var clock = new MutableTimeProvider(FixedNow);
        var store = new InMemoryDeliveryStore();
        var dispatcher = CreateDispatcher(store, [], clock);
        await dispatcher.QueueAsync(BuildQueueRequest("not-configured"));

        var result = (await dispatcher.DispatchDueAsync("worker-a")).Should().ContainSingle().Subject;

        result.State.Should().Be(ReportingDeliveryState.Blocked);
        result.LastErrorCode.Should().Be("MISSING_TRANSPORT");
        result.AttemptCount.Should().Be(1);
        result.Receipts.Should().BeEmpty();
    }

    [Fact]
    public async Task Dispatcher_RejectsMissingUnreleasedMismatchedOrUnverifiedReleaseAuthorization()
    {
        var clock = new MutableTimeProvider(FixedNow);
        var store = new InMemoryDeliveryStore();
        var valid = BuildQueueRequest("secure-portal");
        var dispatcher = CreateDispatcher(store, [new SecurePortalReportingDeliveryTransport(clock)], clock);

        Func<Task> missing = () => dispatcher.QueueAsync(valid with { ReleaseAuthorization = null });
        Func<Task> draft = () => dispatcher.QueueAsync(valid with
        {
            ReleaseAuthorization = valid.ReleaseAuthorization! with { State = ReportingReleaseState.Draft }
        });
        Func<Task> crossTenant = () => dispatcher.QueueAsync(valid with
        {
            ReleaseAuthorization = valid.ReleaseAuthorization! with { TenantId = "tenant-b" }
        });
        var rejecting = CreateDispatcher(
            store,
            [new SecurePortalReportingDeliveryTransport(clock)],
            clock,
            new StaticReleaseAuthorizationVerifier(false, "SIGNATURE_INVALID"));
        Func<Task> unverified = () => rejecting.QueueAsync(valid);

        await missing.Should().ThrowAsync<InvalidOperationException>();
        await draft.Should().ThrowAsync<InvalidOperationException>();
        await crossTenant.Should().ThrowAsync<UnauthorizedAccessException>();
        await unverified.Should().ThrowAsync<UnauthorizedAccessException>();
        store.Count.Should().Be(0);
    }

    [Fact]
    public async Task Dispatcher_ClassifiesTransientRetryBackoffAndExhaustion()
    {
        var clock = new MutableTimeProvider(FixedNow);
        var store = new InMemoryDeliveryStore();
        var transport = new SequencedTransport(
            "relay",
            ReportingDeliveryTransportResult.TransientFailure("HTTP_503"),
            ReportingDeliveryTransportResult.TransientFailure("HTTP_429"));
        var dispatcher = CreateDispatcher(store, [transport], clock);
        await dispatcher.QueueAsync(BuildQueueRequest("relay", maxAttempts: 2));

        var first = (await dispatcher.DispatchDueAsync("worker-a")).Single();
        first.State.Should().Be(ReportingDeliveryState.RetryScheduled);
        first.NextAttemptAtUtc.Should().Be(FixedNow.AddSeconds(10));
        (await dispatcher.DispatchDueAsync("worker-a")).Should().BeEmpty();

        clock.Advance(TimeSpan.FromSeconds(10));
        var second = (await dispatcher.DispatchDueAsync("worker-a")).Single();
        second.State.Should().Be(ReportingDeliveryState.Failed);
        second.NextAttemptAtUtc.Should().BeNull();
        second.AttemptCount.Should().Be(2);
        transport.CallCount.Should().Be(2);
    }

    [Fact]
    public async Task Dispatcher_AtomicLeasePreventsConcurrentDoubleDispatch()
    {
        var clock = new MutableTimeProvider(FixedNow);
        var store = new InMemoryDeliveryStore();
        var transport = new SequencedTransport(
            "secure-portal",
            ReportingDeliveryTransportResult.Sent("PORTAL_PUBLISHED"));
        var dispatcher = CreateDispatcher(store, [transport], clock);
        await dispatcher.QueueAsync(BuildQueueRequest("secure-portal"));

        var results = await Task.WhenAll(
            dispatcher.DispatchDueAsync("worker-a"),
            dispatcher.DispatchDueAsync("worker-b"));

        results.Sum(static result => result.Count).Should().Be(1);
        transport.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task HttpRelay_IssuesEphemeralFragmentGrantAndPersistsNoRawToken()
    {
        var clock = new MutableTimeProvider(FixedNow);
        var grantStore = new InMemoryAccessGrantStore();
        var grantService = new ReportingAccessGrantService(grantStore, clock);
        var relay = new RecordingRelayClient(new ReportingHttpRelayResult(
            IsSuccess: true,
            IsTransientFailure: false,
            Code: "ACCEPTED",
            ProviderMessageId: "relay-message-1"));
        var transport = new HttpRelayReportingDeliveryTransport(relay, grantService, clock);
        var deliveryStore = new InMemoryDeliveryStore();
        var dispatcher = CreateDispatcher(deliveryStore, [transport], clock);
        var request = BuildQueueRequest(
            "http-relay",
            access: new ReportingDeliveryAccessPolicy(
                "investor-42",
                "https://reports.example.test/portal/reporting/access",
                TimeSpan.FromHours(1),
                ArtifactIds: ["statement.pdf"]));

        var queued = await dispatcher.QueueAsync(request);
        var sent = (await dispatcher.DispatchDueAsync("worker-a")).Single();

        sent.State.Should().Be(ReportingDeliveryState.Sent);
        sent.AccessGrantId.Should().NotBeNullOrWhiteSpace();
        sent.ProviderMessageId.Should().Be("relay-message-1");
        var message = relay.Messages.Should().ContainSingle().Subject;
        message.RecipientAccessUri.Should().Contain($"/{sent.AccessGrantId}#token=");
        var rawToken = new Uri(message.RecipientAccessUri).Fragment["#token=".Length..];
        rawToken.Should().HaveLength(64);
        JsonSerializer.Serialize(queued).Should().NotContain(rawToken);
        JsonSerializer.Serialize(sent).Should().NotContain(rawToken);
        var retainedGrant = (await grantStore.GetAsync(sent.AccessGrantId!))!;
        retainedGrant.TokenHashSha256.Should().NotBe(rawToken);

        var validation = await grantService.ValidateAsync(new ReportingAccessGrantValidationRequest(
            retainedGrant.GrantId,
            rawToken,
            "tenant-a",
            "investor-42",
            "package-1",
            "statement.pdf",
            ConsumeUse: false));
        validation.Status.Should().Be(ReportingAccessGrantValidationStatus.Valid);
    }

    [Fact]
    public async Task HttpRelay_RevokesGrantBeforeSchedulingTransientRetry()
    {
        var clock = new MutableTimeProvider(FixedNow);
        var grantStore = new InMemoryAccessGrantStore();
        var grantService = new ReportingAccessGrantService(grantStore, clock);
        var relay = new RecordingRelayClient(new ReportingHttpRelayResult(
            IsSuccess: false,
            IsTransientFailure: true,
            Code: "HTTP_503",
            Detail: "relay unavailable"));
        var transport = new HttpRelayReportingDeliveryTransport(relay, grantService, clock);
        var deliveryStore = new InMemoryDeliveryStore();
        var dispatcher = CreateDispatcher(deliveryStore, [transport], clock);
        await dispatcher.QueueAsync(BuildQueueRequest(
            "http-relay",
            access: new ReportingDeliveryAccessPolicy(
                "investor-42",
                "https://reports.example.test/portal/reporting/access",
                TimeSpan.FromHours(1))));

        var retry = (await dispatcher.DispatchDueAsync("worker-a")).Single();

        retry.State.Should().Be(ReportingDeliveryState.RetryScheduled);
        retry.LastErrorCode.Should().Be("HTTP_503");
        retry.AccessGrantId.Should().BeNull();
        grantStore.Records.Should().ContainSingle();
        grantStore.Records.Single().RevokedAtUtc.Should().Be(FixedNow);
    }

    private static ReportingDeliveryDispatcher CreateDispatcher(
        IReportingDeliveryStore store,
        IEnumerable<IReportingDeliveryTransport> transports,
        TimeProvider clock,
        IReportingReleaseAuthorizationVerifier? releaseVerifier = null) =>
        new(
            store,
            transports,
            releaseVerifier ?? new StaticReleaseAuthorizationVerifier(true, "VERIFIED"),
            clock,
            new ReportingDeliveryDispatcherOptions(
                TimeSpan.FromMinutes(1),
                TimeSpan.FromSeconds(10),
                TimeSpan.FromMinutes(1),
                BatchSize: 10));

    private static ReportingDeliveryQueueRequest BuildQueueRequest(
        string transportId,
        int maxAttempts = 3,
        ReportingDeliveryAccessPolicy? access = null) =>
        new(
            "tenant-a",
            "package-1",
            BuildReleaseAuthorization(),
            "investor-relations",
            transportId,
            "controller",
            new ReportingDeliveryPayload(
                "Investor 42",
                "Limited partner",
                "investor42@example.test",
                "Your Meridian report is available",
                "Use the secure recipient link to review the released report package.",
                "/portal/reporting/packages/package-1",
                access),
            maxAttempts);

    private static ReportingDeliveryReleaseAuthorization BuildReleaseAuthorization() =>
        new(
            "release-auth-1",
            ReportingReleaseState.Released,
            "tenant-a",
            "package-1",
            "release-v1",
            new string('a', 64),
            [new ReportingReleasedArtifactReference(
                "statement.pdf",
                "reporting-artifact://tenant-a/statement.pdf",
                new string('b', 64),
                1024)],
            ["release-evidence:1"],
            FixedNow,
            "release-officer",
            "signed-release-proof");

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan duration) => _utcNow = _utcNow.Add(duration);
    }

    private sealed class InMemoryAccessGrantStore : IReportingAccessGrantStore
    {
        private readonly object _gate = new();
        private readonly Dictionary<string, ReportingAccessGrantRecord> _records = new(StringComparer.Ordinal);

        public IReadOnlyList<ReportingAccessGrantRecord> Records
        {
            get
            {
                lock (_gate)
                {
                    return _records.Values.ToArray();
                }
            }
        }

        public Task<ReportingAccessGrantRecord?> GetAsync(string grantId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            lock (_gate)
            {
                return Task.FromResult(_records.GetValueOrDefault(grantId));
            }
        }

        public Task<bool> TryCreateAsync(ReportingAccessGrantRecord grant, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            lock (_gate)
            {
                if (_records.ContainsKey(grant.GrantId))
                {
                    return Task.FromResult(false);
                }

                _records.Add(grant.GrantId, grant);
                return Task.FromResult(true);
            }
        }

        public Task<bool> TryUpdateAsync(
            string grantId,
            long expectedVersion,
            ReportingAccessGrantRecord updatedGrant,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            lock (_gate)
            {
                if (!_records.TryGetValue(grantId, out var current)
                    || current.Version != expectedVersion
                    || updatedGrant.Version != expectedVersion + 1)
                {
                    return Task.FromResult(false);
                }

                _records[grantId] = updatedGrant;
                return Task.FromResult(true);
            }
        }
    }

    private sealed class InMemoryDeliveryStore : IReportingDeliveryStore
    {
        private readonly object _gate = new();
        private readonly Dictionary<string, ReportingDeliveryJobRecord> _jobs = new(StringComparer.Ordinal);

        public int Count
        {
            get
            {
                lock (_gate)
                {
                    return _jobs.Count;
                }
            }
        }

        public Task<ReportingDeliveryJobRecord?> GetAsync(string jobId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            lock (_gate)
            {
                return Task.FromResult(_jobs.GetValueOrDefault(jobId));
            }
        }

        public Task<ReportingDeliveryJobRecord?> GetByIdempotencyKeyAsync(
            string idempotencyKey,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            lock (_gate)
            {
                return Task.FromResult(_jobs.Values.FirstOrDefault(job =>
                    string.Equals(job.IdempotencyKey, idempotencyKey, StringComparison.Ordinal)));
            }
        }

        public Task<bool> TryCreateAsync(ReportingDeliveryJobRecord job, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            lock (_gate)
            {
                if (_jobs.ContainsKey(job.JobId)
                    || _jobs.Values.Any(item => string.Equals(item.IdempotencyKey, job.IdempotencyKey, StringComparison.Ordinal)))
                {
                    return Task.FromResult(false);
                }

                _jobs.Add(job.JobId, job);
                return Task.FromResult(true);
            }
        }

        public Task<IReadOnlyList<ReportingDeliveryJobRecord>> ClaimDueAsync(
            DateTimeOffset nowUtc,
            string leaseOwner,
            TimeSpan leaseDuration,
            int take,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            lock (_gate)
            {
                var due = _jobs.Values
                    .Where(job =>
                        (job.State is ReportingDeliveryState.Queued or ReportingDeliveryState.RetryScheduled
                         && job.NextAttemptAtUtc <= nowUtc)
                        || (job.State == ReportingDeliveryState.Dispatching
                            && job.LeaseExpiresAtUtc <= nowUtc))
                    .OrderBy(static job => job.NextAttemptAtUtc)
                    .ThenBy(static job => job.JobId, StringComparer.Ordinal)
                    .Take(take)
                    .ToArray();
                var claimed = new List<ReportingDeliveryJobRecord>(due.Length);
                foreach (var job in due)
                {
                    var updated = job with
                    {
                        State = ReportingDeliveryState.Dispatching,
                        UpdatedAtUtc = nowUtc,
                        LeaseOwner = leaseOwner,
                        LeaseExpiresAtUtc = nowUtc.Add(leaseDuration),
                        Version = job.Version + 1
                    };
                    _jobs[job.JobId] = updated;
                    claimed.Add(updated);
                }

                return Task.FromResult<IReadOnlyList<ReportingDeliveryJobRecord>>(claimed);
            }
        }

        public Task<bool> TryUpdateAsync(
            string jobId,
            long expectedVersion,
            ReportingDeliveryJobRecord updatedJob,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            lock (_gate)
            {
                if (!_jobs.TryGetValue(jobId, out var current)
                    || current.Version != expectedVersion
                    || updatedJob.Version != expectedVersion + 1)
                {
                    return Task.FromResult(false);
                }

                _jobs[jobId] = updatedJob;
                return Task.FromResult(true);
            }
        }
    }

    private sealed class StaticReleaseAuthorizationVerifier(bool authorized, string code)
        : IReportingReleaseAuthorizationVerifier
    {
        public Task<ReportingReleaseAuthorizationResult> VerifyAsync(
            ReportingDeliveryReleaseAuthorization authorization,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(new ReportingReleaseAuthorizationResult(authorized, code));
        }
    }

    private sealed class SequencedTransport(
        string transportId,
        params ReportingDeliveryTransportResult[] results) : IReportingDeliveryTransport
    {
        private int _callCount;

        public string TransportId { get; } = transportId;

        public int CallCount => _callCount;

        public Task<ReportingDeliveryTransportResult> DeliverAsync(
            ReportingDeliveryTransportRequest request,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            var index = Interlocked.Increment(ref _callCount) - 1;
            return Task.FromResult(results[Math.Min(index, results.Length - 1)]);
        }
    }

    private sealed class RecordingRelayClient(ReportingHttpRelayResult result) : IReportingHttpRelayClient
    {
        public List<ReportingHttpRelayMessage> Messages { get; } = [];

        public Task<ReportingHttpRelayResult> SendAsync(
            ReportingHttpRelayMessage message,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            Messages.Add(message);
            return Task.FromResult(result);
        }
    }
}
