using System.Collections.ObjectModel;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Shared.Services;
using Meridian.Wpf.Models;

namespace Meridian.Wpf.ViewModels;

internal sealed class FundLedgerCollectionsSectionViewModel
{
    public ObservableCollection<FundTrialBalanceLine> TrialBalance { get; } = [];
    public ObservableCollection<FundJournalLine> Journal { get; } = [];
    public ObservableCollection<FundAccountSummary> Accounts { get; } = [];
    public ObservableCollection<BankAccountSnapshot> BankSnapshots { get; } = [];
    public ObservableCollection<FundPortfolioPosition> PortfolioPositions { get; } = [];
    public ObservableCollection<CashFlowEntryDto> CashFlowEntries { get; } = [];
    public ObservableCollection<CashLadderBucketDto> CashFlowBuckets { get; } = [];
    public ObservableCollection<FundTrialBalanceLine> VisibleTrialBalance { get; } = [];
    public ObservableCollection<FundJournalLine> VisibleJournal { get; } = [];
    public ObservableCollection<FundLedgerDimensionView> LedgerDimensions { get; } = [];
    public ObservableCollection<FundReconciliationItem> ReconciliationRuns { get; } = [];
    public ObservableCollection<string> CashFinancingHighlights { get; } = [];
    public ObservableCollection<FundAuditEntry> AuditTrail { get; } = [];
    public ObservableCollection<FundReportAssetClassSectionDto> ReportPackAssetSections { get; } = [];
}
