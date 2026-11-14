using Meridian.Contracts.SecurityMaster;
using Npgsql;

namespace Meridian.Storage.SecurityMaster;

public sealed partial class PostgresCorporateActionOperationsStore
{
    private async Task EnsureCashDividendCanonicalInstrumentAsync(NpgsqlConnection connection,
        NpgsqlTransaction transaction, Guid boundSecurityId, string? symbol, Guid securityId,
        long expectedVersion, DateTimeOffset asOf, CancellationToken ct)
    {
        if (securityId != boundSecurityId || expectedVersion <= 0 || string.IsNullOrWhiteSpace(symbol))
            throw new CorporateActionValidationException("The bounded dividend requires its exact native instrument identity and ticker.");
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"select version from {Qualified("securities")} where security_id=@security and asset_class='Equity' and currency='USD' and status='Active'";
        command.Parameters.AddWithValue("security", securityId);
        if (await command.ExecuteScalarAsync(ct).ConfigureAwait(false) is not long version || version != expectedVersion)
            throw new CorporateActionValidationException("The bounded dividend's current active USD equity version changed.");
        command.CommandText = $"select identifier_value,is_primary,valid_from,valid_to from {Qualified("security_identifiers")} where security_id=@security and identifier_kind=@kind order by identifier_value,valid_from";
        command.Parameters.AddWithValue("kind", SecurityIdentifierKind.Ticker.ToString());
        var identifiers = new List<SecurityIdentifierDto>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            identifiers.Add(new(SecurityIdentifierKind.Ticker, reader.GetString(0), reader.GetBoolean(1),
                ReadTimestamp(reader, 2), reader.IsDBNull(3) ? null : ReadTimestamp(reader, 3)));
        var canonical = SecurityMasterLedgerSymbol.ResolveUniqueActiveTicker(identifiers, asOf);
        if (canonical is null || !string.Equals(canonical, symbol, StringComparison.Ordinal))
            throw new CorporateActionValidationException("The retained candidate ticker is missing, ambiguous or different from current native Security Master authority.");
    }
}
