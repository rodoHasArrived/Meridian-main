using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.Identity.Auth;
using Meridian.Ui.Shared.Endpoints;
using Meridian.Ui.Shared.Evidence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Tests.Ui;

public sealed class SinglePrincipalAttestationEndpointTests
{
    private const string Route = "/api/workstation/evidence/attestations";
    private const UserPermission Read = UserPermission.ViewReporting | UserPermission.ViewPreparerAttestations;
    private const UserPermission All = Read | UserPermission.ManageReporting
        | UserPermission.ManageSinglePrincipalDeclarations | UserPermission.RecordPreparerAttestations;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public async Task DraftAndEvidenceCapabilitiesRequireTheirOwnDomainGrant_AndCorrectiveRevocationNeedsNoFreshRecordGrant()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Permissions = Read | UserPermission.RecordPreparerAttestations | UserPermission.ModifySecurityMaster;
        var workspace = await fixture.WorkspaceAsync();
        workspace.CanAttest.Should().BeFalse();
        workspace.CanRevoke.Should().BeFalse();
        workspace.CanAttestDrafts.Should().BeTrue();
        workspace.CanRevokeDrafts.Should().BeTrue();

        fixture.Permissions = Read | UserPermission.ModifySecurityMaster;
        workspace = await fixture.WorkspaceAsync();
        workspace.CanAttestDrafts.Should().BeFalse();
        workspace.CanRevokeDrafts.Should().BeTrue();

        fixture.Permissions = Read | UserPermission.RecordPreparerAttestations | UserPermission.ManageReporting;
        workspace = await fixture.WorkspaceAsync();
        workspace.CanAttest.Should().BeTrue();
        workspace.CanRevoke.Should().BeTrue();
        workspace.CanAttestDrafts.Should().BeFalse();
        workspace.CanRevokeDrafts.Should().BeFalse();
    }

    [Fact]
    public async Task ExplicitGrantsAndFeatureGateAreRequired_AndDefaultRolesDoNotAcquireGrants()
    {
        await using var fixture = await Fixture.CreateAsync(enabled: false);
        var state = await fixture.WorkspaceAsync();
        state.State.PolicyEnabled.Should().BeFalse();
        (await fixture.Client.PostAsJsonAsync(Route + "/declaration", Declaration())).StatusCode
            .Should().Be(HttpStatusCode.BadRequest);
        foreach (var role in Enum.GetValues<UserRole>())
        {
            var permissions = RolePermissions.For(role);
            (permissions & (UserPermission.ManageSinglePrincipalDeclarations
                | UserPermission.RecordPreparerAttestations | UserPermission.ViewPreparerAttestations))
                .Should().Be(UserPermission.None);
        }
        fixture.Permissions = UserPermission.ViewReporting;
        (await fixture.Client.GetAsync(Route)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AcknowledgmentIsSeparateFromDocumentReview_AndRetriesSurviveWithdrawalAndLostWriteGrants()
    {
        await using var fixture = await Fixture.CreateAsync();
        var intake = await fixture.IntakeAsync();
        (await fixture.Client.PostAsJsonAsync(Route + "/declaration", Declaration())).EnsureSuccessStatusCode();
        var subject = await fixture.SubjectAsync(intake);
        var command = Acknowledgment(subject);
        var response = await fixture.Client.PostAsJsonAsync(Route + "/receipts", command);
        response.EnsureSuccessStatusCode();
        var receipt = (await response.Content.ReadFromJsonAsync<PreparerAttestationReceiptDto>(Json))!;
        receipt.AttestedBy.Should().Be("principal-a");
        receipt.SourceActor.Should().Be("external-importer");
        receipt.SourceSubjectId.Should().Be("account-a");
        receipt.Disclosure.Should().Be(SinglePrincipalAttestationPolicy.Disclosure);
        var document = await fixture.Artifacts.TryGetVaultIdentityAsync(intake.VaultIdentity.VaultId, "tenant-a", "company-a");
        document!.Documents.Single().ReviewerState.Status.Should().Be(EvidenceDocumentReviewStatusDto.Unreviewed);

        (await fixture.Client.PostAsJsonAsync(Route + "/declaration", Declaration(1, false))).EnsureSuccessStatusCode();
        fixture.Permissions = Read;
        var retry = await fixture.Client.PostAsJsonAsync(Route + "/receipts", command);
        retry.EnsureSuccessStatusCode();
        (await retry.Content.ReadFromJsonAsync<PreparerAttestationReceiptDto>(Json))!.ReceiptId.Should().Be(receipt.ReceiptId);
        (await fixture.WorkspaceAsync()).State.Receipts.Should().ContainSingle();
        (await fixture.Client.PostAsJsonAsync(Route + "/receipts", command with { CommandId = Guid.NewGuid() }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData(OperationsActionOriginDto.AutomationSuggestion)]
    [InlineData(OperationsActionOriginDto.AssistantDraft)]
    [InlineData(OperationsActionOriginDto.AutomationAssistant)]
    public async Task TrustedSessionScopeAndOriginCannotBeReplacedByBodyAssertions(OperationsActionOriginDto declaredOrigin)
    {
        await using var fixture = await Fixture.CreateAsync();
        var intake = await fixture.IntakeAsync();
        var subject = await fixture.SubjectAsync(intake);
        (await fixture.Client.PostAsJsonAsync(Route + "/declaration",
            Declaration() with { Origin = declaredOrigin })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await fixture.Client.PostAsJsonAsync(Route + "/declaration", Declaration())).EnsureSuccessStatusCode();
        (await fixture.Client.PostAsJsonAsync(Route + "/receipts",
            Acknowledgment(subject) with { Origin = declaredOrigin })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var accepted = await fixture.Client.PostAsJsonAsync(Route + "/receipts", Acknowledgment(subject));
        accepted.EnsureSuccessStatusCode();
        var receipt = (await accepted.Content.ReadFromJsonAsync<PreparerAttestationReceiptDto>(Json))!;
        (await fixture.Client.PostAsJsonAsync(Route + "/revocations",
            new RevokePreparerAttestationRequestDto(Guid.NewGuid(), receipt.ReceiptId, 1, "Correcting my assertion", declaredOrigin)))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await fixture.WorkspaceAsync()).State.Revocations.Should().BeEmpty();
        fixture.TenantId = "foreign-tenant";
        (await fixture.Client.GetAsync(Route + $"/subjects?vaultId={intake.VaultIdentity.VaultId}&documentId={intake.Document!.DocumentId}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await fixture.WorkspaceAsync()).State.Declaration.Should().BeNull();
        fixture.TenantId = null;
        // The shared workstation resolver deliberately uses authenticated company scope as
        // the tenant when a session has no separate tenant ID; it is not client-supplied scope.
        (await fixture.WorkspaceAsync()).State.Scope.TenantId.Should().Be("company-a");
        fixture.CompanyId = null;
        (await fixture.Client.GetAsync(Route)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task StaleSubjectIsRejected_AndOriginalAttesterCanRevokeAfterWithdrawalAndSourceCorruption()
    {
        await using var fixture = await Fixture.CreateAsync();
        var intake = await fixture.IntakeAsync();
        (await fixture.Client.PostAsJsonAsync(Route + "/declaration", Declaration())).EnsureSuccessStatusCode();
        var subject = await fixture.SubjectAsync(intake);
        (await fixture.Client.PostAsJsonAsync(Route + "/receipts",
            Acknowledgment(subject) with { SubjectRevision = "stale" })).StatusCode.Should().Be(HttpStatusCode.Conflict);
        var response = await fixture.Client.PostAsJsonAsync(Route + "/receipts", Acknowledgment(subject));
        response.EnsureSuccessStatusCode();
        var receipt = (await response.Content.ReadFromJsonAsync<PreparerAttestationReceiptDto>(Json))!;
        (await fixture.Client.PostAsJsonAsync(Route + "/declaration", Declaration(1, false))).EnsureSuccessStatusCode();
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, intake.VaultIdentity.Artifacts.Single().RelativePath), "corrupt");
        fixture.Permissions = Read | UserPermission.ManageReporting;
        var revocation = new RevokePreparerAttestationRequestDto(Guid.NewGuid(), receipt.ReceiptId, 1, "The preparer assertion was erroneous");
        (await fixture.Client.PostAsJsonAsync(Route + "/revocations", revocation)).EnsureSuccessStatusCode();
        var state = (await fixture.WorkspaceAsync()).State;
        state.Receipts.Should().ContainSingle();
        state.Revocations.Should().ContainSingle();
        state.Declaration!.Enabled.Should().BeFalse();
    }

    [Fact]
    public async Task ReceiptHistoryAndReplayRespectCurrentSourceReadPermission()
    {
        await using var fixture = await Fixture.CreateAsync();
        var intake = await fixture.IntakeAsync(subjectKind: "ledger-amount");
        fixture.Permissions = All | UserPermission.ViewLedgerReports;
        (await fixture.Client.PostAsJsonAsync(Route + "/declaration", Declaration())).EnsureSuccessStatusCode();
        var command = Acknowledgment(await fixture.SubjectAsync(intake));
        (await fixture.Client.PostAsJsonAsync(Route + "/receipts", command)).EnsureSuccessStatusCode();
        fixture.Permissions = All;
        var state = (await fixture.WorkspaceAsync()).State;
        state.Receipts.Should().BeEmpty();
        state.AuditTrail.Should().OnlyContain(item => item.ReceiptId == null);
        (await fixture.Client.PostAsJsonAsync(Route + "/receipts", command)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task OriginalProofExportIsStableAfterWithdrawalRevocationAndSourceCorruption()
    {
        await using var fixture = await Fixture.CreateAsync();
        var intake = await fixture.IntakeAsync();
        (await fixture.Client.PostAsJsonAsync(Route + "/declaration", Declaration())).EnsureSuccessStatusCode();
        var response = await fixture.Client.PostAsJsonAsync(Route + "/receipts", Acknowledgment(await fixture.SubjectAsync(intake)));
        response.EnsureSuccessStatusCode();
        var receipt = (await response.Content.ReadFromJsonAsync<PreparerAttestationReceiptDto>(Json))!;
        var exportRoute = Route + $"/receipts/{receipt.ReceiptId:D}/export";
        var original = await fixture.Client.GetAsync(exportRoute);
        original.EnsureSuccessStatusCode();
        var originalBytes = await original.Content.ReadAsByteArrayAsync();
        var etag = original.Headers.ETag;
        original.Headers.CacheControl!.NoStore.Should().BeTrue();
        using (var proof = JsonDocument.Parse(originalBytes))
        {
            proof.RootElement.GetProperty("disclosure").GetString().Should().Be(SinglePrincipalAttestationPolicy.Disclosure);
            proof.RootElement.GetProperty("receiptReference").GetString().Should().StartWith("preparer-attestation:v1:");
            proof.RootElement.GetProperty("receipt").GetProperty("receiptId").GetGuid().Should().Be(receipt.ReceiptId);
            proof.RootElement.GetProperty("declaration").GetProperty("enabled").GetBoolean().Should().BeTrue();
            proof.RootElement.TryGetProperty("revocations", out _).Should().BeFalse();
        }
        (await fixture.WorkspaceAsync()).State.AuditTrail.Should().HaveCount(2, "a read-only proof export creates no authority or success event");
        (await fixture.Client.PostAsJsonAsync(Route + "/declaration", Declaration(1, false))).EnsureSuccessStatusCode();
        (await fixture.Client.PostAsJsonAsync(Route + "/revocations", new RevokePreparerAttestationRequestDto(
            Guid.NewGuid(), receipt.ReceiptId, 1, "Correcting the original assertion"))).EnsureSuccessStatusCode();
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, intake.VaultIdentity.Artifacts.Single().RelativePath), "corrupt");
        fixture.Permissions = Read;
        var historical = await fixture.Client.GetAsync(exportRoute);
        historical.EnsureSuccessStatusCode();
        (await historical.Content.ReadAsByteArrayAsync()).Should().Equal(originalBytes);
        historical.Headers.ETag.Should().Be(etag);
        var state = (await fixture.WorkspaceAsync()).State;
        state.Declaration!.Enabled.Should().BeFalse();
        state.Revocations.Should().ContainSingle();
        fixture.TenantId = "foreign-tenant";
        (await fixture.Client.GetAsync(exportRoute)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ProofExportStillRequiresAnInteractiveAuthorizedReader()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.IsApiKey = true;
        (await fixture.Client.GetAsync(Route + $"/receipts/{Guid.NewGuid():D}/export"))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        fixture.IsApiKey = false;
        fixture.Permissions = UserPermission.ViewReporting;
        (await fixture.Client.GetAsync(Route + $"/receipts/{Guid.NewGuid():D}/export"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private static ChangeSinglePrincipalDeclarationRequestDto Declaration(long revision = 0, bool enabled = true) => new(
        Guid.NewGuid(), revision, enabled, "principal-a", SinglePrincipalAttestationPolicy.Version, "I am the principal for this scope");

    private static RecordPreparerAttestationRequestDto Acknowledgment(SinglePrincipalAttestationSubjectSnapshotDto subject) => new(
        Guid.NewGuid(), 1, SinglePrincipalAttestationPolicy.EvidenceAcknowledgment, subject.SubjectKind,
        subject.SubjectId, subject.Revision, subject.Hash, "Inspected retained source and its checks", SinglePrincipalAttestationPolicy.Confirmation);

    private sealed class Fixture : IAsyncDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "meridian-attestation-http-" + Guid.NewGuid().ToString("N"));
        public UserPermission Permissions { get; set; } = All;
        public string? TenantId { get; set; } = "tenant-a";
        public string? CompanyId { get; set; } = "company-a";
        public bool IsApiKey { get; set; }
        public WebApplication App { get; private set; } = null!;
        public HttpClient Client { get; private set; } = null!;
        public IEvidenceArtifactStore Artifacts => App.Services.GetRequiredService<IEvidenceArtifactStore>();

        public static async Task<Fixture> CreateAsync(bool enabled = true)
        {
            var fixture = new Fixture();
            Directory.CreateDirectory(fixture.Root);
            var configPath = Path.Combine(fixture.Root, "appsettings.json");
            await File.WriteAllTextAsync(configPath, "{\"DataRoot\":\".\"}");
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SinglePrincipalAttestation:EnableEvidenceAcknowledgments"] = enabled.ToString()
            });
            builder.Services.AddSingleton(new Meridian.Application.UI.ConfigStore(configPath));
            builder.Services.AddEvidenceArtifactStorage();
            builder.Services.AddRateLimiter(options => options.AddPolicy(UiEndpoints.MutationRateLimitPolicy,
                _ => RateLimitPartition.GetNoLimiter("attestation-tests")));
            var app = builder.Build();
            fixture.App = app;
            app.Use(async (context, next) =>
            {
                context.Items[LoginSessionMiddleware.CurrentUserKey] = "principal-a";
                context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = fixture.Permissions;
                if (fixture.IsApiKey)
                    context.Items[ApiKeyMiddleware.ApiKeyPrincipalKey] = new object();
                if (fixture.CompanyId is not null)
                    context.Items[LoginSessionMiddleware.CurrentUserCompanyIdKey] = fixture.CompanyId;
                if (fixture.TenantId is not null)
                    context.Items[LoginSessionMiddleware.CurrentTenantIdKey] = fixture.TenantId;
                await next();
            });
            app.UseRouting();
            app.UseRateLimiter();
            typeof(EvidenceEndpoints).GetMethod("MapSinglePrincipalAttestationEndpoints", BindingFlags.NonPublic | BindingFlags.Static)!
                .Invoke(null, [app.MapGroup("/api/workstation/evidence"), Json]);
            await app.StartAsync();
            fixture.Client = app.GetTestClient();
            return fixture;
        }

        public async Task<EvidenceVaultIntakeResponseDto> IntakeAsync(string subjectKind = "account") =>
            await Artifacts.WriteIntakeArtifactAsync(new(subjectKind, "account-a", "ManualUpload", "source.json",
                Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"amount\":100}")), "application/json",
                "external-provider", "source-a", "external-importer",
                ExtractedFields: [new("amount", "100", "100", 1m, "Manual", EvidenceStatusDto.Ready, null, "account", "account-a")])
            {
                Actor = "external-importer",
                TenantId = "tenant-a",
                Scope = "company-a",
                ExtractionStatus = EvidenceExtractionStatusDto.Extracted
            });

        public async Task<SinglePrincipalAttestationSubjectSnapshotDto> SubjectAsync(EvidenceVaultIntakeResponseDto intake) =>
            (await Client.GetFromJsonAsync<SinglePrincipalAttestationSubjectSnapshotDto>(Route
                + $"/subjects?vaultId={intake.VaultIdentity.VaultId}&documentId={intake.Document!.DocumentId}", Json))!;

        public async Task<SinglePrincipalAttestationWorkspaceDto> WorkspaceAsync() =>
            (await Client.GetFromJsonAsync<SinglePrincipalAttestationWorkspaceDto>(Route, Json))!;

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await App.DisposeAsync();
            Directory.Delete(Root, recursive: true);
        }
    }
}
