namespace Meridian.Application.SecurityMaster;

/// <summary>
/// Runtime configuration for the Security Master Passport Workbench governed-write surface.
/// Use <see cref="Microsoft.Extensions.Options.IOptionsMonitor{TOptions}"/> so the source-precedence
/// ladder hot-reloads.
/// </summary>
public sealed class SecurityMasterWorkbenchOptions
{
    public const string SectionName = "SecurityMasterWorkbench";

    /// <summary>
    /// Source precedence ladder, highest authority first, using real source-system identifiers. The
    /// first non-operator entry is treated as the golden-copy source unless <see cref="GoldenCopySource"/>
    /// is set explicitly. Empty by default: until this is bound from configuration with the deployment's
    /// real source systems, the policy applies no presumptive ranking and falls through to freshness then
    /// confidence, so a placeholder ladder can never reverse the actual golden copy.
    /// </summary>
    public List<string> SourcePrecedence { get; init; } = [];

    /// <summary>Explicit golden-copy source; when null it is derived from <see cref="SourcePrecedence"/>.</summary>
    public string? GoldenCopySource { get; init; }

    /// <summary>
    /// When true, a governed field-edit revision cannot be reviewed by the actor who drafted it or
    /// approved by the actor who submitted it (submission already refuses a reviewer equal to the
    /// submitter). Default false preserves existing behavior; the sample configuration turns it on.
    /// Read at each submit and approve, so it hot-reloads. Revisions submitted before the drafter
    /// check existed are not rechecked against their drafter, whose identity a submitted revision no
    /// longer carries.
    /// </summary>
    public bool RequireIndependentReviewer { get; init; }

    /// <summary>
    /// When true, approving an asset-profile version is refused if the approver is the actor who
    /// drafted it. Default false preserves existing behavior: no deployment default changes until a
    /// separate decision is made on deployment defaults and legacy-draft handling. Separate from
    /// <see cref="RequireIndependentReviewer"/>, which governs field-edit revisions.
    /// </summary>
    public bool RequireIndependentProfileApprover { get; init; }

    /// <summary>Maximum number of conflicts a single bulk-resolve request may carry.</summary>
    public int MaxBulkResolveBatch { get; init; } = 200;

    /// <summary>
    /// When true, the DIRECT term-amendment endpoints — the generic amend route, the bespoke
    /// preferred/convertible equity PATCH routes, and the legacy
    /// <c>/equities/{securityId}/preferred-terms</c> alias — are refused with guidance to use the governed
    /// workbench Draft → Submitted → Approved → Published path, so every operator term correction
    /// passes maker-checker before it reaches the golden record. Default false preserves the
    /// direct write surface for deployments whose provider-ingest pipelines call these routes;
    /// deployments with independent-reviewer requirements should enable it.
    /// </summary>
    public bool RequireGovernedTermAmendments { get; init; }
}
