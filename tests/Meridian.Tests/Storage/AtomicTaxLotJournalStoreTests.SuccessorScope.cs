using FluentAssertions;
using Meridian.Contracts.AssetOperations;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Xunit;

namespace Meridian.Tests.Storage;

public sealed partial class AtomicTaxLotJournalStoreTests
{
    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task CorporateActionSuccessors_ReviewedTargetVersionCannotAuthorizeForeignPolicy()
        => await AssertSuccessorForeignScopeRefusedAsync("policy");

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task CorporateActionSuccessors_ReviewedTargetVersionCannotAuthorizeForeignFinancialAccount()
        => await AssertSuccessorForeignScopeRefusedAsync("financial-account");

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task CorporateActionSuccessors_ReviewedTargetVersionCannotAuthorizeForeignSleeve()
        => await AssertSuccessorForeignScopeRefusedAsync("sleeve");

    private static async Task AssertSuccessorForeignScopeRefusedAsync(string changedScope)
    {
        await using var fixture = await AmortFixture.CreateAsync(premium: true);
        var command = await SuccessorCommandAsync(fixture, advanceRefunding: false);
        var reviewed = command.CorporateAction!;
        var target = reviewed.Successors.Single();
        var snapshot = await fixture.Positions.GetSecurityAsync(target.Lot.SecurityId);
        var position = snapshot.BookPositions.Single();
        var changed = changedScope switch
        {
            "policy" => position with { BookContext = position.BookContext with { AccountingPolicyId = "foreign-policy" } },
            "financial-account" => position with { PrimaryAccountId = "foreign-account" },
            "sleeve" => position with { BookContext = position.BookContext with { Dimensions = new(SleeveId: "foreign-sleeve") } },
            _ => throw new ArgumentOutOfRangeException(nameof(changedScope))
        };
        changed = await fixture.Positions.UpsertAsync(snapshot.InstrumentRoles.Single(), changed with { Version = position.Version + 1 },
            null, position.Version, new("independent-controller", "evidence://successor-scope-review", "Retain a separately scoped position", DateTimeOffset.UtcNow));
        reviewed = reviewed with { Successors = [target with { ExpectedBookPositionVersion = changed.Version }] };
        var entry = command.Journal.Entry;
        var tags = new Dictionary<string, string>(entry.Metadata.Tags!)
        { [Meridian.Contracts.Accounting.Lots.OpenLotSuccessors.JournalFingerprintTag] = Meridian.Contracts.Accounting.Lots.OpenLotSuccessors.Fingerprint(reviewed) };
        var changedJournal = command.Journal with
        {
            Entry = new JournalEntry(entry.JournalEntryId, entry.Timestamp, entry.Description, entry.Lines, entry.Metadata with { Tags = tags }),
            PostingCommand = command.Journal.PostingCommand! with { LotCorporateAction = reviewed }
        };
        command = (command with { CorporateAction = reviewed, Journal = changedJournal }).WithComputedFingerprint();

        var post = () => fixture.Store.AppendAssetPostingAsync(command);

        await post.Should().ThrowAsync<LedgerValidationException>();
        await AssertSuccessorUnchangedAsync(fixture, command);
    }
}
