using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Meridian.Contracts.FixedIncome;
using Meridian.Storage.SecurityMaster;

namespace Meridian.Instruments.FixedIncome;

public sealed partial class BondProjectionService
{
    public async Task<BondReferencePageDto> GetIssuerLadderPageAsync(string issuerName, int pageSize = 100,
        string? cursor = null, DateOnly? asOf = null, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(issuerName);
        issuerName = issuerName.Trim();
        if (issuerName.Length > 200)
            throw new ArgumentException("Issuer name exceeds 200 characters.", nameof(issuerName));
        var date = asOf ?? Today;
        var queryKey = FormattableString.Invariant($"issuer:{issuerName}:{date:yyyy-MM-dd}");
        var after = ReadCursor(cursor, queryKey, pageSize);
        var rows = await _projectionStore.GetIssuerLadderPageAsync(issuerName, pageSize,
            after.Maturity, after.SecurityId, ct).ConfigureAwait(false);
        return Page(rows, pageSize, date, queryKey, ct);
    }

    public async Task<BondReferencePageDto> GetMaturityLadderPageAsync(DateOnly from, DateOnly to, int pageSize = 100,
        string? cursor = null, DateOnly? asOf = null, CancellationToken ct = default)
    {
        if (to < from || to.DayNumber - from.DayNumber > 18_366)
            throw new ArgumentException("Maturity range must be ordered and cannot exceed 50 years.", nameof(to));
        var date = asOf ?? Today;
        var queryKey = FormattableString.Invariant($"maturity:{from:yyyy-MM-dd}:{to:yyyy-MM-dd}:{date:yyyy-MM-dd}");
        var after = ReadCursor(cursor, queryKey, pageSize);
        var rows = await _projectionStore.GetMaturityLadderPageAsync(from, to, pageSize,
            after.Maturity, after.SecurityId, ct).ConfigureAwait(false);
        return Page(rows, pageSize, date, queryKey, ct);
    }

    private BondReferencePageDto Page(IReadOnlyList<BondProjectionRow> rows, int pageSize, DateOnly asOf,
        string queryKey, CancellationToken ct)
    {
        var page = rows.Take(pageSize).ToArray();
        var next = rows.Count > pageSize && page.Length > 0 ? WriteCursor(page[^1], queryKey) : null;
        return new BondReferencePageDto(MapReferenceList(page, asOf, ct), next);
    }

    private static (DateOnly? Maturity, Guid? SecurityId) ReadCursor(string? cursor, string queryKey, int pageSize)
    {
        if (pageSize is < 1 or > 500)
            throw new ArgumentOutOfRangeException(nameof(pageSize), "Page size must be between 1 and 500.");
        if (cursor is null)
            return (null, null);
        if (cursor.Length is 0 or > 256)
            throw new ArgumentException("Invalid bond ladder cursor.", nameof(cursor));
        try
        {
            var base64 = cursor.Replace('-', '+').Replace('_', '/');
            base64 = base64.PadRight((base64.Length + 3) / 4 * 4, '=');
            var parts = Encoding.UTF8.GetString(Convert.FromBase64String(base64)).Split(':');
            if (parts.Length != 4 || parts[0] != "1" || parts[1] != QueryHash(queryKey)
                || !Guid.TryParseExact(parts[3], "N", out var securityId))
                throw new FormatException();
            DateOnly? maturity = parts[2] == "null" ? null
                : DateOnly.ParseExact(parts[2], "yyyy-MM-dd", CultureInfo.InvariantCulture);
            return (maturity, securityId);
        }
        catch (FormatException exception)
        {
            throw new ArgumentException("Invalid bond ladder cursor or cursor belongs to a different query.", nameof(cursor), exception);
        }
    }

    private static string WriteCursor(BondProjectionRow row, string queryKey)
    {
        var maturity = row.MaturityDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "null";
        var value = $"1:{QueryHash(queryKey)}:{maturity}:{row.SecurityId:N}";
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(value)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static string QueryHash(string queryKey)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(queryKey)));
}
