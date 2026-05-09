using FluentAssertions;
using Meridian.Application.Commands;
using Meridian.Application.LedgerTextJournal;
using Xunit;

namespace Meridian.Tests.Application.Commands;

public sealed class LedgerCliCommandTests
{
    [Fact]
    public void CanHandle_WithLedgerNamespace_ReturnsTrue()
    {
        var command = new LedgerCliCommand();

        command.CanHandle(new[] { "ledger", "-f", "ledger.dat", "balance" }).Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_Balance_PrintsIntroSampleBalancesAndZeroTotal()
    {
        var path = WriteJournal("""
            2004/09/29 Pacific Bell
                Expenses:Pacific Bell              $23.00
                Assets:Checking
            """);
        var command = new LedgerCliCommand();

        var result = await CaptureAsync(() => command.ExecuteAsync(new[] { "ledger", "-f", path, "balance" }));

        result.ExitCode.Should().Be(0);
        result.Out.Should().Contain("$-23.00  Assets:Checking");
        result.Out.Should().Contain("$23.00  Expenses:Pacific Bell");
        result.Out.Should().Contain("0");
        result.Err.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_Register_WithAccountFilter_PrintsPostingAndRunningBalance()
    {
        var path = WriteJournal("""
            2004/09/29 Pacific Bell
                Expenses:Pacific Bell              $23.00
                Assets:Checking
            """);
        var command = new LedgerCliCommand();

        var result = await CaptureAsync(() => command.ExecuteAsync(new[] { "ledger", "-f", path, "register", "checking" }));

        result.ExitCode.Should().Be(0);
        result.Out.Should().Contain("Pacific Bell");
        result.Out.Should().Contain("Assets:Checking");
        result.Out.Should().Contain("$-23.00");
        result.Err.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_Accounts_ListsParsedAccounts()
    {
        var path = WriteJournal("""
            2026-01-01 Opening balance
                Assets:Checking              $100.00
                Equity:Opening Balances
            """);
        var command = new LedgerCliCommand();

        var result = await CaptureAsync(() => command.ExecuteAsync(new[] { "ledger", "-f", path, "accounts" }));

        result.ExitCode.Should().Be(0);
        result.Out.Should().Contain("Assets:Checking");
        result.Out.Should().Contain("Equity:Opening Balances");
        result.Err.Should().BeEmpty();
    }

    [Fact]
    public void LedgerTextJournalParser_InferredPosting_BuildsMeridianLedgerAndRenderableReport()
    {
        var document = LedgerTextJournalParser.Parse([
            "2026/01/01 Opening balance",
            "    Assets:Checking              $100.00",
            "    Equity:Opening Balances",
        ]);

        document.Ledger.JournalEntryCount.Should().Be(1);
        document.Transactions.Single().Postings.Should().Contain(posting =>
            posting.Account.Name == "Equity:Opening Balances" &&
            posting.Amount == -100m &&
            posting.WasInferred);

        var report = LedgerTextReportRenderer.Render(
            document,
            new LedgerTextReportOptions("balance", null));

        report.Should().Contain("$100.00  Assets:Checking");
        report.Should().Contain("$-100.00  Equity:Opening Balances");
    }

    [Fact]
    public async Task ExecuteAsync_MissingFileOption_ReturnsValidationExitCode()
    {
        var command = new LedgerCliCommand();

        var result = await CaptureAsync(() => command.ExecuteAsync(new[] { "ledger", "balance" }));

        result.ExitCode.Should().Be(2);
        result.Err.Should().Contain("requires -f");
    }

    [Fact]
    public async Task ExecuteAsync_UnbalancedExplicitTransaction_ReturnsValidationExitCode()
    {
        var path = WriteJournal("""
            2026/01/01 Broken entry
                Assets:Checking              $100.00
                Income:Sales                 $-50.00
            """);
        var command = new LedgerCliCommand();

        var result = await CaptureAsync(() => command.ExecuteAsync(new[] { "ledger", "-f", path, "balance" }));

        result.ExitCode.Should().Be(2);
        result.Err.Should().Contain("line 1");
        result.Err.Should().Contain("not balanced");
    }

    [Fact]
    public async Task ExecuteAsync_UnsupportedDirective_ReturnsLineNumberedValidation()
    {
        var path = WriteJournal("include other.ledger");
        var command = new LedgerCliCommand();

        var result = await CaptureAsync(() => command.ExecuteAsync(new[] { "ledger", "-f", path, "balance" }));

        result.ExitCode.Should().Be(2);
        result.Err.Should().Contain("line 1");
    }

    [Fact]
    public async Task ExecuteAsync_TwoElidedPostings_ReturnsValidationExitCode()
    {
        var path = WriteJournal("""
            2026/01/01 Ambiguous entry
                Assets:Checking
                Equity:Opening Balances
            """);
        var command = new LedgerCliCommand();

        var result = await CaptureAsync(() => command.ExecuteAsync(new[] { "ledger", "-f", path, "balance" }));

        result.ExitCode.Should().Be(2);
        result.Err.Should().Contain("Only one posting amount may be elided");
    }

    private static string WriteJournal(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"meridian-ledger-{Guid.NewGuid():N}.ledger");
        File.WriteAllText(path, content.ReplaceLineEndings(Environment.NewLine));
        return path;
    }

    private static async Task<(int ExitCode, string Out, string Err)> CaptureAsync(Func<Task<CliResult>> action)
    {
        var originalOut = Console.Out;
        var originalErr = Console.Error;
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();

        try
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);
            var result = await action();
            return (result.ExitCode, stdout.ToString(), stderr.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalErr);
        }
    }
}
