// ARCHIVE TOMBSTONE — src/Meridian.McpServer
//
// This module was retired on 2026-05-23 and is no longer part of the active build.
//
// Reason: The McpServer exposed Meridian market-data operations (backfill, symbol
// management, storage queries, provider status) via the Model Context Protocol,
// allowing AI agents to drive live data operations. Following a policy decision
// to limit AI-facing code strictly to developer-assistance tooling, this operational
// MCP server was removed. Developer-facing MCP tooling remains active in
// src/Meridian.Mcp/.
//
// What was here:
//   Tools/BackfillTools.cs         — MCP tool for triggering historical backfills
//   Tools/StorageTools.cs          — MCP tool for querying local market-data storage
//   Tools/SymbolTools.cs           — MCP tool for adding/removing tracked symbols
//   Tools/ProviderTools.cs         — MCP tool for provider health and status
//   Tools/RepoNavigationTools.cs   — MCP tool for codebase navigation queries
//   Resources/MarketDataResources.cs      — MCP resources exposing market-data surfaces
//   Resources/RepoNavigationResources.cs  — MCP resources exposing repo-navigation data
//   Prompts/MarketDataPrompts.cs   — MCP prompts for market-data workflows
//   Navigation/RepoNavigationCatalog.cs   — repo-navigation catalog loader
//   Program.cs                     — MCP server host entry point
//
// Corresponding tests were removed from tests/Meridian.McpServer.Tests/.
//
// Related active module: src/Meridian.Mcp/ (SRC-MCP — developer-assistance MCP host)
// Module ID that was retired: SRC-MCP-SERVER
