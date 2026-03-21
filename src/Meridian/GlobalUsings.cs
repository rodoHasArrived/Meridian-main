// Global using directives for consolidated domain models
// Models and enums are defined once in Contracts project and imported here

global using Meridian.Contracts.Configuration;
global using Meridian.Contracts.Domain.Enums;
global using Meridian.Contracts.Domain.Models;
global using ContractsBboQuotePayload = Meridian.Contracts.Domain.Models.BboQuotePayload;
global using ContractsHistoricalBar = Meridian.Contracts.Domain.Models.HistoricalBar;
global using ContractsIntegrityEvent = Meridian.Contracts.Domain.Models.IntegrityEvent;
global using ContractsLOBSnapshot = Meridian.Contracts.Domain.Models.LOBSnapshot;
global using ContractsOrderBookLevel = Meridian.Contracts.Domain.Models.OrderBookLevel;
global using ContractsOrderFlowStatistics = Meridian.Contracts.Domain.Models.OrderFlowStatistics;
// Type aliases for backwards compatibility during migration
// These allow existing code using Domain.Models to continue working
global using ContractsTrade = Meridian.Contracts.Domain.Models.Trade;
// Type alias to resolve ambiguity between Domain.Events.MarketEvent and Contracts.Domain.Events.MarketEvent
// Domain.Events.MarketEvent is the primary type used throughout the application
global using MarketEvent = Meridian.Domain.Events.MarketEvent;
global using MarketEventPayload = Meridian.Domain.Events.MarketEventPayload;
// Expose internal classes to test assembly for unit testing
using System.Runtime.CompilerServices;
[assembly: InternalsVisibleTo("Meridian.Tests")]
