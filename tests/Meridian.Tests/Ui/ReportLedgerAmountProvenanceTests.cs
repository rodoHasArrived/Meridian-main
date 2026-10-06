using System.Collections.Immutable;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.Identity.Auth;
using Meridian.Ledger;
using Meridian.Reporting;
using Meridian.Storage.Ledger;
using Meridian.Ui.Shared.Endpoints;
using Meridian.Ui.Shared.Evidence;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Meridian.Tests.Ui;

public sealed class ReportLedgerAmountProvenanceTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static IEnumerable<object[]> ArtifactFormatCases() => new[]
    {
        ReportingOutputFormatDto.Pdf, ReportingOutputFormatDto.Xlsx, ReportingOutputFormatDto.Csv,
        ReportingOutputFormatDto.EvidenceVault, ReportingOutputFormatDto.ClientPackage
    }.Select(format => new object[] { format });

    public static IEnumerable<object[]> ArtifactPopulationTamperCases()
    {
        string[] conditions = ["missing-population", "missing-marker", "changed-payload",
            "rehashed-payload-old-checkpoint", "changed-certified-rows", "changed-declared-counts",
            "missing-population-and-marker", "downgraded-checkpoint-without-resigning"];
        foreach (var format in ArtifactFormatCases())
            foreach (var condition in conditions)
                yield return [format[0], condition];
    }

    [Theory]
    [MemberData(nameof(ArtifactFormatCases))]
    public async Task CertifiedArtifacts_AllFormatsProduceFromAValidatedRetainedPopulation(ReportingOutputFormatDto format)
    {
        using var fixture = new Fixture();
        await fixture.CaptureAsync();
        var manifest = fixture.BuildManifest(fixture.Snapshot, format);
        Action validate = () => ReportingCertifiedManifestValidation.Validate(manifest);
        validate.Should().NotThrow();

        var production = await new DeterministicReportingCertifiedArtifactProducer().ProduceAsync(manifest);

        production.Certification.IsAuthoritative.Should().BeTrue();
        production.Certification.SourceCheckpointHash.Should().Be(manifest.AuthoritativeSource!.CheckpointHash);
        production.Artifacts.Select(artifact => artifact.ArtifactId).Should().Equal(manifest.Artifacts);
        production.Artifacts.Should().OnlyContain(artifact => !artifact.Content.IsEmpty);
        fixture.LiveJournals.VerifyNoOtherCalls();
    }

    [Theory]
    [MemberData(nameof(ArtifactFormatCases))]
    public async Task CertifiedArtifacts_LegacyManifestWithoutPopulationOrMarkerPreservesCompatibility(ReportingOutputFormatDto format)
    {
        using var fixture = new Fixture();
        await fixture.CaptureAsync();
        var manifest = fixture.BuildManifest(fixture.Snapshot, format);
        var legacySource = LegacyCheckpoint(manifest.AuthoritativeSource!);
        manifest = manifest with
        {
            AuthoritativeSource = legacySource,
            CertifiedSnapshot = manifest.CertifiedSnapshot! with { SourceCheckpointId = legacySource.CheckpointId }
        };
        manifest = manifest with
        {
            CertifiedSnapshot = manifest.CertifiedSnapshot! with
            {
                SnapshotHash = ReportingCertifiedManifestValidation.ComputeSnapshotHash(manifest)
            }
        };
        Action validate = () => ReportingCertifiedManifestValidation.Validate(manifest);
        validate.Should().NotThrow();

        var production = await new DeterministicReportingCertifiedArtifactProducer().ProduceAsync(manifest);

        production.Artifacts.Select(artifact => artifact.ArtifactId).Should().Equal(manifest.Artifacts);
        production.Artifacts.Should().OnlyContain(artifact => !artifact.Content.IsEmpty);
        fixture.LiveJournals.VerifyNoOtherCalls();
    }

    [Theory]
    [MemberData(nameof(ArtifactPopulationTamperCases))]
    public async Task CertifiedArtifacts_AllFormatsRejectAlteredRetainedPopulationBeforeRendering(
        ReportingOutputFormatDto format, string condition)
    {
        using var fixture = new Fixture();
        await fixture.CaptureAsync();
        var manifest = fixture.BuildManifest(fixture.Snapshot, format);
        var source = manifest.AuthoritativeSource!;
        var retained = source.LedgerPopulation!;
        var changed = (fixture.Snapshot with { PeriodVersion = fixture.Snapshot.PeriodVersion + 1 }).Retain();
        switch (condition)
        {
            case "missing-population":
                source = source with { LedgerPopulation = null };
                break;
            case "missing-marker":
                source = source with
                {
                    EvidenceIds = source.EvidenceIds
                        .Where(id => !id.StartsWith("ledger-population:", StringComparison.Ordinal)).ToImmutableArray()
                };
                break;
            case "changed-payload":
                source = source with { LedgerPopulation = retained with { PayloadJson = changed.PayloadJson } };
                break;
            case "rehashed-payload-old-checkpoint":
                source = source with
                {
                    LedgerPopulation = changed,
                    EvidenceIds = source.EvidenceIds
                        .Where(id => !id.StartsWith("ledger-population:", StringComparison.Ordinal))
                        .Append($"ledger-population:{changed.SnapshotId}:{changed.ContentHashSha256}").ToImmutableArray()
                };
                break;
            case "changed-certified-rows":
                var row = new Dictionary<string, string>(manifest.CertifiedDatasetRows[0]) { ["netAmount"] = "999" };
                manifest = manifest with { CertifiedDatasetRows = manifest.CertifiedDatasetRows.SetItem(0, row) };
                manifest = manifest with
                {
                    CertifiedSnapshot = manifest.CertifiedSnapshot! with
                    {
                        SnapshotHash = ReportingCertifiedManifestValidation.ComputeSnapshotHash(manifest)
                    }
                };
                break;
            case "changed-declared-counts":
                source = source with { LedgerPopulation = retained with { LedgerLineCount = retained.LedgerLineCount + 1 } };
                break;
            case "missing-population-and-marker":
                source = source with
                {
                    LedgerPopulation = null,
                    EvidenceIds = source.EvidenceIds
                        .Where(id => !id.StartsWith("ledger-population:", StringComparison.Ordinal)).ToImmutableArray()
                };
                break;
            case "downgraded-checkpoint-without-resigning":
                source = LegacyCheckpoint(source);
                manifest = manifest with
                {
                    CertifiedSnapshot = manifest.CertifiedSnapshot! with { SourceCheckpointId = source.CheckpointId }
                };
                break;
        }
        manifest = manifest with { AuthoritativeSource = source };
        Action validate = () => ReportingCertifiedManifestValidation.Validate(manifest);
        var produce = () => new DeterministicReportingCertifiedArtifactProducer().ProduceAsync(manifest).AsTask();

        validate.Should().Throw<InvalidDataException>();
        await produce.Should().ThrowAsync<ReportingGovernanceException>();
        fixture.LiveJournals.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RetainedBalance_BindsOpeningAndCurrentActivityToExactJournalsAndSourceScopes()
    {
        using var fixture = new Fixture();
        await fixture.CaptureAsync();

        var amount = fixture.Service.List(Fixture.RunId, fixture.Access)
            .Single(binding => binding.Label == "Cash");
        var packet = await fixture.Service.GetPacketAsync(amount.SubjectId!, fixture.Scope, fixture.Access);

        amount.Amount.Should().Be(100m, "the prior-period debit of 125 and current credit of 25 support the balance");
        amount.JournalEntryIds.Should().Equal(fixture.Opening.Entry.JournalEntryId, fixture.Current.Entry.JournalEntryId);
        amount.LedgerEntryIds.Should().Equal(fixture.OpeningCash.EntryId, fixture.CurrentCash.EntryId);
        amount.SourceSnapshotHash.Should().Be(fixture.Manifest.AuthoritativeSource!.CheckpointHash);
        packet!.LedgerAmount!.Amount.Should().Be(amount.Amount);
        packet.LedgerAmount.Status.Should().Be(EvidenceStatusDto.Ready);
        packet.Completeness.Status.Should().Be(EvidenceStatusDto.Ready);
        packet.ProofChain!.Status.Should().Be(EvidenceStatusDto.Ready);
        packet.LedgerAmount.Evidence.Select(item => item.EvidenceId).Should().BeEquivalentTo(new[]
        {
            "opening-source", "current-source",
            $"journal-line:{fixture.OpeningSubject}", $"journal-line:{fixture.CurrentSubject}"
        });
        var opening = packet.LedgerAmount.Evidence.Single(item => item.EvidenceId == "opening-source");
        opening.SourceSubjectId.Should().Be(fixture.OpeningSubject);
        opening.SourceScope.Should().Be(fixture.Scope with { PeriodId = fixture.Opening.PeriodId });
        var current = packet.LedgerAmount.Evidence.Single(item => item.EvidenceId == "current-source");
        current.SourceSubjectId.Should().Be(fixture.CurrentSubject);
        current.SourceScope.Should().Be(fixture.Scope);
        opening.Route.Should().Contain(Uri.EscapeDataString(amount.SubjectId!))
            .And.Contain($"periodId={fixture.Scope.PeriodId:D}");
        fixture.LiveJournals.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("missing-source")]
    [InlineData("changed-source")]
    [InlineData("missing-manifest")]
    [InlineData("changed-digest")]
    [InlineData("missing-vault")]
    [InlineData("foreign-source-scope")]
    [InlineData("unreviewed")]
    public async Task UnverifiableContributor_BlocksTheWholeBalanceAndWithholdsEveryEvidenceRoute(string condition)
    {
        using var fixture = new Fixture();
        await fixture.CaptureAsync(condition);
        if (condition == "missing-source")
            File.Delete(fixture.CurrentSourcePath!);
        if (condition == "changed-source")
            await File.WriteAllTextAsync(fixture.CurrentSourcePath!, "altered source bytes");
        if (condition == "missing-manifest")
            File.Delete(fixture.CurrentManifestPath!);

        var packet = await fixture.Service.GetPacketAsync(fixture.ReportSubject, fixture.Scope, fixture.Access);

        AssertBlocked(packet);
        packet!.LedgerAmount!.Amount.Should().Be(100m);
        fixture.LiveJournals.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task BindingBuilder_PreservesNegativeBalancesAndExactOpeningAndCurrentLineIdentities()
    {
        using var fixture = new Fixture();
        await fixture.CaptureAsync();
        var current = fixture.NewRecord(fixture.Scope.PeriodId, fixture.Current.Entry.Timestamp, 2, -200m,
            fixture.Current.Entry.Metadata.EvidenceReferences, fixture.Current.Entry.JournalEntryId, fixture.CurrentCash.EntryId);
        var snapshot = fixture.Snapshot with { Journals = [fixture.Opening, current] };

        var amount = ReportAmountBindingBuilder.Build(snapshot).Single(binding => binding.Label == "Cash");

        amount.Amount.Should().Be(-75m, "asset credit activity must retain its sign when it exceeds the opening debit balance");
        amount.JournalEntryIds.Should().Equal(fixture.Opening.Entry.JournalEntryId, current.Entry.JournalEntryId);
        amount.LedgerEntryIds.Should().Equal(fixture.OpeningCash.EntryId, fixture.CurrentCash.EntryId);
        fixture.LiveJournals.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task PostingAfterCapture_CannotChangeRetainedReportAmountsOrQueryTheLiveLedger()
    {
        using var fixture = new Fixture();
        await fixture.CaptureAsync();
        var before = fixture.Service.List(Fixture.RunId, fixture.Access).Single(item => item.Label == "Cash");
        // A post-capture source now contains another same-account journal and a new global sequence.
        // The strict live store would fail the test if proof attempted to substitute its population.
        fixture.LiveRecords.Add(fixture.NewRecord(fixture.Scope.PeriodId,
            new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero), 3, 900m, []));

        var after = fixture.Service.List(Fixture.RunId, fixture.Access).Single(item => item.Label == "Cash");
        var packet = await fixture.Service.GetPacketAsync(after.SubjectId!, fixture.Scope, fixture.Access);

        after.Amount.Should().Be(before.Amount).And.Be(100m);
        after.LedgerEntryIds.Should().Equal(before.LedgerEntryIds);
        packet!.LedgerAmount!.Status.Should().Be(EvidenceStatusDto.Ready);
        packet.LedgerAmount.Amount.Should().Be(100m);
        var subsequentPostedSubject = Fixture.Subject(fixture.LiveRecords[^1].Entry.Lines[0]);
        packet.LedgerAmount.Evidence.Should().NotContain(item => item.SourceSubjectId == subsequentPostedSubject);
        fixture.LiveJournals.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("amount")]
    [InlineData("line-id")]
    [InlineData("foreign-amount-scope")]
    [InlineData("currency")]
    public async Task RehashedBindingThatDoesNotReproduceTheLedger_IsVisiblyBlocked(string alteration)
    {
        using var fixture = new Fixture();
        await fixture.CaptureAsync();
        var index = fixture.Snapshot.ReportAmounts.IndexOf(fixture.CashAmount);
        var changed = alteration switch
        {
            "amount" => fixture.CashAmount with { Amount = 999m },
            "line-id" => fixture.CashAmount with { LedgerEntryIds = [Guid.NewGuid()] },
            "foreign-amount-scope" => fixture.CashAmount with
            {
                Scope = fixture.Scope with { FundProfileId = "fund-beta" }
            },
            _ => fixture.CashAmount with { Currency = "EUR" }
        };
        var modified = fixture.Snapshot with
        {
            ReportAmounts = fixture.Snapshot.ReportAmounts.SetItem(index, changed)
        };
        fixture.Manifest = fixture.BuildManifest(modified);

        var packet = await fixture.Service.GetPacketAsync(fixture.ReportSubject, fixture.Scope, fixture.Access);

        AssertBlocked(packet);
        Action list = () => fixture.Service.List(Fixture.RunId, fixture.Access);
        list.Should().Throw<ReportingGovernanceException>();
        fixture.LiveJournals.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AlteredPayload_CannotReuseTheOriginalCertifiedCheckpoint(bool recomputePopulationDigest)
    {
        using var fixture = new Fixture();
        await fixture.CaptureAsync();
        var changed = fixture.Snapshot with { PeriodVersion = fixture.Snapshot.PeriodVersion + 1 };
        var retained = changed.Retain();
        var original = fixture.Manifest.AuthoritativeSource!;
        if (!recomputePopulationDigest)
            retained = original.LedgerPopulation! with { PayloadJson = retained.PayloadJson };
        var source = original with
        {
            LedgerPopulation = retained,
            EvidenceIds = original.EvidenceIds.Where(id => !id.StartsWith("ledger-population:", StringComparison.Ordinal))
                .Append($"ledger-population:{retained.SnapshotId}:{retained.ContentHashSha256}").ToImmutableArray()
        };
        fixture.Manifest = fixture.Manifest with { AuthoritativeSource = source };

        var packet = await fixture.Service.GetPacketAsync(fixture.ReportSubject, fixture.Scope, fixture.Access);

        AssertBlocked(packet);
        fixture.LiveJournals.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RehashedCertifiedRowsOutsideRetainedPopulation_BlockAmountProof()
    {
        using var fixture = new Fixture();
        await fixture.CaptureAsync();
        var changedRow = new Dictionary<string, string>(fixture.Manifest.CertifiedDatasetRows[0])
        {
            ["netAmount"] = "999"
        };
        var changed = fixture.Manifest with
        {
            CertifiedDatasetRows = fixture.Manifest.CertifiedDatasetRows.SetItem(0, changedRow)
        };
        fixture.Manifest = changed with
        {
            CertifiedSnapshot = changed.CertifiedSnapshot! with
            {
                SnapshotHash = ReportingCertifiedManifestValidation.ComputeSnapshotHash(changed)
            }
        };

        var packet = await fixture.Service.GetPacketAsync(fixture.ReportSubject, fixture.Scope, fixture.Access);

        AssertBlocked(packet);
        Action list = () => fixture.Service.List(Fixture.RunId, fixture.Access);
        list.Should().Throw<InvalidDataException>();
        fixture.LiveJournals.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("tenant")]
    [InlineData("company")]
    [InlineData("unbound")]
    [InlineData("private-policy")]
    public async Task UnauthorizedReportScope_DeniesBeforeExposingAnAmount(string condition)
    {
        using var fixture = new Fixture();
        await fixture.CaptureAsync();
        var access = condition switch
        {
            "tenant" => fixture.Access with { TenantId = "tenant-beta", HasGlobalOverride = true },
            "company" => fixture.Access with { CompanyId = "company-beta", HasGlobalOverride = true },
            "unbound" => fixture.Access with { RequireBoundScope = false },
            _ => fixture.Access
        };
        if (condition == "private-policy")
            fixture.Manifest = fixture.Manifest with
            {
                ImmutableAccessScope = fixture.Manifest.ImmutableAccessScope! with
                {
                    Mode = ReportingGovernanceAccessMode.Private,
                    OwnerPrincipalId = "other-operator",
                    AllowOwnerAccess = true
                }
            };

        (await fixture.Service.GetPacketAsync(fixture.ReportSubject, fixture.Scope, access)).Should().BeNull();
        Action list = () => fixture.Service.List(Fixture.RunId, access);
        list.Should().Throw<KeyNotFoundException>();
        fixture.LiveJournals.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task AmountEndpoints_UseReportingPermissionAndAuthenticatedScope()
    {
        using var fixture = new Fixture();
        await fixture.CaptureAsync();
        await using var allowed = await fixture.CreateAppAsync(UserPermission.ViewReporting | UserPermission.ViewLedgerReports);
        var amounts = await allowed.GetTestClient().GetFromJsonAsync<ReportLedgerAmountBindingDto[]>(
            fixture.AmountsRoute + "?tenantId=foreign&companyId=foreign", Json);
        amounts!.Single(item => item.Label == "Cash").Amount.Should().Be(100m);
        var packet = await allowed.GetTestClient().GetFromJsonAsync<EvidencePacketDto>(fixture.PacketRoute, Json);
        packet!.LedgerAmount!.Status.Should().Be(EvidenceStatusDto.Ready);
        var openingRoute = packet.LedgerAmount.Evidence.Single(item => item.EvidenceId == "opening-source").Route!;
        (await allowed.GetTestClient().GetAsync(openingRoute)).StatusCode.Should().Be(HttpStatusCode.OK);

        await using var denied = await fixture.CreateAppAsync(UserPermission.ViewLedgerReports);
        (await denied.GetTestClient().GetAsync(fixture.AmountsRoute)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await denied.GetTestClient().GetAsync(fixture.PacketRoute)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await denied.GetTestClient().GetAsync(openingRoute)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await using var reportingOnly = await fixture.CreateAppAsync(UserPermission.ViewReporting);
        (await reportingOnly.GetTestClient().GetAsync(fixture.AmountsRoute)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await reportingOnly.GetTestClient().GetAsync(fixture.PacketRoute)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await reportingOnly.GetTestClient().GetAsync(openingRoute)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await using var foreign = await fixture.CreateAppAsync(UserPermission.ViewReporting, "company-beta");
        var response = await foreign.GetTestClient().GetAsync(fixture.AmountsRoute);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).Should().NotContain("100").And.NotContain("Cash");
        fixture.LiveJournals.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task OpeningSourceRoute_RevalidatesTheCompleteRetainedBalanceBeforeOpeningManifest()
    {
        using var fixture = new Fixture();
        await fixture.CaptureAsync();
        await using var app = await fixture.CreateAppAsync(UserPermission.ViewReporting | UserPermission.ViewLedgerReports);
        var client = app.GetTestClient();
        var packet = await client.GetFromJsonAsync<EvidencePacketDto>(fixture.PacketRoute, Json);
        var route = packet!.LedgerAmount!.Evidence.Single(item => item.EvidenceId == "opening-source").Route!;
        // Changing the other contributor invalidates this formerly verified complete amount.
        await File.WriteAllTextAsync(fixture.CurrentSourcePath!, "changed after drawer opened");

        var response = await client.GetAsync(route);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().Contain("ledger-amount-proof-stale");
        fixture.LiveJournals.VerifyNoOtherCalls();
    }

    private static void AssertBlocked(EvidencePacketDto? packet)
    {
        packet.Should().NotBeNull();
        packet!.LedgerAmount!.Status.Should().Be(EvidenceStatusDto.Blocked);
        packet.Completeness.Status.Should().Be(EvidenceStatusDto.Blocked);
        packet.ProofChain!.Status.Should().Be(EvidenceStatusDto.Blocked);
        packet.LedgerAmount.Evidence.Should().BeEmpty();
        packet.Nodes.SelectMany(node => node.ArtifactRefs).Should().BeEmpty();
        packet.Warnings.Should().NotBeEmpty();
    }

    private static ReportingAuthoritativeSourceCheckpoint LegacyCheckpoint(ReportingAuthoritativeSourceCheckpoint source)
    {
        var checkpointId = "ledger-checkpoint-" + source.CheckpointHash[..32];
        return source with
        {
            CheckpointId = checkpointId,
            LedgerPopulation = null,
            EvidenceIds = source.EvidenceIds
                .Where(id => !id.StartsWith("ledger-population:", StringComparison.Ordinal))
                .Select(id => id == $"reporting-source-checkpoint:{source.CheckpointId}:{source.CheckpointHash}"
                    ? $"reporting-source-checkpoint:{checkpointId}:{source.CheckpointHash}" : id).ToImmutableArray()
        };
    }

    private sealed class Fixture : IDisposable
    {
        public const string RunId = "retained-trial-balance";
        private const string OrganizationId = "organization-alpha";
        private const string TemplateId = "trial-balance";
        private readonly string _root = Path.Combine(Path.GetTempPath(), "report-amount-proof-" + Guid.NewGuid().ToString("N"));
        private readonly Guid _aggregateId = Guid.NewGuid();
        private readonly Guid _openingPeriodId = Guid.NewGuid();
        private readonly Guid _openingJournalId = Guid.NewGuid();
        private readonly Guid _openingCashId = Guid.NewGuid();
        private readonly Guid _currentJournalId = Guid.NewGuid();
        private readonly Guid _currentCashId = Guid.NewGuid();
        private readonly Mock<IReportingRunStore> _runs = new();
        private readonly FileEvidenceArtifactStore _artifacts;
        public Mock<ILedgerJournalStore> LiveJournals { get; } = new(MockBehavior.Strict);
        public List<LedgerJournalEntryRecord> LiveRecords { get; } = [];
        public LedgerAmountScopeDto Scope { get; } = new("tenant-alpha", "company-alpha", "fund-alpha", Guid.NewGuid(), Guid.NewGuid());
        public ReportAccessQueryContext Access => new("controller", CompanyId: Scope.CompanyId,
            TenantId: Scope.TenantId, RequireBoundScope: true);
        public ReportLedgerAmountProvenanceService Service { get; }
        public ReportingLedgerPopulationSnapshot Snapshot { get; private set; } = null!;
        public ReportingOutputManifest Manifest { get; set; } = null!;
        public LedgerJournalEntryRecord Opening { get; private set; } = null!;
        public LedgerJournalEntryRecord Current { get; private set; } = null!;
        public LedgerEntry OpeningCash => Opening.Entry.Lines[0];
        public LedgerEntry CurrentCash => Current.Entry.Lines[0];
        public string OpeningSubject => $"{_openingJournalId:D}:{_openingCashId:D}:debit";
        public string CurrentSubject => $"{_currentJournalId:D}:{_currentCashId:D}:credit";
        public ReportLedgerAmountBindingDto CashAmount => Snapshot.ReportAmounts.Single(item => item.Label == "Cash");
        public string ReportSubject => $"report:{RunId}:{CashAmount.AmountId}";
        public string AmountsRoute => $"/api/fund-structure/reporting/runs/{RunId}/amounts";
        public string PacketRoute => $"/api/workstation/evidence/subjects/ledger-amount/{ReportSubject}/packet"
            + $"?fundProfileId={Scope.FundProfileId}&ledgerBookId={Scope.LedgerBookId:D}&periodId={Scope.PeriodId:D}";
        public string? CurrentSourcePath { get; private set; }
        public string? CurrentManifestPath { get; private set; }

        public Fixture()
        {
            Directory.CreateDirectory(_root);
            _artifacts = new FileEvidenceArtifactStore(_root, NullLogger<FileEvidenceArtifactStore>.Instance);
            LiveJournals.Setup(store => store.QueryAsync(It.IsAny<LedgerJournalEntryQuery>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => LiveRecords);
            _runs.Setup(store => store.GetManifest(It.IsAny<string>(), RunId)).Returns(() => Manifest);
            Service = new ReportLedgerAmountProvenanceService(_runs.Object,
                new PostedLedgerAmountProvenanceService(LiveJournals.Object, null, null, _artifacts));
        }

        public async Task CaptureAsync(string? condition = null)
        {
            var opening = await RetainAsync(OpeningSubject, "opening-source", Scope with { PeriodId = _openingPeriodId });
            var sourceScope = condition == "foreign-source-scope" ? Scope with { FundProfileId = "fund-beta" } : Scope;
            var current = await RetainAsync(CurrentSubject, "current-source", sourceScope);
            if (condition == "changed-digest")
                current = current with { ContentHash = new string('a', 64) };
            if (condition == "missing-vault")
                current = current with { Uri = "vault:" + Guid.NewGuid().ToString("N") };
            if (condition == "unreviewed")
                current = current with { ReviewStatus = "NeedsReview" };
            Opening = NewRecord(_openingPeriodId, new DateTimeOffset(2026, 9, 15, 12, 0, 0, TimeSpan.Zero),
                1, 125m, [opening], _openingJournalId, _openingCashId);
            Current = NewRecord(Scope.PeriodId, new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero),
                2, -25m, [current], _currentJournalId, _currentCashId);
            LiveRecords.AddRange([Opening, Current]);
            var periodStart = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
            var cutoff = new DateTimeOffset(2026, 10, 31, 23, 59, 59, TimeSpan.Zero);
            var rows = Current.Entry.Lines.Select(line => (IReadOnlyDictionary<string, string>)new Dictionary<string, string>
            {
                ["journalEntryId"] = line.JournalEntryId.ToString("D"),
                ["entryId"] = line.EntryId.ToString("D"),
                ["account"] = line.Account.Name,
                ["netAmount"] = (line.Debit - line.Credit).ToString(CultureInfo.InvariantCulture)
            }).ToImmutableArray();
            Snapshot = new ReportingLedgerPopulationSnapshot(Scope,
                new LedgerReportPackRequest(RunId, Scope.FundProfileId, Scope.PeriodId.ToString("D"),
                    periodStart, cutoff, cutoff, "USD", "controller", DateTimeOffset.UtcNow,
                    lineDimensions: Dimensions), [Opening, Current], [])
            {
                PeriodVersion = 1,
                DatasetRows = rows
            };
            Snapshot = Snapshot with { ReportAmounts = ReportAmountBindingBuilder.Build(Snapshot) };
            Manifest = BuildManifest(Snapshot);
        }

        private LedgerLineDimensionSet Dimensions => new(Scope.FundProfileId,
            OrganizationId: OrganizationId, BookId: Scope.LedgerBookId.ToString("D"));

        public LedgerJournalEntryRecord NewRecord(Guid periodId, DateTimeOffset timestamp, long sequence,
            decimal signedCash, IReadOnlyList<JournalEvidenceReference> evidence, Guid? journalId = null, Guid? cashId = null)
        {
            var id = journalId ?? Guid.NewGuid();
            var amount = Math.Abs(signedCash);
            const string description = "Retained activity";
            var cash = new LedgerEntry(cashId ?? Guid.NewGuid(), id, timestamp,
                new LedgerAccount("Cash", LedgerAccountType.Asset),
                signedCash > 0m ? amount : 0m, signedCash < 0m ? amount : 0m, description, Dimensions);
            var equity = new LedgerEntry(Guid.NewGuid(), id, timestamp,
                new LedgerAccount("Capital", LedgerAccountType.Equity), cash.Credit, cash.Debit, description, Dimensions);
            return new LedgerJournalEntryRecord(new JournalEntry(id, timestamp, description, [cash, equity],
                new JournalEntryMetadata(EvidenceReferences: evidence)), _aggregateId, periodId, null, null,
                sequence, DateTimeOffset.UtcNow, AccountingBasisKindDto.Gaap);
        }

        private async Task<JournalEvidenceReference> RetainAsync(string subjectId, string evidenceId, LedgerAmountScopeDto scope)
        {
            var retainedSubject = PostedLedgerAmountProvenanceService.BuildRetainedSubjectId(subjectId, scope);
            var intake = await _artifacts.WriteIntakeArtifactAsync(new EvidenceVaultIntakeRequestDto(
                "ledger-amount", retainedSubject, "upload", evidenceId + ".txt",
                Convert.ToBase64String(Encoding.UTF8.GetBytes(evidenceId)), "text/plain", "bank")
            {
                TenantId = scope.TenantId,
                Scope = scope.CompanyId,
                Actor = "preparer"
            });
            var review = await _artifacts.ReviewDocumentAsync(intake.VaultIdentity.VaultId, intake.Document!.DocumentId,
                scope.TenantId, scope.CompanyId, new EvidenceVaultDocumentReviewRequestDto(
                    EvidenceDocumentReviewStatusDto.Accepted, "reviewer", ExtractionStatus: EvidenceExtractionStatusDto.Accepted)
                {
                    ConfirmedFields = [new EvidenceDocumentConfirmedFieldDto("amount", "retained", "reviewer", DateTimeOffset.UtcNow)]
                });
            var identity = await _artifacts.TryGetVaultIdentityAsync(intake.VaultIdentity.VaultId, scope.TenantId, scope.CompanyId);
            if (evidenceId == "current-source")
            {
                CurrentManifestPath = Path.Combine(_root, identity!.ManifestPath);
                CurrentSourcePath = Path.Combine(_root, identity.Artifacts[0].RelativePath);
            }
            return new JournalEvidenceReference(evidenceId, "vault:" + identity!.VaultId,
                "source-document", "bank", identity.RetainedAt, "preparer", retainedSubject,
                identity.ContentHashSha256, evidenceId, ReviewStatus: "Accepted", ReviewedBy: "reviewer",
                ReviewedAtUtc: review!.AuditEvent.RecordedAt, SubjectType: "ledger-amount");
        }

        public ReportingOutputManifest BuildManifest(ReportingLedgerPopulationSnapshot snapshot,
            ReportingOutputFormatDto outputFormat = ReportingOutputFormatDto.Pdf)
        {
            var now = DateTimeOffset.UtcNow;
            var asOfDate = DateOnly.FromDateTime(snapshot.Request.AsOf.UtcDateTime);
            var retained = snapshot.Retain();
            var source = new ReportingAuthoritativeSourceCheckpoint("durable-ledger-journal", "source-alpha",
                Scope.TenantId, OrganizationId, Scope.CompanyId, Scope.FundProfileId,
                Scope.LedgerBookId.ToString("D"), Scope.PeriodId.ToString("D"), "Gaap", asOfDate,
                snapshot.Request.AsOf, retained.HighestGlobalSequence, 1, snapshot.DatasetRows.Length,
                "pending", new string('0', 64), now, [])
            { LedgerPopulation = retained };
            var checkpointHash = snapshot.ComputeCheckpointHash(source);
            var checkpointId = ReportingRetainedLedgerPopulationValidation.BuildCheckpointId(checkpointHash);
            source = source with
            {
                CheckpointId = checkpointId,
                CheckpointHash = checkpointHash,
                EvidenceIds = [$"reporting-source-checkpoint:{checkpointId}:{checkpointHash}",
                    $"ledger-population:{retained.SnapshotId}:{retained.ContentHashSha256}"]
            };
            var parameters = new ReportingRunParametersDto(new ReportingRunScopeDto(Scope.FundProfileId),
                Scope.PeriodId.ToString("D"), asOfDate, new ReportingLedgerBookSelectionDto(LedgerBookId: Scope.LedgerBookId),
                ReportingAccountingBasisDto.Gaap, "USD", ReportingConsolidationLevelDto.Fund,
                outputFormat, ReportingFinalityDto.Draft, true, false);
            var certified = new ReportingCertifiedSnapshotScope(Scope.TenantId, OrganizationId, Scope.CompanyId,
                Scope.FundProfileId, Scope.LedgerBookId.ToString("D"), Scope.PeriodId.ToString("D"),
                "snapshot-alpha", new string('0', 64), "reconciliation-alpha", now, checkpointId,
                checkpointHash, new string('c', 64), ReportingCanonicalParameterSerializer.Serialize(parameters),
                ReportingCanonicalParameterSerializer.ComputeHash(parameters));
            var template = new VersionedReportTemplateIdDto(TemplateId, 1);
            var manifest = new ReportingOutputManifest(RunId, TemplateId, asOfDate, ReportingRunStatus.Draft,
                [], [], 1, ReportingRunTrigger.AdHoc, ResolvedTemplate: template, ResolvedParameters: parameters,
                Readiness: new ReportingRunReadinessDto("readiness-alpha", now, template, parameters,
                    ReportingRunReadinessStatusDto.Ready, true, false,
                    [new ReportingRunReadinessCheckDto("source", "Source", ReportingRunReadinessStatusDto.Ready,
                        "Retained ledger source is ready.", 0, true, true, EvidenceReferences: ["source:ready"])], [], new string('d', 64)),
                OperationalScope: new ReportingOperationalScope(Scope.TenantId, OrganizationId, Scope.CompanyId,
                    Scope.FundProfileId, Scope.LedgerBookId.ToString("D"), Scope.PeriodId.ToString("D")),
                ImmutableAccessScope: new ReportingAccessScope("company-reporting", "1",
                    ReportingGovernanceAccessMode.CompanyWide, null, false, [], new string('a', 64)),
                CertifiedSnapshot: certified, AuthoritativeSource: source, CertifiedDatasetRows: snapshot.DatasetRows);
            manifest = manifest with
            {
                Artifacts = ReportingArtifactDeclaration.Build(manifest).Select(artifact => artifact.ArtifactId).ToImmutableArray()
            };
            return manifest with
            {
                CertifiedSnapshot = certified with { SnapshotHash = ReportingCertifiedManifestValidation.ComputeSnapshotHash(manifest) }
            };
        }

        public async Task<WebApplication> CreateAppAsync(UserPermission permission, string? companyId = null)
        {
            var coordinator = new Mock<IReportingGovernanceEndpointCoordinator>(MockBehavior.Strict);
            var run = new GovernedReportingRun(RunId, "series-alpha", 1, TemplateId, "1",
                Manifest.OperationalScope!, Manifest.ImmutableAccessScope!, Manifest.CertifiedSnapshot!,
                new ReportingAuthorityScope("controller", Scope.TenantId, OrganizationId, Scope.CompanyId,
                    [ReportingGovernancePermission.CreateRun], ReportingCommandOrigin.HumanOperator, "request-alpha"),
                DateTimeOffset.UtcNow, null, GovernedReportingExecutionState.Succeeded, GovernedReportingState.Draft,
                1, null, null, null, []);
            coordinator.Setup(service => service.GetAsync(RunId, It.IsAny<ReportingGovernanceCallerContext>(),
                It.IsAny<CancellationToken>())).ReturnsAsync(run);
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Services.AddSingleton(coordinator.Object);
            builder.Services.AddSingleton(_runs.Object);
            builder.Services.AddSingleton(Service);
            builder.Services.AddSingleton<IEvidenceArtifactStore>(_artifacts);
            builder.Services.AddEvidenceWorkflowFabric();
            var app = builder.Build();
            app.Use(async (context, next) =>
            {
                context.Items[LoginSessionMiddleware.CurrentUserKey] = "controller";
                context.Items[LoginSessionMiddleware.CurrentUserRoleKey] = UserRole.ReportingAnalyst;
                context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = permission;
                context.Items[LoginSessionMiddleware.CurrentTenantIdKey] = Scope.TenantId;
                context.Items[LoginSessionMiddleware.CurrentUserCompanyIdKey] = companyId ?? Scope.CompanyId;
                await next();
            });
            app.MapReportingGovernanceEndpoints(Json);
            app.MapEvidenceEndpoints(Json);
            await app.StartAsync();
            return app;
        }

        public static string Subject(LedgerEntry line) => $"{line.JournalEntryId:D}:{line.EntryId:D}:{(line.Debit > 0m ? "debit" : "credit")}";
        public void Dispose() => Directory.Delete(_root, recursive: true);
    }
}
