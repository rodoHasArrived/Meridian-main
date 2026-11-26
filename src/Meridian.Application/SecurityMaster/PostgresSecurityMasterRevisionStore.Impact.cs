using Meridian.Contracts.Workstation;
using Meridian.Storage.SecurityMaster;

namespace Meridian.Application.SecurityMaster;

public sealed partial class PostgresSecurityMasterRevisionStore
{
    public Task<SecurityMasterRevisionRecord> BindImpactPreviewAsync(
        Guid revisionId, Guid securityId, SecurityMasterRevisionStateDto expectedState,
        string receiptId, long canonicalVersion, CancellationToken ct = default)
        => PostgresSecurityMasterMutation.ExecuteAsync(_options, securityId, async () =>
        {
            if (!Meridian.Contracts.Integrity.Sha256Digest.IsCanonical(receiptId) || canonicalVersion < 1)
                throw new ArgumentException("A canonical impact receipt digest and positive canonical version are required.");
            await using var lease = await PostgresSecurityMasterMutation.OpenConnectionAsync(_options, ct).ConfigureAwait(false);
            await using var version = lease.Connection.CreateCommand();
            // Hold the canonical row until the outer approval mutation commits, closing the
            // check-then-approve race with canonical amendments.
            version.CommandText = $"select version from {Qualified("securities")} where security_id = @security_id for share";
            version.Parameters.AddWithValue("security_id", securityId);
            var current = await version.ExecuteScalarAsync(ct).ConfigureAwait(false);
            if (current is not long actual || actual != canonicalVersion)
                throw new SecurityMasterRevisionStateException(revisionId, "Canonical terms changed before impact evidence binding.");

            await using var command = lease.Connection.CreateCommand();
            command.CommandText = $"""
                update {Qualified(RevisionsTable)}
                set reviewed_impact_receipt_id = @receipt, reviewed_canonical_version = @canonical_version
                where revision_id = @revision_id and security_id = @security_id and state = @state
                  and (reviewed_impact_receipt_id is null
                       or (reviewed_impact_receipt_id = @receipt and reviewed_canonical_version = @canonical_version))
                returning {RevisionColumns};
                """;
            command.Parameters.AddWithValue("receipt", receiptId);
            command.Parameters.AddWithValue("canonical_version", canonicalVersion);
            command.Parameters.AddWithValue("revision_id", revisionId);
            command.Parameters.AddWithValue("security_id", securityId);
            command.Parameters.AddWithValue("state", expectedState.ToString());
            await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (!await reader.ReadAsync(ct).ConfigureAwait(false))
                throw new SecurityMasterRevisionStateException(revisionId, "State changed or different impact evidence is already bound.");
            return MapRevision(reader);
        }, ct);
}
