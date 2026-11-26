using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.Contracts.Configuration;

namespace Meridian.Ui.Services.Services;

public sealed partial class SettingsConfigurationService
{
    public async Task<ProviderConnectionTemplateOption[]> GetProviderTemplatesAsync(CancellationToken ct)
        => ReadTemplateResponse(await _apiClient.GetWithResponseAsync<ProviderConnectionTemplateOption[]>(
            UiApiRoutes.ProviderConnectionTemplates, ct).ConfigureAwait(false));

    public async Task<ProviderConnectionTemplateFile> ExportProviderTemplateAsync(string name, CancellationToken ct)
        => ReadTemplateResponse(await _apiClient.GetWithResponseAsync<ProviderConnectionTemplateFile>(
            UiApiRoutes.WithQuery(UiApiRoutes.ProviderConnectionTemplateExport, "name=" + Uri.EscapeDataString(name)), ct).ConfigureAwait(false));

    public async Task<int> ImportProviderTemplatesAsync(JsonElement json, CancellationToken ct)
        => ReadTemplateResponse(await _apiClient.PostWithResponseAsync<TemplateImportResult>(
            UiApiRoutes.ProviderConnectionTemplateImport, json, ct).ConfigureAwait(false)).Imported;

    public async Task<ProviderConnectionTemplateOption> SaveConnectionAsTemplateAsync(
        SaveProviderConnectionTemplateRequest request, CancellationToken ct)
        => ReadTemplateResponse(await _apiClient.PostWithResponseAsync<ProviderConnectionTemplateOption>(
            UiApiRoutes.ProviderConnectionTemplates, request, ct).ConfigureAwait(false));

    public async Task<ProviderTemplateSetupDraft> BeginProviderTemplateSetupAsync(BeginProviderTemplateSetupRequest request, CancellationToken ct)
        => ReadTemplateResponse(await _apiClient.PostWithResponseAsync<ProviderTemplateSetupDraft>(
            UiApiRoutes.ProviderConnectionTemplateSetups, request, ct).ConfigureAwait(false));

    public Task<ProviderCredentialStateDto> SaveTemplateCredentialsAsync(ProviderTemplateSetupDraft draft,
        IReadOnlyDictionary<string, string?> fields, CancellationToken ct)
        => SaveProviderCredentialFieldsAsync(draft.ProviderKind, fields,
            UiApiRoutes.WithQuery(CredentialRoute(UiApiRoutes.ProviderCredentialMutation, draft.ProviderKind, null),
                "templateSetupId=" + draft.SetupId.ToString("N")), draft.Environment, ct);

    public async Task<ProviderTemplateSetupTestResult> TestProviderTemplateSetupAsync(Guid setupId, CancellationToken ct)
        => ReadTemplateResponse(await _apiClient.PostWithResponseAsync<ProviderTemplateSetupTestResult>(
            SetupRoute(UiApiRoutes.ProviderConnectionTemplateSetupTest, setupId), null, ct).ConfigureAwait(false));

    public async Task<SavedProviderTemplateConnection> SaveProviderTemplateSetupAsync(Guid setupId, CancellationToken ct)
        => ReadTemplateResponse(await _apiClient.PostWithResponseAsync<SavedProviderTemplateConnection>(
            SetupRoute(UiApiRoutes.ProviderConnectionTemplateSetup, setupId), null, ct).ConfigureAwait(false));

    public async Task DiscardProviderTemplateSetupAsync(Guid setupId, CancellationToken ct)
        => _ = ReadTemplateResponse(await _apiClient.DeleteWithResponseAsync<TemplateDiscardResult>(
            SetupRoute(UiApiRoutes.ProviderConnectionTemplateSetup, setupId), ct).ConfigureAwait(false));

    private static string SetupRoute(string route, Guid id) => UiApiRoutes.WithParam(route, "setupId", id.ToString("N"));

    private static T ReadTemplateResponse<T>(ApiResponse<T> response) where T : class
    {
        if (response.Success && response.Data is not null)
            return response.Data;
        if (response.StatusCode is 401 or 403)
            throw new InvalidOperationException("Connection templates require an authorized, signed-in workspace operator.");

        if (response.StatusCode is 400 or 409 && response.ErrorMessage is { Length: > 0 } json)
        {
            try
            {
                var error = JsonSerializer.Deserialize<TemplateApiError>(json, JsonSerializerOptions.Web);
                if (error?.Issues is { Length: > 0 } issues)
                    throw new InvalidOperationException(string.Join(Environment.NewLine, issues.Select(issue => $"{issue.Field} — {issue.Message}")));
                if (!string.IsNullOrWhiteSpace(error?.Error))
                    throw new InvalidOperationException(error.Error);
            }
            catch (JsonException) { }
        }
        throw new InvalidOperationException("The authenticated service could not confirm the template operation. Try again.");
    }

    private sealed record TemplateImportResult(int Imported);
    private sealed record TemplateDiscardResult(bool Discarded);
    private sealed record TemplateApiError(string? Error, ProviderTemplateImportIssue[]? Issues);
}
