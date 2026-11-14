using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Entities.FundStructure;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Npgsql;

namespace Meridian.Tests.Storage;

/// <summary>
/// A book that existed before entity controls only warns. It may move to enforced posting once its
/// outstanding intercompany source population is attributed to approved agreements and its owning
/// entity has an evidenced lifecycle baseline. Guards against activation without a passing audited
/// preflight, activation on a preflight reviewed before the evidence changed, an unretained or
/// rewritable activation decision, and a book that claims enforcement but still only warns.
/// </summary>
public sealed partial class LedgerBookControlsPostgresTests
{
    private const string IntercompanyIdentityBlocker = "Intercompany lines require exact agreement, mapping and transaction identities.";

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task LegacyBook_WithAttributedSourcePopulationAndLifecycleBaseline_ActivatesWithARetainedReceiptAndThenEnforces()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var ct = timeout.Token;
        await using var fixture = await LegacyIntercompanyControlsFixture.CreateAsync(ct);
        var bookId = fixture.Book.LedgerBookId;

        // While the book only warns, an unattributed intercompany receivable is posted with its warning retained.
        var (record, line) = await fixture.PostUnattributedReceivableAsync(ct);
        var warned = ControlDecision(record);
        warned.Mode.Should().Be("LegacyWarning");
        warned.Blockers.Should().Contain(IntercompanyIdentityBlocker);

        // That outstanding line blocks activation until a reviewer attributes it.
        var blocked = await fixture.Journals.PrepareBookControlsAsync(bookId, ct);
        blocked.CanActivate.Should().BeFalse();
        blocked.Decisions.Should().ContainSingle(item => !item.Allowed).Which.Code.Should().Be("LegacyBalanceAttribution");
        var premature = () => fixture.Journals.ActivateBookControlsAsync(bookId, blocked.ProfileVersion, blocked.Digest,
            "controller", "Activate entity controls", "evidence:migration-review", ct);
        await premature.Should().ThrowAsync<LedgerValidationException>().WithMessage("*preflight changed or contains blockers*");
        (await fixture.Journals.GetBookControlsAsync(bookId, ct))!.Mode.Should().Be(LedgerBookControlModeDto.LegacyWarning);

        await fixture.Attributions.AppendAsync(fixture.Attribution(record, line), null, ct);
        var preflight = await fixture.Journals.PrepareBookControlsAsync(bookId, ct);

        preflight.CanActivate.Should().BeTrue();
        preflight.LedgerBookId.Should().Be(bookId);
        preflight.ProfileVersion.Should().Be(1);
        preflight.Digest.Should().NotBe(blocked.Digest);
        preflight.Decisions.Should().ContainSingle(item => item.Control == "entity-lifecycle")
            .Which.Should().BeEquivalentTo(new { Code = $"baseline:{fixture.EntityId:D}", Allowed = true, EvidenceRevision = fixture.Baseline.ContentHash });
        preflight.Decisions.Where(item => item.Control == "intercompany").Select(item => item.Code)
            .Should().BeEquivalentTo("CurrentRoleBinding", "LegacyBalanceAttribution");
        (await fixture.Journals.PrepareBookControlsAsync(bookId, ct)).Digest.Should().Be(preflight.Digest, "unchanged evidence prepares the same reviewed digest");
        (await fixture.CountAsync("ledger_book_control_activation", ct)).Should().Be(0, "preparing a preflight activates nothing");

        var missingEvidence = () => fixture.Journals.ActivateBookControlsAsync(bookId, preflight.ProfileVersion, preflight.Digest,
            "controller", "Activate entity controls", " ", ct);
        await missingEvidence.Should().ThrowAsync<LedgerValidationException>().WithMessage("*preflight digest, actor, reason and retained evidence*");

        var activated = await fixture.Journals.ActivateBookControlsAsync(bookId, preflight.ProfileVersion, preflight.Digest,
            "controller", "Activate entity controls after attribution review", "evidence:migration-review", ct);

        activated.Mode.Should().Be(LedgerBookControlModeDto.Enforced);
        activated.Version.Should().Be(2);
        activated.ActivatedBy.Should().Be("controller");
        activated.ActivatedAtUtc.Should().NotBeNull();
        var receiptId = Guid.Parse(activated.MigrationReceiptId!);
        // A separately constructed store with no authorities reads the same retained profile after a restart.
        var restarted = new PostgresLedgerJournalStore(fixture.Database.Options,
            new LegacyIntercompanyControlsFixture.TenantAccessor(LegacyIntercompanyControlsFixture.Tenant));
        (await restarted.GetBookControlsAsync(bookId, ct)).Should().BeEquivalentTo(activated);

        await using (var connection = new NpgsqlConnection(fixture.Database.Options.ConnectionString))
        {
            await connection.OpenAsync(ct);
            await using var command = new NpgsqlCommand($"""
                select receipt_id, ledger_book_id, prior_version, preflight_digest, actor, reason, evidence_reference
                from "{fixture.Database.Options.SchemaName}".ledger_book_control_activation;
                """, connection);
            await using var reader = await command.ExecuteReaderAsync(ct);
            (await reader.ReadAsync(ct)).Should().BeTrue();
            reader.GetGuid(0).Should().Be(receiptId);
            reader.GetGuid(1).Should().Be(bookId);
            reader.GetInt64(2).Should().Be(1);
            reader.GetString(3).Should().Be(preflight.Digest);
            reader.GetString(4).Should().Be("controller");
            reader.GetString(5).Should().Be("Activate entity controls after attribution review");
            reader.GetString(6).Should().Be("evidence:migration-review");
            (await reader.ReadAsync(ct)).Should().BeFalse();
        }
        var table = $"\"{fixture.Database.Options.SchemaName}\".ledger_book_control_activation";
        var rewrite = () => fixture.ExecuteAsync($"update {table} set actor = 'someone-else' where receipt_id = @id", receiptId, ct);
        await rewrite.Should().ThrowAsync<PostgresException>().WithMessage("*immutable*");
        var remove = () => fixture.ExecuteAsync($"delete from {table} where receipt_id = @id", receiptId, ct);
        await remove.Should().ThrowAsync<PostgresException>().WithMessage("*immutable*");

        // Repeating the decision neither activates twice nor accepts the superseded profile version.
        (await fixture.Journals.ActivateBookControlsAsync(bookId, activated.Version, preflight.Digest,
            "controller", "Activate entity controls after attribution review", "evidence:migration-review", ct)).Should().BeEquivalentTo(activated);
        var staleVersion = () => fixture.Journals.ActivateBookControlsAsync(bookId, preflight.ProfileVersion, preflight.Digest,
            "controller", "Activate entity controls after attribution review", "evidence:migration-review", ct);
        await staleVersion.Should().ThrowAsync<LedgerValidationException>().WithMessage("*Book controls changed*");
        (await fixture.CountAsync("ledger_book_control_activation", ct)).Should().Be(1);

        // The same kind of posting that only warned before activation is now refused, and nothing is appended.
        var refused = () => fixture.Journals.AppendAsync(fixture.UnattributedReceivable(), ct);
        (await refused.Should().ThrowAsync<LedgerValidationException>()).WithMessage($"*{IntercompanyIdentityBlocker}*");
        (await fixture.ReadJournalsAsync(ct)).Should().ContainSingle().Which.Entry.JournalEntryId.Should().Be(record.Entry.JournalEntryId);

        // Enforcement is specific: a posting with no control blocker is still accepted and retains the enforced decision.
        var ordinary = fixture.OrdinaryContribution();
        await fixture.Journals.AppendAsync(ordinary, ct);
        var accepted = (await fixture.ReadJournalsAsync(ct)).Should().HaveCount(2)
            .And.ContainSingle(item => item.Entry.JournalEntryId == ordinary.Entry.JournalEntryId).Subject;
        var enforced = ControlDecision(accepted);
        enforced.Mode.Should().Be("Enforced");
        enforced.Version.Should().Be(2);
        enforced.Blockers.Should().BeEmpty();
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task PreflightReviewedBeforeLifecycleEvidenceChanged_CannotActivate_UntilTheCurrentEvidenceIsPreparedAgain()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var ct = timeout.Token;
        await using var fixture = await LegacyIntercompanyControlsFixture.CreateAsync(ct);
        var bookId = fixture.Book.LedgerBookId;
        var (record, line) = await fixture.PostUnattributedReceivableAsync(ct);
        await fixture.Attributions.AppendAsync(fixture.Attribution(record, line), null, ct);
        var reviewed = await fixture.Journals.PrepareBookControlsAsync(bookId, ct);
        reviewed.CanActivate.Should().BeTrue();

        // The entity's lifecycle history gains a revision after the preflight was reviewed.
        var scheduledDissolution = new DateTimeOffset(2031, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var amended = await fixture.Lifecycle.AppendAsync(EntityLifecyclePolicy.Seal(fixture.Baseline with
        {
            Revision = 2,
            PreviousHash = fixture.Baseline.ContentHash,
            ContentHash = "",
            RecordedAtUtc = DateTimeOffset.UtcNow,
            Reason = "Evidenced scheduled dissolution",
            EvidenceReference = "fixture:scheduled-dissolution",
            Timeline = [fixture.Baseline.Timeline[0] with { EffectiveTo = scheduledDissolution },
                new(Guid.NewGuid(), scheduledDissolution, null, LegalEntityLifecycleStatusDto.Dissolved, LegalEntityLifecycleEventKindDto.Dissolution)]
        }), 1, ct);

        var stale = () => fixture.Journals.ActivateBookControlsAsync(bookId, reviewed.ProfileVersion, reviewed.Digest,
            "controller", "Activate entity controls", "evidence:migration-review", ct);
        await stale.Should().ThrowAsync<LedgerValidationException>().WithMessage("*preflight changed or contains blockers*");
        var unchanged = (await fixture.Journals.GetBookControlsAsync(bookId, ct))!;
        unchanged.Mode.Should().Be(LedgerBookControlModeDto.LegacyWarning);
        unchanged.Version.Should().Be(1);
        unchanged.MigrationReceiptId.Should().BeNull();
        (await fixture.CountAsync("ledger_book_control_activation", ct)).Should().Be(0);

        var current = await fixture.Journals.PrepareBookControlsAsync(bookId, ct);
        current.CanActivate.Should().BeTrue();
        current.Digest.Should().NotBe(reviewed.Digest);
        current.Decisions.Should().ContainSingle(item => item.Control == "entity-lifecycle").Which.EvidenceRevision.Should().Be(amended.ContentHash);
        var activated = await fixture.Journals.ActivateBookControlsAsync(bookId, current.ProfileVersion, current.Digest,
            "controller", "Activate entity controls", "evidence:migration-review", ct);
        activated.Mode.Should().Be(LedgerBookControlModeDto.Enforced);
        (await fixture.CountAsync("ledger_book_control_activation", ct)).Should().Be(1);
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task PreflightReviewedBeforeTheSourcePopulationOrItsAttributionChanged_CannotActivate()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var ct = timeout.Token;
        await using var fixture = await LegacyIntercompanyControlsFixture.CreateAsync(ct);
        var bookId = fixture.Book.LedgerBookId;
        var (record, line) = await fixture.PostUnattributedReceivableAsync(ct);
        var attribution = await fixture.Attributions.AppendAsync(fixture.Attribution(record, line), null, ct);
        var reviewed = await fixture.Journals.PrepareBookControlsAsync(bookId, ct);
        reviewed.CanActivate.Should().BeTrue();

        // The reviewed attribution is retracted after the preflight was prepared.
        await fixture.Attributions.AppendAsync(LegacyIntercompanyControlsFixture.Revoke(attribution), attribution.AttributionId, ct);
        var afterRevocation = () => fixture.Journals.ActivateBookControlsAsync(bookId, reviewed.ProfileVersion, reviewed.Digest,
            "controller", "Activate entity controls", "evidence:migration-review", ct);
        await afterRevocation.Should().ThrowAsync<LedgerValidationException>().WithMessage("*preflight changed or contains blockers*");
        var retracted = await fixture.Journals.PrepareBookControlsAsync(bookId, ct);
        retracted.CanActivate.Should().BeFalse();
        retracted.Decisions.Should().ContainSingle(item => !item.Allowed).Which.Code.Should().Be("LegacyBalanceAttribution");

        // A second unattributed receivable arrives while the book still only warns.
        var (second, _) = await fixture.PostUnattributedReceivableAsync(ct);
        var afterNewSource = () => fixture.Journals.ActivateBookControlsAsync(bookId, reviewed.ProfileVersion, reviewed.Digest,
            "controller", "Activate entity controls", "evidence:migration-review", ct);
        await afterNewSource.Should().ThrowAsync<LedgerValidationException>().WithMessage("*preflight changed or contains blockers*");
        var current = await fixture.Journals.PrepareBookControlsAsync(bookId, ct);
        current.CanActivate.Should().BeFalse();
        current.Decisions.Where(item => !item.Allowed).Should().HaveCount(2).And.OnlyContain(item => item.Code == "LegacyBalanceAttribution");
        current.Decisions.Should().Contain(item => !item.Allowed && item.Message.Contains(second.Entry.JournalEntryId.ToString("D")));

        var profile = (await fixture.Journals.GetBookControlsAsync(bookId, ct))!;
        profile.Mode.Should().Be(LedgerBookControlModeDto.LegacyWarning);
        profile.Version.Should().Be(1);
        (await fixture.CountAsync("ledger_book_control_activation", ct)).Should().Be(0);
    }

    /// <summary>Reads the eligibility decision the append boundary retained on the posted journal.</summary>
    private static (string Mode, long Version, IReadOnlyList<string> Blockers) ControlDecision(LedgerJournalEntryRecord record)
    {
        record.Entry.Metadata.Tags.Should().ContainKey("controls.eligibility");
        using var document = JsonDocument.Parse(record.Entry.Metadata.Tags!["controls.eligibility"]);
        var root = document.RootElement;
        var blockers = Property(root, "decisions").EnumerateArray()
            .Where(item => !Property(item, "allowed").GetBoolean())
            .Select(item => Property(item, "message").GetString()!).ToArray();
        var mode = Property(root, "mode");
        return (mode.ValueKind == JsonValueKind.String ? mode.GetString()! : ((LedgerBookControlModeDto)mode.GetInt32()).ToString(),
            Property(root, "version").GetInt64(), blockers);
    }

    private static JsonElement Property(JsonElement element, string name)
        => element.EnumerateObject().Single(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase)).Value;
}
