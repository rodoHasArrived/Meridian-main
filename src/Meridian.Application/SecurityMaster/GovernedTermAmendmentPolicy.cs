using Microsoft.Extensions.Options;

namespace Meridian.Application.SecurityMaster;

/// <summary>
/// Whether direct term amendments are refused in favour of the governed workbench
/// (Draft → Submitted → Approved → Published). One policy for every client so the web API and the
/// WPF editor cannot disagree about <see cref="SecurityMasterWorkbenchOptions.RequireGovernedTermAmendments"/>.
/// </summary>
/// <remarks>
/// <see cref="SecurityMasterService"/> enforces it on the operator-direct path
/// (<c>ISecurityMasterService.AmendTermsAsync</c> and its siblings) by raising
/// <see cref="GovernedTermAmendmentRequiredException"/>, so no in-process caller can bypass it. Client
/// edit surfaces also consult it first so they can refuse before building a request. Governed
/// publication (<see cref="IGovernedPublicationAmender"/>) and system ingest
/// (<c>ISecurityMasterAmender</c>) are separate typed paths the policy does not block.
/// </remarks>
public interface IGovernedTermAmendmentPolicy
{
    /// <summary>The refusal an operator should see, or null when direct amendments are permitted.</summary>
    string? RefusalReason { get; }
}

/// <inheritdoc />
public sealed class GovernedTermAmendmentPolicy : IGovernedTermAmendmentPolicy
{
    /// <summary>Problem title used when a direct amendment is refused.</summary>
    public const string RefusalTitle = "Direct term amendments are disabled by governance policy.";

    /// <summary>Guidance used when a direct amendment is refused.</summary>
    public const string RefusalDetail =
        "This deployment requires term corrections to pass maker-checker: stage the edit through the "
        + "Security Master workbench field-edit route and publish it through the governed "
        + "Draft → Submitted → Approved → Published lifecycle instead of amending terms directly.";

    private readonly IOptionsMonitor<SecurityMasterWorkbenchOptions> _options;

    public GovernedTermAmendmentPolicy(IOptionsMonitor<SecurityMasterWorkbenchOptions> options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <inheritdoc />
    public string? RefusalReason
        => _options.CurrentValue.RequireGovernedTermAmendments ? $"{RefusalTitle} {RefusalDetail}" : null;
}

/// <summary>The policy used when none is composed: direct amendments stay permitted.</summary>
internal sealed class PermissiveGovernedTermAmendmentPolicy : IGovernedTermAmendmentPolicy
{
    public static readonly PermissiveGovernedTermAmendmentPolicy Instance = new();

    private PermissiveGovernedTermAmendmentPolicy()
    {
    }

    /// <inheritdoc />
    public string? RefusalReason => null;
}
