using Meridian.Application.FundStructure;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Meridian.Tests.Storage;
using Meridian.Tests.Storage.FundAccounts;
using Meridian.Ui.Shared.Services;

namespace Meridian.Tests.Integration;

public sealed partial class IntercompanyAgreementFeaturePostgresTests
{
    [FundAccountDatabaseFact]
    public Task NativeManualAgreement_PreparesReviewsAndPostsPriorPeriodInverseAfterTermination()
        => GovernedManualReversalAsync(legacyNullSource: false);

    [FundAccountDatabaseFact]
    public Task LegacyNullManualAgreement_SubmitsApprovesAndPostsExactReversalAfterAmendment()
        => GovernedManualReversalAsync(legacyNullSource: true);

    private static async Task GovernedManualReversalAsync(bool legacyNullSource)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        var ct = timeout.Token;
        await using var data = await Fixture.CreateAsync(ct);
        await data.ApproveAsync(data.Draft, ct);
        var options = new LedgerJournalStoreOptions
        {
            ConnectionString = data.Options.ConnectionString,
            SchemaName = data.Server.CreateSchemaName("ic_manual_reversal")
        };
        await new LedgerMigrationRunner(options).EnsureMigratedAsync(ct);
        var authority = new PostgresIntercompanyAgreementPostingAuthority(data.Store);
        var ledger = new PostgresLedgerJournalStore(options, intercompanyAgreementAuthority: () => authority);
        var book = IntercompanyAgreementPostingGuardTests.Book();
        await new PostgresFundProfileTenancyRegistry(options).BindAsync(book.FundProfileId, Fixture.Tenant, ct: ct);
        await ledger.SaveLedgerBookAsync(book, ct);
        var period = await ledger.SavePeriodAsync(new(Guid.NewGuid(), book.LedgerBookId, 2026, 5, "Manual agreement May",
            new(2026, 5, 1), new(2026, 5, 31), "Open", DateTimeOffset.UtcNow, null, 0), 0, ct: ct);
        const string company = "company-agreement";
        var configurations = new InMemoryAccountingConfigurationStore();
        await configurations.SaveAsync(new AccountingConfigurationWorkspaceDto(book.FundProfileId, book.LedgerBookId,
            AccountingConfigurationStatusDto.Draft, "synthetic-v1", DateTimeOffset.UtcNow, [],
            [new("receivable", "Assets:Intercompany Receivable", "Assets:Intercompany Receivable", "Asset"),
             new("cash", "Assets:Cash", "Cash", "Asset")], [], [], [], [], TenantId: Fixture.Tenant, CompanyId: company), ct);
        var audit = new InMemoryAccountingActionAuditStore();
        var configuration = new AccountingConfigurationService(configurations, audit);
        var drafts = new InMemoryManualJournalEntryDraftStore();
        using var target = new DurableLedgerPostingTarget(ledger);
        var workbench = new ManualJournalEntryWorkbenchService(drafts, configuration, audit, journalStore: ledger, postingTarget: target);
        var dimensions = new LedgerDimensionSetDto(EntityId: data.Draft.PartyOneId.ToString("D"),
            CounterpartyId: data.Draft.PartyTwoId.ToString("D"), CostCenterId: "Desk-A",
            ExternalGlDimensions: new Dictionary<string, string> { ["Department"] = "Treasury" })
        { IntercompanyAgreementId = data.AgreementId, IntercompanyAgreementVersion = 1 };
        var now = DateTimeOffset.UtcNow;
        var sourceDraft = new ManualJournalEntryDraftDto(Guid.NewGuid(), ManualJournalEntryStatusDto.Draft,
            book.FundProfileId, book.LedgerBookId, book.AccountingBasis, new(2026, 5, 15), period.PeriodId.ToString("D"),
            data.Draft.PartyOneId.ToString("D"), null, "USD", "Synthetic governed agreement advance", "agreement-preparer", now, now, 1,
            [new("receivable", AccountingTemplateLineSideDto.Debit, 120m, "USD", "Assets:Intercompany Receivable", Dimensions: dimensions),
             new("cash", AccountingTemplateLineSideDto.Credit, 120m, "USD", "Assets:Cash",
                 Dimensions: new(EntityId: data.Draft.PartyOneId.ToString("D")))], [], [], TenantId: Fixture.Tenant, CompanyId: company);
        sourceDraft = sourceDraft with { EvidenceLinks = [Evidence(sourceDraft)] };
        sourceDraft = await workbench.SaveDraftAsync(new(sourceDraft, sourceDraft.PreparedBy,
            LedgerBookId: book.LedgerBookId, TenantId: Fixture.Tenant, CompanyId: company), ct);
        ManualJournalEntryDraftDto source;
        if (legacyNullSource)
        {
            var approved = await ApproveAsync(sourceDraft);
            // Seed an immutable historical posting from the earlier functional-only writer.
            // Today's reversal still traverses the real workbench and PostgreSQL authority.
            var timestamp = new DateTimeOffset(2026, 5, 15, 0, 0, 0, TimeSpan.Zero);
            var key = "synthetic-legacy-agreement:" + approved.JournalEntryId.ToString("D");
            var command = new AccountingPostingCommandDto(Guid.NewGuid(), book.LedgerBookId, period.PeriodId,
                approved.AccountingDate, timestamp, key, ApprovalState: AccountingPostingApprovalStateDto.Approved,
                ApprovalId: approved.ApprovalId, OperatorRationale: "Retained historical approved posting.",
                LedgerBookId: book.LedgerBookId)
            { Actor = "synthetic-legacy-writer" };
            var lines = approved.Lines.Select(line => new LedgerEntry(Guid.NewGuid(), approved.JournalEntryId, timestamp,
                new(line.AccountPath == "Assets:Cash" ? "Cash" : "Assets:Intercompany Receivable", LedgerAccountType.Asset),
                line.Side == AccountingTemplateLineSideDto.Debit ? line.Amount : 0m,
                line.Side == AccountingTemplateLineSideDto.Credit ? line.Amount : 0m,
                approved.Memo, LedgerDimensionMapper.ToDomain(line.Dimensions))).ToArray();
            await ledger.AppendAsync(new(new(approved.JournalEntryId, timestamp, approved.Memo, lines,
                new(EffectiveDate: approved.AccountingDate, IdempotencyKey: key)), book.LedgerBookId, period.PeriodId,
                AccountingBasis: book.AccountingBasis, AccountingPolicyId: book.AccountingPolicyId,
                AccountingPolicyVersion: book.AccountingPolicyVersion, PostingCommand: command, LedgerBookId: book.LedgerBookId), ct);
            source = approved with
            {
                Status = ManualJournalEntryStatusDto.Posted,
                Version = approved.Version + 1,
                PostedAtUtc = now,
                PostedBy = "synthetic-legacy-writer"
            };
            await drafts.SaveAsync(source, ct);
        }
        else
            source = await PostAsync(sourceDraft);

        var original = Assert.Single(await ledger.QueryAsync(new(LedgerBookId: book.LedgerBookId, JournalEntryId: source.JournalEntryId), ct));
        Assert.All(original.Entry.Lines, line => Assert.Equal(legacyNullSource, line.Currency is null));
        await data.ApproveAsync(data.Draft with
        {
            Action = IntercompanyAgreementActionDto.Amend,
            EffectiveFrom = new(2026, 2, 1),
            Terms = new(FixedAnnualRate: 0.075m, Notes: "Reviewed changed current terms")
        }, ct);
        ManualJournalEntryDraftDto inverse;
        if (legacyNullSource)
        {
            inverse = Assert.Single((await workbench.ApplyLifecycleActionAsync(new(source.JournalEntryId, book.FundProfileId,
                JournalEntryLifecycleActionDto.Reverse, "agreement-corrector", source.Version,
                Notes: "Reverse the immutable historical agreement source.", EvidenceLinks: [Evidence(source)],
                LedgerBookId: book.LedgerBookId, TenantId: Fixture.Tenant, CompanyId: company), ct)).GeneratedJournalEntries);
        }
        else
        {
            await data.ApproveAsync(data.Draft with
            {
                Action = IntercompanyAgreementActionDto.Terminate,
                TerminationEffectiveDate = new(2026, 5, 20)
            }, ct);
            await ledger.SaveHardClosedPeriodAsync(period with { Status = "HardClosed", ClosedAt = now }, period.Version,
                new(Guid.NewGuid(), period.PeriodId, "Open", "HardClosed", "close-controller", "Close the native agreement source.", now), ct);
            var june = await ledger.SavePeriodAsync(new(Guid.NewGuid(), book.LedgerBookId, 2026, 6, "Manual agreement June",
                new(2026, 6, 1), new(2026, 6, 30), "Open", now, null, 0), 0, ct: ct);
            var intake = new AutomatedJournalDraftIntakeService(workbench, drafts, configuration);
            inverse = Assert.Single((await new PeriodAdjustmentPreparationService(ledger, intake).PrepareAsync(
                new(book.LedgerBookId, source.JournalEntryId, june.PeriodId, june.StartDate, "native-agreement-inverse",
                    "Reverse the exact native agreement source after termination."), "agreement-corrector", Fixture.Tenant, company, ct)).Created);
        }
        var posted = await PostAsync(inverse);
        var retained = Assert.Single(await ledger.QueryAsync(new(LedgerBookId: book.LedgerBookId, JournalEntryId: posted.JournalEntryId), ct));
        Assert.Equal(source.JournalEntryId, retained.SourceJournalEntryId);
        Assert.All(retained.Entry.Lines, line => Assert.Equal(legacyNullSource, line.Currency is null));
        var unmatched = original.Entry.Lines.ToList();
        foreach (var line in retained.Entry.Lines)
        {
            var match = Assert.Single(unmatched.Where(prior => prior.Account == line.Account));
            Assert.Equal(match.Debit, line.Credit);
            Assert.Equal(match.Credit, line.Debit);
            Assert.Equivalent(LedgerDimensionMapper.ToDto(match.Dimensions), LedgerDimensionMapper.ToDto(line.Dimensions));
            if (match.Currency is { } currency)
                Assert.Equal(new LedgerEntryCurrency(currency.TransactionCurrency, currency.FunctionalCurrency,
                    currency.TransactionCredit, currency.TransactionDebit, currency.FxRateToFunctional), line.Currency);
            unmatched.Remove(match);
        }
        Assert.Empty(unmatched);
        var agreementLine = Assert.Single(retained.Entry.Lines.Where(line => line.Dimensions?.IntercompanyAgreementId is not null));
        Assert.Equal(1, agreementLine.Dimensions!.IntercompanyAgreementVersion);
        Assert.Equal(data.AgreementId, agreementLine.Dimensions.IntercompanyAgreementId);
        var recovered = await ledger.QueryAsync(new(LedgerBookId: book.LedgerBookId, JournalEntryId: source.JournalEntryId), ct);
        Assert.Equivalent(original, Assert.Single(recovered));

        async Task<ManualJournalEntryDraftDto> ApproveAsync(ManualJournalEntryDraftDto draft)
        {
            var validation = await workbench.ValidateDraftAsync(new(draft, draft.PreparedBy, LedgerBookId: book.LedgerBookId,
                TenantId: Fixture.Tenant, CompanyId: company), ct);
            Assert.DoesNotContain(validation.ValidationIssues, issue => issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);
            var submitted = await workbench.SubmitApprovalAsync(new(draft.JournalEntryId, book.FundProfileId, draft.PreparedBy, draft.Version,
                Notes: "Review the exact agreement source and currency evidence.", EvidenceLinks: [Evidence(draft)],
                LedgerBookId: book.LedgerBookId, TenantId: Fixture.Tenant, CompanyId: company), ct);
            return (await workbench.ApplyLifecycleActionAsync(new(draft.JournalEntryId, book.FundProfileId,
                JournalEntryLifecycleActionDto.Approve, "independent-controller", submitted.Version,
                Notes: "Approve the retained agreement journal evidence.", EvidenceLinks: [Evidence(draft)],
                LedgerBookId: book.LedgerBookId, TenantId: Fixture.Tenant, CompanyId: company), ct)).JournalEntry;
        }
        async Task<ManualJournalEntryDraftDto> PostAsync(ManualJournalEntryDraftDto draft)
        {
            var approved = await ApproveAsync(draft);
            return (await workbench.ApplyLifecycleActionAsync(new(draft.JournalEntryId, book.FundProfileId,
                JournalEntryLifecycleActionDto.Post, "independent-controller", approved.Version,
                Notes: "Post the reviewed exact agreement journal.", EvidenceLinks: [Evidence(draft)],
                LedgerBookId: book.LedgerBookId, TenantId: Fixture.Tenant, CompanyId: company), ct)).JournalEntry;
        }
        string Evidence(ManualJournalEntryDraftDto draft)
            => $"evidence://review-correction/tenant/{Fixture.Tenant}/company/{company}/book/{book.LedgerBookId:D}/journal/{draft.JournalEntryId:D}/approval-posting";
    }
}
