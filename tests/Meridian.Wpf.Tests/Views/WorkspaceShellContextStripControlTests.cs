using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Meridian.Wpf.Models;
using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.Views;

namespace Meridian.Wpf.Tests.Views;

public sealed class WorkspaceShellContextStripControlTests
{
    [Fact]
    public void ResolveAttentionBadge_ShouldPrioritizeCriticalDangerBeforeWarnings()
    {
        var shellContext = new WorkspaceShellContext
        {
            Badges =
            [
                new WorkspaceShellBadge
                {
                    Label = "Alerts",
                    Value = "3 unread",
                    Tone = WorkspaceTone.Warning
                },
                new WorkspaceShellBadge
                {
                    Label = "Critical",
                    Value = "Broker sync offline",
                    Tone = WorkspaceTone.Danger
                },
                new WorkspaceShellBadge
                {
                    Label = "Freshness",
                    Value = "stale provider feed",
                    Tone = WorkspaceTone.Warning
                }
            ]
        };

        var attentionBadge = WorkspaceShellContextStripControl.ResolveAttentionBadge(shellContext);

        attentionBadge.Should().NotBeNull();
        attentionBadge!.Label.Should().Be("Critical");
        WorkspaceShellContextStripControl.BuildAttentionTitle(attentionBadge).Should().Be("Action required");
        WorkspaceShellContextStripControl.BuildAttentionDetail(attentionBadge).Should().Be("Critical: Broker sync offline");
    }

    [Fact]
    public void Control_ShouldShowAttentionBannerWhenWarningSignalsExist()
    {
        WpfTestThread.Run(() =>
        {
            RunMatUiAutomationFacade.EnsureApplicationResources();

            var control = new WorkspaceShellContextStripControl
            {
                Width = 960,
                ShellContext = new WorkspaceShellContext
                {
                    WorkspaceTitle = "Trading",
                    WorkspaceSubtitle = "Shell",
                    Badges =
                    [
                        new WorkspaceShellBadge
                        {
                            Label = "Environment",
                            Value = "Offline",
                            Tone = WorkspaceTone.Danger
                        }
                    ]
                }
            };

            var window = CreateHostWindow(control);
            try
            {
                window.Show();
                window.UpdateLayout();
                control.UpdateLayout();

                var banner = control.FindName("AttentionBanner").Should().BeOfType<Border>().Subject;
                var title = control.FindName("AttentionTitleText").Should().BeOfType<TextBlock>().Subject;
                var detail = control.FindName("AttentionDetailText").Should().BeOfType<TextBlock>().Subject;

                banner.Visibility.Should().Be(Visibility.Visible);
                AutomationProperties.GetAutomationId(banner).Should().Be("WorkspaceContextAttentionBanner");
                title.Text.Should().Be("Action required");
                detail.Text.Should().Be("Environment: Offline");
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void Control_ShouldHideAttentionBannerWhenSignalsAreHealthy()
    {
        WpfTestThread.Run(() =>
        {
            RunMatUiAutomationFacade.EnsureApplicationResources();

            var control = new WorkspaceShellContextStripControl
            {
                Width = 960,
                ShellContext = new WorkspaceShellContext
                {
                    WorkspaceTitle = "Research",
                    WorkspaceSubtitle = "Shell",
                    Badges =
                    [
                        new WorkspaceShellBadge
                        {
                            Label = "Environment",
                            Value = "Live",
                            Tone = WorkspaceTone.Success
                        },
                        new WorkspaceShellBadge
                        {
                            Label = "Alerts",
                            Value = "No recent alerts",
                            Tone = WorkspaceTone.Neutral
                        }
                    ]
                }
            };

            var window = CreateHostWindow(control);
            try
            {
                window.Show();
                window.UpdateLayout();
                control.UpdateLayout();

                var banner = control.FindName("AttentionBanner").Should().BeOfType<Border>().Subject;
                banner.Visibility.Should().Be(Visibility.Collapsed);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void Control_ShouldNotTreatDemoEnvironmentAsAttention()
    {
        WpfTestThread.Run(() =>
        {
            RunMatUiAutomationFacade.EnsureApplicationResources();

            var control = new WorkspaceShellContextStripControl
            {
                Width = 960,
                ShellContext = new WorkspaceShellContext
                {
                    WorkspaceTitle = "Data Operations",
                    WorkspaceSubtitle = "Shell",
                    Badges =
                    [
                        new WorkspaceShellBadge
                        {
                            Label = "Environment",
                            Value = "Demo data",
                            Tone = WorkspaceTone.Info
                        }
                    ]
                }
            };

            var window = CreateHostWindow(control);
            try
            {
                window.Show();
                window.UpdateLayout();
                control.UpdateLayout();

                var banner = control.FindName("AttentionBanner").Should().BeOfType<Border>().Subject;
                banner.Visibility.Should().Be(Visibility.Collapsed);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void Control_ShouldKeepTitleReadableWhenManyBadgesWrap()
    {
        WpfTestThread.Run(() =>
        {
            RunMatUiAutomationFacade.EnsureApplicationResources();

            var control = new WorkspaceShellContextStripControl
            {
                Width = 960,
                ShellContext = new WorkspaceShellContext
                {
                    WorkspaceTitle = "Provider Health",
                    WorkspaceSubtitle = "Data Operations provider workflow with shared environment, freshness, and alert context.",
                    Badges =
                    [
                        new WorkspaceShellBadge { Label = "Workspace", Value = "Data Operations", Tone = WorkspaceTone.Info },
                        new WorkspaceShellBadge { Label = "Scope", Value = "Fund", Tone = WorkspaceTone.Neutral },
                        new WorkspaceShellBadge { Label = "Environment", Value = "Demo data", Tone = WorkspaceTone.Info },
                        new WorkspaceShellBadge { Label = "As Of", Value = "Apr 27 2026 09:15", Tone = WorkspaceTone.Neutral },
                        new WorkspaceShellBadge { Label = "Freshness", Value = "Provider workflow active", Tone = WorkspaceTone.Neutral },
                        new WorkspaceShellBadge { Label = "Reachability", Value = "Secondary", Tone = WorkspaceTone.Neutral },
                        new WorkspaceShellBadge { Label = "Related", Value = "4 routes", Tone = WorkspaceTone.Info },
                        new WorkspaceShellBadge { Label = "Alerts", Value = "No recent alerts", Tone = WorkspaceTone.Neutral }
                    ]
                }
            };

            var window = CreateHostWindow(control);
            try
            {
                window.Width = 980;
                window.Height = 260;
                window.Show();
                window.UpdateLayout();
                control.UpdateLayout();

                var title = control.FindName("WorkspaceContextTitleText").Should().BeOfType<TextBlock>().Subject;
                var badges = control.FindName("ContextBadgeItemsControl").Should().BeOfType<ItemsControl>().Subject;

                title.Text.Should().Be("Provider Health");
                title.ActualWidth.Should().BeGreaterThan(600);
                badges.ActualWidth.Should().BeGreaterThan(600);
                badges.TranslatePoint(new Point(0, 0), control).Y.Should().BeGreaterThan(title.TranslatePoint(new Point(0, 0), control).Y);
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static Window CreateHostWindow(FrameworkElement content)
    {
        return new Window
        {
            Width = 980,
            Height = 220,
            Content = content,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Opacity = 0
        };
    }
}
