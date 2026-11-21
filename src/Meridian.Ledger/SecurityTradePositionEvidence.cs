using System.Globalization;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Ledger;

/// <summary>Native-unit changes and exact monetary basis changes from the existing trade projector.</summary>
public sealed record SecurityTradePositionEffect(decimal LongQuantityChange, decimal ShortQuantityChange,
    decimal LongBasisChange, decimal ShortBasisChange, SecurityLotUnitKind QuantityUnit, decimal Multiplier);

/// <summary>Retains position facts alongside trade journals; fees and financing do not invent units.</summary>
public static class SecurityTradePositionEvidence
{
    public const string VersionTag = "positionEvidence.version";
    public const string ReversalSignTag = "positionEvidence.reversalSign";
    private const string Prefix = "positionEvidence.";

    public static JournalEntryMetadata WithPostings(JournalEntryMetadata metadata,
        IReadOnlyList<SecurityTradePosting> postings, SecurityLotUnitKind unit, decimal multiplier)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(postings);
        if (postings.Count == 0 || !Enum.IsDefined(unit) || multiplier <= 0m)
            throw new LedgerValidationException("Position evidence requires trade legs and explicit positive native-unit conventions.");
        var longQuantity = 0m;
        var shortQuantity = 0m;
        var longBasis = 0m;
        var shortBasis = 0m;
        foreach (var posting in postings)
        {
            if (!Enum.IsDefined(posting.Kind) || posting.Quantity <= 0m || posting.GrossValue < 0m
                || posting.CarryingValue is < 0m || posting.BorrowedAmount < 0m || posting.LoanRepaid < 0m)
                throw new LedgerValidationException("Trade position evidence contains invalid retained leg economics.");
            switch (posting.Kind)
            {
                case SecurityTradePostingKind.OpenLong:
                    longQuantity += posting.Quantity;
                    longBasis += posting.GrossValue;
                    break;
                case SecurityTradePostingKind.CloseLong:
                    longQuantity -= posting.Quantity;
                    longBasis -= posting.CarryingValue ?? throw new LedgerValidationException("A close requires retained monetary basis.");
                    break;
                case SecurityTradePostingKind.OpenShort:
                    shortQuantity += posting.Quantity;
                    shortBasis += posting.GrossValue;
                    break;
                case SecurityTradePostingKind.CoverShort:
                    shortQuantity -= posting.Quantity;
                    shortBasis -= posting.CarryingValue ?? throw new LedgerValidationException("A cover requires retained original proceeds.");
                    break;
            }
        }
        var tags = new Dictionary<string, string>(metadata.Tags ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase)
        {
            [VersionTag] = "1", [ReversalSignTag] = "1", [Prefix + "unit"] = unit.ToString(),
            [Prefix + "multiplier"] = Number(multiplier), [Prefix + "longQuantity"] = Number(longQuantity),
            [Prefix + "shortQuantity"] = Number(shortQuantity), [Prefix + "longBasis"] = Number(longBasis),
            [Prefix + "shortBasis"] = Number(shortBasis)
        };
        return metadata with { Tags = tags };
    }

    public static bool TryRead(JournalEntry journal, out SecurityTradePositionEffect? effect, out string? failure,
        IChartOfAccounts? chart = null)
    {
        effect = null;
        failure = null;
        var tags = journal.Metadata.Tags;
        if (tags is null || !tags.TryGetValue(VersionTag, out var version))
        {
            failure = "No versioned native position evidence is retained on the journal.";
            return false;
        }
        try
        {
            if (version != "1" || !tags.TryGetValue(ReversalSignTag, out var signText) || signText is not ("1" or "-1")
                || !tags.TryGetValue(Prefix + "unit", out var unitText)
                || !Enum.TryParse<SecurityLotUnitKind>(unitText, out var unit) || !Enum.IsDefined(unit))
                throw new LedgerValidationException("Position evidence version, native unit or reversal sign is unsupported.");
            var multiplier = Read("multiplier");
            if (multiplier <= 0m)
                throw new LedgerValidationException("The retained native multiplier is not positive.");
            var sign = signText == "1" ? 1m : -1m;
            var candidate = new SecurityTradePositionEffect(sign * Read("longQuantity"), sign * Read("shortQuantity"),
                sign * Read("longBasis"), sign * Read("shortBasis"), unit, multiplier);
            chart ??= StandardChartOfAccounts.Instance;
            var longBalance = 0m;
            var shortBalance = 0m;
            var hasPositionAccount = false;
            foreach (var line in journal.Lines)
            {
                if (!chart.TryGetRegistered(line.Account, out var role)
                    || role is not (LedgerAccountRole.Securities or LedgerAccountRole.ShortSecuritiesPayable))
                    continue;
                hasPositionAccount = true;
                if (!string.Equals(line.Account.Symbol, journal.Metadata.Symbol, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(line.Account.FinancialAccountId, journal.Metadata.FinancialAccountId, StringComparison.OrdinalIgnoreCase))
                    throw new LedgerValidationException("Position evidence does not identify its posted instrument/account.");
                if (role == LedgerAccountRole.Securities)
                    longBalance += line.Debit - line.Credit;
                else
                    shortBalance += line.Credit - line.Debit;
            }
            if (!hasPositionAccount || longBalance != candidate.LongBasisChange || shortBalance != candidate.ShortBasisChange)
                throw new LedgerValidationException("Position evidence disagrees with posted monetary carrying movements.");
            effect = candidate;
            return true;
        }
        catch (Exception exception) when (exception is LedgerValidationException or OverflowException)
        {
            failure = exception.Message;
            return false;
        }

        decimal Read(string name) => tags.TryGetValue(Prefix + name, out var text)
            && decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                ? value : throw new LedgerValidationException($"Position evidence '{name}' is missing or invalid.");
    }

    private static string Number(decimal value) => value.ToString("G29", CultureInfo.InvariantCulture);
}
