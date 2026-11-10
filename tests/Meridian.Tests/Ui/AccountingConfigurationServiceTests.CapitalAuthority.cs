using Meridian.Contracts.Banking;
using Meridian.Contracts.Ledger;
using Meridian.Ledger;
using Meridian.Ui.Shared.Services;

namespace Meridian.Tests.Ui;

public sealed partial class AccountingConfigurationServiceTests
{
    /// <summary>
    /// Preserve the scenario's treasury/payment identities while admitting its issuance through
    /// the real native factory, retained approved authority and trusted automated intake.
    /// </summary>
    private static async Task<(ManualJournalEntryWorkbenchService Service, ManualJournalEntryDraftDto Saved)>
        IntakeAuthorityBackedCapitalScenarioAsync(AccountingConfigurationService configuration,
            ManualJournalEntryDraftDto original, IBankTransactionSource? bankSource = null)
    {
        const string tenant = "native-capital-test-tenant";
        const string company = "native-capital-test-company";
        const string commitmentId = "native-scenario-commitment";
        var amount = original.Lines.Where(line => line.Side == AccountingTemplateLineSideDto.Debit).Sum(line => line.Amount);
        var credits = original.Lines.Where(line => line.Side == AccountingTemplateLineSideDto.Credit).Sum(line => line.Amount);
        if (amount <= 0m || amount != credits)
            throw new ArgumentException("The synthetic capital scenario must contain positive balanced lines.", nameof(original));
        var context = original.TreasuryContext!;
        var request = new RunCapitalCallIssuanceDraftIntakeRequest(original.FundProfileId, original.Currency,
            original.PreparedBy, $"scenario-{original.JournalEntryId:N}", amount,
            original.AccountingDate, original.AccountingDate.AddDays(7),
            [new(commitmentId, context.CapitalAccountId!, context.InvestorId!, 1000m,
                original.AccountingDate, EvidenceLinks: original.EvidenceLinks)],
            LedgerBookId: original.LedgerBookId, PeriodId: original.PeriodId,
            EntityId: CapitalIntakeAuthorityFixture.EntityId, TenantId: tenant, CompanyId: company);
        var authorityStore = CapitalIntakeAuthorityFixture.Issuance(request);
        var scope = new PrivateCapitalScopeDto(tenant, company, request.EntityId!, request.FundProfileId,
            request.LedgerBookId!.Value, request.Currency);
        var retainedCall = (await authorityStore.GetCallAsync(scope, request.CallId))!;
        var line = retainedCall.Lines.Single();

        // Scope the existing chart explicitly, and add the two native issuance accounts. Receipt
        // accounting remains separate: issuing a call debits receivable rather than inventing cash.
        var workspace = await configuration.GetWorkspaceAsync(original.FundProfileId);
        var nodes = workspace.ChartOfAccounts.Concat(new[]
        {
            new ChartOfAccountsNodeDto("native-call-receivable", "Assets:Capital Call Receivable", "Capital Call Receivable", "Asset"),
            new ChartOfAccountsNodeDto("native-investor-capital", "Equity:Investor Capital", "Investor Capital", "Equity")
        });
        foreach (var node in nodes)
            await configuration.UpsertChartNodeAsync(new(original.FundProfileId, node, original.PreparedBy,
                TenantId: tenant, CompanyId: company));

        var draftStore = new InMemoryManualJournalEntryDraftStore();
        var service = CreateManualJournalEntryWorkbenchService(configuration, bankTransactionSource: bankSource,
            draftStore: draftStore, privateCapitalStore: authorityStore);
        var commitment = new InvestorCommitment(commitmentId, request.FundProfileId, request.LedgerBookId,
            context.CapitalAccountId!, context.InvestorId!, request.Currency, 1000m, original.AccountingDate,
            null, CommitmentStatus.Active);
        var evidence = original.EvidenceLinks.Select((link, index) => new JournalEvidenceReference(
            $"scenario-source-{index}", link, "commitment-register", "synthetic-fixture",
            DateTimeOffset.UtcNow, original.PreparedBy)).ToArray();
        var native = CapitalCallDraftFactory.BuildCapitalCallDraft(commitment, line.InstallmentId,
            amount, original.AccountingDate, DateTimeOffset.UtcNow, evidence,
            context.PaymentIntentId);
        native = native with
        {
            Description = original.Memo,
            Event = native.Event with { IdempotencyKey = context.IdempotencyKey },
            Metadata = native.Metadata with
            {
                IdempotencyKey = context.IdempotencyKey, FundEventId = context.FundEventId,
                SettlementReference = context.SettlementReference
            }
        };
        native = PrivateCapitalPostingAuthority.Bind(native,
            new(scope, retainedCall.CallId, commitmentId, line.CommitmentRevision, retainedCall.PlanHash,
                "Issuance", amount, original.AccountingDate),
            context.InvestorId!, context.CapitalAccountId!);
        var result = await new AutomatedJournalDraftIntakeService(service, draftStore, configuration)
            .IntakeDraftsAsync(new(original.FundProfileId, original.Currency, [native], original.PreparedBy,
                original.LedgerBookId, original.PeriodId, scope.EntityId, tenant, company));
        return (service, result.Created.Single());
    }
}
