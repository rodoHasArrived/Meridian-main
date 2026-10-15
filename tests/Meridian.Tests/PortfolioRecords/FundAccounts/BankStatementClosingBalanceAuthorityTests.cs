using FluentAssertions;
using Meridian.Contracts.FundStructure;
using Meridian.PortfolioRecords.FundAccounts;

namespace Meridian.Tests.PortfolioRecords.FundAccounts;

public sealed class BankStatementClosingBalanceAuthorityTests
{
    [Fact]
    public void StatementDate_ExposesHeaderDateWithLegacyValueDateFallback()
    {
        var line = Line(90m);
        line.StatementDate.Should().Be(line.ValueDate);
        (line with { StatementAsOfDate = line.ValueDate.AddDays(3) }).StatementDate.Should().Be(line.ValueDate.AddDays(3));
    }

    [Fact]
    public void SelectAuthoritativeLines_ReplacesWholeStatementBeforeFilteringCurrencyOrSummingFlows()
    {
        var original = Line(90m) with { StatementRevision = 1, LineSequence = 1 };
        var correction = original with { BatchId = Guid.NewGuid(), Currency = "EUR", StatementRevision = 2, Amount = 20m };
        var selected = BankStatementClosingBalanceAuthority.SelectAuthoritativeLines([correction, original]);

        selected.Should().Equal(correction);
        BankStatementClosingBalanceAuthority.SelectClosingBalance([correction, original], "USD").Should().BeNull();
    }

    [Fact]
    public void SelectAuthoritativeLines_CompetingBatchesWithEqualClosingBalances_RemainUnavailable()
    {
        var line = Line(90m) with { StatementRevision = 1, LineSequence = 1 };
        var other = line with { BatchId = Guid.NewGuid(), Amount = 20m };
        BankStatementClosingBalanceAuthority.SelectAuthoritativeLines([line, other]).Should().BeNull();
        BankStatementClosingBalanceAuthority.SelectClosingBalance([line, other]).Should().BeNull();
        BankStatementClosingBalanceAuthority.SelectLatestLine([line, other]).Should().BeNull();
    }

    [Fact]
    public void SelectLatestLine_UsesRevisionAndSourceOrdinalForBackdatedFinalRow()
    {
        var line = Line(90m) with { StatementAsOfDate = new(2026, 10, 7), StatementRevision = 1, LineSequence = 99 };
        var correction = line with { BatchId = Guid.NewGuid(), StatementRevision = 2, LineSequence = 1 };
        var final = correction with { LineId = Guid.NewGuid(), LineSequence = 2, ValueDate = line.ValueDate.AddDays(-1), ClosingBalance = 100m };
        BankStatementClosingBalanceAuthority.SelectLatestLine([final, line, correction]).Should().Be(final);
        BankStatementClosingBalanceAuthority.LatestStatementDate([final]).Should().Be(new DateOnly(2026, 10, 7));
    }

    [Fact]
    public async Task SourceAuthority_PersistsAcrossLocalServiceRestart()
    {
        var path = Path.Combine(Path.GetTempPath(), $"meridian-statement-authority-{Guid.NewGuid():N}.json");
        try
        {
            var service = new InMemoryFundAccountService(path);
            var accountId = Guid.NewGuid();
            var date = new DateOnly(2026, 10, 7);
            await service.CreateAccountAsync(new(accountId, AccountTypeDto.Bank, $"BANK-{accountId:N}", "Bank", "USD", DateTimeOffset.UtcNow, "ops"));
            foreach (var revision in new[] { 2, 1 })
            {
                var batchId = Guid.NewGuid();
                var line = Line(revision == 2 ? 100m : 90m) with { AccountId = accountId, BatchId = batchId, LineSequence = 10 };
                await service.IngestBankStatementAsync(new(batchId, accountId, date, "Bank", null, [line], "ops", StatementRevision: revision));
            }

            var restarted = new InMemoryFundAccountService(path);
            var retained = await restarted.GetBankStatementLinesAsync(accountId);
            retained.Should().HaveCount(2);
            retained.Should().OnlyContain(line => line.LineSequence == 10 && line.StatementAsOfDate == date);
            BankStatementClosingBalanceAuthority.SelectClosingBalance(retained).Should().Be(100m);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void SelectClosingBalance_ConflictingLegacyRowsWithoutSequence_RemainsUnavailable()
    {
        var line = Line(90m);
        BankStatementClosingBalanceAuthority.SelectClosingBalance([line, line with { LineId = Guid.NewGuid(), ClosingBalance = 100m }])
            .Should().BeNull();
    }

    [Fact]
    public void SelectClosingBalance_EqualRevisionConflictingBatches_RemainsUnavailableRegardlessOfOrder()
    {
        var line = Line(90m) with { LineSequence = 1, StatementRevision = 1 };
        var other = line with { BatchId = Guid.NewGuid(), ClosingBalance = 100m };
        BankStatementClosingBalanceAuthority.SelectClosingBalance([line, other]).Should().BeNull();
        BankStatementClosingBalanceAuthority.SelectClosingBalance([other, line]).Should().BeNull();
    }

    [Fact]
    public void SelectClosingBalance_CorrectionWithoutClosingBalance_DoesNotReuseOldRevision()
    {
        var line = Line(90m) with { LineSequence = 1, StatementRevision = 1 };
        var correction = line with { BatchId = Guid.NewGuid(), StatementRevision = 2, ClosingBalance = null };
        BankStatementClosingBalanceAuthority.SelectClosingBalance([line, correction]).Should().BeNull();
    }

    [Fact]
    public void SelectClosingBalance_StatementDatePrecedesRevision_AndSourceSequencePrecedesValueDate()
    {
        var date = new DateOnly(2026, 10, 7);
        var oldStatement = Line(80m) with { StatementAsOfDate = date, StatementRevision = 20, LineSequence = 100 };
        var earlierValue = oldStatement with
        {
            BatchId = Guid.NewGuid(),
            StatementAsOfDate = date.AddDays(1),
            StatementRevision = 1,
            ValueDate = date,
            ClosingBalance = 90m,
            LineSequence = 1
        };
        var final = earlierValue with { LineId = Guid.NewGuid(), ValueDate = date.AddDays(-1), ClosingBalance = 100m, LineSequence = 2 };
        BankStatementClosingBalanceAuthority.SelectClosingBalance([final, oldStatement, earlierValue]).Should().Be(100m);
    }

    private static BankStatementLineDto Line(decimal balance) => new(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new(2026, 10, 7), new(2026, 10, 7),
        10m, "USD", "Deposit", "Deposit", null, balance);
}
