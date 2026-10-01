using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Meridian.Contracts.Api;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.Wpf.Services;
using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.ViewModels;
using Meridian.Wpf.Views;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed class LedgerAmountProofInteractionTests
{
    [Fact]
    public async Task AmountCommand_UsesExactJournalLineAndFundScopeForSameNameAndSymbol()
    {
        var client = new Client();
        using var model = new PostedLedgerViewModel(client);
        await model.RefreshAsync();
        var alpha = model.JournalLines.Single().DebitProof!;

        await model.OpenAmountProofCommand.ExecuteAsync(alpha);

        client.Requests.Should().ContainSingle().Which.Should().Be((alpha.SubjectId, Client.BookA, Client.PeriodA, "fund-a"));
        model.ProofDrawer.StatusText.Should().Be("Ready");
        model.ProofDrawer.Evidence.Select(item => item.EvidenceId).Should().Equal("retained-fund-a");

        await model.SelectBookCommand.ExecuteAsync(model.Books.Single(book => book.LedgerBookId == Client.BookB));
        model.ProofDrawer.IsOpen.Should().BeFalse();
        model.ProofDrawer.Evidence.Should().BeEmpty();
        var beta = model.JournalLines.Single().DebitProof!;
        beta.AccountName.Should().Be(alpha.AccountName);
        model.JournalLines.Single().Symbol.Should().Be("SAME");
        await model.OpenAmountProofCommand.ExecuteAsync(beta);

        model.ProofDrawer.Evidence.Select(item => item.EvidenceId).Should().Equal("retained-fund-b");
        client.Requests.Last().Should().Be((beta.SubjectId, Client.BookB, Client.PeriodB, "fund-b"));
    }

    [Theory]
    [InlineData(EvidenceStatusDto.Missing)]
    [InlineData(EvidenceStatusDto.Stale)]
    [InlineData(EvidenceStatusDto.ReviewRequired)]
    public async Task UnreadySupport_IsReviewRequiredAndCannotAppearAsProof(EvidenceStatusDto status)
    {
        var client = new Client
        {
            Transform = packet => packet with
            {
                LedgerAmount = packet.LedgerAmount! with
                {
                    Status = EvidenceStatusDto.ReviewRequired,
                    Evidence = [packet.LedgerAmount.Evidence.Single() with { Status = status, Reason = "Support requires review." }]
                }
            }
        };
        using var model = new PostedLedgerViewModel(client);
        await model.RefreshAsync();

        await model.OpenAmountProofCommand.ExecuteAsync(model.JournalLines.Single().DebitProof);

        model.ProofDrawer.StatusText.Should().Be("Review required");
        model.ProofDrawer.Evidence.Should().BeEmpty();
        model.ProofDrawer.Warnings.Should().Contain("Support requires review.");
    }

    [Fact]
    public async Task EmptyReadyPayload_CannotClaimReady()
    {
        var client = new Client { Transform = packet => packet with { LedgerAmount = packet.LedgerAmount! with { Evidence = [] } } };
        using var model = new PostedLedgerViewModel(client);
        await model.RefreshAsync();
        await model.OpenAmountProofCommand.ExecuteAsync(model.JournalLines.Single().DebitProof);

        model.ProofDrawer.StatusText.Should().Be("Review required");
        model.ProofDrawer.Evidence.Should().BeEmpty();
    }

    [Fact]
    public async Task LedgerRecordAlone_IsReviewRequired()
    {
        var client = new Client
        {
            Transform = packet => packet with
            {
                LedgerAmount = packet.LedgerAmount! with
                { Evidence = [packet.LedgerAmount.Evidence.Single() with { Kind = "ledger-record" }] }
            }
        };
        using var model = new PostedLedgerViewModel(client);
        await model.RefreshAsync();
        await model.OpenAmountProofCommand.ExecuteAsync(model.JournalLines.Single().DebitProof);

        model.ProofDrawer.StatusText.Should().Be("Review required");
    }

    [Fact]
    public async Task BlockedEvidenceItem_BlocksTheEntireProof()
    {
        var client = new Client
        {
            Transform = packet => packet with
            {
                LedgerAmount = packet.LedgerAmount! with
                { Evidence = [packet.LedgerAmount.Evidence.Single() with { Status = EvidenceStatusDto.Blocked }] }
            }
        };
        using var model = new PostedLedgerViewModel(client);
        await model.RefreshAsync();
        await model.OpenAmountProofCommand.ExecuteAsync(model.JournalLines.Single().DebitProof);

        model.ProofDrawer.StatusText.Should().Be("Blocked");
        model.ProofDrawer.Evidence.Should().BeEmpty();
    }

    [Fact]
    public async Task FutureRetention_IsReviewRequiredWithoutProof()
    {
        var client = new Client
        {
            Transform = packet => packet with
            {
                LedgerAmount = packet.LedgerAmount! with
                { Evidence = [packet.LedgerAmount.Evidence.Single() with { RetainedAt = DateTimeOffset.UtcNow.AddDays(1) }] }
            }
        };
        using var model = new PostedLedgerViewModel(client);
        await model.RefreshAsync();
        await model.OpenAmountProofCommand.ExecuteAsync(model.JournalLines.Single().DebitProof);

        model.ProofDrawer.StatusText.Should().Be("Review required");
        model.ProofDrawer.Evidence.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OpenManifestCommand_VerifiesExactRetainedScopeBeforeDisplaying(bool foreign)
    {
        var client = new Client { ForeignManifest = foreign };
        using var model = new PostedLedgerViewModel(client);
        await model.RefreshAsync();
        await model.OpenAmountProofCommand.ExecuteAsync(model.JournalLines.Single().DebitProof);
        var evidence = model.ProofDrawer.Evidence.Single();
        model.ProofDrawer.OpenManifestCommand.CanExecute(evidence).Should().BeTrue();

        await model.ProofDrawer.OpenManifestCommand.ExecuteAsync(evidence);

        client.ManifestRoutes.Should().ContainSingle().Which.Should().StartWith("/workstation/evidence/vault/ev-fixture?ledgerAmountSubjectId=");
        if (foreign)
        {
            model.ProofDrawer.StatusText.Should().Be("Blocked");
            model.ProofDrawer.Evidence.Should().BeEmpty();
            model.ProofDrawer.ManifestText.Should().BeEmpty();
        }
        else
        {
            model.ProofDrawer.ManifestText.Should().Contain("retained-document");
            model.ProofDrawer.StatusText.Should().Be("Ready");
        }
    }

    [Theory]
    [InlineData("fund")]
    [InlineData("book")]
    [InlineData("period")]
    [InlineData("subject")]
    [InlineData("tenant-missing")]
    [InlineData("company-missing")]
    [InlineData("amount")]
    public async Task ForeignOrIncompletePacket_IsBlockedWithoutLeakingItsSupport(string mismatch)
    {
        var client = new Client
        {
            Transform = packet => packet with
            {
                LedgerAmount = packet.LedgerAmount! with
                {
                    SubjectId = mismatch == "subject" ? "foreign-subject" : packet.LedgerAmount.SubjectId,
                    Amount = mismatch == "amount" ? 999m : packet.LedgerAmount.Amount,
                    Scope = packet.LedgerAmount.Scope with
                    {
                        FundProfileId = mismatch == "fund" ? "fund-b" : "fund-a",
                        LedgerBookId = mismatch == "book" ? Client.BookB : Client.BookA,
                        PeriodId = mismatch == "period" ? Client.PeriodB : Client.PeriodA,
                        TenantId = mismatch == "tenant-missing" ? "" : "tenant-a",
                        CompanyId = mismatch == "company-missing" ? "" : "company-a"
                    }
                }
            }
        };
        using var model = new PostedLedgerViewModel(client);
        await model.RefreshAsync();
        await model.OpenAmountProofCommand.ExecuteAsync(model.JournalLines.Single().DebitProof);

        model.ProofDrawer.StatusText.Should().Be("Blocked");
        model.ProofDrawer.Evidence.Should().BeEmpty();
        model.ProofDrawer.ScopeText.Should().BeEmpty();
    }

    [Fact]
    public async Task DuplicateEvidenceIdentifiers_AreBlockedAsAmbiguous()
    {
        var client = new Client
        {
            Transform = packet => packet with
            {
                LedgerAmount = packet.LedgerAmount! with
                {
                    Evidence = [packet.LedgerAmount.Evidence.Single(), packet.LedgerAmount.Evidence.Single() with { Label = "Unrelated case" }]
                }
            }
        };
        using var model = new PostedLedgerViewModel(client);
        await model.RefreshAsync();
        await model.OpenAmountProofCommand.ExecuteAsync(model.JournalLines.Single().DebitProof);

        model.ProofDrawer.StatusText.Should().Be("Blocked");
        model.ProofDrawer.Evidence.Should().BeEmpty();
    }

    [Fact]
    public async Task ForeignArtifactUnderMatchingTopLevelScope_IsBlockedBeforeOpeningManifest()
    {
        var client = new Client
        {
            Transform = packet => packet with
            {
                Nodes = [packet.Nodes.Single() with
                {
                    ArtifactRefs = [packet.Nodes.Single().ArtifactRefs.Single() with { CanonicalSubjectId = "foreign-fund-retained-subject" }]
                }]
            }
        };
        using var model = new PostedLedgerViewModel(client);
        await model.RefreshAsync();
        await model.OpenAmountProofCommand.ExecuteAsync(model.JournalLines.Single().DebitProof);

        model.ProofDrawer.StatusText.Should().Be("Blocked");
        model.ProofDrawer.Evidence.Should().BeEmpty();
        client.ManifestRoutes.Should().BeEmpty();
    }

    [Fact]
    public async Task MissingPacket_ClearsPreviousProofAndShowsBlocked()
    {
        var client = new Client();
        using var model = new PostedLedgerViewModel(client);
        await model.RefreshAsync();
        await model.OpenAmountProofCommand.ExecuteAsync(model.JournalLines.Single().DebitProof);
        client.Missing = true;

        await model.OpenAmountProofCommand.ExecuteAsync(model.JournalLines.Single().DebitProof);

        model.ProofDrawer.IsOpen.Should().BeTrue();
        model.ProofDrawer.StatusText.Should().Be("Blocked");
        model.ProofDrawer.Evidence.Should().BeEmpty();
    }

    [Fact]
    public async Task LateProofAfterFundSelection_CannotReopenOutgoingFundEvidence()
    {
        var client = new Client();
        using var model = new PostedLedgerViewModel(client);
        await model.RefreshAsync();
        var oldSelection = model.JournalLines.Single().DebitProof!;
        var pending = new TaskCompletionSource<ApiResponse<EvidencePacketDto>>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.Pending = pending;
        var open = model.OpenAmountProofCommand.ExecuteAsync(oldSelection);
        await model.SelectBookCommand.ExecuteAsync(model.Books.Single(book => book.LedgerBookId == Client.BookB));
        pending.SetResult(ApiResponse<EvidencePacketDto>.Ok(Client.Packet(oldSelection)));
        await open;

        model.ProofDrawer.IsOpen.Should().BeFalse();
        model.ProofDrawer.Evidence.Should().BeEmpty();
        await model.OpenAmountProofCommand.ExecuteAsync(oldSelection);
        model.ProofDrawer.StatusText.Should().Be("Blocked");
        client.Requests.Should().HaveCount(1);
    }

    [Fact]
    public async Task CloseWhileLoading_IgnoresLateResponse()
    {
        var client = new Client();
        using var model = new PostedLedgerViewModel(client);
        await model.RefreshAsync();
        var selection = model.JournalLines.Single().DebitProof!;
        var pending = new TaskCompletionSource<ApiResponse<EvidencePacketDto>>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.Pending = pending;
        var open = model.OpenAmountProofCommand.ExecuteAsync(selection);
        model.ProofDrawer.CloseCommand.Execute(null);
        pending.SetResult(ApiResponse<EvidencePacketDto>.Ok(Client.Packet(selection)));
        await open;

        model.ProofDrawer.IsOpen.Should().BeFalse();
        model.ProofDrawer.Evidence.Should().BeEmpty();
    }

    [Fact]
    public void PageDebitButton_OpensBoundSharedProofDrawerAndCloseClearsIt()
    {
        WpfTestThread.Run(async () =>
        {
            var client = new Client();
            using var model = new PostedLedgerViewModel(client);
            var page = new PostedLedgerPage(model);
            var window = new Window { Content = page, Width = 1300, Height = 950, ShowInTaskbar = false };
            try
            {
                window.Show();
                await model.RefreshCommand.ExecuteAsync(null);
                window.UpdateLayout();
                var debit = Descendants<Button>(page).First(button => AutomationProperties.GetAutomationId(button) == "PostedLedgerDebitProof");
                debit.CommandParameter.Should().BeSameAs(model.JournalLines.Single().DebitProof);
                var invoke = (IInvokeProvider)new ButtonAutomationPeer(debit).GetPattern(PatternInterface.Invoke);
                invoke.Invoke();
                await Dispatcher.Yield(DispatcherPriority.Background);
                await (model.OpenAmountProofCommand.ExecutionTask ?? Task.CompletedTask);
                window.UpdateLayout();

                model.ProofDrawer.IsOpen.Should().BeTrue();
                model.ProofDrawer.Evidence.Select(item => item.EvidenceId).Should().Equal("retained-fund-a");
                var status = Descendants<TextBlock>(page).Single(block => AutomationProperties.GetAutomationId(block) == "LedgerAmountProofStatus");
                status.Text.Should().Be("Ready");
                var manifest = Descendants<Button>(page).Single(button => AutomationProperties.GetAutomationId(button) == "LedgerAmountProofOpenManifest");
                manifest.IsEnabled.Should().BeTrue();
                ((IInvokeProvider)new ButtonAutomationPeer(manifest).GetPattern(PatternInterface.Invoke)).Invoke();
                await Dispatcher.Yield(DispatcherPriority.Background);
                await (model.ProofDrawer.OpenManifestCommand.ExecutionTask ?? Task.CompletedTask);
                model.ProofDrawer.ManifestText.Should().Contain("retained-document");
                var close = Descendants<Button>(page).Single(button => AutomationProperties.GetAutomationId(button) == "LedgerAmountProofClose");
                ((IInvokeProvider)new ButtonAutomationPeer(close).GetPattern(PatternInterface.Invoke)).Invoke();
                await Dispatcher.Yield(DispatcherPriority.Background);
                model.ProofDrawer.IsOpen.Should().BeFalse();
                model.ProofDrawer.Evidence.Should().BeEmpty();
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void HttpRoute_PreservesStableSubjectAndExactScope()
    {
        LedgerReportsApiClient.BuildAmountProofRoute("journal:line:debit", Client.BookA, Client.PeriodA, "fund/a")
            .Should().Be($"/api/workstation/evidence/subjects/ledger-amount/journal%3Aline%3Adebit/packet?ledgerBookId={Client.BookA:D}&periodId={Client.PeriodA:D}&fundProfileId=fund%2Fa");
    }

    [Theory]
    [InlineData("https://foreign.example/workstation/evidence/vault/ev-fixture")]
    [InlineData("/workstation/evidence/vault/../other")]
    [InlineData("/workstation/evidence/vault/ev-fixture?fund=other")]
    [InlineData("/workstation/evidence/vault/")]
    public void ManifestRoute_RejectsUnscopedOrExternalTargets(string route)
        => LedgerReportsApiClient.IsRetainedManifestRoute(route).Should().BeFalse();

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
            {
                yield return match;
            }
            foreach (var descendant in Descendants<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private sealed class Client : ILedgerReportsApiClient
    {
        public static readonly Guid BookA = Guid.Parse("10000000-0000-0000-0000-000000000001");
        public static readonly Guid BookB = Guid.Parse("10000000-0000-0000-0000-000000000002");
        public static readonly Guid PeriodA = Guid.Parse("20000000-0000-0000-0000-000000000001");
        public static readonly Guid PeriodB = Guid.Parse("20000000-0000-0000-0000-000000000002");
        private static readonly Guid JournalId = Guid.Parse("30000000-0000-0000-0000-000000000001");
        private static readonly Guid EntryId = Guid.Parse("40000000-0000-0000-0000-000000000001");
        private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-01T12:00:00Z");
        public List<(string SubjectId, Guid BookId, Guid PeriodId, string FundId)> Requests { get; } = [];
        public Func<EvidencePacketDto, EvidencePacketDto> Transform { get; init; } = packet => packet;
        public bool Missing { get; set; }
        public bool ForeignManifest { get; init; }
        public List<string> ManifestRoutes { get; } = [];
        public TaskCompletionSource<ApiResponse<EvidencePacketDto>>? Pending { get; set; }

        public Task<ApiResponse<List<LedgerBookDto>>> GetBooksAsync(CancellationToken ct = default)
            => Task.FromResult(ApiResponse<List<LedgerBookDto>>.Ok([
                new(BookA, "fund-a", BookA, FundStructureNodeKindDto.Fund, "A fund", "USD", Now, Now),
                new(BookB, "fund-b", BookB, FundStructureNodeKindDto.Fund, "B fund", "USD", Now, Now)]));

        public Task<ApiResponse<List<LedgerPeriodDto>>> GetPeriodsAsync(Guid? ledgerBookId, CancellationToken ct = default)
            => Task.FromResult(ApiResponse<List<LedgerPeriodDto>>.Ok([
                new(ledgerBookId == BookA ? PeriodA : PeriodB, ledgerBookId!.Value, 2026, 9, "September", new(2026, 9, 1),
                    new(2026, 9, 30), LedgerPeriodStatusDto.Open, Now, null, 1)]));

        public Task<ApiResponse<List<LedgerPeriodTrialBalanceLineDto>>> GetTrialBalanceAsync(Guid periodId, CancellationToken ct = default)
            => Task.FromResult(ApiResponse<List<LedgerPeriodTrialBalanceLineDto>>.Ok([]));

        public Task<ApiResponse<LedgerPeriodPnlSummaryDto>> GetPnlSummaryAsync(Guid periodId, CancellationToken ct = default)
            => Task.FromResult(ApiResponse<LedgerPeriodPnlSummaryDto>.Fail("Open period", 404));

        public Task<ApiResponse<List<LedgerJournalEntryDto>>> GetJournalEntriesAsync(Guid periodId, CancellationToken ct = default)
            => Task.FromResult(ApiResponse<List<LedgerJournalEntryDto>>.Ok([
                new(JournalId, periodId, periodId == PeriodA ? BookA : BookB, Guid.NewGuid(), null, null, 1, Now, Now,
                    "Retained journal", 100m, 100m, true,
                    [new(EntryId, JournalId, Now, "Same account", "Asset", "SAME", "account-1", 100m, 0m, "Posted debit")])]));

        public Task<ApiResponse<EvidencePacketDto>> GetAmountProofAsync(
            string subjectId, Guid ledgerBookId, Guid periodId, string fundProfileId, CancellationToken ct = default)
        {
            Requests.Add((subjectId, ledgerBookId, periodId, fundProfileId));
            if (Pending is not null)
            {
                return Pending.Task;
            }
            if (Missing)
            {
                return Task.FromResult(ApiResponse<EvidencePacketDto>.Fail("Not found", 404));
            }
            return Task.FromResult(ApiResponse<EvidencePacketDto>.Ok(Transform(Packet(new(
                JournalId, EntryId, ledgerBookId, periodId, fundProfileId, "debit", 100m, "USD", "Same account")))));
        }

        public Task<ApiResponse<JsonDocument>> GetRetainedManifestAsync(string route, CancellationToken ct = default)
        {
            ManifestRoutes.Add(route);
            var request = Requests.Last();
            return Task.FromResult(ApiResponse<JsonDocument>.Ok(JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                tenantId = "tenant-a",
                scope = ForeignManifest ? "company-foreign" : "company-a",
                subject = new { subjectKind = "ledger-amount", subjectId = RetainedSubject(request.SubjectId, request.BookId, request.PeriodId, request.FundId) },
                vaultIdentity = new { contentHashSha256 = new string('a', 64) },
                documents = new[] { "retained-document" }
            }))));
        }

        private static string RetainedSubject(string subjectId, Guid book, Guid period, string fund)
            => $"{fund}:{book:D}:{period:D}:{subjectId}";

        private static string ManifestRoute(PostedLedgerAmountSelection selection)
            => $"/workstation/evidence/vault/ev-fixture?ledgerAmountSubjectId={Uri.EscapeDataString(selection.SubjectId)}"
               + $"&ledgerBookId={selection.LedgerBookId:D}&periodId={selection.PeriodId:D}&fundProfileId={selection.FundProfileId}"
               + $"&expectedContentHash={new string('a', 64)}";

        public static EvidencePacketDto Packet(PostedLedgerAmountSelection selection)
            => new(new(selection.SubjectId, "ledger-amount", "Selected debit", "Accounting", null, "PostedLedger", selection.LedgerBookId),
                Now,
                [new("retained-" + selection.FundProfileId,
                    new(selection.SubjectId, "ledger-amount", "Selected debit", "Accounting", null, "PostedLedger"),
                    "source-document", EvidenceStatusDto.Ready, new(Now, false, null), "vault", "Retained source",
                    [new("retained-" + selection.FundProfileId, "source-document", null, ManifestRoute(selection), Now,
                        new string('a', 64), true, "ledger-amount", RetainedSubject(selection.SubjectId, selection.LedgerBookId, selection.PeriodId, selection.FundProfileId))], [])],
                [], new(100, EvidenceStatusDto.Ready, [], [], [], [], []), [], [])
            {
                LedgerAmount = new(selection.SubjectId,
                    new("tenant-a", "company-a", selection.FundProfileId, selection.LedgerBookId, selection.PeriodId),
                    selection.Amount, selection.Currency, EvidenceStatusDto.Ready,
                    [new("retained-" + selection.FundProfileId, "source-document", "Retained supporting source", ManifestRoute(selection), "vault", Now, EvidenceStatusDto.Ready, new string('a', 64))], [])
            };
    }
}
