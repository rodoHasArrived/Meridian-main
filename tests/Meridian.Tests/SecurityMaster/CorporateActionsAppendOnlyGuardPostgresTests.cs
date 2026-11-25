using FluentAssertions;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// Review A6 / migration 071: a declared corporate action cannot be rewritten or removed; the only
/// permitted updates are a one-time fingerprint backfill from null and the explicit maintenance
/// normalization of event_type with its fingerprint.
/// </summary>
[Trait("Category", "Integration")]
public sealed class CorporateActionsAppendOnlyGuardPostgresTests : IClassFixture<SecurityMasterDatabaseFixture>
{
    private readonly SecurityMasterDatabaseFixture _fixture;

    public CorporateActionsAppendOnlyGuardPostgresTests(SecurityMasterDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [SecurityMasterDatabaseFact]
    public async Task UpdateDeleteAndTruncate_AreRefused()
    {
        var corpActId = await AppendDividendAsync();

        (await TryExecuteAsync($"update {Table} set dividend_per_share = 9.99 where corp_act_id = @id", corpActId))
            .Should().Be("23514", "an economic column of a declared action cannot change");
        (await TryExecuteAsync($"delete from {Table} where corp_act_id = @id", corpActId))
            .Should().Be("23514");
        // Truncation is refused either by the guard (23514) or, first, by the foreign keys that
        // reference the table (0A000); it never succeeds.
        (await TryExecuteAsync($"truncate {Table}", corpActId))
            .Should().BeOneOf("23514", "0A000");
    }

    [SecurityMasterDatabaseFact]
    public async Task MaintenanceSwitch_PermitsOnlyEventTypeAndFingerprint()
    {
        var corpActId = await AppendDividendAsync();

        (await TryExecuteAsync(
                $"update {Table} set event_type = event_type, economic_fingerprint = encode(sha256('normalized'::bytea), 'hex') where corp_act_id = @id", corpActId, normalization: true))
            .Should().BeNull();
        (await TryExecuteAsync(
                $"update {Table} set economic_fingerprint = encode(sha256('other'::bytea), 'hex'), dividend_per_share = 1.23 where corp_act_id = @id", corpActId, normalization: true))
            .Should().Be("23514", "the switch never opens other columns");
    }

    private string Table => $"{_fixture.Options.Schema}.corporate_actions";

    private async Task<Guid> AppendDividendAsync()
    {
        var store = new PostgresSecurityMasterStore(_fixture.Options);
        var securityId = Guid.NewGuid();
        var effectiveFrom = DateTimeOffset.UtcNow.AddDays(-5);
        await store.UpsertProjectionAsync(new SecurityProjectionRecord(
            securityId, "Equity", SecurityStatusDto.Active, "Guard Co", "USD", "Ticker", $"GD{securityId.ToString("N")[..6]}",
            System.Text.Json.JsonSerializer.SerializeToElement(new { currency = "USD" }),
            System.Text.Json.JsonSerializer.SerializeToElement(new { termsFormat = SecurityTermsFormats.Flat, shareClass = "Common" }),
            System.Text.Json.JsonSerializer.SerializeToElement(new { sourceSystem = "test" }),
            1, effectiveFrom, null,
            [new SecurityIdentifierDto(SecurityIdentifierKind.Ticker, $"GD{securityId.ToString("N")[..6]}", true, effectiveFrom)], []));
        var action = new CorporateActionDto(
            Guid.NewGuid(), securityId, "Dividend", new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 16), 0.25m, "USD",
            null, null, null, null, null, null, null);
        await new PostgresSecurityMasterEventStore(_fixture.Options, NullLogger<PostgresSecurityMasterEventStore>.Instance)
            .AppendCorporateActionAsync(action);
        return action.CorpActId;
    }

    private async Task<string?> TryExecuteAsync(string sql, Guid corpActId, bool normalization = false)
    {
        await using var connection = new NpgsqlConnection(_fixture.Options.ConnectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        try
        {
            if (normalization)
            {
                await using var enable = new NpgsqlCommand("set local meridian.corporate_action_normalization = 'on';", connection, transaction);
                await enable.ExecuteNonQueryAsync();
            }

            await using var command = new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("id", corpActId);
            await command.ExecuteNonQueryAsync();
            return null;
        }
        catch (PostgresException ex)
        {
            return ex.SqlState;
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }
}
