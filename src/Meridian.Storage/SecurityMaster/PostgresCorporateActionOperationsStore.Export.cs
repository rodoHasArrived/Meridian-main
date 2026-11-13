using System.Data;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Storage.SecurityMaster;

public sealed partial class PostgresCorporateActionOperationsStore
{
    public async Task<CorporateActionInboxExportSource> ReadInboxExportSourceAsync(
        string tenantId, string companyId, CancellationToken ct = default)
        => await ExecutePersistenceReadAsync(async () =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
            ArgumentException.ThrowIfNullOrWhiteSpace(companyId);
            await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
            await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct).ConfigureAwait(false);
            var proposals = new List<CorporateActionSourceProposalDto>();
            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = $"{ProposalSelect} where state in (@observed,@review) order by observed_at desc, proposal_id;";
                command.Parameters.AddWithValue("observed", CorporateActionSourceProposalStates.Observed);
                command.Parameters.AddWithValue("review", CorporateActionSourceProposalStates.ReviewRequired);
                await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                    proposals.Add(ReadProposal(reader));
            }
            var cases = new List<CorporateActionProcessingCaseDto>();
            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = $"{CaseSelect} where pc.tenant_id=@tenant and pc.company_id=@company order by pc.updated_at desc, pc.case_id;";
                command.Parameters.AddWithValue("tenant", tenantId);
                command.Parameters.AddWithValue("company", companyId);
                await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                    cases.Add(ReadCase(reader));
            }
            await transaction.CommitAsync(ct).ConfigureAwait(false);
            return new CorporateActionInboxExportSource(proposals, cases);
        }, "complete corporate-action inbox export").ConfigureAwait(false);
}
