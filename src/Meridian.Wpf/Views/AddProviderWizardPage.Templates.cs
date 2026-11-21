using System.IO;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using Meridian.Contracts.Api;
using Meridian.Wpf.Services;
using Microsoft.Win32;
using ProviderConnectionDto = Meridian.Contracts.Api.ProviderConnectionDto;

namespace Meridian.Wpf.Views;

public partial class AddProviderWizardPage
{
    private static readonly JsonSerializerOptions TemplateFileJson = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private ProviderConnectionTemplateOption? _reviewedTemplate;
    private ProviderTemplateSetupDraft? _templateDraft;
    private Guid? _templateSetupId;
    private bool _templateBusy;
    private bool _templateTestPassed;
    private CancellationTokenSource _templatePageCts = new();

    private async Task ReloadTemplatesAsync(string? selectName = null)
        => await RunTemplateActionAsync(async ct =>
        {
            var templates = await _settingsConfigService.GetProviderTemplatesAsync(ct);
            ct.ThrowIfCancellationRequested();
            var previous = selectName ?? (TemplatePicker.SelectedItem as ProviderConnectionTemplateOption)?.Template.Name;
            TemplatePicker.ItemsSource = templates;
            TemplatePicker.SelectedItem = templates.FirstOrDefault(item => item.Template.Name == previous) ?? templates.FirstOrDefault();
            TemplateMessage.Text = templates.Length == 0 ? "No connection templates are available." : string.Empty;
        });

    private async void ReloadTemplates_Click(object sender, RoutedEventArgs e)
        => await AsyncHandlerGuard.RunAsync(() => ReloadTemplatesAsync(), nameof(ReloadTemplates_Click)).ConfigureAwait(true);

    private void TemplateSelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateTemplateControls();

    private void TemplateIdentityChanged(object sender, TextChangedEventArgs e) => UpdateTemplateControls();

    private void UpdateTemplateControls()
    {
        if (UseTemplateButton is null || SaveTemplateButton is null)
            return;
        var selected = TemplatePicker.SelectedItem is ProviderConnectionTemplateOption;
        var catalogReady = !_templateBusy && TemplatePicker.Items.Count > 0;
        UseTemplateButton.IsEnabled = catalogReady && selected;
        ExportTemplateButton.IsEnabled = catalogReady && selected;
        ImportTemplatesButton.IsEnabled = catalogReady;
        SaveSourceTemplateButton.IsEnabled = catalogReady && SourceTemplateConnection.SelectedItem is not null &&
            !string.IsNullOrWhiteSpace(TemplateNameInput.Text);
        ContinueTemplateButton.IsEnabled = !_templateBusy && _reviewedTemplate is not null &&
            (!_reviewedTemplate.RequiresExternalAccount || !string.IsNullOrWhiteSpace(TemplateAccountInput.Text));
        DiscardTemplateButton.IsEnabled = !_templateBusy;
        if (_templateDraft is not null)
        {
            CredentialFieldsPanel.IsEnabled = !_templateBusy;
            TemplateLiveAcknowledgement.IsEnabled = !_templateBusy;
        }
        var canTest = !_templateBusy && _templateDraft is not null &&
            (_reviewedTemplate?.Template.ConnectionMode != "Live" || TemplateLiveAcknowledgement.IsChecked == true);
        TestTemplateButton.IsEnabled = canTest;
        SaveTemplateButton.IsEnabled = canTest && _templateTestPassed;
    }

    private void UseTemplate_Click(object sender, RoutedEventArgs e)
    {
        if (_templateBusy || _operationInProgress || TemplatePicker.SelectedItem is not ProviderConnectionTemplateOption option)
            return;

        ResetOwnedConnectionScope();
        _selectedProvider = null;
        _reviewedTemplate = option;
        _viewModel.CurrentStep = 1;
        Step4Label.Text = "Save connection";
        TemplatePickerPanel.Visibility = Visibility.Collapsed;
        ManualProviderPanel.Visibility = Visibility.Collapsed;
        Step2Panel.Visibility = Step3Panel.Visibility = Step4Panel.Visibility = Visibility.Collapsed;
        TemplateReviewPanel.Visibility = Visibility.Visible;
        TemplateReviewTitle.Text = option.Template.Name;
        TemplateReviewRows.ItemsSource = option.ReviewRows;
        TemplateVerificationChecks.ItemsSource = option.VerificationChecks;
        TemplateAccountPanel.Visibility = option.RequiresExternalAccount ? Visibility.Visible : Visibility.Collapsed;
        TemplateMessage.Text = string.Empty;
        if (_viewModel.FindProvider(option.Template.ProviderKind) is { } provider)
            _viewModel.ApplySelectedProvider(provider);
        UpdateTemplateControls();
        TemplateReviewTitle.Focus();
    }

    private async void ContinueTemplate_Click(object sender, RoutedEventArgs e)
        => await AsyncHandlerGuard.RunAsync(OpenTemplateVaultAsync, nameof(ContinueTemplate_Click)).ConfigureAwait(true);

    private async Task OpenTemplateVaultAsync()
    {
        if (_reviewedTemplate is not { } option || _templateDraft is not null)
            return;
        await RunTemplateActionAsync(async ct =>
        {
            var id = _templateSetupId ??= Guid.NewGuid();
            var draft = await _settingsConfigService.BeginProviderTemplateSetupAsync(
                new(id, option.Template.Name, option.Revision, option.RequiresExternalAccount ? TemplateAccountInput.Text.Trim() : null), ct);
            ct.ThrowIfCancellationRequested();
            _templateDraft = draft;
            TemplateAccountInput.Clear();
            TemplateAccountPanel.Visibility = Visibility.Collapsed;
            ContinueTemplateButton.Visibility = Visibility.Collapsed;
            DiscardTemplateButton.Content = "Discard draft";
            ExistingOwnedConnectionPanel.Visibility = Visibility.Collapsed;
            CredentialStepHeading.Text = "Credential vault";
            _viewModel.ApplyCredentialsInfo(option.Template.Name, draft.CredentialFields.Length > 0);
            RenderCredentialEditors(draft.CredentialFields);
            TemplateVaultActions.Visibility = Visibility.Visible;
            TemplateLiveAcknowledgement.Visibility = option.Template.ConnectionMode == "Live" ? Visibility.Visible : Visibility.Collapsed;
            Step2Panel.Visibility = Visibility.Visible;
            _viewModel.CurrentStep = 2;
            CredentialStepHeading.Focus();
            CredentialStepHeading.BringIntoView();
        });
    }

    private void TemplateAcknowledgementChanged(object sender, RoutedEventArgs e)
    {
        _templateTestPassed = false;
        UpdateTemplateControls();
    }

    private async void TestTemplate_Click(object sender, RoutedEventArgs e)
        => await AsyncHandlerGuard.RunAsync(TestTemplateConnectionAsync, nameof(TestTemplate_Click)).ConfigureAwait(true);

    private async Task TestTemplateConnectionAsync()
    {
        if (_templateDraft is not { } draft || !TestTemplateButton.IsEnabled)
            return;
        _templateTestPassed = false;
        await RunTemplateActionAsync(async ct =>
        {
            TemplateVaultMessage.Text = "Testing connection…";
            var fields = CollectEnteredCredentialFields();
            if (fields.Count > 0)
            {
                await _settingsConfigService.SaveTemplateCredentialsAsync(draft, fields, ct);
                ct.ThrowIfCancellationRequested();
                ClearPersistedCredentialFields(fields);
            }
            var result = await _settingsConfigService.TestProviderTemplateSetupAsync(draft.SetupId, ct);
            ct.ThrowIfCancellationRequested();
            _templateTestPassed = result.CanSave;
            TemplateVaultMessage.Text = result.Detail;
            _viewModel.CurrentStep = 3;
        });
    }

    private async void SaveTemplateConnection_Click(object sender, RoutedEventArgs e)
        => await AsyncHandlerGuard.RunAsync(SaveTemplateConnectionAsync, nameof(SaveTemplateConnection_Click)).ConfigureAwait(true);

    private async Task SaveTemplateConnectionAsync()
    {
        if (_templateDraft is not { } draft || !_templateTestPassed)
            return;
        await RunTemplateActionAsync(async ct =>
        {
            var saved = await _settingsConfigService.SaveProviderTemplateSetupAsync(draft.SetupId, ct);
            ct.ThrowIfCancellationRequested();
            ClearTemplateReview();
            TemplateMessage.Text = $"{saved.DisplayName} saved as connection {saved.ConnectionId}. Routing and runtime activation require a separate binding.";
            TemplateMessage.BringIntoView();
            UseTemplateButton.Focus();
            await LoadOwnedConnectionsAsync(_credentialScopeGeneration);
            ct.ThrowIfCancellationRequested();
            SourceTemplateConnection.ItemsSource = _ownedConnections;
        });
    }

    private async void DiscardTemplate_Click(object sender, RoutedEventArgs e)
        => await AsyncHandlerGuard.RunAsync(DiscardTemplateAsync, nameof(DiscardTemplate_Click)).ConfigureAwait(true);

    private async Task<bool> DiscardTemplateAsync()
    {
        await RunTemplateActionAsync(async ct =>
        {
            if (_templateSetupId is Guid id)
                await _settingsConfigService.DiscardProviderTemplateSetupAsync(id, ct);
            ct.ThrowIfCancellationRequested();
            ClearTemplateReview();
            UseTemplateButton.Focus();
        });
        return _reviewedTemplate is null;
    }

    private async Task DiscardAbandonedTemplateAsync(Guid setupId)
    {
        try
        {
            await _settingsConfigService.DiscardProviderTemplateSetupAsync(setupId, CancellationToken.None);
        }
        catch (InvalidOperationException) { /* The authenticated session may have ended. The draft stays outside connection routing. */ }
    }

    private void ClearTemplateReview()
    {
        _reviewedTemplate = null;
        _templateDraft = null;
        _templateSetupId = null;
        _templateTestPassed = false;
        ClearCredentialEditors();
        CredentialFieldsPanel.IsEnabled = true;
        TemplateAccountInput.Clear();
        TemplateLiveAcknowledgement.IsChecked = false;
        TemplateVaultMessage.Text = string.Empty;
        TemplateMessage.Text = string.Empty;
        ContinueTemplateButton.Visibility = Visibility.Visible;
        DiscardTemplateButton.Content = "Back to templates";
        CredentialStepHeading.Text = "Enter Credentials";
        ExistingOwnedConnectionPanel.Visibility = Visibility.Visible;
        TemplateVaultActions.Visibility = Visibility.Collapsed;
        TemplateReviewPanel.Visibility = Step2Panel.Visibility = Visibility.Collapsed;
        ManualProviderPanel.Visibility = TemplatePickerPanel.Visibility = Visibility.Visible;
        _viewModel.CurrentStep = 1;
        Step4Label.Text = "Save selected credentials";
        UpdateTemplateControls();
    }

    private async void ExportTemplate_Click(object sender, RoutedEventArgs e)
        => await AsyncHandlerGuard.RunAsync(ExportTemplateAsync, nameof(ExportTemplate_Click)).ConfigureAwait(true);

    private async Task ExportTemplateAsync()
    {
        if (TemplatePicker.SelectedItem is not ProviderConnectionTemplateOption choice)
            return;
        var picker = new SaveFileDialog
        {
            Filter = "Provider template JSON (*.json)|*.json",
            FileName = choice.Template.Name.Replace(' ', '-'),
            DefaultExt = ".json"
        };
        if (picker.ShowDialog() != true)
            return;
        await RunTemplateActionAsync(async ct =>
        {
            var file = await _settingsConfigService.ExportProviderTemplateAsync(choice.Template.Name, ct);
            await File.WriteAllTextAsync(picker.FileName, JsonSerializer.Serialize(file, TemplateFileJson), ct);
            TemplateMessage.Text = "Template JSON exported.";
        });
    }

    private async void ImportTemplates_Click(object sender, RoutedEventArgs e)
        => await AsyncHandlerGuard.RunAsync(ImportTemplatesAsync, nameof(ImportTemplates_Click)).ConfigureAwait(true);

    private async Task ImportTemplatesAsync()
    {
        var picker = new OpenFileDialog { Filter = "Provider template JSON (*.json)|*.json" };
        if (picker.ShowDialog() != true)
            return;
        var count = -1;
        await RunTemplateActionAsync(async ct =>
        {
            if (new FileInfo(picker.FileName).Length > 64 * 1024)
                throw new InvalidOperationException("Choose a JSON template file no larger than 64 KB.");
            try
            {
                using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(picker.FileName, ct));
                count = await _settingsConfigService.ImportProviderTemplatesAsync(doc.RootElement, ct);
            }
            catch (JsonException)
            {
                throw new InvalidOperationException("Choose a valid template JSON document.");
            }
        });
        if (count < 0)
            return;
        await ReloadTemplatesAsync();
        TemplateMessage.Text = count == 0 ? "The templates are already available." : $"Imported {count} template(s).";
    }

    private async void SaveSourceTemplate_Click(object sender, RoutedEventArgs e)
        => await AsyncHandlerGuard.RunAsync(SaveSourceTemplateAsync, nameof(SaveSourceTemplate_Click)).ConfigureAwait(true);

    private async Task SaveSourceTemplateAsync()
    {
        if (SourceTemplateConnection.SelectedItem is not ProviderConnectionDto source)
            return;
        ProviderConnectionTemplateOption? result = null;
        await RunTemplateActionAsync(async ct =>
        {
            result = await _settingsConfigService.SaveConnectionAsTemplateAsync(new(source.ConnectionId, TemplateNameInput.Text.Trim()), ct);
            ct.ThrowIfCancellationRequested();
            TemplateNameInput.Clear();
        });
        if (result is null)
            return;
        await ReloadTemplatesAsync(result.Template.Name);
        TemplateMessage.Text = $"{result.Template.Name} is available in the template catalog.";
    }

    private async Task RunTemplateActionAsync(Func<CancellationToken, Task> action)
    {
        if (_templateBusy)
            return;
        var ct = _templatePageCts.Token;
        _templateBusy = true;
        TemplateMessage.Text = string.Empty;
        UpdateTemplateControls();
        try
        { await action(ct); }
        catch (Exception) when (ct.IsCancellationRequested) { }
        catch (Exception ex)
        {
            var message = ex is InvalidOperationException ? ex.Message : "The template operation could not be completed. Try again.";
            if (_templateDraft is not null)
                TemplateVaultMessage.Text = message;
            else
                TemplateMessage.Text = message;
        }
        finally
        {
            if (!ct.IsCancellationRequested)
            {
                _templateBusy = false;
                UpdateTemplateControls();
            }
        }
    }
}
