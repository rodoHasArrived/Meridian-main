using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Meridian.Contracts.Plaid;
using Meridian.Storage.Archival;

namespace Meridian.Infrastructure.Adapters.Plaid;

public sealed class FilePlaidConnectionRepository : IPlaidConnectionRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _directory;
    private readonly string _statePath;
    private readonly string _webhookPath;
    private readonly string _transferPath;

    public FilePlaidConnectionRepository(string dataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        _directory = Path.Combine(Path.GetFullPath(dataRoot), "plaid");
        _statePath = Path.Combine(_directory, "connections.json");
        _webhookPath = Path.Combine(_directory, "webhooks.jsonl");
        _transferPath = Path.Combine(_directory, "transfers.jsonl");
    }

    public async Task<IReadOnlyList<PlaidItemDto>> ListItemsAsync(CancellationToken ct = default)
    {
        var state = await ReadStateAsync(ct).ConfigureAwait(false);
        return state.Items.Values.OrderBy(static item => item.InstitutionName, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public async Task<PlaidItemDto?> GetItemAsync(string itemId, CancellationToken ct = default)
    {
        var state = await ReadStateAsync(ct).ConfigureAwait(false);
        return state.Items.TryGetValue(itemId, out var item) ? item : null;
    }

    public async Task<IReadOnlyList<PlaidAccountDto>> ListAccountsAsync(string? itemId = null, CancellationToken ct = default)
    {
        var state = await ReadStateAsync(ct).ConfigureAwait(false);
        return state.Accounts.Values
            .Where(account => string.IsNullOrWhiteSpace(itemId) || string.Equals(account.ItemId, itemId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(static account => account.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task UpsertItemAsync(PlaidItemDto item, IReadOnlyList<PlaidAccountDto> accounts, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(accounts);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var state = await LoadStateCoreAsync(ct).ConfigureAwait(false);
            state.Items[item.ItemId] = item;
            foreach (var account in accounts)
            {
                state.Accounts[$"{account.ItemId}:{account.PlaidAccountId}"] = account;
            }

            await WriteStateCoreAsync(state, ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task UpdateTransactionsCursorAsync(string itemId, string? cursor, DateTimeOffset syncedAt, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var state = await LoadStateCoreAsync(ct).ConfigureAwait(false);
            if (state.Items.TryGetValue(itemId, out var item))
            {
                state.Items[itemId] = item with { TransactionsCursor = cursor, LastSyncedAt = syncedAt, Status = PlaidItemStatusDto.Linked, LastError = null };
                await WriteStateCoreAsync(state, ct).ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task UpdateItemStatusAsync(
        string itemId,
        PlaidItemStatusDto status,
        string? webhookType,
        string? webhookCode,
        string? error,
        CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var state = await LoadStateCoreAsync(ct).ConfigureAwait(false);
            if (state.Items.TryGetValue(itemId, out var item))
            {
                state.Items[itemId] = item with
                {
                    Status = status,
                    LastWebhookType = webhookType,
                    LastWebhookCode = webhookCode,
                    LastError = error
                };
                await WriteStateCoreAsync(state, ct).ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> TryAppendWebhookAsync(PlaidWebhookEventDto webhook, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(webhook);
        Directory.CreateDirectory(_directory);
        var seen = File.Exists(_webhookPath)
            ? await File.ReadAllLinesAsync(_webhookPath, ct).ConfigureAwait(false)
            : [];
        foreach (var line in seen)
        {
            if (line.Contains($"\"eventId\":\"{webhook.EventId}\"", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        await AtomicFileWriter.AppendLinesAsync(
            _webhookPath,
            [JsonSerializer.Serialize(webhook, JsonOptions)],
            ct).ConfigureAwait(false);
        await UpdateItemStatusAsync(
            webhook.ItemId,
            ResolveStatus(webhook.WebhookCode),
            webhook.WebhookType,
            webhook.WebhookCode,
            null,
            ct).ConfigureAwait(false);
        return true;
    }

    public async Task RecordTransferAsync(PlaidTransferResult result, CancellationToken ct = default)
    {
        Directory.CreateDirectory(_directory);
        await AtomicFileWriter.AppendLinesAsync(
            _transferPath,
            [JsonSerializer.Serialize(result, JsonOptions)],
            ct).ConfigureAwait(false);
    }

    public static string BuildAccessTokenKey(string itemId)
        => $"AccessToken:{itemId}";

    public static string BuildWebhookEventId(string itemId, string webhookType, string webhookCode, string payload)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
        return $"{itemId}:{webhookType}:{webhookCode}:{hash[..16]}";
    }

    private async Task<PlaidRepositoryState> ReadStateAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await LoadStateCoreAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<PlaidRepositoryState> LoadStateCoreAsync(CancellationToken ct)
    {
        if (!File.Exists(_statePath))
        {
            return new PlaidRepositoryState();
        }

        await using var stream = File.OpenRead(_statePath);
        return await JsonSerializer.DeserializeAsync<PlaidRepositoryState>(stream, JsonOptions, ct).ConfigureAwait(false)
               ?? new PlaidRepositoryState();
    }

    private async Task WriteStateCoreAsync(PlaidRepositoryState state, CancellationToken ct)
    {
        Directory.CreateDirectory(_directory);
        await using var stream = new MemoryStream();
        await JsonSerializer.SerializeAsync(stream, state, JsonOptions, ct).ConfigureAwait(false);
        await AtomicFileWriter.WriteAsync(_statePath, stream.ToArray(), ct).ConfigureAwait(false);
    }

    private static PlaidItemStatusDto ResolveStatus(string webhookCode)
        => webhookCode.Equals("ERROR", StringComparison.OrdinalIgnoreCase)
            ? PlaidItemStatusDto.Error
            : webhookCode.Contains("PENDING_EXPIRATION", StringComparison.OrdinalIgnoreCase) ||
              webhookCode.Contains("LOGIN_REPAIRED", StringComparison.OrdinalIgnoreCase)
                ? PlaidItemStatusDto.RequiresUpdate
                : PlaidItemStatusDto.Linked;

    private sealed class PlaidRepositoryState
    {
        public Dictionary<string, PlaidItemDto> Items { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, PlaidAccountDto> Accounts { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
