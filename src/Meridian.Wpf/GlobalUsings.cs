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

// Base framework namespaces. We keep these explicit so WPF types stay
// preferred even though the project also enables Windows Forms support
// for NotifyIcon integration.
global using System;
global using System.Collections.Generic;
global using System.Collections.ObjectModel;
global using System.Globalization;
global using System.IO;
global using System.Linq;
global using System.Net.Http;
global using System.Threading;
global using System.Threading.Tasks;
global using System.Windows;
global using System.Windows.Controls;
global using System.Windows.Input;
global using System.Windows.Media;

// Shared desktop collections and contracts
global using Meridian.Ui.Services.Collections;
global using Meridian.Ui.Services.Contracts;
