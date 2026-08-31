using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.Reporting;
using Meridian.Ui.Shared.Services;

namespace Meridian.Tests.Reporting;

public sealed class ReportingSecureDistributionAuthorizationTests
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(ReportingAccessPrincipalKind.User, "User")]
    [InlineData(ReportingAccessPrincipalKind.Group, "Group")]
    [InlineData(ReportingAccessPrincipalKind.Company, "Company")]
    public void TypedRecipientKind_UsesStrictStringJsonRoundTrip(
        ReportingAccessPrincipalKind kind,
        string wireValue)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var command = new SecureReportingGrantIssueCommand(
            "run-a",
            "same-id",
            RecipientPrincipalKind: kind);

        var json = JsonSerializer.Serialize(command, options);
        json.Should().Contain($"\"recipientPrincipalKind\":\"{wireValue}\"");
        JsonSerializer.Deserialize<SecureReportingGrantIssueCommand>(json, options)!
            .RecipientPrincipalKind.Should().Be(kind);

        var numeric = json.Replace($"\"{wireValue}\"", ((int)kind).ToString(), StringComparison.Ordinal);
        var deserializeNumeric = () => JsonSerializer.Deserialize<SecureReportingGrantIssueCommand>(numeric, options);
        deserializeNumeric.Should().Throw<JsonException>();

        var unknown = json.Replace(wireValue, "Unknown", StringComparison.Ordinal);
        var deserializeUnknown = () => JsonSerializer.Deserialize<SecureReportingGrantIssueCommand>(unknown, options);
        deserializeUnknown.Should().Throw<JsonException>();
    }

    [Fact]
    public async Task DeliveryAndGrantDiscovery_DenyOtherCompanyOrAccessPolicyEvenForAdmin()
    {
        var fixture = await Fixture.CreateAsync();
        var otherCompanyAdmin = Authority(
            actor: "owner-a",
            tenant: "tenant-a",
            company: "company-b",
            principals: ["owner-a"],
            isAdmin: true);
        var outsidePrivatePolicyAdmin = Authority(
            actor: "admin-a",
            tenant: "tenant-a",
            company: "company-a",
            principals: ["admin-a"],
            isAdmin: true);
        var crossTenantAdmin = Authority(
            actor: "admin-b",
            tenant: "tenant-b",
            company: "company-a",
            principals: ["owner-a"],
            isAdmin: true);

        var otherCompanyDelivery = () => fixture.Application.GetDeliveryAsync(
            fixture.Job.JobId,
            otherCompanyAdmin);
        var accessDeniedDelivery = () => fixture.Application.GetDeliveryAsync(
            fixture.Job.JobId,
            outsidePrivatePolicyAdmin);
        var crossTenantDelivery = () => fixture.Application.GetDeliveryAsync(
            fixture.Job.JobId,
            crossTenantAdmin);
        var otherCompanyGrant = () => fixture.Application.GetAccessGrantAsync(
            fixture.Grant.GrantId,
            otherCompanyAdmin);
        var accessDeniedRevocation = () => fixture.Application.RevokeAccessGrantAsync(
            fixture.Grant.GrantId,
            "attempted administrative override",
            outsidePrivatePolicyAdmin);

        await otherCompanyDelivery.Should().ThrowAsync<UnauthorizedAccessException>();
        await accessDeniedDelivery.Should().ThrowAsync<UnauthorizedAccessException>();
        await crossTenantDelivery.Should().ThrowAsync<UnauthorizedAccessException>();
        await otherCompanyGrant.Should().ThrowAsync<UnauthorizedAccessException>();
        await accessDeniedRevocation.Should().ThrowAsync<UnauthorizedAccessException>();

        var retained = await fixture.AccessGrantStore.GetAsync(fixture.Grant.GrantId);
        retained!.RevokedAtUtc.Should().BeNull("an admin outside immutable scope cannot mutate a grant");
    }

    [Fact]
    public async Task AccessGrantDiscovery_UsedLegacyMultiArtifactGrantProjectsIndeterminateTerminalState()
    {
        var fixture = await Fixture.CreateAsync();
        var legacyGrant = fixture.Grant with
        {
            ArtifactIds = ["statement.pdf", "statement.xlsx"],
            MaxUses = 2,
            UseCount = 1,
            LastUsedAtUtc = FixedNow,
            Version = 1,
            ConsumedArtifactIds = null
        };
        fixture.AccessGrantStore.SetUnsafe(legacyGrant);
        var authority = Authority(
            "owner-a",
            "tenant-a",
            "company-a",
            ["owner-a"],
            isAdmin: false);

        var detail = await fixture.Application.GetAccessGrantAsync(
            legacyGrant.GrantId,
            authority);
        var listed = await fixture.Application.ListAccessGrantsAsync(
            legacyGrant.RunId,
            authority);

        detail.State.Should().Be("LegacyConsumptionIndeterminate");
        listed.Should().ContainSingle(grant =>
            grant.GrantId == legacyGrant.GrantId
            && grant.State == "LegacyConsumptionIndeterminate");
    }

    [Fact]
    public async Task AccessGrantDiscovery_ZeroArtifactLegacyPackageGrantProjectsScopeIndeterminate()
    {
        var fixture = await Fixture.CreateAsync();
        var legacyGrant = fixture.Grant with
        {
            AllowPackageRead = true,
            ArtifactIds = [],
            MaxUses = 1,
            UseCount = 0,
            LastUsedAtUtc = null,
            ConsumedArtifactIds = null
        };
        fixture.AccessGrantStore.SetUnsafe(legacyGrant);
        var authority = Authority(
            "owner-a",
            "tenant-a",
            "company-a",
            ["owner-a"],
            isAdmin: false);

        var detail = await fixture.Application.GetAccessGrantAsync(
            legacyGrant.GrantId,
            authority);
        var listed = await fixture.Application.ListAccessGrantsAsync(
            legacyGrant.RunId,
            authority);

        detail.State.Should().Be("LegacyScopeIndeterminate");
        listed.Should().ContainSingle(grant =>
            grant.GrantId == legacyGrant.GrantId
            && grant.State == "LegacyScopeIndeterminate");
    }

    [Fact]
    public async Task TransportCatalog_IsAuthenticatedCredentialFreeAndReportsFailClosedReadiness()
    {
        var fixture = await Fixture.CreateAsync();

        var capabilities = fixture.Application.GetTransportCapabilities(Authority(
            "owner-a",
            "tenant-a",
            "company-a",
            ["owner-a"],
            isAdmin: false));

        capabilities.Should().ContainSingle(capability =>
            capability.TransportId == "secure-portal"
            && capability.IsConfigured
            && capability.IsInfrastructureReady
            && capability.IsReady
            && !capability.IsExternal);
        capabilities.Should().ContainSingle(capability =>
            capability.TransportId == "http-relay"
            && !capability.IsConfigured
            && !capability.IsInfrastructureReady
            && !capability.IsReady
            && capability.InfrastructureDisabledReasonCode == "ADAPTER_NOT_CONFIGURED");
        JsonSerializer.Serialize(capabilities).Should()
            .NotContain("BearerCredential")
            .And.NotContain("HmacSecret")
            .And.NotContain("ExternalAccessBaseUri");

        var unauthenticated = () => fixture.Application.GetTransportCapabilities(
            Authority("owner-a", "tenant-a", "company-a", ["owner-a"], isAdmin: false) with
            {
                CanView = false
            });
        unauthenticated.Should().Throw<UnauthorizedAccessException>();

        var viewOnlyAuthority = Authority(
            "owner-a",
            "tenant-a",
            "company-a",
            ["owner-a"],
            isAdmin: false) with
        { CanDeliver = false };
        var viewOnly = fixture.Application.GetDistributionCapabilities(viewOnlyAuthority);
        viewOnly.CanQueueDelivery.Should().BeFalse();
        viewOnly.CanIssueAccessGrant.Should().BeFalse();
        viewOnly.CanRevokeAccessGrant.Should().BeFalse();
        viewOnly.ActionDisabledReasonCode.Should().Be("DELIVER_PERMISSION_REQUIRED");
        viewOnly.Transports.Should().OnlyContain(transport =>
            !transport.IsReady && transport.DisabledReasonCode == "DELIVER_PERMISSION_REQUIRED");
        viewOnly.Transports.Single(transport => transport.TransportId == "secure-portal")
            .IsInfrastructureReady.Should().BeTrue("permission and configuration readiness are separate server facts");
    }

    [Fact]
    public void ClientPackageDistribution_RejectsRequestedPrimaryArtifactSubset()
    {
        var scope = new ReportingOperationalScope(
            "tenant-a",
            "organization-a",
            "company-a",
            "fund-a",
            "book-a",
            "2026-07");
        var snapshot = CertifiedSnapshot(
            scope,
            outputFormat: ReportingOutputFormatDto.ClientPackage);

        Action selectPdfOnly = () =>
            ReportingSecureDistributionApplicationService.RequireAtomicClientPackageSelection(
                "run-a",
                snapshot,
                ["run-a.pdf"]);
        Action selectCompletePackage = () =>
            ReportingSecureDistributionApplicationService.RequireAtomicClientPackageSelection(
                "run-a",
                snapshot,
                ["run-a.pdf", "run-a.xlsx"]);

        selectPdfOnly.Should().Throw<InvalidOperationException>()
            .WithMessage("*atomic*both released PDF and XLSX primary artifacts*");
        selectCompletePackage.Should().NotThrow();
    }

    [Fact]
    public async Task ClientPackageExternalGrants_DefaultToSelectedArtifactCount()
    {
        var resolver = new ConfiguredReportingRecipientDestinationResolver(
        [
            new ReportingRecipientDestinationBinding(
                "tenant-a",
                "company-a",
                "recipient-a",
                "http-relay",
                "governed-recipient@example.test")
        ]);
        var fixture = await ReleasedFixture.CreateAsync(
            resolver,
            outputFormat: ReportingOutputFormatDto.ClientPackage);
        var artifactIds = new[] { $"{fixture.Run.RunId}.pdf", $"{fixture.Run.RunId}.xlsx" };
        var authority = Authority("owner-a", "tenant-a", "company-a", ["owner-a"], isAdmin: false);

        var issued = await fixture.Application.IssueAccessGrantAsync(
            new SecureReportingGrantIssueCommand(
                fixture.Run.RunId,
                "recipient-a",
                artifactIds,
                LifetimeSeconds: 900),
            authority);
        var queued = await fixture.Application.QueueDeliveryAsync(
            fixture.BuildQueueCommand(
                string.Empty,
                artifactIds: artifactIds,
                grantMaxUses: null),
            authority);

        (await fixture.AccessGrantStore.GetAsync(issued.GrantId))!.MaxUses
            .Should().Be(artifactIds.Length);
        queued.Payload.ExternalAccess.Should().NotBeNull();
        queued.Payload.ExternalAccess!.MaxUses.Should().Be(artifactIds.Length);
    }

    [Theory]
    [InlineData(".pdf", ".xlsx")]
    [InlineData(".xlsx", ".pdf")]
    public async Task ClientPackageDeliveryLinkedGrant_DuplicateCannotConsumeOtherArtifactBudget(
        string firstExtension,
        string secondExtension)
    {
        var fixture = await ReleasedFixture.CreateAsync(
            new RejectingReportingRecipientDestinationResolver(),
            outputFormat: ReportingOutputFormatDto.ClientPackage);
        var artifactIds = new[]
        {
            $"{fixture.Run.RunId}.pdf",
            $"{fixture.Run.RunId}.xlsx"
        };
        var issued = await fixture.Application.IssueAccessGrantAsync(
            new SecureReportingGrantIssueCommand(
                fixture.Run.RunId,
                "recipient-a",
                artifactIds,
                LifetimeSeconds: 900,
                MaxUses: artifactIds.Length),
            Authority("owner-a", "tenant-a", "company-a", ["owner-a"], isAdmin: false));
        var job = BuildLinkedDelivery(
            fixture,
            $"delivery-client-package-{firstExtension[1..]}",
            issued.GrantId,
            ReportingDeliveryState.Sent,
            providerMessageId: "provider-client-package",
            lastErrorCode: null,
            receipts: []);
        (await fixture.DeliveryStore.TryCreateAsync(job)).Should().BeTrue();
        var token = ExtractFragmentValue(issued.RecipientAccessUri, "token");
        var firstArtifactId = $"{fixture.Run.RunId}{firstExtension}";
        var secondArtifactId = $"{fixture.Run.RunId}{secondExtension}";

        var first = await fixture.Application.ExchangeGrantForDownloadAsync(
            issued.GrantId,
            new SecureReportingGrantExchangeCommand(token, firstArtifactId),
            $"download-{firstExtension[1..]}");
        var duplicate = () => fixture.Application.ExchangeGrantForDownloadAsync(
            issued.GrantId,
            new SecureReportingGrantExchangeCommand(token, firstArtifactId),
            $"duplicate-{firstExtension[1..]}");
        var denied = await duplicate.Should()
            .ThrowAsync<SecureReportingAccessGrantDeniedException>();
        var second = await fixture.Application.ExchangeGrantForDownloadAsync(
            issued.GrantId,
            new SecureReportingGrantExchangeCommand(token, secondArtifactId),
            $"download-{secondExtension[1..]}");

        denied.Which.Status.Should().Be(
            ReportingAccessGrantValidationStatus.UseLimitExceeded);
        first.Artifact.ArtifactId.Should().Be(firstArtifactId);
        second.Artifact.ArtifactId.Should().Be(secondArtifactId);
        var consumedGrant = (await fixture.AccessGrantStore.GetAsync(issued.GrantId))!;
        consumedGrant.UseCount.Should().Be(2);
        consumedGrant.ConsumedArtifactIds.Should().Equal(artifactIds);
        var delivery = (await fixture.DeliveryStore.GetAsync(job.JobId))!;
        delivery.Receipts.Should().HaveCount(2)
            .And.OnlyContain(receipt =>
                receipt.Kind == ReportingDeliveryReceiptKind.Downloaded);
        fixture.AuditStore.Events.Should().HaveCount(2)
            .And.OnlyContain(audit =>
                audit.Action == ReportingArtifactAuditAction.ContentAccessed);
    }

    [Fact]
    public async Task ClientPackageDeliveryLinkedGrant_ConcurrentDistinctReadsRetryAtomicConflict()
    {
        var fixture = await ReleasedFixture.CreateAsync(
            new RejectingReportingRecipientDestinationResolver(),
            outputFormat: ReportingOutputFormatDto.ClientPackage);
        var artifactIds = new[]
        {
            $"{fixture.Run.RunId}.pdf",
            $"{fixture.Run.RunId}.xlsx"
        };
        var issued = await fixture.Application.IssueAccessGrantAsync(
            new SecureReportingGrantIssueCommand(
                fixture.Run.RunId,
                "recipient-a",
                artifactIds,
                LifetimeSeconds: 900,
                MaxUses: artifactIds.Length),
            Authority("owner-a", "tenant-a", "company-a", ["owner-a"], isAdmin: false));
        var job = BuildLinkedDelivery(
            fixture,
            "delivery-client-package-concurrent",
            issued.GrantId,
            ReportingDeliveryState.Sent,
            providerMessageId: "provider-client-package-concurrent",
            lastErrorCode: null,
            receipts: []);
        (await fixture.DeliveryStore.TryCreateAsync(job)).Should().BeTrue();
        fixture.DeliveryStore.CoordinateNextAtomicCommitPair = true;
        var token = ExtractFragmentValue(issued.RecipientAccessUri, "token");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var downloads = await Task.WhenAll(
            fixture.Application.ExchangeGrantForDownloadAsync(
                issued.GrantId,
                new SecureReportingGrantExchangeCommand(token, artifactIds[0]),
                "download-concurrent-pdf",
                timeout.Token),
            fixture.Application.ExchangeGrantForDownloadAsync(
                issued.GrantId,
                new SecureReportingGrantExchangeCommand(token, artifactIds[1]),
                "download-concurrent-xlsx",
                timeout.Token));

        downloads.Select(download => download.Artifact.ArtifactId)
            .Should().BeEquivalentTo(artifactIds);
        var consumedGrant = (await fixture.AccessGrantStore.GetAsync(issued.GrantId))!;
        consumedGrant.UseCount.Should().Be(2);
        consumedGrant.ConsumedArtifactIds.Should().Equal(artifactIds);
        var delivery = (await fixture.DeliveryStore.GetAsync(job.JobId))!;
        delivery.Receipts.Should().HaveCount(2)
            .And.OnlyContain(receipt =>
                receipt.Kind == ReportingDeliveryReceiptKind.Downloaded);
        fixture.AuditStore.Events.Should().HaveCount(2);
        fixture.DeliveryStore.AtomicCommitAttempts.Should().Be(3,
            "the losing initial commit should reload both aggregates and succeed once");
    }

    [Fact]
    public async Task ClientPackageExternalGrants_RejectUseLimitBelowSelectedArtifactCount()
    {
        var resolver = new ConfiguredReportingRecipientDestinationResolver(
        [
            new ReportingRecipientDestinationBinding(
                "tenant-a",
                "company-a",
                "recipient-a",
                "http-relay",
                "governed-recipient@example.test")
        ]);
        var fixture = await ReleasedFixture.CreateAsync(
            resolver,
            outputFormat: ReportingOutputFormatDto.ClientPackage);
        var artifactIds = new[] { $"{fixture.Run.RunId}.pdf", $"{fixture.Run.RunId}.xlsx" };
        var authority = Authority("owner-a", "tenant-a", "company-a", ["owner-a"], isAdmin: false);

        var issue = () => fixture.Application.IssueAccessGrantAsync(
            new SecureReportingGrantIssueCommand(
                fixture.Run.RunId,
                "recipient-a",
                artifactIds,
                LifetimeSeconds: 900,
                MaxUses: 1),
            authority);
        var queue = () => fixture.Application.QueueDeliveryAsync(
            fixture.BuildQueueCommand(
                string.Empty,
                artifactIds: artifactIds,
                grantMaxUses: 1),
            authority);

        await issue.Should().ThrowAsync<ArgumentOutOfRangeException>()
            .WithMessage("*all 2 selected artifacts*");
        await queue.Should().ThrowAsync<ArgumentOutOfRangeException>()
            .WithMessage("*all 2 selected artifacts*");
        fixture.AccessGrantStore.Count.Should().Be(0);
        fixture.DeliveryStore.Count.Should().Be(0);
    }

    [Fact]
    public async Task NonClientPackageGrant_PreservesConfiguredDefaultAndMaximum()
    {
        var fixture = await ReleasedFixture.CreateAsync(
            new RejectingReportingRecipientDestinationResolver());
        var authority = Authority("owner-a", "tenant-a", "company-a", ["owner-a"], isAdmin: false);

        var issued = await fixture.Application.IssueAccessGrantAsync(
            new SecureReportingGrantIssueCommand(
                fixture.Run.RunId,
                "recipient-a",
                ["statement.pdf"],
                LifetimeSeconds: 900),
            authority);
        var aboveMaximum = () => fixture.Application.IssueAccessGrantAsync(
            new SecureReportingGrantIssueCommand(
                fixture.Run.RunId,
                "recipient-a",
                ["statement.pdf"],
                LifetimeSeconds: 900,
                MaxUses: SecureReportingDistributionOptions.Default.MaximumGrantMaxUses + 1),
            authority);

        (await fixture.AccessGrantStore.GetAsync(issued.GrantId))!.MaxUses
            .Should().Be(SecureReportingDistributionOptions.Default.DefaultGrantMaxUses);
        await aboveMaximum.Should().ThrowAsync<ArgumentOutOfRangeException>()
            .WithMessage("*outside the configured range*");
        fixture.AccessGrantStore.Count.Should().Be(1);
    }

    [Theory]
    [InlineData(256, true)]
    [InlineData(257, false)]
    public async Task VerifiedProviderReceipt_EnforcesDurableProviderMessageIdBoundary(
        int providerMessageIdLength,
        bool expectedAccepted)
    {
        var providerMessageId = new string('p', providerMessageIdLength);
        var fixture = await Fixture.CreateAsync(providerMessageId);
        var command = new SecureReportingDeliveryReceiptCommand(
            "provider-event-boundary",
            ReportingDeliveryReceiptKind.Delivered,
            FixedNow,
            providerMessageId,
            "provider-evidence",
            "delivered");

        var record = () => fixture.Application.RecordVerifiedProviderReceiptAsync(
            fixture.Job.TransportId,
            fixture.Job.JobId,
            command,
            new ReportingProviderReceiptAuthentication("timestamp", "signature"));

        if (expectedAccepted)
        {
            var retained = await record();
            retained.State.Should().Be(ReportingDeliveryState.Delivered);
            retained.ProviderMessageId.Should().Be(providerMessageId);
        }
        else
        {
            await record.Should().ThrowAsync<ArgumentException>()
                .WithMessage("*256*");
        }
    }

    [Fact]
    public async Task RecipientDestinationResolver_RequiresExactScopeAndRejectsDuplicateBindings()
    {
        var resolver = new ConfiguredReportingRecipientDestinationResolver(
        [
            new ReportingRecipientDestinationBinding(
                "tenant-a",
                "company-a",
                "recipient-a",
                "http-relay",
                "recipient@example.test")
        ]);

        var exact = await resolver.ResolveDestinationAsync(new ReportingRecipientDestinationRequest(
            "tenant-a",
            "company-a",
            "recipient-a",
            "HTTP-RELAY"));
        var crossTenant = await resolver.ResolveDestinationAsync(new ReportingRecipientDestinationRequest(
            "tenant-b",
            "company-a",
            "recipient-a",
            "http-relay"));
        var crossCompany = await resolver.ResolveDestinationAsync(new ReportingRecipientDestinationRequest(
            "tenant-a",
            "company-b",
            "recipient-a",
            "http-relay"));
        var crossPrincipal = await resolver.ResolveDestinationAsync(new ReportingRecipientDestinationRequest(
            "tenant-a",
            "company-a",
            "recipient-b",
            "http-relay"));

        resolver.IsConfigured.Should().BeTrue();
        resolver.ConfiguredTransportIds.Should().Equal("http-relay");
        exact.Should().Be("recipient@example.test");
        crossTenant.Should().BeNull();
        crossCompany.Should().BeNull();
        crossPrincipal.Should().BeNull();
        var duplicate = () => new ConfiguredReportingRecipientDestinationResolver(
        [
            new ReportingRecipientDestinationBinding(
                "tenant-a", "company-a", "recipient-a", "http-relay", "first@example.test"),
            new ReportingRecipientDestinationBinding(
                "tenant-a", "company-a", "recipient-a", "HTTP-RELAY", "second@example.test")
        ]);
        duplicate.Should().Throw<ArgumentException>().WithMessage("*duplicate*");
        var durableBoundary = new string('d', 2_048);
        var boundaryResolver = new ConfiguredReportingRecipientDestinationResolver(
        [
            new ReportingRecipientDestinationBinding(
                "tenant-a", "company-a", "recipient-a", "http-relay", durableBoundary)
        ]);
        (await boundaryResolver.ResolveDestinationAsync(new ReportingRecipientDestinationRequest(
            "tenant-a", "company-a", "recipient-a", "http-relay"))).Should().Be(durableBoundary);
        var overDurableBoundary = () => new ConfiguredReportingRecipientDestinationResolver(
        [
            new ReportingRecipientDestinationBinding(
                "tenant-a", "company-a", "recipient-a", "http-relay", new string('d', 2_049))
        ]);
        overDurableBoundary.Should().Throw<ArgumentException>().WithMessage("*2048*");
    }

    [Fact]
    public async Task TypedRecipientCollision_RequiresKindAndCannotReplayExistingDelivery()
    {
        const string sharedId = "shared-principal";
        var resolver = new ConfiguredReportingRecipientDestinationResolver(
        [
            new ReportingRecipientDestinationBinding(
                "tenant-a", "company-a", sharedId, "http-relay", "shared@example.test",
                ReportingAccessPrincipalKind.User),
            new ReportingRecipientDestinationBinding(
                "tenant-a", "company-a", sharedId, "http-relay", "shared@example.test",
                ReportingAccessPrincipalKind.Group)
        ]);
        var fixture = await ReleasedFixture.CreateAsync(
            resolver,
            ReportingGovernanceAccessMode.Restricted,
            additionalPrincipals:
            [
                new ReportingAccessPrincipalScope(ReportingAccessPrincipalKind.User, sharedId),
                new ReportingAccessPrincipalScope(ReportingAccessPrincipalKind.Group, sharedId)
            ]);
        var authority = Authority("owner-a", "tenant-a", "company-a", ["owner-a"], isAdmin: false);

        var ambiguous = () => fixture.Application.IssueAccessGrantAsync(
            new SecureReportingGrantIssueCommand(
                fixture.Run.RunId,
                sharedId,
                ["statement.pdf"],
                LifetimeSeconds: 900,
                MaxUses: 1),
            authority);
        await ambiguous.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*kind is required*");

        var userGrant = await fixture.Application.IssueAccessGrantAsync(
            new SecureReportingGrantIssueCommand(
                fixture.Run.RunId,
                sharedId,
                ["statement.pdf"],
                LifetimeSeconds: 900,
                MaxUses: 1,
                ReportingAccessPrincipalKind.User),
            authority);
        var groupGrant = await fixture.Application.IssueAccessGrantAsync(
            new SecureReportingGrantIssueCommand(
                fixture.Run.RunId,
                sharedId,
                ["statement.pdf"],
                LifetimeSeconds: 900,
                MaxUses: 1,
                ReportingAccessPrincipalKind.Group),
            authority);

        userGrant.AudienceKind.Should().Be(ReportingAccessPrincipalKind.User);
        groupGrant.AudienceKind.Should().Be(ReportingAccessPrincipalKind.Group);
        (await fixture.AccessGrantStore.GetAsync(userGrant.GrantId))!.AudienceKind
            .Should().Be(ReportingAccessPrincipalKind.User);
        (await fixture.AccessGrantStore.GetAsync(groupGrant.GrantId))!.AudienceKind
            .Should().Be(ReportingAccessPrincipalKind.Group);

        var queuedUser = await fixture.Application.QueueDeliveryAsync(
            fixture.BuildQueueCommand(
                "shared@example.test",
                sharedId,
                "typed-collision-distribution",
                ReportingAccessPrincipalKind.User),
            authority);
        var replayAsGroup = () => fixture.Application.QueueDeliveryAsync(
            fixture.BuildQueueCommand(
                "shared@example.test",
                sharedId,
                "typed-collision-distribution",
                ReportingAccessPrincipalKind.Group),
            authority);

        queuedUser.Payload.RecipientKind.Should().Be(ReportingAccessPrincipalKind.User);
        await replayAsGroup.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*different immutable delivery content*");
        fixture.DeliveryStore.Count.Should().Be(1);
    }

    [Fact]
    public async Task ExternalDelivery_UsesOnlyServerResolvedDestinationAndExposesReadinessTruth()
    {
        var resolver = new ConfiguredReportingRecipientDestinationResolver(
        [
            new ReportingRecipientDestinationBinding(
                "tenant-a",
                "company-a",
                "recipient-a",
                "http-relay",
                "governed-recipient@example.test")
        ]);
        var fixture = await ReleasedFixture.CreateAsync(resolver);
        var authority = Authority("owner-a", "tenant-a", "company-a", ["owner-a"], isAdmin: false);
        var command = fixture.BuildQueueCommand(string.Empty);

        var queued = await fixture.Application.QueueDeliveryAsync(command, authority);
        var mismatch = () => fixture.Application.QueueDeliveryAsync(
            command with
            {
                DistributionId = "distribution-mismatch",
                Destination = "attacker@example.test"
            },
            authority);

        queued.Payload.Recipient.Should().Be("recipient-a");
        queued.Payload.Destination.Should().Be("governed-recipient@example.test");
        fixture.Application.GetTransportCapabilities(authority).Should().ContainSingle(capability =>
            capability.TransportId == "http-relay"
            && capability.IsInfrastructureReady
            && capability.IsReady
            && !capability.RequiresDestination);
        await mismatch.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("*server-resolved*");
        fixture.DeliveryStore.Count.Should().Be(1);
    }

    [Fact]
    public async Task ExternalDelivery_FailsClosedWhenExactScopeDestinationIsMissing()
    {
        var resolver = new ConfiguredReportingRecipientDestinationResolver(
        [
            new ReportingRecipientDestinationBinding(
                "tenant-b",
                "company-a",
                "recipient-a",
                "http-relay",
                "other-tenant@example.test")
        ]);
        var fixture = await ReleasedFixture.CreateAsync(resolver);
        var queue = () => fixture.Application.QueueDeliveryAsync(
            fixture.BuildQueueCommand(string.Empty),
            Authority("owner-a", "tenant-a", "company-a", ["owner-a"], isAdmin: false));

        await queue.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("*no server-resolved destination*");
        fixture.DeliveryStore.Count.Should().Be(0);
    }

    [Theory]
    [InlineData("owner-a")]
    [InlineData("recipient-a")]
    public async Task PrivateGrant_ExchangesForExactBytesAndAppendsAccessAudit(string recipient)
    {
        var fixture = await ReleasedFixture.CreateAsync(new RejectingReportingRecipientDestinationResolver());
        var issued = await fixture.Application.IssueAccessGrantAsync(
            new SecureReportingGrantIssueCommand(
                fixture.Run.RunId,
                recipient,
                ["statement.pdf"],
                LifetimeSeconds: 900,
                MaxUses: 1),
            Authority("owner-a", "tenant-a", "company-a", ["owner-a"], isAdmin: false));
        var token = ExtractFragmentValue(issued.RecipientAccessUri, "token");

        var download = await fixture.Application.ExchangeGrantForDownloadAsync(
            issued.GrantId,
            new SecureReportingGrantExchangeCommand(token, "statement.pdf"),
            "download-correlation");

        download.Content.Should().Equal(fixture.ExactBytes);
        download.Artifact.Identity.ContentHashSha256.Should().Be(fixture.ContentHash);
        (await fixture.AccessGrantStore.GetAsync(issued.GrantId))!.UseCount.Should().Be(1);
        fixture.AuditStore.Events.Should().ContainSingle(audit =>
            audit.Action == ReportingArtifactAuditAction.ContentAccessed
            && audit.ArtifactId == "statement.pdf"
            && audit.ActorId.Contains(issued.GrantId, StringComparison.Ordinal));
    }

    [Fact]
    public async Task RestrictedOwner_NotDuplicatedInPrincipalIds_CanDownloadGrantAndResolveDestination()
    {
        var resolver = new ConfiguredReportingRecipientDestinationResolver(
        [
            new ReportingRecipientDestinationBinding(
                "tenant-a",
                "company-a",
                "owner-a",
                "http-relay",
                "owner@example.test")
        ]);
        var fixture = await ReleasedFixture.CreateAsync(
            resolver,
            ReportingGovernanceAccessMode.Restricted);
        var authority = Authority("owner-a", "tenant-a", "company-a", ["owner-a"], isAdmin: false);

        fixture.Run.Access.OwnerPrincipalId.Should().Be("owner-a");
        fixture.Run.Access.Principals.Should().NotContain(principal =>
            principal.Kind == ReportingAccessPrincipalKind.User
            && principal.PrincipalId == "owner-a");

        var directDownload = await fixture.Application.DownloadArtifactAsync(
            fixture.Run.RunId,
            "statement.pdf",
            authority);
        var issued = await fixture.Application.IssueAccessGrantAsync(
            new SecureReportingGrantIssueCommand(
                fixture.Run.RunId,
                "owner-a",
                ["statement.pdf"],
                LifetimeSeconds: 900,
                MaxUses: 1),
            authority);
        var grantDownload = await fixture.Application.ExchangeGrantForDownloadAsync(
            issued.GrantId,
            new SecureReportingGrantExchangeCommand(
                ExtractFragmentValue(issued.RecipientAccessUri, "token"),
                "statement.pdf"),
            "restricted-owner-grant-download");
        var queued = await fixture.Application.QueueDeliveryAsync(
            fixture.BuildQueueCommand(
                "owner@example.test",
                recipientPrincipalId: "owner-a",
                distributionId: "restricted-owner-distribution"),
            authority);

        directDownload.Content.Should().Equal(fixture.ExactBytes);
        grantDownload.Content.Should().Equal(fixture.ExactBytes);
        (await fixture.AccessGrantStore.GetAsync(issued.GrantId))!.UseCount.Should().Be(1);
        queued.Payload.Recipient.Should().Be("owner-a");
        queued.Payload.Destination.Should().Be("owner@example.test");
        fixture.AuditStore.Events.Count(audit =>
            audit.Action == ReportingArtifactAuditAction.ContentAccessed).Should().Be(2);
    }

    [Fact]
    public async Task RestrictedOwner_NotDuplicatedInPrincipalIds_DeniesUnlistedPrincipal()
    {
        var fixture = await ReleasedFixture.CreateAsync(
            new RejectingReportingRecipientDestinationResolver(),
            ReportingGovernanceAccessMode.Restricted);
        var owner = Authority("owner-a", "tenant-a", "company-a", ["owner-a"], isAdmin: false);
        var outsider = Authority("outsider-a", "tenant-a", "company-a", ["outsider-a"], isAdmin: false);
        var download = () => fixture.Application.DownloadArtifactAsync(
            fixture.Run.RunId,
            "statement.pdf",
            outsider);
        var issueGrant = () => fixture.Application.IssueAccessGrantAsync(
            new SecureReportingGrantIssueCommand(
                fixture.Run.RunId,
                "outsider-a",
                ["statement.pdf"],
                LifetimeSeconds: 900,
                MaxUses: 1),
            owner);
        var queue = () => fixture.Application.QueueDeliveryAsync(
            fixture.BuildQueueCommand(
                string.Empty,
                recipientPrincipalId: "outsider-a",
                distributionId: "restricted-outsider-distribution"),
            owner);

        await download.Should().ThrowAsync<UnauthorizedAccessException>();
        await issueGrant.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("*outside the immutable reporting access policy*");
        await queue.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("*outside the immutable reporting access policy*");
        fixture.DeliveryStore.Count.Should().Be(0);
    }

    [Fact]
    public async Task AuthenticatedPackageDownload_DoesNotMarkUnattributedPortalRecipientsDelivered()
    {
        var fixture = await ReleasedFixture.CreateAsync(
            new RejectingReportingRecipientDestinationResolver());
        var release = ReportingDeliveryReleaseAuthorizationFactory.Create(fixture.Run);
        foreach (var recipient in new[] { "recipient-a", "recipient-b" })
        {
            var job = new ReportingDeliveryJobRecord(
                $"delivery-{recipient}",
                fixture.Run.Scope.TenantId,
                release.PackageId,
                $"distribution-{recipient}",
                "secure-portal",
                release,
                "owner-a",
                Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(recipient))).ToLowerInvariant(),
                new ReportingDeliveryPayload(
                    recipient,
                    "Private",
                    recipient,
                    "Released report",
                    "A report is available.",
                    $"/portal/reporting/secure/packages/{fixture.Run.RunId}"),
                ReportingDeliveryState.Sent,
                AttemptCount: 1,
                MaxAttempts: 3,
                FixedNow,
                FixedNow,
                NextAttemptAtUtc: null,
                LeaseOwner: null,
                LeaseExpiresAtUtc: null,
                LastErrorCode: null,
                LastError: null,
                ProviderMessageId: $"portal:{recipient}",
                AccessGrantId: null,
                Receipts: []);
            (await fixture.DeliveryStore.TryCreateAsync(job)).Should().BeTrue();
        }

        var download = await fixture.Application.DownloadArtifactAsync(
            fixture.Run.RunId,
            "statement.pdf",
            Authority("owner-a", "tenant-a", "company-a", ["owner-a"], isAdmin: false));

        download.Content.Should().Equal(fixture.ExactBytes);
        var retained = await fixture.DeliveryStore.ListByPackageAsync(
            fixture.Run.Scope.TenantId,
            release.PackageId);
        retained.Should().HaveCount(2).And.OnlyContain(job =>
            job.State == ReportingDeliveryState.Sent && job.Receipts.Count == 0);
    }

    [Fact]
    public async Task FailedProviderReceipt_IsDurableBeforeRevocationAndRestartReconciliationConverges()
    {
        var fixture = await ReleasedFixture.CreateAsync(
            new RejectingReportingRecipientDestinationResolver());
        var authority = Authority("owner-a", "tenant-a", "company-a", ["owner-a"], isAdmin: false);
        var issued = await fixture.Application.IssueAccessGrantAsync(
            new SecureReportingGrantIssueCommand(
                fixture.Run.RunId,
                "recipient-a",
                ["statement.pdf"],
                LifetimeSeconds: 900,
                MaxUses: 1,
                RecipientPrincipalKind: ReportingAccessPrincipalKind.User),
            authority);
        var release = ReportingDeliveryReleaseAuthorizationFactory.Create(fixture.Run);
        var job = new ReportingDeliveryJobRecord(
            "delivery-bounced-reconciliation",
            fixture.Run.Scope.TenantId,
            release.PackageId,
            "distribution-bounced-reconciliation",
            "http-relay",
            release,
            authority.ActorId,
            new string('f', 64),
            new ReportingDeliveryPayload(
                "recipient-a",
                fixture.Run.Access.Mode.ToString(),
                "recipient@example.test",
                "Released report",
                "Use the secure link.",
                $"/portal/reporting/secure/packages/{fixture.Run.RunId}",
                new ReportingDeliveryAccessPolicy(
                    "recipient-a",
                    "https://reports.example.test/portal/reporting/access",
                    TimeSpan.FromMinutes(15),
                    AllowPackageRead: false,
                    ArtifactIds: ["statement.pdf"],
                    MaxUses: 1,
                    AudienceKind: ReportingAccessPrincipalKind.User),
                ReportingAccessPrincipalKind.User),
            ReportingDeliveryState.Sent,
            AttemptCount: 1,
            MaxAttempts: 3,
            FixedNow,
            FixedNow,
            NextAttemptAtUtc: null,
            LeaseOwner: null,
            LeaseExpiresAtUtc: null,
            LastErrorCode: null,
            LastError: null,
            ProviderMessageId: "provider-bounced-1",
            AccessGrantId: issued.GrantId,
            Receipts: []);
        (await fixture.DeliveryStore.TryCreateAsync(job)).Should().BeTrue();
        fixture.AccessGrantStore.FailNextUpdates = 8;
        var command = new SecureReportingDeliveryReceiptCommand(
            "provider-event-bounced-1",
            ReportingDeliveryReceiptKind.Bounced,
            FixedNow,
            "provider-bounced-1",
            "provider-evidence-bounced-1",
            "recipient mailbox rejected notification");

        var interrupted = () => fixture.Application.RecordVerifiedProviderReceiptAsync(
            job.TransportId,
            job.JobId,
            command,
            new ReportingProviderReceiptAuthentication("timestamp", "signature"));
        await interrupted.Should().ThrowAsync<IOException>()
            .WithMessage("*pending reconciliation*");

        var durableFailure = (await fixture.DeliveryStore.GetAsync(job.JobId))!;
        durableFailure.State.Should().Be(ReportingDeliveryState.Failed);
        durableFailure.Receipts.Should().ContainSingle(receipt =>
            receipt.Kind == ReportingDeliveryReceiptKind.Bounced);
        (await fixture.AccessGrantStore.GetAsync(issued.GrantId))!.RevokedAtUtc.Should().BeNull();

        (await fixture.Application.ReconcileFailedDeliveryAccessGrantsAsync()).Should().Be(1);
        (await fixture.AccessGrantStore.GetAsync(issued.GrantId))!.RevokedAtUtc.Should().Be(FixedNow);
        var replayed = await fixture.Application.RecordVerifiedProviderReceiptAsync(
            job.TransportId,
            job.JobId,
            command,
            new ReportingProviderReceiptAuthentication("timestamp", "signature"));
        replayed.Should().Be(durableFailure);
        replayed.Receipts.Should().ContainSingle();
    }

    [Theory]
    [InlineData("RELAY_OUTCOME_UNKNOWN")]
    [InlineData("TRANSPORT_CANCELLED")]
    public async Task GrantExchange_AfterTransientUnknownOutcomeAndProviderSuccess_RemainsValid(
        string transientOutcomeCode)
    {
        var fixture = await ReleasedFixture.CreateAsync(
            new RejectingReportingRecipientDestinationResolver());
        var issued = await fixture.Application.IssueAccessGrantAsync(
            new SecureReportingGrantIssueCommand(
                fixture.Run.RunId,
                "recipient-a",
                ["statement.pdf"],
                LifetimeSeconds: 900,
                MaxUses: 1,
                RecipientPrincipalKind: ReportingAccessPrincipalKind.User),
            Authority("owner-a", "tenant-a", "company-a", ["owner-a"], isAdmin: false));
        const string providerMessageId = "provider-recovered-after-unknown";
        var retainedFailure = new ReportingDeliveryReceipt(
            $"attempt-{transientOutcomeCode}",
            ReportingDeliveryReceiptKind.Failed,
            FixedNow,
            "http-relay",
            ProviderReference: null,
            EvidenceReference: "release-evidence-a",
            Detail: $"{transientOutcomeCode}: provider acceptance was not yet observable");
        var providerSuccess = new ReportingDeliveryReceipt(
            $"provider-success-{transientOutcomeCode}",
            ReportingDeliveryReceiptKind.Delivered,
            FixedNow.AddSeconds(1),
            "http-relay",
            providerMessageId,
            "provider-delivery-evidence");
        var job = BuildLinkedDelivery(
            fixture,
            $"delivery-recovered-{transientOutcomeCode}",
            issued.GrantId,
            ReportingDeliveryState.Delivered,
            providerMessageId,
            lastErrorCode: null,
            receipts: [retainedFailure, providerSuccess]);
        (await fixture.DeliveryStore.TryCreateAsync(job)).Should().BeTrue();

        (await fixture.Application.ReconcileFailedDeliveryAccessGrantsAsync()).Should().Be(0);
        var download = await fixture.Application.ExchangeGrantForDownloadAsync(
            issued.GrantId,
            new SecureReportingGrantExchangeCommand(
                ExtractFragmentValue(issued.RecipientAccessUri, "token"),
                "statement.pdf"),
            $"recovered-{transientOutcomeCode}");

        download.Content.Should().Equal(fixture.ExactBytes);
        var grant = (await fixture.AccessGrantStore.GetAsync(issued.GrantId))!;
        grant.RevokedAtUtc.Should().BeNull();
        grant.UseCount.Should().Be(1);
        (await fixture.DeliveryStore.GetAsync(job.JobId))!.Receipts.Should().ContainSingle(
            receipt => receipt.Kind == ReportingDeliveryReceiptKind.Downloaded
                       && receipt.EvidenceReference == download.AuditEventId);
    }

    [Fact]
    public async Task DeliveryLinkedGrantExchange_TransientCommitConflictRetriesWithoutDuplicateReadOrReceipt()
    {
        var fixture = await ReleasedFixture.CreateAsync(
            new RejectingReportingRecipientDestinationResolver());
        var issued = await fixture.Application.IssueAccessGrantAsync(
            new SecureReportingGrantIssueCommand(
                fixture.Run.RunId,
                "recipient-a",
                ["statement.pdf"],
                LifetimeSeconds: 900,
                MaxUses: 1),
            Authority("owner-a", "tenant-a", "company-a", ["owner-a"], isAdmin: false));
        var job = BuildLinkedDelivery(
            fixture,
            "delivery-atomic-conflict",
            issued.GrantId,
            ReportingDeliveryState.Sent,
            providerMessageId: null,
            lastErrorCode: null,
            receipts: []);
        (await fixture.DeliveryStore.TryCreateAsync(job)).Should().BeTrue();
        fixture.DeliveryStore.FailNextAtomicCommits = 1;

        var download = await fixture.Application.ExchangeGrantForDownloadAsync(
            issued.GrantId,
            new SecureReportingGrantExchangeCommand(
                ExtractFragmentValue(issued.RecipientAccessUri, "token"),
                "statement.pdf"),
            "atomic-conflict-download");

        download.Content.Should().Equal(fixture.ExactBytes);
        (await fixture.AccessGrantStore.GetAsync(issued.GrantId))!.UseCount.Should().Be(1);
        (await fixture.DeliveryStore.GetAsync(job.JobId))!.Receipts.Should().ContainSingle(
            receipt => receipt.Kind == ReportingDeliveryReceiptKind.Downloaded
                       && receipt.EvidenceReference == download.AuditEventId);
        fixture.AuditStore.Events.Should().ContainSingle();
        fixture.DeliveryStore.AtomicCommitAttempts.Should().Be(2);
    }

    [Fact]
    public async Task DeliveryLinkedGrantExchange_PersistentCommitConflictStopsAtBoundWithoutUseOrReceipt()
    {
        var fixture = await ReleasedFixture.CreateAsync(
            new RejectingReportingRecipientDestinationResolver());
        var issued = await fixture.Application.IssueAccessGrantAsync(
            new SecureReportingGrantIssueCommand(
                fixture.Run.RunId,
                "recipient-a",
                ["statement.pdf"],
                LifetimeSeconds: 900,
                MaxUses: 1),
            Authority("owner-a", "tenant-a", "company-a", ["owner-a"], isAdmin: false));
        var job = BuildLinkedDelivery(
            fixture,
            "delivery-persistent-atomic-conflict",
            issued.GrantId,
            ReportingDeliveryState.Sent,
            providerMessageId: null,
            lastErrorCode: null,
            receipts: []);
        (await fixture.DeliveryStore.TryCreateAsync(job)).Should().BeTrue();
        fixture.DeliveryStore.FailNextAtomicCommits = int.MaxValue;

        var exchange = () => fixture.Application.ExchangeGrantForDownloadAsync(
            issued.GrantId,
            new SecureReportingGrantExchangeCommand(
                ExtractFragmentValue(issued.RecipientAccessUri, "token"),
                "statement.pdf"),
            "persistent-atomic-conflict-download");

        var denied = await exchange.Should()
            .ThrowAsync<SecureReportingAccessGrantDeniedException>();
        denied.Which.Status.Should().Be(
            ReportingAccessGrantValidationStatus.ConcurrencyConflict);
        (await fixture.AccessGrantStore.GetAsync(issued.GrantId))!.UseCount.Should().Be(0);
        (await fixture.DeliveryStore.GetAsync(job.JobId))!.Receipts.Should().BeEmpty();
        fixture.AuditStore.Events.Should().ContainSingle(
            "optimistic retries must reuse the one integrity-verified retained read");
        fixture.DeliveryStore.AtomicCommitAttempts.Should().Be(8);
    }

    [Fact]
    public async Task DeliveryLinkedGrantExchange_WithoutAtomicCommitterFailsBeforeReadOrUse()
    {
        var fixture = await ReleasedFixture.CreateAsync(
            new RejectingReportingRecipientDestinationResolver(),
            exposeAtomicGrantDownloadCommitter: false);
        var issued = await fixture.Application.IssueAccessGrantAsync(
            new SecureReportingGrantIssueCommand(
                fixture.Run.RunId,
                "recipient-a",
                ["statement.pdf"],
                LifetimeSeconds: 900,
                MaxUses: 1),
            Authority("owner-a", "tenant-a", "company-a", ["owner-a"], isAdmin: false));
        var job = BuildLinkedDelivery(
            fixture,
            "delivery-non-atomic",
            issued.GrantId,
            ReportingDeliveryState.Sent,
            providerMessageId: null,
            lastErrorCode: null,
            receipts: []);
        (await fixture.DeliveryStore.TryCreateAsync(job)).Should().BeTrue();

        var exchange = () => fixture.Application.ExchangeGrantForDownloadAsync(
            issued.GrantId,
            new SecureReportingGrantExchangeCommand(
                ExtractFragmentValue(issued.RecipientAccessUri, "token"),
                "statement.pdf"),
            "non-atomic-download");

        await exchange.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*atomic PostgreSQL distribution boundary*");
        (await fixture.AccessGrantStore.GetAsync(issued.GrantId))!.UseCount.Should().Be(0);
        (await fixture.DeliveryStore.GetAsync(job.JobId))!.Receipts.Should().BeEmpty();
        fixture.AuditStore.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task Reconciliation_LateBounceAfterDelivered_RevokesLinkedGrant()
    {
        var fixture = await ReleasedFixture.CreateAsync(
            new RejectingReportingRecipientDestinationResolver());
        var issued = await fixture.Application.IssueAccessGrantAsync(
            new SecureReportingGrantIssueCommand(
                fixture.Run.RunId,
                "recipient-a",
                ["statement.pdf"],
                LifetimeSeconds: 900,
                MaxUses: 1,
                RecipientPrincipalKind: ReportingAccessPrincipalKind.User),
            Authority("owner-a", "tenant-a", "company-a", ["owner-a"], isAdmin: false));
        const string providerMessageId = "provider-late-bounce";
        var delivered = new ReportingDeliveryReceipt(
            "provider-delivered-before-bounce",
            ReportingDeliveryReceiptKind.Delivered,
            FixedNow,
            "http-relay",
            providerMessageId,
            "provider-delivery-evidence");
        var lateBounce = new ReportingDeliveryReceipt(
            "provider-late-bounce-event",
            ReportingDeliveryReceiptKind.Bounced,
            FixedNow.AddSeconds(1),
            "http-relay",
            providerMessageId,
            "provider-bounce-evidence",
            "mailbox later rejected the accepted notification");
        var job = BuildLinkedDelivery(
            fixture,
            "delivery-late-bounce",
            issued.GrantId,
            ReportingDeliveryState.Delivered,
            providerMessageId,
            lastErrorCode: null,
            receipts: [delivered, lateBounce]);
        (await fixture.DeliveryStore.TryCreateAsync(job)).Should().BeTrue();

        (await fixture.Application.ReconcileFailedDeliveryAccessGrantsAsync()).Should().Be(1);

        (await fixture.AccessGrantStore.GetAsync(issued.GrantId))!.RevokedAtUtc.Should().Be(FixedNow);
    }

    [Theory]
    [InlineData(-301, false)]
    [InlineData(-300, true)]
    [InlineData(-1, true)]
    [InlineData(300, true)]
    [InlineData(301, false)]
    public async Task VerifiedProviderReceipt_ResolvesPreboundUnknownRetryWithinInclusiveClockSkew(
        int occurredOffsetSeconds,
        bool accepted)
    {
        var fixture = await ReleasedFixture.CreateAsync(
            new RejectingReportingRecipientDestinationResolver());
        var issued = await fixture.Application.IssueAccessGrantAsync(
            new SecureReportingGrantIssueCommand(
                fixture.Run.RunId,
                "recipient-a",
                ["statement.pdf"],
                LifetimeSeconds: 900,
                MaxUses: 1,
                RecipientPrincipalKind: ReportingAccessPrincipalKind.User),
            Authority("owner-a", "tenant-a", "company-a", ["owner-a"], isAdmin: false));
        var retainedFailure = new ReportingDeliveryReceipt(
            "attempt-unknown-clock-skew",
            ReportingDeliveryReceiptKind.Failed,
            FixedNow,
            "http-relay",
            ProviderReference: null,
            EvidenceReference: "release-evidence-a",
            Detail: "RELAY_OUTCOME_UNKNOWN: provider acceptance was not yet observable");
        var job = BuildLinkedDelivery(
            fixture,
            $"delivery-clock-skew-{occurredOffsetSeconds}",
            issued.GrantId,
            ReportingDeliveryState.RetryScheduled,
            providerMessageId: null,
            lastErrorCode: "RELAY_OUTCOME_UNKNOWN",
            receipts: [retainedFailure]);
        (await fixture.DeliveryStore.TryCreateAsync(job)).Should().BeTrue();
        var command = new SecureReportingDeliveryReceiptCommand(
            $"provider-event-clock-skew-{occurredOffsetSeconds}",
            ReportingDeliveryReceiptKind.Delivered,
            FixedNow.AddSeconds(occurredOffsetSeconds),
            "provider-clock-skew-recovered",
            "provider-delivery-evidence",
            "provider confirmed delivery after the retained unknown outcome");
        var record = () => fixture.Application.RecordVerifiedProviderReceiptAsync(
            job.TransportId,
            job.JobId,
            command,
            new ReportingProviderReceiptAuthentication("timestamp", "signature"));

        if (accepted)
        {
            var resolved = await record();
            resolved.State.Should().Be(ReportingDeliveryState.Delivered);
            resolved.ProviderMessageId.Should().Be("provider-clock-skew-recovered");
            resolved.Receipts.Should().HaveCount(2);
            (await fixture.Application.ReconcileFailedDeliveryAccessGrantsAsync()).Should().Be(0);
            (await fixture.AccessGrantStore.GetAsync(issued.GrantId))!.RevokedAtUtc.Should().BeNull();
        }
        else
        {
            await record.Should().ThrowAsync<ArgumentOutOfRangeException>();
            var retained = (await fixture.DeliveryStore.GetAsync(job.JobId))!;
            retained.State.Should().Be(ReportingDeliveryState.RetryScheduled);
            retained.ProviderMessageId.Should().BeNull();
            retained.Receipts.Should().ContainSingle();
        }
    }

    [Fact]
    public async Task GrantExchange_MaxLengthAudience_UsesBoundedAuditActorAndReturnsExactBytes()
    {
        var audience = new string('p', 256);
        var fixture = await ReleasedFixture.CreateAsync(
            new RejectingReportingRecipientDestinationResolver(),
            additionalPrincipalId: audience);
        var issued = await fixture.Application.IssueAccessGrantAsync(
            new SecureReportingGrantIssueCommand(
                fixture.Run.RunId,
                audience,
                ["statement.pdf"],
                LifetimeSeconds: 900,
                MaxUses: 1),
            Authority("owner-a", "tenant-a", "company-a", ["owner-a"], isAdmin: false));

        var download = await fixture.Application.ExchangeGrantForDownloadAsync(
            issued.GrantId,
            new SecureReportingGrantExchangeCommand(
                ExtractFragmentValue(issued.RecipientAccessUri, "token"),
                "statement.pdf"),
            "max-audience-download");

        download.Content.Should().Equal(fixture.ExactBytes);
        fixture.AuditStore.Events.Should().ContainSingle(audit =>
            audit.Action == ReportingArtifactAuditAction.ContentAccessed
            && audit.ActorId == $"grant:{issued.GrantId}"
            && audit.ActorId.Length <= 256);
    }

    [Fact]
    public async Task GrantExchange_DoesNotConsumeUseWhenExactBytesAreCorrupt()
    {
        var fixture = await ReleasedFixture.CreateAsync(new RejectingReportingRecipientDestinationResolver());
        var issued = await fixture.Application.IssueAccessGrantAsync(
            new SecureReportingGrantIssueCommand(
                fixture.Run.RunId,
                "owner-a",
                ["statement.pdf"],
                LifetimeSeconds: 900,
                MaxUses: 1),
            Authority("owner-a", "tenant-a", "company-a", ["owner-a"], isAdmin: false));
        fixture.ArtifactStore.Content = Encoding.UTF8.GetBytes("corrupt replacement bytes");
        var exchange = () => fixture.Application.ExchangeGrantForDownloadAsync(
            issued.GrantId,
            new SecureReportingGrantExchangeCommand(
                ExtractFragmentValue(issued.RecipientAccessUri, "token"),
                "statement.pdf"),
            "corrupt-download");

        await exchange.Should().ThrowAsync<ReportingArtifactIntegrityException>();
        (await fixture.AccessGrantStore.GetAsync(issued.GrantId))!.UseCount.Should().Be(0);
        fixture.AuditStore.Events.Should().ContainSingle(audit =>
            audit.Action == ReportingArtifactAuditAction.IntegrityFailure);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DirectGrant_IssuanceFailsBeforeCredentialExistsWhenReleasedBytesAreUnavailable(
        bool corruptBytes)
    {
        var fixture = await ReleasedFixture.CreateAsync(
            new RejectingReportingRecipientDestinationResolver());
        if (corruptBytes)
        {
            fixture.ArtifactStore.Content = Encoding.UTF8.GetBytes("corrupt replacement bytes");
        }
        else
        {
            fixture.ArtifactCatalog.ReturnArtifact = false;
        }

        var issue = () => fixture.Application.IssueAccessGrantAsync(
            new SecureReportingGrantIssueCommand(
                fixture.Run.RunId,
                "owner-a",
                ["statement.pdf"],
                LifetimeSeconds: 900,
                MaxUses: 1),
            Authority("owner-a", "tenant-a", "company-a", ["owner-a"], isAdmin: false));

        // Both unavailability modes fail closed with InvalidDataException and issue no grant;
        // the message is case-specific (corrupt bytes fail integrity verification, absent bytes
        // are reported missing from immutable storage).
        var expectedMessage = corruptBytes
            ? "*exact integrity verification*"
            : "*missing from immutable storage*";
        await issue.Should().ThrowAsync<InvalidDataException>()
            .WithMessage(expectedMessage);
        fixture.AccessGrantStore.Count.Should().Be(0);
    }

    [Fact]
    public async Task GrantExchange_DoesNotConsumeUseWhenCatalogBindingIsMissing()
    {
        var fixture = await ReleasedFixture.CreateAsync(new RejectingReportingRecipientDestinationResolver());
        var issued = await fixture.Application.IssueAccessGrantAsync(
            new SecureReportingGrantIssueCommand(
                fixture.Run.RunId,
                "owner-a",
                ["statement.pdf"],
                LifetimeSeconds: 900,
                MaxUses: 1),
            Authority("owner-a", "tenant-a", "company-a", ["owner-a"], isAdmin: false));
        fixture.ArtifactCatalog.ReturnArtifact = false;
        var exchange = () => fixture.Application.ExchangeGrantForDownloadAsync(
            issued.GrantId,
            new SecureReportingGrantExchangeCommand(
                ExtractFragmentValue(issued.RecipientAccessUri, "token"),
                "statement.pdf"),
            "missing-catalog-download");

        var denied = await exchange.Should().ThrowAsync<SecureReportingAccessGrantDeniedException>();
        denied.Which.Status.Should().Be(ReportingAccessGrantValidationStatus.ArtifactOutOfScope);
        (await fixture.AccessGrantStore.GetAsync(issued.GrantId))!.UseCount.Should().Be(0);
    }

    private static string ExtractFragmentValue(string accessUri, string key)
    {
        var fragmentIndex = accessUri.IndexOf('#', StringComparison.Ordinal);
        fragmentIndex.Should().BeGreaterThanOrEqualTo(0);
        var fragment = accessUri[(fragmentIndex + 1)..];
        return fragment.Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(static pair => pair.Split('=', 2))
            .Where(static pair => pair.Length == 2)
            .ToDictionary(
                static pair => Uri.UnescapeDataString(pair[0]),
                static pair => Uri.UnescapeDataString(pair[1]),
            StringComparer.Ordinal)[key];
    }

    private static ReportingDeliveryJobRecord BuildLinkedDelivery(
        ReleasedFixture fixture,
        string jobId,
        string accessGrantId,
        ReportingDeliveryState state,
        string? providerMessageId,
        string? lastErrorCode,
        IReadOnlyList<ReportingDeliveryReceipt> receipts)
    {
        var release = ReportingDeliveryReleaseAuthorizationFactory.Create(fixture.Run);
        var artifactIds = release.Artifacts
            .Select(static artifact => artifact.ArtifactId)
            .OrderBy(static artifactId => artifactId, StringComparer.Ordinal)
            .ToArray();
        return new ReportingDeliveryJobRecord(
            jobId,
            fixture.Run.Scope.TenantId,
            release.PackageId,
            $"distribution-{jobId}",
            "http-relay",
            release,
            "owner-a",
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(jobId))).ToLowerInvariant(),
            new ReportingDeliveryPayload(
                "recipient-a",
                fixture.Run.Access.Mode.ToString(),
                "recipient@example.test",
                "Released report",
                "Use the secure link.",
                $"/portal/reporting/secure/packages/{fixture.Run.RunId}",
                new ReportingDeliveryAccessPolicy(
                    "recipient-a",
                    "https://reports.example.test/portal/reporting/access",
                    TimeSpan.FromMinutes(15),
                    AllowPackageRead: false,
                    ArtifactIds: artifactIds,
                    MaxUses: artifactIds.Length,
                    AudienceKind: ReportingAccessPrincipalKind.User),
                ReportingAccessPrincipalKind.User),
            state,
            AttemptCount: 1,
            MaxAttempts: 3,
            FixedNow,
            FixedNow,
            NextAttemptAtUtc: state == ReportingDeliveryState.RetryScheduled
                ? FixedNow.AddMinutes(1)
                : null,
            LeaseOwner: null,
            LeaseExpiresAtUtc: null,
            LastErrorCode: lastErrorCode,
            LastError: lastErrorCode is null ? null : "provider outcome retained for exact replay",
            ProviderMessageId: providerMessageId,
            AccessGrantId: accessGrantId,
            Receipts: receipts);
    }

    private static ReportingDistributionAuthority Authority(
        string actor,
        string tenant,
        string company,
        ImmutableArray<string> principals,
        bool isAdmin) =>
        new(
            actor,
            tenant,
            company,
            principals,
            CanView: true,
            CanDeliver: true,
            CanAdminister: isAdmin,
            CorrelationId: "test-correlation");

    private static ReportingCertifiedSnapshotScope CertifiedSnapshot(
        ReportingOperationalScope scope,
        string snapshotId = "snapshot-a",
        ReportingOutputFormatDto outputFormat = ReportingOutputFormatDto.Pdf)
    {
        var parametersJson = JsonSerializer.Serialize(new
        {
            scope = new
            {
                fundProfileId = scope.FundId,
                entityScopeKind = ReportingEntityScopeKindDto.AllEntities.ToString(),
                entityId = (string?)null,
                portfolioId = (string?)null,
                investorId = (string?)null,
                dimensions = (object?)null
            },
            periodId = scope.PeriodId,
            asOfDate = "2026-07-15",
            ledgerBookId = (string?)null,
            ledgerBookCode = scope.BookId,
            accountingBasis = ReportingAccountingBasisDto.Gaap.ToString(),
            presentationCurrency = "USD",
            consolidationLevel = ReportingConsolidationLevelDto.Fund.ToString(),
            outputFormat = outputFormat.ToString(),
            finality = ReportingFinalityDto.Final.ToString(),
            includeSupportingSchedules = true,
            includeEvidenceAppendix = true,
            templateParameters = new Dictionary<string, string>()
        });
        var parametersHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(parametersJson)))
            .ToLowerInvariant();
        return new ReportingCertifiedSnapshotScope(
            scope.TenantId,
            scope.OrganizationId,
            scope.CompanyId,
            scope.FundId,
            scope.BookId,
            scope.PeriodId,
            snapshotId,
            new string('b', 64),
            "reconciliation-a",
            FixedNow,
            SourceCheckpointId: "source-a",
            SourceCheckpointHash: new string('d', 64),
            ReconciliationCheckpointHash: new string('e', 64),
            ParametersCanonicalJson: parametersJson,
            ParametersHash: parametersHash);
    }

    private sealed class Fixture
    {
        private Fixture(
            ReportingSecureDistributionApplicationService application,
            MemoryAccessGrantStore accessGrantStore,
            ReportingAccessGrantRecord grant,
            ReportingDeliveryJobRecord job)
        {
            Application = application;
            AccessGrantStore = accessGrantStore;
            Grant = grant;
            Job = job;
        }

        public ReportingSecureDistributionApplicationService Application { get; }
        public MemoryAccessGrantStore AccessGrantStore { get; }
        public ReportingAccessGrantRecord Grant { get; }
        public ReportingDeliveryJobRecord Job { get; }

        public static async Task<Fixture> CreateAsync(
            string providerMessageId = "portal:package-a")
        {
            var clock = new FixedTimeProvider(FixedNow);
            var governanceRepository = new MemoryGovernanceRepository();
            var governance = new ReportingGovernanceService(
                governanceRepository,
                clock,
                prefix => $"{prefix}-1");
            var scope = new ReportingOperationalScope(
                "tenant-a",
                "organization-a",
                "company-a",
                "fund-a",
                "book-a",
                "2026-07");
            var creator = new ReportingAuthorityScope(
                "owner-a",
                "tenant-a",
                "organization-a",
                "company-a",
                [ReportingGovernancePermission.CreateRun],
                ReportingCommandOrigin.HumanOperator,
                "create-run",
                ["owner-a"]);
            var run = await governance.CreateRunAsync(
                new ReportingRunCreationRequest(
                    "run-a",
                    "series-a",
                    "investor-statement",
                    "1",
                    scope,
                    new ReportingAccessScope(
                        "policy-a",
                        "1",
                        ReportingGovernanceAccessMode.Private,
                        "owner-a",
                        AllowOwnerAccess: true,
                        ImmutableArray<ReportingAccessPrincipalScope>.Empty,
                        new string('a', 64)),
                    CertifiedSnapshot(scope)),
                creator);
            var packageId = ReportingArtifactPackageIdentity.Create(run);
            var release = new ReportingDeliveryReleaseAuthorization(
                "release-a",
                ReportingReleaseState.Released,
                "tenant-a",
                packageId,
                run.RunId,
                "1.1",
                new string('c', 64),
                [new ReportingReleasedArtifactReference(
                    "statement.pdf",
                    $"reporting-artifact://tenant-a/{packageId}/statement.pdf",
                    new string('d', 64),
                    128)],
                ["release-evidence-a"],
                FixedNow,
                "release-officer",
                new string('e', 64));
            var job = new ReportingDeliveryJobRecord(
                "delivery-a",
                "tenant-a",
                packageId,
                "distribution-a",
                "secure-portal",
                release,
                "owner-a",
                new string('f', 64),
                new ReportingDeliveryPayload(
                    "owner-a",
                    "Private",
                    "owner-a",
                    "Released report",
                    "A report is available.",
                    "/portal/reporting/packages/run-a"),
                ReportingDeliveryState.Sent,
                1,
                3,
                FixedNow,
                FixedNow,
                NextAttemptAtUtc: null,
                LeaseOwner: null,
                LeaseExpiresAtUtc: null,
                LastErrorCode: null,
                LastError: null,
                ProviderMessageId: providerMessageId,
                AccessGrantId: null,
                Receipts: []);
            var deliveryStore = new MemoryDeliveryStore(job);
            var accessGrantStore = new MemoryAccessGrantStore();
            var accessGrantService = new ReportingAccessGrantService(accessGrantStore, clock);
            var secret = await accessGrantService.IssueAsync(new ReportingAccessGrantIssueRequest(
                "tenant-a",
                "owner-a",
                run.RunId,
                packageId,
                FixedNow.AddHours(1),
                AllowPackageRead: false,
                ArtifactIds: ["statement.pdf"],
                MaxUses: 1));
            var grant = (await accessGrantStore.GetAsync(secret.GrantId))!;
            var artifactCatalog = new EmptyArtifactCatalog();
            var artifactStore = new EmptyArtifactStore();
            var releaseVerifier = new AlwaysAuthorizedReleaseVerifier();
            var dispatcher = new ReportingDeliveryDispatcher(
                deliveryStore,
                [new SecurePortalReportingDeliveryTransport(clock)],
                releaseVerifier,
                clock);
            var application = new ReportingSecureDistributionApplicationService(
                governanceRepository,
                artifactCatalog,
                dispatcher,
                deliveryStore,
                accessGrantService,
                accessGrantStore,
                new ReportingArtifactVaultService(
                    artifactStore,
                    artifactCatalog,
                    new ValidArtifactAuditStore(),
                    clock),
                releaseVerifier,
                new AcceptingProviderReceiptAuthenticator(),
                clock,
                SecureReportingDistributionOptions.Default);
            return new Fixture(application, accessGrantStore, grant, job);
        }
    }

    private sealed class ReleasedFixture
    {
        private ReleasedFixture(
            ReportingSecureDistributionApplicationService application,
            GovernedReportingRun run,
            MemoryAccessGrantStore accessGrantStore,
            ReleasedDeliveryStore deliveryStore,
            MutableArtifactCatalog artifactCatalog,
            MutableArtifactStore artifactStore,
            RecordingArtifactAuditStore auditStore,
            byte[] exactBytes,
            string contentHash)
        {
            Application = application;
            Run = run;
            AccessGrantStore = accessGrantStore;
            DeliveryStore = deliveryStore;
            ArtifactCatalog = artifactCatalog;
            ArtifactStore = artifactStore;
            AuditStore = auditStore;
            ExactBytes = exactBytes;
            ContentHash = contentHash;
        }

        public ReportingSecureDistributionApplicationService Application { get; }
        public GovernedReportingRun Run { get; }
        public MemoryAccessGrantStore AccessGrantStore { get; }
        public ReleasedDeliveryStore DeliveryStore { get; }
        public MutableArtifactCatalog ArtifactCatalog { get; }
        public MutableArtifactStore ArtifactStore { get; }
        public RecordingArtifactAuditStore AuditStore { get; }
        public byte[] ExactBytes { get; }
        public string ContentHash { get; }

        public SecureReportingDeliveryQueueCommand BuildQueueCommand(
            string destination,
            string recipientPrincipalId = "recipient-a",
            string distributionId = "distribution-a",
            ReportingAccessPrincipalKind? recipientPrincipalKind = null,
            IReadOnlyList<string>? artifactIds = null,
            int? grantMaxUses = 1) =>
            new(
                Run.RunId,
                distributionId,
                "http-relay",
                recipientPrincipalId,
                destination,
                "Released report",
                "Use the secure governed recipient link.",
                artifactIds ?? ["statement.pdf"],
                GrantLifetimeSeconds: 900,
                GrantMaxUses: grantMaxUses,
                MaxAttempts: 3,
                RecipientPrincipalKind: recipientPrincipalKind);

        public static async Task<ReleasedFixture> CreateAsync(
            IReportingRecipientDestinationResolver destinationResolver,
            ReportingGovernanceAccessMode accessMode = ReportingGovernanceAccessMode.Private,
            string? additionalPrincipalId = null,
            IReadOnlyList<ReportingAccessPrincipalScope>? additionalPrincipals = null,
            bool exposeAtomicGrantDownloadCommitter = true,
            ReportingOutputFormatDto outputFormat = ReportingOutputFormatDto.Pdf)
        {
            var clock = new FixedTimeProvider(FixedNow);
            var governanceRepository = new MemoryGovernanceRepository();
            var nextId = 0;
            var governance = new ReportingGovernanceService(
                governanceRepository,
                clock,
                prefix => $"{prefix}-{Interlocked.Increment(ref nextId)}");
            var scope = new ReportingOperationalScope(
                "tenant-a",
                "organization-a",
                "company-a",
                "fund-a",
                "book-a",
                "2026-07");
            ImmutableArray<ReportingAccessPrincipalScope> principals =
                (accessMode == ReportingGovernanceAccessMode.Restricted
                    ? new[] { "reviewer-a", "release-officer-a" }
                    : new[] { "recipient-a", "reviewer-a", "release-officer-a" })
                .Select(static principalId => new ReportingAccessPrincipalScope(
                    ReportingAccessPrincipalKind.User,
                    principalId))
                .ToImmutableArray();
            if (!string.IsNullOrWhiteSpace(additionalPrincipalId))
            {
                principals = principals.Add(new ReportingAccessPrincipalScope(
                    ReportingAccessPrincipalKind.User,
                    additionalPrincipalId));
            }
            if (additionalPrincipals is not null)
            {
                principals = principals.AddRange(additionalPrincipals);
            }

            var access = new ReportingAccessScope(
                "policy-a",
                "1",
                accessMode,
                "owner-a",
                AllowOwnerAccess: true,
                principals,
                new string('a', 64));
            var snapshot = CertifiedSnapshot(scope, outputFormat: outputFormat);
            var creator = GovernanceAuthority(
                "owner-a",
                scope,
                [
                    ReportingGovernancePermission.CreateRun,
                    ReportingGovernancePermission.ExecuteRun,
                    ReportingGovernancePermission.ValidateRun,
                    ReportingGovernancePermission.SubmitRun
                ]);
            var run = await governance.CreateRunAsync(
                new ReportingRunCreationRequest(
                    "run-a",
                    "series-a",
                    "investor-statement",
                    "1",
                    scope,
                    access,
                    snapshot),
                creator);
            run = await governance.BeginExecutionAsync(run.RunId, run.Version, creator);
            run = await governance.CompleteExecutionAsync(run.RunId, run.Version, creator);
            var readiness = new ReportingReadinessReceipt(
                "readiness-a",
                new string('0', 64),
                run.RunId,
                scope.TenantId,
                scope.OrganizationId,
                scope.CompanyId,
                snapshot.SnapshotId,
                snapshot.SnapshotHash,
                FixedNow,
                [new ReportingReadinessCheck("exact-source", true, ["evidence-source-a"])]);
            readiness = readiness with
            {
                ReceiptHash = ReportingGovernanceCanonicalValidation.ComputeReadinessReceiptHash(readiness)
            };
            run = await governance.ValidateAsync(
                run.RunId,
                run.Version,
                readiness,
                creator);
            run = await governance.SubmitAsync(run.RunId, run.Version, creator);
            run = await governance.ApproveAsync(
                run.RunId,
                run.Version,
                "independent review complete",
                GovernanceAuthority(
                    "reviewer-a",
                    scope,
                    [ReportingGovernancePermission.ApproveRun]));

            (string ArtifactId, string FileName, string ContentType, byte[] Content)[] artifactContents =
                outputFormat == ReportingOutputFormatDto.ClientPackage
                    ?
                    [
                        (
                            $"{run.RunId}.pdf",
                            $"{run.RunId}.pdf",
                            "application/pdf",
                            Encoding.UTF8.GetBytes("exact immutable client PDF bytes")),
                        (
                            $"{run.RunId}.xlsx",
                            $"{run.RunId}.xlsx",
                            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                            Encoding.UTF8.GetBytes("exact immutable client XLSX bytes"))
                    ]
                    :
                    [
                        (
                            "statement.pdf",
                            "statement.pdf",
                            "application/pdf",
                            Encoding.UTF8.GetBytes("exact immutable private reporting bytes"))
                    ];
            var releaseArtifacts = artifactContents
                .Select(artifact => new ReportingArtifactReference(
                    artifact.ArtifactId,
                    Convert.ToHexString(SHA256.HashData(artifact.Content)).ToLowerInvariant(),
                    artifact.Content.LongLength))
                .ToImmutableArray();
            var manifestHash = new string('d', 64);
            run = await governance.ReleaseAsync(
                run.RunId,
                run.Version,
                new ReportingReleaseEvidence(
                    "manifest-a",
                    manifestHash,
                    releaseArtifacts,
                    ["evidence-release-a"]),
                GovernanceAuthority(
                    "release-officer-a",
                    scope,
                    [ReportingGovernancePermission.ReleaseRun]));

            var packageId = ReportingArtifactPackageIdentity.Create(run);
            var retainedArtifacts = artifactContents
                .Select(artifact =>
                {
                    var hash = Convert.ToHexString(SHA256.HashData(artifact.Content)).ToLowerInvariant();
                    return new ReportingRetainedArtifactRecord(
                        packageId,
                        run.RunId,
                        run.SeriesId,
                        run.Revision,
                        scope,
                        access,
                        snapshot,
                        run.Release!.ManifestId,
                        run.Release.ManifestHash,
                        artifact.ArtifactId,
                        artifact.FileName,
                        artifact.ContentType,
                        new ReportingArtifactIdentity(scope.TenantId, hash),
                        artifact.Content.LongLength,
                        FixedNow);
                })
                .ToArray();
            var artifactCatalog = new MutableArtifactCatalog(retainedArtifacts);
            var artifactStore = new MutableArtifactStore(
                retainedArtifacts.ToDictionary(
                    static artifact => artifact.Identity,
                    artifact => artifactContents
                        .Single(content => content.ArtifactId == artifact.ArtifactId)
                        .Content),
                FixedNow);
            var auditStore = new RecordingArtifactAuditStore();
            var accessGrantStore = new MemoryAccessGrantStore();
            var accessGrantService = new ReportingAccessGrantService(accessGrantStore, clock);
            var deliveryStore = new ReleasedDeliveryStore(accessGrantStore);
            IReportingDeliveryStore applicationDeliveryStore =
                exposeAtomicGrantDownloadCommitter
                    ? deliveryStore
                    : new NonAtomicDeliveryStoreAdapter(deliveryStore);
            var releaseVerifier = new GovernanceReportingReleaseAuthorizationVerifier(
                governanceRepository,
                new ReportingReleasedArtifactIntegrityGate(artifactCatalog, artifactStore));
            var dispatcher = new ReportingDeliveryDispatcher(
                applicationDeliveryStore,
                [new HttpRelayReportingDeliveryTransport(
                    new AcceptingRelayClient(),
                    accessGrantService,
                    new HmacReportingDeliveryGrantCredentialDeriver(
                        Enumerable.Range(1, 32).Select(static value => (byte)value).ToArray()),
                    clock)],
                releaseVerifier,
                clock);
            var application = new ReportingSecureDistributionApplicationService(
                governanceRepository,
                artifactCatalog,
                dispatcher,
                applicationDeliveryStore,
                accessGrantService,
                accessGrantStore,
                new ReportingArtifactVaultService(
                    artifactStore,
                    artifactCatalog,
                    auditStore,
                    clock),
                releaseVerifier,
                new AcceptingProviderReceiptAuthenticator(),
                clock,
                SecureReportingDistributionOptions.Default with
                {
                    ExternalAccessBaseUri = "https://reports.example.test/portal/reporting/access"
                },
                destinationResolver);
            return new ReleasedFixture(
                application,
                run,
                accessGrantStore,
                deliveryStore,
                artifactCatalog,
                artifactStore,
                auditStore,
                artifactContents[0].Content,
                releaseArtifacts[0].ArtifactHash);
        }

        private static ReportingAuthorityScope GovernanceAuthority(
            string actor,
            ReportingOperationalScope scope,
            ImmutableArray<ReportingGovernancePermission> permissions) =>
            new(
                actor,
                scope.TenantId,
                scope.OrganizationId,
                scope.CompanyId,
                permissions,
                ReportingCommandOrigin.HumanOperator,
                $"correlation-{actor}",
                [actor]);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class AlwaysAuthorizedReleaseVerifier : IReportingReleaseAuthorizationVerifier
    {
        public Task<ReportingReleaseAuthorizationResult> VerifyAsync(
            ReportingDeliveryReleaseAuthorization authorization,
            CancellationToken ct = default) =>
            Task.FromResult(new ReportingReleaseAuthorizationResult(true, "TEST_VERIFIED"));
    }

    private sealed class MemoryGovernanceRepository :
        IReportingGovernanceRepository,
        IReportingGovernanceTransaction
    {
        private readonly Dictionary<(string TenantId, string RunId), GovernedReportingRun> _runs = [];

        public async ValueTask<TResult> ExecuteTransactionAsync<TResult>(
            Func<IReportingGovernanceTransaction, CancellationToken, ValueTask<TResult>> operation,
            CancellationToken cancellationToken = default) =>
            await operation(this, cancellationToken);

        public ValueTask<GovernedReportingRun?> GetRunAsync(
            string tenantId,
            string runId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_runs.GetValueOrDefault((tenantId, runId)));

        public ValueTask<IReadOnlyList<GovernedReportingRun>> ListRunsBySeriesAsync(
            string tenantId,
            string seriesId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<GovernedReportingRun>>(_runs.Values
                .Where(run => run.Scope.TenantId == tenantId && run.SeriesId == seriesId)
                .ToArray());

        public ValueTask AddRunAsync(
            GovernedReportingRun run,
            CancellationToken cancellationToken = default)
        {
            _runs.Add((run.Scope.TenantId, run.RunId), run);
            return ValueTask.CompletedTask;
        }

        public ValueTask ReplaceRunAsync(
            GovernedReportingRun run,
            long expectedVersion,
            CancellationToken cancellationToken = default)
        {
            _runs[(run.Scope.TenantId, run.RunId)] = run;
            return ValueTask.CompletedTask;
        }

        public ValueTask<ReportingRestatementRequest?> GetRestatementRequestAsync(
            string tenantId,
            string requestId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<ReportingRestatementRequest?>(null);

        public ValueTask AddRestatementRequestAsync(
            string tenantId,
            ReportingRestatementRequest request,
            CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask ReplaceRestatementRequestAsync(
            string tenantId,
            ReportingRestatementRequest request,
            long expectedVersion,
            CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }

    private sealed class MemoryAccessGrantStore : IReportingAccessGrantStore
    {
        private readonly Dictionary<string, ReportingAccessGrantRecord> _grants = new(StringComparer.Ordinal);

        internal object Gate { get; } = new();

        public int Count
        {
            get
            {
                lock (Gate)
                {
                    return _grants.Count;
                }
            }
        }

        public int FailNextUpdates { get; set; }

        public Task<ReportingAccessGrantRecord?> GetAsync(string grantId, CancellationToken ct = default)
        {
            lock (Gate)
            {
                return Task.FromResult(_grants.GetValueOrDefault(grantId));
            }
        }

        public Task<IReadOnlyList<ReportingAccessGrantRecord>> ListByPackageAsync(
            string tenantId,
            string packageId,
            CancellationToken ct = default)
        {
            lock (Gate)
            {
                return Task.FromResult<IReadOnlyList<ReportingAccessGrantRecord>>(
                    _grants.Values.Where(grant =>
                        grant.TenantId == tenantId && grant.PackageId == packageId).ToArray());
            }
        }

        public Task<bool> TryCreateAsync(ReportingAccessGrantRecord grant, CancellationToken ct = default)
        {
            lock (Gate)
            {
                if (!_grants.TryAdd(grant.GrantId, grant))
                {
                    return Task.FromResult(false);
                }

                return Task.FromResult(true);
            }
        }

        public Task<bool> TryUpdateAsync(
            string grantId,
            long expectedVersion,
            ReportingAccessGrantRecord updatedGrant,
            CancellationToken ct = default)
        {
            lock (Gate)
            {
                if (!_grants.TryGetValue(grantId, out var current) || current.Version != expectedVersion)
                {
                    return Task.FromResult(false);
                }

                if (FailNextUpdates > 0)
                {
                    FailNextUpdates--;
                    return Task.FromResult(false);
                }

                _grants[grantId] = updatedGrant;
                return Task.FromResult(true);
            }
        }

        internal ReportingAccessGrantRecord? GetUnsafe(string grantId) =>
            _grants.GetValueOrDefault(grantId);

        internal void SetUnsafe(ReportingAccessGrantRecord grant) =>
            _grants[grant.GrantId] = grant;
    }

    private sealed class MemoryDeliveryStore(ReportingDeliveryJobRecord job) : IReportingDeliveryStore
    {
        private ReportingDeliveryJobRecord _job = job;

        public Task<ReportingDeliveryJobRecord?> GetAsync(string jobId, CancellationToken ct = default) =>
            Task.FromResult<ReportingDeliveryJobRecord?>(_job.JobId == jobId ? _job : null);

        public Task<ReportingDeliveryJobRecord?> GetByIdempotencyKeyAsync(
            string idempotencyKey,
            CancellationToken ct = default) =>
            Task.FromResult<ReportingDeliveryJobRecord?>(_job.IdempotencyKey == idempotencyKey ? _job : null);

        public Task<ReportingDeliveryJobRecord?> GetByAccessGrantIdAsync(
            string accessGrantId,
            CancellationToken ct = default) =>
            Task.FromResult<ReportingDeliveryJobRecord?>(null);

        public Task<IReadOnlyList<ReportingDeliveryJobRecord>> ListByPackageAsync(
            string tenantId,
            string packageId,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ReportingDeliveryJobRecord>>(
                _job.TenantId == tenantId && _job.PackageId == packageId ? [_job] : []);

        public Task<bool> TryCreateAsync(ReportingDeliveryJobRecord created, CancellationToken ct = default) =>
            Task.FromResult(false);

        public Task<IReadOnlyList<ReportingDeliveryJobRecord>> ClaimDueAsync(
            DateTimeOffset nowUtc,
            string leaseOwner,
            TimeSpan leaseDuration,
            int take,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ReportingDeliveryJobRecord>>([]);

        // Optimistic-concurrency update matching the real store contract: succeed and retain the
        // new record when the expected version matches, so receipt-recording success paths can be
        // exercised. A hardcoded false would make every receipt update loop until it reports a
        // spurious "conflicted repeatedly".
        public Task<bool> TryUpdateAsync(
            string jobId,
            long expectedVersion,
            ReportingDeliveryJobRecord updatedJob,
            CancellationToken ct = default)
        {
            if (_job.JobId != jobId || _job.Version != expectedVersion)
            {
                return Task.FromResult(false);
            }

            _job = updatedJob;
            return Task.FromResult(true);
        }
    }

    private sealed class ReleasedDeliveryStore(MemoryAccessGrantStore accessGrantStore) :
        IReportingDeliveryStore,
        IReportingDeliveryGrantDownloadCommitter
    {
        private readonly Dictionary<string, ReportingDeliveryJobRecord> _jobs = new(StringComparer.Ordinal);
        private readonly TaskCompletionSource<bool> _coordinatedAtomicCommitPair =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _atomicCommitAttempts;
        private int _coordinatedAtomicCommitArrivals;

        public int FailNextAtomicCommits { get; set; }

        public bool CoordinateNextAtomicCommitPair { get; set; }

        public int AtomicCommitAttempts => Volatile.Read(ref _atomicCommitAttempts);

        public int Count
        {
            get
            {
                lock (accessGrantStore.Gate)
                {
                    return _jobs.Count;
                }
            }
        }

        public Task<ReportingDeliveryJobRecord?> GetAsync(string jobId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            lock (accessGrantStore.Gate)
            {
                return Task.FromResult(_jobs.GetValueOrDefault(jobId));
            }
        }

        public Task<ReportingDeliveryJobRecord?> GetByIdempotencyKeyAsync(
            string idempotencyKey,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            lock (accessGrantStore.Gate)
            {
                return Task.FromResult(_jobs.Values.SingleOrDefault(job =>
                    string.Equals(job.IdempotencyKey, idempotencyKey, StringComparison.Ordinal)));
            }
        }

        public Task<ReportingDeliveryJobRecord?> GetByAccessGrantIdAsync(
            string accessGrantId,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            lock (accessGrantStore.Gate)
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
            lock (accessGrantStore.Gate)
            {
                return Task.FromResult<IReadOnlyList<ReportingDeliveryJobRecord>>(_jobs.Values
                    .Where(job => string.Equals(job.TenantId, tenantId, StringComparison.Ordinal)
                                  && string.Equals(job.PackageId, packageId, StringComparison.Ordinal))
                    .ToArray());
            }
        }

        public Task<IReadOnlyList<ReportingDeliveryGrantRevocationCandidate>>
            ListPendingAccessGrantRevocationsAsync(
                int take,
                CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            lock (accessGrantStore.Gate)
            {
                return Task.FromResult<IReadOnlyList<ReportingDeliveryGrantRevocationCandidate>>(
                    _jobs.Values
                        .Where(job =>
                            job.AccessGrantId is not null
                            && job.Receipts.Any(receipt =>
                                receipt.Kind is ReportingDeliveryReceiptKind.Bounced or ReportingDeliveryReceiptKind.Rejected
                                || job.State == ReportingDeliveryState.Failed
                                && receipt.Kind == ReportingDeliveryReceiptKind.Failed
                                && receipt.Detail?.StartsWith("RELAY_OUTCOME_UNKNOWN:", StringComparison.Ordinal) != true
                                && receipt.Detail?.StartsWith("TRANSPORT_CANCELLED:", StringComparison.Ordinal) != true))
                        .OrderBy(job => job.UpdatedAtUtc)
                        .ThenBy(job => job.JobId, StringComparer.Ordinal)
                        .Take(take)
                        .Select(job => new ReportingDeliveryGrantRevocationCandidate(
                            job.JobId,
                            job.TenantId,
                            job.AccessGrantId!))
                        .ToArray());
            }
        }

        public Task<bool> TryCreateAsync(ReportingDeliveryJobRecord job, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            lock (accessGrantStore.Gate)
            {
                if (_jobs.ContainsKey(job.JobId)
                    || _jobs.Values.Any(existing =>
                        string.Equals(existing.IdempotencyKey, job.IdempotencyKey, StringComparison.Ordinal)))
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
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ReportingDeliveryJobRecord>>([]);

        public Task<bool> TryUpdateAsync(
            string jobId,
            long expectedVersion,
            ReportingDeliveryJobRecord updatedJob,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            lock (accessGrantStore.Gate)
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

        public async Task<ReportingDeliveryGrantDownloadCommitStatus> TryCommitAsync(
            ReportingDeliveryGrantDownloadCommit commit,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _atomicCommitAttempts);
            if (CoordinateNextAtomicCommitPair)
            {
                var arrival = Interlocked.Increment(ref _coordinatedAtomicCommitArrivals);
                if (arrival <= 2)
                {
                    if (arrival == 2)
                    {
                        _coordinatedAtomicCommitPair.TrySetResult(true);
                    }

                    await _coordinatedAtomicCommitPair.Task.WaitAsync(ct);
                }
            }

            lock (accessGrantStore.Gate)
            {
                if (FailNextAtomicCommits > 0)
                {
                    FailNextAtomicCommits--;
                    return ReportingDeliveryGrantDownloadCommitStatus.ConcurrencyConflict;
                }

                var currentGrant = accessGrantStore.GetUnsafe(commit.ConsumedGrant.GrantId);
                if (currentGrant is null
                    || currentGrant.Version != commit.ExpectedGrantVersion
                    || !_jobs.TryGetValue(
                        commit.DeliveryWithDownloadReceipt.JobId,
                        out var currentDelivery)
                    || currentDelivery.Version != commit.ExpectedDeliveryVersion)
                {
                    return ReportingDeliveryGrantDownloadCommitStatus.ConcurrencyConflict;
                }

                currentGrant.UseCount.Should().Be(commit.ConsumedGrant.UseCount - 1);
                currentDelivery.AccessGrantId.Should().Be(currentGrant.GrantId);
                commit.DeliveryWithDownloadReceipt.Receipts.Should().HaveCount(
                    currentDelivery.Receipts.Count + 1);
                commit.DeliveryWithDownloadReceipt.Receipts[^1].Kind
                    .Should().Be(ReportingDeliveryReceiptKind.Downloaded);
                accessGrantStore.SetUnsafe(commit.ConsumedGrant);
                _jobs[currentDelivery.JobId] = commit.DeliveryWithDownloadReceipt;
                return ReportingDeliveryGrantDownloadCommitStatus.Committed;
            }
        }
    }

    private sealed class NonAtomicDeliveryStoreAdapter(IReportingDeliveryStore inner)
        : IReportingDeliveryStore
    {
        public Task<ReportingDeliveryJobRecord?> GetAsync(
            string jobId,
            CancellationToken ct = default) =>
            inner.GetAsync(jobId, ct);

        public Task<ReportingDeliveryJobRecord?> GetByIdempotencyKeyAsync(
            string idempotencyKey,
            CancellationToken ct = default) =>
            inner.GetByIdempotencyKeyAsync(idempotencyKey, ct);

        public Task<ReportingDeliveryJobRecord?> GetByAccessGrantIdAsync(
            string accessGrantId,
            CancellationToken ct = default) =>
            inner.GetByAccessGrantIdAsync(accessGrantId, ct);

        public Task<IReadOnlyList<ReportingDeliveryJobRecord>> ListByPackageAsync(
            string tenantId,
            string packageId,
            CancellationToken ct = default) =>
            inner.ListByPackageAsync(tenantId, packageId, ct);

        public Task<IReadOnlyList<ReportingDeliveryJobRecord>> ListByRunAsync(
            string tenantId,
            string runId,
            CancellationToken ct = default) =>
            inner.ListByRunAsync(tenantId, runId, ct);

        public Task<IReadOnlyList<ReportingDeliveryGrantRevocationCandidate>>
            ListPendingAccessGrantRevocationsAsync(
                int take,
                CancellationToken ct = default) =>
            inner.ListPendingAccessGrantRevocationsAsync(take, ct);

        public Task<bool> TryCreateAsync(
            ReportingDeliveryJobRecord job,
            CancellationToken ct = default) =>
            inner.TryCreateAsync(job, ct);

        public Task<IReadOnlyList<ReportingDeliveryJobRecord>> ClaimDueAsync(
            DateTimeOffset nowUtc,
            string leaseOwner,
            TimeSpan leaseDuration,
            int take,
            CancellationToken ct = default) =>
            inner.ClaimDueAsync(nowUtc, leaseOwner, leaseDuration, take, ct);

        public Task<bool> TryUpdateAsync(
            string jobId,
            long expectedVersion,
            ReportingDeliveryJobRecord updatedJob,
            CancellationToken ct = default) =>
            inner.TryUpdateAsync(jobId, expectedVersion, updatedJob, ct);
    }

    private sealed class MutableArtifactCatalog : IReportingArtifactCatalog
    {
        private readonly IReadOnlyList<ReportingRetainedArtifactRecord> _artifacts;

        public MutableArtifactCatalog(ReportingRetainedArtifactRecord artifact)
            : this([artifact])
        {
        }

        public MutableArtifactCatalog(IReadOnlyList<ReportingRetainedArtifactRecord> artifacts)
        {
            _artifacts = artifacts;
        }

        public bool ReturnArtifact { get; set; } = true;

        public ValueTask<ReportingArtifactCatalogWriteResult> AddPackageAsync(
            ReportingRetainedArtifactPackage package,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ReportingArtifactCatalogWriteResult(AlreadyExisted: true));

        public ValueTask<ReportingRetainedArtifactPackage?> GetPackageAsync(
            string tenantId,
            string packageId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var retained = _artifacts
                .Where(artifact =>
                    string.Equals(artifact.Scope.TenantId, tenantId, StringComparison.Ordinal)
                    && string.Equals(artifact.PackageId, packageId, StringComparison.Ordinal))
                .ToArray();
            return ValueTask.FromResult<ReportingRetainedArtifactPackage?>(
                ReturnArtifact
                && retained.Length > 0
                    ? new ReportingRetainedArtifactPackage(packageId, retained.ToImmutableArray())
                    : null);
        }

        public ValueTask<ReportingRetainedArtifactRecord?> GetArtifactAsync(
            string tenantId,
            string packageId,
            string artifactId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var retained = _artifacts.SingleOrDefault(artifact =>
                string.Equals(artifact.Scope.TenantId, tenantId, StringComparison.Ordinal)
                && string.Equals(artifact.PackageId, packageId, StringComparison.Ordinal)
                && string.Equals(artifact.ArtifactId, artifactId, StringComparison.Ordinal));
            return ValueTask.FromResult<ReportingRetainedArtifactRecord?>(
                ReturnArtifact ? retained : null);
        }
    }

    private sealed class MutableArtifactStore : IReportingArtifactStore
    {
        private readonly Dictionary<ReportingArtifactIdentity, byte[]> _contents;
        private readonly ReportingArtifactIdentity _primaryIdentity;
        private readonly DateTimeOffset _storedAtUtc;

        public MutableArtifactStore(
            ReportingArtifactIdentity identity,
            byte[] content,
            DateTimeOffset storedAtUtc)
            : this(
                new Dictionary<ReportingArtifactIdentity, byte[]>
                {
                    [identity] = content
                },
                storedAtUtc)
        {
        }

        public MutableArtifactStore(
            IReadOnlyDictionary<ReportingArtifactIdentity, byte[]> contents,
            DateTimeOffset storedAtUtc)
        {
            contents.Should().NotBeEmpty();
            _contents = contents.ToDictionary(
                static pair => pair.Key,
                static pair => pair.Value.ToArray());
            _primaryIdentity = _contents.Keys.First();
            _storedAtUtc = storedAtUtc;
        }

        public byte[] Content
        {
            get => _contents[_primaryIdentity].ToArray();
            set => _contents[_primaryIdentity] = value.ToArray();
        }

        public Task<ReportingArtifactWriteResult> StoreAsync(
            ReportingArtifactWriteRequest request,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<ReportingArtifactReadResult> ReadAsync(
            ReportingArtifactIdentity requestedIdentity,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            _contents.TryGetValue(requestedIdentity, out var content).Should().BeTrue();
            return Task.FromResult(new ReportingArtifactReadResult(
                requestedIdentity,
                content!.LongLength,
                _storedAtUtc,
                content.ToArray()));
        }
    }

    private sealed class RecordingArtifactAuditStore : IReportingArtifactAuditStore
    {
        private string? _lastHash;

        public List<ReportingArtifactAuditEvent> Events { get; } = [];

        public ValueTask<ReportingArtifactAuditReceipt> AppendAsync(
            ReportingArtifactAuditEvent auditEvent,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Events.Add(auditEvent);
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                    $"{Events.Count}:{auditEvent.EventId}:{_lastHash}")))
                .ToLowerInvariant();
            var receipt = new ReportingArtifactAuditReceipt(
                auditEvent.EventId,
                Events.Count,
                _lastHash,
                hash);
            _lastHash = hash;
            return ValueTask.FromResult(receipt);
        }
    }

    private sealed class AcceptingRelayClient : IReportingHttpRelayClient
    {
        public Task<ReportingHttpRelayResult> SendAsync(
            ReportingHttpRelayMessage message,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(new ReportingHttpRelayResult(
                IsSuccess: true,
                IsTransientFailure: false,
                Code: "ACCEPTED",
                ProviderMessageId: "provider-message-a"));
        }
    }

    private sealed class AcceptingProviderReceiptAuthenticator : IReportingProviderReceiptAuthenticator
    {
        public ValueTask<bool> AuthenticateAsync(
            ReportingProviderReceiptAuthenticationRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(true);
        }
    }

    private sealed class EmptyArtifactCatalog : IReportingArtifactCatalog
    {
        public ValueTask<ReportingArtifactCatalogWriteResult> AddPackageAsync(
            ReportingRetainedArtifactPackage package,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ReportingArtifactCatalogWriteResult(false));

        public ValueTask<ReportingRetainedArtifactRecord?> GetArtifactAsync(
            string tenantId,
            string packageId,
            string artifactId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<ReportingRetainedArtifactRecord?>(null);
    }

    private sealed class EmptyArtifactStore : IReportingArtifactStore
    {
        public Task<ReportingArtifactWriteResult> StoreAsync(
            ReportingArtifactWriteRequest request,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<ReportingArtifactReadResult> ReadAsync(
            ReportingArtifactIdentity identity,
            CancellationToken ct = default) =>
            throw new ReportingArtifactNotFoundException(identity);
    }

    private sealed class ValidArtifactAuditStore : IReportingArtifactAuditStore
    {
        public ValueTask<ReportingArtifactAuditReceipt> AppendAsync(
            ReportingArtifactAuditEvent auditEvent,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ReportingArtifactAuditReceipt(
                auditEvent.EventId,
                1,
                PreviousHash: null,
                new string('a', 64)));
    }
}
