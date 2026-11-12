using System.Text.Json;
using FluentAssertions;
using Meridian.Application.Subscriptions.Services;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using PortfolioSummary = Meridian.Core.Subscriptions.Models.PortfolioSummary;
using Meridian.Core.Config;
using Meridian.Core.Subscriptions.Models;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Meridian.Tests.Storage;
using Meridian.Ui.Shared.Services;
using Meridian.PortfolioRecords.FundAccounts;
using NSubstitute;

namespace Meridian.Tests.Ui;

public sealed partial class PortfolioOpeningBalancePostingTests
{
    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public Task TwoSymbolImport_PreparesReviewsAndPostsOneBalancedOpeningJournal()
        => PrepareReviewAndPostOpeningAsync(AccountingBasisKindDto.Primary);

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public Task NonPrimaryBookOpening_PreparesReviewsAndPostsWithAuthoritativeBasisAndPolicy()
        => PrepareReviewAndPostOpeningAsync(AccountingBasisKindDto.Tax);

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public Task MixedOpening_ClosedSourcePreparesAndPostsCurrentPeriodInverseAndReversal()
        => PrepareReviewAndPostOpeningAsync(AccountingBasisKindDto.Primary, JournalEntryLifecycleActionDto.Reverse);

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public Task MixedTaxOpening_ClosedSourcePreparesAndPostsCurrentPeriodInverseAndRebook()
        => PrepareReviewAndPostOpeningAsync(AccountingBasisKindDto.Tax, JournalEntryLifecycleActionDto.Rebook);

    private static async Task PrepareReviewAndPostOpeningAsync(AccountingBasisKindDto basis,
        JournalEntryLifecycleActionDto? correctionAction = null, OpeningRebookScenario? openRebook = null)
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var root = Path.Combine(Path.GetTempPath(), "meridian-opening-post-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            const string tenant = "tenant-opening";
            const string company = "company-opening";
            var at = new DateTimeOffset(2026, 11, 12, 18, 0, 0, TimeSpan.Zero);
            var fund = Guid.NewGuid().ToString("D");
            var account = Guid.NewGuid();
            var entity = Guid.NewGuid();
            var book = Guid.NewGuid();
            var period = Guid.NewGuid();
            await database.JournalStore.SaveLedgerBookAsync(new(book, fund, account,
                FundStructureNodeKindDto.Account, "Imported brokerage", "USD", at.AddDays(-1), at.AddDays(-1),
                AccountingBasis: basis, AccountingPolicyId: "broker-opening-policy", AccountingPolicyVersion: "v2"));
            await database.JournalStore.SavePeriodAsync(new(period, book, 2026, 11, "2026-11",
                new(2026, 11, 1), new(2026, 11, 30), "Open", at.AddDays(-1), null, 0), 0);
            var securities = Substitute.For<ISecurityMasterQueryService>();
            foreach (var symbol in new[] { "AAPL", "MSFT" })
            {
                var detail = Security(symbol, at);
                securities.GetByIdentifierAsync(SecurityIdentifierKind.Ticker, symbol, null, Arg.Any<CancellationToken>(), Arg.Any<DateTimeOffset?>())
                    .Returns(detail);
                securities.GetByIdAsync(detail.SecurityId, Arg.Any<CancellationToken>()).Returns(detail);
                securities.GetByIdAsOfAsync(detail.SecurityId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(detail);
            }
            var configurationStore = new InMemoryAccountingConfigurationStore();
            await configurationStore.SaveAsync(new AccountingConfigurationWorkspaceDto(fund, book,
                AccountingConfigurationStatusDto.Draft, "v1", at, [],
                [new("cash", "Assets:Cash", "Cash", "Asset"),
                 new("securities", "Assets:Securities", "Securities", "Asset"),
                 new("capital", "Equity:Capital", "Capital Account", "Equity")], [], [], [], [], TenantId: tenant, CompanyId: company));
            var audit = new InMemoryAccountingActionAuditStore();
            var configuration = new AccountingConfigurationService(configurationStore, audit);
            var drafts = new InMemoryManualJournalEntryDraftStore();
            using var postingTarget = new DurableLedgerPostingTarget(database.JournalStore);
            var capturingTarget = new CapturingPostingTarget(postingTarget);
            var workbench = new ManualJournalEntryWorkbenchService(drafts, configuration, audit, securities,
                database.JournalStore, postingTarget: capturingTarget);
            var intake = new AutomatedJournalDraftIntakeService(workbench, drafts, configuration);
            var configPath = Path.Combine(root, "appsettings.json");
            await File.WriteAllTextAsync(configPath, JsonSerializer.Serialize(new AppConfig(DataRoot: root)));
            var accountAuthority = Substitute.For<IFundAccountService>();
            accountAuthority.GetAccountAsync(account, Arg.Any<CancellationToken>()).Returns(new AccountSummaryDto(
                account, AccountTypeDto.Brokerage, entity, Guid.Parse(fund), null, null, "synthetic-account", "Imported brokerage",
                "USD", "alpaca", true, at.AddYears(-1), null, null, null, null, null));
            var queue = new PortfolioOpeningBalanceQueue(intake, new ConfigStore(configPath), database.JournalStore, securities, accountAuthority);
            var request = new PortfolioImportRunRequest(Guid.NewGuid(), "alpaca", new(BrokerAccountId: "synthetic-account"),
                new(tenant, company, fund, account, book, period, new(2026, 11, 12), "import-preparer", entity), true);
            var cashOnly = openRebook == OpeningRebookScenario.CashUnchanged;
            var openingTotal = cashOnly ? 100m : 375m;
            var snapshot = new PortfolioSummary("alpaca", "synthetic-account", openingTotal, cashOnly ? 100m : 25m, null,
                cashOnly ? [] : [new("AAPL", 2m, 200m, 100m, null, "us_equity", null),
                 new("MSFT", 3m, 150m, 50m, null, "us_equity", null)], at);
            var run = new PortfolioImportRun(request, "synthetic-command-hash", PortfolioImportRunStatus.Partial, at, at,
                snapshot, PortfolioImportRunService.SnapshotHash(snapshot));
            var id = await queue.QueueAsync(run);
            (await database.JournalStore.GetByPeriodAsync(period)).Should().BeEmpty("intake must never approve or post automatically");
            var draft = (await drafts.GetAsync(fund, id, tenantId: tenant, companyId: company))!;
            draft.AccountingBasis.Should().Be(basis, "opening intake must retain the selected book's accounting basis");
            var differentBasis = basis == AccountingBasisKindDto.Primary ? AccountingBasisKindDto.Tax : AccountingBasisKindDto.Primary;
            Assert.Throws<InvalidOperationException>(() => ManualJournalEntryWorkbenchService.EnsureOpeningBalanceSaveAuthority(
                draft with { AccountingBasis = differentBasis }, draft, trustedAutomatedIntake: false));
            Assert.Throws<InvalidOperationException>(() => ManualJournalEntryWorkbenchService.EnsureOpeningBalanceSaveAuthority(
                draft with { RebookedFromJournalEntryId = Guid.NewGuid() }, draft, trustedAutomatedIntake: false));
            var validated = await workbench.ValidateDraftAsync(new(draft, "import-preparer", LedgerBookId: book, TenantId: tenant, CompanyId: company));
            validated.ValidationIssues.Should().NotContain(issue => issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);
            draft.ValidationIssues.Should().NotContain(issue => issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);
            draft.Lines.Where(line => line.LedgerAccountSymbol is not null).Should().OnlyContain(line => line.SecurityId.HasValue);
            var review = $"evidence://review/tenant/{tenant}/company/{company}/book/{book:D}/journal/{id:D}/approval-posting";
            var submitted = await workbench.SubmitApprovalAsync(new(id, fund, "import-preparer", draft.Version,
                Notes: "Submit retained broker opening", EvidenceLinks: [review], LedgerBookId: book, TenantId: tenant, CompanyId: company));
            submitted.AccountingBasis.Should().Be(basis);
            var approved = await workbench.ApplyLifecycleActionAsync(new(id, fund, JournalEntryLifecycleActionDto.Approve,
                "controller", submitted.Version, Notes: "Reviewed source cost and cash", EvidenceLinks: [review],
                LedgerBookId: book, TenantId: tenant, CompanyId: company));
            approved.JournalEntry.AccountingBasis.Should().Be(basis);
            var posted = await workbench.ApplyLifecycleActionAsync(new(id, fund, JournalEntryLifecycleActionDto.Post,
                "controller", approved.JournalEntry.Version, Notes: "Post reviewed opening balances", EvidenceLinks: [review],
                LedgerBookId: book, TenantId: tenant, CompanyId: company));
            posted.JournalEntry.Status.Should().Be(ManualJournalEntryStatusDto.Posted);
            posted.JournalEntry.AccountingBasis.Should().Be(basis);
            var record = (await database.JournalStore.GetByPeriodAsync(period)).Should().ContainSingle().Subject;
            record.AccountingBasis.Should().Be(basis);
            record.AccountingPolicyId.Should().Be("broker-opening-policy");
            record.AccountingPolicyVersion.Should().Be("v2");
            var retained = record.Entry;
            retained.Lines.Sum(line => line.Debit).Should().Be(openingTotal);
            retained.Lines.Sum(line => line.Credit).Should().Be(openingTotal);
            retained.Lines.Where(line => line.Account.Symbol is not null).Should().HaveCount(cashOnly ? 0 : 2)
                .And.OnlyContain(line => line.Dimensions!.InstrumentId.HasValue);
            retained.Metadata.SecurityId.Should().BeNull("mixed journals use the proven per-line identities");
            if (!cashOnly)
                retained.Metadata.Tags!["securityMaster.lineageMode"].Should().Be("per-line");
            (await queue.QueueAsync(run)).Should().Be(id, "receipt recovery cannot create another opening");
            (await database.JournalStore.GetByPeriodAsync(period)).Should().ContainSingle();
            if (openRebook is { } scenario)
                await ExerciseOpenOpeningRebooksAsync(new(database, workbench, drafts, configuration, capturingTarget,
                    posted.JournalEntry, retained, fund, tenant, company, book, period), scenario);
            if (correctionAction is { } action)
            {
                var originalPeriod = (await database.JournalStore.GetPeriodAsync(period))!;
                await database.JournalStore.SaveHardClosedPeriodAsync(originalPeriod with { Status = "HardClosed", ClosedAt = at },
                    originalPeriod.Version, new(Guid.NewGuid(), period, "Open", "HardClosed", "close-controller",
                        "Retain the imported opening before correction.", at));
                var nextPeriod = Guid.NewGuid();
                await database.JournalStore.SavePeriodAsync(new(nextPeriod, book, 2026, 12, "2026-12",
                    new(2026, 12, 1), new(2026, 12, 31), "Open", at, null, 0), 0);
                var preparation = new PeriodAdjustmentPreparationService(database.JournalStore, intake);
                var inverse = (await preparation.PrepareAsync(new(book, id, nextPeriod, new(2026, 12, 1),
                    "correct-mixed-opening", "Correct the closed imported opening."), "adjustment-preparer", tenant, company)).Created.Single();
                inverse.AccountingBasis.Should().Be(basis);
                var sourceInstrumentIds = retained.Lines.Where(line => line.Account.Symbol is not null)
                    .Select(line => line.Dimensions!.InstrumentId!.Value).ToArray();
                inverse.Lines.Where(line => line.LedgerAccountSymbol is not null).Select(line => line.SecurityId!.Value)
                    .Should().BeEquivalentTo(sourceInstrumentIds);

                // A serialized client cannot substitute both the inverse and its expected lines.
                var alteredLines = inverse.Lines.Select(line => line.LedgerAccountSymbol == "AAPL"
                    ? line with { SecurityId = Guid.NewGuid(), Dimensions = line.Dimensions! with { InstrumentId = Guid.NewGuid() } }
                    : line).ToArray();
                var untrusted = JsonSerializer.Deserialize<ManualJournalEntryDraftDto>(JsonSerializer.Serialize(inverse with
                {
                    Lines = alteredLines,
                    PriorPeriodAdjustmentSource = inverse.PriorPeriodAdjustmentSource! with { ExpectedLines = alteredLines }
                }))!;
                var refused = await workbench.ValidateDraftAsync(new(untrusted, "adjustment-preparer",
                    LedgerBookId: book, TenantId: tenant, CompanyId: company));
                refused.ValidationIssues.Should().Contain(issue => issue.Code == "manual-je.prior-period-authority-required");
                (await database.JournalStore.GetByPeriodAsync(nextPeriod)).Should().BeEmpty();

                var postedInverse = await ReviewAndPostAsync(inverse);
                var inverseWrite = capturingTarget.LastWrite!;
                AssertPerLineIdentity(inverseWrite.Entry);
                // The public guard does not turn a prior-period prefix into source authority.
                var retainedNextPeriod = (await database.JournalStore.GetPeriodAsync(nextPeriod))!;
                var unverified = () => LedgerPeriodPostingGuard.Validate(inverseWrite, retainedNextPeriod);
                unverified.Should().Throw<LedgerValidationException>().WithMessage("*retained opening*");
                var wrongRootTags = new Dictionary<string, string>(inverseWrite.Entry.Metadata.Tags!)
                { ["prior-period.sourceJournalEntryId"] = Guid.NewGuid().ToString("D") };
                var wrongRoot = inverseWrite with
                {
                    Entry = new JournalEntry(inverseWrite.Entry.JournalEntryId,
                    inverseWrite.Entry.Timestamp, inverseWrite.Entry.Description, inverseWrite.Entry.Lines,
                    inverseWrite.Entry.Metadata with { Tags = wrongRootTags })
                };
                var appendWrongRoot = () => database.JournalStore.AppendAsync(wrongRoot);
                await appendWrongRoot.Should().ThrowAsync<LedgerValidationException>().WithMessage("*root does not match*");
                var changedEconomics = inverseWrite with
                {
                    Entry = new JournalEntry(inverseWrite.Entry.JournalEntryId,
                    inverseWrite.Entry.Timestamp, inverseWrite.Entry.Description, inverseWrite.Entry.Lines.Select(line =>
                        new LedgerEntry(line.EntryId, line.JournalEntryId, line.Timestamp, line.Account,
                            line.Debit * 2m, line.Credit * 2m, line.Description, line.Dimensions,
                            new LedgerEntryCurrency("USD", "USD", line.Debit * 2m, line.Credit * 2m, 1m))).ToArray(),
                    inverseWrite.Entry.Metadata)
                };
                var appendChangedEconomics = () => database.JournalStore.AppendAsync(changedEconomics);
                await appendChangedEconomics.Should().ThrowAsync<LedgerValidationException>()
                    .WithMessage("*every immutable predecessor inverse leg*");
                var wrongBasisWrite = inverseWrite with
                {
                    AccountingBasis = differentBasis,
                    PostingCommand = inverseWrite.PostingCommand! with
                    {
                        BookContext = inverseWrite.PostingCommand.BookContext is { } retainedContext
                            ? retainedContext with { AccountingBasis = differentBasis }
                            : null
                    }
                };
                var wrongBasis = () => database.JournalStore.AppendAsync(wrongBasisWrite);
                await wrongBasis.Should().ThrowAsync<LedgerValidationException>().WithMessage("*root does not match*");
                var missingLineTags = new Dictionary<string, string>(inverseWrite.Entry.Metadata.Tags!);
                var instrumentLine = inverseWrite.Entry.Lines.First(line => line.Account.Symbol is not null);
                missingLineTags.Remove($"securityMaster.line.{instrumentLine.EntryId:D}.provenance");
                var missingLine = inverseWrite with
                {
                    Entry = new JournalEntry(inverseWrite.Entry.JournalEntryId,
                    inverseWrite.Entry.Timestamp, inverseWrite.Entry.Description, inverseWrite.Entry.Lines,
                    inverseWrite.Entry.Metadata with { Tags = missingLineTags })
                };
                var appendMissingLine = () => database.JournalStore.AppendAsync(missingLine);
                await appendMissingLine.Should().ThrowAsync<LedgerValidationException>().WithMessage("*Each opening instrument line*");

                var correctionEvidence = Evidence(postedInverse);
                var correction = (await workbench.ApplyLifecycleActionAsync(new(postedInverse.JournalEntryId, fund, action,
                    "correction-preparer", postedInverse.Version, "Correct the posted current-period inverse.",
                    EvidenceLinks: [correctionEvidence], LedgerBookId: book, TenantId: tenant, CompanyId: company)))
                    .GeneratedJournalEntries.Single();
                var postedCorrection = await ReviewAndPostAsync(correction);
                postedCorrection.AccountingBasis.Should().Be(basis);
                AssertPerLineIdentity(capturingTarget.LastWrite!.Entry);
                var current = await database.JournalStore.GetByPeriodAsync(nextPeriod);
                current.Should().HaveCount(2).And.OnlyContain(row => row.AccountingBasis == basis);
                var balances = current.SelectMany(row => row.Entry.Lines).GroupBy(line => line.Account)
                    .ToDictionary(group => group.Key, group => group.Sum(line => line.Debit - line.Credit));
                foreach (var originalLine in retained.Lines)
                    balances[originalLine.Account].Should().Be(action == JournalEntryLifecycleActionDto.Reverse
                        ? 0m : originalLine.Credit - originalLine.Debit,
                        "a reversal nets the inverse to zero; an unchanged rebook replaces it without counting twice");
                (await database.JournalStore.GetByPeriodAsync(period)).Should().ContainSingle()
                    .Which.Entry.Should().BeEquivalentTo(retained);
                (await database.JournalStore.GetPeriodAsync(period))!.Status.Should().Be("HardClosed");
                (await drafts.GetAsync(fund, id, tenantId: tenant, companyId: company))!.Should()
                    .BeEquivalentTo(posted.JournalEntry, "preparing and correcting a later draft cannot mutate the closed source");

                string Evidence(ManualJournalEntryDraftDto item)
                    => $"evidence://review-correction/tenant/{tenant}/company/{company}/book/{book:D}/journal/{item.JournalEntryId:D}/approval-posting";

                async Task<ManualJournalEntryDraftDto> ReviewAndPostAsync(ManualJournalEntryDraftDto item)
                {
                    var validation = await workbench.ValidateDraftAsync(new(item, item.PreparedBy,
                        LedgerBookId: book, TenantId: tenant, CompanyId: company));
                    validation.ValidationIssues.Should().NotContain(issue => issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);
                    var evidence = Evidence(item);
                    var submit = await workbench.SubmitApprovalAsync(new(item.JournalEntryId, fund, item.PreparedBy, item.Version,
                        Notes: "Submit the source-linked correction.", EvidenceLinks: [evidence],
                        LedgerBookId: book, TenantId: tenant, CompanyId: company));
                    var approve = await workbench.ApplyLifecycleActionAsync(new(item.JournalEntryId, fund,
                        JournalEntryLifecycleActionDto.Approve, "independent-controller", submit.Version,
                        Notes: "Review each native-currency source leg.", EvidenceLinks: [evidence],
                        LedgerBookId: book, TenantId: tenant, CompanyId: company));
                    return (await workbench.ApplyLifecycleActionAsync(new(item.JournalEntryId, fund,
                        JournalEntryLifecycleActionDto.Post, "independent-controller", approve.JournalEntry.Version,
                        Notes: "Post the independently approved correction.", EvidenceLinks: [evidence],
                        LedgerBookId: book, TenantId: tenant, CompanyId: company))).JournalEntry;
                }

                void AssertPerLineIdentity(JournalEntry journal)
                {
                    journal.Metadata.SecurityId.Should().BeNull();
                    journal.Metadata.Symbol.Should().BeNull();
                    journal.Metadata.Tags!["securityMaster.lineageMode"].Should().Be("per-line");
                    foreach (var line in journal.Lines.Where(line => line.Account.Symbol is not null))
                    {
                        var prefix = $"securityMaster.line.{line.EntryId:D}.";
                        journal.Metadata.Tags[prefix + "securityId"].Should().Be(line.Dimensions!.InstrumentId!.Value.ToString("D"));
                        journal.Metadata.Tags[prefix + "provenance"].Should().Contain(line.Account.Symbol == "AAPL" ? "version:3;" : "version:7;");
                        line.Currency!.IsFunctionalCurrency.Should().BeTrue();
                    }
                }
            }
        }
        finally { Directory.Delete(root, true); }
    }

    private static SecurityDetailDto Security(string symbol, DateTimeOffset at)
        => new(Guid.NewGuid(), "Equity", SecurityStatusDto.Active, symbol, "USD",
            JsonSerializer.SerializeToElement(new { }), JsonSerializer.SerializeToElement(new { }),
            [new(SecurityIdentifierKind.Ticker, symbol, true, at.AddYears(-1), null, null)], [], symbol == "AAPL" ? 3 : 7, at.AddYears(-1), null);

    private sealed class CapturingPostingTarget(IGovernedLedgerPostingTarget inner) : IGovernedLedgerPostingTarget
    {
        public LedgerJournalEntryWrite? LastWrite { get; private set; }

        public Task<GovernedLedgerPostingResult> PostAsync(LedgerJournalEntryWrite write, CancellationToken ct = default)
        {
            LastWrite = write;
            return inner.PostAsync(write, ct);
        }
    }
}
