using System.Text.Json;
using Meridian.Contracts.Configuration;

namespace Meridian.DataIntegration.Credentials;

public interface IProviderVerificationHistoryReader
{
    Task<ProviderVerificationHistoryDto> GetVerificationHistoryAsync(
        string providerId, ProviderCredentialScope scope, int limit = 100, CancellationToken ct = default);
}

public sealed partial class FileProviderCredentialStore : IProviderVerificationHistoryReader
{
    /// <summary>Reads existing audit evidence; never probes a provider or invents earlier observations.</summary>
    public async Task<ProviderVerificationHistoryDto> GetVerificationHistoryAsync(
        string providerId, ProviderCredentialScope scope, int limit = 100, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (limit is < 1 or > 500) throw new ArgumentOutOfRangeException(nameof(limit));
        var descriptor = RequireDescriptor(providerId);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // An unappended committed event makes the apparent history incomplete. Do not return
            // that partial log as complete, and do not repair persistence as a side effect of a read.
            if (CountPendingAuditRecords() > 0)
                throw new IOException("Provider verification history has pending audit publication.");
            if (!File.Exists(_auditPath))
                return new(scope.ConnectionId, descriptor.ProviderId, DateTimeOffset.UtcNow, [], false);
            if (new FileInfo(_auditPath).Length > 32 * 1024 * 1024)
                throw new IOException("Provider verification history exceeds the bounded read size.");

            var observations = new PriorityQueue<ProviderVerificationObservationDto, DateTimeOffset>();
            var matched = 0;
            var asOf = DateTimeOffset.UtcNow;
            using var reader = new StreamReader(new FileStream(_auditPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
            while (await reader.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                // The audit also contains OAuth/migration events with another shape. Read only
                // verification envelopes and refuse malformed JSON instead of silently dropping it.
                using var document = JsonDocument.Parse(line);
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                    throw new InvalidDataException("Provider audit envelope is unavailable.");
                if (!document.RootElement.TryGetProperty("action", out var action)) continue;
                if (action.ValueKind != JsonValueKind.String)
                    throw new InvalidDataException("Provider audit action is unavailable.");
                if (action.GetString() is not ("verify-success" or "verify-failure")) continue;
                var entry = JsonSerializer.Deserialize<ProviderCredentialAuditEntry>(line, JsonOptions)
                    ?? throw new InvalidDataException("Provider verification audit entry is unavailable.");
                if (entry.Scope != scope || !string.Equals(entry.ProviderId, descriptor.ProviderId, StringComparison.OrdinalIgnoreCase)) continue;
                if (entry.Timestamp == default || entry.Timestamp > asOf)
                    throw new InvalidDataException("Provider verification timestamp is unavailable.");
                matched++;
                observations.Enqueue(new(entry.Timestamp, entry.Action == "verify-success"), entry.Timestamp);
                if (observations.Count > limit) observations.Dequeue();
            }
            return new(scope.ConnectionId, descriptor.ProviderId, asOf,
                observations.UnorderedItems.Select(item => item.Element).OrderBy(item => item.RecordedAtUtc).ToArray(), matched > limit);
        }
        finally { _gate.Release(); }
    }
}
