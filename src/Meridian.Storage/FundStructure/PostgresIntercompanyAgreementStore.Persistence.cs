using System.Text.Json;
using Meridian.Contracts.FundStructure;
using Npgsql;
using NpgsqlTypes;

namespace Meridian.Storage.FundStructure;

public sealed partial class PostgresIntercompanyAgreementStore
{
    private static void ValidateAppend(IntercompanyAgreementStoreState? previous,
        IntercompanyAgreementStoreState next, Guid agreementId)
    {
        if (next.AgreementId != agreementId || next.AggregateVersion != (previous?.AggregateVersion ?? 0) + 1)
            throw new IntercompanyAgreementConflictException("An agreement command must advance the same aggregate exactly one version.");

        var priorRevisions = previous?.Revisions ?? [];
        var priorHistory = previous?.History ?? [];
        var priorReceipts = previous?.Receipts ?? [];
        RequireImmutablePrefix(priorRevisions, next.Revisions, "approved revisions");
        RequireImmutablePrefix(priorHistory, next.History, "audit history");
        RequireImmutablePrefix(priorReceipts, next.Receipts, "command receipts");
        if (next.Revisions.Count > priorRevisions.Count + 1
            || next.History.Count != priorHistory.Count + 1
            || next.Receipts.Count != priorReceipts.Count + 1)
            throw new IntercompanyAgreementValidationException("One command must append one audit event and receipt, and at most one approval.");
        if (next.Proposals.Select(x => x.ProposalId).Distinct().Count() != next.Proposals.Count
            || next.History.Select(x => x.EventId).Distinct().Count() != next.History.Count
            || next.Receipts.Select(x => x.CommandId).Distinct().Count() != next.Receipts.Count)
            throw new IntercompanyAgreementConflictException("Agreement proposal, event and command identities must be unique.");

        for (var index = 0; index < next.Revisions.Count; index++)
        {
            var revision = next.Revisions[index];
            if (revision.AgreementId != agreementId || revision.AgreementVersion != index + 1)
                throw new IntercompanyAgreementValidationException("Approved agreement versions must be contiguous and retain their agreement identity.");
        }

        var audit = next.History[^1];
        var receipt = next.Receipts[^1];
        if (audit.EventId == Guid.Empty || audit.AggregateVersion != next.AggregateVersion
            || receipt.CommandId == Guid.Empty || receipt.AggregateVersion != next.AggregateVersion
            || receipt.Detail.AgreementId != agreementId || receipt.Detail.AggregateVersion != next.AggregateVersion)
            throw new IntercompanyAgreementValidationException("The command receipt and audit event must bind the committed aggregate version.");
    }

    private static void RequireImmutablePrefix<T>(IReadOnlyList<T> previous, IReadOnlyList<T> next, string label)
    {
        if (next.Count < previous.Count)
            throw new IntercompanyAgreementConflictException($"Retained agreement {label} cannot be removed.");
        for (var index = 0; index < previous.Count; index++)
        {
            if (!string.Equals(JsonSerializer.Serialize(previous[index], JsonOptions),
                    JsonSerializer.Serialize(next[index], JsonOptions), StringComparison.Ordinal))
                throw new IntercompanyAgreementConflictException($"Retained agreement {label} cannot be rewritten.");
        }
    }

    private async Task PersistAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string tenant,
        IntercompanyAgreementStoreState? previous, IntercompanyAgreementStoreState next, CancellationToken ct)
    {
        if (previous is null)
        {
            await using var create = connection.CreateCommand();
            create.Transaction = transaction;
            create.CommandText = $"""
                insert into {Q("intercompany_agreement")} (tenant_id, agreement_id, aggregate_version)
                values (@tenant, @id, @version);
                """;
            AddScope(create, tenant, next.AgreementId);
            create.Parameters.AddWithValue("version", next.AggregateVersion);
            await create.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await InsertPayloadAsync(connection, transaction, "intercompany_agreement_snapshot", "aggregate_version",
            next.AggregateVersion, tenant, next.AgreementId, next, ct).ConfigureAwait(false);
        for (var index = previous?.Revisions.Count ?? 0; index < next.Revisions.Count; index++)
        {
            var revision = next.Revisions[index];
            await InsertPayloadAsync(connection, transaction, "intercompany_agreement_revision", "agreement_version",
                revision.AgreementVersion, tenant, next.AgreementId, revision, ct).ConfigureAwait(false);
        }
        await InsertPayloadAsync(connection, transaction, "intercompany_agreement_audit", "sequence_number",
            next.History.Count, tenant, next.AgreementId, next.History[^1], ct).ConfigureAwait(false);
        await InsertPayloadAsync(connection, transaction, "intercompany_agreement_command_receipt", "command_id",
            next.Receipts[^1].CommandId, tenant, next.AgreementId, next.Receipts[^1], ct).ConfigureAwait(false);

        if (previous is not null)
        {
            await using var advance = connection.CreateCommand();
            advance.Transaction = transaction;
            advance.CommandText = $"""
                update {Q("intercompany_agreement")} set aggregate_version = @version
                where tenant_id = @tenant and agreement_id = @id and aggregate_version = @expected;
                """;
            AddScope(advance, tenant, next.AgreementId);
            advance.Parameters.AddWithValue("version", next.AggregateVersion);
            advance.Parameters.AddWithValue("expected", previous.AggregateVersion);
            if (await advance.ExecuteNonQueryAsync(ct).ConfigureAwait(false) != 1)
                throw new IntercompanyAgreementConflictException("Agreement authority changed before commit.");
        }
    }

    private async Task InsertPayloadAsync<T>(NpgsqlConnection connection, NpgsqlTransaction transaction,
        string table, string sequenceColumn, object sequence, string tenant, Guid agreementId, T payload,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            insert into {Q(table)} (tenant_id, agreement_id, {sequenceColumn}, payload)
            values (@tenant, @id, @sequence, @payload);
            """;
        AddScope(command, tenant, agreementId);
        command.Parameters.AddWithValue("sequence", sequence);
        command.Parameters.AddWithValue("payload", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(payload, JsonOptions));
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static void AddScope(NpgsqlCommand command, string tenant, Guid agreementId)
    {
        command.Parameters.AddWithValue("tenant", tenant);
        command.Parameters.AddWithValue("id", agreementId);
    }
}
