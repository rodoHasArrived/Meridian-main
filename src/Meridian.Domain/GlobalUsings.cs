// Global using directives for Domain layer
global using Meridian.Contracts.Configuration;
global using Meridian.Contracts.Domain.Enums;
global using Meridian.Contracts.Domain.Models;
// Expose internal classes to test assembly and Application layer for testing
using System.Runtime.CompilerServices;
[assembly: InternalsVisibleTo("Meridian.Tests")]
[assembly: InternalsVisibleTo("Meridian.Application")]
