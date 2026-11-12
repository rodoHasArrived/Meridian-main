using System.Data;
using System.Text.Json;
using Meridian.Contracts.SecurityMaster;
using Npgsql;

namespace Meridian.Storage.SecurityMaster;

public sealed record CorporateActionCalendarReadSet(
    IReadOnlyList<CorporateActionSourceProposalDto> Proposals,
    IReadOnlyList<CorporateActionProcessingCaseDto> Cases,
    IReadOnlyList<CorporateActionEvidenceDto> Evidence);

public sealed partial class PostgresCorporateActionOperationsStore
{
    public async Task<IReadOnlyList<CorporateActionEvidenceDto>> ListDeadlineEvidenceAsync(
        Guid caseId, string tenantId, string companyId, CancellationToken ct = default)
        => await ExecutePersistenceReadAsync(async () =>
        {
            await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
            return await ReadDeadlineRowsAsync(connection, null, [caseId], tenantId, companyId, ct).ConfigureAwait(false);
        }, "deadline history read").ConfigureAwait(false);

    public async Task<CorporateActionCalendarReadSet> ReadCalendarAsync(
        string tenantId, string companyId, DateOnly from, DateOnly to,
        string? financialAccountId, string? custodyAccountId, CancellationToken ct = default)
        => await ExecutePersistenceReadAsync(async () =>
        {
            await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
            await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct).ConfigureAwait(false);
            var scopedCasePredicate = """
                pc.tenant_id = @tenant and pc.company_id = @company
                and (@account is null or pc.financial_account_id is null or pc.financial_account_id = @account)
                and (@custody is null or pc.custody_account_id is null or pc.custody_account_id = @custody)
                """;
            var proposals = new List<CorporateActionSourceProposalDto>();
            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = $$"""
                    {{ProposalSelect}}
                    where proposal_id in (
                        select p.proposal_id from {{Qualified("corporate_action_source_proposals")}} p
                        where (
                            p.state in ('Observed', 'ReviewRequired')
                            or exists (select 1 from {{Qualified("corporate_action_processing_cases")}} pc
                                where pc.proposal_id = p.proposal_id and {{scopedCasePredicate}})
                        ) and (
                            p.proposed_action ->> 'exDate' between @from_date and @to_date
                            or p.proposed_action ->> 'recordDate' between @from_date and @to_date
                            or p.proposed_action ->> 'payDate' between @from_date and @to_date
                            or exists (
                                select 1 from {{Qualified("corporate_action_processing_cases")}} pc
                                join {{Qualified("corporate_action_case_evidence")}} e on e.case_id = pc.case_id
                                where pc.proposal_id = p.proposal_id and {{scopedCasePredicate}}
                                    and e.evidence_kind = 'ElectionDeadline'
                                    and coalesce(e.metadata #>> '{deadline,date}', left(e.metadata #>> '{deadline,at}', 10))
                                        between @from_date and @to_date
                            )
                        )
                    );
                    """;
                AddCalendarParameters(command, tenantId, companyId, financialAccountId, custodyAccountId);
                command.Parameters.AddWithValue("from_date", from.ToString("yyyy-MM-dd"));
                command.Parameters.AddWithValue("to_date", to.ToString("yyyy-MM-dd"));
                await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await reader.ReadAsync(ct).ConfigureAwait(false)) proposals.Add(ReadProposal(reader));
            }
            var cases = new List<CorporateActionProcessingCaseDto>();
            if (proposals.Count > 0)
            {
                await using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = $$"""
                    {{CaseSelect}}
                    where pc.proposal_id = any(@proposals) and {{scopedCasePredicate}}
                    order by pc.case_id;
                    """;
                AddCalendarParameters(command, tenantId, companyId, financialAccountId, custodyAccountId);
                command.Parameters.AddWithValue("proposals", proposals.Select(p => p.ProposalId).ToArray());
                await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await reader.ReadAsync(ct).ConfigureAwait(false)) cases.Add(ReadCase(reader));
            }
            IReadOnlyList<CorporateActionEvidenceDto> evidence = cases.Count == 0 ? [] : await ReadDeadlineRowsAsync(connection, transaction,
                cases.Select(c => c.CaseId).ToArray(), tenantId, companyId, ct).ConfigureAwait(false);
            await transaction.CommitAsync(ct).ConfigureAwait(false);
            return new CorporateActionCalendarReadSet(proposals, cases, evidence);
        }, "date-range corporate action calendar read").ConfigureAwait(false);

    private static void AddCalendarParameters(NpgsqlCommand command, string tenant, string company, string? account, string? custody)
    {
        command.Parameters.AddWithValue("tenant", tenant);
        command.Parameters.AddWithValue("company", company);
        command.Parameters.Add(new NpgsqlParameter("account", NpgsqlTypes.NpgsqlDbType.Text) { Value = (object?)account ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("custody", NpgsqlTypes.NpgsqlDbType.Text) { Value = (object?)custody ?? DBNull.Value });
    }

    private async Task<IReadOnlyList<CorporateActionEvidenceDto>> ReadDeadlineRowsAsync(
        NpgsqlConnection connection, NpgsqlTransaction? transaction, Guid[] caseIds,
        string tenantId, string companyId, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $$"""
            select e.evidence_id, e.case_id, e.evidence_kind, e.evidence_reference, e.evidence_hash,
                e.description, e.metadata::text, e.case_version, e.recorded_by, e.recorded_at
            from {{Qualified("corporate_action_case_evidence")}} e
            join {{Qualified("corporate_action_processing_cases")}} pc on pc.case_id = e.case_id
            where e.case_id = any(@case_ids) and pc.tenant_id = @tenant and pc.company_id = @company
                and e.evidence_kind = 'ElectionDeadline'
            order by e.case_version, e.evidence_id;
            """;
        command.Parameters.AddWithValue("case_ids", caseIds);
        command.Parameters.AddWithValue("tenant", tenantId);
        command.Parameters.AddWithValue("company", companyId);
        var result = new List<CorporateActionEvidenceDto>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            result.Add(new CorporateActionEvidenceDto(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : JsonSerializer.Deserialize<JsonElement>(reader.GetString(6)),
                reader.GetInt64(7), reader.GetString(8), ReadTimestamp(reader, 9)));
        return result;
    }

    private async Task ValidateDeadlineAppendAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        CorporateActionProcessingCaseDto processingCase, AddCorporateActionEvidenceRequestDto request, CancellationToken ct)
    {
        var deadline = CorporateActionDeadlinePolicy.Read(request.Metadata);
        if (CorporateActionDeadlinePolicy.ValidateCapture(deadline, request.EvidenceReference, request.EvidenceHash, request.Description) is { } error)
            throw new CorporateActionValidationException(error);
        if (CorporateActionDeadlinePolicy.ValidateApplicability(processingCase.Scope, deadline) is { } applicabilityError)
            throw new CorporateActionScopeMismatchException(applicabilityError);
        var history = await ReadDeadlineRowsAsync(connection, transaction, [processingCase.CaseId], request.TenantId, request.CompanyId, ct).ConfigureAwait(false);
        var current = history.Where(e => CorporateActionDeadlinePolicy.Read(e.Metadata).SeriesId == deadline.SeriesId)
            .OrderByDescending(e => e.CaseVersion).FirstOrDefault();
        if (CorporateActionDeadlinePolicy.ValidateAppend(deadline, current?.EvidenceId,
            current is null ? null : CorporateActionDeadlinePolicy.Read(current.Metadata)) is { } appendError)
            throw new CorporateActionStateConflictException(processingCase.CaseId, appendError);
    }
}
