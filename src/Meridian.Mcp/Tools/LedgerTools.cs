using System.Globalization;
using System.Net;

namespace Meridian.Mcp.Tools;

/// <summary>Read-only accounting access through the workstation API's existing authorization
/// and run-visibility boundary. No archive or database path bypasses that boundary.</summary>
[McpServerToolType]
public sealed class LedgerTools(IHttpClientFactory clients)
{
    public const string HttpClientName = "meridian-ledger-read";

    [McpServerTool(Name = "get_run_trial_balance")]
    [Description("Read a strategy run's trial balance at an inclusive as-of instant. Calls the configured Meridian workstation API and honors its authentication, tenant scope and run visibility. Returns an explicit unavailable/refused error when no authorized retained ledger exists.")]
    public async Task<string> GetRunTrialBalanceAsync(
        [Description("Retained strategy run identifier")] string runId,
        [Description("Inclusive as-of instant with UTC offset, for example 2026-10-31T23:59:59Z")] DateTimeOffset asOf,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        if (runId.Length > 256 || runId.Any(char.IsControl))
            throw new ArgumentException("Run identifier is invalid.", nameof(runId));
        var client = clients.CreateClient(HttpClientName);
        var path = "api/workstation/runs/" + Uri.EscapeDataString(runId.Trim()) +
            "/ledger/trial-balance?asOf=" + Uri.EscapeDataString(asOf.ToString("O", CultureInfo.InvariantCulture));
        try
        {
            using var response = await client.GetAsync(path, HttpCompletionOption.ResponseHeadersRead, ct);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                return "Trial balance access refused by the workstation API. Configure an authorized MeridianApi:BearerToken session.";
            if (response.StatusCode == HttpStatusCode.NotFound)
                return "Trial balance unavailable: the run is not visible or has no retained ledger.";
            if (!response.IsSuccessStatusCode)
                return $"Trial balance unavailable: workstation API returned HTTP {(int)response.StatusCode}.";
            if (response.Content.Headers.ContentLength > 1_048_576)
                return "Trial balance exceeds the MCP response size limit; use the workstation export.";
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var reader = new StreamReader(stream);
            var buffer = new char[1_048_577];
            var count = 0;
            while (count < buffer.Length)
            {
                var read = await reader.ReadAsync(buffer.AsMemory(count), ct);
                if (read == 0)
                    break;
                count += read;
            }
            return count > 1_048_576 ? "Trial balance exceeds the MCP response size limit; use the workstation export." :
                new string(buffer, 0, count);
        }
        catch (HttpRequestException)
        {
            return "Trial balance unavailable: cannot reach the configured workstation API.";
        }
    }
}
