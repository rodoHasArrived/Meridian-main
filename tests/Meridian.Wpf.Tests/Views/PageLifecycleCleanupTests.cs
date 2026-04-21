using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using FluentAssertions;
using Meridian.Contracts.Services;
using Meridian.Wpf.Services;
using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.Views;

namespace Meridian.Wpf.Tests.Views;

public sealed class PageLifecycleCleanupTests
{
    private sealed class TestConnectivityProbeService : IConnectivityProbeService
    {
        private EventHandler<bool>? _connectivityChanged;

        public bool IsOnline => true;

        public int SubscriberCount => _connectivityChanged?.GetInvocationList().Length ?? 0;

        public event EventHandler<bool>? ConnectivityChanged
        {
            add => _connectivityChanged += value;
            remove => _connectivityChanged -= value;
        }
    }

    [Fact]
    public void DiagnosticsPage_Unloaded_RemovesConnectivitySubscriptions()
    {
        WpfTestThread.Run(() =>
        {
            RunMatUiAutomationFacade.EnsureApplicationResources();

            var probe = new TestConnectivityProbeService();
            var page = new DiagnosticsPage(NavigationService.Instance, NotificationService.Instance, probe);

            probe.SubscriberCount.Should().Be(1);

            page.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));

            probe.SubscriberCount.Should().Be(0);
        });
    }

    [Fact]
    public void ArchiveHealthPage_Unloaded_RemovesArchiveHealthEventSubscriptions()
    {
        WpfTestThread.Run(() =>
        {
            var healthService = ArchiveHealthService.Instance;
            var healthUpdatedField = typeof(ArchiveHealthService).GetField("HealthStatusUpdated", BindingFlags.Instance | BindingFlags.NonPublic);
            var verificationCompletedField = typeof(ArchiveHealthService).GetField("VerificationCompleted", BindingFlags.Instance | BindingFlags.NonPublic);
            var pageType = typeof(ArchiveHealthPage);
            var healthServiceField = pageType.GetField("_healthService", BindingFlags.Instance | BindingFlags.NonPublic);
            var subscribedField = pageType.GetField("_healthEventsSubscribed", BindingFlags.Instance | BindingFlags.NonPublic);
            var subscribeMethod = pageType.GetMethod("SubscribeToHealthEvents", BindingFlags.Instance | BindingFlags.NonPublic);
            var unloadedMethod = pageType.GetMethod("Page_Unloaded", BindingFlags.Instance | BindingFlags.NonPublic);

            healthUpdatedField.Should().NotBeNull();
            verificationCompletedField.Should().NotBeNull();
            healthServiceField.Should().NotBeNull();
            subscribedField.Should().NotBeNull();
            subscribeMethod.Should().NotBeNull();
            unloadedMethod.Should().NotBeNull();

            var originalHealthUpdated = healthUpdatedField!.GetValue(healthService);
            var originalVerificationCompleted = verificationCompletedField!.GetValue(healthService);

            try
            {
                var initialHealthUpdatedCount = GetHandlerCount(healthUpdatedField, healthService);
                var initialVerificationCompletedCount = GetHandlerCount(verificationCompletedField, healthService);
                var page = (ArchiveHealthPage)RuntimeHelpers.GetUninitializedObject(pageType);
                healthServiceField!.SetValue(page, healthService);
                subscribedField!.SetValue(page, false);
                subscribeMethod!.Invoke(page, []);

                GetHandlerCount(healthUpdatedField, healthService).Should().Be(initialHealthUpdatedCount + 1);
                GetHandlerCount(verificationCompletedField, healthService).Should().Be(initialVerificationCompletedCount + 1);

                unloadedMethod!.Invoke(page, [page, new RoutedEventArgs(FrameworkElement.UnloadedEvent)]);

                GetHandlerCount(healthUpdatedField, healthService).Should().Be(initialHealthUpdatedCount);
                GetHandlerCount(verificationCompletedField, healthService).Should().Be(initialVerificationCompletedCount);
            }
            finally
            {
                healthUpdatedField.SetValue(healthService, originalHealthUpdated);
                verificationCompletedField.SetValue(healthService, originalVerificationCompleted);
            }
        });
    }

    private static int GetHandlerCount(FieldInfo field, object target)
    {
        return (field.GetValue(target) as MulticastDelegate)?.GetInvocationList().Length ?? 0;
    }
}
