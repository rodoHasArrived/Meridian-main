using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
using Meridian.Contracts.FundStructure;
using Meridian.Ui.Services.Services.Accounting;
using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.ViewModels.Accounting;
using Meridian.Wpf.Views;

namespace Meridian.Wpf.Tests.Views;

/// <summary>Actual Windows controls on the shared STA dispatcher, populated with deterministic synthetic server responses.</summary>
[Collection("NavigationServiceSerialCollection")]
public sealed partial class IntercompanyNativeControlSmokeTests
{
    [Fact]
    public void AgreementControl_RendersTypedTermsAttestationAndAdvisoryBalancesWithoutBindingErrors()
    {
        WpfTestThread.Run(async () =>
        {
            RunMatUiAutomationFacade.EnsureApplicationResources();
            using var errors = new BindingErrorCapture();
            var client = new AgreementClient();
            using var workspace = new IntercompanyAgreementWorkspace(client) { AsOf = "2026-11-13" };
            await workspace.LoadCommand.ExecuteAsync(null);
            workspace.SelectedRow = Assert.Single(workspace.Agreements);
            await workspace.OpenCommand.ExecuteAsync(null);
            var control = new IntercompanyAgreementsControl { DataContext = workspace };
            var window = Host(control);
            try
            {
                await ArrangeAsync(window, control);
                Assert.Equal(ApartmentState.STA, Thread.CurrentThread.GetApartmentState());
                var rate = Named<TextBox>(control, "Agreement annual rate fraction");
                Assert.Equal("0.0523456789", rate.Text);
                Assert.Equal(BindingStatus.Active, rate.GetBindingExpression(TextBox.TextProperty)!.Status);
                SetText(rate, "0.0623456789");
                Assert.True(workspace.IsDirty);
                await ExecuteAsync(Button(control, "Save draft"));
                Assert.Equal(0.0623456789m, client.LastSave!.Draft!.Terms!.StructuredTerms!.FixedAnnualRate);
                Assert.False(workspace.IsDirty);

                await ExecuteAsync(Button(control, "Preview retained proposal dates"));
                await ArrangeAsync(window, control);
                var dates = Named<DataGrid>(control, "Declared agreement date preview");
                Assert.Single(dates.Items.Cast<object>());
                Assert.Contains("synthetic-calendar", workspace.DatePreviewStatus);

                await ExecuteAsync(Button(control, "Submit"));
                await ArrangeAsync(window, control);
                var approve = Button(control, "Approve");
                Assert.False(approve.IsEnabled); // Native independent approval remains unavailable to the preparer.
                var external = Named<CheckBox>(control, "Use external review attestation");
                external.SetCurrentValue(ToggleButton.IsCheckedProperty, true);
                external.GetBindingExpression(ToggleButton.IsCheckedProperty)!.UpdateSource();
                SetText(Named<TextBox>(control, "External reviewer"), "Synthetic external counsel");
                SetText(Named<TextBox>(control, "Reviewer reference"), "SYNTHETIC-MATTER-42");
                SetText(Named<TextBox>(control, "Reviewed on (yyyy-MM-dd)"), "2026-11-12");
                SetText(Named<TextBox>(control, "Evidence Vault reference"), "evidence://evidence-vault/synthetic-review");
                SetText(Named<TextBox>(control, "Retained artifact ID"), "synthetic-document");
                SetText(Named<TextBox>(control, "Agreement review rationale"), "External review of the exact submitted synthetic terms");
                await ArrangeAsync(window, control);
                Assert.True(approve.IsEnabled);
                await ExecuteAsync(approve);
                Assert.Equal(client.SubmittedHash, client.LastApproval!.ExternalReview!.SubmittedHash);
                Assert.Equal("Synthetic external counsel", client.LastApproval.ExternalReview.ExternalReviewer);
                await ArrangeAsync(window, control);
                var retained = Named<TextBox>(control, "Selected agreement version terms");
                Assert.Contains("Externally reviewed by Synthetic external counsel", retained.Text);
                Assert.Contains("attested by synthetic-operator", retained.Text);

                SetText(Named<TextBox>(control, "Agreement journal ledger book"), client.BookId.ToString("D"));
                await ExecuteAsync(Button(control, "Evaluate obligation monitor"));
                await ArrangeAsync(window, control);
                var findings = Named<DataGrid>(control, "Nonblocking intercompany obligation findings");
                Assert.Single(findings.Items.Cast<object>());
                var balances = Named<DataGrid>(control, "Posted obligation balances");
                Assert.Single(balances.Items.Cast<object>());
                Assert.Equal(ExactBalance, workspace.ObligationBalances.Single().TransactionSignedBalance);
                Assert.Single(Named<DataGrid>(control, "Guarantee disclosure register").Items.Cast<object>());
                Assert.Contains("Advisory only", workspace.ObligationStatus);
                balances.BringIntoView();
                await ArrangeAsync(window, control);
                Assert.True(control.IsVisible);
                Assert.True(double.IsFinite(control.ActualWidth) && control.ActualWidth > 0);
                Assert.True(double.IsFinite(balances.ActualHeight) && balances.ActualHeight > 0);
                Assert.Empty(errors.Messages);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void EntityControl_ReviewsPopulatedLifecycleTimelineAndClearsStaleSubjectHash()
    {
        WpfTestThread.Run(async () =>
        {
            RunMatUiAutomationFacade.EnsureApplicationResources();
            using var errors = new BindingErrorCapture();
            var client = new EntityClient();
            var model = new EntityConsolidationViewModel(client) { AsOf = new DateTime(2026, 11, 13) };
            await model.LoadChoicesAsync();
            model.SelectedOrganization = Assert.Single(model.Organizations);
            model.SelectedEntity = Assert.Single(model.Entities);
            await model.LoadTimelineAsync();
            var control = new EntityConsolidationControl { DataContext = model };
            var window = Host(control);
            try
            {
                await ArrangeAsync(window, control);
                Button(control, "Prepare complete lifecycle revision").Command.Execute(null);
                await ArrangeAsync(window, control);
                var timeline = Named<DataGrid>(control, "Reviewed lifecycle timeline editor");
                Assert.Single(timeline.Items.Cast<object>());
                timeline.ScrollIntoView(timeline.Items[0]);
                timeline.UpdateLayout();
                await ArrangeAsync(window, control);
                var status = Named<ComboBox>(control, "Lifecycle window status");
                status.SetCurrentValue(Selector.SelectedItemProperty, LegalEntityLifecycleStatusDto.WindingDown);
                status.GetBindingExpression(Selector.SelectedItemProperty)!.UpdateSource();
                Assert.Equal(LegalEntityLifecycleStatusDto.WindingDown, model.LifecycleDraftWindows.Single().Status);
                SetText(Named<TextBox>(control, "Legal change reason"), "Synthetic reviewed wind-down");
                SetText(Named<TextBox>(control, "Retained legal evidence reference"), "evidence://evidence-vault/synthetic-lifecycle");
                await ExecuteAsync(Button(control, "Preview exact lifecycle review subject"));
                await ArrangeAsync(window, control);
                var subject = Named<TextBox>(control, "Exact lifecycle review subject SHA-256");
                Assert.NotEmpty(subject.Text);
                var firstHash = subject.Text;
                SetText(Named<TextBox>(control, "Legal change reason"), "Synthetic reviewed wind-down with correction rationale");
                await ArrangeAsync(window, control);
                Assert.Empty(subject.Text);
                Assert.False(Button(control, "Attest external review and retain lifecycle revision").IsEnabled);
                await ExecuteAsync(Button(control, "Preview exact lifecycle review subject"));
                Assert.NotEqual(firstHash, model.LifecycleSubjectHash);

                SetText(Named<TextBox>(control, "External reviewer"), "Synthetic lifecycle counsel");
                SetText(Named<TextBox>(control, "External reviewer reference"), "SYNTHETIC-LIFECYCLE-42");
                SetText(Named<TextBox>(control, "Reviewed on (yyyy-MM-dd)"), "2026-11-12");
                SetText(Named<TextBox>(control, "External review evidence reference"), "evidence://evidence-vault/synthetic-external-review");
                SetText(Named<TextBox>(control, "Retained external artifact ID"), "synthetic-lifecycle-document");
                SetText(Named<TextBox>(control, "External review attestation rationale"), "Reviewed this exact effective-dated timeline");
                var reviewedHash = model.LifecycleSubjectHash;
                await ExecuteAsync(Button(control, "Attest external review and retain lifecycle revision"));
                await ArrangeAsync(window, control);
                Assert.Equal(reviewedHash, client.LastAppend!.ExternalReview!.SubmittedHash);
                Assert.Equal("Synthetic lifecycle counsel", client.LastAppend.ExternalReview.ExternalReviewer);
                Assert.Equal(LegalEntityLifecycleStatusDto.WindingDown, client.LastAppend.Timeline.Single().Status);
                Assert.Contains("attested by synthetic-operator", model.Status);
                Assert.False(model.IsLifecycleEditing);
                Assert.True(control.IsVisible);
                Assert.True(double.IsFinite(control.ActualWidth) && control.ActualWidth > 0);
                Assert.Empty(errors.Messages);
            }
            finally { window.Close(); }
        });
    }

    private static Window Host(UserControl control)
    {
        var window = new Window { Width = 1366, Height = 900, Content = control, WindowStartupLocation = WindowStartupLocation.Manual };
        window.Show();
        return window;
    }

    private static async Task ArrangeAsync(Window window, DependencyObject control)
    {
        for (var round = 0; round < 3; round++)
        {
            window.UpdateLayout();
            foreach (var expander in Descendants<Expander>(control).ToArray())
                expander.SetCurrentValue(Expander.IsExpandedProperty, true);
            await Dispatcher.Yield(DispatcherPriority.ContextIdle);
        }
        window.UpdateLayout();
    }

    private static T Named<T>(DependencyObject root, string name) where T : DependencyObject
        => Assert.Single(Descendants<T>(root), item => AutomationProperties.GetName(item) == name);
    private static Button Button(DependencyObject root, string caption)
        => Assert.Single(Descendants<Button>(root), button => Equals(button.Content, caption));
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T target)
            yield return target;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var child in Descendants<T>(VisualTreeHelper.GetChild(root, index)))
                yield return child;
    }
    private static void SetText(TextBox input, string text)
    {
        input.SetCurrentValue(TextBox.TextProperty, text);
        Assert.NotNull(input.GetBindingExpression(TextBox.TextProperty));
        input.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
    }
    private static async Task ExecuteAsync(Button button)
    {
        Assert.True(button.IsEnabled, $"The visible '{button.Content}' control is unexpectedly disabled.");
        var command = Assert.IsAssignableFrom<IAsyncRelayCommand>(button.Command);
        Assert.True(command.CanExecute(button.CommandParameter));
        await command.ExecuteAsync(button.CommandParameter);
        await Dispatcher.Yield(DispatcherPriority.ContextIdle);
    }

    private sealed class BindingErrorCapture : TraceListener, IDisposable
    {
        private readonly SourceLevels _previous = PresentationTraceSources.DataBindingSource.Switch.Level;
        public List<string> Messages { get; } = [];
        public BindingErrorCapture()
        {
            PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
            PresentationTraceSources.DataBindingSource.Listeners.Add(this);
        }
        public override void Write(string? message) { if (!string.IsNullOrWhiteSpace(message)) Messages.Add(message); }
        public override void WriteLine(string? message) => Write(message);
        protected override void Dispose(bool disposing)
        {
            PresentationTraceSources.DataBindingSource.Listeners.Remove(this);
            PresentationTraceSources.DataBindingSource.Switch.Level = _previous;
            base.Dispose(disposing);
        }
    }
}
