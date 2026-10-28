using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Reporting;
using Meridian.Contracts.Workstation;
using Meridian.Wpf.Features.Reporting;
using Meridian.Wpf.Services;

namespace Meridian.Wpf.Tests.Features.Reporting;

public sealed class ReportRetainedAmountsViewModelTests
{
    private static readonly Guid Book = Guid.Parse("f11f7111-1111-4111-8111-111111111111");
    private static readonly Guid Period = Guid.Parse("f22f7222-2222-4222-8222-222222222222");
    private static readonly Guid OpeningPeriod = Guid.Parse("f33f7333-3333-4333-8333-333333333333");
    private static readonly Guid Journal = Guid.Parse("f44f7444-4444-4444-8444-444444444444");
    private static readonly Guid Line = Guid.Parse("f55f7555-5555-4555-8555-555555555555");
    private static readonly LedgerAmountScopeDto Scope = new("tenant-a", "company-a", "fund-a", Book, Period);
    private static readonly string Hash = new('a', 64);
    private static readonly string AmountId = new('b', 64);
    private static string Subject => $"report:run-a:{AmountId}";
    private static string SourceSubject => $"{Journal:D}:{Line:D}:credit";
    private static string CanonicalSource => $"fund-a:{Book:D}:{OpeningPeriod:D}:{SourceSubject}";
    private static string SourceRoute => $"/workstation/evidence/vault/ev-retained?ledgerAmountSubjectId={Uri.EscapeDataString(Subject)}&ledgerBookId={Book:D}&periodId={Period:D}&fundProfileId=fund-a&expectedContentHash={Hash}";

    [Fact]
    public async Task Catalogue_PreservesExactNegativeDecimalAndHistoricalSourceManifestScope()
    {
        var client = new Client();
        var model = new ReportRetainedAmountsViewModel(client, client);
        model.SetRun(Run());
        await model.RefreshAsync();
        var row = model.Rows.Single();
        row.AmountLabel.Should().Be("-9007199254740993.1234 USD");

        await model.InspectCommand.ExecuteAsync(row);

        client.ProofRequests.Should().Equal(Subject);
        model.ProofDrawer.StatusText.Should().Be("Ready");
        model.ProofDrawer.Title.Should().Contain("-9007199254740993.1234 USD");
        await model.ProofDrawer.OpenManifestCommand.ExecuteAsync(model.ProofDrawer.Evidence.Single());
        client.ManifestRequests.Should().Equal(SourceRoute);
        model.ProofDrawer.ManifestText.Should().Contain(CanonicalSource);
    }

    [Theory]
    [InlineData("tenant")]
    [InlineData("company")]
    [InlineData("book")]
    [InlineData("period")]
    [InlineData("subject")]
    [InlineData("checkpoint")]
    [InlineData("duplicate")]
    [InlineData("missing-lines")]
    public async Task ForeignOrAmbiguousCatalogue_CannotExposeProof(string mismatch)
    {
        var binding = Binding();
        binding = mismatch switch
        {
            "tenant" => binding with { Scope = Scope with { TenantId = "foreign" } },
            "company" => binding with { Scope = Scope with { CompanyId = "foreign" } },
            "book" => binding with { Scope = Scope with { LedgerBookId = Guid.NewGuid() } },
            "period" => binding with { Scope = Scope with { PeriodId = OpeningPeriod } },
            "subject" => binding with { SubjectId = $"report:foreign:{AmountId}" },
            "checkpoint" => binding with { SourceSnapshotHash = new string('c', 64) },
            "missing-lines" => binding with { LedgerEntryIds = [] },
            _ => binding
        };
        var client = new Client { Amounts = mismatch == "duplicate" ? [binding, binding] : [binding] };
        var model = new ReportRetainedAmountsViewModel(client, client);
        model.SetRun(Run());

        await model.RefreshAsync();

        model.Rows.Should().BeEmpty();
        model.StatusText.Should().Contain("blocked");
        client.ProofRequests.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(null)]
    public async Task MissingProofPermission_DoesNotIssueEvidenceRequest(bool? permission)
    {
        var client = new Client { Amounts = [Binding() with { ProofAvailable = permission }] };
        var model = new ReportRetainedAmountsViewModel(client, client);
        model.SetRun(Run());
        await model.RefreshAsync();

        model.InspectCommand.CanExecute(model.Rows.Single()).Should().BeFalse();
        await model.InspectCommand.ExecuteAsync(model.Rows.Single());

        client.ProofRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task LegacyRunWithoutRetainedBindings_IsReviewRequiredWithoutLiveFallback()
    {
        var client = new Client { Amounts = [] };
        var model = new ReportRetainedAmountsViewModel(client, client);
        model.SetRun(Run());
        await model.RefreshAsync();
        model.Rows.Should().BeEmpty();
        model.StatusText.Should().Contain("Review required");
        client.ProofRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task LateCatalogueCannotRestoreRowsAfterRunRevisionOrNavigationChange()
    {
        var pending = new TaskCompletionSource<ApiResponse<ReportLedgerAmountBindingDto[]>>();
        var client = new Client { PendingAmounts = pending.Task };
        var model = new ReportRetainedAmountsViewModel(client, client);
        model.SetRun(Run());
        var read = model.RefreshAsync();
        model.SetRun(Run() with { Version = 2 });
        pending.SetResult(ApiResponse<ReportLedgerAmountBindingDto[]>.Ok([Binding()]));
        await read;
        model.Rows.Should().BeEmpty();
        model.ProofDrawer.IsOpen.Should().BeFalse();
        model.IsLoading.Should().BeFalse();
    }

    [Fact]
    public async Task LateProofCannotOpenAfterRunScopeRoundTrip()
    {
        var pending = new TaskCompletionSource<ApiResponse<EvidencePacketDto>>();
        var client = new Client { PendingProof = pending.Task };
        var model = new ReportRetainedAmountsViewModel(client, client);
        model.SetRun(Run());
        await model.RefreshAsync();
        var oldRow = model.Rows.Single();
        var proof = model.InspectCommand.ExecuteAsync(oldRow);
        model.SetRun(null);
        model.SetRun(Run());
        pending.SetResult(ApiResponse<EvidencePacketDto>.Ok(Packet()));
        await proof;
        model.ProofDrawer.IsOpen.Should().BeFalse();
        model.InspectCommand.CanExecute(oldRow).Should().BeFalse();
    }

    [Theory]
    [InlineData("source-scope")]
    [InlineData("source-line")]
    [InlineData("source-subject")]
    [InlineData("artifact-hash")]
    [InlineData("source-missing")]
    public async Task AlteredContributor_BlocksWholeAmountWithoutManifestRequest(string mismatch)
    {
        var packet = Packet();
        var evidence = packet.LedgerAmount!.Evidence.Single();
        packet = mismatch switch
        {
            "source-scope" => packet with { LedgerAmount = packet.LedgerAmount with { Evidence = [evidence with { SourceScope = Scope with { CompanyId = "foreign" } }] } },
            "source-line" => packet with { LedgerAmount = packet.LedgerAmount with { Evidence = [evidence with { SourceSubjectId = $"{Journal:D}:{Guid.NewGuid():D}:credit" }] } },
            "source-subject" => packet with { Nodes = [packet.Nodes.Single() with { Subject = packet.Subject }] },
            "artifact-hash" => packet with { Nodes = [packet.Nodes.Single() with { ArtifactRefs = [packet.Nodes.Single().ArtifactRefs.Single() with { Hash = new string('c', 64) }] }] },
            _ => packet with { LedgerAmount = packet.LedgerAmount with { Evidence = [] } }
        };
        var client = new Client { Proof = packet };
        var model = new ReportRetainedAmountsViewModel(client, client);
        model.SetRun(Run());
        await model.RefreshAsync();
        await model.InspectCommand.ExecuteAsync(model.Rows.Single());
        model.ProofDrawer.StatusText.Should().Be("Blocked");
        model.ProofDrawer.Evidence.Should().BeEmpty();
        client.ManifestRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task MissingDeclaredJournal_BlocksEvenWhenEveryLineHasSomeSupport()
    {
        var secondJournal = Guid.NewGuid();
        var secondLine = Guid.NewGuid();
        var client = new Client
        {
            Amounts = [Binding() with { JournalEntryIds = [Journal, secondJournal], LedgerEntryIds = [Line, secondLine] }]
        };
        var packet = Packet();
        var item = packet.LedgerAmount!.Evidence.Single();
        client.Proof = packet with
        {
            LedgerAmount = packet.LedgerAmount with
            {
                Evidence = [item, item with { EvidenceId = "second-source", SourceSubjectId = $"{Journal:D}:{secondLine:D}:credit" }]
            }
        };
        var model = new ReportRetainedAmountsViewModel(client, client);
        model.SetRun(Run());
        await model.RefreshAsync();
        await model.InspectCommand.ExecuteAsync(model.Rows.Single());
        model.ProofDrawer.StatusText.Should().Be("Blocked");
        model.ProofDrawer.Evidence.Should().BeEmpty();
        client.ManifestRequests.Should().BeEmpty();
    }

    private static GovernedReportingRunDto Run() => new("run-a", "series-a", 1, "trial-balance", "1",
        new("tenant-a", "organization-a", "company-a", "fund-a", Book.ToString("D"), Period.ToString("D")),
        new("policy", "1", "Private", "operator", true, [], Hash),
        new("snapshot", Hash, "reconciliation", DateTimeOffset.UnixEpoch, SourceCheckpointHash: Hash),
        new("operator", "tenant-a", "organization-a", "company-a", [], "HumanOperator", "run-a", []),
        DateTimeOffset.UnixEpoch, null, "Succeeded", "Draft", 1, null, null, null, []);

    private static ReportLedgerAmountBindingDto Binding() => new(AmountId, "Retained capital",
        -9007199254740993.1234m, "USD", Scope, [Journal], [Line])
        { SubjectId = Subject, SourceSnapshotHash = Hash, ProofAvailable = true };

    private static EvidencePacketDto Packet()
    {
        var now = DateTimeOffset.UtcNow.AddMinutes(-1);
        var id = "retained-source@" + SourceSubject;
        return new(new(Subject, "ledger-amount", "Retained capital", "Reporting", null, "ReportRunGovernance", Book), now,
            [new(id, new(SourceSubject, "ledger-amount", "Historical source", "Accounting", null, "PostedLedger"),
                "source-document", EvidenceStatusDto.Ready, new(now, false, null), "vault", "Retained source",
                [new(id, "source-document", null, SourceRoute, now, Hash, true, "ledger-amount", CanonicalSource)], [])],
            [], new(100, EvidenceStatusDto.Ready, [], [], [], [], []), [], [])
        {
            LedgerAmount = new(Subject, Scope, Binding().Amount, "USD", EvidenceStatusDto.Ready,
                [new(id, "source-document", "Historical retained source", SourceRoute, "vault", now, EvidenceStatusDto.Ready, Hash)
                    { SourceSubjectId = SourceSubject, SourceScope = Scope with { PeriodId = OpeningPeriod } }], [])
        };
    }

    private sealed class Client : IReportRetainedAmountsApiClient, ILedgerReportsApiClient
    {
        public ReportLedgerAmountBindingDto[] Amounts { get; init; } = [Binding()];
        public EvidencePacketDto Proof { get; set; } = Packet();
        public Task<ApiResponse<ReportLedgerAmountBindingDto[]>>? PendingAmounts { get; init; }
        public Task<ApiResponse<EvidencePacketDto>>? PendingProof { get; init; }
        public List<string> ProofRequests { get; } = [];
        public List<string> ManifestRequests { get; } = [];
        public Task<ApiResponse<ReportLedgerAmountBindingDto[]>> GetAmountsAsync(string runId, CancellationToken ct)
            => PendingAmounts ?? Task.FromResult(ApiResponse<ReportLedgerAmountBindingDto[]>.Ok(Amounts));
        public Task<ApiResponse<EvidencePacketDto>> GetAmountProofAsync(string subjectId, Guid ledgerBookId, Guid periodId, string fundProfileId, CancellationToken ct = default)
        {
            ProofRequests.Add(subjectId);
            return PendingProof ?? Task.FromResult(ApiResponse<EvidencePacketDto>.Ok(Proof));
        }
        public Task<ApiResponse<JsonDocument>> GetRetainedManifestAsync(string route, CancellationToken ct = default)
        {
            ManifestRequests.Add(route);
            return Task.FromResult(ApiResponse<JsonDocument>.Ok(JsonSerializer.SerializeToDocument(new
            {
                tenantId = "tenant-a", scope = "company-a", subject = new { subjectKind = "ledger-amount", subjectId = CanonicalSource },
                vaultIdentity = new { contentHashSha256 = Hash }
            })));
        }
        public Task<ApiResponse<List<LedgerBookDto>>> GetBooksAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ApiResponse<List<LedgerPeriodDto>>> GetPeriodsAsync(Guid? ledgerBookId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ApiResponse<List<LedgerPeriodTrialBalanceLineDto>>> GetTrialBalanceAsync(Guid periodId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ApiResponse<LedgerPeriodPnlSummaryDto>> GetPnlSummaryAsync(Guid periodId, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
