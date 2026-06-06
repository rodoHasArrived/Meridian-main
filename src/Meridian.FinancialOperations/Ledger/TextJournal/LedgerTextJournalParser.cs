using System.Globalization;
using Meridian.Ledger;
using MeridianLedger = Meridian.Ledger.Ledger;

namespace Meridian.FinancialOperations.LedgerTextJournal;

public static class LedgerTextJournalParser
{
    private static readonly string[] s_dateFormats = ["yyyy/MM/dd", "yyyy-MM-dd"];

    public static LedgerTextJournalDocument Parse(IReadOnlyList<string> lines)
    {
        var ledger = new MeridianLedger();
        var transactions = new List<LedgerTextTransaction>();
        PendingTransaction? current = null;

        for (var index = 0; index < lines.Count; index++)
        {
            var lineNumber = index + 1;
            var line = lines[index];
            var trimmed = line.Trim();

            if (trimmed.Length == 0)
            {
                Flush();
                continue;
            }

            if (trimmed.StartsWith(';') || trimmed.StartsWith('#'))
                continue;

            if (char.IsWhiteSpace(line[0]))
            {
                if (current is null)
                    throw new LedgerTextJournalException(lineNumber, "Posting line appeared before a transaction header.");

                current.Postings.Add(ParsePosting(trimmed, lineNumber));
                continue;
            }

            Flush();
            if (IsUnsupportedDirective(trimmed))
                throw new LedgerTextJournalException(lineNumber, $"Unsupported Ledger directive '{trimmed.Split(' ', 2)[0]}'.");

            current = ParseHeader(trimmed, lineNumber);
        }

        Flush();
        return new LedgerTextJournalDocument(ledger, transactions);

        void Flush()
        {
            if (current is null)
                return;

            var transaction = MaterializeTransaction(current);
            transactions.Add(transaction);
            PostToLedger(ledger, transaction);
            current = null;
        }
    }

    private static void PostToLedger(MeridianLedger ledger, LedgerTextTransaction transaction)
    {
        var journalId = Guid.NewGuid();
        ledger.Post(new JournalEntry(
            journalId,
            transaction.Date,
            transaction.Payee,
            transaction.Postings
                .Select(posting => new LedgerEntry(
                    Guid.NewGuid(),
                    journalId,
                    transaction.Date,
                    posting.Account,
                    posting.Amount > 0m ? posting.Amount : 0m,
                    posting.Amount < 0m ? Math.Abs(posting.Amount) : 0m,
                    transaction.Payee))
                .ToArray()));
    }

    private static PendingTransaction ParseHeader(string line, int lineNumber)
    {
        var splitAt = line.IndexOf(' ');
        if (splitAt <= 0)
            throw new LedgerTextJournalException(lineNumber, "Transaction header must contain a date and payee.");

        var dateToken = line[..splitAt];
        var payee = line[(splitAt + 1)..].Trim();
        if (payee.Length == 0)
            throw new LedgerTextJournalException(lineNumber, "Transaction header payee must not be empty.");

        if (!DateTimeOffset.TryParseExact(
                dateToken,
                s_dateFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var date))
        {
            throw new LedgerTextJournalException(lineNumber, $"Unsupported transaction date '{dateToken}'. Use yyyy/MM/dd or yyyy-MM-dd.");
        }

        return new PendingTransaction(date, payee, lineNumber);
    }

    private static bool IsUnsupportedDirective(string line)
    {
        var token = line.Split(' ', 2)[0];
        return token.StartsWith('!') ||
               token.Equals("include", StringComparison.OrdinalIgnoreCase) ||
               token.Equals("account", StringComparison.OrdinalIgnoreCase) ||
               token.Equals("commodity", StringComparison.OrdinalIgnoreCase) ||
               token.Equals("P", StringComparison.OrdinalIgnoreCase);
    }

    private static PendingPosting ParsePosting(string line, int lineNumber)
    {
        var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            throw new LedgerTextJournalException(lineNumber, "Posting line must not be empty.");

        if (TryParseAmount(parts[^1], out var amount))
        {
            if (parts.Length == 1)
                throw new LedgerTextJournalException(lineNumber, "Posting line with an amount must also include an account.");

            return new PendingPosting(lineNumber, string.Join(' ', parts[..^1]), amount);
        }

        if (parts.Any(part => part.Contains('$', StringComparison.Ordinal)))
            throw new LedgerTextJournalException(lineNumber, $"Unsupported amount syntax '{parts[^1]}'.");

        return new PendingPosting(lineNumber, line, null);
    }

    private static LedgerTextTransaction MaterializeTransaction(PendingTransaction pending)
    {
        if (pending.Postings.Count == 0)
            throw new LedgerTextJournalException(pending.LineNumber, "Transaction must contain at least one posting.");

        var elided = pending.Postings.Where(posting => posting.Amount is null).ToArray();
        if (elided.Length > 1)
            throw new LedgerTextJournalException(elided[1].LineNumber, "Only one posting amount may be elided per transaction.");

        var explicitTotal = pending.Postings.Sum(posting => posting.Amount ?? 0m);
        var postings = new List<LedgerTextPosting>(pending.Postings.Count);

        foreach (var posting in pending.Postings)
        {
            var amount = posting.Amount ?? -explicitTotal;
            var account = new LedgerAccount(posting.AccountName, InferAccountType(posting.AccountName, posting.LineNumber));
            postings.Add(new LedgerTextPosting(posting.LineNumber, account, amount, posting.Amount is null));
        }

        if (elided.Length == 0 && explicitTotal != 0m)
            throw new LedgerTextJournalException(pending.LineNumber, $"Transaction is not balanced; posting amounts total {LedgerTextReportRenderer.FormatAmount(explicitTotal)}.");

        return new LedgerTextTransaction(pending.Date, pending.Payee, postings);
    }

    private static LedgerAccountType InferAccountType(string accountName, int lineNumber)
    {
        var root = accountName.Split(':', 2)[0];
        return root.ToLowerInvariant() switch
        {
            "asset" or "assets" => LedgerAccountType.Asset,
            "liability" or "liabilities" => LedgerAccountType.Liability,
            "equity" => LedgerAccountType.Equity,
            "income" or "revenue" or "revenues" => LedgerAccountType.Revenue,
            "expense" or "expenses" => LedgerAccountType.Expense,
            _ => throw new LedgerTextJournalException(lineNumber, $"Unsupported account root '{root}'. Use Assets, Liabilities, Equity, Income/Revenue, or Expenses.")
        };
    }

    private static bool TryParseAmount(string token, out decimal amount)
    {
        var normalized = token.Trim().Replace(",", string.Empty, StringComparison.Ordinal);
        var negative = false;

        if (normalized.StartsWith("-$", StringComparison.Ordinal))
        {
            negative = true;
            normalized = normalized[2..];
        }
        else
        {
            if (normalized.StartsWith('$'))
                normalized = normalized[1..];

            if (normalized.StartsWith('-'))
            {
                negative = true;
                normalized = normalized[1..];
            }
        }

        if (!decimal.TryParse(normalized, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out amount))
            return false;

        if (negative)
            amount = -amount;

        return true;
    }

    private sealed record PendingTransaction(DateTimeOffset Date, string Payee, int LineNumber)
    {
        public List<PendingPosting> Postings { get; } = [];
    }

    private sealed record PendingPosting(int LineNumber, string AccountName, decimal? Amount);
}
