using System.Net;
using System.Security.Cryptography;
using System.Text;
using Meridian.Identity;
using Meridian.Identity.Auth;

namespace Meridian.Ui.Shared.Services;

/// <summary>Creates the first local administrator through a loopback-only, one-use launcher token.</summary>
public sealed class InitialAccountBootstrapService(IUserAccountStore accountStore, LoginSessionService sessions)
{
    public const string TokenEnvironmentVariable = "MDC_BOOTSTRAP_TOKEN";
    private readonly SemaphoreSlim _gate = new(1, 1);

    public bool IsAvailable => !accountStore.HasAccounts && !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(TokenEnvironmentVariable));

    public async Task<string?> CreateAsync(IPAddress? remoteAddress, string? suppliedToken, string username, string password, CancellationToken ct)
    {
        if (remoteAddress is not null && !IPAddress.IsLoopback(remoteAddress))
            return null;
        if (string.IsNullOrWhiteSpace(username) || username.Length > 80 || password.Length < 12)
            return null;

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (accountStore.HasAccounts)
                return null;
            var expected = Environment.GetEnvironmentVariable(TokenEnvironmentVariable);
            if (!TokenEquals(expected, suppliedToken))
                return null;

            await accountStore.UpsertAsync(
                new UserAccountUpsertRequestDto(
                    username.Trim(), UserRole.Admin.ToString(), null, null, password, null, false, false,
                    "local-installer", "Create the first local Meridian administrator.", "initial-account-bootstrap", "local"),
                "local-installer", ct).ConfigureAwait(false);

            Environment.SetEnvironmentVariable(TokenEnvironmentVariable, null);
            return sessions.CreateSession(username.Trim(), password);
        }
        finally { _gate.Release(); }
    }

    private static bool TokenEquals(string? expected, string? supplied)
    {
        if (string.IsNullOrWhiteSpace(expected) || string.IsNullOrWhiteSpace(supplied))
            return false;
        var left = Encoding.UTF8.GetBytes(expected);
        var right = Encoding.UTF8.GetBytes(supplied);
        return left.Length == right.Length && CryptographicOperations.FixedTimeEquals(left, right);
    }
}
