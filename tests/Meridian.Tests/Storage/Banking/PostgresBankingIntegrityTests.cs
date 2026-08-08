using FluentAssertions;
using Meridian.Contracts.Banking;
using Meridian.FinancialOperations.Banking;
using Meridian.Storage.Banking;
using Meridian.TestSupport;
using Npgsql;
using Xunit;

namespace Meridian.Tests.Storage.Banking;

[Trait("Category", "Integration")]
public sealed class PostgresBankingIntegrityTests : IAsyncLifetime
{
    private const string ConnectionStringVariable = "MERIDIAN_BANKING_CONNECTION_STRING";
    private PostgresTestServer? _server;

    public async Task InitializeAsync()
    {
        _server = await PostgresTestServer.CreateAsync(ConnectionStringVariable).ConfigureAwait(false);
    }

    public async Task DisposeAsync()
    {
        if (_server is not null)
        {
            await _server.DisposeAsync().ConfigureAwait(false);
        }
    }

    [BankingDatabaseFact]
    public async Task Migration003_ShouldPreserveGenericRowsAndLeaveLegacyPaymentCurrencyUnresolved()
    {
        var options = CreateOptions("banking_legacy");
        var legacyPaymentId = Guid.NewGuid();
        var legacyPendingPaymentId = Guid.NewGuid();
        var legacyRejectPaymentId = Guid.NewGuid();
        var entityId = Guid.NewGuid();
        var genericTransactionId = Guid.NewGuid();

        await using (var connection = new NpgsqlConnection(options.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                CREATE SCHEMA {options.Schema};
                CREATE TABLE {options.Schema}.pending_payments (
                    pending_payment_id UUID NOT NULL PRIMARY KEY,
                    entity_id UUID NOT NULL,
                    amount NUMERIC(19,4) NOT NULL,
                    effective_date DATE NOT NULL,
                    external_ref TEXT,
                    notes TEXT,
                    status SMALLINT NOT NULL DEFAULT 0,
                    reviewed_by TEXT,
                    review_notes TEXT,
                    initiated_at TIMESTAMPTZ NOT NULL,
                    reviewed_at TIMESTAMPTZ);
                CREATE TABLE {options.Schema}.bank_transactions (
                    bank_transaction_id UUID NOT NULL PRIMARY KEY,
                    entity_id UUID NOT NULL,
                    transaction_type TEXT NOT NULL,
                    effective_date DATE NOT NULL,
                    transaction_date DATE NOT NULL,
                    settlement_date DATE NOT NULL,
                    amount NUMERIC(19,4) NOT NULL,
                    currency TEXT NOT NULL DEFAULT 'USD',
                    external_ref TEXT,
                    recorded_at TIMESTAMPTZ NOT NULL,
                    is_voided BOOLEAN NOT NULL DEFAULT FALSE);
                INSERT INTO {options.Schema}.pending_payments
                    (pending_payment_id, entity_id, amount, effective_date, status, initiated_at)
                VALUES (@payment_id, @entity_id, 125.00, DATE '2026-02-01', 1, now());
                INSERT INTO {options.Schema}.pending_payments
                    (pending_payment_id, entity_id, amount, effective_date, status, initiated_at)
                VALUES (@pending_payment_id, @entity_id, 250.00, DATE '2026-02-02', 0, now());
                INSERT INTO {options.Schema}.pending_payments
                    (pending_payment_id, entity_id, amount, effective_date, status, initiated_at)
                VALUES (@reject_payment_id, @entity_id, 275.00, DATE '2026-02-03', 0, now());
                INSERT INTO {options.Schema}.bank_transactions
                    (bank_transaction_id, entity_id, transaction_type, effective_date,
                     transaction_date, settlement_date, amount, currency, recorded_at, is_voided)
                VALUES (@transaction_id, @entity_id, 'InterestPayment', DATE '2026-02-01',
                        DATE '2026-02-01', DATE '2026-02-03', 125.00, 'USD', now(), false);
                """;
            command.Parameters.AddWithValue("payment_id", legacyPaymentId);
            command.Parameters.AddWithValue("pending_payment_id", legacyPendingPaymentId);
            command.Parameters.AddWithValue("reject_payment_id", legacyRejectPaymentId);
            command.Parameters.AddWithValue("entity_id", entityId);
            command.Parameters.AddWithValue("transaction_id", genericTransactionId);
            await command.ExecuteNonQueryAsync();
        }

        await new BankingMigrationRunner(options).EnsureMigratedAsync();
        await new BankingMigrationRunner(options).EnsureMigratedAsync();
        var store = new PostgresBankingStore(options);
        var legacyPayment = await store.GetPendingPaymentAsync(legacyPaymentId);
        var genericTransaction = (await store.GetBankTransactionsAsync(entityId)).Single();

        legacyPayment.Should().NotBeNull();
        legacyPayment!.Currency.Should().BeNull("migration 003 must not invent historical USD intent currency");
        genericTransaction.BankTransactionId.Should().Be(genericTransactionId);
        genericTransaction.PendingPaymentId.Should().BeNull();
        genericTransaction.EvidenceId.Should().BeNull();
        genericTransaction.CanonicalInputHash.Should().BeNull();

        await using (var connection = new NpgsqlConnection(options.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                SELECT count(*)
                FROM {options.Schema}.schema_migrations
                WHERE filename = '003_payment_intent_integrity.sql';
                """;
            ((long)(await command.ExecuteScalarAsync() ?? 0L)).Should().Be(1);
        }

        var service = new PostgresBankingService(store);
        var approveLegacy = () => service.ApprovePaymentAsync(
            legacyPendingPaymentId,
            new ApprovePaymentRequest("should remain pending", "reviewer"));
        var approvalException = await Assert.ThrowsAsync<BankingException>(approveLegacy);
        approvalException.Message.Should().Contain("Remediate this legacy intent before approval");
        (await store.GetPendingPaymentAsync(legacyPendingPaymentId))!.Status
            .Should().Be(PaymentApprovalStatus.Pending);

        var remediated = await service.RemediatePaymentCurrencyAsync(
            legacyPendingPaymentId,
            new RemediatePaymentCurrencyRequest(
                " eur ",
                "Recovered from signed source instruction",
                "currency-operator"));
        remediated.Should().NotBeNull();
        remediated!.Currency.Should().Be("EUR");
        remediated.CurrencyRemediatedBy.Should().Be("currency-operator");
        remediated.CurrencyRemediationReason.Should().Be("Recovered from signed source instruction");
        remediated.CurrencyRemediatedAt.Should().NotBeNull();

        var duplicateRemediation = () => service.RemediatePaymentCurrencyAsync(
            legacyPendingPaymentId,
            new RemediatePaymentCurrencyRequest("USD", "Attempted replacement", "other-operator"));
        await Assert.ThrowsAsync<BankingConflictException>(duplicateRemediation);
        var approvedLegacy = await service.ApprovePaymentAsync(
            legacyPendingPaymentId,
            new ApprovePaymentRequest("currency repaired", "reviewer"));
        approvedLegacy!.Status.Should().Be(PaymentApprovalStatus.Approved);
        approvedLegacy.Currency.Should().Be("EUR");
        approvedLegacy.CurrencyRemediatedBy.Should().Be("currency-operator");

        var rejectedLegacy = await service.RejectPaymentAsync(
            legacyRejectPaymentId,
            new RejectPaymentRequest("currency unresolved", "reviewer"));
        rejectedLegacy!.Status.Should().Be(PaymentApprovalStatus.Rejected,
            "rejection must remain available to close an unsafe legacy intent");

        var act = () => service.RecordPaymentBankEvidenceAsync(
            legacyPaymentId,
            new RecordPaymentBankEvidenceRequest(
                "BankConfirmation",
                EvidenceId: "legacy-confirmation"));

        var exception = await Assert.ThrowsAsync<BankingException>(act);
        exception.Message.Should().Contain("Remediate this legacy intent");
        (await store.GetBankTransactionsAsync(entityId)).Should().ContainSingle();
    }

    [BankingDatabaseFact]
    public async Task ApproveAndRejectConcurrentBarrier_ShouldPersistOneTerminalDecision()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var (options, service) = await CreateServiceAsync("banking_cas", cts.Token);
        var pending = await service.InitiatePaymentAsync(
            Guid.NewGuid(),
            new InitiatePaymentRequest(1_000m, new DateOnly(2026, 2, 1), null, null, "USD"),
            cts.Token);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<(PendingPaymentDto? Payment, Exception? Error)> CaptureAsync(
            Func<Task<PendingPaymentDto?>> transition)
        {
            await start.Task.WaitAsync(cts.Token);
            try
            {
                return (await transition(), null);
            }
            catch (Exception exception)
            {
                return (null, exception);
            }
        }

        var approve = CaptureAsync(() => service.ApprovePaymentAsync(
            pending.PendingPaymentId,
            new ApprovePaymentRequest("approve", "approver"),
            cts.Token));
        var reject = CaptureAsync(() => service.RejectPaymentAsync(
            pending.PendingPaymentId,
            new RejectPaymentRequest("reject", "rejector"),
            cts.Token));
        start.SetResult();
        var outcomes = await Task.WhenAll(approve, reject);

        outcomes.Should().ContainSingle(outcome => outcome.Payment != null);
        outcomes.Should().ContainSingle(outcome => outcome.Error is BankingConflictException);
        var restarted = new PostgresBankingService(new PostgresBankingStore(options));
        var retained = await restarted.GetPaymentAsync(pending.PendingPaymentId, cts.Token);
        retained!.Status.Should().Be(outcomes.Single(outcome => outcome.Payment is not null).Payment!.Status);
    }

    [BankingDatabaseFact]
    public async Task PendingPaymentInsert_ShouldBeIdempotentButNeverRewriteIntentOrTerminalDecision()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var (options, service) = await CreateServiceAsync("banking_immutable_intent", cts.Token);
        var pending = await service.InitiatePaymentAsync(
            Guid.NewGuid(),
            new InitiatePaymentRequest(1_250m, new DateOnly(2026, 2, 7), "immutable", null, "USD"),
            cts.Token);
        var store = new PostgresBankingStore(options);

        await store.UpsertPendingPaymentAsync(pending, cts.Token);

        var changedEconomics = () => store.UpsertPendingPaymentAsync(
            pending with { Amount = 1_251m },
            cts.Token);
        await Assert.ThrowsAsync<InvalidOperationException>(changedEconomics);
        (await store.GetPendingPaymentAsync(pending.PendingPaymentId, cts.Token))!
            .Amount.Should().Be(1_250m);

        var approved = await service.ApprovePaymentAsync(
            pending.PendingPaymentId,
            new ApprovePaymentRequest("approved once", "reviewer"),
            cts.Token);
        approved.Should().NotBeNull();

        var resetDecision = () => store.UpsertPendingPaymentAsync(pending, cts.Token);
        await Assert.ThrowsAsync<InvalidOperationException>(resetDecision);
        var retained = await store.GetPendingPaymentAsync(pending.PendingPaymentId, cts.Token);
        retained!.Status.Should().Be(PaymentApprovalStatus.Approved);
        retained.ReviewNotes.Should().Be("approved once");
        retained.ReviewedBy.Should().Be("reviewer");
    }

    [BankingDatabaseFact]
    public async Task RecordEvidence_ConcurrentReplayConflictAndRestart_ShouldRetainOneCanonicalResult()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var (options, service) = await CreateServiceAsync("banking_replay", cts.Token);
        var entityId = Guid.NewGuid();
        var pending = await service.InitiatePaymentAsync(
            entityId,
            new InitiatePaymentRequest(2_500m, new DateOnly(2026, 2, 2), "payment-2500", null, "gbp"),
            cts.Token);
        pending.Currency.Should().Be("GBP");
        (await new PostgresBankingStore(options).GetPendingPaymentAsync(pending.PendingPaymentId, cts.Token))!
            .Currency.Should().Be("GBP");
        await service.ApprovePaymentAsync(
            pending.PendingPaymentId,
            new ApprovePaymentRequest("approved", "reviewer"),
            cts.Token);
        var request = new RecordPaymentBankEvidenceRequest(
            "BankConfirmation",
            TransactionDate: new DateOnly(2026, 2, 3),
            SettlementDate: new DateOnly(2026, 2, 4),
            Amount: 2_500m,
            Currency: "GBP",
            ExternalRef: "bank-2500",
            RecordedBy: "cash-ops",
            EvidenceId: "bank-event-2500");
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writes = Enumerable.Range(0, 20)
            .Select(async _ =>
            {
                await start.Task.WaitAsync(cts.Token);
                return await service.RecordPaymentBankEvidenceAsync(
                    pending.PendingPaymentId,
                    request,
                    cts.Token);
            })
            .ToArray();
        start.SetResult();
        var retained = await Task.WhenAll(writes);

        retained.Should().OnlyContain(transaction =>
            transaction != null &&
            transaction.BankTransactionId == retained[0]!.BankTransactionId);
        (await service.GetBankTransactionsAsync(entityId, cts.Token)).Should().ContainSingle();

        var restarted = new PostgresBankingService(new PostgresBankingStore(options));
        var replay = await restarted.RecordPaymentBankEvidenceAsync(
            pending.PendingPaymentId,
            request,
            cts.Token);
        replay!.BankTransactionId.Should().Be(retained[0]!.BankTransactionId);
        replay.CanonicalInputHash.Should().Be(retained[0]!.CanonicalInputHash);

        var conflict = () => restarted.RecordPaymentBankEvidenceAsync(
            pending.PendingPaymentId,
            request with { ExternalRef = "different-bank-reference" },
            cts.Token);
        await Assert.ThrowsAsync<BankingConflictException>(conflict);
        (await restarted.GetBankTransactionsAsync(entityId, cts.Token)).Should().ContainSingle();
    }

    [BankingDatabaseFact]
    public async Task RecordEvidence_ShouldReverifyEntityAmountAndCurrencyInsideStoreTransaction()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var (options, service) = await CreateServiceAsync("banking_binding", cts.Token);
        var entityId = Guid.NewGuid();
        var pending = await service.InitiatePaymentAsync(
            entityId,
            new InitiatePaymentRequest(600m, new DateOnly(2026, 2, 5), null, null, "EUR"),
            cts.Token);
        await service.ApprovePaymentAsync(
            pending.PendingPaymentId,
            new ApprovePaymentRequest(null, "reviewer"),
            cts.Token);
        var store = new PostgresBankingStore(options);

        BankTransactionDto Candidate(Guid candidateEntityId, decimal amount, string currency, string evidenceId)
            => new(
                Guid.NewGuid(),
                candidateEntityId,
                "BankConfirmation",
                pending.EffectiveDate,
                pending.EffectiveDate,
                pending.EffectiveDate,
                amount,
                currency,
                null,
                DateTimeOffset.UtcNow,
                false,
                "cash-ops",
                pending.PendingPaymentId,
                evidenceId,
                new string('A', 64));

        var wrongEntity = await store.RecordPaymentBankEvidenceAsync(
            Candidate(Guid.NewGuid(), 600m, "EUR", "wrong-entity"),
            cts.Token);
        var wrongAmount = await store.RecordPaymentBankEvidenceAsync(
            Candidate(entityId, 601m, "EUR", "wrong-amount"),
            cts.Token);
        var wrongCurrency = await store.RecordPaymentBankEvidenceAsync(
            Candidate(entityId, 600m, "USD", "wrong-currency"),
            cts.Token);

        wrongEntity.Status.Should().Be(PaymentBankEvidenceWriteStatus.PaymentBindingConflict);
        wrongAmount.Status.Should().Be(PaymentBankEvidenceWriteStatus.PaymentBindingConflict);
        wrongCurrency.Status.Should().Be(PaymentBankEvidenceWriteStatus.PaymentBindingConflict);
        (await store.GetBankTransactionsAsync(entityId, cts.Token)).Should().BeEmpty();
    }

    [BankingDatabaseFact]
    public async Task RecordEvidence_CancellationWhileInsertIsBlocked_ShouldRollbackWithoutEvidence()
    {
        using var overallCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var (options, service) = await CreateServiceAsync("banking_cancel", overallCts.Token);
        var entityId = Guid.NewGuid();
        var pending = await service.InitiatePaymentAsync(
            entityId,
            new InitiatePaymentRequest(700m, new DateOnly(2026, 2, 6), null, null, "USD"),
            overallCts.Token);
        await service.ApprovePaymentAsync(
            pending.PendingPaymentId,
            new ApprovePaymentRequest(null, "reviewer"),
            overallCts.Token);

        await using var blockerConnection = new NpgsqlConnection(options.ConnectionString);
        await blockerConnection.OpenAsync(overallCts.Token);
        await using var blockerTransaction = await blockerConnection.BeginTransactionAsync(overallCts.Token);
        await using (var blockerCommand = blockerConnection.CreateCommand())
        {
            blockerCommand.Transaction = blockerTransaction;
            blockerCommand.CommandText = $"LOCK TABLE {options.Schema}.bank_transactions IN ACCESS EXCLUSIVE MODE;";
            await blockerCommand.ExecuteNonQueryAsync(overallCts.Token);
        }

        using var writeCts = CancellationTokenSource.CreateLinkedTokenSource(overallCts.Token);
        var write = service.RecordPaymentBankEvidenceAsync(
            pending.PendingPaymentId,
            new RecordPaymentBankEvidenceRequest(
                "BankConfirmation",
                EvidenceId: "cancelled-bank-event"),
            writeCts.Token);
        await WaitForBlockedInsertAsync(options, overallCts.Token);
        await writeCts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => write);
        await blockerTransaction.RollbackAsync(overallCts.Token);
        (await service.GetBankTransactionsAsync(entityId, overallCts.Token)).Should().BeEmpty();
    }

    private BankingStoreOptions CreateOptions(string prefix)
    {
        _server.Should().NotBeNull();
        return new BankingStoreOptions
        {
            ConnectionString = _server!.ConnectionString,
            Schema = _server.CreateSchemaName(prefix)
        };
    }

    private async Task<(BankingStoreOptions Options, PostgresBankingService Service)> CreateServiceAsync(
        string prefix,
        CancellationToken ct)
    {
        var options = CreateOptions(prefix);
        await new BankingMigrationRunner(options).EnsureMigratedAsync(ct);
        return (options, new PostgresBankingService(new PostgresBankingStore(options)));
    }

    private static async Task WaitForBlockedInsertAsync(
        BankingStoreOptions options,
        CancellationToken ct)
    {
        await using var observer = new NpgsqlConnection(options.ConnectionString);
        await observer.OpenAsync(ct);
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            await using var command = observer.CreateCommand();
            command.CommandText =
                """
                SELECT EXISTS (
                    SELECT 1
                    FROM pg_stat_activity
                    WHERE pid <> pg_backend_pid()
                      AND wait_event_type = 'Lock'
                      AND query ILIKE @query_pattern);
                """;
            command.Parameters.AddWithValue("query_pattern", $"%INSERT INTO {options.Schema}.bank_transactions%");
            if (await command.ExecuteScalarAsync(ct) is true)
            {
                return;
            }

            await Task.Yield();
        }
    }
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class BankingDatabaseFactAttribute : FactAttribute
{
    private const string DisableDockerVariable = "MERIDIAN_DISABLE_DOCKER_TESTS";
    private const string ConnectionStringVariable = "MERIDIAN_BANKING_CONNECTION_STRING";

    public BankingDatabaseFactAttribute()
    {
        if (string.Equals(
                Environment.GetEnvironmentVariable(DisableDockerVariable),
                "true",
                StringComparison.OrdinalIgnoreCase))
        {
            Skip = $"Banking PostgreSQL tests are skipped because {DisableDockerVariable}=true.";
            return;
        }

        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionStringVariable)))
        {
            return;
        }

        if (!IsDockerAvailable())
        {
            Skip = "Banking PostgreSQL tests are skipped because Docker is unavailable. " +
                   $"Start Docker or set {ConnectionStringVariable} to an external PostgreSQL instance.";
        }
    }

    private static bool IsDockerAvailable()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                using var pipe = new System.IO.Pipes.NamedPipeClientStream(
                    ".",
                    "docker_engine",
                    System.IO.Pipes.PipeDirection.InOut,
                    System.IO.Pipes.PipeOptions.Asynchronous);
                pipe.Connect(250);
                return pipe.IsConnected;
            }

            return File.Exists("/var/run/docker.sock");
        }
        catch
        {
            return false;
        }
    }
}
