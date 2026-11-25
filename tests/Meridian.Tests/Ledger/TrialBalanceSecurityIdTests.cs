using FluentAssertions;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Xunit;

namespace Meridian.Tests.Ledger;

/// <summary>
/// Security Master review F2: the period trial balance carries the account SecurityId, so a
/// SecurityId-keyed holding is one row across a ticker rename and never merges with a legacy
/// ticker-keyed row.
/// </summary>
public sealed class TrialBalanceSecurityIdTests
{
    private static readonly Guid MetaSecurityId = Guid.Parse("f2f2f2f2-0000-4000-8000-0000000000c3");
    private static readonly DateTimeOffset At = new(2026, 6, 1, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ProjectTrialBalanceCheckpoint_RenamedHoldingIsOneSecurityIdRow_SeparateFromLegacyTickerRow()
    {
        var beforeRename = LedgerAccounts.Securities(LedgerSecurityKey.Resolved(MetaSecurityId, "FB"), "broker-1");
        var afterRename = LedgerAccounts.Securities(LedgerSecurityKey.Resolved(MetaSecurityId, "META"), "broker-1");
        var legacy = LedgerAccounts.Securities(LedgerSecurityKey.Unresolved("FB"), "broker-1");
        var cash = LedgerAccounts.CashAccount("broker-1");

        var rows = PostgresLedgerBookService.ProjectTrialBalanceCheckpoint(
        [
            Record(1, (beforeRename, 3_000m, 0m), (cash, 0m, 3_000m)),
            Record(2, (afterRename, 1_600m, 0m), (cash, 0m, 1_600m)),
            Record(3, (legacy, 500m, 0m), (cash, 0m, 500m))
        ]);

        var securities = rows.Where(row => row.AccountName == LedgerAccounts.SecuritiesAccountName).ToArray();
        securities.Should().HaveCount(2);
        securities.Single(row => row.SecurityId == MetaSecurityId).Balance.Should().Be(4_600m,
            "both tickers book to the one SecurityId-keyed account");
        securities.Single(row => row.SecurityId is null).Should().Match<Meridian.Contracts.Ledger.LedgerPeriodTrialBalanceLineDto>(
            row => row.Symbol == "FB" && row.Balance == 500m);
    }

    [Fact]
    public void TrialBalanceRow_SecurityId_IsOmittedFromJsonWhenNull()
    {
        var legacyRow = new Meridian.Contracts.Ledger.LedgerPeriodTrialBalanceLineDto(
            "Securities", "Asset", "FB", null, 1m, 0m, 1m, 1);
        var options = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);

        System.Text.Json.JsonSerializer.Serialize(legacyRow, options).Should().NotContain("securityId");
        System.Text.Json.JsonSerializer.Serialize(legacyRow with { SecurityId = MetaSecurityId }, options)
            .Should().Contain(MetaSecurityId.ToString("D"));
    }

    private static LedgerJournalEntryRecord Record(long sequence, params (LedgerAccount Account, decimal Debit, decimal Credit)[] lines)
    {
        var journalId = Guid.NewGuid();
        var timestamp = At.AddMinutes(sequence);
        var entry = new JournalEntry(journalId, timestamp, $"entry {sequence}", lines.Select(line =>
            new LedgerEntry(Guid.NewGuid(), journalId, timestamp, line.Account, line.Debit, line.Credit, $"entry {sequence}")).ToArray());
        return new LedgerJournalEntryRecord(entry, Guid.NewGuid(), Guid.NewGuid(), null, null, sequence, timestamp);
    }
}
