using System.Globalization;
using System.Text;

namespace Meridian.Application.LedgerTextJournal;

internal static class LedgerTextReportRenderer
{
    public static string Render(LedgerTextJournalDocument document, LedgerTextReportOptions options)
    {
        var builder = new StringBuilder();

        if (options.Report.Equals("accounts", StringComparison.OrdinalIgnoreCase))
        {
            AppendAccounts(builder, document);
        }
        else if (options.Report.Equals("balance", StringComparison.OrdinalIgnoreCase))
        {
            AppendBalance(builder, document, options.AccountFilter);
        }
        else if (options.Report.Equals("register", StringComparison.OrdinalIgnoreCase))
        {
            AppendRegister(builder, document, options.AccountFilter);
        }
        else
        {
            AppendPrint(builder, document);
        }

        return builder.ToString();
    }

    internal static string FormatAmount(decimal amount)
    {
        if (amount == 0m)
            return "0";

        var prefix = amount < 0m ? "$-" : "$";
        return prefix + Math.Abs(amount).ToString("0.00", CultureInfo.InvariantCulture);
    }

    private static void AppendAccounts(StringBuilder builder, LedgerTextJournalDocument document)
    {
        foreach (var account in document.Ledger.Accounts.OrderBy(account => account.Name, StringComparer.OrdinalIgnoreCase))
            builder.AppendLine(account.Name);
    }

    private static void AppendBalance(StringBuilder builder, LedgerTextJournalDocument document, string? filter)
    {
        decimal total = 0m;
        var balances = document.Transactions
            .SelectMany(transaction => transaction.Postings)
            .Where(posting => MatchesFilter(posting.Account.Name, filter))
            .GroupBy(posting => posting.Account.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => new { AccountName = group.First().Account.Name, Balance = group.Sum(posting => posting.Amount) })
            .OrderBy(row => row.AccountName, StringComparer.OrdinalIgnoreCase);

        foreach (var row in balances)
        {
            total += row.Balance;
            builder.AppendLine($"{FormatAmount(row.Balance),16}  {row.AccountName}");
        }

        builder.AppendLine("--------------------");
        builder.AppendLine($"{FormatAmount(total),16}");
    }

    private static void AppendRegister(StringBuilder builder, LedgerTextJournalDocument document, string? filter)
    {
        var running = 0m;
        foreach (var transaction in document.Transactions)
        {
            foreach (var posting in transaction.Postings.Where(posting => MatchesFilter(posting.Account.Name, filter)))
            {
                running += posting.Amount;
                builder.AppendLine(
                    $"{transaction.Date:yy-MMM-dd} {Truncate(transaction.Payee, 24),-24} {Truncate(posting.Account.Name, 28),-28} {FormatAmount(posting.Amount),12} {FormatAmount(running),12}");
            }
        }
    }

    private static void AppendPrint(StringBuilder builder, LedgerTextJournalDocument document)
    {
        foreach (var transaction in document.Transactions)
        {
            builder.AppendLine($"{transaction.Date:yyyy/MM/dd} {transaction.Payee}");
            foreach (var posting in transaction.Postings)
            {
                var amountSuffix = posting.WasInferred ? string.Empty : $"  {FormatAmount(posting.Amount)}";
                builder.AppendLine($"    {posting.Account.Name}{amountSuffix}");
            }

            builder.AppendLine();
        }
    }

    private static bool MatchesFilter(string accountName, string? filter)
        => string.IsNullOrWhiteSpace(filter) ||
           accountName.Contains(filter.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength];
}
