using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Meridian.Contracts.Workstation;

namespace Meridian.Wpf.Services;

internal static class DesktopLaunchTicketClient
{
    public static async Task<string[]> ResolveAsync(
        IReadOnlyList<string> args,
        CancellationToken cancellationToken = default)
    {
        var ticket = ReadValue(args, "--launch-ticket=");
        var host = ReadValue(args, "--host=");
        var safeArgs = args
            .Where(arg => !arg.StartsWith("--launch-ticket=", StringComparison.OrdinalIgnoreCase) &&
                          !arg.StartsWith("--host=", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (string.IsNullOrWhiteSpace(ticket) || !TryGetLoopbackHost(host, out var hostUri))
        {
            return safeArgs.ToArray();
        }

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var redemptionUri = new Uri(
            hostUri,
            $"/api/auth/desktop-launch/{Uri.EscapeDataString(ticket)}");
        using var response = await client.GetAsync(redemptionUri, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return safeArgs.ToArray();
        }

        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        var redemption = await JsonSerializer.DeserializeAsync<DesktopLaunchTicketRedemptionDto>(
            stream,
            new JsonSerializerOptions(JsonSerializerDefaults.Web),
            cancellationToken).ConfigureAwait(false);
        if (redemption is not null && !string.IsNullOrWhiteSpace(redemption.Page))
        {
            safeArgs.Add($"--page={redemption.Page}");
        }

        return safeArgs.ToArray();
    }

    private static string? ReadValue(IReadOnlyList<string> args, string prefix) =>
        args.FirstOrDefault(arg => arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))?[prefix.Length..];

    private static bool TryGetLoopbackHost(string? value, out Uri hostUri)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out var parsed) &&
            parsed.Scheme == Uri.UriSchemeHttp &&
            IPAddress.TryParse(parsed.Host, out var address) &&
            IPAddress.IsLoopback(address))
        {
            hostUri = parsed;
            return true;
        }

        hostUri = null!;
        return false;
    }
}
