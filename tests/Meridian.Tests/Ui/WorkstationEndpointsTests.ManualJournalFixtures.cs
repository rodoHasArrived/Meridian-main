using FluentAssertions;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Tests.Ui;

public sealed partial class WorkstationEndpointsTests
{
    private static ManualJournalEntryDraftDto OrdinaryManualJournalEntryDraft()
        => ManualJournalEntryDraft() with
        {
            EntryType = ManualJournalEntryTypeDto.General,
            TreasuryContext = null
        };

    private static RunCapitalCallIssuanceDraftIntakeRequest CapitalProjectionRequest()
        => new("fund-alpha", "USD", "ops-user", "20260630", 100m,
            new DateOnly(2026, 6, 30), new DateOnly(2026, 6, 30),
            [new("commitment-1", "capital-account:fund-alpha:lp-1", "investor:lp-1", 100m,
                new DateOnly(2025, 1, 1), EvidenceLinks: ["/api/workstation/evidence/subjects/accounting-record/manual-je-endpoint"])],
            LedgerBookId: Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            PeriodId: "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb", EntityId: CapitalIntakeAuthorityFixture.EntityId,
            TenantId: "company-alpha", CompanyId: "company-alpha");

    private static void RegisterCapitalProjectionConfigurationServices(IServiceCollection services)
    {
        RegisterAccountingConfigurationServices(services);
        var request = CapitalProjectionRequest();
        services.AddSingleton<IPrivateCapitalStore>(CapitalIntakeAuthorityFixture.Issuance(request));
        var journalStore = new WritableEndpointLedgerJournalStore();
        var openedAt = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        journalStore.Seed(new LedgerBookRecord(request.LedgerBookId!.Value, request.FundProfileId,
                Guid.Parse(request.EntityId!), FundStructureNodeKindDto.Entity, "Fund Alpha entity book", "USD", openedAt, openedAt),
            new LedgerAccountingPeriod(Guid.Parse(request.PeriodId!), request.LedgerBookId, 2026, 6, "2026-06",
                new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30), "Open", openedAt, null, 1));
        services.AddSingleton<ILedgerJournalStore>(journalStore);
    }

    private static async Task<ManualJournalEntryDraftDto> SeedGovernedCapitalProjectionDraftAsync(IServiceProvider services)
    {
        var request = CapitalProjectionRequest();
        var configuration = services.GetRequiredService<IAccountingConfigurationService>();
        foreach (var account in new[]
        {
            new ChartOfAccountsNodeDto("capital-call-receivable", "Assets:Capital Call Receivable", "Capital Call Receivable", "Asset"),
            new ChartOfAccountsNodeDto("investor-capital", "Equity:Investor Capital", "Investor Capital", "Equity")
        })
            await configuration.UpsertChartNodeAsync(new(request.FundProfileId, account, "ops-user",
                CompanyId: request.CompanyId, LedgerBookId: request.LedgerBookId, TenantId: request.TenantId));
        var workbench = services.GetRequiredService<IManualJournalEntryWorkbenchService>();
        var intake = new AutomatedJournalDraftIntakeService(workbench,
            services.GetRequiredService<IManualJournalEntryDraftStore>(), configuration);
        var runner = new AutomatedJournalIntakeRunner(intake, new FeeScheduleAccrualEventProducer(),
            manualJournalWorkbench: workbench, privateCapitalStore: services.GetRequiredService<IPrivateCapitalStore>());
        var result = await runner.RunCapitalCallIssuanceIntakeAsync(request);
        var retained = result.Intake.Created.Should().ContainSingle().Subject;
        retained.PrivateCapitalAuthorityJson.Should().NotBeNullOrWhiteSpace();
        retained.ValidationIssues.Should().NotContain(issue => issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);
        // The HTTP save remains responsible for preparing descriptive and non-executing
        // payment-reference fields; the server-retained financial authority stays untouched.
        return retained with
        {
            Memo = "Manual close adjustment",
            TreasuryContext = retained.TreasuryContext! with
            {
                PaymentIntentId = "payment:fund-alpha:capital-call:20260630",
                SettlementReference = "settlement:fund-alpha:capital-call:20260630"
            }
        };
    }
}
