using Meridian.Ledger;
using Meridian.Storage.Ledger;

namespace Meridian.Tests.Storage;

public sealed partial class GovernedLedgerPostingTargetTests
{
    [Fact]
    public async Task PostAsync_ExactReplayPreservesAppendOnlyEntityControlReceipt()
    {
        var write = BuildCommandWrite();
        var normalized = AccountingPostingCommandValidator.NormalizeAndValidate(write);
        var tags = new Dictionary<string, string>(normalized.Entry.Metadata.Tags!, StringComparer.OrdinalIgnoreCase)
        {
            ["controls.eligibility"] = "{\"mode\":\"Enforced\",\"decisions\":[]}"
        };
        var retainedWrite = normalized with { Entry = CloneEntry(normalized.Entry, normalized.Entry.Metadata with { Tags = tags }) };
        var retained = new List<LedgerJournalEntryRecord> { ToRecord(retainedWrite) };
        var store = BuildStore(retained, _ => throw new InvalidOperationException("Exact replay cannot append again."));
        using var target = new DurableLedgerPostingTarget(store.Object);

        var result = await target.PostAsync(write);

        Assert.False(result.WasAppended);
        Assert.Single(retained);
        Assert.Equal(tags["controls.eligibility"], retained[0].Entry.Metadata.Tags!["controls.eligibility"]);
    }
}
