using System.Text.Json;
using FluentAssertions;
using Meridian.Ledger;
using Meridian.Storage.Ledger;

namespace Meridian.Tests.Storage;

/// <summary>
/// Security Master review F2: per-security accounts are keyed by SecurityId, and existing journals
/// keep their ticker keys. A replay against a journal retained before F2 must still verify, while
/// SecurityId-keyed journals compare strictly on SecurityId.
/// </summary>
public sealed partial class GovernedLedgerPostingTargetTests
{
    private static readonly Guid AaplSecurityId = Guid.Parse("55555555-5555-5555-5555-555555555555");

    [Fact]
    public void VerifyRetainedEntry_LegacyTickerKeyedJournal_AcceptsSecurityIdKeyedReplay()
    {
        var legacy = BuildWrite();
        var replay = KeySecuritiesLines(legacy, AaplSecurityId, "AAPL");

        var verify = () => GovernedLedgerPostingTarget.VerifyRetainedEntry(ToRecord(legacy), replay);

        verify.Should().NotThrow("a journal retained before SecurityId keying keeps its ticker key");
    }

    [Fact]
    public void VerifyRetainedEntry_LegacyTickerKeyedJournal_RejectsReplayForDifferentTicker()
    {
        var legacy = BuildWrite();
        var replay = KeySecuritiesLines(legacy, AaplSecurityId, "MSFT");

        var verify = () => GovernedLedgerPostingTarget.VerifyRetainedEntry(ToRecord(legacy), replay);

        verify.Should().Throw<LedgerValidationException>()
            .WithMessage("*already retained with different accounting content*");
    }

    [Fact]
    public void VerifyRetainedEntry_SecurityIdKeyedJournal_RejectsDifferentSecurity()
    {
        var retained = KeySecuritiesLines(BuildWrite(), AaplSecurityId, "AAPL");
        var replay = KeySecuritiesLines(BuildWrite(), Guid.NewGuid(), "AAPL");

        var verify = () => GovernedLedgerPostingTarget.VerifyRetainedEntry(ToRecord(retained), replay);

        verify.Should().Throw<LedgerValidationException>(
            "a SecurityId-keyed account is a different account from any other security's, whatever the ticker");
    }

    [Fact]
    public void VerifyRetainedEntry_SecurityIdKeyedJournal_RejectsLegacyTickerReplay()
    {
        var retained = KeySecuritiesLines(BuildWrite(), AaplSecurityId, "AAPL");
        var legacyReplay = BuildWrite();

        var verify = () => GovernedLedgerPostingTarget.VerifyRetainedEntry(ToRecord(retained), legacyReplay);

        verify.Should().Throw<LedgerValidationException>(
            "a journal already keyed by SecurityId must not be satisfied by a ticker-keyed replay");
    }

    [Fact]
    public void VerifyRetainedEntry_SecurityIdKeyedJournal_IgnoresDisplayTickerRename()
    {
        var retained = KeySecuritiesLines(BuildWrite(), AaplSecurityId, "AAPL");
        var renamed = KeySecuritiesLines(BuildWrite(), AaplSecurityId, "AAPLX");

        var verify = () => GovernedLedgerPostingTarget.VerifyRetainedEntry(ToRecord(retained), renamed);

        verify.Should().NotThrow("the ticker is a display attribute of a SecurityId-keyed account");
    }

    [Fact]
    public void LedgerAccount_LegacyKeyJson_IsUnchangedSoRetainedFingerprintsReplay()
    {
        // Atomic tax-lot fingerprints, lot snapshots and paper journals reflection-serialize
        // LedgerAccount. A legacy (null SecurityId) account must serialize exactly as before F2.
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var legacy = LedgerAccounts.Securities(LedgerSecurityKey.Unresolved("aapl"), "broker-alpha");

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(legacy, options));

        document.RootElement.EnumerateObject().Select(static property => property.Name)
            .Should().Equal("name", "accountType", "symbol", "financialAccountId");
    }

    [Fact]
    public void LedgerAccount_SecurityIdKeyJson_RoundTripsTheKey()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var keyed = LedgerAccounts.Securities(LedgerSecurityKey.Resolved(AaplSecurityId, "AAPL"), "broker-alpha");

        var json = JsonSerializer.Serialize(keyed, options);
        var roundTripped = JsonSerializer.Deserialize<LedgerAccount>(json, options);

        json.Should().Contain("\"securityId\":\"55555555-5555-5555-5555-555555555555\"")
            .And.NotContain("isSecurityKeyed");
        roundTripped.Should().Be(keyed);
        roundTripped!.SecurityId.Should().Be(AaplSecurityId);
    }

    private static LedgerJournalEntryWrite KeySecuritiesLines(LedgerJournalEntryWrite write, Guid securityId, string ticker)
        => WithLines(write, line => line.Account.Name != LedgerAccounts.SecuritiesAccountName
            ? line
            : new LedgerEntry(
                line.EntryId,
                line.JournalEntryId,
                line.Timestamp,
                LedgerAccounts.Securities(LedgerSecurityKey.Resolved(securityId, ticker), line.Account.FinancialAccountId),
                line.Debit,
                line.Credit,
                line.Description,
                line.Dimensions,
                line.Currency)
            {
                Memo = line.Memo,
                ChartAccount = line.ChartAccount
            });
}
