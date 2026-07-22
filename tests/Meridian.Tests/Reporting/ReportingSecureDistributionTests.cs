using System.Collections.Immutable;
using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
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
            "run-1",
            "package-1",
            FixedNow.AddHours(1),
            ArtifactIds: ["board-pack.pdf"],
            MaxUses: 1);

        var first = await service.IssueAsync(request);
        var second = await service.IssueAsync(request);

        first.Token.Should().HaveLength(64).And.NotBe(second.Token);
        first.GrantId.Should().NotBe(second.GrantId);
        var retained = (await store.GetAsync(first.GrantId))!;
        retained.Should().NotBeNull();
        retained.TokenHashSha256.Should().HaveLength(64).And.NotBe(first.Token);
        JsonSerializer.Serialize(retained).Should().NotContain(first.Token);

        var valid = await service.ValidateAsync(new ReportingAccessGrantValidationRequest(
            first.GrantId,
            first.Token,
            "tenant-a",
            "board-recipient",
            "run-1",
            "package-1",
            "board-pack.pdf"));
        var exhausted = await service.ValidateAsync(new ReportingAccessGrantValidationRequest(
            first.GrantId,
            first.Token,
            "tenant-a",
            "board-recipient",
            "run-1",
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
            "run-1",
            "package-1",
            FixedNow.AddMinutes(30),
            AllowPackageRead: false,
            ArtifactIds: ["statement.pdf"],
            MaxUses: 2));

        (await ValidateAsync(Token: new string('0', 64))).Should().Be(ReportingAccessGrantValidationStatus.TokenMismatch);
        (await ValidateAsync(TenantId: "tenant-b")).Should().Be(ReportingAccessGrantValidationStatus.TenantMismatch);
        (await ValidateAsync(Audience: "investor-43")).Should().Be(ReportingAccessGrantValidationStatus.AudienceMismatch);
        (await ValidateAsync(RunId: "run-2")).Should().Be(ReportingAccessGrantValidationStatus.PackageMismatch);
        (await ValidateAsync(PackageId: "package-2")).Should().Be(ReportingAccessGrantValidationStatus.PackageMismatch);
        (await ValidateAsync(ArtifactId: null)).Should().Be(ReportingAccessGrantValidationStatus.ArtifactOutOfScope);
        (await ValidateAsync(ArtifactId: "other.pdf")).Should().Be(ReportingAccessGrantValidationStatus.ArtifactOutOfScope);
        (await ValidateAsync()).Should().Be(ReportingAccessGrantValidationStatus.Valid);

        clock.Advance(TimeSpan.FromMinutes(31));
        (await ValidateAsync()).Should().Be(ReportingAccessGrantValidationStatus.Expired);

        var revocable = await service.IssueAsync(new ReportingAccessGrantIssueRequest(
            "tenant-a",
            "investor-42",
            "run-2",
            "package-2",
            clock.GetUtcNow().AddHours(1)));
        (await service.RevokeAsync(revocable.GrantId, "tenant-a", "controller", "recipient removed")).Should().BeTrue();
        var revoked = await service.ValidateAsync(new ReportingAccessGrantValidationRequest(
            revocable.GrantId,
            revocable.Token,
            "tenant-a",
            "investor-42",
            "run-2",
            "package-2"));
        revoked.Status.Should().Be(ReportingAccessGrantValidationStatus.Revoked);

        async Task<ReportingAccessGrantValidationStatus> ValidateAsync(
            string? Token = null,
            string TenantId = "tenant-a",
            string Audience = "investor-42",
            string RunId = "run-1",
            string PackageId = "package-1",
            string? ArtifactId = "statement.pdf")
        {
            var result = await service.ValidateAsync(new ReportingAccessGrantValidationRequest(
                secret.GrantId,
                Token ?? secret.Token,
                TenantId,
                Audience,
                RunId,
                PackageId,
                ArtifactId,
                ConsumeUse: false));
            return result.Status;
        }
    }

    [Fact]
    public async Task AccessGrant_ConsumptionUsesOneAuthorityTimeAndCannotRetainUseAfterExpiry()
    {
        var clock = new AdvancingTimeProvider(FixedNow, TimeSpan.FromMinutes(2));
        var store = new InMemoryAccessGrantStore();
        var service = new ReportingAccessGrantService(store, clock);
        var secret = await service.IssueAsync(new ReportingAccessGrantIssueRequest(
            "tenant-a",
            "investor-42",
            "run-1",
            "package-1",
            FixedNow.AddMinutes(3),
            AllowPackageRead: false,
            ArtifactIds: ["statement.pdf"],
            MaxUses: 1));

        var consumed = await service.ValidateAsync(new ReportingAccessGrantValidationRequest(
            secret.GrantId,
            secret.Token,
            "tenant-a",
            "investor-42",
            "run-1",
            "package-1",
            "statement.pdf",
            ConsumeUse: true));

        consumed.Status.Should().Be(ReportingAccessGrantValidationStatus.Valid);
        consumed.Grant!.LastUsedAtUtc.Should().Be(FixedNow.AddMinutes(2));
        consumed.Grant.LastUsedAtUtc.Should().BeBefore(consumed.Grant.ExpiresAtUtc);
        clock.CallCount.Should().Be(2,
            "issuance and one validation instant are sufficient; consumption must not reread the clock");
    }

    [Fact]
    public async Task Dispatcher_IdempotencyRejectsChangedPayloadAndReceiptReplayMutation()
    {
        var clock = new MutableTimeProvider(FixedNow);
        var store = new InMemoryDeliveryStore();
        var dispatcher = CreateDispatcher(store, [new SecurePortalReportingDeliveryTransport(clock)], clock);
        var request = BuildQueueRequest("secure-portal");
        var queued = await dispatcher.QueueAsync(request);
        var changed = request with
        {
            Payload = request.Payload with { Destination = "different-recipient@example.test" }
        };

        var replayWithChangedPayload = () => dispatcher.QueueAsync(changed);

        await replayWithChangedPayload.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*idempotency key*");
        store.Count.Should().Be(1);

        var sent = (await dispatcher.DispatchDueAsync("worker-a")).Single();
        var receipt = new ReportingDeliveryReceipt(
            "provider-event-1",
            ReportingDeliveryReceiptKind.Delivered,
            FixedNow,
            "secure-portal",
            sent.ProviderMessageId);
        await dispatcher.AppendReceiptAsync(sent.JobId, sent.TenantId, receipt);
        var conflictingReplay = () => dispatcher.AppendReceiptAsync(
            sent.JobId,
            sent.TenantId,
            receipt with { Kind = ReportingDeliveryReceiptKind.Bounced });

        await conflictingReplay.Should().ThrowAsync<InvalidDataException>()
            .WithMessage("*different immutable content*");
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

    [Theory]
    [InlineData(256, true)]
    [InlineData(257, false)]
    public async Task Dispatcher_EnforcesProviderMessageIdBoundaryBeforeDurableCommit(
        int providerMessageIdLength,
        bool expectedSent)
    {
        var clock = new MutableTimeProvider(FixedNow);
        var store = new InMemoryDeliveryStore();
        var providerMessageId = new string('p', providerMessageIdLength);
        var transport = new SequencedTransport(
            "relay",
            ReportingDeliveryTransportResult.Sent("ACCEPTED", providerMessageId));
        var dispatcher = CreateDispatcher(store, [transport], clock);
        await dispatcher.QueueAsync(BuildQueueRequest("relay"));

        var retained = (await dispatcher.DispatchDueAsync("worker-a")).Single();

        if (expectedSent)
        {
            retained.State.Should().Be(ReportingDeliveryState.Sent);
            retained.ProviderMessageId.Should().Be(providerMessageId);
        }
        else
        {
            retained.State.Should().Be(ReportingDeliveryState.Failed);
            retained.LastErrorCode.Should().Be("TRANSPORT_IDENTIFIER_INVALID");
            retained.ProviderMessageId.Should().BeNull();
        }
    }

    [Fact]
    public async Task Dispatcher_MissingTransportBlocksFailClosed()
    {
        var clock = new MutableTimeProvider(FixedNow);
        var store = new InMemoryDeliveryStore();
        var dispatcher = CreateDispatcher(store, [], clock);
        await dispatcher.QueueAsync(BuildQueueRequest("not-configured"));

        var result = (await dispatcher.DispatchDueAsync("worker-a")).Should().ContainSingle().Subject;

        result.State.Should().Be(ReportingDeliveryState.Failed);
        result.LastErrorCode.Should().Be("MISSING_TRANSPORT");
        result.AttemptCount.Should().Be(1);
        result.Receipts.Should().ContainSingle(receipt => receipt.Kind == ReportingDeliveryReceiptKind.Failed);
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
        var transport = new HttpRelayReportingDeliveryTransport(
            relay,
            grantService,
            CreateCredentialDeriver(),
            clock);
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
        var recipientUri = new Uri(message.RecipientAccessUri);
        recipientUri.AbsolutePath.Should().EndWith($"/{sent.AccessGrantId}/exchange");
        recipientUri.Query.Should().BeEmpty();
        var fragment = recipientUri.Fragment.TrimStart('#')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(static pair => pair.Split('=', 2))
            .ToDictionary(
                static pair => Uri.UnescapeDataString(pair[0]),
                static pair => Uri.UnescapeDataString(pair[1]),
                StringComparer.Ordinal);
        var rawToken = fragment["token"];
        rawToken.Should().HaveLength(64);
        fragment["artifact"].Should().Be("statement.pdf");
        JsonSerializer.Serialize(queued).Should().NotContain(rawToken);
        JsonSerializer.Serialize(sent).Should().NotContain(rawToken);
        var retainedGrant = (await grantStore.GetAsync(sent.AccessGrantId!))!;
        retainedGrant.Should().NotBeNull();
        retainedGrant.TokenHashSha256.Should().NotBe(rawToken);

        var validation = await grantService.ValidateAsync(new ReportingAccessGrantValidationRequest(
            retainedGrant.GrantId,
            rawToken,
            "tenant-a",
            "investor-42",
            "run-1",
            "package-1",
            "statement.pdf",
            ConsumeUse: false));
        validation.Status.Should().Be(ReportingAccessGrantValidationStatus.Valid);
    }

    [Fact]
    public async Task HttpRelay_TransientResponseRetainsOneCredentialForStableReplay()
    {
        var clock = new MutableTimeProvider(FixedNow);
        var grantStore = new InMemoryAccessGrantStore();
        var grantService = new ReportingAccessGrantService(grantStore, clock);
        var relay = new RecordingRelayClient(new ReportingHttpRelayResult(
            IsSuccess: false,
            IsTransientFailure: true,
            Code: "HTTP_503",
            Detail: "relay unavailable"));
        var transport = new HttpRelayReportingDeliveryTransport(
            relay,
            grantService,
            CreateCredentialDeriver(),
            clock);
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
        retry.LastErrorCode.Should().Be("RELAY_OUTCOME_UNKNOWN");
        retry.AccessGrantId.Should().NotBeNull();
        grantStore.Records.Should().ContainSingle();
        grantStore.Records.Single().RevokedAtUtc.Should().BeNull();

        clock.Advance(TimeSpan.FromSeconds(10));
        var secondRetry = (await dispatcher.DispatchDueAsync("worker-a")).Single();
        secondRetry.State.Should().Be(ReportingDeliveryState.RetryScheduled);
        relay.Messages.Should().HaveCount(2);
        relay.Messages.Select(static message => message.IdempotencyKey)
            .Distinct(StringComparer.Ordinal)
            .Should().ContainSingle("transient relay responses do not prove provider rejection");
        relay.Messages.Select(static message => message.RecipientAccessUri)
            .Distinct(StringComparer.Ordinal)
            .Should().ContainSingle();
        grantStore.Records.Should().ContainSingle(grant =>
            grant.GrantId == retry.AccessGrantId && grant.RevokedAtUtc == null);
    }

    [Fact]
    public async Task HttpRelay_TransientGrantStoreFailure_RestartsWithExactBoundAttemptAndNoOrphanGrant()
    {
        var clock = new MutableTimeProvider(FixedNow);
        var retainedGrants = new InMemoryAccessGrantStore();
        var grantStore = new FailOnceAccessGrantStore(retainedGrants);
        var relay = new RecordingRelayClient(new ReportingHttpRelayResult(
            IsSuccess: true,
            IsTransientFailure: false,
            Code: "ACCEPTED",
            ProviderMessageId: "relay-message-after-grant-store-recovery"));
        var deliveryStore = new InMemoryDeliveryStore();
        var firstDispatcher = CreateDispatcher(
            deliveryStore,
            [new HttpRelayReportingDeliveryTransport(
                relay,
                new ReportingAccessGrantService(grantStore, clock),
                CreateCredentialDeriver(),
                clock)],
            clock);
        var queued = await firstDispatcher.QueueAsync(BuildQueueRequest(
            "http-relay",
            access: new ReportingDeliveryAccessPolicy(
                "investor-42",
                "https://reports.example.test/portal/reporting/access",
                TimeSpan.FromHours(1),
                ArtifactIds: ["statement.pdf"])));

        var retry = (await firstDispatcher.DispatchDueAsync("worker-before-restart")).Single();

        retry.State.Should().Be(ReportingDeliveryState.RetryScheduled);
        retry.LastErrorCode.Should().Be("RELAY_OUTCOME_UNKNOWN");
        retry.AccessGrantId.Should().NotBeNullOrWhiteSpace();
        retry.IdempotencyKey.Should().Be(queued.IdempotencyKey);
        retainedGrants.Records.Should().BeEmpty("the failed database create must not leave an orphan grant");
        relay.Messages.Should().BeEmpty("the provider is not called before the durable grant exists");

        clock.Advance(TimeSpan.FromSeconds(10));
        var restartedDispatcher = CreateDispatcher(
            deliveryStore,
            [new HttpRelayReportingDeliveryTransport(
                relay,
                new ReportingAccessGrantService(grantStore, clock),
                CreateCredentialDeriver(),
                clock)],
            clock);
        var sent = (await restartedDispatcher.DispatchDueAsync("worker-after-restart")).Single();

        sent.State.Should().Be(ReportingDeliveryState.Sent);
        sent.AccessGrantId.Should().Be(retry.AccessGrantId);
        sent.IdempotencyKey.Should().Be(retry.IdempotencyKey);
        relay.Messages.Should().ContainSingle();
        retainedGrants.Records.Should().ContainSingle(grant =>
            grant.GrantId == retry.AccessGrantId && grant.RevokedAtUtc == null);
    }

    [Fact]
    public async Task HttpRelay_CallerCancellationIsDistinctAndRetainsStableReplayCredential()
    {
        var clock = new MutableTimeProvider(FixedNow);
        var grantStore = new InMemoryAccessGrantStore();
        var grantService = new ReportingAccessGrantService(grantStore, clock);
        using var cancellation = new CancellationTokenSource();
        var relay = new CancellingRelayClient(cancellation);
        var transport = new HttpRelayReportingDeliveryTransport(
            relay,
            grantService,
            CreateCredentialDeriver(),
            clock);
        var deliveryStore = new InMemoryDeliveryStore();
        var dispatcher = CreateDispatcher(deliveryStore, [transport], clock);
        await dispatcher.QueueAsync(BuildQueueRequest(
            "http-relay",
            access: new ReportingDeliveryAccessPolicy(
                "investor-42",
                "https://reports.example.test/portal/reporting/access",
                TimeSpan.FromHours(1))));

        var retry = (await dispatcher.DispatchDueAsync("worker-a", cancellation.Token)).Single();

        retry.State.Should().Be(ReportingDeliveryState.RetryScheduled);
        retry.LastErrorCode.Should().Be("TRANSPORT_CANCELLED");
        retry.AccessGrantId.Should().NotBeNull();
        grantStore.Records.Should().ContainSingle();
        grantStore.Records.Single().RevokedAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task HttpRelay_TransientResponseCommitCrash_ReplaysSameAttemptAndThenSends()
    {
        var clock = new MutableTimeProvider(FixedNow);
        var grantStore = new InMemoryAccessGrantStore();
        var grantService = new ReportingAccessGrantService(grantStore, clock);
        var relay = new TransientThenAcceptingRelayClient();
        var deliveryStore = new InMemoryDeliveryStore { FailNextUpdate = true };
        var firstDispatcher = CreateDispatcher(
            deliveryStore,
            [new HttpRelayReportingDeliveryTransport(
                relay,
                grantService,
                CreateCredentialDeriver(),
                clock)],
            clock);
        var queued = await firstDispatcher.QueueAsync(BuildQueueRequest(
            "http-relay",
            access: new ReportingDeliveryAccessPolicy(
                "investor-42",
                "https://reports.example.test/portal/reporting/access",
                TimeSpan.FromHours(1),
                ArtifactIds: ["statement.pdf"])));

        var interrupted = () => firstDispatcher.DispatchDueAsync("worker-before-crash");
        await interrupted.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*could not be committed*");
        (await deliveryStore.GetAsync(queued.JobId))!.State.Should().Be(ReportingDeliveryState.Dispatching);
        grantStore.Records.Should().ContainSingle(grant => grant.RevokedAtUtc == null);

        clock.Advance(TimeSpan.FromMinutes(2));
        var restartedDispatcher = CreateDispatcher(
            deliveryStore,
            [new HttpRelayReportingDeliveryTransport(
                relay,
                new ReportingAccessGrantService(grantStore, clock),
                CreateCredentialDeriver(),
                clock)],
            clock);
        var sent = (await restartedDispatcher.DispatchDueAsync("worker-after-crash")).Single();

        sent.State.Should().Be(ReportingDeliveryState.Sent);
        sent.ProviderMessageId.Should().Be("provider-message-after-transient");
        sent.AccessGrantId.Should().Be(grantStore.Records.Single().GrantId);
        relay.Messages.Should().HaveCount(2);
        relay.Messages.Select(static message => message.IdempotencyKey)
            .Distinct(StringComparer.Ordinal).Should().ContainSingle();
        relay.Messages.Select(static message => message.RecipientAccessUri)
            .Distinct(StringComparer.Ordinal).Should().ContainSingle();
        grantStore.Records.Should().ContainSingle(grant => grant.RevokedAtUtc == null);
    }

    [Fact]
    public async Task HttpRelay_ClientTimeoutRetainsCredentialAndSchedulesStableReplay()
    {
        var clock = new MutableTimeProvider(FixedNow);
        var grantStore = new InMemoryAccessGrantStore();
        var grantService = new ReportingAccessGrantService(grantStore, clock);
        var relay = new ConfiguredReportingHttpRelayClient(
            new SingleHttpClientFactory(new HttpClient(new TimeoutHttpMessageHandler())),
            new ReportingHttpRelayClientOptions(
                new Uri("https://relay.example.test/messages"),
                new string('s', 32),
                TimeSpan.FromSeconds(10)));
        var transport = new HttpRelayReportingDeliveryTransport(
            relay,
            grantService,
            CreateCredentialDeriver(),
            clock);
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
        retry.LastErrorCode.Should().Be("RELAY_OUTCOME_UNKNOWN");
        retry.LastError.Should().Contain("same provider idempotency key");
        retry.AccessGrantId.Should().NotBeNull();
        grantStore.Records.Should().ContainSingle(grant =>
            grant.GrantId == retry.AccessGrantId && grant.RevokedAtUtc == null);
    }

    [Fact]
    public async Task HttpRelay_AcceptedThenTimedOut_ReplaysOneProviderMessageAndUsableCredential()
    {
        var clock = new MutableTimeProvider(FixedNow);
        var grantStore = new InMemoryAccessGrantStore();
        var grantService = new ReportingAccessGrantService(grantStore, clock);
        var relay = new AcceptThenTimeoutRelayClient();
        var transport = new HttpRelayReportingDeliveryTransport(
            relay,
            grantService,
            CreateCredentialDeriver(),
            clock);
        var deliveryStore = new InMemoryDeliveryStore();
        var dispatcher = CreateDispatcher(deliveryStore, [transport], clock);
        await dispatcher.QueueAsync(BuildQueueRequest(
            "http-relay",
            access: new ReportingDeliveryAccessPolicy(
                "investor-42",
                "https://reports.example.test/portal/reporting/access",
                TimeSpan.FromHours(1),
                ArtifactIds: ["statement.pdf"])));

        var unknown = (await dispatcher.DispatchDueAsync("worker-a")).Single();
        clock.Advance(TimeSpan.FromSeconds(10));
        var sent = (await dispatcher.DispatchDueAsync("worker-a")).Single();

        unknown.State.Should().Be(ReportingDeliveryState.RetryScheduled);
        unknown.LastErrorCode.Should().Be("RELAY_OUTCOME_UNKNOWN");
        sent.State.Should().Be(ReportingDeliveryState.Sent);
        sent.AttemptCount.Should().Be(2);
        sent.ProviderMessageId.Should().Be("provider-message-ambiguous-1");
        relay.SendCallCount.Should().Be(2);
        relay.AcceptedMessageCount.Should().Be(1);
        relay.Messages.Select(static message => message.IdempotencyKey)
            .Distinct(StringComparer.Ordinal).Should().ContainSingle();
        relay.Messages.Select(static message => message.RecipientAccessUri)
            .Distinct(StringComparer.Ordinal).Should().ContainSingle();
        grantStore.Records.Should().ContainSingle();
        var retainedGrant = grantStore.Records.Single();
        retainedGrant.RevokedAtUtc.Should().BeNull();
        retainedGrant.GrantId.Should().Be(sent.AccessGrantId);
        var token = ExtractFragmentValue(relay.Messages[0].RecipientAccessUri, "token");
        var validation = await grantService.ValidateAsync(new ReportingAccessGrantValidationRequest(
            retainedGrant.GrantId,
            token,
            "tenant-a",
            "investor-42",
            "run-1",
            "package-1",
            "statement.pdf",
            ConsumeUse: false));
        validation.Status.Should().Be(ReportingAccessGrantValidationStatus.Valid);
    }

    [Fact]
    public async Task HttpRelay_HeadersThenStalledBody_TimesOutAcrossReceiptReadWithoutRevokingReplayCredential()
    {
        var clock = new MutableTimeProvider(FixedNow);
        var grantStore = new InMemoryAccessGrantStore();
        var grantService = new ReportingAccessGrantService(grantStore, clock);
        var relay = new ConfiguredReportingHttpRelayClient(
            new SingleHttpClientFactory(new HttpClient(new StalledResponseBodyHttpMessageHandler())),
            new ReportingHttpRelayClientOptions(
                new Uri("https://relay.example.test/messages"),
                new string('s', 32),
                TimeSpan.FromMilliseconds(50)));
        var transport = new HttpRelayReportingDeliveryTransport(
            relay,
            grantService,
            CreateCredentialDeriver(),
            clock);
        var deliveryStore = new InMemoryDeliveryStore();
        var dispatcher = CreateDispatcher(deliveryStore, [transport], clock);
        await dispatcher.QueueAsync(BuildQueueRequest(
            "http-relay",
            access: new ReportingDeliveryAccessPolicy(
                "investor-42",
                "https://reports.example.test/portal/reporting/access",
                TimeSpan.FromHours(1))));
        using var testDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        var retry = (await dispatcher.DispatchDueAsync("worker-a", testDeadline.Token)).Single();

        retry.State.Should().Be(ReportingDeliveryState.RetryScheduled);
        retry.LastErrorCode.Should().Be("RELAY_OUTCOME_UNKNOWN");
        grantStore.Records.Should().ContainSingle(grant => grant.RevokedAtUtc == null);
    }

    [Fact]
    public async Task HttpRelay_RestartAfterProviderAcceptanceReusesOneStableCredentialAndMessage()
    {
        var clock = new MutableTimeProvider(FixedNow);
        var grantStore = new InMemoryAccessGrantStore();
        var grantService = new ReportingAccessGrantService(grantStore, clock);
        var relay = new IdempotentRelayClient();
        var deliveryStore = new InMemoryDeliveryStore { FailNextUpdate = true };
        var request = BuildQueueRequest(
            "http-relay",
            access: new ReportingDeliveryAccessPolicy(
                "investor-42",
                "https://reports.example.test/portal/reporting/access",
                TimeSpan.FromHours(1),
                ArtifactIds: ["statement.pdf"]));
        var firstDispatcher = CreateDispatcher(
            deliveryStore,
            [new HttpRelayReportingDeliveryTransport(
                relay,
                grantService,
                CreateCredentialDeriver(),
                clock)],
            clock);
        var queued = await firstDispatcher.QueueAsync(request);

        var firstDispatch = () => firstDispatcher.DispatchDueAsync("worker-before-restart");
        await firstDispatch.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*could not be committed*");
        grantStore.Records.Should().ContainSingle(grant => grant.RevokedAtUtc == null);
        (await deliveryStore.GetAsync(queued.JobId))!.AccessGrantId.Should().NotBeNull();

        clock.Advance(TimeSpan.FromMinutes(2));
        var restartedDispatcher = CreateDispatcher(
            deliveryStore,
            [new HttpRelayReportingDeliveryTransport(
                relay,
                new ReportingAccessGrantService(grantStore, clock),
                CreateCredentialDeriver(),
                clock)],
            clock);
        var sent = (await restartedDispatcher.DispatchDueAsync("worker-after-restart")).Single();

        sent.State.Should().Be(ReportingDeliveryState.Sent);
        sent.AccessGrantId.Should().Be(grantStore.Records.Single().GrantId);
        sent.ProviderMessageId.Should().Be("provider-message-1");
        relay.SendCallCount.Should().Be(2, "the provider request is replayed after the uncommitted result");
        relay.AcceptedMessageCount.Should().Be(1, "the stable provider idempotency key creates one message");
        relay.ObservedAccessUris.Distinct(StringComparer.Ordinal).Should().ContainSingle(
            "the restart must reproduce the exact same grant id and bearer");
        grantStore.Records.Should().ContainSingle(grant =>
            grant.GrantId == sent.AccessGrantId && grant.RevokedAtUtc == null);

        (await grantService.RevokeAsync(
            sent.AccessGrantId!,
            "tenant-a",
            "controller",
            "restart proof complete")).Should().BeTrue();
        (await grantStore.GetAsync(sent.AccessGrantId!))!.RevokedAtUtc.Should().Be(FixedNow.AddMinutes(2));
    }

    [Fact]
    public async Task ReleasedArtifactIntegrityGate_VerifiesExactBytesAndFailsClosedOnCorruption()
    {
        var bytes = Encoding.UTF8.GetBytes("immutable released reporting bytes");
        var contentHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes))
            .ToLowerInvariant();
        var identity = new ReportingArtifactIdentity("tenant-a", contentHash);
        var scope = new ReportingOperationalScope(
            "tenant-a",
            "organization-a",
            "company-a",
            "fund-a",
            "book-a",
            "2026-07");
        var artifact = new ReportingRetainedArtifactRecord(
            "package-1",
            "run-1",
            "series-1",
            1,
            scope,
            new ReportingAccessScope(
                "policy-a",
                "1",
                ReportingGovernanceAccessMode.CompanyWide,
                OwnerPrincipalId: null,
                AllowOwnerAccess: false,
                Principals: ImmutableArray<ReportingAccessPrincipalScope>.Empty,
                new string('c', 64)),
            new ReportingCertifiedSnapshotScope(
                "tenant-a",
                "organization-a",
                "company-a",
                "fund-a",
                "book-a",
                "2026-07",
                "snapshot-1",
                new string('d', 64),
                "reconciliation-1",
                FixedNow),
            "manifest-1",
            new string('a', 64),
            "statement.pdf",
            "statement.pdf",
            "application/pdf",
            identity,
            bytes.LongLength,
            FixedNow);
        var catalog = new SinglePackageArtifactCatalog(
            new ReportingRetainedArtifactPackage("package-1", [artifact]));
        var store = new MutableArtifactStore(new ReportingArtifactReadResult(
            identity,
            bytes.LongLength,
            FixedNow,
            bytes));
        var gate = new ReportingReleasedArtifactIntegrityGate(catalog, store);
        var authorization = BuildReleaseAuthorization() with
        {
            Artifacts =
            [
                new ReportingReleasedArtifactReference(
                    "statement.pdf",
                    "reporting-artifact://tenant-a/package-1/statement.pdf",
                    contentHash,
                    bytes.LongLength)
            ]
        };

        var verified = await gate.VerifyAsync(authorization);
        store.Result = store.Result with { Content = Encoding.UTF8.GetBytes("corrupt retained bytes") };
        var corrupted = await gate.VerifyAsync(authorization);
        var crossTenant = await gate.VerifyAsync(authorization with { TenantId = "tenant-b" });

        verified.IsAuthorized.Should().BeTrue();
        verified.Code.Should().Be("RELEASE_AND_ARTIFACTS_VERIFIED");
        corrupted.IsAuthorized.Should().BeFalse();
        corrupted.Code.Should().Be("ARTIFACT_BYTES_CORRUPT");
        crossTenant.IsAuthorized.Should().BeFalse();
        crossTenant.Code.Should().Be("ARTIFACT_PACKAGE_NOT_FOUND");
    }

    [Fact]
    public async Task ProviderReceiptHmac_BindsEveryStateFieldAndRejectsStaleOrAlteredPayload()
    {
        var secret = Enumerable.Range(1, 32).Select(static value => (byte)value).ToArray();
        var clock = new MutableTimeProvider(FixedNow);
        var authenticator = new HmacReportingProviderReceiptAuthenticator(secret, clock);
        var command = new SecureReportingDeliveryReceiptCommand(
            "provider-event-1",
            ReportingDeliveryReceiptKind.Delivered,
            FixedNow,
            "provider-message-1",
            "provider-evidence-1",
            "delivered to recipient");
        var unixSeconds = FixedNow.ToUnixTimeSeconds();
        var unsigned = new ReportingProviderReceiptAuthenticationRequest(
            "http-relay",
            "delivery-1",
            command,
            new ReportingProviderReceiptAuthentication(
                unixSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
                string.Empty));
        var signature = HmacReportingProviderReceiptAuthenticator.CreateSignature(
            secret,
            unsigned,
            unixSeconds);
        var signed = unsigned with
        {
            Authentication = unsigned.Authentication with { Signature = $"sha256={signature}" }
        };

        (await authenticator.AuthenticateAsync(signed)).Should().BeTrue();
        (await authenticator.AuthenticateAsync(signed with
        {
            Receipt = signed.Receipt with { Kind = ReportingDeliveryReceiptKind.Bounced }
        })).Should().BeFalse();

        clock.Advance(TimeSpan.FromMinutes(6));
        (await authenticator.AuthenticateAsync(signed)).Should().BeFalse();
    }

    [Fact]
    public async Task ConfiguredHttpRelay_UsesAuthenticatedIdempotentRequestAndRequiresProviderMessageId()
    {
        var handler = new RecordingHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.Accepted)
        {
            Content = new StringContent(
                "{\"code\":\"accepted\",\"providerMessageId\":\"provider-42\"}",
                Encoding.UTF8,
                "application/json")
        });
        var client = new HttpClient(handler);
        var relay = new ConfiguredReportingHttpRelayClient(
            new SingleHttpClientFactory(client),
            new ReportingHttpRelayClientOptions(
                new Uri("https://relay.example.test/messages"),
                new string('s', 32),
                TimeSpan.FromSeconds(10)));
        var message = new ReportingHttpRelayMessage(
            "tenant-a",
            "package-1",
            "investor@example.test",
            "Released report",
            "Use the secure link.",
            "https://reports.example.test/portal/reporting/access-grants/grant-1/exchange#token=opaque",
            new string('f', 64),
            "delivery-42",
            "/hooks/reporting/distribution/http-relay/deliveries/delivery-42/receipts");

        var result = await relay.SendAsync(message);

        result.IsSuccess.Should().BeTrue();
        result.ProviderMessageId.Should().Be("provider-42");
        handler.Authorization.Should().Be(new AuthenticationHeaderValue("Bearer", new string('s', 32)));
        handler.IdempotencyKey.Should().Be(new string('f', 64));
        handler.Body.Should().Contain("recipientAccessUri").And.Contain("#token=opaque");
        handler.Body.Should().Contain("deliveryJobId").And.Contain("delivery-42");
        handler.Body.Should().Contain("receiptCallbackPath")
            .And.Contain("/hooks/reporting/distribution/http-relay/deliveries/delivery-42/receipts");
    }

    [Theory]
    [InlineData(256, true)]
    [InlineData(257, false)]
    public async Task ConfiguredHttpRelay_EnforcesDurableProviderMessageIdBoundary(
        int providerMessageIdLength,
        bool expectedSuccess)
    {
        var providerMessageId = new string('p', providerMessageIdLength);
        var handler = new RecordingHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.Accepted)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { code = "accepted", providerMessageId }),
                Encoding.UTF8,
                "application/json")
        });
        var relay = new ConfiguredReportingHttpRelayClient(
            new SingleHttpClientFactory(new HttpClient(handler)),
            new ReportingHttpRelayClientOptions(
                new Uri("https://relay.example.test/messages"),
                new string('s', 32),
                TimeSpan.FromSeconds(10)));

        var result = await relay.SendAsync(new ReportingHttpRelayMessage(
            "tenant-a",
            "package-1",
            "investor@example.test",
            "Released report",
            "Use the secure link.",
            "https://reports.example.test/access#token=opaque",
            new string('f', 64),
            "delivery-42",
            "/hooks/reporting/distribution/http-relay/deliveries/delivery-42/receipts"));

        result.IsSuccess.Should().Be(expectedSuccess);
        if (expectedSuccess)
        {
            result.ProviderMessageId.Should().Be(providerMessageId);
        }
        else
        {
            result.ProviderMessageId.Should().BeNull();
            result.Code.Should().Be("PROVIDER_MESSAGE_ID_INVALID");
        }
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

    private static IReportingDeliveryGrantCredentialDeriver CreateCredentialDeriver() =>
        new HmacReportingDeliveryGrantCredentialDeriver(
            Enumerable.Range(1, 32).Select(static value => (byte)value).ToArray());

    private static string ExtractFragmentValue(string accessUri, string key) =>
        new Uri(accessUri).Fragment.TrimStart('#')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(static pair => pair.Split('=', 2))
            .Where(static pair => pair.Length == 2)
            .ToDictionary(
                static pair => Uri.UnescapeDataString(pair[0]),
                static pair => Uri.UnescapeDataString(pair[1]),
                StringComparer.Ordinal)[key];

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
            "run-1",
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

    private sealed class AdvancingTimeProvider(
        DateTimeOffset utcNow,
        TimeSpan increment) : TimeProvider
    {
        private DateTimeOffset _next = utcNow;

        public int CallCount { get; private set; }

        public override DateTimeOffset GetUtcNow()
        {
            var current = _next;
            _next = _next.Add(increment);
            CallCount++;
            return current;
        }
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

        public Task<IReadOnlyList<ReportingAccessGrantRecord>> ListByPackageAsync(
            string tenantId,
            string packageId,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            lock (_gate)
            {
                return Task.FromResult<IReadOnlyList<ReportingAccessGrantRecord>>(_records.Values
                    .Where(grant => string.Equals(grant.TenantId, tenantId, StringComparison.Ordinal)
                        && string.Equals(grant.PackageId, packageId, StringComparison.Ordinal))
                    .OrderByDescending(static grant => grant.CreatedAtUtc)
                    .ThenBy(static grant => grant.GrantId, StringComparer.Ordinal)
                    .ToArray());
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

    private sealed class FailOnceAccessGrantStore(InMemoryAccessGrantStore inner)
        : IReportingAccessGrantStore
    {
        private int _remainingCreateFailures = 1;

        public Task<ReportingAccessGrantRecord?> GetAsync(
            string grantId,
            CancellationToken ct = default) =>
            inner.GetAsync(grantId, ct);

        public Task<IReadOnlyList<ReportingAccessGrantRecord>> ListByPackageAsync(
            string tenantId,
            string packageId,
            CancellationToken ct = default) =>
            inner.ListByPackageAsync(tenantId, packageId, ct);

        public Task<bool> TryCreateAsync(
            ReportingAccessGrantRecord grant,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (Interlocked.Exchange(ref _remainingCreateFailures, 0) == 1)
            {
                throw new TestDbException("Simulated transient grant-store outage.");
            }

            return inner.TryCreateAsync(grant, ct);
        }

        public Task<bool> TryUpdateAsync(
            string grantId,
            long expectedVersion,
            ReportingAccessGrantRecord updatedGrant,
            CancellationToken ct = default) =>
            inner.TryUpdateAsync(grantId, expectedVersion, updatedGrant, ct);
    }

    private sealed class TestDbException(string message) : DbException(message)
    {
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

        public bool FailNextUpdate { get; set; }

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

        public Task<ReportingDeliveryJobRecord?> GetByAccessGrantIdAsync(
            string accessGrantId,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            lock (_gate)
            {
                return Task.FromResult(_jobs.Values.SingleOrDefault(job =>
                    string.Equals(job.AccessGrantId, accessGrantId, StringComparison.Ordinal)));
            }
        }

        public Task<IReadOnlyList<ReportingDeliveryJobRecord>> ListByPackageAsync(
            string tenantId,
            string packageId,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            lock (_gate)
            {
                return Task.FromResult<IReadOnlyList<ReportingDeliveryJobRecord>>(_jobs.Values
                    .Where(job => string.Equals(job.TenantId, tenantId, StringComparison.Ordinal)
                        && string.Equals(job.PackageId, packageId, StringComparison.Ordinal))
                    .OrderByDescending(static job => job.CreatedAtUtc)
                    .ThenBy(static job => job.JobId, StringComparer.Ordinal)
                    .ToArray());
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

                var isInitialAttemptBinding =
                    current.State == ReportingDeliveryState.Dispatching
                    && updatedJob.State == ReportingDeliveryState.Dispatching
                    && current.AccessGrantId is null
                    && updatedJob.AccessGrantId is not null
                    && current.LeaseOwner == updatedJob.LeaseOwner
                    && current.LeaseExpiresAtUtc == updatedJob.LeaseExpiresAtUtc
                    && current.AttemptCount == updatedJob.AttemptCount;
                if (FailNextUpdate && !isInitialAttemptBinding)
                {
                    FailNextUpdate = false;
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

    private sealed class CancellingRelayClient(CancellationTokenSource cancellation) : IReportingHttpRelayClient
    {
        public Task<ReportingHttpRelayResult> SendAsync(
            ReportingHttpRelayMessage message,
            CancellationToken ct = default)
        {
            cancellation.Cancel();
            throw new OperationCanceledException(ct);
        }
    }

    private sealed class IdempotentRelayClient : IReportingHttpRelayClient
    {
        private readonly Dictionary<string, ReportingHttpRelayMessage> _accepted = new(StringComparer.Ordinal);

        public int SendCallCount { get; private set; }

        public int AcceptedMessageCount => _accepted.Count;

        public List<string> ObservedAccessUris { get; } = [];

        public Task<ReportingHttpRelayResult> SendAsync(
            ReportingHttpRelayMessage message,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            SendCallCount++;
            ObservedAccessUris.Add(message.RecipientAccessUri);
            if (_accepted.TryGetValue(message.IdempotencyKey, out var retained))
            {
                retained.Should().Be(message, "an idempotency replay must contain the exact same recipient message");
            }
            else
            {
                _accepted.Add(message.IdempotencyKey, message);
            }

            return Task.FromResult(new ReportingHttpRelayResult(
                IsSuccess: true,
                IsTransientFailure: false,
                Code: "ACCEPTED",
                ProviderMessageId: "provider-message-1"));
        }
    }

    private sealed class AcceptThenTimeoutRelayClient : IReportingHttpRelayClient
    {
        private readonly Dictionary<string, ReportingHttpRelayMessage> _accepted = new(StringComparer.Ordinal);

        public int SendCallCount { get; private set; }

        public int AcceptedMessageCount => _accepted.Count;

        public List<ReportingHttpRelayMessage> Messages { get; } = [];

        public Task<ReportingHttpRelayResult> SendAsync(
            ReportingHttpRelayMessage message,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            SendCallCount++;
            Messages.Add(message);
            if (_accepted.TryGetValue(message.IdempotencyKey, out var retained))
            {
                retained.Should().Be(message, "an ambiguous-outcome retry must replay the exact provider request");
            }
            else
            {
                _accepted.Add(message.IdempotencyKey, message);
            }

            if (SendCallCount == 1)
            {
                throw new TimeoutException("Provider accepted the message but its response was lost.");
            }

            return Task.FromResult(new ReportingHttpRelayResult(
                IsSuccess: true,
                IsTransientFailure: false,
                Code: "ACCEPTED",
                ProviderMessageId: "provider-message-ambiguous-1"));
        }
    }

    private sealed class TransientThenAcceptingRelayClient : IReportingHttpRelayClient
    {
        public List<ReportingHttpRelayMessage> Messages { get; } = [];

        public Task<ReportingHttpRelayResult> SendAsync(
            ReportingHttpRelayMessage message,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            Messages.Add(message);
            return Task.FromResult(Messages.Count == 1
                ? new ReportingHttpRelayResult(
                    IsSuccess: false,
                    IsTransientFailure: true,
                    Code: "HTTP_503",
                    Detail: "relay unavailable")
                : new ReportingHttpRelayResult(
                    IsSuccess: true,
                    IsTransientFailure: false,
                    Code: "ACCEPTED",
                    ProviderMessageId: "provider-message-after-transient"));
        }
    }

    private sealed class SinglePackageArtifactCatalog(ReportingRetainedArtifactPackage package)
        : IReportingArtifactCatalog
    {
        public ValueTask<ReportingArtifactCatalogWriteResult> AddPackageAsync(
            ReportingRetainedArtifactPackage retainedPackage,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ReportingArtifactCatalogWriteResult(AlreadyExisted: true));

        public ValueTask<ReportingRetainedArtifactPackage?> GetPackageAsync(
            string tenantId,
            string packageId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<ReportingRetainedArtifactPackage?>(
                string.Equals(package.PackageId, packageId, StringComparison.Ordinal)
                && package.Artifacts.All(artifact => string.Equals(
                    artifact.Scope.TenantId,
                    tenantId,
                    StringComparison.Ordinal))
                    ? package
                    : null);
        }

        public ValueTask<ReportingRetainedArtifactRecord?> GetArtifactAsync(
            string tenantId,
            string packageId,
            string artifactId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(package.Artifacts.SingleOrDefault(artifact =>
                string.Equals(artifact.Scope.TenantId, tenantId, StringComparison.Ordinal)
                && string.Equals(artifact.PackageId, packageId, StringComparison.Ordinal)
                && string.Equals(artifact.ArtifactId, artifactId, StringComparison.Ordinal)));
        }
    }

    private sealed class MutableArtifactStore(ReportingArtifactReadResult result) : IReportingArtifactStore
    {
        public ReportingArtifactReadResult Result { get; set; } = result;

        public Task<ReportingArtifactWriteResult> StoreAsync(
            ReportingArtifactWriteRequest request,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<ReportingArtifactReadResult> ReadAsync(
            ReportingArtifactIdentity identity,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(Result);
        }
    }

    private sealed class SingleHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class RecordingHttpMessageHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        public AuthenticationHeaderValue? Authorization { get; private set; }
        public string? IdempotencyKey { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Authorization = request.Headers.Authorization;
            IdempotencyKey = request.Headers.TryGetValues("Idempotency-Key", out var values)
                ? values.Single()
                : null;
            Body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return response;
        }
    }

    private sealed class TimeoutHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>(new TaskCanceledException(
                "Simulated HttpClient timeout.",
                new TimeoutException("The configured relay exceeded its timeout.")));
    }

    private sealed class StalledResponseBodyHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Accepted)
            {
                Content = new StreamContent(new CancellationAwareStalledStream())
            });
        }
    }

    private sealed class CancellationAwareStalledStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override async Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }
}
