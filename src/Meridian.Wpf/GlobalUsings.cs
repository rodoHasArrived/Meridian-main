// =============================================================================
// GlobalUsings.cs - WPF Project Namespace Imports
// =============================================================================
// Imports shared library namespaces so collections and contracts
// from Meridian.Ui.Services are available throughout this project.
// 
// NOTE: Type aliases and Contracts namespaces are NOT re-defined here because
// they are already provided by the referenced Meridian.Ui.Services
// project (via its GlobalUsings.cs). Re-defining them would cause CS0101
// duplicate type definition errors.
//
// IMPORTANT: We do NOT import Meridian.Ui.Services.Services globally
// because it conflicts with WPF-specific services in Meridian.Wpf.Services.
// Files that need shared services should import them explicitly.
// =============================================================================

// Shared desktop collections and contracts
global using Meridian.Ui.Services.Collections;
global using Meridian.Ui.Services.Contracts;
