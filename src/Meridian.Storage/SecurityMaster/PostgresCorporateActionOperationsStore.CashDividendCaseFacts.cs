using System.Data;
using System.Text.Json;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Storage.SecurityMaster;

public sealed partial class PostgresCorporateActionOperationsStore
{
    public Task<CorporateActionCashDividendCaseFactsDto> GetCashDividendCaseFactsAsync(
        Guid caseId, string tenantId, string companyId, long expectedCaseVersion, CancellationToken ct = default) =>
        ExecutePersistenceReadAsync(async () =>
        {
            RequireCashDividendScope(tenantId, companyId);
            if (caseId == Guid.Empty || expectedCaseVersion <= 0)
                throw new CorporateActionValidationException("An exact case identity and version are required for eligibility facts.");
            await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
            await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct).ConfigureAwait(false);
            var processingCase = await LoadCaseAsync(connection, transaction, caseId, tenantId, companyId, false, ct)
                .ConfigureAwait(false) ?? throw new CorporateActionNotFoundException("Cash-dividend case", caseId);
            EnsureVersion(caseId, expectedCaseVersion, processingCase.Version);
            var options = new List<CorporateActionProcessingOptionDto>();
            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = $"""
                    select option_id, case_id, option_code, label, description, state, source_methodology,
                           blockers::text, parameters::text, case_version, recorded_by, recorded_at
                    from {Qualified("corporate_action_processing_options")}
                    where case_id = @case_id order by option_id;
                    """;
                command.Parameters.AddWithValue("case_id", caseId);
                await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                    options.Add(new(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3),
                        reader.GetString(4), reader.GetString(5), reader.IsDBNull(6) ? null : reader.GetString(6),
                        JsonSerializer.Deserialize<IReadOnlyList<string>>(reader.GetString(7), JsonOptions) ?? [],
                        reader.IsDBNull(8) ? null : JsonSerializer.Deserialize<JsonElement>(reader.GetString(8)),
                        reader.GetInt64(9), reader.GetString(10), ReadTimestamp(reader, 11)));
            }
            var evidence = new List<CorporateActionEvidenceDto>();
            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = $"""
                    select evidence_id, case_id, evidence_kind, evidence_reference, evidence_hash,
                           description, metadata::text, case_version, recorded_by, recorded_at
                    from {Qualified("corporate_action_case_evidence")}
                    where case_id = @case_id order by evidence_id;
                    """;
                command.Parameters.AddWithValue("case_id", caseId);
                await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                    evidence.Add(new(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3),
                        reader.IsDBNull(4) ? null : reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetString(5),
                        reader.IsDBNull(6) ? null : JsonSerializer.Deserialize<JsonElement>(reader.GetString(6)),
                        reader.GetInt64(7), reader.GetString(8), ReadTimestamp(reader, 9)));
            }
            // No case State/Version/UpdatedAt: preparer-stage append does not change these facts.
            var json = JsonSerializer.Serialize(new
            {
                SchemaVersion = 1,
                Method = "complete-scoped-cash-dividend-case-facts-v1",
                CaseId = caseId,
                Scope = processingCase.Scope,
                Complete = true,
                Options = options,
                Evidence = evidence
            }, JsonOptions);
            await transaction.CommitAsync(ct).ConfigureAwait(false);
            return new CorporateActionCashDividendCaseFactsDto(caseId, processingCase.Version, options, evidence,
                json, Sha256Digest.ComputeUtf8(json));
        }, "cash-dividend processing facts");
}
