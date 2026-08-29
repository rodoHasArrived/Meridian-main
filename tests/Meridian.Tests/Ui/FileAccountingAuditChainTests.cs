using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Meridian.Contracts.Ledger;
using Meridian.Ui.Shared.Services;

namespace Meridian.Tests.Ui;

/// <summary>
/// W9-GOV-008 criterion 3, file posture. <see cref="FileAccountingConfigurationStore"/> is the
/// accounting audit store that runs whenever nobody has stood up PostgreSQL — the workstation and
/// WPF compositions both fall through to it — so its tamper-evidence has to hold on its own terms.
/// </summary>
/// <remarks>
/// The store persists a whole-file snapshot: every write replaces the document. That makes the
/// distinction these tests are built around load-bearing. A predecessor-hash chain detects
/// <b>mutation and reordering</b>. It cannot, on its own, detect <b>deletion or rollback</b>,
/// because removing the newest events together with the stored head leaves a shorter chain that is
/// internally perfect. The tests below therefore assert both halves: that the chain catches the
/// first class, and that the externally retained head — and only it — catches the second.
/// </remarks>
public sealed class FileAccountingAuditChainTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("meridian-audit-chain-").FullName;

    private string SnapshotPath => Path.Combine(_root, "accounting-configuration.json");

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A leaked temp directory must never fail a test run.
        }
    }

    [Fact]
    public async Task AppendAsync_ChainsEachEventToItsPredecessorAndAnchorsTheHeadOutsideTheSnapshot()
    {
        var store = new FileAccountingConfigurationStore(SnapshotPath);

        await store.AppendAsync(AuditEvent("post-journal"));
        await store.AppendAsync(AuditEvent("close-period"));
        await store.AppendAsync(AuditEvent("reopen-period"));

        var verification = await store.VerifyAuditChainAsync();
        verification.IsValid.Should().BeTrue();
        verification.LinksChecked.Should().Be(3);
        verification.PreChainEventCount.Should().Be(0);

        var chain = ReadChain();
        chain!.Links.Select(link => link.Sequence).Should().Equal(1L, 2L, 3L);
        chain.Links[0].PreviousHash.Should().BeNull();
        chain.Links[1].PreviousHash.Should().Be(chain.Links[0].EntryHash);
        chain.Links[2].PreviousHash.Should().Be(chain.Links[1].EntryHash);

        // The head is retained beside the snapshot, not inside it.
        File.Exists(store.AuditChainAnchorPath).Should().BeTrue();
        var anchor = await new FileAccountingAuditChainAnchor(store.AuditChainAnchorPath).ReadHeadAsync();
        anchor!.Sequence.Should().Be(3);
        anchor.EntryHash.Should().Be(chain.Links[2].EntryHash);
        anchor.Phase.Should().Be(AccountingAuditChainAnchorPhase.Committed);
    }

    [Fact]
    public async Task VerifyAuditChainAsync_DetectsAMutatedEvent()
    {
        var store = new FileAccountingConfigurationStore(SnapshotPath);
        await store.AppendAsync(AuditEvent("post-journal"));
        await store.AppendAsync(AuditEvent("close-period"));

        MutateSnapshot(snapshot =>
        {
            // Rewrite who performed the action, leaving everything else — including the chain — intact.
            snapshot["auditEvents"]!.AsArray()[0]!["actor"] = "someone-else";
        });

        var verification = await store.VerifyAuditChainAsync();
        verification.Status.Should().Be(AccountingAuditChainStatus.EventMutated);
    }

    [Fact]
    public async Task VerifyAuditChainAsync_DetectsAReorderedChain()
    {
        var store = new FileAccountingConfigurationStore(SnapshotPath);
        await store.AppendAsync(AuditEvent("post-journal"));
        await store.AppendAsync(AuditEvent("close-period"));
        await store.AppendAsync(AuditEvent("reopen-period"));

        MutateSnapshot(snapshot =>
        {
            var links = snapshot["auditChain"]!["links"]!.AsArray();
            var second = links[1]!.DeepClone();
            var third = links[2]!.DeepClone();
            links[1] = third;
            links[2] = second;
        });

        var verification = await store.VerifyAuditChainAsync();
        verification.Status.Should().Be(AccountingAuditChainStatus.BrokenSequence);
    }

    [Fact]
    public async Task VerifyAuditChainAsync_DetectsARemovedEventThatTheChainStillReferences()
    {
        var store = new FileAccountingConfigurationStore(SnapshotPath);
        await store.AppendAsync(AuditEvent("post-journal"));
        await store.AppendAsync(AuditEvent("close-period"));

        MutateSnapshot(snapshot =>
        {
            // Drop an event from the middle of the retained history but leave the chain untouched.
            snapshot["auditEvents"]!.AsArray().RemoveAt(0);
        });

        var verification = await store.VerifyAuditChainAsync();
        verification.Status.Should().Be(AccountingAuditChainStatus.MissingEvent);
    }

    /// <summary>
    /// The case a chain stored inside the snapshot cannot catch: the newest events are removed
    /// together with the head that recorded them, so what is left verifies perfectly on its own.
    /// </summary>
    [Fact]
    public async Task VerifyAuditChainAsync_DetectsATruncatedTailThatIsInternallyValid()
    {
        var store = new FileAccountingConfigurationStore(SnapshotPath);
        await store.AppendAsync(AuditEvent("post-journal"));
        await store.AppendAsync(AuditEvent("close-period"));
        await store.AppendAsync(AuditEvent("reopen-period"));

        var truncatedEventId = ReadChain()!.Links[^1].AuditEventId;
        MutateSnapshot(snapshot =>
        {
            var links = snapshot["auditChain"]!["links"]!.AsArray();
            links.RemoveAt(links.Count - 1);

            var events = snapshot["auditEvents"]!.AsArray();
            for (var index = events.Count - 1; index >= 0; index--)
            {
                if (Guid.Parse(events[index]!["auditEventId"]!.GetValue<string>()) == truncatedEventId)
                {
                    events.RemoveAt(index);
                }
            }
        });

        // The remaining chain is self-consistent — this is exactly why an in-snapshot head is not
        // enough, and the assertion is here so a future change that "simplifies" the anchor away
        // fails loudly instead of silently weakening the guarantee.
        AccountingAuditChain.VerifyLinks(ReadChain(), ReadEvents()).IsValid.Should().BeTrue();

        var verification = await store.VerifyAuditChainAsync();
        verification.Status.Should().Be(AccountingAuditChainStatus.AnchorMismatch);
    }

    [Fact]
    public async Task VerifyAuditChainAsync_DetectsARollbackToAnEarlierValidSnapshot()
    {
        var store = new FileAccountingConfigurationStore(SnapshotPath);
        await store.AppendAsync(AuditEvent("post-journal"));
        await store.AppendAsync(AuditEvent("close-period"));

        // Keep a byte-for-byte copy of a genuinely valid earlier state.
        var earlierSnapshot = await File.ReadAllBytesAsync(SnapshotPath);

        await store.AppendAsync(AuditEvent("reopen-period"));
        await store.AppendAsync(AuditEvent("post-adjustment"));

        await File.WriteAllBytesAsync(SnapshotPath, earlierSnapshot);

        // Restored wholesale, the snapshot is a real snapshot the store itself wrote.
        AccountingAuditChain.VerifyLinks(ReadChain(), ReadEvents()).IsValid.Should().BeTrue();

        var verification = await store.VerifyAuditChainAsync();
        verification.Status.Should().Be(AccountingAuditChainStatus.AnchorMismatch);
        verification.Detail.Should().Contain("2").And.Contain("4");
    }

    [Fact]
    public async Task VerifyAuditChainAsync_TreatsADeletedHeadJournalAsTamperingRatherThanAnUnanchoredStore()
    {
        var store = new FileAccountingConfigurationStore(SnapshotPath);
        await store.AppendAsync(AuditEvent("post-journal"));
        await store.AppendAsync(AuditEvent("close-period"));

        // Deleting the head must not be a way out of detection — otherwise rolling back the snapshot
        // and removing the journal would restore a "clean" store.
        File.Delete(store.AuditChainAnchorPath);

        var verification = await store.VerifyAuditChainAsync();
        verification.Status.Should().Be(AccountingAuditChainStatus.AnchorMissing);
    }

    [Fact]
    public async Task AppendAsync_FailsClosedRatherThanExtendingATamperedChain()
    {
        var store = new FileAccountingConfigurationStore(SnapshotPath);
        await store.AppendAsync(AuditEvent("post-journal"));
        await store.AppendAsync(AuditEvent("close-period"));

        MutateSnapshot(snapshot => snapshot["auditEvents"]!.AsArray()[0]!["actor"] = "someone-else");

        var append = async () => await store.AppendAsync(AuditEvent("reopen-period"));

        (await append.Should().ThrowAsync<AccountingAuditChainIntegrityException>())
            .Which.Verification.Status.Should().Be(AccountingAuditChainStatus.EventMutated);
    }

    [Fact]
    public async Task VerifyAuditChainAsync_ReportsPreChainHistoryAsOutsideTheChainRatherThanProtectedByIt()
    {
        // A snapshot written before chaining existed: real events, no chain, no head.
        var legacy = new JsonObject
        {
            ["workspaces"] = new JsonArray(),
            ["auditEvents"] = new JsonArray(
                SerializeEvent(AuditEvent("legacy-post")),
                SerializeEvent(AuditEvent("legacy-close"))),
        };
        await File.WriteAllTextAsync(SnapshotPath, legacy.ToJsonString());

        var store = new FileAccountingConfigurationStore(SnapshotPath);

        var beforeChaining = await store.VerifyAuditChainAsync();
        beforeChaining.IsValid.Should().BeTrue();
        beforeChaining.LinksChecked.Should().Be(0);
        beforeChaining.PreChainEventCount.Should().Be(2);

        await store.AppendAsync(AuditEvent("post-journal"));

        var afterChaining = await store.VerifyAuditChainAsync();
        afterChaining.IsValid.Should().BeTrue();
        afterChaining.LinksChecked.Should().Be(1);

        // The declared boundary: the two retained legacy events are named as unprotected rather than
        // presented as tamper-evident by a chain that never covered them.
        afterChaining.PreChainEventCount.Should().Be(2);
        ReadChain()!.GenesisSequence.Should().Be(AccountingAuditChainState.FirstSequence);
    }

    [Fact]
    public async Task VerifyAuditChainAsync_DistinguishesAnInterruptedAppendFromTampering()
    {
        var store = new FileAccountingConfigurationStore(SnapshotPath);
        await store.AppendAsync(AuditEvent("post-journal"));

        // Simulate a crash after the head was declared but before the snapshot write landed.
        var anchor = new FileAccountingAuditChainAnchor(store.AuditChainAnchorPath);
        await anchor.DeclareAsync(2, new string('a', 64));

        var verification = await store.VerifyAuditChainAsync();
        verification.Status.Should().Be(AccountingAuditChainStatus.InterruptedAppend);
        verification.Status.Should().NotBe(AccountingAuditChainStatus.AnchorMismatch);
    }

    [Fact]
    public async Task Anchor_RefusesAHeadThatDoesNotAdvance()
    {
        var anchor = new FileAccountingAuditChainAnchor(Path.Combine(_root, "head.log"));
        await anchor.DeclareAsync(1, new string('a', 64));
        await anchor.CommitAsync(1, new string('a', 64));
        await anchor.DeclareAsync(2, new string('b', 64));

        var rewind = async () => await anchor.DeclareAsync(1, new string('c', 64));

        await rewind.Should().ThrowAsync<AccountingAuditChainAnchorException>();
    }

    [Fact]
    public async Task Anchor_DetectsAnEditedHeadJournalLine()
    {
        var anchorPath = Path.Combine(_root, "head.log");
        var anchor = new FileAccountingAuditChainAnchor(anchorPath);
        await anchor.DeclareAsync(1, new string('a', 64));
        await anchor.CommitAsync(1, new string('a', 64));
        await anchor.DeclareAsync(2, new string('b', 64));
        await anchor.CommitAsync(2, new string('b', 64));

        var lines = await File.ReadAllLinesAsync(anchorPath);
        var forged = JsonNode.Parse(lines[^1])!.AsObject();
        forged["sequence"] = 9;
        lines[^1] = forged.ToJsonString();
        await File.WriteAllLinesAsync(anchorPath, lines);

        var read = async () => await anchor.ReadHeadAsync();

        await read.Should().ThrowAsync<AccountingAuditChainAnchorException>();
    }

    [Fact]
    public async Task AppendAsync_SerializesConcurrentAppendsWithoutForkingTheChain()
    {
        var store = new FileAccountingConfigurationStore(SnapshotPath);

        await Task.WhenAll(Enumerable
            .Range(0, 12)
            .Select(index => store.AppendAsync(AuditEvent($"action-{index.ToString()}"))));

        var verification = await store.VerifyAuditChainAsync();
        verification.IsValid.Should().BeTrue();
        verification.LinksChecked.Should().Be(12);

        var chain = ReadChain()!;
        chain.Links.Select(link => link.Sequence).Should().Equal(Enumerable.Range(1, 12).Select(value => (long)value));
        chain.Links.Select(link => link.AuditEventId).Should().OnlyHaveUniqueItems();
    }

    private static AccountingActionAuditEventDto AuditEvent(string action)
        => new(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            Actor: "operator@example.test",
            Action: action,
            FundProfileId: "fund-alpha",
            LedgerBookId: null,
            CorrelationId: null,
            BeforeHash: new string('0', 64),
            AfterHash: new string('1', 64),
            ValidationIssues: [],
            EvidenceLinks: [],
            CompanyId: "company-alpha",
            ReportGroupPrincipalIds: null,
            TenantId: "tenant-alpha");

    private static JsonNode SerializeEvent(AccountingActionAuditEventDto auditEvent)
        => JsonSerializer.SerializeToNode(auditEvent, WebJson)!;

    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    private void MutateSnapshot(Action<JsonObject> mutate)
    {
        var snapshot = JsonNode.Parse(File.ReadAllText(SnapshotPath))!.AsObject();
        mutate(snapshot);
        File.WriteAllText(SnapshotPath, snapshot.ToJsonString());
    }

    private AccountingAuditChainState? ReadChain()
    {
        var snapshot = JsonNode.Parse(File.ReadAllText(SnapshotPath))!.AsObject();
        return snapshot["auditChain"] is { } chain
            ? chain.Deserialize<AccountingAuditChainState>(WebJson)
            : null;
    }

    private IReadOnlyList<AccountingActionAuditEventDto> ReadEvents()
    {
        var snapshot = JsonNode.Parse(File.ReadAllText(SnapshotPath))!.AsObject();
        return snapshot["auditEvents"].Deserialize<List<AccountingActionAuditEventDto>>(WebJson) ?? [];
    }
}
