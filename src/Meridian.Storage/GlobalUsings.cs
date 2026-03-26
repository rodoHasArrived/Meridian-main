// Global using directives for Storage layer
global using Meridian.Contracts.Configuration;
global using Meridian.Contracts.Domain.Enums;
global using Meridian.Contracts.Domain.Models;
global using ContractsHistoricalBar = Meridian.Contracts.Domain.Models.HistoricalBar;
// Backwards compatibility aliases
global using ContractsTrade = Meridian.Contracts.Domain.Models.Trade;
// Type aliases - Domain.Events.MarketEvent is the primary type
global using MarketEvent = Meridian.Domain.Events.MarketEvent;
global using MarketEventPayload = Meridian.Domain.Events.MarketEventPayload;
// Expose internal classes to test assembly for unit testing
using System.Runtime.CompilerServices;
[assembly: InternalsVisibleTo("Meridian.Tests")]
[assembly: InternalsVisibleTo("Meridian.Benchmarks")]
