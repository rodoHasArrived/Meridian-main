using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Meridian.Reporting;
using Meridian.Ui.Shared.Services;
using Xunit;

namespace Meridian.Tests.Ui;

public sealed class ReportingArtifactVaultServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Retain_and_download_store_exact_bytes_once_and_append_verified_audit()
    {
        var blobStore = new InMemoryArtifactStore(Now);
        var catalog = new InMemoryCatalog();
        var audit = new HashChainedAuditStore();
        var service = CreateService(blobStore, catalog, audit);
        var bytes = new byte[] { 0, 1, 2, 3, 127, 128, 254, 255 };
        var request = CreateRequest(bytes);

        var first = await service.RetainPackageAsync(request, CreateAuthority());
        var retry = await service.RetainPackageAsync(request, CreateAuthority());
        var download = await service.ReadForDownloadAsync("package-1", "statement.pdf", CreateAccessContext());

        blobStore.RetainedBlobCount.Should().Be(1, "content-addressed bytes are stored once per tenant");
        first.CatalogAlreadyExisted.Should().BeFalse();
        retry.CatalogAlreadyExisted.Should().BeTrue();
        first.Package.Artifacts.Should().ContainSingle();
        first.Package.Artifacts[0].Identity.ContentHashSha256.Should().Be(Sha256(bytes));
        first.Package.Artifacts[0].ByteLength.Should().Be(bytes.LongLength);
        download.Content.Should().Equal(bytes);
        download.Artifact.Identity.Should().Be(first.Package.Artifacts[0].Identity);
        audit.Events.Select(static item => item.Action).Should().Equal(
            ReportingArtifactAuditAction.ArtifactRetained,
            ReportingArtifactAuditAction.RetentionVerified,
            ReportingArtifactAuditAction.ContentAccessed);
        audit.Receipts.Should().OnlyContain(static receipt => receipt.Hash.Length == 64);
    }

    [Fact]
    public async Task Retention_snapshots_every_caller_buffer_before_the_first_store_await()
    {
        var blobStore = new InMemoryArtifactStore(Now) { BlockFirstStore = true };
        var service = CreateService(blobStore, new InMemoryCatalog(), new HashChainedAuditStore());
        var manifestBytes = new byte[] { 1, 2, 3 };
        var scheduleBytes = new byte[] { 4, 5, 6 };
        var expectedSchedule = scheduleBytes.ToArray();
        var request = CreateRequest(manifestBytes) with
        {
            Artifacts =
            [
                new ReportingRenderedArtifact(
                    "statement.pdf",
                    "statement.pdf",
                    "application/pdf",
                    manifestBytes),
                new ReportingRenderedArtifact(
                    "schedule.csv",
                    "schedule.csv",
                    "text/csv",
                    scheduleBytes)
            ]
        };

        var retention = service.RetainPackageAsync(request, CreateAuthority());
        await blobStore.FirstStoreEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        scheduleBytes[0] = 99;
        blobStore.ReleaseFirstStore.TrySetResult(true);

        await retention;
        var download = await service.ReadForDownloadAsync(
            "package-1",
            "schedule.csv",
            CreateAccessContext());
        download.Content.Should().Equal(expectedSchedule);
    }

    [Fact]
    public async Task Cross_tenant_download_is_denied_before_blob_read_and_is_audited()
    {
        var blobStore = new InMemoryArtifactStore(Now);
        var catalog = new InMemoryCatalog();
        var audit = new HashChainedAuditStore();
        var service = CreateService(blobStore, catalog, audit);
        await service.RetainPackageAsync(CreateRequest([1, 2, 3]), CreateAuthority());
        var retentionReadCalls = blobStore.ReadCalls;
        var crossTenant = CreateAccessContext() with
        {
            TenantId = "tenant-b",
            OrganizationId = "organization-b",
            CompanyId = "company-b"
        };

        var action = () => service.ReadForDownloadAsync("package-1", "statement.pdf", crossTenant);

        await action.Should().ThrowAsync<ReportingArtifactVaultAccessDeniedException>();
        blobStore.ReadCalls.Should().Be(retentionReadCalls, "denied access must not read artifact bytes");
        audit.Events[^1].Action.Should().Be(ReportingArtifactAuditAction.AccessDenied);
        audit.Events[^1].ActorTenantId.Should().Be("tenant-b");
        audit.Events[^1].TargetTenantId.Should().Be("tenant-b");
    }

    [Fact]
    public async Task Operational_scope_mismatch_is_denied_before_blob_read()
    {
        var blobStore = new InMemoryArtifactStore(Now);
        var catalog = new InMemoryCatalog();
        var audit = new HashChainedAuditStore();
        var service = CreateService(blobStore, catalog, audit);
        await service.RetainPackageAsync(CreateRequest([1, 2, 3]), CreateAuthority());
        var retentionReadCalls = blobStore.ReadCalls;

        var action = () => service.ReadForDownloadAsync(
            "package-1",
            "statement.pdf",
            CreateAccessContext() with { FundId = "another-fund" });

        await action.Should().ThrowAsync<ReportingArtifactVaultAccessDeniedException>();
        blobStore.ReadCalls.Should().Be(retentionReadCalls, "denied access must not read artifact bytes");
        audit.Events[^1].Action.Should().Be(ReportingArtifactAuditAction.AccessDenied);
        audit.Events[^1].Reason.Should().Contain("operational scope");
    }

    [Fact]
    public async Task Corrupt_retrieved_bytes_fail_closed_and_append_integrity_event()
    {
        var blobStore = new InMemoryArtifactStore(Now);
        var catalog = new InMemoryCatalog();
        var audit = new HashChainedAuditStore();
        var service = CreateService(blobStore, catalog, audit);
        await service.RetainPackageAsync(CreateRequest([1, 2, 3]), CreateAuthority());
        blobStore.CorruptReads = true;

        var action = () => service.ReadForDownloadAsync("package-1", "statement.pdf", CreateAccessContext());

        await action.Should().ThrowAsync<ReportingArtifactIntegrityException>();
        audit.Events[^1].Action.Should().Be(ReportingArtifactAuditAction.IntegrityFailure);
        audit.Events.Should().NotContain(static item => item.Action == ReportingArtifactAuditAction.ContentAccessed);
    }

    [Fact]
    public async Task Download_fails_closed_when_access_audit_cannot_be_persisted()
    {
        var blobStore = new InMemoryArtifactStore(Now);
        var catalog = new InMemoryCatalog();
        var audit = new HashChainedAuditStore();
        var service = CreateService(blobStore, catalog, audit);
        await service.RetainPackageAsync(CreateRequest([1, 2, 3]), CreateAuthority());
        var retentionReadCalls = blobStore.ReadCalls;
        audit.FailAppends = true;

        var action = () => service.ReadForDownloadAsync("package-1", "statement.pdf", CreateAccessContext());

        await action.Should().ThrowAsync<IOException>().WithMessage("audit unavailable");
        blobStore.ReadCalls.Should().Be(retentionReadCalls + 1, "the requested bytes are verified before the access audit fails closed");
        audit.Events.Should().NotContain(static item => item.Action == ReportingArtifactAuditAction.ContentAccessed);
    }

    [Fact]
    public async Task Retention_requires_server_authority_bound_to_run_tenant()
    {
        var blobStore = new InMemoryArtifactStore(Now);
        var service = CreateService(blobStore, new InMemoryCatalog(), new HashChainedAuditStore());
        var wrongTenant = CreateAuthority() with { TenantId = "tenant-b" };

        var action = () => service.RetainPackageAsync(CreateRequest([1, 2, 3]), wrongTenant);

        await action.Should().ThrowAsync<ReportingArtifactVaultAccessDeniedException>();
        blobStore.StoreCalls.Should().Be(0);
    }

    [Fact]
    public async Task Retention_rejects_a_package_without_the_declared_manifest_artifact()
    {
        var blobStore = new InMemoryArtifactStore(Now);
        var service = CreateService(blobStore, new InMemoryCatalog(), new HashChainedAuditStore());
        var request = CreateRequest([1, 2, 3]) with { ManifestId = "missing-manifest.json" };

        var action = () => service.RetainPackageAsync(request, CreateAuthority());

        await action.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*exactly one rendered manifest artifact*");
        blobStore.StoreCalls.Should().Be(0);
    }

    [Fact]
    public async Task Retention_rejects_manifest_bytes_that_do_not_match_the_declared_hash()
    {
        var blobStore = new InMemoryArtifactStore(Now);
        var service = CreateService(blobStore, new InMemoryCatalog(), new HashChainedAuditStore());
        var request = CreateRequest([1, 2, 3]) with { ManifestHash = new string('c', 64) };

        var action = () => service.RetainPackageAsync(request, CreateAuthority());

        await action.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*does not match the declared manifest hash*");
        blobStore.StoreCalls.Should().Be(0);
    }

    [Fact]
    public async Task Restricted_policy_is_evaluated_from_immutable_catalog_snapshot()
    {
        var blobStore = new InMemoryArtifactStore(Now);
        var catalog = new InMemoryCatalog();
        var audit = new HashChainedAuditStore();
        var service = CreateService(blobStore, catalog, audit);
        await service.RetainPackageAsync(CreateRequest([1, 2, 3]), CreateAuthority());
        var retentionReadCalls = blobStore.ReadCalls;
        var unentitled = CreateAccessContext() with
        {
            ActorId = "intruder",
            PrincipalIds = ImmutableArray.Create("group-unentitled")
        };

        var action = () => service.ReadForDownloadAsync("package-1", "statement.pdf", unentitled);

        await action.Should().ThrowAsync<ReportingArtifactVaultAccessDeniedException>();
        blobStore.ReadCalls.Should().Be(retentionReadCalls, "denied access must not read artifact bytes");
        audit.Events[^1].Reason.Should().Contain("access-policy snapshot");
    }

    private static ReportingArtifactVaultService CreateService(
        InMemoryArtifactStore blobStore,
        InMemoryCatalog catalog,
        HashChainedAuditStore audit) =>
        new(blobStore, catalog, audit, new FixedTimeProvider(Now));

    private static ReportingArtifactPackageRetentionRequest CreateRequest(byte[] content)
    {
        var scope = new ReportingOperationalScope(
            "tenant-a",
            "organization-a",
            "company-a",
            "fund-a",
            "book-a",
            "2026-06");
        var access = new ReportingAccessScope(
            "policy-a",
            "7",
            ReportingGovernanceAccessMode.Restricted,
            OwnerPrincipalId: null,
            AllowOwnerAccess: false,
            Principals:
            [
                new ReportingAccessPrincipalScope(ReportingAccessPrincipalKind.User, "analyst-a"),
                new ReportingAccessPrincipalScope(ReportingAccessPrincipalKind.Group, "group-reporting")
            ],
            PolicyHash: new string('a', 64));
        var snapshot = new ReportingCertifiedSnapshotScope(
            scope.TenantId,
            scope.OrganizationId,
            scope.CompanyId,
            scope.FundId,
            scope.BookId,
            scope.PeriodId,
            "snapshot-a",
            new string('b', 64),
            "reconciliation-a",
            Now.AddMinutes(-5));

        return new ReportingArtifactPackageRetentionRequest(
            "package-1",
            "run-1",
            "series-1",
            Revision: 1,
            scope,
            access,
            snapshot,
            "statement.pdf",
            Sha256(content),
            ImmutableArray.Create(new ReportingRenderedArtifact(
                "statement.pdf",
                "statement.pdf",
                "application/pdf",
                content)));
    }

    private static ReportingAuthorityScope CreateAuthority() =>
        new(
            "renderer-service",
            "tenant-a",
            "organization-a",
            "company-a",
            ImmutableArray.Create(ReportingGovernancePermission.ExecuteRun),
            ReportingCommandOrigin.ServicePrincipal,
            "correlation-retain-1");

    private static ReportingArtifactAccessContext CreateAccessContext() =>
        new(
            "analyst-a",
            "tenant-a",
            "organization-a",
            "company-a",
            "fund-a",
            "book-a",
            "2026-06",
            ImmutableArray.Create("group-reporting"),
            "correlation-download-1");

    private static string Sha256(byte[] content) =>
        Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class InMemoryArtifactStore(DateTimeOffset storedAtUtc) : IReportingArtifactStore
    {
        private readonly Dictionary<ReportingArtifactIdentity, byte[]> _content = [];

        public int StoreCalls { get; private set; }

        public int ReadCalls { get; private set; }

        public int RetainedBlobCount => _content.Count;

        public bool CorruptReads { get; set; }

        public bool BlockFirstStore { get; init; }

        public TaskCompletionSource<bool> FirstStoreEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool> ReleaseFirstStore { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<ReportingArtifactWriteResult> StoreAsync(
            ReportingArtifactWriteRequest request,
            CancellationToken ct = default)
        {
            StoreCalls++;
            var bytes = request.Content.ToArray();
            if (BlockFirstStore && StoreCalls == 1)
            {
                FirstStoreEntered.TrySetResult(true);
                await ReleaseFirstStore.Task.WaitAsync(ct);
            }
            var identity = new ReportingArtifactIdentity(request.TenantId, Sha256(bytes));
            var alreadyExisted = _content.ContainsKey(identity);
            _content.TryAdd(identity, bytes);
            return new ReportingArtifactWriteResult(
                identity,
                bytes.LongLength,
                storedAtUtc,
                alreadyExisted);
        }

        public Task<ReportingArtifactReadResult> ReadAsync(
            ReportingArtifactIdentity identity,
            CancellationToken ct = default)
        {
            ReadCalls++;
            if (!_content.TryGetValue(identity, out var retained))
            {
                throw new ReportingArtifactNotFoundException(identity);
            }

            var bytes = retained.ToArray();
            if (CorruptReads)
            {
                bytes[0] ^= 0xff;
            }

            return Task.FromResult(new ReportingArtifactReadResult(
                identity,
                bytes.LongLength,
                storedAtUtc,
                bytes));
        }
    }

    private sealed class InMemoryCatalog : IReportingArtifactCatalog
    {
        private readonly Dictionary<string, ReportingRetainedArtifactPackage> _packages =
            new(StringComparer.Ordinal);

        public ValueTask<ReportingArtifactCatalogWriteResult> AddPackageAsync(
            ReportingRetainedArtifactPackage package,
            CancellationToken cancellationToken = default)
        {
            if (_packages.TryGetValue(package.PackageId, out var existing))
            {
                if (!existing.Artifacts.SequenceEqual(package.Artifacts))
                {
                    throw new ReportingArtifactCatalogIntegrityException(
                        "Attempted to replace immutable package metadata.");
                }

                return ValueTask.FromResult(new ReportingArtifactCatalogWriteResult(AlreadyExisted: true));
            }

            _packages.Add(package.PackageId, package);
            return ValueTask.FromResult(new ReportingArtifactCatalogWriteResult(AlreadyExisted: false));
        }

        public ValueTask<ReportingRetainedArtifactRecord?> GetArtifactAsync(
            string tenantId,
            string packageId,
            string artifactId,
            CancellationToken cancellationToken = default)
        {
            var result = _packages.TryGetValue(packageId, out var package)
                ? package.Artifacts.FirstOrDefault(item =>
                    string.Equals(item.Scope.TenantId, tenantId, StringComparison.Ordinal)
                    && string.Equals(item.ArtifactId, artifactId, StringComparison.Ordinal))
                : null;
            return ValueTask.FromResult(result);
        }

        public ValueTask<ReportingRetainedArtifactPackage?> GetPackageAsync(
            string tenantId,
            string packageId,
            CancellationToken cancellationToken = default)
        {
            var result = _packages.TryGetValue(packageId, out var package)
                && package.Artifacts.All(item =>
                    string.Equals(item.Scope.TenantId, tenantId, StringComparison.Ordinal))
                    ? package
                    : null;
            return ValueTask.FromResult(result);
        }
    }

    private sealed class HashChainedAuditStore : IReportingArtifactAuditStore
    {
        public List<ReportingArtifactAuditEvent> Events { get; } = [];

        public List<ReportingArtifactAuditReceipt> Receipts { get; } = [];

        public bool FailAppends { get; set; }

        public ValueTask<ReportingArtifactAuditReceipt> AppendAsync(
            ReportingArtifactAuditEvent auditEvent,
            CancellationToken cancellationToken = default)
        {
            if (FailAppends)
            {
                throw new IOException("audit unavailable");
            }

            var previousHash = Receipts.LastOrDefault()?.Hash;
            var payload = string.Join(
                '|',
                previousHash,
                auditEvent.EventId,
                auditEvent.OccurredAtUtc.ToString("O"),
                auditEvent.Action,
                auditEvent.ActorId,
                auditEvent.ActorTenantId,
                auditEvent.TargetTenantId,
                auditEvent.PackageId,
                auditEvent.ArtifactId,
                auditEvent.ContentHashSha256,
                auditEvent.CorrelationId,
                auditEvent.Reason);
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
            var receipt = new ReportingArtifactAuditReceipt(
                auditEvent.EventId,
                Receipts.Count + 1,
                previousHash,
                hash);
            Events.Add(auditEvent);
            Receipts.Add(receipt);
            return ValueTask.FromResult(receipt);
        }
    }
}
