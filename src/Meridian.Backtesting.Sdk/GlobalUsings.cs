global using System;
global using System.Collections.Generic;
global using System.Threading;
global using System.Threading.Tasks;
global using Meridian.Contracts.Domain.Events;
global using Meridian.Contracts.Domain.Models;
global using Meridian.Ledger;
global using BacktestJournalEntry = Meridian.Ledger.JournalEntry;
global using BacktestJournalEntryMetadata = Meridian.Ledger.JournalEntryMetadata;
// Type aliases for backward compatibility with backtesting SDK namespace conventions
global using BacktestLedger = Meridian.Ledger.Ledger;
global using BacktestLedgerAccount = Meridian.Ledger.LedgerAccount;
global using BacktestLedgerAccounts = Meridian.Ledger.LedgerAccounts;
global using BacktestLedgerAccountType = Meridian.Ledger.LedgerAccountType;
global using BacktestLedgerEntry = Meridian.Ledger.LedgerEntry;
