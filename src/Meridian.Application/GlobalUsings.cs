// Global using directives for Application layer
global using Meridian.Contracts.Configuration;
global using Meridian.Contracts.Domain.Enums;
global using Meridian.Contracts.Domain.Models;
global using ContractsBboQuotePayload = Meridian.Contracts.Domain.Models.BboQuotePayload;
global using ContractsHistoricalBar = Meridian.Contracts.Domain.Models.HistoricalBar;
global using ContractsIntegrityEvent = Meridian.Contracts.Domain.Models.IntegrityEvent;
global using ContractsLOBSnapshot = Meridian.Contracts.Domain.Models.LOBSnapshot;
global using ContractsOrderBookLevel = Meridian.Contracts.Domain.Models.OrderBookLevel;
global using ContractsOrderFlowStatistics = Meridian.Contracts.Domain.Models.OrderFlowStatistics;
// Backwards compatibility aliases
global using ContractsTrade = Meridian.Contracts.Domain.Models.Trade;
// Type aliases - Domain.Events.MarketEvent is the primary type
global using MarketEvent = Meridian.Domain.Events.MarketEvent;
global using MarketEventPayload = Meridian.Domain.Events.MarketEventPayload;
// Expose internal classes to test assembly and main entry point for unit testing
using System.Runtime.CompilerServices;
[assembly: InternalsVisibleTo("Meridian.Tests")]
[assembly: InternalsVisibleTo("Meridian")]
