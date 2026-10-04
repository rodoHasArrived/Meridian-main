using FluentAssertions;
using Meridian.Ledger;
using Meridian.Storage.Ledger;

namespace Meridian.Tests.Storage;

public sealed partial class GovernedLedgerPostingTargetTests
{
    [Theory]
    [InlineData("postingActor", true)]
    [InlineData("POSTINGACTORATTRIBUTION", true)]
    [InlineData("postingActor", false)]
    [InlineData("postingActorAttribution", false)]
    public void Normalize_ReservedActorTagWithoutCommandActor_IsRejected(string tag, bool hasCommand)
    {
        var original = BuildCommandWrite();
        var write = original with
        {
            PostingCommand = hasCommand ? original.PostingCommand : null,
            Entry = CloneEntry(original.Entry, original.Entry.Metadata with
            {
                Tags = new Dictionary<string, string>(StringComparer.Ordinal) { [tag] = "caller-asserted" }
            })
        };
        var normalize = () => AccountingPostingCommandValidator.NormalizeAndValidate(write);
        normalize.Should().Throw<LedgerValidationException>().WithMessage("*Reserved posting actor metadata*");
    }

    [Theory]
    [InlineData("postingActor", "forged-actor")]
    [InlineData("postingActorAttribution", "untrusted-version")]
    public void Normalize_ActorTagsCannotOverrideTheCommand(string tag, string value)
    {
        var original = BuildCommandWrite();
        var write = original with
        {
            PostingCommand = original.PostingCommand! with { Actor = "posting-controller" },
            Entry = CloneEntry(original.Entry, original.Entry.Metadata with
            {
                Tags = new Dictionary<string, string> { [tag] = value }
            })
        };
        var normalize = () => AccountingPostingCommandValidator.NormalizeAndValidate(write);
        normalize.Should().Throw<LedgerValidationException>().WithMessage("*conflicts with the accounting posting command*");
    }

    [Fact]
    public async Task Normalize_CommandActorRetainsVersionedTagsAndExactRetryDoesNotAppend()
    {
        var original = BuildCommandWrite();
        original = original with { PostingCommand = original.PostingCommand! with { Actor = "posting-controller" } };
        var normalized = AccountingPostingCommandValidator.NormalizeAndValidate(original);
        normalized.Entry.Metadata.Tags!["postingActorAttribution"].Should().Be("command-v1");
        AccountingPostingCommandValidator.ReadRetainedPostingActor(normalized.Entry.Metadata).Should().Be("posting-controller");
        var twice = AccountingPostingCommandValidator.NormalizeAndValidate(normalized);
        twice.Entry.Metadata.Tags.Should().BeEquivalentTo(normalized.Entry.Metadata.Tags);
        var retained = new List<LedgerJournalEntryRecord> { ToRecord(normalized) };
        var store = BuildStore(retained, _ => throw new InvalidOperationException("replay must not append"));
        using var target = new DurableLedgerPostingTarget(store.Object);
        (await target.PostAsync(original)).WasAppended.Should().BeFalse();
        var differentActor = () => target.PostAsync(original with
        {
            PostingCommand = original.PostingCommand! with { Actor = "later-controller" }
        });
        await differentActor.Should().ThrowAsync<LedgerValidationException>();
    }

    [Fact]
    public void ReadRetainedActor_UnversionedLegacyTagNeverSuppliesAttribution()
    {
        var metadata = BuildWrite().Entry.Metadata with
        {
            Tags = new Dictionary<string, string> { ["postingActor"] = "unverified-legacy-metadata" }
        };
        AccountingPostingCommandValidator.ReadRetainedPostingActor(metadata).Should().BeNull();
    }

    [Theory]
    [InlineData("command-v2", "actor")]
    [InlineData("command-v1", "")]
    public void ReadRetainedActor_UnsupportedOrIncompleteVersionedAttributionFails(string version, string actor)
    {
        var metadata = BuildWrite().Entry.Metadata with
        {
            Tags = new Dictionary<string, string>
            {
                ["postingActor"] = actor,
                ["postingActorAttribution"] = version
            }
        };
        var read = () => AccountingPostingCommandValidator.ReadRetainedPostingActor(metadata);
        read.Should().Throw<LedgerValidationException>().WithMessage("*incomplete or unsupported*");
    }
}
