using FluentAssertions;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Tenancy;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.OperationsContinuity;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Npgsql;

namespace Meridian.Tests.Storage;

[Trait("Category", "Integration")]
public sealed class StrictTenantMutationPostgresTests
{
    [LedgerDatabaseFact]
    public async Task WashSaleResolver_SameLotIdAcrossAccounts_ExcludesOnlyDisposingAccount()
    {
        // A LedgerBook-scoped repurchase in a sibling brokerage account is a distinct lot,
        // even when both brokers assigned the same identifier to their acquisitions.
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var ct = timeout.Token;
        await using var database = await LedgerPostgresTestDatabase.CreateAsync(ct);
        var registry = new PostgresFundProfileTenancyRegistry(database.Options);
        await registry.BindAsync("fund-alpha", "tenant-alpha", ct: ct);
        var now = DateTimeOffset.Parse("2026-05-15T12:00:00Z");
        var saleDate = new DateOnly(2026, 5, 15);
        var book = new LedgerBookRecord(Guid.NewGuid(), "fund-alpha", Guid.NewGuid(),
            FundStructureNodeKindDto.Fund, "Alpha", "USD", now, now);
        await database.JournalStore.SaveLedgerBookAsync(book, ct);
        var account = LedgerAccounts.Securities("AAPL", "broker-1");
        var sibling = LedgerAccounts.Securities("AAPL", "broker-2");
        var securityId = Guid.NewGuid();
        var strict = new PostgresLedgerJournalStore(database.Options, new WorkerAccessor(), TenantScopeEnforcementOptions.FailClosed);
        using var authority = FundScopeTenantAuthority.Enter("tenant-alpha", "wash-sale lot identity regression");
        foreach (var lotAccount in new[] { account, sibling })
        {
            await strict.SaveTaxLotAsync(new LedgerTaxLotRecord(
                Guid.NewGuid(), book.LedgerBookId, lotAccount, "same-lot", saleDate,
                100m, 100m, 100m, "USD", now, now, SecurityId: securityId, BookPositionId: Guid.NewGuid()), ct);
        }

        var query = new WashSaleReplacementQuery(book.LedgerBookId, account, securityId,
            saleDate, WashSalePolicy.UnitedStates, ["SAME-LOT"]);
        var lookup = await strict.ResolveAsync(query, ct);

        lookup.Replacements.Should().ContainSingle().Which.Account.Should().Be(sibling);
        var projection = LedgerTaxLotReliefProjector.Project(new LedgerTaxLotReliefInput(
            account, saleDate, 100m, 80m, LedgerTaxLotReliefMethod.Fifo,
            [new("same-lot", saleDate, 100m, 100m, securityId)],
            washSalePolicy: query.Policy, replacementAcquisitions: lookup.Replacements));
        projection.DisallowedWashSaleLoss.Should().Be(2000m);
        projection.WashSale!.BasisIncreases.Should().ContainSingle().Which.ReplacementAccount.Should().Be(sibling);
        projection.IsBalanced.Should().BeTrue();

        var accountLookup = await strict.ResolveAsync(query with
        {
            Policy = query.Policy with { Scope = WashSaleReplacementScope.DisposingAccount }
        }, ct);
        accountLookup.Replacements.Should().BeEmpty();
    }

    [LedgerDatabaseFact]
    public async Task WashSaleResolver_PriorDeferralsWithSameLotId_ApplyOnlyToCompleteDisposingAccount()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var ct = timeout.Token;
        await using var database = await LedgerPostgresTestDatabase.CreateAsync(ct);
        var registry = new PostgresFundProfileTenancyRegistry(database.Options);
        await registry.BindAsync("fund-alpha", "tenant-alpha", ct: ct);
        var now = DateTimeOffset.Parse("2026-05-15T12:00:00Z");
        var saleDate = new DateOnly(2026, 5, 15);
        var securityId = Guid.NewGuid();
        var book = new LedgerBookRecord(Guid.NewGuid(), "fund-alpha", Guid.NewGuid(),
            FundStructureNodeKindDto.Fund, "Alpha", "USD", now, now);
        var strict = new PostgresLedgerJournalStore(database.Options, new WorkerAccessor(), TenantScopeEnforcementOptions.FailClosed);
        using var authority = FundScopeTenantAuthority.Enter("tenant-alpha", "wash-sale prior-deferral account regression");
        await strict.SaveLedgerBookAsync(book, ct);
        var period = await strict.SavePeriodAsync(new(Guid.NewGuid(), book.LedgerBookId, 2026, 5, "2026-05",
            new(2026, 5, 1), new(2026, 5, 31), "Open", now, null, 0), 0, ct: ct);
        var priorJournal = Write(book.LedgerBookId, period.PeriodId, now.AddDays(-5));
        await strict.AppendAsync(priorJournal, ct);
        var priorBatchId = Guid.NewGuid();

        // Freeze a retained prior-disposal batch for the lookup fixture; this test exercises real
        // persisted deferrals and account resolution, not current disposal production.
        await using (var connection = new NpgsqlConnection(database.Options.ConnectionString))
        {
            await connection.OpenAsync(ct);
            await using var command = connection.CreateCommand();
            command.CommandText = $$"""
                insert into "{{database.Options.SchemaName}}".atomic_tax_lot_posting_batches (
                    mutation_batch_id, ledger_book_id, period_id, journal_entry_id, source_event_id,
                    idempotency_key, canonical_fingerprint, expected_period_version, mutation_kind,
                    retained_evidence, created_at, security_id, book_position_id)
                values (@batch_id, @book_id, @period_id, @journal_id, @source_event_id,
                    'prior-wash-sale-disposal', @fingerprint, @period_version, 'Disposal',
                    '[{"fixture":"retained-prior-disposal"}]'::jsonb, @recorded_at, @security_id, @position_id);
                """;
            command.Parameters.AddWithValue("batch_id", priorBatchId);
            command.Parameters.AddWithValue("book_id", book.LedgerBookId);
            command.Parameters.AddWithValue("period_id", period.PeriodId);
            command.Parameters.AddWithValue("journal_id", priorJournal.Entry.JournalEntryId);
            command.Parameters.AddWithValue("source_event_id", Guid.NewGuid());
            command.Parameters.AddWithValue("fingerprint", $"sha256:{new string('a', 64)}");
            command.Parameters.AddWithValue("period_version", period.Version);
            command.Parameters.AddWithValue("recorded_at", now.UtcDateTime);
            command.Parameters.AddWithValue("security_id", securityId);
            command.Parameters.AddWithValue("position_id", Guid.NewGuid());
            await command.ExecuteNonQueryAsync(ct);
        }

        var account = LedgerAccounts.Securities("AAPL", "broker-1");
        LedgerAccount[] accounts =
        [
            account,
            account with { FinancialAccountId = "broker-2" },
            account with { Name = "Other investments" },
            account with { AccountType = LedgerAccountType.Liability },
            account with { Symbol = "OTHER" },
            account with { Symbol = null },
            account with { FinancialAccountId = null }
        ];
        var deferrals = new List<WashSaleDeferralRecord>();
        for (var i = 0; i < accounts.Length; i++)
        {
            var lot = await strict.SaveTaxLotAsync(new LedgerTaxLotRecord(
                Guid.NewGuid(), book.LedgerBookId, accounts[i], "same-lot", saleDate.AddDays(-4),
                100m, 100m, 100m, "USD", now, now, SecurityId: securityId, BookPositionId: Guid.NewGuid()), ct);
            deferrals.Add(new WashSaleDeferralRecord(
                Guid.NewGuid(), book.LedgerBookId, priorBatchId, securityId, saleDate.AddDays(-5),
                account, lot.TaxLotRecordId, lot.LotId, 10m + i, 100m, new DateOnly(2024, 1, 1).AddDays(i),
                "prior-policy", 30, WashSaleReplacementScope.LedgerBook, now));
        }
        await strict.SaveWashSaleDeferralsAsync(deferrals, ct);

        foreach (var scope in new[] { WashSaleReplacementScope.LedgerBook, WashSaleReplacementScope.DisposingAccount })
        {
            for (var i = 0; i < accounts.Length; i++)
            {
                // Financial-account casing is an alias under LedgerAccount equality; null remains
                // a distinct identity, while name, type, and symbol retain their exact semantics.
                var disposingAccount = accounts[i] with
                {
                    FinancialAccountId = accounts[i].FinancialAccountId?.ToUpperInvariant()
                };
                var lookup = await strict.ResolveAsync(new WashSaleReplacementQuery(
                    book.LedgerBookId, disposingAccount, securityId, saleDate,
                    WashSalePolicy.UnitedStates with { Scope = scope }, ["SAME-LOT"]), ct);
                var adjustment = lookup.PriorDeferrals.Should().ContainSingle(
                    "basis belongs to the replacement account even when replacement discovery is book-wide").Which;
                adjustment.Value.Should().Be(deferrals[i].DisallowedAmount);
                adjustment.HoldingPeriodCarryDate.Should().Be(deferrals[i].HoldingPeriodCarryDate);
                adjustment.Reference.Should().Be($"wash-sale-deferral:{deferrals[i].DeferralId:D}");
                lookup.Replacements.Should().NotContain(replacement => replacement.Account == disposingAccount);
                lookup.Replacements.Should().HaveCount(scope == WashSaleReplacementScope.LedgerBook
                    ? accounts.Length - 1
                    : 0);
            }
        }
    }

    [LedgerDatabaseFact]
    public async Task LedgerStrictWrites_AllowRetainedOwnerAndRejectMissingOrForeignAuthorityWithoutMutation()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var registry = new PostgresFundProfileTenancyRegistry(database.Options);
        await registry.BindAsync("fund-alpha", "tenant-alpha");
        await registry.BindAsync("fund-beta", "tenant-beta");
        var strict = new PostgresLedgerJournalStore(database.Options, new WorkerAccessor(), TenantScopeEnforcementOptions.FailClosed);
        var now = DateTimeOffset.Parse("2026-05-15T12:00:00Z");
        var alpha = new LedgerBookRecord(Guid.NewGuid(), "fund-alpha", Guid.NewGuid(), FundStructureNodeKindDto.Fund,
            "Alpha retained", "USD", now, now);
        var beta = alpha with { LedgerBookId = Guid.NewGuid(), FundProfileId = "fund-beta", DisplayName = "Beta retained" };
        LedgerAccountingPeriod period;
        LedgerAccountTaxLotPolicyRecord policy;
        using (FundScopeTenantAuthority.Enter("tenant-alpha", "retained alpha job"))
        {
            await strict.SaveLedgerBookAsync(alpha);
            period = await strict.SavePeriodAsync(new(Guid.NewGuid(), alpha.LedgerBookId, 2026, 5, "2026-05",
                new(2026, 5, 1), new(2026, 5, 31), "Open", now, null, 0), 0);
            await strict.AppendAsync(Write(alpha.LedgerBookId, period.PeriodId, now));
            policy = await strict.SaveTaxLotPolicyAsync(new(Guid.NewGuid(), alpha.LedgerBookId,
                new("Cash", LedgerAccountType.Asset), LedgerTaxLotReliefMethod.Fifo, "reviewed", new(2026, 5, 1), now, now));
            (await strict.GetByPeriodAsync(period.PeriodId)).Should().ContainSingle();
        }
        using (FundScopeTenantAuthority.Enter("tenant-beta", "retained beta job"))
            await strict.SaveLedgerBookAsync(beta);

        foreach (var tenant in new string?[] { null, "tenant-beta" })
        {
            using var authority = tenant is null ? null : FundScopeTenantAuthority.Enter(tenant, "denied job");
            Func<Task> save = () => strict.SaveLedgerBookAsync(alpha with { DisplayName = "wrong", FundProfileId = "fund-beta" });
            Func<Task> update = () => strict.SavePeriodAsync(period with { LedgerBookId = beta.LedgerBookId, Label = "wrong" }, period.Version);
            Func<Task> append = () => strict.AppendAsync(Write(alpha.LedgerBookId, period.PeriodId, now));
            Func<Task> policyWrite = () => strict.SaveTaxLotPolicyAsync(policy with { LedgerBookId = beta.LedgerBookId, PolicyId = "wrong" });
            await save.Should().ThrowAsync<UnauthorizedAccessException>();
            await update.Should().ThrowAsync<UnauthorizedAccessException>();
            await append.Should().ThrowAsync<UnauthorizedAccessException>();
            await policyWrite.Should().ThrowAsync<UnauthorizedAccessException>();
        }
        (await database.JournalStore.GetLedgerBookAsync(alpha.LedgerBookId))!.DisplayName.Should().Be("Alpha retained");
        (await database.JournalStore.GetPeriodAsync(period.PeriodId))!.Label.Should().Be("2026-05");
        (await database.JournalStore.GetByPeriodAsync(period.PeriodId)).Should().ContainSingle();
        (await database.JournalStore.ListTaxLotPoliciesAsync(alpha.LedgerBookId)).Should().ContainSingle(row => row.PolicyId == "reviewed");
        using (FundScopeTenantAuthority.Enter("tenant-alpha", "retained owner update"))
            (await strict.SavePeriodAsync(period with { Label = "Reviewed" }, period.Version)).Version.Should().Be(2);
    }

    [LedgerDatabaseFact]
    public async Task WorkflowStrictWritesAndAudit_RequireOwnerAndKeepRetainedStateOnRefusal()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var registry = new PostgresFundProfileTenancyRegistry(database.Options);
        await registry.BindAsync("fund-alpha", "tenant-alpha");
        await registry.BindAsync("fund-beta", "tenant-beta");
        var now = DateTimeOffset.UtcNow;
        var alpha = await database.JournalStore.SaveLedgerBookAsync(new(Guid.NewGuid(), "fund-alpha", Guid.NewGuid(),
            FundStructureNodeKindDto.Fund, "Alpha", "USD", now, now));
        var beta = await database.JournalStore.SaveLedgerBookAsync(alpha with { LedgerBookId = Guid.NewGuid(), FundProfileId = "fund-beta" });
        var strict = new PostgresOperationsContinuityStore(database.Options, database.JournalStore,
            database.StatusDerivation, new WorkerAccessor(), TenantScopeEnforcementOptions.FailClosed);
        var workflow = OperationsContinuityWorkflow.Start(Guid.NewGuid(), Guid.NewGuid(), "2026-05", null,
            "retained-alpha", now, ledgerBookId: alpha.LedgerBookId);
        var audit = new OperationsWorkflowAuditDraft(workflow.WorkflowId, workflow.FundAccountId, workflow.PeriodId,
            "workflow-started", OperationsWorkflowStatusDto.NotStarted, OperationsWorkflowStatusDto.NotStarted,
            null, null, null, "retained-operator", "Reviewed", null, []);
        using (FundScopeTenantAuthority.Enter("tenant-alpha", "retained workflow job"))
        {
            await strict.CommitWorkflowStartAsync(workflow, audit);
            (await strict.GetAsync(workflow.WorkflowId)).Should().NotBeNull();
            (await strict.GetTimelineAsync(workflow.WorkflowId)).Should().ContainSingle();
        }
        var retained = await database.OperationsStore.GetAsync(workflow.WorkflowId);
        foreach (var tenant in new string?[] { null, "tenant-beta" })
        {
            using var authority = tenant is null ? null : FundScopeTenantAuthority.Enter(tenant, "denied workflow job");
            var foreign = OperationsContinuityWorkflow.Start(workflow.WorkflowId, workflow.FundAccountId, workflow.PeriodId,
                null, "wrong", now, ledgerBookId: beta.LedgerBookId);
            Func<Task> overwrite = () => strict.SaveAsync(foreign);
            Func<Task> auditWrite = () => strict.AppendAsync(audit);
            Func<Task> transition = () => strict.CommitWorkflowTransitionAsync(foreign, audit, false);
            Func<Task> auditRead = () => strict.GetTimelineAsync(workflow.WorkflowId);
            Func<Task> foreignCreate = () => strict.SaveAsync(OperationsContinuityWorkflow.Start(Guid.NewGuid(), Guid.NewGuid(),
                "2026-05", null, "wrong", now, ledgerBookId: alpha.LedgerBookId));
            await overwrite.Should().ThrowAsync<UnauthorizedAccessException>();
            await auditWrite.Should().ThrowAsync<UnauthorizedAccessException>();
            await transition.Should().ThrowAsync<UnauthorizedAccessException>();
            await auditRead.Should().ThrowAsync<UnauthorizedAccessException>();
            await foreignCreate.Should().ThrowAsync<UnauthorizedAccessException>();
        }
        (await database.OperationsStore.GetAsync(workflow.WorkflowId))!.BrokerSource.Should().Be(retained!.BrokerSource);
        (await database.OperationsStore.GetTimelineAsync(workflow.WorkflowId)).Should().ContainSingle();
    }

    [LedgerDatabaseFact]
    public async Task TaxLotApis_RequireRetainedBookAuthorityForReadsAndWrites()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var registry = new PostgresFundProfileTenancyRegistry(database.Options);
        await registry.BindAsync("fund-alpha", "tenant-alpha");
        await registry.BindAsync("fund-beta", "tenant-beta");
        var now = DateTimeOffset.UtcNow;
        var date = DateOnly.FromDateTime(now.UtcDateTime);
        var alpha = await database.JournalStore.SaveLedgerBookAsync(new(Guid.NewGuid(), "fund-alpha", Guid.NewGuid(),
            FundStructureNodeKindDto.Fund, "Alpha", "USD", now, now));
        var beta = await database.JournalStore.SaveLedgerBookAsync(alpha with { LedgerBookId = Guid.NewGuid(), FundProfileId = "fund-beta" });
        var strict = new PostgresLedgerJournalStore(database.Options, new WorkerAccessor(), TenantScopeEnforcementOptions.FailClosed);
        var lot = new LedgerTaxLotRecord(Guid.NewGuid(), alpha.LedgerBookId, new("Investments", LedgerAccountType.Asset),
            "retained-lot", date, 10m, 10m, 20m, "USD", now, now, SecurityId: Guid.NewGuid(), BookPositionId: Guid.NewGuid());
        var replacementQuery = new WashSaleReplacementQuery(alpha.LedgerBookId, lot.Account, lot.SecurityId,
            date, WashSalePolicy.UnitedStates, []);
        using (FundScopeTenantAuthority.Enter("tenant-alpha", "retained lot owner"))
        {
            lot = await strict.SaveTaxLotAsync(lot);
            (await strict.ListOpenTaxLotsAsync(alpha.LedgerBookId, lot.Account)).Should().ContainSingle();
            (await strict.GetTaxLotsByIdsAsync(alpha.LedgerBookId, [lot.TaxLotRecordId])).Should().ContainSingle();
            (await strict.ResolveAsync(replacementQuery)).Replacements.Should().ContainSingle();
            (await strict.CaptureReportingSnapshotAsync(new(LedgerBookId: alpha.LedgerBookId)))
                .Journals.Should().BeEmpty();
        }
        var deferral = new WashSaleDeferralRecord(Guid.NewGuid(), alpha.LedgerBookId, Guid.NewGuid(), lot.SecurityId,
            date, lot.Account, lot.TaxLotRecordId, lot.LotId, 1m, 1m, date, "reviewed", 30,
            WashSaleReplacementScope.LedgerBook, now);
        foreach (var tenant in new string?[] { null, "tenant-beta" })
        {
            using var authority = tenant is null ? null : FundScopeTenantAuthority.Enter(tenant, "denied lot job");
            Func<Task>[] denied =
            [
                () => strict.SaveTaxLotAsync(lot with { LedgerBookId = beta.LedgerBookId, OpenQuantity = 1m }),
                () => strict.ListOpenTaxLotsAsync(alpha.LedgerBookId, lot.Account),
                () => strict.GetTaxLotsByIdsAsync(alpha.LedgerBookId, [lot.TaxLotRecordId]),
                () => strict.ListOpenTaxLotsByAssetScopeAsync(alpha.LedgerBookId, lot.SecurityId, lot.BookPositionId, date),
                () => strict.GetTaxLotDisposalHistoryAsync(alpha.LedgerBookId, [Guid.NewGuid()]),
                () => strict.CaptureReportingSnapshotAsync(new(LedgerBookId: alpha.LedgerBookId)),
                () => strict.ResolveAsync(replacementQuery),
                () => strict.ListWashSaleDeferralsAsync(alpha.LedgerBookId, date, date),
                () => strict.SaveWashSaleDeferralsAsync([deferral])
            ];
            foreach (var operation in denied)
                await operation.Should().ThrowAsync<UnauthorizedAccessException>();
        }
        (await database.JournalStore.GetTaxLotsByIdsAsync(alpha.LedgerBookId, [lot.TaxLotRecordId]))
            .Should().ContainSingle(row => row.OpenQuantity == 10m && row.LedgerBookId == alpha.LedgerBookId);
        (await database.JournalStore.ListWashSaleDeferralsAsync(alpha.LedgerBookId, date, date)).Should().BeEmpty();
    }

    [LedgerDatabaseFact]
    public async Task TaxLotUpsert_RejectsBookMoveWithinSameTenantAndPreservesRetainedRow()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var registry = new PostgresFundProfileTenancyRegistry(database.Options);
        await registry.BindAsync("fund-alpha", "tenant-alpha");
        var now = DateTimeOffset.Parse("2026-05-15T12:00:00Z");
        var originalBook = await database.JournalStore.SaveLedgerBookAsync(new(Guid.NewGuid(), "fund-alpha", Guid.NewGuid(),
            FundStructureNodeKindDto.Fund, "Original book", "USD", now, now));
        var otherBook = await database.JournalStore.SaveLedgerBookAsync(originalBook with
        {
            LedgerBookId = Guid.NewGuid(),
            FundStructureNodeId = Guid.NewGuid(),
            DisplayName = "Another book owned by the same tenant"
        });
        var strict = new PostgresLedgerJournalStore(database.Options, new WorkerAccessor(), TenantScopeEnforcementOptions.FailClosed);
        using var authority = FundScopeTenantAuthority.Enter("tenant-alpha", "retained lot owner");
        var retained = await strict.SaveTaxLotAsync(new(Guid.NewGuid(), originalBook.LedgerBookId,
            new("Investments", LedgerAccountType.Asset), "retained-lot", new(2026, 5, 15), 10m, 10m, 20m, "USD", now, now,
            EvidenceRef: "original-evidence"));
        var move = retained with
        {
            LedgerBookId = otherBook.LedgerBookId,
            OpenQuantity = 1m,
            EvidenceRef = "replacement-evidence",
            UpdatedAt = now.AddMinutes(1)
        };

        Func<Task> strictMove = () => strict.SaveTaxLotAsync(move);
        await strictMove.Should().ThrowAsync<UnauthorizedAccessException>();
        // The SQL guard also enforces immutable book identity when the compatibility store skips
        // the authority preflight, including an upsert that encounters an intervening insert.
        Func<Task> compatibilityMove = () => database.JournalStore.SaveTaxLotAsync(move);
        await compatibilityMove.Should().ThrowAsync<InvalidOperationException>();

        (await strict.GetTaxLotsByIdsAsync(originalBook.LedgerBookId, [retained.TaxLotRecordId]))
            .Should().ContainSingle().Which.Should().BeEquivalentTo(retained);
        (await strict.GetTaxLotsByIdsAsync(otherBook.LedgerBookId, [retained.TaxLotRecordId])).Should().BeEmpty();
        var updated = await strict.SaveTaxLotAsync(retained with { OpenQuantity = 9m, UpdatedAt = now.AddMinutes(2) });
        updated.LedgerBookId.Should().Be(originalBook.LedgerBookId);
        updated.OpenQuantity.Should().Be(9m);
        updated.Version.Should().Be(retained.Version + 1);
    }

    private static LedgerJournalEntryWrite Write(Guid bookId, Guid periodId, DateTimeOffset now)
    {
        var id = Guid.NewGuid();
        return new(new JournalEntry(id, now, "Scoped posting",
        [
            new LedgerEntry(Guid.NewGuid(), id, now, new("Cash", LedgerAccountType.Asset), 100m, 0m, "Scoped posting"),
            new LedgerEntry(Guid.NewGuid(), id, now, new("Revenue", LedgerAccountType.Revenue), 0m, 100m, "Scoped posting")
        ]), bookId, periodId, LedgerBookId: bookId);
    }

    private sealed class WorkerAccessor : IFundScopeTenantAccessor
    {
        public string? ResolveCallerTenant() => FundScopeTenantAuthority.CurrentTenantId;
    }
}
