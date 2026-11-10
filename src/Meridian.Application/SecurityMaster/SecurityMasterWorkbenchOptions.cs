namespace Meridian.Application.SecurityMaster;

/// <summary>
/// The governance profile a deployment declares for the Security Master governed-write surface
/// (<see cref="SecurityMasterWorkbenchOptions.GovernanceProfile"/>).
/// </summary>
public enum SecurityMasterGovernanceProfile
{
    /// <summary>
    /// Each governance requirement follows its own setting; all three default to off. The code
    /// default, because the supported local workstation is single-operator.
    /// </summary>
    Standard = 0,

    /// <summary>
    /// Governed term amendments, independent field-edit review and independent profile approval are
    /// all in force and cannot be turned off. A production composition always runs this profile.
    /// </summary>
    Institutional = 1
}

/// <summary>
/// Runtime configuration for the Security Master Passport Workbench governed-write surface.
/// Use <see cref="Microsoft.Extensions.Options.IOptionsMonitor{TOptions}"/> so the source-precedence
/// ladder hot-reloads.
/// </summary>
/// <remarks>
/// Do not read <see cref="RequireGovernedTermAmendments"/>, <see cref="RequireIndependentReviewer"/> or
/// <see cref="RequireIndependentProfileApprover"/> to decide enforcement. They are the configured
/// values; the effective requirements, which also account for <see cref="GovernanceProfile"/> and the
/// host's deployment posture, come from <see cref="ISecurityMasterGovernanceRequirements"/>.
/// </remarks>
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
    /// The governance profile this deployment declares. Default <see cref="SecurityMasterGovernanceProfile.Standard"/>.
    /// The effective profile is <see cref="SecurityMasterGovernanceProfile.Institutional"/> when this says so
    /// or when the host is a production composition
    /// (<c>ProductionServiceRegistrationPolicy.IsProductionComposition</c>).
    /// </summary>
    /// <remarks>
    /// <para>Under the Institutional profile <see cref="RequireGovernedTermAmendments"/>,
    /// <see cref="RequireIndependentReviewer"/> and <see cref="RequireIndependentProfileApprover"/> are
    /// all in force and cannot be turned off. Configuration that sets any of them to false, or sets
    /// this to Standard on a production composition, refuses host start naming the key; a hot reload
    /// to such a value is ignored and logged, and the requirement stays in force.</para>
    /// <para>The code default stays Standard with all three off because the supported local
    /// workstation is single-operator: maker-checker with one actor can never approve anything, so
    /// defaulting on would dead-end local use.</para>
    /// <para>Limit of the Institutional profile: it does not recheck records that carry no maker.
    /// A field-edit revision submitted before the drafter check existed no longer carries its
    /// drafter, so only its submitter is excluded from approving it. An asset-profile draft without a
    /// recorded <c>DraftedBy</c> is checked against its recorded author and drafted audit events;
    /// no maker is invented for either.</para>
    /// <para>Changing this value on a running host takes effect at the next read, like the three
    /// settings it governs.</para>
    /// </remarks>
    public SecurityMasterGovernanceProfile GovernanceProfile { get; init; }

    /// <summary>
    /// When true, a governed field-edit revision cannot be reviewed by the actor who drafted it or
    /// approved by the actor who submitted it (submission already refuses a reviewer equal to the
    /// submitter). The code default stays false because the supported local workstation is
    /// single-operator and one actor could then never approve anything; the sample configuration
    /// turns it on, and the Institutional <see cref="GovernanceProfile"/> forces it on.
    /// Read at each submit and approve, so it hot-reloads. Revisions submitted before the drafter
    /// check existed are not rechecked against their drafter, whose identity a submitted revision no
    /// longer carries.
    /// </summary>
    public bool RequireIndependentReviewer { get; init; }

    /// <summary>
    /// When true, approving an asset-profile version is refused if the approver is an actor who
    /// drafted it. The code default stays false for the same single-operator reason as
    /// <see cref="RequireIndependentReviewer"/>; the Institutional <see cref="GovernanceProfile"/>
    /// forces it on. Separate from <see cref="RequireIndependentReviewer"/>, which governs field-edit
    /// revisions. A legacy draft without a recorded drafter is checked against its recorded author.
    /// </summary>
    public bool RequireIndependentProfileApprover { get; init; }

    /// <summary>Maximum number of conflicts a single bulk-resolve request may carry.</summary>
    public int MaxBulkResolveBatch { get; init; } = 200;

    /// <summary>
    /// When true, the DIRECT term-amendment endpoints — the generic amend route, the bespoke
    /// preferred/convertible equity PATCH routes, and the legacy
    /// <c>/equities/{securityId}/preferred-terms</c> alias — are refused with guidance to use the governed
    /// workbench Draft → Submitted → Approved → Published path, so every operator term correction
    /// passes maker-checker before it reaches the golden record. The code default stays false: the
    /// supported local workstation is single-operator, where a governed edit could never be approved.
    /// The Institutional <see cref="GovernanceProfile"/> forces it on. Governed publication and
    /// provider/system ingest use separate typed write paths this does not block.
    /// </summary>
    public bool RequireGovernedTermAmendments { get; init; }
}
