using System;
using Meridian.Ui.Services.Services;

namespace Meridian.Wpf.Services;

/// <summary>
/// WPF platform-specific notification service.
/// Extends <see cref="NotificationServiceBase"/> implementing the
/// <see cref="Meridian.Ui.Services.Contracts.INotificationService"/> contract.
/// Part of Phase 2 service extraction.
/// </summary>
public sealed class NotificationService : NotificationServiceBase, Meridian.Ui.Services.Contracts.INotificationService
{
    private static readonly Lazy<NotificationService> _instance = new(() => new NotificationService());

    public static NotificationService Instance => _instance.Value;

    private NotificationService()
    {
    }
}
