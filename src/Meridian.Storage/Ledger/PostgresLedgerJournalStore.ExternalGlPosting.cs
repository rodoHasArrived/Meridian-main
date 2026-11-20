using System.Data;
using System.Text.Json;
using Meridian.Contracts.AccountingSystem;
using Meridian.Contracts.Integrity;
using Npgsql;

namespace Meridian.Storage.Ledger;

public sealed partial class PostgresLedgerJournalStore : IExternalGlConfirmationGate
{
    private static readonly JsonSerializerOptions ExternalGlJson = new(JsonSerializerDefaults.Web);

    public async Task RequireExternalGlBookAsync(Guid bookId, string tenant, string company, string fund, CancellationToken ct)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await LockExternalGlBookAsync(connection, null, bookId, tenant, company, fund, write: false, ct).ConfigureAwait(false);
    }

    private async Task LockExternalGlBookAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction,
        Guid bookId, string tenant, string company, string fund, bool write, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand($"""
            select b.ledger_book_id
            from {Qualified("ledger_books")} b
            join {Qualified("fund_profile_tenancy")} t on t.fund_profile_id = lower(trim(b.fund_profile_id))
            where b.ledger_book_id = @book and lower(trim(b.fund_profile_id)) = lower(@fund)
              and lower(trim(b.tenant_id)) = lower(@tenant) and lower(trim(t.tenant_id)) = lower(@tenant)
              and lower(trim(t.company_id)) = lower(@company)
            {(write ? "for update of b" : "for share of b")} for share of t
            """, connection, transaction);
        command.Parameters.AddWithValue("book", bookId);
        command.Parameters.AddWithValue("fund", fund);
        command.Parameters.AddWithValue("tenant", tenant);
        command.Parameters.AddWithValue("company", company);
        if (await command.ExecuteScalarAsync(ct).ConfigureAwait(false) is null)
            throw new UnauthorizedAccessException("The fund and ledger book must belong to the signed-in company.");
    }

    public async Task RetainExternalGlMappingProfileAsync(Guid bookId, string tenant, string company, string fund,
        string providerId, string profileId, string content, ExternalGlDestination? expectedDestination, CancellationToken ct)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        await LockExternalGlBookAsync(connection, transaction, bookId, tenant, company, fund, write: false, ct).ConfigureAwait(false);
        if (expectedDestination is not null &&
            (await ReadExternalGlPolicyAsync(connection, transaction, bookId, ct).ConfigureAwait(false))?.Destination != expectedDestination)
            throw new InvalidOperationException("The mapping certificate must identify the book's current external destination.");

        await using var command = new NpgsqlCommand($"""
            insert into {Qualified("external_gl_mapping_profiles")}
                (ledger_book_id, tenant_id, company_id, provider_id, profile_id, content)
            values (@book, @tenant, @company, @provider, @profile, @content)
            """, connection, transaction);
        command.Parameters.AddWithValue("book", bookId);
        command.Parameters.AddWithValue("tenant", tenant);
        command.Parameters.AddWithValue("company", company);
        command.Parameters.AddWithValue("provider", providerId);
        command.Parameters.AddWithValue("profile", profileId.ToLowerInvariant());
        command.Parameters.AddWithValue("content", content);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<string>> ReadExternalGlMappingProfilesAsync(Guid bookId, string tenant, string company, string fund,
        string? providerId, string? profileId, CancellationToken ct)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await LockExternalGlBookAsync(connection, null, bookId, tenant, company, fund, write: false, ct).ConfigureAwait(false);
        await using var command = new NpgsqlCommand($"""
            select distinct on (provider_id, profile_id) content
            from {Qualified("external_gl_mapping_profiles")}
            where ledger_book_id = @book and lower(tenant_id) = lower(@tenant) and lower(company_id) = lower(@company)
              and (@provider = '' or provider_id = @provider) and (@profile = '' or profile_id = @profile)
            order by provider_id, profile_id, revision desc
            """, connection);
        command.Parameters.AddWithValue("book", bookId);
        command.Parameters.AddWithValue("tenant", tenant);
        command.Parameters.AddWithValue("company", company);
        command.Parameters.AddWithValue("provider", providerId ?? "");
        command.Parameters.AddWithValue("profile", profileId?.ToLowerInvariant() ?? "");
        var retained = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            retained.Add(reader.GetString(0));
        return retained;
    }

    public async Task<ExternalGlBookPolicy?> GetExternalGlPolicyAsync(Guid bookId, string tenant, string company, string fund, CancellationToken ct)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await LockExternalGlBookAsync(connection, null, bookId, tenant, company, fund, write: false, ct).ConfigureAwait(false);
        return await ReadExternalGlPolicyAsync(connection, null, bookId, ct).ConfigureAwait(false);
    }

    public async Task SaveExternalGlPolicyAsync(Guid bookId, string tenant, string company, string fund,
        ExternalGlBookPolicy policy, CancellationToken ct)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        await LockExternalGlBookAsync(connection, transaction, bookId, tenant, company, fund, write: true, ct).ConfigureAwait(false);
        var previous = await ReadExternalGlPolicyAsync(connection, transaction, bookId, ct).ConfigureAwait(false);
        if (previous is not null && previous.Destination != policy.Destination)
        {
            await using var check = new NpgsqlCommand($"""
                select exists (select 1 from {Qualified("external_gl_posting_packages")} p
                    join {Qualified("external_gl_posting_decisions")} d using (package_id)
                    where p.ledger_book_id = @book and d.decision_kind = 'Approval')
                """, connection, transaction);
            check.Parameters.AddWithValue("book", bookId);
            if (await check.ExecuteScalarAsync(ct).ConfigureAwait(false) is true)
                throw new InvalidOperationException("This book already has approved posting evidence for its external destination.");
        }

        await using var command = new NpgsqlCommand($"""
            insert into {Qualified("external_gl_book_policy")}
                (ledger_book_id, tenant_id, company_id, provider_id, external_ledger_id, environment, confirmation_required, policy)
            values (@book, @tenant, @company, @provider, @external, @environment, @required, @policy)
            on conflict (ledger_book_id) do update set tenant_id = excluded.tenant_id, company_id = excluded.company_id,
                provider_id = excluded.provider_id, external_ledger_id = excluded.external_ledger_id,
                environment = excluded.environment, confirmation_required = excluded.confirmation_required, policy = excluded.policy
            """, connection, transaction);
        command.Parameters.AddWithValue("book", bookId);
        command.Parameters.AddWithValue("tenant", tenant);
        command.Parameters.AddWithValue("company", company);
        BindDestination(command, policy.Destination);
        command.Parameters.AddWithValue("required", policy.ConfirmationRequired);
        command.Parameters.AddWithValue("policy", SerializeExternalGl(policy));
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
    }

    private async Task<ExternalGlBookPolicy?> ReadExternalGlPolicyAsync(
        NpgsqlConnection connection, NpgsqlTransaction? transaction, Guid bookId, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(
            $"""
            select policy.policy from {Qualified("external_gl_book_policy")} policy
            join {Qualified("ledger_books")} b using (ledger_book_id)
            join {Qualified("fund_profile_tenancy")} t on t.fund_profile_id = lower(trim(b.fund_profile_id))
            where policy.ledger_book_id = @book and lower(policy.tenant_id) = lower(b.tenant_id)
              and lower(policy.tenant_id) = lower(t.tenant_id) and lower(policy.company_id) = lower(t.company_id)
            """, connection, transaction);
        command.Parameters.AddWithValue("book", bookId);
        var json = await command.ExecuteScalarAsync(ct).ConfigureAwait(false) as string;
        return json is null ? null : DeserializeExternalGl<ExternalGlBookPolicy>(json);
    }

    // Internal closing transfers and reversals of those transfers are Meridian close mechanics.
    private string ExternalGlJournalPopulation => $"""
        from {Qualified("journal_entries")} j
        left join {Qualified("journal_entries")} original on original.journal_entry_id = j.source_journal_entry_id
        join {Qualified("accounting_periods")} period on period.period_id = j.period_id
        where j.period_id = @period and period.ledger_book_id = @book and j.posting_kind <> 'ClosingEntry'
          and original.posting_kind is distinct from 'ClosingEntry'
        """;

    public async Task<IReadOnlyList<Guid>> ListExternalGlSourceIdsAsync(
        ExternalGlPostingScope scope, ExternalGlDestination destination, bool unassignedOnly, CancellationToken ct)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = new NpgsqlCommand($"""
            select j.journal_entry_id {ExternalGlJournalPopulation}
            and lower(period.tenant_id) = lower(@tenant)
            and (@unassigned = false or not exists (
                select 1 from {Qualified("external_gl_posting_outbox")} o
                where o.journal_entry_id = j.journal_entry_id and not o.superseded
                  and o.provider_id = @provider and o.external_ledger_id = @external and o.environment = @environment))
            order by j.global_sequence, j.journal_entry_id
            """, connection);
        BindScope(command, scope);
        command.Parameters.AddWithValue("unassigned", unassignedOnly);
        BindDestination(command, destination);
        var ids = new List<Guid>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            ids.Add(reader.GetGuid(0));
        return ids;
    }

    public async Task RequireConfirmationAsync(Guid ledgerBookId, Guid periodId, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await RequireExternalGlConfirmationAsync(connection, null, ledgerBookId, periodId, ct).ConfigureAwait(false);
    }

    public async Task RequireConfirmationForRangeAsync(Guid ledgerBookId, DateOnly start, DateOnly end, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct).ConfigureAwait(false);
        var policy = await ReadExternalGlPolicyAsync(connection, transaction, ledgerBookId, ct).ConfigureAwait(false);
        if (policy?.ConfirmationRequired != true)
            return;

        var periods = new List<Guid>();
        await using (var command = new NpgsqlCommand($"""
            select period_id from {Qualified("accounting_periods")}
            where ledger_book_id = @book and start_date <= @end and end_date >= @start
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("book", ledgerBookId);
            command.Parameters.AddWithValue("start", start);
            command.Parameters.AddWithValue("end", end);
            await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
                periods.Add(reader.GetGuid(0));
        }
        if (periods.Count == 0)
            throw new LedgerPeriodTransitionException("External GL confirmation is required. Resolve the report's retained accounting period.");

        foreach (var period in periods)
            await RequireExternalGlConfirmationAsync(connection, transaction, ledgerBookId, period, ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
    }

    private async Task RequireExternalGlConfirmationAsync(
        NpgsqlConnection connection, NpgsqlTransaction? transaction, Guid bookId, Guid periodId, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand($"""
            with required_policy as (
                select policy.* from {Qualified("external_gl_book_policy")} policy
                join {Qualified("ledger_books")} b using (ledger_book_id)
                join {Qualified("fund_profile_tenancy")} t on t.fund_profile_id = lower(trim(b.fund_profile_id))
                where policy.ledger_book_id = @book and policy.confirmation_required
                  and lower(policy.tenant_id) = lower(b.tenant_id) and lower(policy.tenant_id) = lower(t.tenant_id)
                  and lower(policy.company_id) = lower(t.company_id)
            )
            select case
                when not exists (select 1 from required_policy) then 0
                when not exists (select 1 from {Qualified("accounting_periods")}
                    where period_id = @period and ledger_book_id = @book) then -1
                else (select count(*) {ExternalGlJournalPopulation}
                    and not exists (
                        select 1 from {Qualified("external_gl_posting_outbox")} o
                        join {Qualified("external_gl_posting_packages")} p on p.package_id = o.package_id
                        join {Qualified("external_gl_posting_decisions")} a on a.package_id = p.package_id and a.decision_kind = 'Approval'
                        join required_policy policy on policy.ledger_book_id = p.ledger_book_id
                        where o.journal_entry_id = j.journal_entry_id and not o.superseded and o.status = 'FullyAccepted'
                          and p.period_id = @period
                          and lower(p.tenant_id) = lower(policy.tenant_id) and lower(p.company_id) = lower(policy.company_id)
                          and o.provider_id = policy.provider_id and o.external_ledger_id = policy.external_ledger_id
                          and o.environment = policy.environment
                          and exists (select 1 from {Qualified("external_gl_posting_receipts")} receipt
                            where receipt.idempotency_key = o.idempotency_key and receipt.observation::jsonb -> 'receipt' ->> 'kind' = 'Completed')))
                end
            """, connection, transaction);
        command.Parameters.AddWithValue("book", bookId);
        command.Parameters.AddWithValue("period", periodId);
        var missing = (long)(await command.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
        if (missing < 0)
            throw new LedgerPeriodTransitionException("External GL confirmation is required. Resolve an accounting period in the selected ledger book.");
        if (missing > 0)
            throw new LedgerPeriodTransitionException($"External GL confirmation is required for this book. {missing} Meridian journal(s) in the period need a fully accepted receipt.");
    }

    public async Task<ExternalGlPostingPackage> RetainExternalGlPackageAsync(ExternalGlPostingPayload payload,
        string packageId, string actor, DateTimeOffset now, CancellationToken ct)
    {
        var scope = payload.Scope;
        var json = SerializeExternalGl(payload);
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        await LockExternalGlBookAsync(connection, transaction, scope.LedgerBookId, scope.TenantId, scope.CompanyId, scope.FundProfileId, write: false, ct).ConfigureAwait(false);
        await RequireExternalGlPeriodAsync(connection, transaction, scope, ct).ConfigureAwait(false);
        var policy = await ReadExternalGlPolicyAsync(connection, transaction, scope.LedgerBookId, ct).ConfigureAwait(false);
        if (policy?.Destination != payload.Destination)
            throw new InvalidOperationException("The book's external destination changed. Prepare a new package.");

        await using var command = new NpgsqlCommand($"""
            insert into {Qualified("external_gl_posting_packages")}
                (package_id, ledger_book_id, period_id, tenant_id, company_id, payload_hash, payload, prepared_by, prepared_at)
            values (@package, @book, @period, @tenant, @company, @hash, @payload, @actor, @now)
            on conflict (package_id) do nothing
            """, connection, transaction);
        BindScope(command, scope, packageId);
        command.Parameters.AddWithValue("hash", Sha256Digest.ComputeUtf8(json));
        command.Parameters.AddWithValue("payload", json);
        command.Parameters.AddWithValue("actor", actor);
        command.Parameters.AddWithValue("now", now);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        var retained = await ReadExternalGlPackageAsync(connection, transaction, scope, packageId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The retained package is outside this accounting scope.");
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return retained;
    }

    public async Task<ExternalGlPostingPackage?> GetExternalGlPackageAsync(ExternalGlPostingScope scope, string packageId, CancellationToken ct)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct).ConfigureAwait(false);
        await LockExternalGlBookAsync(connection, transaction, scope.LedgerBookId, scope.TenantId, scope.CompanyId, scope.FundProfileId, write: false, ct).ConfigureAwait(false);
        await RequireExternalGlPeriodAsync(connection, transaction, scope, ct).ConfigureAwait(false);
        var package = await ReadExternalGlPackageAsync(connection, transaction, scope, packageId, ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return package;
    }

    public async Task<IReadOnlyList<ExternalGlPackageListItem>> ListExternalGlPackagesAsync(ExternalGlPostingScope scope, CancellationToken ct)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = new NpgsqlCommand($"""
            select p.package_id, p.payload::jsonb ->> 'purpose', p.prepared_at,
                exists (select 1 from {Qualified("external_gl_posting_decisions")} c where c.package_id = p.package_id and c.decision_kind = 'Certification'),
                exists (select 1 from {Qualified("external_gl_posting_decisions")} a where a.package_id = p.package_id and a.decision_kind = 'Approval'),
                array(select o.status from {Qualified("external_gl_posting_outbox")} o where o.package_id = p.package_id)
            from {Qualified("external_gl_posting_packages")} p
            where {ExternalGlScopeSql} order by p.prepared_at desc, p.package_id
            """, connection);
        BindScope(command, scope);
        var results = new List<ExternalGlPackageListItem>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            results.Add(new(reader.GetString(0), Enum.Parse<ExternalGlPostingPurpose>(reader.GetString(1)),
                PackageStatus(reader.GetFieldValue<string[]>(5).Select(Enum.Parse<ExternalGlPostingStatus>)),
                reader.GetFieldValue<DateTimeOffset>(2), reader.GetBoolean(3), reader.GetBoolean(4)));
        }
        return results;
    }

    private async Task RequireExternalGlPeriodAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        ExternalGlPostingScope scope, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand($"""
            select 1 from {Qualified("accounting_periods")}
            where period_id = @period and ledger_book_id = @book and lower(tenant_id) = lower(@tenant)
            """, connection, transaction);
        BindScope(command, scope);
        if (await command.ExecuteScalarAsync(ct).ConfigureAwait(false) is null)
            throw new LedgerBookNotFoundException("The accounting period is outside the selected book and tenant.");
    }

    private const string ExternalGlScopeSql =
        "p.ledger_book_id = @book and p.period_id = @period and lower(p.tenant_id) = lower(@tenant) and lower(p.company_id) = lower(@company)";

    private async Task<ExternalGlPostingPackage?> ReadExternalGlPackageAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        ExternalGlPostingScope scope, string packageId, CancellationToken ct)
    {
        ExternalGlPostingPayload payload;
        string hash;
        string actor;
        DateTimeOffset prepared;
        ExternalGlPostingDecision? certification;
        ExternalGlPostingDecision? approval;
        await using (var command = new NpgsqlCommand($"""
            select p.payload, p.payload_hash, p.prepared_by, p.prepared_at, c.decision, a.decision
            from {Qualified("external_gl_posting_packages")} p
            left join {Qualified("external_gl_posting_decisions")} c on c.package_id = p.package_id and c.decision_kind = 'Certification'
            left join {Qualified("external_gl_posting_decisions")} a on a.package_id = p.package_id and a.decision_kind = 'Approval'
            where p.package_id = @package and {ExternalGlScopeSql}
            """, connection, transaction))
        {
            BindScope(command, scope, packageId);
            await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (!await reader.ReadAsync(ct).ConfigureAwait(false))
                return null;
            var json = reader.GetString(0);
            hash = reader.GetString(1);
            if (Sha256Digest.ComputeUtf8(json) != hash)
                throw new InvalidDataException("The external GL package does not match its retained content hash.");
            payload = DeserializeExternalGl<ExternalGlPostingPayload>(json);
            actor = reader.GetString(2);
            prepared = reader.GetFieldValue<DateTimeOffset>(3);
            certification = reader.IsDBNull(4) ? null : DeserializeExternalGl<ExternalGlPostingDecision>(reader.GetString(4));
            approval = reader.IsDBNull(5) ? null : DeserializeExternalGl<ExternalGlPostingDecision>(reader.GetString(5));
        }

        var observations = new Dictionary<string, (ExternalGlPostingStatus Status, List<ExternalGlReceiptObservation> History, int Attempts, DateTimeOffset? AttemptedAt)>();
        await using (var command = new NpgsqlCommand($"""
            select o.idempotency_key, o.status, o.post_attempts, o.last_post_at, r.observation
            from {Qualified("external_gl_posting_outbox")} o
            left join {Qualified("external_gl_posting_receipts")} r using (idempotency_key)
            where o.package_id = @package order by o.idempotency_key, r.observation_id
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("package", packageId);
            await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                var key = reader.GetString(0);
                if (!observations.TryGetValue(key, out var entry))
                {
                    entry = (Enum.Parse<ExternalGlPostingStatus>(reader.GetString(1)), [], reader.GetInt32(2),
                        reader.IsDBNull(3) ? null : reader.GetFieldValue<DateTimeOffset>(3));
                    observations.Add(key, entry);
                }
                if (!reader.IsDBNull(4))
                    entry.History.Add(DeserializeExternalGl<ExternalGlReceiptObservation>(reader.GetString(4)));
            }
        }
        var progress = payload.Documents.Select(doc => observations.TryGetValue(doc.IdempotencyKey, out var entry)
            ? new ExternalGlDocumentProgress(doc.IdempotencyKey, entry.Status, entry.History, entry.Attempts, entry.AttemptedAt)
            : new ExternalGlDocumentProgress(doc.IdempotencyKey, ExternalGlPostingStatus.Pending, [])).ToArray();
        return new(packageId, hash, payload, actor, prepared, certification, approval, PackageStatus(progress.Select(row => row.Status)), progress);
    }

    private static ExternalGlPostingStatus PackageStatus(IEnumerable<ExternalGlPostingStatus> statuses)
    {
        var states = statuses.ToHashSet();
        if (states.Contains(ExternalGlPostingStatus.OutcomeUnknown))
            return ExternalGlPostingStatus.OutcomeUnknown;
        if (states.Count == 0 || states.Contains(ExternalGlPostingStatus.Pending))
            return ExternalGlPostingStatus.Pending;
        return states.Count == 1 ? states.Single() : ExternalGlPostingStatus.PartiallyAccepted;
    }

    private static void BindScope(NpgsqlCommand command, ExternalGlPostingScope scope, string? packageId = null)
    {
        command.Parameters.AddWithValue("book", scope.LedgerBookId);
        command.Parameters.AddWithValue("period", scope.PeriodId);
        command.Parameters.AddWithValue("tenant", scope.TenantId);
        command.Parameters.AddWithValue("company", scope.CompanyId);
        if (packageId is not null)
            command.Parameters.AddWithValue("package", packageId);
    }

    private static void BindDestination(NpgsqlCommand command, ExternalGlDestination destination)
    {
        command.Parameters.AddWithValue("provider", destination.ProviderId);
        command.Parameters.AddWithValue("external", destination.ExternalLedgerId);
        command.Parameters.AddWithValue("environment", destination.Environment);
    }

    private static string SerializeExternalGl<T>(T value) => JsonSerializer.Serialize(value, ExternalGlJson);

    private static T DeserializeExternalGl<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, ExternalGlJson) ?? throw new InvalidDataException("External GL evidence is unreadable.");
}
