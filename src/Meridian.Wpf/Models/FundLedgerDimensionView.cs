using System.Collections.Generic;
using Meridian.Contracts.Workstation;

namespace Meridian.Wpf.Models;

public sealed record FundLedgerDimensionView(
    string Key,
    string DisplayName,
    string CoverageText,
    string StatusText,
    int ExpectedScopeCount,
    int LinkedAccountCount,
    int TrialBalanceLineCount,
    int JournalEntryCount,
    bool IsConsolidated,
    bool HasScopedLedgerData,
    IReadOnlySet<string> FinancialAccountIds,
    IReadOnlyList<FundLedgerSliceDto> LedgerSlices);
