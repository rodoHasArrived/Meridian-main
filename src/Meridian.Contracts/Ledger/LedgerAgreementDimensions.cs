using System.Globalization;

namespace Meridian.Contracts.Ledger;

/// <summary>Canonical optional agreement provenance carried by ledger line dimensions.</summary>
public static class LedgerAgreementDimensions
{
    /// <summary>Preserves legacy signatures exactly when agreement provenance is absent.</summary>
    public static string AppendSignature(string signature, Guid? agreementId, int? agreementVersion, string separator = "|")
    {
        if (agreementId.HasValue)
            signature += separator + "intercompanyAgreementId=" + agreementId.Value.ToString("D");
        if (agreementVersion.HasValue)
            signature += separator + "intercompanyAgreementVersion=" + agreementVersion.Value.ToString(CultureInfo.InvariantCulture);
        return signature;
    }

    public static Guid? ReadId(IReadOnlyDictionary<string, string>? tags, string prefix = "")
    {
        var value = ReadTag(tags, prefix + "intercompanyAgreementId");
        if (value is null)
            return null;
        return Guid.TryParse(value, out var id) && id != Guid.Empty
            ? id : throw new ArgumentException("The intercompany agreement dimension must be a nonempty GUID.");
    }

    public static int? ReadVersion(IReadOnlyDictionary<string, string>? tags, string prefix = "")
    {
        var value = ReadTag(tags, prefix + "intercompanyAgreementVersion");
        if (value is null)
            return null;
        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var version) && version > 0
            ? version : throw new ArgumentException("The intercompany agreement version must be a positive integer.");
    }

    private static string? ReadTag(IReadOnlyDictionary<string, string>? tags, string key)
    {
        if (tags is null)
            return null;
        foreach (var pair in tags)
        {
            if (!string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
                continue;
            if (string.IsNullOrWhiteSpace(pair.Value))
                throw new ArgumentException("A supplied intercompany agreement dimension cannot be blank.");
            return pair.Value.Trim();
        }
        return null;
    }
}
