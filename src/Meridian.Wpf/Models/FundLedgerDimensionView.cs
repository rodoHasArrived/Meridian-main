using System.Collections.Generic;
using Meridian.Contracts.Workstation;

namespace Meridian.Wpf.Models;

public sealed record FundLedgerDimensionView(
    string Key,
    string DisplayName,
    string CoverageText,
    string StatusText,
    int ExpectedScopeCount,
    int MaterializedScopeCount,
    int LinkedAccountCount,
    int TrialBalanceLineCount,
    int JournalEntryCount,
    FundLedgerTotalsDto Totals,
    bool IsConsolidated,
    bool HasScopedLedgerData,
    IReadOnlyList<FundLedgerSliceDto> LedgerSlices);
