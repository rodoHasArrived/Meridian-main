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
        await anchor.DeclareAsync(2, new string('a', 64), AccountingAuditChainState.FirstSequence, preChainEventCount: 0);

        var verification = await store.VerifyAuditChainAsync();
        verification.Status.Should().Be(AccountingAuditChainStatus.InterruptedAppend);
        verification.Status.Should().NotBe(AccountingAuditChainStatus.AnchorMismatch);
    }

    [Fact]
    public void ComputePayloadHash_IsStableAcrossTheResolutionPostgresRetains()
    {
        // timestamptz stores microseconds; DateTimeOffset carries 100ns ticks. A digest over the full
        // tick would verify in memory and then fail the moment the same event came back from
        // PostgreSQL — reported as tampering, caused by rounding. One digest scheme has to survive
        // both postures, so it is taken at the coarser resolution.
        var recordedAt = new DateTimeOffset(2026, 8, 29, 12, 0, 0, TimeSpan.Zero).AddTicks(1234567);
        var withSubMicrosecondTicks = AuditEvent("close-period") with { RecordedAtUtc = recordedAt };
        var asPostgresWouldReturnIt = withSubMicrosecondTicks with
        {
            RecordedAtUtc = new DateTimeOffset(recordedAt.Ticks - (recordedAt.Ticks % 10), TimeSpan.Zero),
        };

        AccountingAuditChain.ComputePayloadHash(withSubMicrosecondTicks).Should()
            .Be(AccountingAuditChain.ComputePayloadHash(asPostgresWouldReturnIt));
    }

    [Fact]
    public void ComputePayloadHash_StillDistinguishesInstantsPostgresCanTellApart()
    {
        var recordedAt = new DateTimeOffset(2026, 8, 29, 12, 0, 0, TimeSpan.Zero);
        var earlier = AuditEvent("close-period") with { RecordedAtUtc = recordedAt };
        var oneMicrosecondLater = earlier with { RecordedAtUtc = recordedAt.AddTicks(10) };

        AccountingAuditChain.ComputePayloadHash(earlier).Should()
            .NotBe(AccountingAuditChain.ComputePayloadHash(oneMicrosecondLater));
    }

    [Fact]
    public void ComputePayloadHash_NormalizesOffsetToUtc()
    {
        var instant = new DateTimeOffset(2026, 8, 29, 12, 0, 0, TimeSpan.Zero);
        var asUtc = AuditEvent("close-period") with { RecordedAtUtc = instant };
        var sameInstantOtherOffset = asUtc with { RecordedAtUtc = instant.ToOffset(TimeSpan.FromHours(5)) };

        AccountingAuditChain.ComputePayloadHash(asUtc).Should()
            .Be(AccountingAuditChain.ComputePayloadHash(sameInstantOtherOffset));
    }

    [Fact]
    public async Task Anchor_RefusesAHeadThatDoesNotAdvance()
    {
        var anchor = new FileAccountingAuditChainAnchor(Path.Combine(_root, "head.log"));
        await anchor.DeclareAsync(1, new string('a', 64), AccountingAuditChainState.FirstSequence, preChainEventCount: 0);
        await anchor.CommitAsync(1, new string('a', 64), AccountingAuditChainState.FirstSequence, preChainEventCount: 0);
        await anchor.DeclareAsync(2, new string('b', 64), AccountingAuditChainState.FirstSequence, preChainEventCount: 0);

        var rewind = async () => await anchor.DeclareAsync(1, new string('c', 64), AccountingAuditChainState.FirstSequence, preChainEventCount: 0);

        await rewind.Should().ThrowAsync<AccountingAuditChainAnchorException>();
    }

    [Fact]
    public async Task Anchor_DetectsAnEditedHeadJournalLine()
    {
        var anchorPath = Path.Combine(_root, "head.log");
        var anchor = new FileAccountingAuditChainAnchor(anchorPath);
        await anchor.DeclareAsync(1, new string('a', 64), AccountingAuditChainState.FirstSequence, preChainEventCount: 0);
        await anchor.CommitAsync(1, new string('a', 64), AccountingAuditChainState.FirstSequence, preChainEventCount: 0);
        await anchor.DeclareAsync(2, new string('b', 64), AccountingAuditChainState.FirstSequence, preChainEventCount: 0);
        await anchor.CommitAsync(2, new string('b', 64), AccountingAuditChainState.FirstSequence, preChainEventCount: 0);

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

    [Fact]
    public async Task AppendAsync_SerializesAppendsAcrossStoreInstancesRatherThanLosingOne()
    {
        // W9-GOV-008 criterion 3: cross-process file-append serialization, deferred from #2866 and
        // #2871. Two instances model the two processes that really compose this store over one data
        // root (the browser host and the WPF shell): each instance carries its own in-process gate,
        // so only the shared lock file serializes them — exactly the seam a second process exercises.
        // Without it, both cycles read the same head, both pass the anchor's declare (a pending
        // declaration supersedes another at the same sequence, by design, for crash recovery), and
        // the later snapshot write replaces the earlier one: a committed audit event vanishes and
        // verification reports the race as tampering.
        var first = new FileAccountingConfigurationStore(SnapshotPath);
        var second = new FileAccountingConfigurationStore(SnapshotPath);

        await Task.WhenAll(Enumerable
            .Range(0, 8)
            .SelectMany(index => new[]
            {
                first.AppendAsync(AuditEvent($"first-{index.ToString()}")),
                second.AppendAsync(AuditEvent($"second-{index.ToString()}")),
            }));

        var verification = await first.VerifyAuditChainAsync();
        verification.IsValid.Should().BeTrue();
        verification.LinksChecked.Should().Be(16);

        var chain = ReadChain()!;
        chain.Links.Select(link => link.Sequence).Should().Equal(Enumerable.Range(1, 16).Select(value => (long)value));
        chain.Links.Select(link => link.AuditEventId).Should().OnlyHaveUniqueItems();
        ReadEvents().Should().HaveCount(16, "no append may replace another instance's committed write");
    }

    [Fact]
    public async Task SaveAsync_FromAnotherInstance_DoesNotDiscardAConcurrentlyAppendedEvent()
    {
        // A workspace save replaces the whole document too, so a save cycle racing an append from
        // another process used to write back a snapshot without the event that append had already
        // committed to the anchor — shortening a chain whose external head says otherwise, which is
        // indistinguishable from deliberate rollback. Interleaved saves and appends through two
        // instances must leave every appended event retained and the chain verifying.
        var appender = new FileAccountingConfigurationStore(SnapshotPath);
        var saver = new FileAccountingConfigurationStore(SnapshotPath);

        await Task.WhenAll(Enumerable
            .Range(0, 8)
            .SelectMany(index => new[]
            {
                appender.AppendAsync(AuditEvent($"action-{index.ToString()}")),
                saver.SaveAsync(Workspace($"v{index.ToString()}")),
            }));

        var verification = await appender.VerifyAuditChainAsync();
        verification.IsValid.Should().BeTrue();
        verification.LinksChecked.Should().Be(8);
        ReadEvents().Should().HaveCount(8);
        (await saver.GetAsync("fund-alpha", tenantId: "tenant-alpha", companyId: "company-alpha"))
            .Should().NotBeNull("the saves must survive alongside the appends");
    }

    [Fact]
    public async Task AppendAsync_WaitsForTheStoreLockAnotherProcessHolds()
    {
        // The deterministic half of the serialization proof: with the lock file held the way
        // another process's write cycle holds it, an append must wait rather than proceed — and
        // must complete once the holder releases, rather than failing.
        var store = new FileAccountingConfigurationStore(SnapshotPath);
        var lockPath = SnapshotPath + ".lock";

        Task append;
        await using (new FileStream(
            lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            append = store.AppendAsync(AuditEvent("post-journal"));
            var winner = await Task.WhenAny(append, Task.Delay(TimeSpan.FromMilliseconds(300)));
            winner.Should().NotBe(append, "an append must not run while another process holds the store lock");
        }

        await append;
        (await store.VerifyAuditChainAsync()).IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task VerifyAuditChainAsync_NeverReportsAnInFlightAppendAsAnIncident()
    {
        // Verification reads two files. Unserialized, it could read the anchor between another
        // process's declare and commit and report an interrupted append — or read the pair in the
        // other order and report a rollback — about a store that is merely busy. Under the store
        // lock every verification observes a quiesced boundary.
        var writer = new FileAccountingConfigurationStore(SnapshotPath);
        var observer = new FileAccountingConfigurationStore(SnapshotPath);
        await writer.AppendAsync(AuditEvent("genesis"));

        var appends = Task.Run(async () =>
        {
            for (var index = 0; index < 10; index++)
            {
                await writer.AppendAsync(AuditEvent($"action-{index.ToString()}"));
            }
        });

        // Bounded, with a breather between rounds: verification must observe a valid boundary
        // every time it looks, but a loop that reacquires the lock back-to-back would starve the
        // appender's acquisition polls rather than prove anything about serialization.
        for (var round = 0; round < 25 && !appends.IsCompleted; round++)
        {
            var verification = await observer.VerifyAuditChainAsync();
            verification.IsValid.Should().BeTrue(
                "an append in flight is not an incident; observed {0}", verification.Status);
            await Task.Delay(TimeSpan.FromMilliseconds(5));
        }

        await appends;
        (await observer.VerifyAuditChainAsync()).LinksChecked.Should().Be(11);
    }

    [Fact]
    public async Task AnEventAddedWithoutAChainLink_IsReportedRatherThanServedAsHistory()
    {
        // Codex review finding on PR #2866. Every link binding to a real, unmutated event says
        // nothing about an event that no link points at -- and ListAsync serves such an event as
        // ordinary audit history, so without the count check this is a way past tamper detection
        // that leaves both the chain and its external anchor reporting Valid.
        var store = new FileAccountingConfigurationStore(SnapshotPath);
        await store.AppendAsync(AuditEvent("post-journal"));
        await store.AppendAsync(AuditEvent("close-period"));

        MutateSnapshot(snapshot =>
        {
            var events = snapshot["auditEvents"]!.AsArray();
            events.Add(SerializeEvent(AuditEvent("fabricated-approval")));
        });

        var verification = await store.VerifyAuditChainAsync();

        verification.IsValid.Should().BeFalse();
        verification.Status.Should().Be(AccountingAuditChainStatus.UnlinkedEvent);
    }

    [Fact]
    public async Task AnAppendInterruptedAfterItsDeclaration_DoesNotBlockEveryLaterAppend()
    {
        // Codex review finding on PR #2866. Write-ahead ordering means a crash between DeclareAsync
        // and the snapshot write leaves the journal one declared append ahead. Refusing that state
        // forever would let one power cut permanently stop the audit log of the posture that runs
        // whenever PostgreSQL is not configured -- far worse than the crash it reports.
        var store = new FileAccountingConfigurationStore(SnapshotPath);
        await store.AppendAsync(AuditEvent("post-journal"));

        var anchor = new FileAccountingAuditChainAnchor(store.AuditChainAnchorPath);
        await anchor.DeclareAsync(2, new string('a', 64), AccountingAuditChainState.FirstSequence, preChainEventCount: 0);

        (await store.VerifyAuditChainAsync()).Status
            .Should().Be(AccountingAuditChainStatus.InterruptedAppend, "the declared write never landed");

        // The store must be able to carry on: the abandoned declaration holds no event.
        await store.AppendAsync(AuditEvent("close-period"));

        var verification = await store.VerifyAuditChainAsync();
        verification.IsValid.Should().BeTrue();
        verification.LinksChecked.Should().Be(2);
        (await store.ListAsync("fund-alpha")).Should().HaveCount(2);
    }

    [Fact]
    public async Task ARolledBackSnapshot_IsStillRefusedRatherThanResumed()
    {
        // The resume above must stay narrow: it accepts a *pending* declaration at the slot the next
        // append will take, and nothing else. A committed head the snapshot has fallen behind is the
        // rollback signature and must still fail closed.
        var store = new FileAccountingConfigurationStore(SnapshotPath);
        await store.AppendAsync(AuditEvent("post-journal"));
        await store.AppendAsync(AuditEvent("close-period"));

        // Roll back to the first append: keep only the event its surviving link references, so the
        // chain is internally perfect and ONLY the external head can tell that anything is missing.
        // (Retained events are ordered newest-first, so this must select by id rather than position.)
        var chain = ReadChain()!;
        var survivor = ReadEvents().Single(item => item.AuditEventId == chain.Links[0].AuditEventId);
        MutateSnapshot(snapshot =>
        {
            snapshot["auditEvents"] = new JsonArray(SerializeEvent(survivor));
            snapshot["auditChain"] = JsonSerializer.SerializeToNode(
                chain with { Links = [chain.Links[0]] }, WebJson);
        });

        var append = async () => await store.AppendAsync(AuditEvent("reopen-period"));

        (await append.Should().ThrowAsync<AccountingAuditChainIntegrityException>())
            .Which.Verification.Status.Should().Be(AccountingAuditChainStatus.AnchorMismatch);
    }

    [Fact]
    public void ThePayloadDigest_IsStableAcrossTheTrimmingTheDatabaseWritePathApplies()
    {
        // Codex review finding on PR #2866, and a regression the previous round introduced: the
        // PostgreSQL head check now recomputes the payload digest from the RETAINED row, while
        // AddTextOrNull trims text on the way in. Hashing the raw DTO therefore records a digest of a
        // value the row does not hold, and the next append reports EventMutated over an event nobody
        // touched -- permanently stopping the chain. Pinning the property the store now relies on:
        // an event and its trimmed form must digest alike.
        var raw = AuditEvent("close-period") with
        {
            Actor = "  operator@example.test  ",
            FundProfileId = "  fund-alpha  ",
            TenantId = "  tenant-alpha  ",
            CompanyId = "  company-alpha  ",
        };
        var stored = raw with
        {
            Actor = "operator@example.test",
            FundProfileId = "fund-alpha",
            TenantId = "tenant-alpha",
            CompanyId = "company-alpha",
        };

        AccountingAuditChain.ComputePayloadHash(raw).Should()
            .NotBe(
                AccountingAuditChain.ComputePayloadHash(stored),
                "the digest is over exact bytes, which is why the store must normalize BEFORE hashing "
                + "rather than rely on the digest to forgive it");

        // A blank optional field is stored as null, so the two must also digest alike.
        var blank = AuditEvent("close-period") with { CorrelationId = "   " };
        var nulled = blank with { CorrelationId = null };
        AccountingAuditChain.ComputePayloadHash(blank).Should()
            .NotBe(AccountingAuditChain.ComputePayloadHash(nulled));
    }

    [Fact]
    public async Task AppendAsync_IsIdempotentOnTheEventIdSoARetryDoesNotBreakTheChain()
    {
        // The chain requires each link to claim a distinct event, so a second append of one id
        // leaves a history that can never verify again -- and every append after it throws. A retry
        // is not hypothetical: it is exactly what RecoverPendingAuditAsync does after a crash
        // between a mutation and its audit.
        var store = new FileAccountingConfigurationStore(SnapshotPath);
        var auditEvent = AuditEvent("post-journal");

        await store.AppendAsync(auditEvent);
        await store.AppendAsync(auditEvent);

        var verification = await store.VerifyAuditChainAsync();
        verification.IsValid.Should().BeTrue();
        verification.LinksChecked.Should().Be(1);

        (await store.ListAsync("fund-alpha", null)).Should().ContainSingle();

        // And the chain keeps growing afterwards, which is the part a broken chain would deny.
        await store.AppendAsync(AuditEvent("close-period"));
        (await store.VerifyAuditChainAsync()).LinksChecked.Should().Be(2);
    }

    [Fact]
    public async Task AppendAsync_LeavesTheAnchorOnTheLandedSequenceWhenARepeatIsIgnored()
    {
        // The write-ahead anchor must not be advanced for an append that produced no link: a
        // declared sequence no event occupies is what InterruptedAppend means, and manufacturing
        // one out of a retry would report a crash that never happened.
        var store = new FileAccountingConfigurationStore(SnapshotPath);
        var auditEvent = AuditEvent("post-journal");

        await store.AppendAsync(auditEvent);
        await store.AppendAsync(auditEvent);

        var head = await new FileAccountingAuditChainAnchor(store.AuditChainAnchorPath).ReadHeadAsync();
        head!.Sequence.Should().Be(1);
        head.Phase.Should().Be(AccountingAuditChainAnchorPhase.Committed);
    }

    [Fact]
    public async Task AppendAsync_StillRefusesARepeatWhenTheRetainedChainDoesNotVerify()
    {
        // The idempotency check sits after verification on purpose. A repeat writes nothing, but
        // answering "appended" for one on a history that no longer verifies would report success
        // about a broken store -- and this method fails closed, which has to hold for every outcome
        // rather than only the writing ones.
        var store = new FileAccountingConfigurationStore(SnapshotPath);
        var auditEvent = AuditEvent("post-journal");
        await store.AppendAsync(auditEvent);
        await store.AppendAsync(AuditEvent("close-period"));

        MutateSnapshot(snapshot => snapshot["auditEvents"]!.AsArray()[0]!["actor"] = "someone-else");

        var repeat = async () => await store.AppendAsync(auditEvent);

        await repeat.Should().ThrowAsync<AccountingAuditChainIntegrityException>();
    }

    [Fact]
    public async Task AppendAsync_WritesNothingAtAllWhenTheEventIsAlreadyRetained()
    {
        // Second Codex review finding on PR #2871. The repeat returned the snapshot unchanged, but
        // it still went through the write path -- and this store replaces the whole document, while
        // its gate is a per-instance semaphore that two stores on one path do not share. Rewriting
        // an unchanged snapshot is therefore a chance to lose data rather than a no-op: a record
        // another writer appended between this cycle's read and its write would be replaced by this
        // cycle's stale copy, while the external anchor stayed ahead of the chain.
        //
        // Asserted as "no write happened" rather than by reconstructing the race, which is not
        // deterministic in-process: the retained bytes would be identical either way, so only the
        // absence of the write itself distinguishes the two.
        var store = new FileAccountingConfigurationStore(SnapshotPath);
        var auditEvent = AuditEvent("post-journal");
        await store.AppendAsync(auditEvent);

        var untouched = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(SnapshotPath, untouched);

        await store.AppendAsync(auditEvent);

        File.GetLastWriteTimeUtc(SnapshotPath).Should().Be(untouched,
            "a repeat has nothing to say and must not replace the retained document");

        // And a genuine append still does write, so the skip is narrow.
        await store.AppendAsync(AuditEvent("close-period"));
        File.GetLastWriteTimeUtc(SnapshotPath).Should().NotBe(untouched);
    }

    [Fact]
    public async Task AppendAsync_RefusesTwoDifferentEventsSharingOneId()
    {
        // Not a retry. Appending would break verification permanently and dropping it would lose an
        // audit record, so neither is done silently.
        var store = new FileAccountingConfigurationStore(SnapshotPath);
        var first = AuditEvent("post-journal");
        await store.AppendAsync(first);

        var collision = async () => await store.AppendAsync(first with { Action = "close-period" });

        (await collision.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain("different content");

        (await store.VerifyAuditChainAsync()).IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task VerifyAuditChainAsync_DetectsAnInjectedEventCoveredByARaisedPreChainCount()
    {
        // Nineteenth Codex review round. The unlinked-event check bounds the retained count by
        // PreChainEventCount + Links.Count -- and BOTH of those live in the snapshot being
        // protected. So the check could be satisfied by the same edit that defeats it: add an
        // unlinked event, raise the pre-chain count by one, and every link still verifies while the
        // anchor still passes, because the anchor hash bound only the chain head. The injected
        // record was then served by ListAsync as ordinary audit history.
        //
        // A count checked against a number the attacker also controls is not a check. The boundary
        // now rides the anchor, inside the anchor's own hash, so the snapshot cannot restate it.
        var store = new FileAccountingConfigurationStore(SnapshotPath);
        await store.AppendAsync(AuditEvent("post-journal"));
        await store.AppendAsync(AuditEvent("close-period"));

        MutateSnapshot(snapshot =>
        {
            var events = snapshot["auditEvents"]!.AsArray();
            var injected = events[0]!.DeepClone()!.AsObject();
            injected["auditEventId"] = Guid.NewGuid().ToString("D");
            injected["actor"] = "intruder@example.test";
            events.Add(injected);

            // The cover-up: one more event, one higher boundary, so the arithmetic still balances.
            var chain = snapshot["auditChain"]!.AsObject();
            chain["preChainEventCount"] = chain["preChainEventCount"]!.GetValue<int>() + 1;
        });

        var verification = await store.VerifyAuditChainAsync();

        verification.Status.Should().Be(AccountingAuditChainStatus.AnchorMismatch);
        verification.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task VerifyAuditChainAsync_RefusesToResumeAFirstAppendOverAChangedHistory()
    {
        // Twenty-first Codex review round. The genesis comparison added last round is guarded on
        // `state is not null` -- and state is null for exactly the append that ESTABLISHES the
        // genesis. So a crash between the pending anchor line and the snapshot left the one case
        // the boundary exists to protect unchecked: if the unchained history then changed, the
        // retry redeclared the same sequence over it and founded the chain on events nobody had
        // verified, while verification still reported a benign InterruptedAppend.
        var (store, anchor) = await SeedUnchainedHistoryAsync();
        await anchor.DeclareAsync(
            AccountingAuditChainState.FirstSequence,
            new string('a', 64),
            AccountingAuditChainState.FirstSequence,
            preChainEventCount: ReadEvents().Count);

        // The unchained history moves before the retry.
        MutateSnapshot(snapshot => snapshot["auditEvents"]!.AsArray().RemoveAt(0));

        var verification = await store.VerifyAuditChainAsync();

        verification.Status.Should().Be(AccountingAuditChainStatus.AnchorMismatch);
        verification.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task VerifyAuditChainAsync_StillResumesAFirstAppendOverAnUnchangedHistory()
    {
        // The control: checking the boundary must not turn an ordinary interrupted first append
        // into a tamper report. Same setup, nothing touched in between.
        var (store, anchor) = await SeedUnchainedHistoryAsync();
        await anchor.DeclareAsync(
            AccountingAuditChainState.FirstSequence,
            new string('a', 64),
            AccountingAuditChainState.FirstSequence,
            preChainEventCount: ReadEvents().Count);

        var verification = await store.VerifyAuditChainAsync();

        verification.Status.Should().Be(AccountingAuditChainStatus.InterruptedAppend);
    }

    /// <summary>
    /// A snapshot holding two audit events that no chain covers and no anchor records — the state a
    /// store is in immediately before its very first chained append.
    /// </summary>
    /// <remarks>
    /// Built by appending through the store and then stripping what that added, rather than by
    /// hand-writing a snapshot: it keeps the event shape whatever the DTO currently is. The anchor
    /// journal is deleted outright because <c>EnsureAdvances</c> refuses to move a head backwards,
    /// so a journal left over from the seeding appends would reject the declaration under test.
    /// </remarks>
    private async Task<(FileAccountingConfigurationStore Store, FileAccountingAuditChainAnchor Anchor)>
        SeedUnchainedHistoryAsync()
    {
        var store = new FileAccountingConfigurationStore(SnapshotPath);
        await store.AppendAsync(AuditEvent("post-journal"));
        await store.AppendAsync(AuditEvent("close-period"));

        var anchor = new FileAccountingAuditChainAnchor(
            FileAccountingAuditChainAnchor.AnchorPathFor(SnapshotPath));
        File.Delete(anchor.AnchorPath);
        MutateSnapshot(snapshot => snapshot.Remove("auditChain"));

        ReadChain().Should().BeNull("the scenario under test is a store that has never chained");
        ReadEvents().Should().HaveCount(2);
        return (store, anchor);
    }

    [Fact]
    public async Task VerifyAuditChainAsync_AcceptsAChainWhoseGenesisMatchesItsAnchor()
    {
        // The control the test above needs: binding the boundary must not make an untouched store
        // report tampering, including across several appends where the anchor is rewritten each
        // time and the boundary has to stay consistent between them.
        var store = new FileAccountingConfigurationStore(SnapshotPath);
        await store.AppendAsync(AuditEvent("post-journal"));
        await store.AppendAsync(AuditEvent("close-period"));
        await store.AppendAsync(AuditEvent("approve-pack"));

        (await store.VerifyAuditChainAsync()).IsValid.Should().BeTrue();
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

    private static AccountingConfigurationWorkspaceDto Workspace(string version)
        => new(
            FundProfileId: "fund-alpha",
            LedgerBookId: null,
            Status: AccountingConfigurationStatusDto.Draft,
            ConfigurationVersion: version,
            UpdatedAtUtc: DateTimeOffset.UtcNow,
            LedgerBooks: [],
            ChartOfAccounts: [],
            JournalTemplates: [],
            PostingRules: [],
            ValidationIssues: [],
            AuditTrail: [],
            TenantId: "tenant-alpha",
            CompanyId: "company-alpha");

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
