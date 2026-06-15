namespace Meridian.Contracts.Configuration;

public static class ProviderSetupStateProjection
{
    public static ProviderSetupProjectionDto Project(
        ProviderCredentialStateDto credentialState,
        ProviderVerificationStateDto verificationState,
        string? environment,
        string? connectionMode,
        bool bindingEnabled,
        bool liveApproved = false)
    {
        var env = environment?.Trim();
        var mode = connectionMode?.Trim();
        var isLive = EqualsIgnoreCase(env, "live") || EqualsIgnoreCase(mode, "Live");
        var isPaper = EqualsIgnoreCase(env, "paper") || EqualsIgnoreCase(mode, "Paper");
        var requiresCredentials = credentialState is not ProviderCredentialStateDto.NotRequired;

        if (credentialState is ProviderCredentialStateDto.Missing or ProviderCredentialStateDto.Partial)
        {
            return Projection(ProviderSetupStateDto.CredentialsMissing, "Required credential fields are missing.", "Routing disabled until required credentials are saved.");
        }

        if (verificationState == ProviderVerificationStateDto.Failed || credentialState == ProviderCredentialStateDto.Invalid)
        {
            return Projection(ProviderSetupStateDto.VerificationFailed, "Credential verification failed. Re-enter credentials and verify this provider.", "Routing disabled until provider verification succeeds.");
        }

        if (requiresCredentials && verificationState is ProviderVerificationStateDto.NotVerified or ProviderVerificationStateDto.Stale)
        {
            return Projection(ProviderSetupStateDto.CredentialsSavedVerificationRequired, "Credentials saved. Verify before using this provider.", "Routing disabled until saved credentials are verified.");
        }

        if (isLive && !liveApproved)
        {
            return Projection(ProviderSetupStateDto.LiveRequiresApproval, "Live endpoint selected. Routing remains disabled until approval.", "Live endpoint selected. Routing remains disabled until approval.");
        }

        if (!bindingEnabled)
        {
            return Projection(ProviderSetupStateDto.VerifiedRoutingDisabled, "Provider verified, but routing bindings are disabled.", "Routing binding is disabled.");
        }

        if (isLive)
        {
            return Projection(ProviderSetupStateDto.LiveReady, "Provider verified for live workflows.", null);
        }

        if (isPaper)
        {
            return Projection(ProviderSetupStateDto.ReadyPaper, "Provider verified for paper workflows.", null);
        }

        return Projection(ProviderSetupStateDto.ReadyReadOnly, "Provider verified for read-only workflows.", null);
    }

    private static ProviderSetupProjectionDto Projection(ProviderSetupStateDto state, string explanation, string? routingDisabledReason)
        => new(state, explanation, routingDisabledReason);

    private static bool EqualsIgnoreCase(string? value, string expected)
        => string.Equals(value, expected, StringComparison.OrdinalIgnoreCase);
}

public sealed record ProviderSetupProjectionDto(
    ProviderSetupStateDto State,
    string Explanation,
    string? RoutingDisabledReason);
