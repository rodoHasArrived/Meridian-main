using FluentAssertions;
using Meridian.Contracts.Ledger;
using Meridian.Storage.Ledger;
using Meridian.Tests.Storage;
using Meridian.Ui.Shared.Services;

namespace Meridian.Tests.Ui;

public sealed partial class AccountingConfigurationServiceTests
{
    /// <summary>
    /// Two hosts share one PostgreSQL draft table (ledger migration 077). Their concurrent edits of the same
    /// draft version are serialized by the shared PostgreSQL mutation lease: exactly one wins and the other is
    /// refused as stale, so neither silently overwrites the other.
    /// </summary>
    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task PostgresDrafts_ConcurrentEditsFromTwoHosts_OneWinsAndTheOtherIsRefusedAsStale()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var configuration = CreateService();
        await SeedBalancedConfigurationAsync(configuration);
        var hostA = CreateManualJournalEntryWorkbenchService(configuration,
            draftStore: new PostgresManualJournalEntryDraftStore(database.Options));
        var hostB = CreateManualJournalEntryWorkbenchService(configuration,
            draftStore: new PostgresManualJournalEntryDraftStore(database.Options));

        var saved = await hostA.SaveDraftAsync(new SaveManualJournalEntryDraftRequest(BalancedManualJournalEntry(), "ops-user"));
        var fromB = await hostB.GetWorkbenchAsync(saved.FundProfileId);
        fromB.Drafts.Should().ContainSingle(item => item.JournalEntryId == saved.JournalEntryId);

        var edits = await Task.WhenAll(
            Attempt(hostA, saved with { Memo = "Edited on host A" }, "corr-a"),
            Attempt(hostB, saved with { Memo = "Edited on host B" }, "corr-b"));

        edits.Count(error => error is null).Should().Be(1, string.Join(" | ", edits.Select(error => error?.Message)));
        edits.Single(error => error is not null)!.Message.Should().Contain("stale");
        var retained = await new PostgresManualJournalEntryDraftStore(database.Options).GetAsync(saved.FundProfileId, saved.JournalEntryId);
        retained!.Version.Should().Be(saved.Version + 1);
        retained.Memo.Should().BeOneOf("Edited on host A", "Edited on host B");

        static async Task<Exception?> Attempt(ManualJournalEntryWorkbenchService host, ManualJournalEntryDraftDto draft, string correlation)
        {
            try
            {
                await host.SaveDraftAsync(new SaveManualJournalEntryDraftRequest(draft, "ops-user", CorrelationId: correlation,
                    LedgerBookId: draft.LedgerBookId));
                return null;
            }
            catch (InvalidOperationException error)
            {
                return error;
            }
        }
    }
}
