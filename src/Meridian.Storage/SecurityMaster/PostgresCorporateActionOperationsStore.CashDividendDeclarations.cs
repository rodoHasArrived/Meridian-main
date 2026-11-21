using System.Data;
using System.Text.Json;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Npgsql;

namespace Meridian.Storage.SecurityMaster;

public sealed partial class PostgresCorporateActionOperationsStore : ICorporateActionCashDividendAccountingStore
{
    private const string ChangeCashDividendDeclarationOperation = "ChangeCashDividendDeclaration";
    private const string AttestCashDividendOperation = "AttestCashDividendAccounting";
    private const string PostAttestedCashDividendOperation = "PostAttestedCashDividendAccounting";

    public Task<CashDividendAttestationDeclarationResultDto?> GetCashDividendDeclarationReceiptAsync(
        ChangeCashDividendAttestationDeclarationRequestDto request, CancellationToken ct = default) =>
        ExecutePersistenceReadAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(request);
            RequireCashDividendScope(request.TenantId, request.CompanyId);
            if (request.Authority is not { CanAdministerDeclaration: true, Origin: OperationsActionOriginDto.HumanOperator })
                throw new CorporateActionPermissionDeniedException("CashDividendDeclaration", "CanAdministerDeclaration");
            await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
            var replay = await ReadReceiptAsync<CashDividendAttestationDeclarationResultDto>(connection, null,
                ChangeCashDividendDeclarationOperation, CashDividendScopeAggregate(request.TenantId, request.CompanyId),
                request.CommandId.ToString("D"), CashDividendDeclarationFingerprint(request), ct).ConfigureAwait(false);
            return replay is null ? null : replay with { Replayed = true };
        }, "cash-dividend declaration receipt");

    public async Task<CashDividendAttestationDeclarationDto?> GetCashDividendDeclarationAsync(
        string tenantId, string companyId, CancellationToken ct = default)
        => await ExecutePersistenceReadAsync(async () =>
    {
        RequireCashDividendScope(tenantId, companyId);
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        return await LoadCashDividendDeclarationAsync(connection, null, tenantId, companyId, false, ct).ConfigureAwait(false);
    }, "cash-dividend declaration read").ConfigureAwait(false);

    public async Task<CashDividendAttestationDeclarationStateDto> GetCashDividendDeclarationStateAsync(
        string tenantId, string companyId, CancellationToken ct = default)
        => await ExecutePersistenceReadAsync(async () =>
    {
        RequireCashDividendScope(tenantId, companyId);
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct).ConfigureAwait(false);
        var declaration = await LoadCashDividendDeclarationAsync(connection, transaction, tenantId, companyId, false, ct).ConfigureAwait(false);
        var history = new List<CashDividendAttestationDeclarationDto>();
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = $"select payload::text, payload_hash from {Qualified("cash_dividend_attestation_declaration_history")} where tenant_id=@tenant and company_id=@company order by revision";
            command.Parameters.AddWithValue("tenant", tenantId);
            command.Parameters.AddWithValue("company", companyId);
            await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                var item = ReadCashDividendPayload<CashDividendAttestationDeclarationDto>(reader.GetString(0), reader.GetString(1));
                if (item.TenantId != tenantId || item.CompanyId != companyId || item.Revision != history.Count + 1)
                    throw CashDividendUnavailable("Economic declaration history failed scope or revision validation.");
                history.Add(item);
            }
        }
        if ((declaration?.Revision ?? 0) != history.Count || declaration is not null && history[^1] != declaration)
            throw CashDividendUnavailable("Economic declaration history does not match its current authority.");
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return new CashDividendAttestationDeclarationStateDto(tenantId, companyId, _options.EnableSinglePrincipalCashDividendAttestations,
            CorporateActionCashDividendAttestationPolicy.Version, CorporateActionCashDividendAttestationPolicy.PolicyHash,
            declaration, history);
    }, "cash-dividend declaration history read").ConfigureAwait(false);

    public Task<CashDividendAttestationDeclarationResultDto> ChangeCashDividendDeclarationAsync(
        ChangeCashDividendAttestationDeclarationRequestDto request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireCashDividendScope(request.TenantId, request.CompanyId);
        if (request.CommandId == Guid.Empty || request.LedgerBookId == Guid.Empty
            || string.IsNullOrWhiteSpace(request.Actor) || string.IsNullOrWhiteSpace(request.Reason)
            || request.Reason.Length > 4000)
            throw new CorporateActionValidationException("Economic declaration identity, selected book, actor and reason are required.");
        if (request.Authority is not { CanAdministerDeclaration: true, Origin: OperationsActionOriginDto.HumanOperator })
            throw new CorporateActionPermissionDeniedException("CashDividendDeclaration", "CanAdministerDeclaration");
        return ExecuteCaseMutationWithRetryAsync(CashDividendScopeAggregate(request.TenantId, request.CompanyId),
            "cash-dividend declaration", retryCt => ChangeCashDividendDeclarationOnceAsync(request, retryCt), ct);
    }

    private async Task<CashDividendAttestationDeclarationResultDto> ChangeCashDividendDeclarationOnceAsync(
        ChangeCashDividendAttestationDeclarationRequestDto request, CancellationToken ct)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct).ConfigureAwait(false);
        await AcquireCashDividendScopeLockAsync(connection, transaction, request.TenantId, request.CompanyId, ct).ConfigureAwait(false);
        var aggregate = CashDividendScopeAggregate(request.TenantId, request.CompanyId);
        var fingerprint = CashDividendDeclarationFingerprint(request);
        var key = request.CommandId.ToString("D");
        var replay = await ReadReceiptAsync<CashDividendAttestationDeclarationResultDto>(connection, transaction,
            ChangeCashDividendDeclarationOperation, aggregate, key, fingerprint, ct).ConfigureAwait(false);
        if (replay is not null)
        {
            await transaction.CommitAsync(ct).ConfigureAwait(false);
            return replay with { Replayed = true };
        }
        var current = await LoadCashDividendDeclarationAsync(connection, transaction, request.TenantId, request.CompanyId, true, ct).ConfigureAwait(false);
        if (request.ExpectedRevision != (current?.Revision ?? 0))
            throw new CorporateActionVersionConflictException(aggregate, request.ExpectedRevision, current?.Revision ?? 0);
        if (request.Enabled && !_options.EnableSinglePrincipalCashDividendAttestations)
            throw new CorporateActionOperationException("cash_dividend_policy_disabled", "Economic preparer attestation is disabled on this host.");
        if (request.PolicyVersion != CorporateActionCashDividendAttestationPolicy.Version
            || !Sha256Digest.FixedEquals(request.PolicyHash, CorporateActionCashDividendAttestationPolicy.PolicyHash))
            throw new CorporateActionValidationException("The confirmed economic policy version or hash is unsupported.");
        if (current is not null && current.LedgerBookId != request.LedgerBookId && (current.Enabled || !request.Enabled))
            throw new CorporateActionValidationException("Withdraw the current selected book before enabling a different book; withdrawal cannot change the selected book.");
        var binding = request.AccountRoleBinding ?? (!request.Enabled ? current?.AccountRoleBinding : null);
        if (request.Enabled && (binding is not { SchemaVersion: 1 }
            || binding.LedgerBookId != request.LedgerBookId
            || string.IsNullOrWhiteSpace(binding.DividendReceivableAccount)
            || string.IsNullOrWhiteSpace(binding.DividendIncomeAccount)
            || string.IsNullOrWhiteSpace(binding.DividendReceivableAccountName)
            || string.IsNullOrWhiteSpace(binding.DividendIncomeAccountName)
            || binding.DividendReceivableAccount == binding.DividendIncomeAccount
            || string.IsNullOrWhiteSpace(binding.RulePackId) || string.IsNullOrWhiteSpace(binding.RulePackVersion)
            || string.IsNullOrWhiteSpace(binding.SelectedRuleId) || string.IsNullOrWhiteSpace(binding.SelectedRuleVersion)))
            throw new CorporateActionValidationException("Enabling requires the selected book's explicit existing dividend receivable/income accounts and exact promoted accounting rule.");
        if (binding != current?.AccountRoleBinding && request.Authority?.CanBindAccountingRoles != true)
            throw new CorporateActionPermissionDeniedException("CashDividendAccountRoles", "CanBindAccountingRoles");
        var declaration = new CashDividendAttestationDeclarationDto(current?.DeclarationId ?? Guid.NewGuid(),
            checked((current?.Revision ?? 0) + 1), request.TenantId, request.CompanyId, request.LedgerBookId,
            request.Enabled, request.Actor.Trim(), request.PolicyVersion, request.PolicyHash,
            request.Actor.Trim(), DateTimeOffset.UtcNow, request.Reason, binding);
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = $"""
                insert into {Qualified("cash_dividend_attestation_declarations")}
                    (tenant_id,company_id,declaration_id,revision,ledger_book_id,enabled,principal_id,payload,payload_hash)
                values (@tenant,@company,@id,@revision,@book,@enabled,@principal,@payload,@hash)
                on conflict (tenant_id,company_id) do update set declaration_id=excluded.declaration_id,
                    revision=excluded.revision,ledger_book_id=excluded.ledger_book_id,enabled=excluded.enabled,
                    principal_id=excluded.principal_id,payload=excluded.payload,payload_hash=excluded.payload_hash;
                insert into {Qualified("cash_dividend_attestation_declaration_history")}
                    (tenant_id,company_id,declaration_id,revision,ledger_book_id,recorded_at,payload,payload_hash)
                values (@tenant,@company,@id,@revision,@book,@recorded,@payload,@hash);
                """;
            command.Parameters.AddWithValue("tenant", declaration.TenantId);
            command.Parameters.AddWithValue("company", declaration.CompanyId);
            command.Parameters.AddWithValue("id", declaration.DeclarationId);
            command.Parameters.AddWithValue("revision", declaration.Revision);
            command.Parameters.AddWithValue("book", declaration.LedgerBookId);
            command.Parameters.AddWithValue("enabled", declaration.Enabled);
            command.Parameters.AddWithValue("principal", declaration.PrincipalId);
            command.Parameters.AddWithValue("recorded", declaration.RecordedAtUtc.UtcDateTime);
            AddCashDividendPayload(command, declaration);
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        var result = new CashDividendAttestationDeclarationResultDto(declaration, false);
        await WriteReceiptAsync(connection, transaction, ChangeCashDividendDeclarationOperation, aggregate, key,
            fingerprint, result, declaration.RecordedAtUtc, ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return result;
    }

    private async Task<CashDividendAttestationDeclarationDto?> LoadCashDividendDeclarationAsync(NpgsqlConnection connection,
        NpgsqlTransaction? transaction, string tenantId, string companyId, bool forUpdate, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"select payload::text,payload_hash,declaration_id,revision,ledger_book_id,enabled,principal_id from {Qualified("cash_dividend_attestation_declarations")} where tenant_id=@tenant and company_id=@company" + (forUpdate ? " for update" : "");
        command.Parameters.AddWithValue("tenant", tenantId);
        command.Parameters.AddWithValue("company", companyId);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
            return null;
        var declaration = ReadCashDividendPayload<CashDividendAttestationDeclarationDto>(reader.GetString(0), reader.GetString(1));
        if (declaration.TenantId != tenantId || declaration.CompanyId != companyId
            || declaration.DeclarationId != reader.GetGuid(2) || declaration.Revision != reader.GetInt64(3)
            || declaration.LedgerBookId != reader.GetGuid(4) || declaration.Enabled != reader.GetBoolean(5)
            || declaration.PrincipalId != reader.GetString(6)
            || declaration.PolicyVersion != CorporateActionCashDividendAttestationPolicy.Version
            || declaration.PolicyHash != CorporateActionCashDividendAttestationPolicy.PolicyHash)
            throw CashDividendUnavailable("The economic declaration failed its retained authority binding.");
        return declaration;
    }

    private static void RequireCashDividendScope(string tenantId, string companyId)
    {
        if (string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(companyId)
            || tenantId.Length > 256 || companyId.Length > 256)
            throw new CorporateActionValidationException("A bound tenant and company are required for economic attestation.");
    }

    private static Guid CashDividendScopeAggregate(string tenantId, string companyId) =>
        new(Convert.FromHexString(Sha256Digest.ComputeUtf8(JsonSerializer.Serialize(new { tenantId, companyId }, JsonOptions)))[..16]);

    private static string CashDividendDeclarationFingerprint(ChangeCashDividendAttestationDeclarationRequestDto request) =>
        Sha256Digest.ComputeUtf8(JsonSerializer.Serialize(request with { Authority = null }, JsonOptions));

    private static Task AcquireCashDividendScopeLockAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        string tenantId, string companyId, CancellationToken ct) =>
        AcquireTransactionLockAsync(connection, transaction, $"cash-dividend-attestation-scope:{JsonSerializer.Serialize(new[] { tenantId.Trim().ToUpperInvariant(), companyId.Trim().ToUpperInvariant() }, JsonOptions)}", ct);

    private async Task AcquireCashDividendSourceChainAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        Guid caseId, string tenantId, string companyId, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"select security_id from {Qualified("corporate_action_processing_cases")} where case_id=@case and tenant_id=@tenant and company_id=@company";
        command.Parameters.AddWithValue("case", caseId);
        command.Parameters.AddWithValue("tenant", tenantId);
        command.Parameters.AddWithValue("company", companyId);
        if (await command.ExecuteScalarAsync(ct).ConfigureAwait(false) is not Guid securityId)
            throw new CorporateActionStateConflictException(caseId, "The scoped dividend case is unavailable.");
        // Freeze current security class/status/currency/version before taking source-chain
        // authority. FOR SHARE is compatible with the FK key-share locks of canonical inserts.
        command.CommandText = $"select security_id from {Qualified("securities")} where security_id=@security for share";
        command.Parameters.Clear();
        command.Parameters.AddWithValue("security", securityId);
        if (await command.ExecuteScalarAsync(ct).ConfigureAwait(false) is null)
            throw new CorporateActionValidationException("The authoritative dividend security is unavailable.");
        // Match canonical source writers: security-chain authority precedes case locks.
        await PostgresCorporateActionCanonicalStore.AcquireSecurityChainLockAsync(connection, transaction, securityId, ct).ConfigureAwait(false);
    }

    private async Task<Guid> ReadCashDividendSelectedBookAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        Guid caseId, string tenantId, string companyId, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"select ledger_book_id from {Qualified("corporate_action_processing_cases")} where case_id=@case and tenant_id=@tenant and company_id=@company";
        command.Parameters.AddWithValue("case", caseId);
        command.Parameters.AddWithValue("tenant", tenantId);
        command.Parameters.AddWithValue("company", companyId);
        if (await command.ExecuteScalarAsync(ct).ConfigureAwait(false) is not string book || !Guid.TryParse(book, out var bookId) || bookId == Guid.Empty)
            throw new CorporateActionValidationException("The scoped dividend case has no exact selected ledger book.");
        return bookId;
    }

    private async Task EnsureCurrentCashDividendSourceAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        CorporateActionProcessingCaseDto processingCase, CancellationToken ct)
    {
        var action = processingCase.SourceSnapshot?.ProposedAction
            ?? throw new CorporateActionValidationException("The retained dividend source is unavailable.");
        var canonical = await PostgresCorporateActionCanonicalStore.LoadOrReconcileByEconomicIdentityAsync(connection,
            transaction, Qualified("corporate_actions"), processingCase.SecurityId,
            CorporateActionEconomicFingerprint.Compute(action), action.LifecycleState, action.SupersedesCorpActId, ct).ConfigureAwait(false);
        if (canonical?.CorpActId != processingCase.CorporateActionId)
            throw new CorporateActionValidationException("The retained regular dividend is no longer the exact current canonical source.");
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            select count(*) filter (where ex_date=@date and event_type in (@regular,@special)),
                   count(*) filter (where supersedes_corp_act_id=@action)
            from {Qualified("corporate_actions")} where security_id=@security
            """;
        command.Parameters.AddWithValue("date", action.ExDate);
        command.Parameters.AddWithValue("regular", CorporateActionEventTypes.Dividend);
        command.Parameters.AddWithValue("special", CorporateActionEventTypes.SpecialDividend);
        command.Parameters.AddWithValue("action", processingCase.CorporateActionId);
        command.Parameters.AddWithValue("security", processingCase.SecurityId);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        await reader.ReadAsync(ct).ConfigureAwait(false);
        if (reader.GetInt64(0) != 1 || reader.GetInt64(1) != 0)
            throw new CorporateActionValidationException("The retained dividend has an ambiguous related source or a canonical correction/supersession.");
    }

    private static void AddCashDividendPayload<T>(NpgsqlCommand command, T payload)
    {
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        command.Parameters.Add(new NpgsqlParameter("payload", NpgsqlTypes.NpgsqlDbType.Jsonb) { Value = json });
        command.Parameters.AddWithValue("hash", Sha256Digest.ComputeUtf8(json));
    }

    private static T ReadCashDividendPayload<T>(string json, string hash) where T : class
    {
        try
        {
            var value = JsonSerializer.Deserialize<T>(json, JsonOptions);
            if (value is null || !Sha256Digest.FixedEquals(hash, Sha256Digest.ComputeUtf8(JsonSerializer.Serialize(value, JsonOptions))))
                throw CashDividendUnavailable("Retained economic attestation evidence failed its digest binding.");
            return value;
        }
        catch (JsonException)
        {
            throw CashDividendUnavailable("Retained economic attestation evidence is unreadable.");
        }
    }

    private static CorporateActionPersistenceUnavailableException CashDividendUnavailable(string message) => new(message);
}
