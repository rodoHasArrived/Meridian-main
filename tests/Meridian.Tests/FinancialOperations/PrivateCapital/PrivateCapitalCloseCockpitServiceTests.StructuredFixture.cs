using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.Intercompany;
using Meridian.FinancialOperations.OperationsContinuity;
using Meridian.FinancialOperations.PrivateCapital;
using Meridian.Tests.FinancialOperations.Intercompany;

namespace Meridian.Tests.FinancialOperations.PrivateCapital;

public sealed partial class PrivateCapitalCloseCockpitServiceTests
{
    private static PrivateCapitalCloseCockpitService CreateSourceBackedCockpitService(
        IManualJournalEntryWorkbenchService? manualJournalEntryWorkbenchService,
        IOperationsContinuityWorkflowService? operationsContinuityWorkflowService,
        IDailyValuationScheduleStatusSource? dailyValuationScheduleStatusSource = null,
        IAutomatedJournalScheduleStatusSource? automatedJournalScheduleStatusSource = null,
        IRecurringJournalQueueSource? recurringJournalQueueSource = null)
        => new(manualJournalEntryWorkbenchService, operationsContinuityWorkflowService, dailyValuationScheduleStatusSource,
            automatedJournalScheduleStatusSource, recurringJournalQueueSource,
            manualJournalEntryWorkbenchService is not null && operationsContinuityWorkflowService is not null
                ? new PostedFixtureSchedule() : null);

    /// <summary>Supplies explicit test ownership around genuine balanced posted intercompany fixtures.</summary>
    private sealed class PostedFixtureSchedule : IIntercompanyBalanceScheduleSource
    {
        private readonly Dictionary<(string Tenant, string Company, string Fund, Guid Book), IntercompanyBalanceScheduleService> _sources = new();
        public Task<IntercompanyBalanceScheduleDto> GetAsync(string? tenantId, string? companyId, string? fundProfileId,
            Guid? ledgerBookId, DateOnly asOf, CancellationToken ct = default)
            => Source(tenantId, companyId, fundProfileId, ledgerBookId).GetAsync(tenantId ?? "tenant-a", companyId ?? "company-a",
                fundProfileId ?? FundProfileId, ledgerBookId ?? FixtureBook, asOf, ct);
        public Task<IntercompanyBalanceScheduleDto> GetForCloseAsync(string? tenant, string? company, string? fund,
            Guid? book, string? periodId, CancellationToken ct = default)
            => Source(tenant, company, fund, book).GetForCloseAsync(tenant ?? "tenant-a", company ?? "company-a", fund ?? FundProfileId,
                book ?? FixtureBook, periodId, ct);
        private static readonly Guid FixtureBook = Guid.Parse("33333333-3333-3333-3333-333333333333");
        private IntercompanyBalanceScheduleService Source(string? tenant, string? company, string? fund, Guid? book)
        {
            var key = (tenant ?? "tenant-a", company ?? "company-a", fund ?? FundProfileId, book ?? FixtureBook);
            if (!_sources.TryGetValue(key, out var source)) _sources[key] = source = IntercompanyBalanceScheduleTests.ReadySchedule(key.Item3, key.Item4, key.Item1, key.Item2);
            return source;
        }
    }
}
