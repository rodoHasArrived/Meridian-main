using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Meridian.Reporting;
using Meridian.Storage.Reporting;
using Npgsql;
using NpgsqlTypes;
using Xunit;

namespace Meridian.Tests.Storage.Reporting;

[Trait("Category", "Integration")]
public sealed class ReportingDistributionStoreTests : IClassFixture<ReportingArtifactDatabaseFixture>
{
    private static readonly DateTimeOffset FixedNow = new(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);
    private readonly ReportingArtifactDatabaseFixture _database;
    private readonly PostgresReportingAccessGrantStore _grantStore;
    private readonly PostgresReportingDeliveryStore _deliveryStore;

    public ReportingDistributionStoreTests(ReportingArtifactDatabaseFixture database)
    {
        _database = database;
        _grantStore = new PostgresReportingAccessGrantStore(database.Options);
        _deliveryStore = new PostgresReportingDeliveryStore(database.Options);
    }

    [ReportingDatabaseFact]
    public async Task AccessGrantStore_PersistsOnlyHashAndEnforcesAtomicUseCounter()
    {
        var rawTokenBytes = RandomNumberGenerator.GetBytes(32);
        var rawToken = Convert.ToHexString(rawTokenBytes).ToLowerInvariant();
        var tokenHash = Convert.ToHexString(SHA256.HashData(rawTokenBytes)).ToLowerInvariant();
        var grant = BuildGrant(NewTenantId(), tokenHash, ["statement.pdf"], maxUses: 2);
        (await _grantStore.TryCreateAsync(grant)).Should().BeTrue();

        var retained = (await _grantStore.GetAsync(grant.GrantId))!;
        retained.Should().NotBeNull();
        var rawRow = await QueryGrantRowJsonAsync(grant.GrantId);
        retained.TokenHashSha256.Should().MatchRegex("^[0-9a-f]{64}$");
        retained.TokenHashSha256.Should().NotBe(rawToken);
        rawRow.Should().NotContain(rawToken);

        var first = retained with
        {
            UseCount = 1,
            LastUsedAtUtc = FixedNow.AddMinutes(1),
            Version = 1
        };
        var second = first with { LastUsedAtUtc = FixedNow.AddMinutes(2) };
        var competing = await Task.WhenAll(
            _grantStore.TryUpdateAsync(retained.GrantId, retained.Version, first),
            _grantStore.TryUpdateAsync(retained.GrantId, retained.Version, second));

        competing.Count(static result => result).Should().Be(1);
        var after = (await _grantStore.GetAsync(grant.GrantId))!;
        after.Should().NotBeNull();
        after.UseCount.Should().Be(1);
        after.Version.Should().Be(1);
        (await _grantStore.ListByPackageAsync(grant.TenantId, grant.PackageId))
            .Should().ContainSingle(item => item.GrantId == grant.GrantId && item.RunId == grant.RunId);
        (await _grantStore.ListByPackageAsync(NewTenantId(), grant.PackageId)).Should().BeEmpty();

        Func<Task> skipUseCounter = () => ExecuteAsync(
            $"update {QualifiedGrantTable} set use_count = use_count + 2, last_used_at_utc = @used, version = version + 1 where grant_id = @id;",
            ("used", NpgsqlDbType.TimestampTz, FixedNow.AddMinutes(3).UtcDateTime),
            ("id", NpgsqlDbType.Text, grant.GrantId));
        (await skipUseCounter.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("55000");
    }

    [ReportingDatabaseFact]
    public async Task AccessGrantStore_RoundTripsImmutableTypedAudience()
    {
        foreach (var audienceKind in new[]
                 {
                     ReportingAccessPrincipalKind.Group,
                     ReportingAccessPrincipalKind.Company
                 })
        {
            var grant = BuildGrant(
                NewTenantId(),
                new string('a', 64),
                ["statement.pdf"],
                audienceKind: audienceKind);
            (await _grantStore.TryCreateAsync(grant)).Should().BeTrue();

            var retained = (await _grantStore.GetAsync(grant.GrantId))!;
            retained.Audience.Should().Be(grant.Audience);
            retained.AudienceKind.Should().Be(audienceKind);

            Func<Task> mutateKind = () => ExecuteAsync(
                $"update {QualifiedGrantTable} set audience_kind = @kind, version = version + 1 where grant_id = @id;",
                ("kind", NpgsqlDbType.Integer, ((int)audienceKind + 1) % 3),
                ("id", NpgsqlDbType.Text, grant.GrantId));
            (await mutateKind.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("55000");
        }
    }

    [ReportingDatabaseFact]
    public async Task AccessGrantStore_PersistsOneWayTenantBoundRevocationAndFailsClosedOnCorruption()
    {
        var tenantId = NewTenantId();
        var grant = BuildGrant(tenantId, new string('a', 64), ["statement.pdf"]);
        (await _grantStore.TryCreateAsync(grant)).Should().BeTrue();

        var revoked = grant with
        {
            RevokedAtUtc = FixedNow.AddMinutes(1),
            RevokedBy = "release-officer",
            RevocationReason = "recipient access withdrawn",
            Version = 1
        };
        (await _grantStore.TryUpdateAsync(grant.GrantId, 0, revoked)).Should().BeTrue();
        (await _grantStore.GetAsync(grant.GrantId)).Should().Be(revoked);

        Func<Task> mutateTenant = () => ExecuteAsync(
            $"update {QualifiedGrantTable} set tenant_id = @value where grant_id = @id;",
            ("value", NpgsqlDbType.Text, NewTenantId()),
            ("id", NpgsqlDbType.Text, grant.GrantId));
        (await mutateTenant.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("55000");

        await ExecuteAsync($"alter table {QualifiedGrantTable} disable trigger user;");
        try
        {
            await ExecuteAsync(
                $"update {QualifiedGrantTable} set artifact_ids = array['statement.pdf', 'statement.pdf'] where grant_id = @id;",
                ("id", NpgsqlDbType.Text, grant.GrantId));
        }
        finally
        {
            await ExecuteAsync($"alter table {QualifiedGrantTable} enable trigger user;");
        }

        Func<Task> corruptRead = () => _grantStore.GetAsync(grant.GrantId);
        await corruptRead.Should().ThrowAsync<ReportingDistributionStateCorruptionException>();
    }

    [ReportingDatabaseFact]
    public async Task DeliveryStore_EnforcesIdempotencyAndAtomicSkipLockedLeases()
    {
        var job = BuildJob(NewTenantId());
        var duplicate = job with { JobId = $"delivery-{Guid.NewGuid():N}" };

        (await _deliveryStore.TryCreateAsync(job)).Should().BeTrue();
        (await _deliveryStore.TryCreateAsync(duplicate)).Should().BeFalse();
        (await _deliveryStore.GetByIdempotencyKeyAsync(job.IdempotencyKey))!.JobId.Should().Be(job.JobId);

        var claims = await Task.WhenAll(
            _deliveryStore.ClaimDueAsync(FixedNow, "worker-a", TimeSpan.FromMinutes(1), 10),
            _deliveryStore.ClaimDueAsync(FixedNow, "worker-b", TimeSpan.FromMinutes(1), 10));

        claims.Sum(static result => result.Count).Should().Be(1);
        var claimed = claims.SelectMany(static result => result).Single();
        claimed.State.Should().Be(ReportingDeliveryState.Dispatching);
        claimed.Version.Should().Be(1);
        claimed.LeaseOwner.Should().BeOneOf("worker-a", "worker-b");

        (await _deliveryStore.ClaimDueAsync(
            FixedNow.AddSeconds(59),
            "worker-c",
            TimeSpan.FromMinutes(1),
            10)).Should().BeEmpty();
        var reclaimed = (await _deliveryStore.ClaimDueAsync(
            FixedNow.AddMinutes(1),
            "worker-c",
            TimeSpan.FromMinutes(1),
            10)).Should().ContainSingle().Subject;
        reclaimed.JobId.Should().Be(job.JobId);
        reclaimed.LeaseOwner.Should().Be("worker-c");
        reclaimed.Version.Should().Be(2);
    }

    [ReportingDatabaseFact]
    public async Task DeliveryStore_PersistsRetryStateAndAppendOnlyReceiptsAtomically()
    {
        var job = BuildJob(NewTenantId(), maxAttempts: 3);
        (await _deliveryStore.TryCreateAsync(job)).Should().BeTrue();
        var firstClaim = (await _deliveryStore.ClaimDueAsync(
            FixedNow,
            "worker-a",
            TimeSpan.FromMinutes(1),
            1)).Single();

        var retry = firstClaim with
        {
            State = ReportingDeliveryState.RetryScheduled,
            AttemptCount = 1,
            UpdatedAtUtc = FixedNow.AddSeconds(1),
            NextAttemptAtUtc = FixedNow.AddSeconds(31),
            LeaseOwner = null,
            LeaseExpiresAtUtc = null,
            LastErrorCode = "HTTP_503",
            LastError = "relay unavailable",
            Version = firstClaim.Version + 1
        };
        (await _deliveryStore.TryUpdateAsync(job.JobId, firstClaim.Version, retry)).Should().BeTrue();
        (await _deliveryStore.ClaimDueAsync(
            FixedNow.AddSeconds(30),
            "worker-b",
            TimeSpan.FromMinutes(1),
            1)).Should().BeEmpty();

        var retryClaim = (await _deliveryStore.ClaimDueAsync(
            FixedNow.AddSeconds(31),
            "worker-b",
            TimeSpan.FromMinutes(1),
            1)).Single();
        var published = new ReportingDeliveryReceipt(
            $"receipt-{Guid.NewGuid():N}",
            ReportingDeliveryReceiptKind.Published,
            FixedNow.AddSeconds(32),
            job.TransportId,
            "provider-message-1",
            "relay-evidence:1");
        var sent = retryClaim with
        {
            State = ReportingDeliveryState.Sent,
            AttemptCount = 2,
            UpdatedAtUtc = FixedNow.AddSeconds(32),
            LeaseOwner = null,
            LeaseExpiresAtUtc = null,
            LastErrorCode = null,
            LastError = null,
            ProviderMessageId = "provider-message-1",
            Receipts = [published],
            Version = retryClaim.Version + 1
        };
        (await _deliveryStore.TryUpdateAsync(job.JobId, retryClaim.Version, sent)).Should().BeTrue();

        var accessed = new ReportingDeliveryReceipt(
            $"receipt-{Guid.NewGuid():N}",
            ReportingDeliveryReceiptKind.Accessed,
            FixedNow.AddSeconds(33),
            job.TransportId,
            "provider-message-1",
            "portal-access:1");
        var delivered = sent with
        {
            State = ReportingDeliveryState.Delivered,
            UpdatedAtUtc = FixedNow.AddSeconds(33),
            Receipts = [published, accessed],
            Version = sent.Version + 1
        };
        (await _deliveryStore.TryUpdateAsync(job.JobId, sent.Version, delivered)).Should().BeTrue();

        var retained = (await _deliveryStore.GetAsync(job.JobId))!;
        retained.Should().NotBeNull();
        retained.State.Should().Be(ReportingDeliveryState.Delivered);
        retained.AttemptCount.Should().Be(2);
        retained.Receipts.Should().Equal(published, accessed);
        (await _deliveryStore.ListByPackageAsync(job.TenantId, job.PackageId))
            .Should().ContainSingle(item => item.JobId == job.JobId && item.Receipts.Count == 2);
        (await _deliveryStore.ListByPackageAsync(NewTenantId(), job.PackageId)).Should().BeEmpty();

        Func<Task> mutateReceipt = () => ExecuteAsync(
            $"update {QualifiedReceiptTable} set detail = 'tampered' where job_id = @job_id and receipt_id = @receipt_id;",
            ("job_id", NpgsqlDbType.Text, job.JobId),
            ("receipt_id", NpgsqlDbType.Text, published.ReceiptId));
        (await mutateReceipt.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("55000");

        Func<Task> crossTenantReceipt = () => ExecuteAsync(
            $"""
            insert into {QualifiedReceiptTable} (
                job_id, tenant_id, receipt_id, kind, occurred_at_utc, transport_id)
            values (@job_id, @tenant_id, @receipt_id, @kind, @occurred_at_utc, @transport_id);
            """,
            ("job_id", NpgsqlDbType.Text, job.JobId),
            ("tenant_id", NpgsqlDbType.Text, NewTenantId()),
            ("receipt_id", NpgsqlDbType.Text, $"receipt-{Guid.NewGuid():N}"),
            ("kind", NpgsqlDbType.Integer, (int)ReportingDeliveryReceiptKind.Accepted),
            ("occurred_at_utc", NpgsqlDbType.TimestampTz, FixedNow.UtcDateTime),
            ("transport_id", NpgsqlDbType.Text, job.TransportId));
        (await crossTenantReceipt.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("23503");

        Func<Task> moveDeliveredBackToSent = () => ExecuteAsync(
            $"update {QualifiedJobTable} set state = @state, version = version + 1 where job_id = @job_id;",
            ("state", NpgsqlDbType.Integer, (int)ReportingDeliveryState.Sent),
            ("job_id", NpgsqlDbType.Text, job.JobId));
        (await moveDeliveredBackToSent.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("55000");

        Func<Task> clearProviderIdentity = () => ExecuteAsync(
            $"update {QualifiedJobTable} set provider_message_id = null, version = version + 1 where job_id = @job_id;",
            ("job_id", NpgsqlDbType.Text, job.JobId));
        (await clearProviderIdentity.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("55000");
    }

    [ReportingDatabaseFact]
    public async Task DeliveryStore_ProviderReceiptResolvesOnlyBoundUnknownRetryOutcome()
    {
        var job = BuildJob(NewTenantId(), maxAttempts: 3);
        var linkedGrant = BuildGrant(job.TenantId, new string('b', 64), ["statement.pdf"]) with
        {
            RunId = job.ReleaseAuthorization.RunId,
            PackageId = job.PackageId
        };
        (await _grantStore.TryCreateAsync(linkedGrant)).Should().BeTrue();
        (await _deliveryStore.TryCreateAsync(job)).Should().BeTrue();
        var claimed = (await _deliveryStore.ClaimDueAsync(
            FixedNow,
            "worker-a",
            TimeSpan.FromMinutes(1),
            1)).Single();
        var bound = claimed with
        {
            AccessGrantId = linkedGrant.GrantId,
            Version = claimed.Version + 1
        };
        (await _deliveryStore.TryUpdateAsync(job.JobId, claimed.Version, bound)).Should().BeTrue();
        var retry = bound with
        {
            State = ReportingDeliveryState.RetryScheduled,
            AttemptCount = 1,
            UpdatedAtUtc = FixedNow.AddSeconds(1),
            NextAttemptAtUtc = FixedNow.AddMinutes(1),
            LeaseOwner = null,
            LeaseExpiresAtUtc = null,
            LastErrorCode = "RELAY_OUTCOME_UNKNOWN",
            LastError = "provider acceptance was not observed",
            Version = bound.Version + 1
        };
        (await _deliveryStore.TryUpdateAsync(job.JobId, bound.Version, retry)).Should().BeTrue();

        var receipt = new ReportingDeliveryReceipt(
            $"receipt-{Guid.NewGuid():N}",
            ReportingDeliveryReceiptKind.Rejected,
            FixedNow.AddSeconds(2),
            job.TransportId,
            "provider-message-unknown-recovered");
        var failed = retry with
        {
            State = ReportingDeliveryState.Failed,
            UpdatedAtUtc = FixedNow.AddSeconds(2),
            NextAttemptAtUtc = null,
            LastErrorCode = "REJECTED",
            LastError = "provider rejected notification",
            ProviderMessageId = receipt.ProviderReference,
            Receipts = [receipt],
            Version = retry.Version + 1
        };

        (await _deliveryStore.TryUpdateAsync(job.JobId, retry.Version, failed)).Should().BeTrue();
        (await _deliveryStore.GetAsync(job.JobId))!.Should().Be(failed);
        (await _deliveryStore.ListPendingAccessGrantRevocationsAsync(10))
            .Should().ContainSingle(candidate =>
                candidate.JobId == job.JobId
                && candidate.AccessGrantId == linkedGrant.GrantId);
        var revokedGrant = linkedGrant with
        {
            RevokedAtUtc = FixedNow.AddSeconds(3),
            RevokedBy = "provider-receipt-reconciler",
            RevocationReason = "provider rejected notification",
            Version = 1
        };
        (await _grantStore.TryUpdateAsync(linkedGrant.GrantId, 0, revokedGrant)).Should().BeTrue();
        (await _deliveryStore.ListPendingAccessGrantRevocationsAsync(10)).Should().BeEmpty();

        var ordinaryJob = BuildJob(NewTenantId(), maxAttempts: 3);
        (await _deliveryStore.TryCreateAsync(ordinaryJob)).Should().BeTrue();
        var ordinaryClaim = (await _deliveryStore.ClaimDueAsync(
            FixedNow,
            "worker-b",
            TimeSpan.FromMinutes(1),
            1)).Single();
        var ordinaryRetry = ordinaryClaim with
        {
            State = ReportingDeliveryState.RetryScheduled,
            AttemptCount = 1,
            UpdatedAtUtc = FixedNow.AddSeconds(1),
            NextAttemptAtUtc = FixedNow.AddMinutes(1),
            LeaseOwner = null,
            LeaseExpiresAtUtc = null,
            LastErrorCode = "HTTP_503",
            LastError = "relay unavailable",
            Version = ordinaryClaim.Version + 1
        };
        (await _deliveryStore.TryUpdateAsync(
            ordinaryJob.JobId,
            ordinaryClaim.Version,
            ordinaryRetry)).Should().BeTrue();
        var invalidReceipt = receipt with { ReceiptId = $"receipt-{Guid.NewGuid():N}" };
        var invalidResolution = ordinaryRetry with
        {
            State = ReportingDeliveryState.Delivered,
            UpdatedAtUtc = FixedNow.AddSeconds(2),
            NextAttemptAtUtc = null,
            ProviderMessageId = invalidReceipt.ProviderReference,
            Receipts = [invalidReceipt],
            Version = ordinaryRetry.Version + 1
        };
        var resolveOrdinaryRetry = () => _deliveryStore.TryUpdateAsync(
            ordinaryJob.JobId,
            ordinaryRetry.Version,
            invalidResolution);
        await resolveOrdinaryRetry.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*unknown provider outcome*");
    }

    [ReportingDatabaseFact]
    public async Task DeliveryStore_RevocationCandidatesIncludeLateProviderFailureButExcludeRecoveredUnknownOutcome()
    {
        foreach (var lateFailureKind in new[]
                 {
                     ReportingDeliveryReceiptKind.Bounced,
                     ReportingDeliveryReceiptKind.Rejected
                 })
        {
            var tenantId = NewTenantId();
            var lateBounceJob = BuildJob(tenantId, maxAttempts: 3);
            var lateBounceGrant = BuildGrant(tenantId, new string('c', 64), ["statement.pdf"]) with
            {
                RunId = lateBounceJob.ReleaseAuthorization.RunId,
                PackageId = lateBounceJob.PackageId
            };
            (await _grantStore.TryCreateAsync(lateBounceGrant)).Should().BeTrue();
            (await _deliveryStore.TryCreateAsync(lateBounceJob)).Should().BeTrue();
            var lateBounceClaim = (await _deliveryStore.ClaimDueAsync(
                FixedNow,
                "worker-late-bounce",
                TimeSpan.FromMinutes(1),
                1)).Single();
            var lateBounceBound = lateBounceClaim with
            {
                AccessGrantId = lateBounceGrant.GrantId,
                Version = lateBounceClaim.Version + 1
            };
            (await _deliveryStore.TryUpdateAsync(
                lateBounceJob.JobId,
                lateBounceClaim.Version,
                lateBounceBound)).Should().BeTrue();
            const string lateProviderMessageId = "provider-late-bounce";
            var published = new ReportingDeliveryReceipt(
                $"receipt-{Guid.NewGuid():N}",
                ReportingDeliveryReceiptKind.Published,
                FixedNow.AddSeconds(1),
                lateBounceJob.TransportId,
                lateProviderMessageId,
                "relay-evidence-late-bounce");
            var sent = lateBounceBound with
            {
                State = ReportingDeliveryState.Sent,
                AttemptCount = 1,
                UpdatedAtUtc = FixedNow.AddSeconds(1),
                NextAttemptAtUtc = null,
                LeaseOwner = null,
                LeaseExpiresAtUtc = null,
                ProviderMessageId = lateProviderMessageId,
                Receipts = [published],
                Version = lateBounceBound.Version + 1
            };
            (await _deliveryStore.TryUpdateAsync(
                lateBounceJob.JobId,
                lateBounceBound.Version,
                sent)).Should().BeTrue();
            var deliveredReceipt = new ReportingDeliveryReceipt(
                $"receipt-{Guid.NewGuid():N}",
                ReportingDeliveryReceiptKind.Delivered,
                FixedNow.AddSeconds(2),
                lateBounceJob.TransportId,
                lateProviderMessageId,
                "provider-delivered-evidence");
            var delivered = sent with
            {
                State = ReportingDeliveryState.Delivered,
                UpdatedAtUtc = FixedNow.AddSeconds(2),
                Receipts = [published, deliveredReceipt],
                Version = sent.Version + 1
            };
            (await _deliveryStore.TryUpdateAsync(
                lateBounceJob.JobId,
                sent.Version,
                delivered)).Should().BeTrue();
            var bounceReceipt = new ReportingDeliveryReceipt(
                $"receipt-{Guid.NewGuid():N}",
                lateFailureKind,
                FixedNow.AddSeconds(3),
                lateBounceJob.TransportId,
                lateProviderMessageId,
                "provider-bounced-evidence",
                "mailbox later rejected the notification");
            var lateBounced = delivered with
            {
                UpdatedAtUtc = FixedNow.AddSeconds(3),
                LastErrorCode = lateFailureKind.ToString().ToUpperInvariant(),
                LastError = "mailbox later rejected the notification",
                Receipts = [published, deliveredReceipt, bounceReceipt],
                Version = delivered.Version + 1
            };
            (await _deliveryStore.TryUpdateAsync(
                lateBounceJob.JobId,
                delivered.Version,
                lateBounced)).Should().BeTrue();

            (await _deliveryStore.ListPendingAccessGrantRevocationsAsync(10))
                .Should().ContainSingle(candidate =>
                    candidate.JobId == lateBounceJob.JobId
                    && candidate.AccessGrantId == lateBounceGrant.GrantId);
            var revokedLateBounceGrant = lateBounceGrant with
            {
                RevokedAtUtc = FixedNow.AddSeconds(4),
                RevokedBy = "provider-receipt-reconciler",
                RevocationReason = "late provider bounce",
                Version = 1
            };
            (await _grantStore.TryUpdateAsync(
                lateBounceGrant.GrantId,
                0,
                revokedLateBounceGrant)).Should().BeTrue();

            var recoveredJob = BuildJob(tenantId, maxAttempts: 3);
            var recoveredGrant = BuildGrant(tenantId, new string('d', 64), ["statement.pdf"]) with
            {
                RunId = recoveredJob.ReleaseAuthorization.RunId,
                PackageId = recoveredJob.PackageId
            };
            (await _grantStore.TryCreateAsync(recoveredGrant)).Should().BeTrue();
            (await _deliveryStore.TryCreateAsync(recoveredJob)).Should().BeTrue();
            var recoveredClaim = (await _deliveryStore.ClaimDueAsync(
                FixedNow,
                "worker-recovered",
                TimeSpan.FromMinutes(1),
                1)).Single();
            var recoveredBound = recoveredClaim with
            {
                AccessGrantId = recoveredGrant.GrantId,
                Version = recoveredClaim.Version + 1
            };
            (await _deliveryStore.TryUpdateAsync(
                recoveredJob.JobId,
                recoveredClaim.Version,
                recoveredBound)).Should().BeTrue();
            var transientFailure = new ReportingDeliveryReceipt(
                $"receipt-{Guid.NewGuid():N}",
                ReportingDeliveryReceiptKind.Failed,
                FixedNow.AddSeconds(1),
                recoveredJob.TransportId,
                ProviderReference: null,
                EvidenceReference: "relay-evidence-unknown",
                Detail: "RELAY_OUTCOME_UNKNOWN: provider acceptance was not yet observable");
            var retry = recoveredBound with
            {
                State = ReportingDeliveryState.RetryScheduled,
                AttemptCount = 1,
                UpdatedAtUtc = FixedNow.AddSeconds(1),
                NextAttemptAtUtc = FixedNow.AddMinutes(1),
                LeaseOwner = null,
                LeaseExpiresAtUtc = null,
                LastErrorCode = "RELAY_OUTCOME_UNKNOWN",
                LastError = "provider acceptance was not yet observable",
                Receipts = [transientFailure],
                Version = recoveredBound.Version + 1
            };
            (await _deliveryStore.TryUpdateAsync(
                recoveredJob.JobId,
                recoveredBound.Version,
                retry)).Should().BeTrue();
            var recoveredReceipt = new ReportingDeliveryReceipt(
                $"receipt-{Guid.NewGuid():N}",
                ReportingDeliveryReceiptKind.Delivered,
                FixedNow.AddSeconds(2),
                recoveredJob.TransportId,
                "provider-recovered",
                "provider-recovered-evidence");
            var recovered = retry with
            {
                State = ReportingDeliveryState.Delivered,
                UpdatedAtUtc = FixedNow.AddSeconds(2),
                NextAttemptAtUtc = null,
                LastErrorCode = null,
                LastError = null,
                ProviderMessageId = "provider-recovered",
                Receipts = [transientFailure, recoveredReceipt],
                Version = retry.Version + 1
            };
            (await _deliveryStore.TryUpdateAsync(
                recoveredJob.JobId,
                retry.Version,
                recovered)).Should().BeTrue();

            (await _deliveryStore.ListPendingAccessGrantRevocationsAsync(10)).Should().BeEmpty(
                "a retained unknown-outcome attempt is diagnostic evidence after later provider success, not revocation evidence");
            (await _grantStore.GetAsync(recoveredGrant.GrantId))!.RevokedAtUtc.Should().BeNull();
        }
    }

    [ReportingDatabaseFact]
    public async Task DeliveryStore_RejectsTokenBearingPayloadAndFailsClosedOnCorruptAuthorization()
    {
        var tokenBearing = BuildJob(NewTenantId()) with
        {
            Payload = BuildJob(NewTenantId()).Payload with
            {
                PortalUri = "/portal/reporting/package#token=plaintext-secret"
            }
        };
        Func<Task> retainToken = () => _deliveryStore.TryCreateAsync(tokenBearing);
        await retainToken.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*token*");

        var job = BuildJob(NewTenantId());
        (await _deliveryStore.TryCreateAsync(job)).Should().BeTrue();
        await ExecuteAsync($"alter table {QualifiedJobTable} disable trigger user;");
        try
        {
            await ExecuteAsync(
                $"update {QualifiedJobTable} set release_authorization = '{{}}'::jsonb where job_id = @job_id;",
                ("job_id", NpgsqlDbType.Text, job.JobId));
        }
        finally
        {
            await ExecuteAsync($"alter table {QualifiedJobTable} enable trigger user;");
        }

        Func<Task> corruptRead = () => _deliveryStore.GetAsync(job.JobId);
        await corruptRead.Should().ThrowAsync<ReportingDistributionStateCorruptionException>();
    }

    private string QualifiedGrantTable =>
        $"\"{_database.Options.Schema}\".\"reporting_access_grants\"";

    private string QualifiedJobTable =>
        $"\"{_database.Options.Schema}\".\"reporting_delivery_jobs\"";

    private string QualifiedReceiptTable =>
        $"\"{_database.Options.Schema}\".\"reporting_delivery_receipts\"";

    private async Task<string> QueryGrantRowJsonAsync(string grantId)
    {
        await using var connection = new NpgsqlConnection(_database.Options.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"select row_to_json(retained)::text from {QualifiedGrantTable} as retained where grant_id = @grant_id;";
        command.Parameters.AddWithValue("grant_id", NpgsqlDbType.Text, grantId);
        return (string)(await command.ExecuteScalarAsync() ?? string.Empty);
    }

    private async Task ExecuteAsync(
        string sql,
        params (string Name, NpgsqlDbType Type, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(_database.Options.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var parameter in parameters)
        {
            command.Parameters.AddWithValue(parameter.Name, parameter.Type, parameter.Value);
        }

        await command.ExecuteNonQueryAsync();
    }

    private static ReportingAccessGrantRecord BuildGrant(
        string tenantId,
        string tokenHash,
        IReadOnlyList<string>? artifactIds = null,
        int maxUses = 3,
        ReportingAccessPrincipalKind audienceKind = ReportingAccessPrincipalKind.User) =>
        new(
            $"grant_{Guid.NewGuid():N}",
            tokenHash,
            tenantId,
            "investor-42",
            $"run-{Guid.NewGuid():N}",
            $"package-{Guid.NewGuid():N}",
            AllowPackageRead: true,
            artifactIds ?? [],
            FixedNow,
            FixedNow.AddHours(1),
            MaxUses: maxUses,
            UseCount: 0,
            AudienceKind: audienceKind);

    private static ReportingDeliveryJobRecord BuildJob(string tenantId, int maxAttempts = 3)
    {
        var packageId = $"package-{Guid.NewGuid():N}";
        var release = new ReportingDeliveryReleaseAuthorization(
            $"release-{Guid.NewGuid():N}",
            ReportingReleaseState.Released,
            tenantId,
            packageId,
            $"run-{Guid.NewGuid():N}",
            "release-v1",
            new string('b', 64),
            [new ReportingReleasedArtifactReference(
                "statement.pdf",
                $"reporting-artifact://{tenantId}/{new string('c', 64)}",
                new string('c', 64),
                1024)],
            ["release-evidence:1"],
            FixedNow,
            "release-officer",
            "signed-release-proof");
        var payload = new ReportingDeliveryPayload(
            "Investor 42",
            "Limited partner",
            "investor42@example.test",
            "Your report is ready",
            "Use the secure portal to review the released package.",
            $"/portal/reporting/packages/{packageId}",
            new ReportingDeliveryAccessPolicy(
                "investor-42",
                "https://reports.example.test/portal/reporting/access",
                TimeSpan.FromHours(1),
                ArtifactIds: ["statement.pdf"]));
        var canonicalIdempotencyScope = string.Join(
            "\u001f",
            tenantId,
            packageId,
            "investor-relations",
            "http-relay",
            release.ReleaseVersion,
            release.ArtifactManifestHashSha256);
        var idempotency = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(canonicalIdempotencyScope)))
            .ToLowerInvariant();
        return new ReportingDeliveryJobRecord(
            $"delivery-{Guid.NewGuid():N}",
            tenantId,
            packageId,
            "investor-relations",
            "http-relay",
            release,
            "controller",
            idempotency,
            payload,
            ReportingDeliveryState.Queued,
            AttemptCount: 0,
            maxAttempts,
            FixedNow,
            FixedNow,
            FixedNow,
            LeaseOwner: null,
            LeaseExpiresAtUtc: null,
            LastErrorCode: null,
            LastError: null,
            ProviderMessageId: null,
            AccessGrantId: null,
            Receipts: []);
    }

    private static string NewTenantId() => $"tenant-{Guid.NewGuid():N}";
}
