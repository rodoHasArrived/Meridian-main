// =============================================================================
// TypeForwards.cs - Phase 2 type forwarding
// =============================================================================
// Re-exports types that were extracted from WPF services into shared base classes
// in Meridian.Ui.Services.Services namespace.
// This ensures existing WPF view code referencing WpfServices.NotificationType,
// WpfServices.LogLevel, etc. continues to resolve without per-file changes.
// =============================================================================

// Notification types (NotificationType is in the root Ui.Services namespace;
// the rest are in Ui.Services.Services from NotificationServiceBase)
global using NotificationType = Meridian.Ui.Services.NotificationType;
global using NotificationSettings = Meridian.Ui.Services.Services.NotificationSettings;
global using NotificationHistoryItem = Meridian.Ui.Services.Services.NotificationHistoryItem;
global using NotificationEventArgs = Meridian.Ui.Services.Services.NotificationEventArgs;

// Logging types (from LoggingServiceBase)
global using LogLevel = Meridian.Ui.Services.Services.LogLevel;
global using LogEntryEventArgs = Meridian.Ui.Services.Services.LogEntryEventArgs;

// Backend service types (from BackendServiceManagerBase)
global using BackendServiceStatus = Meridian.Ui.Services.Services.BackendServiceStatus;
global using BackendServiceOperationResult = Meridian.Ui.Services.Services.BackendServiceOperationResult;
global using BackendInstallationInfo = Meridian.Ui.Services.Services.BackendInstallationInfo;
global using BackendRuntimeInfo = Meridian.Ui.Services.Services.BackendRuntimeInfo;

// Status types (from StatusServiceBase)
global using StatusChangedEventArgs = Meridian.Ui.Services.Services.StatusChangedEventArgs;
global using LiveStatusEventArgs = Meridian.Ui.Services.Services.LiveStatusEventArgs;
global using SimpleStatus = Meridian.Ui.Services.Services.SimpleStatus;
global using StatusProviderInfo = Meridian.Ui.Services.Services.StatusProviderInfo;
