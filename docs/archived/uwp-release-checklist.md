# UWP Desktop Release Checklist (Desktop-Ready Scope)

> **Archived:** The UWP desktop application was deprecated and removed. The WPF desktop app (`Meridian.Wpf`) is now the sole desktop client. All must-ship items below have been implemented in the WPF app. See [UWP to WPF Migration](uwp-to-wpf-migration.md) for migration details.

This checklist defined the minimal **desktop-ready** scope for the original UWP app release. The acceptance criteria below were carried forward to the WPF implementation.

## Desktop-Ready Scope

### Must-Ship — All Implemented in WPF

| Area | Acceptance Criteria | WPF Implementation |
| --- | --- | --- |
| **Notification handling** | - Connection loss, reconnect attempts, and recovery trigger notifications.<br>- Backfill completion and data gap warnings trigger notifications when enabled in settings.<br>- Notifications are logged in history for review. | [NotificationService](../../src/Meridian.Wpf/Services/NotificationService.cs), [NotificationCenterPage](../../src/Meridian.Wpf/Views/NotificationCenterPage.xaml.cs) |
| **Reconnection behavior** | - Automatic reconnection with exponential backoff is enabled and configurable (max attempts, base delay).<br>- Reconnection attempts and outcomes update UI status and activity feed.<br>- Manual pause/resume of auto-reconnect is supported for maintenance windows. | [ConnectionService](../../src/Meridian.Wpf/Services/ConnectionService.cs), [MainPage](../../src/Meridian.Wpf/Views/MainPage.xaml.cs) |
| **Integrity visibility** | - Dashboard displays integrity counters and recent integrity events.<br>- Users can expand/collapse integrity details, acknowledge alerts, and export an integrity report.<br>- Integrity events are recorded with severity and surfaced in the notification center. | [DashboardPage](../../src/Meridian.Wpf/Views/DashboardPage.xaml), [IntegrityEventsService](../../src/Meridian.Ui.Services/Services/IntegrityEventsService.cs) |

### Post-Ship

| Area | Acceptance Criteria | Status |
| --- | --- | --- |
| **System tray + advanced notification routing** | - System tray icon supports quick actions and notification badges.<br>- Deep links provide direct navigation to remediation pages. | Future enhancement for WPF app |
| **Expanded archive health reporting** | - Archive health page supports scheduled full verification and trend reporting across multiple sessions.<br>- Exportable compliance report includes integrity summary and verification metadata. | Implemented: [ArchiveHealthPage](../../src/Meridian.Wpf/Views/ArchiveHealthPage.xaml) |

## Release Notes Gate

A release is considered **desktop-ready** only when all **must-ship** items meet their acceptance criteria. Post-ship items are tracked in the refinement backlog and can be scheduled independently.
