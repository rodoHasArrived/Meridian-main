using System.Text.Json;
using Npgsql;

namespace Meridian.Storage.DirectLending;

public sealed partial class PostgresDirectLendingStateStore
{
    private async Task EnqueueAssetPublicationAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        Guid loanId, string kind, Guid runId, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            insert into {Qualified("outbox_message")}
                (outbox_message_id, topic, message_key, payload, headers, occurred_at, visible_after, error_count)
            values (@id, 'direct-lending.asset-operations.requested', @key, cast(@payload as jsonb),
                    null, now(), now(), 0)
            on conflict (topic, message_key) do nothing;
            """;
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("key", $"{loanId:N}/{kind}/{runId:N}");
        command.Parameters.AddWithValue("payload", JsonSerializer.Serialize(new
        {
            loanId,
            sourceEventId = runId,
            commandId = runId,
            sourceSystem = "meridian.direct-lending"
        }));
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }
}
