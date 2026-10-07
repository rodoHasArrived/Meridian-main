using FluentAssertions;
using Meridian.Ledger;
using Meridian.Storage.Ledger;

namespace Meridian.Tests.Storage;

public sealed partial class AtomicTaxLotJournalStoreTests
{
    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task DisposalHistory_RetainsOriginalPolicyRevisionAndRecordedTimeAfterPolicyChanges()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var (command, _, _) = await PrepareProceedsDisposalAsync(database, precisePrice: false);
        var posted = await database.JournalStore.AppendAssetPostingAsync(command);
        var originalPolicy = (await database.JournalStore.ListTaxLotPoliciesAsync(command.LedgerBookId)).Single();

        // The configured row is mutable, even when its identity and effective date stay the same.
        // A retained result must not substitute this policy's new revision or activation settings.
        await database.JournalStore.SaveTaxLotPolicyAsync(originalPolicy with
        {
            PolicyId = "tax-policy-v2",
            ReliefMethod = LedgerTaxLotReliefMethod.Lifo,
            WashSalePolicy = new(true, 45, WashSaleReplacementScope.DisposingAccount, new(2026, 5, 1)),
            UpdatedAt = originalPolicy.UpdatedAt.AddDays(15)
        });

        var restarted = new PostgresLedgerJournalStore(database.Options);
        var retained = (await restarted.GetTaxLotDisposalHistoryAsync(command.LedgerBookId,
            [command.Journal.Entry.JournalEntryId])).Single();

        retained.PolicyRevision.Should().Be("tax-policy-v1");
        retained.ReliefMethod.Should().Be(LedgerTaxLotReliefMethod.Fifo);
        retained.RecordedAt.Should().Be(posted.Mutations[0].RecordedAt);
        retained.WashSaleBasisIncreases.Should().BeEmpty("no retained deferral proves any wash-sale policy settings");
    }
}
