using Meridian.Identity.Auth;

namespace Meridian.Wpf.Services;

/// <summary>
/// Answers whether the desktop operator may perform a governed write that requires a specific
/// <see cref="UserPermission"/>. Narrow on purpose, like <see cref="IDesktopActorSource"/>: view
/// models that gate a mutation command need only this, and depending on it rather than the whole
/// session keeps them testable without standing up a real login session. Attribution and
/// authorization stay separate seams — an actor source says who to record a write against, this
/// says whether the write may be accepted at all.
/// </summary>
public interface IDesktopMutationAuthorization
{
    /// <summary>
    /// Returns <c>true</c> when the current desktop session may perform a write requiring
    /// <paramref name="permission"/>.
    /// </summary>
    bool IsGranted(UserPermission permission);
}

/// <summary>
/// The production <see cref="IDesktopMutationAuthorization"/>: decides from the desktop session via
/// <see cref="DesktopMutationPermissionResolver"/>, so dialog view models composed by a parent that
/// holds the (nullable) session share one decision path with the parent's own command enablement.
/// </summary>
public sealed class DesktopMutationAuthorization(DesktopAuthenticationSession? session) : IDesktopMutationAuthorization
{
    public bool IsGranted(UserPermission permission)
        => DesktopMutationPermissionResolver.IsGranted(session, permission);
}

/// <summary>
/// Resolves whether the desktop operator holds the permission a governed mutation requires, so the
/// WPF shell refuses the same writes the browser lane does.
///
/// <para>The desktop reaches services such as <c>ISecurityMasterService</c> in-process rather than
/// through the HTTP routes, so no endpoint filter stands between an operator and the golden record.
/// Every HTTP route that mutates the Security Master golden record requires
/// <see cref="UserPermission.ModifySecurityMaster"/>; this resolver holds the desktop mutation
/// commands to the same grant.</para>
///
/// <para>Deliberately not built on <see cref="DesktopAuthenticationSession.HasPermission"/> alone.
/// That method answers true to every permission on a credential-free host, so a host that names an
/// anonymous role in <c>MDC_ANONYMOUS_ROLE</c> — an explicit choice about what an unauthenticated
/// operator may do, which the browser lane honours by refusing mutations — would stay fully mutable
/// through the workstation. When that variable names a role, the decision comes from the role's own
/// grants in <see cref="RolePermissions"/>, exactly as
/// <see cref="DesktopWorkflowReadScopeResolver"/> already resolves read scope and for the same
/// reason: the session answers true to every permission on such a host and would therefore ignore
/// the choice. A named-but-unrecognised role refuses, because a typo in a security setting must
/// never grant everything.</para>
///
/// <para>A null session, or a credential-free host that names no anonymous role, keeps the shell's
/// fail-open unconfigured local-development posture — the same decision
/// <see cref="DesktopAuthenticationSession.HasPermission"/> and the read-scope resolver already
/// make for that host. On a credential-backed host the session's own fail-closed check decides.</para>
/// </summary>
internal static class DesktopMutationPermissionResolver
{
    private const string AnonymousRoleEnvironmentVariable = "MDC_ANONYMOUS_ROLE";

    public static bool IsGranted(DesktopAuthenticationSession? session, UserPermission permission)
    {
        if (TryResolveConfiguredAnonymousPermissions(session, out var granted))
        {
            return (granted & permission) == permission;
        }

        if (session is null)
        {
            // No session at all means the shell was composed without authentication -- the
            // unconfigured local-development posture, not a restricted operator.
            return true;
        }

        return session.HasPermission(permission);
    }

    /// <summary>
    /// The permissions a named anonymous role grants, when this host runs credential-free and has
    /// named one. Resolved from <see cref="RolePermissions"/> rather than from the session, because
    /// the session answers true to every permission on such a host and would therefore ignore the
    /// choice.
    /// </summary>
    private static bool TryResolveConfiguredAnonymousPermissions(
        DesktopAuthenticationSession? session,
        out UserPermission granted)
    {
        granted = UserPermission.None;
        if (session is not null && !session.CanContinueWithoutCredentials)
        {
            return false;
        }

        var configured = Environment.GetEnvironmentVariable(AnonymousRoleEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(configured))
        {
            // Unset is not misconfigured. The shell's own fail-open decision stands.
            return false;
        }

        // Named but unrecognised is misconfigured, and it must refuse rather than fall through to
        // the session, which would grant everything -- the one outcome a misconfigured security
        // setting must never produce. RolePermissions.TryParseRoleName is the shared name-only
        // parser: Enum.TryParse would accept "0" and resolve it to Admin.
        if (RolePermissions.TryParseRoleName(configured, out var role))
        {
            granted = RolePermissions.For(role);
        }

        return true;
    }
}
