using FluentAssertions;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Tenancy;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.OperationsContinuity;
using Meridian.Ledger;
using Meridian.Storage.Ledger;

namespace Meridian.Tests.Storage;

[Trait("Category", "Integration")]
public sealed class StrictTenantMutationPostgresTests
{
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
