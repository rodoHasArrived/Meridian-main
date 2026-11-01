using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Text.Json;
using CommunityToolkit.Mvvm.Input;
using Meridian.Contracts.Api;
using Meridian.Contracts.Calendars;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.SecurityMaster;
using Meridian.Instruments.FixedIncome;
using Meridian.Wpf.Services;
using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.ViewModels;
using Meridian.Wpf.ViewModels.SecurityMaster;
using Meridian.Wpf.Views;

namespace Meridian.Wpf.Tests.Views;

/// <summary>Exercises the actual editor controls and service calculation using retained synthetic SOFR inputs.</summary>
[Collection("NavigationServiceSerialCollection")]
public sealed class BondAnalyticsFloatingViewTests
{
    private static readonly Guid SecurityId = Guid.Parse("5d1e7c3a-9b2f-4e6a-8c1d-3f4a5b6c7d8e");
    private static readonly DateTimeOffset AsOf = new(2026, 2, 15, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(null)]
    [InlineData("MissingFixing")]
    [InlineData("ForwardCurveRequired")]
    public void Editor_SelectsV3AndShowsCalculatedSofrPaymentsOrExplicitRefusals(string? refusal)
    {
        WpfTestThread.Run(async () =>
        {
            var retainFixing = true;
            var retainCurve = true;
            var query = new Mock<ISecurityMasterQueryService>();
            query.Setup(value => value.GetByIdAsync(SecurityId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => Floating(retainFixing));
            var curves = new Mock<ISecurityForwardCurveProvider>();
            curves.Setup(value => value.GetForwardCurveAsync("SOFR", It.IsAny<BondCalculationContext>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => retainCurve ? new SecurityForwardCurve("SOFR", new DateOnly(2026, 1, 1),
                    [new(new DateOnly(2026, 1, 1), 0.02m), new(new DateOnly(2029, 1, 1), 0.02m)], Source: "synthetic-sofr-forward") : null);
            var service = new BondAnalyticsService(query.Object,
                new StaticBusinessDayCalendarSource([BusinessDayCalendars.WeekendsOnly]), forwardCurves: curves.Object);
            var client = new Mock<IWorkstationSecurityMasterApiClient>();
            var requests = new List<BondAnalyticsRequest>();
            client.Setup(value => value.GetBondAnalyticsAsync(It.IsAny<BondAnalyticsRequest>(), It.IsAny<CancellationToken>()))
                .Returns(async (BondAnalyticsRequest request, CancellationToken token) =>
                {
                    requests.Add(request);
                    return ApiResponse<BondAnalyticsResult>.Ok((await service.CalculateAsync(request, token))!);
                });
            var editor = new SecurityPassportEditorViewModel(client.Object)
            {
                Parameter = new SecurityPassportEditorParameter(SecurityId, 7, "Synthetic SOFR + 100 bp", "Bond")
            };
            var view = new SecurityPassportEditorView { DataContext = editor };
            var window = new Window { Width = 1440, Height = 900, Content = view };
            try
            {
                window.Show();
                window.UpdateLayout();

                var selector = Named<ComboBox>(view, "Bond calculation version");
                selector.SelectedValue.Should().Be("bond-analytics-v2");
                selector.SelectedValue = "bond-analytics-v3";
                Named<DatePicker>(view, "Calculation date").SelectedDate = AsOf.UtcDateTime;
                Named<DatePicker>(view, "Settlement date").SelectedDate = AsOf.UtcDateTime;
                Descendants<CheckBox>(view).Single(control => Equals(control.Content, "Price from annual yield instead of clean price")).IsChecked = true;
                var valueInput = Named<TextBox>(view, "Price or yield");
                valueInput.Text = "0.03";
                var calculate = Descendants<Button>(view).Single(control => Equals(control.Content, "Calculate bond analytics"));
                var analytics = editor.BondAnalytics!;

                await ClickAsync(calculate, analytics.CalculateCommand);
                window.UpdateLayout();

                requests.Should().ContainSingle().Which.CalculationVersion.Should().Be("bond-analytics-v3");
                analytics.HasFloatingResult.Should().BeTrue(string.Join("; ", analytics.Blockers));
                analytics.ProvenanceText.Should().Contain("bond-analytics-v3").And.Contain("USD");
                analytics.ModelDescription.Should().Contain("not a discount margin");
                var metrics = Named<ItemsControl>(view, "Bond analytics metrics");
                metrics.Items.Cast<BondMetricRow>().Should().Contain(row => row.Label == "Projected-flow yield (annual fraction)" && row.Value == "0.03");
                metrics.Items.Cast<BondMetricRow>().Should().Contain(row => row.Label == "Accrued interest / 100" && row.Value == "0.333333333333");
                metrics.Items.Cast<BondMetricRow>().Should().Contain(row => row.Label.StartsWith("DV01") && Number(row.Value) > 0m);
                metrics.Items.Cast<BondMetricRow>().Should().Contain(row => row.Label.StartsWith("SpreadDv01") && Number(row.Value) > 0m);
                metrics.Items.Cast<BondMetricRow>().Should().NotContain(row => row.Label.StartsWith("Yield to worst"), "v3 calculates no exercise scenarios");

                var payments = await ExpandGridAsync(view, "Projected contractual payments", "Projected bond payments");
                payments.Items.Cast<BondCashFlowRow>().Should().HaveCount(4);
                Number(payments.Items.Cast<BondCashFlowRow>().First().Interest).Should().Be(2m);
                payments.Items.Cast<BondCashFlowRow>().Skip(1).Should().OnlyContain(row => Number(row.Interest) == 1.5m);
                Number(payments.Items.Cast<BondCashFlowRow>().Last().Principal).Should().Be(100m);
                var resetEvidence = await ExpandGridAsync(view, "Floating coupon reset evidence", "Floating coupon reset evidence");
                resetEvidence.Items.Cast<BondCashFlowRow>().Should().Contain(row => row.ResetDate == "2026-01-15"
                    && row.FixingStatus == "Observed fixing" && row.IndexName == "SOFR" && Number(row.IndexRate) == 0.03m
                    && Number(row.SpreadBps) == 100m && Number(row.CouponRate) == 0.04m && row.Source == "Retained reset schedule");
                resetEvidence.Items.Cast<BondCashFlowRow>().Should().Contain(row => row.ResetDate == "2026-07-15"
                    && row.FixingStatus == "Forecast fixing" && Number(row.IndexRate) == 0.02m && Number(row.CouponRate) == 0.03m
                    && row.Source == "synthetic-sofr-forward");
                analytics.Inputs.Should().Contain(input => input.Contains("ForwardCurveContent") && input.Contains("SOFR"));

                if (refusal is not null)
                {
                    retainFixing = refusal != "MissingFixing";
                    retainCurve = refusal != "ForwardCurveRequired";
                    valueInput.Text = "0.031";
                    await ClickAsync(calculate, analytics.CalculateCommand);
                    window.UpdateLayout();

                    analytics.IsBlocked.Should().BeTrue();
                    analytics.HasFloatingResult.Should().BeFalse();
                    Named<ItemsControl>(view, "Calculation blockers").Items.Cast<string>().Should().ContainSingle()
                        .Which.Should().StartWith(refusal + ":");
                    analytics.Metrics.Should().BeEmpty("a refusal cannot retain prior values");
                    analytics.CashFlows.Should().BeEmpty();
                    requests.Should().HaveCount(2).And.OnlyContain(request => request.CalculationVersion == "bond-analytics-v3", "there is no fallback request");
                }
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static async Task ClickAsync(Button button, IAsyncRelayCommand command)
    {
        button.BringIntoView();
        button.UpdateLayout();
        var peer = UIElementAutomationPeer.FromElement(button) ?? new ButtonAutomationPeer(button);
        ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)).Invoke();
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        command.ExecutionTask.Should().NotBeNull("the visible Calculate button must invoke the bound command");
        await command.ExecutionTask!;
    }

    private static T Named<T>(DependencyObject root, string name) where T : DependencyObject
        => Descendants<T>(root).Single(control => AutomationProperties.GetName(control) == name);

    private static decimal Number(string value) => decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture);

    private static async Task<DataGrid> ExpandGridAsync(DependencyObject root, string headerPrefix, string gridName)
    {
        var expanders = Descendants<Expander>(root).Where(control => control.Header?.ToString()?.StartsWith(headerPrefix) == true).ToArray();
        expanders.Should().ContainSingle($"the {headerPrefix} expander must be present in the actual editor");
        var expander = expanders[0];
        var grid = expander.Content is DataGrid direct ? direct
            : ((Panel)expander.Content).Children.OfType<DataGrid>().Single();
        AutomationProperties.GetName(grid).Should().Be(gridName);
        var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        RoutedEventHandler onLoaded = (_, _) => loaded.TrySetResult();
        grid.Loaded += onLoaded;
        try
        {
            if (grid.IsLoaded) loaded.TrySetResult();
            expander.IsExpanded = true;
            expander.BringIntoView();
            // The Material Design expander attaches its content through template bindings on the
            // dispatcher. UpdateLayout alone cannot flush that binding work; await the actual grid.
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            await loaded.Task.WaitAsync(TimeSpan.FromSeconds(10));
            grid.UpdateLayout();
            expander.IsExpanded.Should().BeTrue();
            grid.IsVisible.Should().BeTrue("expanded payment evidence must render in the editor");
            PresentationSource.FromVisual(grid).Should().NotBeNull("the test must inspect the rendered grid, not detached controls");
            return grid;
        }
        finally
        {
            grid.Loaded -= onLoaded;
        }
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    private static SecurityDetailDto Floating(bool retainFixing)
    {
        var terms = new Dictionary<string, object?>
        {
            ["assetClassTermsVersion"] = 2,
            ["issueDate"] = "2026-01-15",
            ["maturity"] = "2028-01-15",
            ["couponType"] = "Floating",
            ["floatingIndex"] = "SOFR",
            ["spreadBps"] = 100,
            ["dayCount"] = "30/360",
            ["par"] = 100m,
            ["paymentFrequency"] = "SemiAnnual",
            ["scheduleConventions"] = new { accrualAdjusted = false, couponRateUnit = "Fraction", couponCashBasis = "Level" }
        };
        if (retainFixing)
            terms["resetSchedule"] = new[] { new { resetDate = "2026-01-15", fixingRate = 0.03m } };
        return new(SecurityId, "Bond", SecurityStatusDto.Active, "Synthetic SOFR + 100 bp", "USD",
            JsonSerializer.SerializeToElement(new { }), JsonSerializer.SerializeToElement(terms), [], [], 7,
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), null);
    }
}
