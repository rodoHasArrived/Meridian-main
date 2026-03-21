global using System;
global using System.Collections.Generic;
global using System.Linq;
global using System.Runtime.CompilerServices;
global using System.Threading;
global using System.Threading.Tasks;
global using Meridian.Backtesting.Sdk;
global using Meridian.Contracts.Domain.Models;
global using Meridian.Ledger;
global using Microsoft.Extensions.Logging;
// Preserve the BacktestLedger name used throughout the Engine project.
global using BacktestLedger = Meridian.Ledger.Ledger;
[assembly: InternalsVisibleTo("Meridian.Backtesting.Tests")]
