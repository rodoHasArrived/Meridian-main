using System.Text.Json;
using Meridian.FinancialOperations.FundAdministration;
using Meridian.Ledger;

namespace Meridian.Tests.FinancialOperations;

public sealed class RecurringJournalEvidencePinStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "meridian-recurring-pin-tests", Guid.NewGuid().ToString("N"));
    private static readonly DateTimeOffset At = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Date = new(2026, 10, 1);
    private string Snapshot => Path.Combine(_directory, "recurring-journals.json");
    private FileRecurringJournalStore Store() => new(_directory);

    [Fact]
    public async Task Pin_IsDurableWriteOnce_EqualRetryAndConflictLeaveExactStoredBytes()
    {
        var claim = await SeedAsync();
        var digest = new string('a', 64);
        await using (var session = await Store().OpenSessionAsync())
        {
            var pinned = await session.PinDraftEvidenceAsync(claim.OccurrenceKey, digest);
            Assert.Equal(digest, pinned.DraftEvidenceDigest);
            await session.SetOutcomeAsync(claim.OccurrenceKey, RecurringOccurrenceState.Drafted, null, "period", null, null, At);
            var bytes = await File.ReadAllBytesAsync(Snapshot);
            Assert.Equal(digest, (await session.PinDraftEvidenceAsync(claim.OccurrenceKey, digest)).DraftEvidenceDigest);
            Assert.Equal(bytes, await File.ReadAllBytesAsync(Snapshot));
            await Assert.ThrowsAsync<InvalidOperationException>(() => session.PinDraftEvidenceAsync(claim.OccurrenceKey, new string('b', 64)));
            Assert.Equal(bytes, await File.ReadAllBytesAsync(Snapshot));
        }
        await using var restarted = await Store().OpenSessionAsync();
        var retained = Assert.Single(restarted.Occurrences);
        Assert.Equal(digest, retained.DraftEvidenceDigest);
        Assert.Equal(claim.DraftId, retained.DraftId);
        Assert.Equal(RecurringOccurrenceState.Drafted, retained.State);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FirstPin_AfterDraftedHistoryIsForbidden_EvenIfCurrentStateIsBlocked(bool blockedAfterDraft)
    {
        var claim = await SeedAsync();
        await using (var session = await Store().OpenSessionAsync())
        {
            await session.SetOutcomeAsync(claim.OccurrenceKey, RecurringOccurrenceState.Drafted, null, "period", null, null, At);
            if (blockedAfterDraft)
                await session.SetOutcomeAsync(claim.OccurrenceKey, RecurringOccurrenceState.Blocked, "review blocked", "period", null, null, At.AddMinutes(1));
            var bytes = await File.ReadAllBytesAsync(Snapshot);
            await Assert.ThrowsAsync<InvalidOperationException>(() => session.PinDraftEvidenceAsync(claim.OccurrenceKey, new string('a', 64)));
            Assert.Equal(bytes, await File.ReadAllBytesAsync(Snapshot));
            Assert.Null(Assert.Single(session.Occurrences).DraftEvidenceDigest);
        }
        await using var restarted = await Store().OpenSessionAsync();
        Assert.Null(Assert.Single(restarted.Occurrences).DraftEvidenceDigest);
    }

    [Fact]
    public async Task LegacyUnpinnedOccurrence_OmitsNewMember_AndRestartDoesNotRewriteEnvelope()
    {
        var claim = await SeedAsync();
        var bytes = await File.ReadAllBytesAsync(Snapshot);
        using (var document = JsonDocument.Parse(bytes))
        {
            var occurrence = Assert.Single(document.RootElement.GetProperty("document").GetProperty("occurrences").EnumerateArray());
            Assert.False(occurrence.TryGetProperty("draftEvidenceDigest", out _));
        }
        await using var restarted = await Store().OpenSessionAsync();
        var retained = await restarted.ClaimAsync("pin-schedule", Date, 1, 1, At.AddHours(1));
        Assert.Null(retained.DraftEvidenceDigest);
        Assert.Equal(claim.DraftId, retained.DraftId);
        Assert.Equal(RecurringJournalIdentity.SerializeSchedule(claim.ScheduleDefinition), RecurringJournalIdentity.SerializeSchedule(retained.ScheduleDefinition));
        Assert.Equal(RecurringJournalIdentity.SerializeTemplate(claim.TemplateDefinition), RecurringJournalIdentity.SerializeTemplate(retained.TemplateDefinition));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(Snapshot));
    }

    private async Task<RecurringOccurrenceRecord> SeedAsync()
    {
        var store = Store();
        await store.InitializeAsync();
        await using var session = await store.OpenSessionAsync();
        var template = new JournalTemplate("pin-template", "Fee", "Retained fee source",
        [
            new JournalTemplateLine(new LedgerAccount("Expenses:Fee", LedgerAccountType.Expense), JournalTemplateSide.Debit, "fee"),
            new JournalTemplateLine(new LedgerAccount("Liabilities:Fee", LedgerAccountType.Liability), JournalTemplateSide.Credit, "fee")
        ], ledgerBook: "Fund");
        var scope = new RecurringJournalScope("fund-a", Guid.NewGuid(), "entity-a", "USD", "tenant-a", "company-a");
        var schedule = new RecurringJournalSchedule("pin-schedule", template.TemplateId, new("fund-a", "Fund"),
            RecurringJournalCadence.Monthly, Date, "controller", At, new Dictionary<string, decimal> { ["fee"] = 100m });
        JournalEvidenceReference[] evidence = [new("contract", "vault://contract", "Contract", "DocumentVault", At,
            "controller", ContentHash: new string('c', 64))];
        await session.RegisterTemplateAsync(template, "controller", 0, At);
        await session.RegisterScheduleAsync(schedule, scope, evidence, "controller", 0, At);
        return await session.ClaimAsync(schedule.ScheduleId, Date, 1, 1, At);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }
}
