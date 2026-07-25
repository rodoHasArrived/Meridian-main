using System.Security.Cryptography;
using System.Text;
using Meridian.Contracts.Ledger;
using Meridian.Ledger;

namespace Meridian.FinancialOperations.Ledger;

internal static class AssetAccountingCandidateCanonicalizer
{
    public static AccountingPostingCandidateWriteResult Canonicalize(
        AccountingPostingCandidateWriteResult result,
        Guid ledgerBookId,
        Guid eventId,
        long eventVersion,
        long draftSpineVersion)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Write is not { } write)
        {
            return result;
        }

        var journalId = CreateId(
            "asset-accounting-journal",
            ledgerBookId.ToString("D"),
            eventId.ToString("D"),
            eventVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
            draftSpineVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var idMap = new Dictionary<Guid, Guid>();
        var lines = write.Entry.Lines.Select((line, index) =>
        {
            var lineId = CreateId(
                "asset-accounting-journal-line",
                journalId.ToString("D"),
                index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                line.Account.ToString(),
                line.Debit.ToString(System.Globalization.CultureInfo.InvariantCulture),
                line.Credit.ToString(System.Globalization.CultureInfo.InvariantCulture));
            idMap[line.EntryId] = lineId;
            return new LedgerEntry(
                lineId,
                journalId,
                line.Timestamp,
                line.Account,
                line.Debit,
                line.Credit,
                line.Description,
                line.Dimensions,
                line.Currency);
        }).ToArray();

        var tags = write.Entry.Metadata.Tags is null
            ? null
            : write.Entry.Metadata.Tags.ToDictionary(
                pair => RewriteLineDimensionTag(pair.Key, idMap),
                static pair => pair.Value,
                StringComparer.OrdinalIgnoreCase);
        var entry = new JournalEntry(
            journalId,
            write.Entry.Timestamp,
            write.Entry.Description,
            lines,
            write.Entry.Metadata with { Tags = tags });
        var canonicalWrite = write with { Entry = entry };
        var canonicalCandidate = result.Candidate with
        {
            JournalEntryId = journalId,
            PostingCommand = canonicalWrite.PostingCommand
        };
        return new AccountingPostingCandidateWriteResult(canonicalCandidate, canonicalWrite);
    }

    private static string RewriteLineDimensionTag(string key, IReadOnlyDictionary<Guid, Guid> idMap)
    {
        const string prefix = "lineDimensions.";
        if (!key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return key;
        }

        var idStart = prefix.Length;
        var separator = key.IndexOf('.', idStart);
        if (separator <= idStart ||
            !Guid.TryParseExact(key[idStart..separator], "N", out var priorId) ||
            !idMap.TryGetValue(priorId, out var canonicalId))
        {
            return key;
        }

        return $"{prefix}{canonicalId:N}{key[separator..]}";
    }

    private static Guid CreateId(params string[] components)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|', components)));
        return new Guid(hash.AsSpan(0, 16));
    }
}
