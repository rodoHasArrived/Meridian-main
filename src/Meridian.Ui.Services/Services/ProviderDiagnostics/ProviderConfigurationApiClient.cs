using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Meridian.Contracts.Api;
using Meridian.Contracts.Configuration;
using Meridian.Contracts.Lifecycle;
using Meridian.Core.Config;

namespace Meridian.Ui.Services.ProviderDiagnostics;

/// <summary>Reads and edits provider configuration through the authoritative host.</summary>
public sealed class ProviderConfigurationApiClient(ApiClientService api)
{
    // Older host versions write the domain provider enum as a number. Desktop edits use the same
    // named values the host accepts for writes, while also retaining the host's nested option fields.
    private static readonly JsonSerializerOptions SourcesJsonOptions = new(DesktopJsonOptions.Api)
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver
        {
            Modifiers = { static type =>
            {
                if (type.Type == typeof(DataSourceConfigDto))
                    type.Properties.Single(property => property.Name == "provider").CustomConverter = new ProviderNameConverter();
            } }
        }
    };

    public async Task<DataSourcesConfigDto> GetDataSourcesAsync(CancellationToken ct = default)
    {
        var body = HostResponse.RequireData(await api.GetTextWithResponseAsync(UiApiRoutes.ConfigDataSources, ct).ConfigureAwait(false));
        try
        {
            return JsonSerializer.Deserialize<DataSourcesConfigDto>(body, SourcesJsonOptions)
                ?? throw new InvalidOperationException("The host returned an empty data source configuration.");
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("The host returned an invalid data source configuration. Refresh and try again.");
        }
    }

    public async Task<string> SaveDataSourceAsync(DataSourceConfigRequest request, CancellationToken ct = default)
        => HostResponse.RequireData(await api.PostWithResponseAsync<SavedDataSource>(UiApiRoutes.ConfigDataSources, request, ct).ConfigureAwait(false)).Id;

    public async Task DeleteDataSourceAsync(string id, CancellationToken ct = default)
        => HostResponse.EnsureSuccess(await api.DeleteWithResponseAsync<object>(SourcePath(id), ct).ConfigureAwait(false));

    public async Task SetDataSourceEnabledAsync(string id, bool enabled, CancellationToken ct = default)
        => HostResponse.EnsureSuccess(await api.PostWithResponseAsync<object>(SourcePath(id) + "/toggle", new ToggleRequest(enabled), ct).ConfigureAwait(false));

    public async Task SetDefaultAsync(string id, bool isHistorical, CancellationToken ct = default)
        => HostResponse.EnsureSuccess(await api.PostWithResponseAsync<object>(UiApiRoutes.ConfigDataSourcesDefaults,
            new DefaultSourcesRequest(isHistorical ? null : id, isHistorical ? id : null)
            {
                UpdateRealTime = !isHistorical,
                UpdateHistorical = isHistorical
            }, ct).ConfigureAwait(false));

    public async Task SaveFailoverAsync(bool enabled, int timeout, CancellationToken ct = default)
        => HostResponse.EnsureSuccess(await api.PostWithResponseAsync<object>(UiApiRoutes.ConfigDataSourcesFailover,
            new FailoverSettingsRequest(enabled, timeout), ct).ConfigureAwait(false));

    public async Task SaveAlpacaFeedAsync(string sourceId, string feed, CancellationToken ct = default)
        => _ = HostResponse.RequireData(await api.PutWithResponseAsync<ConfigurationChangeStatus>(UiApiRoutes.ConfigAlpacaFeed,
            new AlpacaFeedUpdateDto(feed, sourceId), ct).ConfigureAwait(false));

    public async Task<string> GetAlpacaFeedAsync(CancellationToken ct = default)
        => HostResponse.RequireData(await api.GetWithResponseAsync<SavedAlpacaFeed>(UiApiRoutes.ConfigAlpacaFeed, ct).ConfigureAwait(false)).Feed;

    public async Task<ProviderActivationResponse> GetActivationAsync(CancellationToken ct = default)
        => HostResponse.RequireData(await api.GetWithResponseAsync<ProviderActivationResponse>("/api/providers/activation", ct).ConfigureAwait(false));

    public async Task RestartAsync(CancellationToken ct = default)
        => _ = HostResponse.RequireData(await api.PostWithResponseAsync<LifecycleShutdownAcceptedDto>(UiApiRoutes.ProviderRestart, ct: ct).ConfigureAwait(false));

    public async Task<BackfillProviderStatusDto[]> GetBackfillProvidersAsync(CancellationToken ct = default)
        => HostResponse.RequireData(await api.GetWithResponseAsync<BackfillProviderStatusDto[]>(UiApiRoutes.BackfillProviderStatuses, ct).ConfigureAwait(false));

    public async Task<IReadOnlyList<EffectiveBackfillProvider>> GetEffectiveOrderAsync(CancellationToken ct = default)
        => HostResponse.RequireData(await api.GetWithResponseAsync<EffectiveBackfillOrder>("/api/backfill/providers/effective-order", ct).ConfigureAwait(false)).Providers;

    public async Task<BackfillProviderOptionsDto> SaveBackfillProviderAsync(string id, BackfillProviderEdit edit, CancellationToken ct = default)
        => HostResponse.RequireData(await api.PutWithResponseAsync<SavedBackfillProvider>(UiApiRoutes.BackfillProviders + "/" + Uri.EscapeDataString(id), edit, ct).ConfigureAwait(false)).Options;

    public async Task<ProviderConfigAuditEntryDto[]> GetBackfillAuditAsync(CancellationToken ct = default)
        => HostResponse.RequireData(await api.GetWithResponseAsync<ProviderConfigAuditEntryDto[]>(UiApiRoutes.BackfillProviderConfigAudit, ct).ConfigureAwait(false));

    public async Task<BackfillDryRunPlanDto> PreviewBackfillAsync(string[] symbols, CancellationToken ct = default)
        => HostResponse.RequireData(await api.PostWithResponseAsync<BackfillDryRunPlanDto>(UiApiRoutes.BackfillDryRunPlan,
            new { symbols }, ct).ConfigureAwait(false));

    private static string SourcePath(string id) => $"{UiApiRoutes.ConfigDataSources}/{Uri.EscapeDataString(id)}";

    private sealed record SavedDataSource(string Id);
    private sealed record SavedAlpacaFeed(string Feed);
    private sealed record SavedBackfillProvider(BackfillProviderOptionsDto Options);
    private sealed record EffectiveBackfillOrder(IReadOnlyList<EffectiveBackfillProvider> Providers);

    private sealed class ProviderNameConverter : JsonConverter<string>
    {
        public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => reader.TokenType switch
            {
                JsonTokenType.String => reader.GetString()!,
                JsonTokenType.Number when reader.TryGetByte(out var code) && Enum.IsDefined((DataSourceKind)code) => ((DataSourceKind)code).ToString(),
                _ => throw new JsonException("Invalid data source provider.")
            };

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) => writer.WriteStringValue(value);
    }
}

/// <summary>A provider in the host's current backfill routing order.</summary>
public sealed record EffectiveBackfillProvider(string ProviderId, string DisplayName, int Priority, bool IsSimulated);

internal static class HostResponse
{
    internal static T RequireData<T>(ApiResponse<T> response) where T : class
    {
        EnsureSuccess(response);
        return response.Data ?? throw new InvalidOperationException("The host returned an empty response. Refresh and try again.");
    }

    internal static void EnsureSuccess<T>(ApiResponse<T> response) where T : class
    {
        if (response.Success)
            return;

        if (response.IsConnectionError || response.StatusCode == 0)
            throw new InvalidOperationException("Could not reach the host. Check the connection and try again.");

        var message = response.StatusCode switch
        {
            401 => "Sign in to the host and try again.",
            403 => "Your host account does not have permission for this operation.",
            503 => "This service is unavailable on the current host. Refresh and try again.",
            _ => $"The host could not complete the operation (HTTP {response.StatusCode})."
        };
        if (response.StatusCode is 400 or 409 or 422 && TryReadProblem(response.ErrorMessage) is { } problem)
            message = problem;
        throw new InvalidOperationException(message);
    }

    private static string? TryReadProblem(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return null;

        try
        {
            using var json = JsonDocument.Parse(body);
            if (json.RootElement.ValueKind == JsonValueKind.String)
                return json.RootElement.GetString();
            if (json.RootElement.ValueKind == JsonValueKind.Object)
            {
                if (json.RootElement.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Object)
                {
                    var messages = errors.EnumerateObject().Where(error => error.Value.ValueKind == JsonValueKind.Array)
                        .SelectMany(error => error.Value.EnumerateArray()).Where(value => value.ValueKind == JsonValueKind.String)
                        .Select(value => value.GetString()).Take(5).ToArray();
                    if (messages.Length > 0)
                        return string.Join(" ", messages);
                }

                foreach (var name in new[] { "detail", "error", "title" })
                {
                    if (json.RootElement.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
                        return value.GetString();
                }
            }
        }
        catch (JsonException)
        {
            // Error pages can be HTML. Show the status instead of the response body.
        }

        return null;
    }
}
