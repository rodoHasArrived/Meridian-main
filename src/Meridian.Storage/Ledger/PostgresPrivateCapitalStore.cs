using System.Data;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.Ledger;
using Meridian.Ledger;
using Npgsql;

namespace Meridian.Storage.Ledger;

/// <summary>PostgreSQL authority sharing the ledger migration and posting transaction boundary.</summary>
public sealed class PostgresPrivateCapitalStore : IPrivateCapitalStore
{
    internal static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private readonly LedgerJournalStoreOptions _options;
    private readonly string _schema;
    private const string ScopePredicate = "tenant_id=@tenant and company_id=@company and fund_profile_id=@fund and ledger_book_id=@book";

    public PostgresPrivateCapitalStore(LedgerJournalStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.ConnectionString))
            throw new ArgumentException("PostgreSQL connection is required for private-capital authority.", nameof(options));
        if (string.IsNullOrWhiteSpace(options.SchemaName))
            throw new ArgumentException("Ledger schema is required.", nameof(options));
        _options = options;
        _schema = "\"" + options.SchemaName.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    public async Task<IReadOnlyList<PrivateCapitalCommitmentDto>> ListCommitmentsAsync(PrivateCapitalScopeDto scope, CancellationToken ct = default)
    {
        scope = PrivateCapitalAuthorityRules.NormalizeScope(scope);
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await EnsureScopeAsync(connection, null, scope, ct).ConfigureAwait(false);
        var values = await ReadCommitmentsAsync(connection, null, scope, ct).ConfigureAwait(false);
        var receipts = await ReadReceiptsAsync(connection, null, scope, ct).ConfigureAwait(false);
        return values.Select(x => Decorate(x, receipts)).ToArray();
    }

    public async Task<PrivateCapitalCommitmentDto?> GetCommitmentAsync(
        PrivateCapitalScopeDto scope, string commitmentId, int? revision = null, CancellationToken ct = default)
    {
        commitmentId = PrivateCapitalAuthorityRules.Id(commitmentId, nameof(commitmentId));
        var all = await ListCommitmentsAsync(scope, ct).ConfigureAwait(false);
        return all.Where(x => x.CommitmentId == commitmentId && (!revision.HasValue || x.Revision == revision.Value))
            .OrderByDescending(x => x.Revision).FirstOrDefault();
    }

    public async Task<PrivateCapitalCommitmentDto> SaveCommitmentDraftAsync(
        PrivateCapitalCommitmentDto draft, int expectedRevision, CancellationToken ct = default)
    {
        PrivateCapitalAuthorityRules.ValidateCommitment(draft);
        var scope = PrivateCapitalAuthorityRules.NormalizeScope(draft.Scope);
        if (draft.Scope != scope || draft.CommitmentId != PrivateCapitalAuthorityRules.Id(draft.CommitmentId, nameof(draft.CommitmentId))
            || expectedRevision < 0 || draft.Revision != checked(expectedRevision + 1)
            || draft.ApprovalStatus != PrivateCapitalCommitmentApprovalStatus.Draft
            || draft.ApprovedBy is not null || draft.ApprovedAtUtc is not null
            || draft.CalledAmount != 0m || draft.FundedAmount != 0m)
            throw new ArgumentException("A new canonical, unapproved commitment revision is required.");
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable, ct).ConfigureAwait(false);
        await EnsureScopeAsync(connection, transaction, scope, ct).ConfigureAwait(false);
        await LockAsync(connection, transaction, PrivateCapitalAuthorityRules.LockKey(scope, draft.CommitmentId), ct).ConfigureAwait(false);
        var revisions = (await ReadCommitmentsAsync(connection, transaction, scope, ct).ConfigureAwait(false))
            .Where(x => x.CommitmentId == draft.CommitmentId).OrderByDescending(x => x.Revision).ToArray();
        var retained = revisions.FirstOrDefault(x => x.Revision == draft.Revision);
        if (retained is not null)
        {
            if (!SameTerms(retained, draft))
                throw new InvalidOperationException("This commitment revision already retains different terms.");
            return retained;
        }
        var latest = revisions.FirstOrDefault();
        if ((latest?.Revision ?? 0) != expectedRevision)
            throw new InvalidOperationException("The commitment revision changed. Reload the retained terms before revising them.");
        if (latest is not null && (latest.InvestorId != draft.InvestorId
            || latest.CapitalAccountId != draft.CapitalAccountId || latest.CommitmentDate != draft.CommitmentDate))
            throw new InvalidOperationException("Investor, capital account, and original commitment date cannot be reassigned by a revision.");
        await using var command = new NpgsqlCommand($"""
            insert into {_schema}.private_capital_commitments
                (tenant_id,company_id,fund_profile_id,ledger_book_id,commitment_id,revision,document)
            values (@tenant,@company,@fund,@book,@id,@revision,cast(@document as jsonb));
            """, connection, transaction);
        AddScope(command, scope);
        command.Parameters.AddWithValue("id", draft.CommitmentId);
        command.Parameters.AddWithValue("revision", draft.Revision);
        command.Parameters.AddWithValue("document", JsonSerializer.Serialize(draft, JsonOptions));
        try
        {
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            await transaction.CommitAsync(ct).ConfigureAwait(false);
        }
        catch (PostgresException ex) when (IsConcurrencyFailure(ex))
        {
            throw new InvalidOperationException("Commitment authority changed concurrently. Reload the retained revision before retrying.", ex);
        }
        return draft;
    }

    public async Task<PrivateCapitalCommitmentDto> ApproveCommitmentAsync(
        PrivateCapitalScopeDto scope, string commitmentId, int revision, string actor, DateTimeOffset approvedAtUtc,
        CancellationToken ct = default)
    {
        scope = PrivateCapitalAuthorityRules.NormalizeScope(scope);
        commitmentId = PrivateCapitalAuthorityRules.Id(commitmentId, nameof(commitmentId));
        if (revision < 1 || string.IsNullOrWhiteSpace(actor))
            throw new ArgumentException("An exact revision and authenticated review actor are required.");
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable, ct).ConfigureAwait(false);
        await EnsureScopeAsync(connection, transaction, scope, ct).ConfigureAwait(false);
        await LockAsync(connection, transaction, PrivateCapitalAuthorityRules.LockKey(scope, commitmentId), ct).ConfigureAwait(false);
        var allRevisions = await ReadCommitmentsAsync(connection, transaction, scope, ct).ConfigureAwait(false);
        var revisions = allRevisions.Where(x => x.CommitmentId == commitmentId).OrderByDescending(x => x.Revision).ToArray();
        var current = revisions.FirstOrDefault(x => x.Revision == revision)
            ?? throw new InvalidOperationException("The commitment revision does not exist in this scope.");
        if (current.ApprovalStatus == PrivateCapitalCommitmentApprovalStatus.Approved)
            return current;
        if (revisions[0].Revision != revision)
            throw new InvalidOperationException("A superseded draft cannot become the current commitment authority.");
        PrivateCapitalAuthorityRules.ValidateCommitment(current);
        await LockAsync(connection, transaction,
            $"private-capital-account|{scope.TenantId}|{scope.CompanyId}|{scope.FundProfileId}|{scope.LedgerBookId:D}|{current.CapitalAccountId}", ct).ConfigureAwait(false);
        if (allRevisions.Any(x => x.ApprovalStatus == PrivateCapitalCommitmentApprovalStatus.Approved
            && x.CapitalAccountId == current.CapitalAccountId && x.InvestorId != current.InvestorId))
            throw new InvalidOperationException("This capital account already has approved authority for a different investor.");
        var previous = revisions.FirstOrDefault(x => x.ApprovalStatus == PrivateCapitalCommitmentApprovalStatus.Approved);
        if (previous is not null && current.EffectiveFrom < previous.EffectiveFrom)
            throw new InvalidOperationException("Backdated replacement before the preceding approved effective date requires governed opening reconciliation.");
        if (await HasLegacyAsync(connection, transaction, scope, current.InvestorId, ct).ConfigureAwait(false))
            throw new InvalidOperationException("Historical capital activity requires opening reconciliation before this commitment can be approved.");
        var receipts = await ReadReceiptsAsync(connection, transaction, scope, ct).ConfigureAwait(false);
        if (receipts.Where(x => x.CommitmentId == commitmentId && x.Kind == "Issuance").Sum(x => x.Amount) > current.TotalCommitment)
            throw new InvalidOperationException("The approved commitment cannot be reduced below capital already called.");
        var approved = current with
        {
            ApprovalStatus = PrivateCapitalCommitmentApprovalStatus.Approved,
            ApprovedBy = actor.Trim(),
            ApprovedAtUtc = approvedAtUtc
        };
        await using var command = new NpgsqlCommand($"""
            update {_schema}.private_capital_commitments set document=cast(@document as jsonb)
            where {ScopePredicate} and commitment_id=@id and revision=@revision;
            """, connection, transaction);
        AddScope(command, scope);
        command.Parameters.AddWithValue("id", commitmentId);
        command.Parameters.AddWithValue("revision", revision);
        command.Parameters.AddWithValue("document", JsonSerializer.Serialize(approved, JsonOptions));
        try
        {
            if (await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) != 1)
                throw new InvalidOperationException("Commitment authority changed during review.");
            await transaction.CommitAsync(ct).ConfigureAwait(false);
        }
        catch (PostgresException ex) when (IsConcurrencyFailure(ex))
        {
            throw new InvalidOperationException("Commitment authority changed concurrently. Reload and review the retained revision again.", ex);
        }
        return Decorate(approved, receipts);
    }

    public async Task<IReadOnlyList<PrivateCapitalCallDto>> ListCallsAsync(PrivateCapitalScopeDto scope, CancellationToken ct = default)
    {
        scope = PrivateCapitalAuthorityRules.NormalizeScope(scope);
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await EnsureScopeAsync(connection, null, scope, ct).ConfigureAwait(false);
        var calls = await ReadCallsAsync(connection, null, scope, ct).ConfigureAwait(false);
        var receipts = await ReadReceiptsAsync(connection, null, scope, ct).ConfigureAwait(false);
        return calls.Select(x => Decorate(x, receipts)).ToArray();
    }

    public async Task<PrivateCapitalCallDto?> GetCallAsync(PrivateCapitalScopeDto scope, string callId, CancellationToken ct = default)
    {
        callId = PrivateCapitalAuthorityRules.Id(callId, nameof(callId));
        return (await ListCallsAsync(scope, ct).ConfigureAwait(false)).FirstOrDefault(x => x.CallId == callId);
    }

    public async Task<PrivateCapitalCallDto> SaveCallAsync(PrivateCapitalCallDto call, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(call);
        var scope = PrivateCapitalAuthorityRules.NormalizeScope(call.Scope);
        if (call.Scope != scope || call.CallId != PrivateCapitalAuthorityRules.Id(call.CallId, nameof(call.CallId))
            || call.AmountToCall <= 0m || call.DueDate < call.NoticeDate
            || call.Lines is not { Count: > 0 } || call.Lines.Any(x => x.Amount <= 0m || x.IssuedAmount != 0m || x.FundedAmount != 0m || x.IssuanceJournalEntryId is not null)
            || call.Lines.Sum(x => x.Amount) != call.AmountToCall
            || call.Lines.Select(x => x.CommitmentId).Distinct(StringComparer.Ordinal).Count() != call.Lines.Count
            || call.PlanHash != PrivateCapitalCallPlanHash.Compute(call) || string.IsNullOrWhiteSpace(call.CreatedBy))
            throw new ArgumentException("A complete unchanged call preview is required.");
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable, ct).ConfigureAwait(false);
        await EnsureScopeAsync(connection, transaction, scope, ct).ConfigureAwait(false);
        foreach (var id in call.Lines.Select(x => x.CommitmentId).OrderBy(x => x, StringComparer.Ordinal))
            await LockAsync(connection, transaction, PrivateCapitalAuthorityRules.LockKey(scope, id), ct).ConfigureAwait(false);
        await LockAsync(connection, transaction, $"private-capital-call|{scope.TenantId}|{scope.CompanyId}|{scope.LedgerBookId:D}|{call.CallId}", ct).ConfigureAwait(false);
        var retained = (await ReadCallsAsync(connection, transaction, scope, ct).ConfigureAwait(false)).FirstOrDefault(x => x.CallId == call.CallId);
        if (retained is not null)
        {
            if (retained.PlanHash != call.PlanHash)
                throw new InvalidOperationException("This call identity already retains a different reviewed plan.");
            return retained;
        }
        var revisions = await ReadCommitmentsAsync(connection, transaction, scope, ct).ConfigureAwait(false);
        var receipts = await ReadReceiptsAsync(connection, transaction, scope, ct).ConfigureAwait(false);
        foreach (var line in call.Lines)
        {
            var current = revisions.Where(x => x.CommitmentId == line.CommitmentId
                    && x.ApprovalStatus == PrivateCapitalCommitmentApprovalStatus.Approved && x.EffectiveFrom <= call.NoticeDate)
                .OrderByDescending(x => x.EffectiveFrom).ThenByDescending(x => x.Revision).FirstOrDefault();
            var called = receipts.Where(x => x.CommitmentId == line.CommitmentId && x.Kind == "Issuance").Sum(x => x.Amount);
            if (current is null || !current.IsActive || current.Revision != line.CommitmentRevision
                || current.InvestorId != line.InvestorId || current.CapitalAccountId != line.CapitalAccountId
                || current.TotalCommitment - called != line.UncalledBefore
                || line.UncalledAfter != line.UncalledBefore - line.Amount || line.UncalledAfter < 0m)
                throw new InvalidOperationException("Commitment authority or called capital changed. Preview and review the call again.");
        }
        await using var command = new NpgsqlCommand($"""
            insert into {_schema}.private_capital_calls
                (tenant_id,company_id,fund_profile_id,ledger_book_id,call_id,document)
            values (@tenant,@company,@fund,@book,@id,cast(@document as jsonb));
            """, connection, transaction);
        AddScope(command, scope);
        command.Parameters.AddWithValue("id", call.CallId);
        command.Parameters.AddWithValue("document", JsonSerializer.Serialize(call, JsonOptions));
        try
        {
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            await transaction.CommitAsync(ct).ConfigureAwait(false);
        }
        catch (PostgresException ex) when (IsConcurrencyFailure(ex))
        {
            throw new InvalidOperationException("Call authority changed concurrently. Reload and review the allocation again.", ex);
        }
        return call;
    }

    public async Task<IReadOnlyList<PrivateCapitalPostingReceiptDto>> ListPostingReceiptsAsync(PrivateCapitalScopeDto scope, CancellationToken ct = default)
    {
        scope = PrivateCapitalAuthorityRules.NormalizeScope(scope);
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await EnsureScopeAsync(connection, null, scope, ct).ConfigureAwait(false);
        return await ReadReceiptsAsync(connection, null, scope, ct).ConfigureAwait(false);
    }

    public async Task<bool> HasUnreconciledLegacyCapitalAsync(PrivateCapitalScopeDto scope, string investorId, CancellationToken ct = default)
    {
        scope = PrivateCapitalAuthorityRules.NormalizeScope(scope);
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await EnsureScopeAsync(connection, null, scope, ct).ConfigureAwait(false);
        return await HasLegacyAsync(connection, null, scope, investorId, ct).ConfigureAwait(false);
    }

    private async Task<bool> HasLegacyAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction,
        PrivateCapitalScopeDto scope, string investorId, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand($"""
            select exists (
                select 1 from {_schema}.journal_entries je
                join {_schema}.accounting_periods p on p.period_id=je.period_id
                where p.ledger_book_id=@book
                  and (lower(coalesce(je.metadata->>'fundEventType',''))='capitalcall'
                    or lower(coalesce(je.metadata->'tags'->>'fundEventType',''))='capitalcall')
                  and (nullif(coalesce(je.metadata->>'investorId',je.metadata->'tags'->>'investorId',''),'') is null
                    or lower(coalesce(je.metadata->>'investorId',je.metadata->'tags'->>'investorId'))=@investor)
                  and not exists (select 1 from {_schema}.private_capital_postings pc where pc.journal_entry_id=je.journal_entry_id));
            """, connection, transaction);
        AddScope(command, scope);
        command.Parameters.AddWithValue("investor", PrivateCapitalAuthorityRules.Id(investorId, nameof(investorId)));
        return (bool)(await command.ExecuteScalarAsync(ct).ConfigureAwait(false) ?? true);
    }

    private async Task EnsureScopeAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction,
        PrivateCapitalScopeDto scope, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand($"""
            select exists (select 1 from {_schema}.ledger_books b
                join {_schema}.fund_profile_tenancy t on t.fund_profile_id=lower(btrim(b.fund_profile_id))
                where b.ledger_book_id=@book and lower(btrim(b.fund_profile_id))=@fund
                  and lower(btrim(b.tenant_id))=@tenant and lower(btrim(t.tenant_id))=@tenant
                  and lower(btrim(t.company_id))=@company
                  and lower(b.fund_structure_node_id::text)=@entity
                  and lower(b.fund_structure_node_kind)='entity' and upper(b.base_currency)=@currency);
            """, connection, transaction);
        AddScope(command, scope);
        if (await command.ExecuteScalarAsync(ct).ConfigureAwait(false) is not true)
            throw new InvalidOperationException("Private-capital scope does not match an owned legal-entity book and its currency.");
    }

    private async Task<IReadOnlyList<PrivateCapitalCommitmentDto>> ReadCommitmentsAsync(
        NpgsqlConnection connection, NpgsqlTransaction? transaction, PrivateCapitalScopeDto scope, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand($"select commitment_id,revision,document::text from {_schema}.private_capital_commitments where {ScopePredicate} order by commitment_id,revision;", connection, transaction);
        AddScope(command, scope);
        var list = new List<PrivateCapitalCommitmentDto>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var item = JsonSerializer.Deserialize<PrivateCapitalCommitmentDto>(reader.GetString(2), JsonOptions)
                ?? throw new InvalidOperationException("Retained commitment authority is unreadable.");
            PrivateCapitalAuthorityRules.ValidateCommitment(item);
            if (item.Scope != scope || item.CommitmentId != reader.GetString(0) || item.Revision != reader.GetInt32(1))
                throw new InvalidOperationException("Retained commitment identity does not match its scope.");
            list.Add(item);
        }
        return list;
    }

    private async Task<IReadOnlyList<PrivateCapitalCallDto>> ReadCallsAsync(
        NpgsqlConnection connection, NpgsqlTransaction? transaction, PrivateCapitalScopeDto scope, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand($"select call_id,document::text from {_schema}.private_capital_calls where {ScopePredicate} order by call_id;", connection, transaction);
        AddScope(command, scope);
        var list = new List<PrivateCapitalCallDto>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var item = JsonSerializer.Deserialize<PrivateCapitalCallDto>(reader.GetString(1), JsonOptions)
                ?? throw new InvalidOperationException("Retained call authority is unreadable.");
            if (item.Scope != scope || item.CallId != reader.GetString(0) || item.PlanHash != PrivateCapitalCallPlanHash.Compute(item))
                throw new InvalidOperationException("Retained call plan identity or hash does not match its scope.");
            list.Add(item);
        }
        return list;
    }

    private async Task<IReadOnlyList<PrivateCapitalPostingReceiptDto>> ReadReceiptsAsync(
        NpgsqlConnection connection, NpgsqlTransaction? transaction, PrivateCapitalScopeDto scope, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand($"""
            select posting_id,commitment_id,call_id,installment_id,kind,amount,journal_entry_id,effective_date,
                funding_receipt_id,bank_line_id,entity_id,currency
            from {_schema}.private_capital_postings where {ScopePredicate} order by effective_date,posting_id;
            """, connection, transaction);
        AddScope(command, scope);
        var list = new List<PrivateCapitalPostingReceiptDto>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            if (reader.GetString(10) != scope.EntityId || reader.GetString(11) != scope.Currency)
                throw new InvalidOperationException("Retained capital receipt has a mismatched entity or currency.");
            list.Add(new(reader.GetString(0), scope, reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.GetString(4), reader.GetDecimal(5), reader.GetGuid(6), reader.GetFieldValue<DateOnly>(7),
                reader.IsDBNull(8) ? null : reader.GetString(8), reader.IsDBNull(9) ? null : reader.GetString(9)));
        }
        return list;
    }

    private static PrivateCapitalCommitmentDto Decorate(PrivateCapitalCommitmentDto value, IReadOnlyList<PrivateCapitalPostingReceiptDto> receipts)
        => value with
        {
            CalledAmount = receipts.Where(x => x.CommitmentId == value.CommitmentId && x.Kind == "Issuance").Sum(x => x.Amount),
            FundedAmount = receipts.Where(x => x.CommitmentId == value.CommitmentId && x.Kind == "Funding").Sum(x => x.Amount)
        };

    private static PrivateCapitalCallDto Decorate(PrivateCapitalCallDto value, IReadOnlyList<PrivateCapitalPostingReceiptDto> receipts)
        => value with
        {
            Lines = value.Lines.Select(line =>
            {
                var items = receipts.Where(x => x.CallId == value.CallId && x.CommitmentId == line.CommitmentId).ToArray();
                var issuance = items.SingleOrDefault(x => x.Kind == "Issuance");
                return line with
                {
                    IssuedAmount = issuance?.Amount ?? 0m,
                    FundedAmount = items.Where(x => x.Kind == "Funding").Sum(x => x.Amount),
                    IssuanceJournalEntryId = issuance?.JournalEntryId
                };
            }).ToArray()
        };

    private static bool SameTerms(PrivateCapitalCommitmentDto left, PrivateCapitalCommitmentDto right)
    {
        static PrivateCapitalCommitmentDto Draft(PrivateCapitalCommitmentDto x) => x with
        {
            ApprovalStatus = PrivateCapitalCommitmentApprovalStatus.Draft,
            ApprovedBy = null,
            ApprovedAtUtc = null,
            CreatedAtUtc = default,
            CalledAmount = 0m,
            FundedAmount = 0m
        };
        return JsonSerializer.Serialize(Draft(left), JsonOptions) == JsonSerializer.Serialize(Draft(right), JsonOptions);
    }

    private async Task<NpgsqlConnection> OpenAsync(CancellationToken ct)
    {
        var connection = new NpgsqlConnection(_options.ConnectionString);
        try
        { await connection.OpenAsync(ct).ConfigureAwait(false); return connection; }
        catch { await connection.DisposeAsync().ConfigureAwait(false); throw; }
    }

    private static async Task LockAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string key, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand("select pg_advisory_xact_lock(hashtextextended(@key,0));", connection, transaction);
        command.Parameters.AddWithValue("key", key);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static void AddScope(NpgsqlCommand command, PrivateCapitalScopeDto scope)
    {
        command.Parameters.AddWithValue("tenant", scope.TenantId);
        command.Parameters.AddWithValue("company", scope.CompanyId);
        command.Parameters.AddWithValue("fund", scope.FundProfileId);
        command.Parameters.AddWithValue("book", scope.LedgerBookId);
        command.Parameters.AddWithValue("entity", scope.EntityId);
        command.Parameters.AddWithValue("currency", scope.Currency);
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private static bool IsConcurrencyFailure(PostgresException exception)
        => exception.SqlState is PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.UniqueViolation
            or PostgresErrorCodes.DeadlockDetected;
}
