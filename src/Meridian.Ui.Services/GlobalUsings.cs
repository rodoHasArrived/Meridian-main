// =============================================================================
// GlobalUsings.cs - Type Aliases and Namespace Imports for Shared UI Services
// =============================================================================
// This file provides global using directives to bring Contracts namespaces into
// scope and type aliases for backwards compatibility with existing desktop code.
// =============================================================================

// Import all Contracts namespaces globally so types are available throughout the library
global using Meridian.Contracts.Api;
global using Meridian.Contracts.Archive;
global using Meridian.Contracts.Backfill;
global using Meridian.Contracts.Credentials;
global using Meridian.Contracts.Export;
global using Meridian.Contracts.Manifest;
global using Meridian.Contracts.Pipeline;
global using Meridian.Contracts.Schema;
global using Meridian.Contracts.Session;

// Domain namespaces (Models, Events, Enums)
global using Meridian.Contracts.Domain;
global using Meridian.Contracts.Domain.Models;
global using Meridian.Contracts.Domain.Events;
global using Meridian.Contracts.Domain.Enums;

// Configuration type aliases (Dto suffix -> non-Dto names for backwards compatibility)
global using AppConfig = Meridian.Contracts.Configuration.AppConfigDto;
global using AlpacaOptions = Meridian.Contracts.Configuration.AlpacaOptionsDto;
global using StorageConfig = Meridian.Contracts.Configuration.StorageConfigDto;
global using SymbolConfig = Meridian.Contracts.Configuration.SymbolConfigDto;
global using BackfillConfig = Meridian.Contracts.Configuration.BackfillConfigDto;
global using DataSourcesConfig = Meridian.Contracts.Configuration.DataSourcesConfigDto;
global using DataSourceConfig = Meridian.Contracts.Configuration.DataSourceConfigDto;
global using PolygonOptions = Meridian.Contracts.Configuration.PolygonOptionsDto;
global using IBOptions = Meridian.Contracts.Configuration.IBOptionsDto;
global using SymbolGroupsConfig = Meridian.Contracts.Configuration.SymbolGroupsConfigDto;
global using SymbolGroup = Meridian.Contracts.Configuration.SymbolGroupDto;
global using SmartGroupCriteria = Meridian.Contracts.Configuration.SmartGroupCriteriaDto;
global using ExtendedSymbolConfig = Meridian.Contracts.Configuration.ExtendedSymbolConfigDto;
global using AppSettings = Meridian.Contracts.Configuration.AppSettingsDto;
global using DerivativesConfig = Meridian.Contracts.Configuration.DerivativesConfigDto;
global using IndexOptionsConfig = Meridian.Contracts.Configuration.IndexOptionsConfigDto;
