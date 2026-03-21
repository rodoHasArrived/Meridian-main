using Meridian.Ui.Shared.Endpoints;

// ═══════════════════════════════════════════════════════════════════════════
// Meridian.Ui - Thin Web Dashboard Host
// ═══════════════════════════════════════════════════════════════════════════
// This is a minimal host shell that uses the shared UI module.
// All endpoint logic, services, and templates are in Meridian.Ui.Shared.
// Uses the consolidated BuildUiHost() helper for single-entry-point setup.
// ═══════════════════════════════════════════════════════════════════════════

var app = WebApplication.CreateBuilder(args).BuildUiHost();

app.Run();
