// Global using directives for Core layer (cross-cutting concerns)
global using Meridian.Contracts.Configuration;
global using Meridian.Contracts.Domain.Enums;
global using Meridian.Contracts.Domain.Models;
// Type aliases used by serialization context
global using MarketEvent = Meridian.Domain.Events.MarketEvent;
global using MarketEventPayload = Meridian.Domain.Events.MarketEventPayload;
// Expose internal classes to test assembly for unit testing
using System.Runtime.CompilerServices;
[assembly: InternalsVisibleTo("Meridian.Tests")]
[assembly: InternalsVisibleTo("Meridian.Benchmarks")]
