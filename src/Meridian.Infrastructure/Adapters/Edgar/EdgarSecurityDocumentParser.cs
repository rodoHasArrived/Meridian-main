using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Infrastructure.Adapters.Edgar;

internal static class EdgarSecurityDocumentParser
{
    public static IReadOnlyList<EdgarDebtOfferingTerms> ParseDebtOfferings(
        EdgarFilingSummary filing,
        string sourceDocument,
        string? sourceDocumentDescription,
        string? documentText)
    {
        if (string.IsNullOrWhiteSpace(documentText))
            return Array.Empty<EdgarDebtOfferingTerms>();

        var text = NormalizeDocumentText(documentText);
        if (!LooksLikeDebtOffering(text, filing, sourceDocumentDescription))
            return Array.Empty<EdgarDebtOfferingTerms>();

        var securityTitle = ExtractSecurityTitle(text);
        var couponRate = ExtractCouponRate(text, securityTitle);
        var maturityDate = ExtractMaturityDate(text, securityTitle);
        var principalAmount = ExtractMoney(
            text,
            [
                "aggregate principal amount",
                "principal amount",
                "amount offered",
                "total amount"
            ]);
        var issueSize = ExtractMoney(
            text,
            [
                "aggregate offering price",
                "gross proceeds",
                "aggregate principal amount"
            ]);
        var denomination = ExtractDenomination(text);
        var additionalDenomination = ExtractAdditionalDenomination(text);
        var seniority = ExtractSeniority(text);
        var redemptionTerms = ExtractSentences(
            text,
            ["optional redemption", "make-whole", "redeem the notes", "redemption price"],
            maxSentences: 3);
        var firstCallDate = ExtractDateAfter(text, ["on or after", "prior to"]);
        bool? isCallable = redemptionTerms.Count > 0 || firstCallDate.HasValue
            ? true
            : text.Contains("not redeemable", StringComparison.OrdinalIgnoreCase)
                ? false
                : null;

        var notes = new List<string>();
        if (string.IsNullOrWhiteSpace(ExtractCusip(text)))
            notes.Add("CUSIP not found");
        if (couponRate is null)
            notes.Add("Coupon rate not found");
        if (maturityDate is null)
            notes.Add("Maturity date not found");
        if (principalAmount is null)
            notes.Add("Principal amount not found");

        var populatedCoreFields = new[]
        {
            ExtractCusip(text) is not null,
            couponRate.HasValue,
            maturityDate.HasValue,
            principalAmount.HasValue,
            seniority is not null
        }.Count(v => v);

        var confidence = Math.Min(1m, 0.2m + populatedCoreFields * 0.16m + (securityTitle is null ? 0m : 0.1m));

        return
        [
            new EdgarDebtOfferingTerms(
                Cik: filing.Cik,
                AccessionNumber: filing.AccessionNumber,
                Form: filing.Form,
                SourceDocument: sourceDocument,
                SourceDocumentDescription: sourceDocumentDescription,
                IssuerName: null,
                SecurityTitle: securityTitle,
                Cusip: ExtractCusip(text),
                Isin: ExtractIsin(text),
                Currency: ExtractCurrency(text),
                PrincipalAmount: principalAmount,
                IssueSize: issueSize,
                CouponRate: couponRate,
                CouponType: ExtractCouponType(text),
                DayCount: ExtractDayCount(text),
                InterestPaymentFrequency: ExtractPaymentFrequency(text),
                IssueDate: ExtractDateAfter(text, ["issue date", "settlement date", "original issue date"]),
                MaturityDate: maturityDate,
                FirstInterestPaymentDate: ExtractDateAfter(text, ["first interest payment date"]),
                Seniority: seniority,
                Ranking: ExtractRanking(text),
                IsCallable: isCallable,
                FirstCallDate: firstCallDate,
                OfferingPrice: ExtractPercentAfter(text, ["public offering price", "issue price", "price to public"]),
                MinimumDenomination: denomination,
                AdditionalDenomination: additionalDenomination,
                Trustee: ExtractTrustee(text),
                Underwriters: ExtractUnderwriters(text),
                RedemptionTerms: redemptionTerms,
                ExtractionConfidence: decimal.Round(confidence, 2),
                ExtractionNotes: notes)
        ];
    }

    public static IReadOnlyList<EdgarFundHolding> ParseFundHoldings(
        EdgarFilingSummary filing,
        string sourceDocument,
        string? documentText)
    {
        if (string.IsNullOrWhiteSpace(documentText))
            return Array.Empty<EdgarFundHolding>();

        try
        {
            var doc = XDocument.Parse(documentText, LoadOptions.None);
            var nportHoldings = doc
                .Descendants()
                .Where(e => LocalNameEquals(e, "invstOrSec"))
                .Select(e => ToFundHolding(filing, sourceDocument, e, is13F: false))
                .Where(h => h.HoldingName is not null || h.Cusip is not null || h.Isin is not null)
                .ToArray();

            if (nportHoldings.Length > 0)
                return nportHoldings;

            return doc
                .Descendants()
                .Where(e => LocalNameEquals(e, "infoTable"))
                .Select(e => ToFundHolding(filing, sourceDocument, e, is13F: true))
                .Where(h => h.HoldingName is not null || h.Cusip is not null)
                .ToArray();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Xml.XmlException)
        {
            return Array.Empty<EdgarFundHolding>();
        }
    }

    private static EdgarFundHolding ToFundHolding(
        EdgarFilingSummary filing,
        string sourceDocument,
        XElement node,
        bool is13F)
        => new(
            Cik: filing.Cik,
            AccessionNumber: filing.AccessionNumber,
            Form: filing.Form,
            SourceDocument: sourceDocument,
            HoldingName: is13F ? Text(node, "nameOfIssuer") : Text(node, "name"),
            IssuerName: Text(node, "issuerName"),
            Title: is13F ? Text(node, "titleOfClass") : Text(node, "title"),
            Cusip: NormalizeIdentifier(Text(node, "cusip")),
            Isin: NormalizeIdentifier(Text(node, "isin")),
            Ticker: Text(node, "ticker"),
            Lei: Text(node, "lei"),
            AssetCategory: Text(node, "assetCat"),
            IssuerCategory: Text(node, "issuerCat"),
            Country: Text(node, "invCountry"),
            Currency: Text(node, "curCd"),
            Balance: Decimal(Text(node, is13F ? "sshPrnamt" : "balance")),
            Units: Text(node, is13F ? "sshPrnamtType" : "units"),
            ValueUsd: Decimal(Text(node, is13F ? "value" : "valUSD")),
            PercentageOfNetAssets: Decimal(Text(node, "pctVal")),
            CouponRate: Decimal(Text(node, "annualizedRt")),
            MaturityDate: Date(Text(node, "maturityDt")),
            Restricted: Bool(Text(node, "isRestrictedSec")),
            FairValueLevel: Text(node, "fairValLevel"));

    private static string NormalizeDocumentText(string documentText)
    {
        var withoutScripts = Regex.Replace(
            documentText,
            "<(script|style)[^>]*>.*?</\\1>",
            " ",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        var withoutTags = Regex.Replace(withoutScripts, "<[^>]+>", " ");
        var decoded = WebUtility.HtmlDecode(withoutTags);
        return Regex.Replace(decoded, @"\s+", " ").Trim();
    }

    private static bool LooksLikeDebtOffering(
        string text,
        EdgarFilingSummary filing,
        string? description)
    {
        if (filing.Form.StartsWith("424B", StringComparison.OrdinalIgnoreCase) ||
            filing.Form.Equals("FWP", StringComparison.OrdinalIgnoreCase) ||
            filing.Form.StartsWith("S-3", StringComparison.OrdinalIgnoreCase) ||
            filing.Form.StartsWith("F-3", StringComparison.OrdinalIgnoreCase) ||
            filing.Form.StartsWith("S-1", StringComparison.OrdinalIgnoreCase))
            return ContainsDebtLanguage(text);

        return ContainsDebtLanguage(description) || ContainsDebtLanguage(text);
    }

    private static bool ContainsDebtLanguage(string? value)
        => !string.IsNullOrWhiteSpace(value) &&
           (value.Contains("note", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("debenture", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("bond", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("indenture", StringComparison.OrdinalIgnoreCase));

    private static string? ExtractSecurityTitle(string text)
    {
        var match = Regex.Match(
            text,
            @"(?<title>(?:\d+(?:\.\d+)?%\s+)?(?:senior\s+|subordinated\s+|secured\s+|unsecured\s+|fixed\s+rate\s+|floating\s+rate\s+)?(?:notes|debentures|bonds)\s+due\s+(?:[A-Za-z]+\s+\d{1,2},\s+)?\d{4})",
            RegexOptions.IgnoreCase);
        return match.Success ? NormalizeTitle(match.Groups["title"].Value) : null;
    }

    private static string? ExtractCusip(string text)
    {
        var match = Regex.Match(
            text,
            @"CUSIP(?:\s+(?:Number|No\.?))?\s*[:\-]?\s*(?<cusip>[0-9A-Z]{3}\s?[0-9A-Z]{3}\s?[0-9A-Z]{2}\s?[0-9A-Z])",
            RegexOptions.IgnoreCase);
        return match.Success ? NormalizeIdentifier(match.Groups["cusip"].Value) : null;
    }

    private static string? ExtractIsin(string text)
    {
        var match = Regex.Match(text, @"\b(?<isin>[A-Z]{2}[A-Z0-9]{10})\b", RegexOptions.IgnoreCase);
        return match.Success ? NormalizeIdentifier(match.Groups["isin"].Value) : null;
    }

    private static decimal? ExtractCouponRate(string text, string? title)
    {
        var source = title ?? text;
        var match = Regex.Match(source, @"(?<rate>\d+(?:\.\d+)?)\s*%", RegexOptions.IgnoreCase);
        return match.Success ? Decimal(match.Groups["rate"].Value) : null;
    }

    private static string? ExtractCouponType(string text)
    {
        if (text.Contains("floating rate", StringComparison.OrdinalIgnoreCase))
            return "Floating";
        if (text.Contains("zero coupon", StringComparison.OrdinalIgnoreCase))
            return "ZeroCoupon";
        return ExtractCouponRate(text, null).HasValue ? "Fixed" : null;
    }

    private static DateOnly? ExtractMaturityDate(string text, string? title)
    {
        var titleDate = ExtractDateAfter(title ?? string.Empty, ["due"]);
        return titleDate ?? ExtractDateAfter(text, ["maturity date", "stated maturity", "due"]);
    }

    private static decimal? ExtractMoney(string text, IReadOnlyList<string> labels)
    {
        foreach (var label in labels)
        {
            var labelThenMoney = Regex.Escape(label) + @"[^$A-Z0-9]{0,80}(?<money>(?:U\.S\.\s*)?\$?\s?[\d,]+(?:\.\d+)?\s?(?:million|billion)?)";
            var moneyThenLabel = @"(?<money>(?:U\.S\.\s*)?\$?\s?[\d,]+(?:\.\d+)?\s?(?:million|billion)?)\s+" + Regex.Escape(label);
            var match = Regex.Match(text, labelThenMoney, RegexOptions.IgnoreCase);
            if (!match.Success)
                match = Regex.Match(text, moneyThenLabel, RegexOptions.IgnoreCase);
            if (match.Success)
                return ParseMoney(match.Groups["money"].Value);
        }

        return null;
    }

    private static decimal? ExtractDenomination(string text)
    {
        var match = Regex.Match(
            text,
            @"minimum\s+denominations?\s+of\s+(?<money>(?:U\.S\.\s*)?\$?\s?[\d,]+(?:\.\d+)?)",
            RegexOptions.IgnoreCase);
        return match.Success ? ParseMoney(match.Groups["money"].Value) : null;
    }

    private static decimal? ExtractAdditionalDenomination(string text)
    {
        var match = Regex.Match(
            text,
            @"integral\s+multiples?\s+of\s+(?<money>(?:U\.S\.\s*)?\$?\s?[\d,]+(?:\.\d+)?)",
            RegexOptions.IgnoreCase);
        return match.Success ? ParseMoney(match.Groups["money"].Value) : null;
    }

    private static DateOnly? ExtractDateAfter(string text, IReadOnlyList<string> labels)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        foreach (var label in labels)
        {
            var pattern = Regex.Escape(label) + @"[^A-Za-z0-9]{0,60}(?<date>[A-Za-z]+\s+\d{1,2},\s+\d{4}|\d{4}-\d{2}-\d{2})";
            var match = Regex.Match(text, pattern, RegexOptions.IgnoreCase);
            if (match.Success && DateOnly.TryParse(match.Groups["date"].Value, CultureInfo.InvariantCulture, out var date))
                return date;
        }

        return null;
    }

    private static decimal? ExtractPercentAfter(string text, IReadOnlyList<string> labels)
    {
        foreach (var label in labels)
        {
            var pattern = Regex.Escape(label) + @"[^0-9]{0,80}(?<value>\d+(?:\.\d+)?)\s*%";
            var match = Regex.Match(text, pattern, RegexOptions.IgnoreCase);
            if (match.Success)
                return Decimal(match.Groups["value"].Value);
        }

        return null;
    }

    private static string? ExtractSeniority(string text)
    {
        if (text.Contains("senior secured", StringComparison.OrdinalIgnoreCase))
            return "Senior Secured";
        if (text.Contains("senior unsecured", StringComparison.OrdinalIgnoreCase))
            return "Senior Unsecured";
        if (text.Contains("senior notes", StringComparison.OrdinalIgnoreCase))
            return "Senior";
        if (text.Contains("subordinated notes", StringComparison.OrdinalIgnoreCase))
            return "Subordinated";
        if (text.Contains("unsecured", StringComparison.OrdinalIgnoreCase))
            return "Unsecured";
        if (text.Contains("secured", StringComparison.OrdinalIgnoreCase))
            return "Secured";
        return null;
    }

    private static string? ExtractRanking(string text)
    {
        var match = Regex.Match(
            text,
            @"rank\s+(?<ranking>equally|senior|junior|pari passu)[^.]{0,120}",
            RegexOptions.IgnoreCase);
        return match.Success ? match.Value.Trim() : null;
    }

    private static string? ExtractDayCount(string text)
    {
        var match = Regex.Match(text, @"\b(30/360|actual/360|actual/365|Actual/Actual)\b", RegexOptions.IgnoreCase);
        return match.Success ? match.Value : null;
    }

    private static string? ExtractPaymentFrequency(string text)
    {
        if (text.Contains("semi-annually", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("semiannually", StringComparison.OrdinalIgnoreCase))
            return "SemiAnnual";
        if (text.Contains("quarterly", StringComparison.OrdinalIgnoreCase))
            return "Quarterly";
        if (text.Contains("monthly", StringComparison.OrdinalIgnoreCase))
            return "Monthly";
        if (text.Contains("annually", StringComparison.OrdinalIgnoreCase))
            return "Annual";
        return null;
    }

    private static string? ExtractCurrency(string text)
    {
        if (text.Contains("U.S.$", StringComparison.OrdinalIgnoreCase) || text.Contains("$", StringComparison.Ordinal))
            return "USD";
        if (text.Contains("EUR", StringComparison.OrdinalIgnoreCase) || text.Contains("euro", StringComparison.OrdinalIgnoreCase))
            return "EUR";
        if (text.Contains("GBP", StringComparison.OrdinalIgnoreCase) || text.Contains("sterling", StringComparison.OrdinalIgnoreCase))
            return "GBP";
        return null;
    }

    private static string? ExtractTrustee(string text)
    {
        var match = Regex.Match(
            text,
            @"(?<trustee>[A-Z][A-Za-z0-9 ,.&'-]{3,80})\s*,?\s+as\s+trustee",
            RegexOptions.IgnoreCase);
        return match.Success ? NormalizeTitle(match.Groups["trustee"].Value) : null;
    }

    private static IReadOnlyList<string> ExtractUnderwriters(string text)
    {
        var match = Regex.Match(
            text,
            @"(?:underwriters|joint book-running managers|book-running managers)\s*:?\s+(?<value>[A-Z][A-Za-z0-9 ,.&'-]{5,220})",
            RegexOptions.IgnoreCase);
        if (!match.Success)
            return Array.Empty<string>();

        return match.Groups["value"].Value
            .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(v => v.Length > 1)
            .Take(12)
            .ToArray();
    }

    private static IReadOnlyList<string> ExtractSentences(
        string text,
        IReadOnlyList<string> phrases,
        int maxSentences)
    {
        var sentences = Regex.Split(text, @"(?<=[.!?])\s+");
        return sentences
            .Where(sentence => phrases.Any(phrase => sentence.Contains(phrase, StringComparison.OrdinalIgnoreCase)))
            .Select(sentence => sentence.Trim())
            .Where(sentence => sentence.Length > 0)
            .Take(maxSentences)
            .ToArray();
    }

    private static decimal? ParseMoney(string value)
    {
        value = value.Replace("U.S.", string.Empty, StringComparison.OrdinalIgnoreCase);
        var multiplier = value.Contains("billion", StringComparison.OrdinalIgnoreCase)
            ? 1_000_000_000m
            : value.Contains("million", StringComparison.OrdinalIgnoreCase)
                ? 1_000_000m
                : 1m;
        var cleaned = Regex.Replace(value, @"[^\d.]", string.Empty);
        return decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? parsed * multiplier
            : null;
    }

    private static decimal? Decimal(string? value)
        => decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;

    private static DateOnly? Date(string? value)
        => DateOnly.TryParse(value, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;

    private static bool? Bool(string? value)
        => bool.TryParse(value, out var parsed) ? parsed : null;

    private static string? Text(XElement node, string localName)
        => node
            .Descendants()
            .FirstOrDefault(e => LocalNameEquals(e, localName))?
            .Value
            .Trim();

    private static bool LocalNameEquals(XElement element, string localName)
        => string.Equals(element.Name.LocalName, localName, StringComparison.OrdinalIgnoreCase);

    private static string? NormalizeIdentifier(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var normalized = Regex.Replace(value, @"[^A-Z0-9]", string.Empty, RegexOptions.IgnoreCase).ToUpperInvariant();
        return normalized.Length == 0 ? null : normalized;
    }

    private static string NormalizeTitle(string value)
        => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(value.Trim().ToLowerInvariant());
}
