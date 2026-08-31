using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Meridian.Reporting;
using Meridian.Storage.Reporting;
using Meridian.Ui.Shared.Services;
using Npgsql;
using NpgsqlTypes;
using Xunit;

namespace Meridian.Tests.Storage.Reporting;

[Trait("Category", "Integration")]
public sealed class ReportingDistributionStoreTests :
    IClassFixture<ReportingArtifactDatabaseFixture>,
    IAsyncLifetime
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

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => _database.ResetAsync();

    [ReportingDatabaseFact]
    public async Task ReportingDistributionMigration_RetainsReadIndexAndGrantConsumptionAuthority()
    {
        (await _database.HasMigrationAsync("011_reporting_delivery_run_read_index.sql"))
            .Should().BeTrue();
        (await _database.HasMigrationAsync("012_reporting_access_grant_artifact_consumption.sql"))
            .Should().BeTrue();

        var probe = new PostgresReportingDeploymentProbe(_database.Options).Probe();
        probe.IsReachable.Should().BeTrue();
        probe.HasColumn("reporting_access_grants", "consumed_artifact_ids")
            .Should().BeTrue("the nullable legacy marker must have no database default");
        probe.HasTrigger(
                PostgresReportingDeploymentProbe
                    .AccessGrantArtifactConsumptionTriggerName)
            .Should().BeTrue("insert, update, and delete must share the canonical grant guard");
        (await HasTriggerAsync("trg_reporting_access_grants_guard"))
            .Should().BeFalse(
                "the pre-012 trigger name must remain absent as a reverse-version readiness sentinel");
        probe.HasConstraint(
                "reporting_access_grants.ck_reporting_access_grant_consumed_artifacts")
            .Should().BeTrue();
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
            ConsumedArtifactIds = ["statement.pdf"],
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
    public async Task AccessGrantStore_ConsumedArtifactsUseOrdinalAppendOnlyAuthority()
    {
        var grant = BuildGrant(
            NewTenantId(),
            new string('a', 64),
            ["Z-client-package.pdf", "a-client-package.xlsx"],
            maxUses: 2);
        (await _grantStore.TryCreateAsync(grant)).Should().BeTrue();

        Func<Task> laggingWriterUse = () => ExecuteAsync(
            $"""
            update {QualifiedGrantTable}
            set use_count = 1,
                last_used_at_utc = @used,
                version = 1
            where grant_id = @id;
            """,
            ("used", NpgsqlDbType.TimestampTz, FixedNow.AddMinutes(1).UtcDateTime),
            ("id", NpgsqlDbType.Text, grant.GrantId));
        (await laggingWriterUse.Should().ThrowAsync<PostgresException>())
            .Which.SqlState.Should().Be(
                "55000",
                "a pre-012 writer cannot consume a new tracked grant without its exact artifact transition");

        var first = grant with
        {
            UseCount = 1,
            LastUsedAtUtc = FixedNow.AddMinutes(1),
            ConsumedArtifactIds = ["Z-client-package.pdf"],
            Version = 1
        };
        (await _grantStore.TryUpdateAsync(grant.GrantId, grant.Version, first))
            .Should().BeTrue();
        var second = first with
        {
            UseCount = 2,
            LastUsedAtUtc = FixedNow.AddMinutes(2),
            ConsumedArtifactIds = ["Z-client-package.pdf", "a-client-package.xlsx"],
            Version = 2
        };
        (await _grantStore.TryUpdateAsync(grant.GrantId, first.Version, second))
            .Should().BeTrue();
        (await _grantStore.GetAsync(grant.GrantId)).Should()
            .BeEquivalentTo(second, options => options.WithStrictOrdering());

        Func<Task> removeConsumedArtifact = () => ExecuteAsync(
            $"""
            update {QualifiedGrantTable}
            set consumed_artifact_ids = array['a-client-package.xlsx'],
                use_count = use_count,
                last_used_at_utc = last_used_at_utc,
                version = version + 1
            where grant_id = @id;
            """,
            ("id", NpgsqlDbType.Text, grant.GrantId));
        (await removeConsumedArtifact.Should().ThrowAsync<PostgresException>())
            .Which.SqlState.Should().Be("55000");
    }

    [ReportingDatabaseFact]
    public async Task AccessGrantStore_LegacyTrackingInitializesOnlyWithOneExactArtifact()
    {
        var legacy = BuildGrant(
                NewTenantId(),
                new string('a', 64),
                ["client-package.pdf", "client-package.xlsx"],
                maxUses: 2)
            with
        {
            ConsumedArtifactIds = null
        };
        await InsertRetainedPre012GrantAsync(legacy);

        Func<Task> initializeWithoutExactArtifact = () => ExecuteAsync(
            $"""
            update {QualifiedGrantTable}
            set consumed_artifact_ids = array[]::text[],
                use_count = 1,
                last_used_at_utc = @used,
                version = 1
            where grant_id = @id;
            """,
            ("used", NpgsqlDbType.TimestampTz, FixedNow.AddMinutes(1).UtcDateTime),
            ("id", NpgsqlDbType.Text, legacy.GrantId));
        (await initializeWithoutExactArtifact.Should().ThrowAsync<PostgresException>())
            .Which.SqlState.Should().Be("55000");

        var initialized = legacy with
        {
            UseCount = 1,
            LastUsedAtUtc = FixedNow.AddMinutes(1),
            ConsumedArtifactIds = ["client-package.pdf"],
            Version = 1
        };
        (await _grantStore.TryUpdateAsync(legacy.GrantId, legacy.Version, initialized))
            .Should().BeTrue();
        (await _grantStore.GetAsync(legacy.GrantId)).Should()
            .BeEquivalentTo(initialized, options => options.WithStrictOrdering());
    }

    [ReportingDatabaseFact]
    public async Task AccessGrantStore_Pre012RetainedGrantUseIsFencedAndNewReaderInitializesExactTracking()
    {
        var rawToken = Enumerable.Repeat((byte)0x2c, 32).ToArray();
        var token = Convert.ToHexString(rawToken).ToLowerInvariant();
        var legacy = BuildGrant(
                NewTenantId(),
                Convert.ToHexString(SHA256.HashData(rawToken)).ToLowerInvariant(),
                ["client-package.pdf", "client-package.xlsx"],
                maxUses: 2)
            with
        {
            ConsumedArtifactIds = null
        };
        await InsertRetainedPre012GrantAsync(legacy);

        var inserted = (await _grantStore.GetAsync(legacy.GrantId))!;
        inserted.ConsumedArtifactIds.Should().BeNull(
            "a row retained before migration 012 must remain distinguishable as legacy");
        Func<Task> laggingWriterUse = () => ExecuteAsync(
            $"""
            update {QualifiedGrantTable}
            set use_count = 1,
                last_used_at_utc = @used,
                version = 1
            where grant_id = @id;
            """,
            ("used", NpgsqlDbType.TimestampTz, FixedNow.AddMinutes(1).UtcDateTime),
            ("id", NpgsqlDbType.Text, legacy.GrantId));
        (await laggingWriterUse.Should().ThrowAsync<PostgresException>())
            .Which.SqlState.Should().Be("55000");

        var service = new ReportingAccessGrantService(
            _grantStore,
            new FixedTimeProvider(FixedNow.AddMinutes(2)));
        var package = await service.ValidateAsync(new ReportingAccessGrantValidationRequest(
            legacy.GrantId,
            token,
            legacy.TenantId,
            legacy.Audience,
            legacy.RunId,
            legacy.PackageId,
            ArtifactId: null));
        var exact = await service.ValidateAsync(new ReportingAccessGrantValidationRequest(
            legacy.GrantId,
            token,
            legacy.TenantId,
            legacy.Audience,
            legacy.RunId,
            legacy.PackageId,
            "client-package.pdf"));

        package.Status.Should().Be(ReportingAccessGrantValidationStatus.ArtifactOutOfScope);
        exact.Status.Should().Be(ReportingAccessGrantValidationStatus.Valid);
        var retained = (await _grantStore.GetAsync(legacy.GrantId))!;
        retained.UseCount.Should().Be(1);
        retained.ConsumedArtifactIds.Should().Equal("client-package.pdf");

        var singleLegacy = legacy with
        {
            GrantId = $"grant_{Guid.NewGuid():N}",
            ArtifactIds = ["statement.pdf"]
        };
        await InsertRetainedPre012GrantAsync(singleLegacy);
        var singlePackage = await service.ValidateAsync(
            new ReportingAccessGrantValidationRequest(
                singleLegacy.GrantId,
                token,
                singleLegacy.TenantId,
                singleLegacy.Audience,
                singleLegacy.RunId,
                singleLegacy.PackageId,
                ArtifactId: null));

        singlePackage.Status.Should().Be(ReportingAccessGrantValidationStatus.Valid);
        singlePackage.Grant!.ConsumedArtifactIds.Should().Equal("statement.pdf");
    }

    [ReportingDatabaseFact]
    public async Task AccessGrantStore_Pre012WriterShapedInsertIsRejectedByDatabaseVersionFence()
    {
        var oldWriterGrant = BuildGrant(
                NewTenantId(),
                new string('b', 64),
                ["client-package.pdf"],
                maxUses: 1)
            with
        {
            ConsumedArtifactIds = null
        };

        Func<Task> insert = () => InsertGrantLikeOldWriterAsync(oldWriterGrant);

        var failure = await insert.Should().ThrowAsync<PostgresException>();
        failure.Which.SqlState.Should().Be("55000");
        failure.Which.MessageText.Should().Contain("012-compatible application writer");
        (await _grantStore.GetAsync(oldWriterGrant.GrantId)).Should().BeNull();
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
        (await _grantStore.GetAsync(grant.GrantId)).Should()
            .BeEquivalentTo(revoked, options => options.WithStrictOrdering());

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
    public async Task DistributionStore_AtomicallyConsumesLinkedGrantAndAppendsDownloadedReceipt()
    {
        var job = BuildJob(NewTenantId());
        var grant = BuildGrant(job.TenantId, new string('a', 64), ["statement.pdf"], maxUses: 2) with
        {
            RunId = job.ReleaseAuthorization.RunId,
            PackageId = job.PackageId
        };
        job = BuildLinkedSentJob(job, grant);
        (await _grantStore.TryCreateAsync(grant)).Should().BeTrue();
        (await _deliveryStore.TryCreateAsync(job)).Should().BeTrue();

        var accessedAtUtc = FixedNow.AddMinutes(1);
        const string auditEventId = "artifact-audit-event";
        var receipt = new ReportingDeliveryReceipt(
            ReportingDeliveryDownloadReceiptIdentity.Create(
                job.JobId,
                "statement.pdf",
                auditEventId),
            ReportingDeliveryReceiptKind.Downloaded,
            accessedAtUtc,
            job.TransportId,
            EvidenceReference: auditEventId);
        var consumedGrant = grant with
        {
            UseCount = 1,
            LastUsedAtUtc = accessedAtUtc,
            ConsumedArtifactIds = ["statement.pdf"],
            Version = 1
        };
        var deliveryWithReceipt = job with
        {
            State = ReportingDeliveryState.Delivered,
            UpdatedAtUtc = accessedAtUtc,
            Receipts = [receipt],
            Version = 1
        };

        var status = await _deliveryStore.TryCommitAsync(
            new ReportingDeliveryGrantDownloadCommit(
                "statement.pdf",
                grant.Version,
                consumedGrant,
                job.Version,
                deliveryWithReceipt));

        status.Should().Be(ReportingDeliveryGrantDownloadCommitStatus.Committed);
        (await _grantStore.GetAsync(grant.GrantId)).Should()
            .BeEquivalentTo(consumedGrant, options => options.WithStrictOrdering());
        (await _deliveryStore.GetAsync(job.JobId)).Should()
            .BeEquivalentTo(deliveryWithReceipt, options => options.WithStrictOrdering());
    }

    [ReportingDatabaseFact]
    public async Task DistributionStore_OutOfOrderConcurrentReadPreservesLatestGrantUseTime()
    {
        var job = BuildJob(NewTenantId());
        job = job with
        {
            ReleaseAuthorization = job.ReleaseAuthorization with
            {
                Artifacts =
                [
                    .. job.ReleaseAuthorization.Artifacts,
                    new ReportingReleasedArtifactReference(
                        "statement.xlsx",
                        $"reporting-artifact://{job.TenantId}/{new string('d', 64)}",
                        new string('d', 64),
                        2048)
                ]
            }
        };
        var grant = BuildGrant(
                job.TenantId,
                new string('a', 64),
                ["statement.pdf", "statement.xlsx"],
                maxUses: 2)
            with
        {
            RunId = job.ReleaseAuthorization.RunId,
            PackageId = job.PackageId
        };
        job = BuildLinkedSentJob(job, grant);
        (await _grantStore.TryCreateAsync(grant)).Should().BeTrue();
        (await _deliveryStore.TryCreateAsync(job)).Should().BeTrue();

        var laterAccessedAtUtc = FixedNow.AddMinutes(2);
        const string laterAuditEventId = "later-read-audit-event";
        var laterReceipt = new ReportingDeliveryReceipt(
            ReportingDeliveryDownloadReceiptIdentity.Create(
                job.JobId,
                "statement.pdf",
                laterAuditEventId),
            ReportingDeliveryReceiptKind.Downloaded,
            laterAccessedAtUtc,
            job.TransportId,
            EvidenceReference: laterAuditEventId);
        var firstConsumedGrant = grant with
        {
            UseCount = 1,
            LastUsedAtUtc = laterAccessedAtUtc,
            ConsumedArtifactIds = ["statement.pdf"],
            Version = 1
        };
        var firstDelivery = job with
        {
            State = ReportingDeliveryState.Delivered,
            UpdatedAtUtc = laterAccessedAtUtc,
            Receipts = [laterReceipt],
            Version = 1
        };
        (await _deliveryStore.TryCommitAsync(
            new ReportingDeliveryGrantDownloadCommit(
                "statement.pdf",
                grant.Version,
                firstConsumedGrant,
                job.Version,
                firstDelivery))).Should().Be(
                    ReportingDeliveryGrantDownloadCommitStatus.Committed);

        var earlierAccessedAtUtc = FixedNow.AddMinutes(1);
        const string earlierAuditEventId = "earlier-read-audit-event";
        var earlierReceipt = new ReportingDeliveryReceipt(
            ReportingDeliveryDownloadReceiptIdentity.Create(
                job.JobId,
                "statement.xlsx",
                earlierAuditEventId),
            ReportingDeliveryReceiptKind.Downloaded,
            earlierAccessedAtUtc,
            job.TransportId,
            EvidenceReference: earlierAuditEventId);
        var secondConsumedGrant = firstConsumedGrant with
        {
            UseCount = 2,
            LastUsedAtUtc = laterAccessedAtUtc,
            ConsumedArtifactIds = ["statement.pdf", "statement.xlsx"],
            Version = 2
        };
        var secondDelivery = firstDelivery with
        {
            UpdatedAtUtc = laterAccessedAtUtc,
            Receipts = [laterReceipt, earlierReceipt],
            Version = 2
        };

        var status = await _deliveryStore.TryCommitAsync(
            new ReportingDeliveryGrantDownloadCommit(
                "statement.xlsx",
                firstConsumedGrant.Version,
                secondConsumedGrant,
                firstDelivery.Version,
                secondDelivery));

        status.Should().Be(ReportingDeliveryGrantDownloadCommitStatus.Committed);
        (await _grantStore.GetAsync(grant.GrantId))!.LastUsedAtUtc
            .Should().Be(laterAccessedAtUtc);
        (await _deliveryStore.GetAsync(job.JobId))!.Receipts
            .Select(static receipt => receipt.OccurredAtUtc)
            .Should().Equal(laterAccessedAtUtc, earlierAccessedAtUtc);
    }

    [ReportingDatabaseFact]
    public async Task DeliveryStore_GenericUpdateCannotAppendDownloadedReceipt()
    {
        var job = BuildJob(NewTenantId());
        var grant = BuildGrant(job.TenantId, new string('a', 64), ["statement.pdf"], maxUses: 2) with
        {
            RunId = job.ReleaseAuthorization.RunId,
            PackageId = job.PackageId
        };
        job = BuildLinkedSentJob(job, grant);
        (await _deliveryStore.TryCreateAsync(job)).Should().BeTrue();

        var downloaded = new ReportingDeliveryReceipt(
            ReportingDeliveryDownloadReceiptIdentity.Create(
                job.JobId,
                "statement.pdf",
                "generic-update-audit-event"),
            ReportingDeliveryReceiptKind.Downloaded,
            FixedNow.AddMinutes(1),
            job.TransportId,
            EvidenceReference: "generic-update-audit-event");
        var forgedUpdate = job with
        {
            State = ReportingDeliveryState.Delivered,
            UpdatedAtUtc = downloaded.OccurredAtUtc,
            Receipts = [downloaded],
            Version = 1
        };

        var update = () => _deliveryStore.TryUpdateAsync(
            job.JobId,
            job.Version,
            forgedUpdate);

        await update.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*atomic access-grant consumption boundary*");
        var retained = await _deliveryStore.GetAsync(job.JobId);
        retained!.State.Should().Be(job.State);
        retained.Version.Should().Be(job.Version);
        retained.Receipts.Should().BeEmpty();
    }

    [ReportingDatabaseFact]
    public async Task DistributionStore_DeliveryConflictFailsClosedBeforeConsumingGrant()
    {
        var job = BuildJob(NewTenantId());
        var grant = BuildGrant(job.TenantId, new string('a', 64), ["statement.pdf"], maxUses: 2) with
        {
            RunId = job.ReleaseAuthorization.RunId,
            PackageId = job.PackageId
        };
        job = BuildLinkedSentJob(job, grant);
        (await _grantStore.TryCreateAsync(grant)).Should().BeTrue();
        (await _deliveryStore.TryCreateAsync(job)).Should().BeTrue();

        var providerReceipt = new ReportingDeliveryReceipt(
            $"receipt-{Guid.NewGuid():N}",
            ReportingDeliveryReceiptKind.Accepted,
            FixedNow.AddSeconds(30),
            job.TransportId);
        var concurrentlyUpdated = job with
        {
            UpdatedAtUtc = providerReceipt.OccurredAtUtc,
            Receipts = [providerReceipt],
            Version = 1
        };
        (await _deliveryStore.TryUpdateAsync(job.JobId, job.Version, concurrentlyUpdated))
            .Should().BeTrue();

        var accessedAtUtc = FixedNow.AddMinutes(1);
        const string auditEventId = "conflicted-artifact-audit-event";
        var downloaded = new ReportingDeliveryReceipt(
            ReportingDeliveryDownloadReceiptIdentity.Create(
                job.JobId,
                "statement.pdf",
                auditEventId),
            ReportingDeliveryReceiptKind.Downloaded,
            accessedAtUtc,
            job.TransportId,
            EvidenceReference: auditEventId);
        var staleDeliveryWithReceipt = job with
        {
            State = ReportingDeliveryState.Delivered,
            UpdatedAtUtc = accessedAtUtc,
            Receipts = [downloaded],
            Version = 1
        };
        var consumedGrant = grant with
        {
            UseCount = 1,
            LastUsedAtUtc = accessedAtUtc,
            ConsumedArtifactIds = ["statement.pdf"],
            Version = 1
        };

        var status = await _deliveryStore.TryCommitAsync(
            new ReportingDeliveryGrantDownloadCommit(
                "statement.pdf",
                grant.Version,
                consumedGrant,
                job.Version,
                staleDeliveryWithReceipt));

        status.Should().Be(ReportingDeliveryGrantDownloadCommitStatus.ConcurrencyConflict);
        (await _grantStore.GetAsync(grant.GrantId))!.UseCount.Should().Be(0);
        (await _deliveryStore.GetAsync(job.JobId))!.Receipts.Should().Equal(providerReceipt);
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
        (await _deliveryStore.ListByRunAsync(job.TenantId, job.ReleaseAuthorization.RunId))
            .Should().ContainSingle(item => item.JobId == job.JobId && item.Receipts.Count == 2);
        (await _deliveryStore.ListByRunAsync(job.TenantId, $"other-{job.ReleaseAuthorization.RunId}"))
            .Should().BeEmpty();
        (await _deliveryStore.ListByRunAsync(NewTenantId(), job.ReleaseAuthorization.RunId))
            .Should().BeEmpty();

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
        (await _deliveryStore.GetAsync(job.JobId))!.Should()
            .BeEquivalentTo(failed, options => options.WithStrictOrdering());
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

    private async Task<bool> HasTriggerAsync(string triggerName)
    {
        await using var connection = new NpgsqlConnection(_database.Options.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            select exists (
                select 1
                from pg_catalog.pg_trigger trigger_row
                join pg_catalog.pg_class target_table
                  on target_table.oid = trigger_row.tgrelid
                join pg_catalog.pg_namespace target_schema
                  on target_schema.oid = target_table.relnamespace
                where target_schema.nspname = @schema
                  and target_table.relname = 'reporting_access_grants'
                  and trigger_row.tgname = @trigger_name
                  and not trigger_row.tgisinternal);
            """;
        command.Parameters.AddWithValue("schema", _database.Options.Schema);
        command.Parameters.AddWithValue("trigger_name", triggerName);
        return (bool)(await command.ExecuteScalarAsync() ?? false);
    }

    private async Task InsertGrantLikeOldWriterAsync(ReportingAccessGrantRecord grant)
    {
        await using var connection = new NpgsqlConnection(_database.Options.ConnectionString);
        await connection.OpenAsync();
        await InsertGrantLikeOldWriterAsync(connection, null, grant);
    }

    private async Task InsertRetainedPre012GrantAsync(ReportingAccessGrantRecord grant)
    {
        await using var connection = new NpgsqlConnection(_database.Options.ConnectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        await using (var disableTrigger = connection.CreateCommand())
        {
            disableTrigger.Transaction = transaction;
            disableTrigger.CommandText =
                $"alter table {QualifiedGrantTable} disable trigger "
                + $"{PostgresReportingDeploymentProbe.AccessGrantArtifactConsumptionTriggerName};";
            await disableTrigger.ExecuteNonQueryAsync();
        }

        await InsertGrantLikeOldWriterAsync(connection, transaction, grant);

        await using (var enableTrigger = connection.CreateCommand())
        {
            enableTrigger.Transaction = transaction;
            enableTrigger.CommandText =
                $"alter table {QualifiedGrantTable} enable trigger "
                + $"{PostgresReportingDeploymentProbe.AccessGrantArtifactConsumptionTriggerName};";
            await enableTrigger.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
    }

    private async Task InsertGrantLikeOldWriterAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        ReportingAccessGrantRecord grant)
    {
        grant.ConsumedArtifactIds.Should().BeNull();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            insert into {QualifiedGrantTable} (
                grant_id,
                token_hash_sha256,
                tenant_id,
                audience,
                audience_kind,
                run_id,
                package_id,
                allow_package_read,
                artifact_ids,
                created_at_utc,
                expires_at_utc,
                max_uses,
                use_count,
                version)
            values (
                @grant_id,
                @token_hash_sha256,
                @tenant_id,
                @audience,
                @audience_kind,
                @run_id,
                @package_id,
                @allow_package_read,
                @artifact_ids,
                @created_at_utc,
                @expires_at_utc,
                @max_uses,
                @use_count,
                @version);
            """;
        command.Parameters.AddWithValue("grant_id", NpgsqlDbType.Text, grant.GrantId);
        command.Parameters.AddWithValue(
            "token_hash_sha256",
            NpgsqlDbType.Text,
            grant.TokenHashSha256);
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Text, grant.TenantId);
        command.Parameters.AddWithValue("audience", NpgsqlDbType.Text, grant.Audience);
        command.Parameters.AddWithValue(
            "audience_kind",
            NpgsqlDbType.Integer,
            (int)grant.AudienceKind);
        command.Parameters.AddWithValue("run_id", NpgsqlDbType.Text, grant.RunId);
        command.Parameters.AddWithValue("package_id", NpgsqlDbType.Text, grant.PackageId);
        command.Parameters.AddWithValue(
            "allow_package_read",
            NpgsqlDbType.Boolean,
            grant.AllowPackageRead);
        command.Parameters.AddWithValue(
            "artifact_ids",
            NpgsqlDbType.Array | NpgsqlDbType.Text,
            grant.ArtifactIds.ToArray());
        command.Parameters.AddWithValue(
            "created_at_utc",
            NpgsqlDbType.TimestampTz,
            grant.CreatedAtUtc.UtcDateTime);
        command.Parameters.AddWithValue(
            "expires_at_utc",
            NpgsqlDbType.TimestampTz,
            grant.ExpiresAtUtc.UtcDateTime);
        command.Parameters.AddWithValue("max_uses", NpgsqlDbType.Integer, grant.MaxUses);
        command.Parameters.AddWithValue("use_count", NpgsqlDbType.Integer, grant.UseCount);
        command.Parameters.AddWithValue("version", NpgsqlDbType.Bigint, grant.Version);
        await command.ExecuteNonQueryAsync();
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
            AudienceKind: audienceKind,
            ConsumedArtifactIds: []);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

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

    private static ReportingDeliveryJobRecord BuildLinkedSentJob(
        ReportingDeliveryJobRecord job,
        ReportingAccessGrantRecord grant) =>
        job with
        {
            State = ReportingDeliveryState.Sent,
            AttemptCount = 1,
            UpdatedAtUtc = FixedNow,
            NextAttemptAtUtc = null,
            AccessGrantId = grant.GrantId
        };

    private static string NewTenantId() => $"tenant-{Guid.NewGuid():N}";
}
