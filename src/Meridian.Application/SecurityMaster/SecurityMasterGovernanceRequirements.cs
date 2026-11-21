using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Meridian.Application.SecurityMaster;

/// <summary>Why the effective <see cref="SecurityMasterGovernanceProfile"/> is what it is.</summary>
public enum SecurityMasterGovernanceProfileSource
{
    /// <summary>Nothing declared a profile, so the Standard code default applies.</summary>
    Default = 0,

    /// <summary><see cref="SecurityMasterWorkbenchOptions.GovernanceProfile"/> declared it.</summary>
    Configuration = 1,

    /// <summary>
    /// The host is a production composition, which always runs the Institutional profile. Reported in
    /// preference to <see cref="Configuration"/> because configuration cannot change it.
    /// </summary>
    ProductionComposition = 2,

    /// <summary>
    /// This process ran the Institutional profile earlier and configuration no longer declares it.
    /// The profile stays in force until the process restarts; the current configuration is ignored
    /// and <c>SecurityMasterWorkbench:GovernanceProfile</c> is reported as contradicted.
    /// </summary>
    Latched = 3
}

/// <summary>Whether one governance requirement is in force, and what puts it in force.</summary>
public enum SecurityMasterGovernanceRequirementBasis
{
    /// <summary>Not in force: the Standard profile with the requirement's own setting off.</summary>
    NotInForce = 0,

    /// <summary>In force because the requirement's own boolean setting is on (Standard profile).</summary>
    Setting = 1,

    /// <summary>In force because of the Institutional profile; the requirement's own setting cannot turn it off.</summary>
    Profile = 2
}

/// <summary>
/// One consistent reading of the effective Security Master governance requirements, taken from a
/// single options value so the three requirements cannot be read from different reloads.
/// </summary>
/// <param name="Profile">The effective profile.</param>
/// <param name="ProfileSource">Why <paramref name="Profile"/> is in effect.</param>
/// <param name="GovernedTermAmendments">Basis for refusing direct term amendments.</param>
/// <param name="IndependentReviewer">Basis for independent field-edit review.</param>
/// <param name="IndependentProfileApprover">Basis for independent asset-profile approval.</param>
/// <param name="ContradictedKeys">
/// Configuration keys whose explicit value contradicts the effective profile. Each is ignored (the
/// requirement stays in force); host start refuses while any is present.
/// </param>
public sealed record SecurityMasterGovernanceSnapshot(
    SecurityMasterGovernanceProfile Profile,
    SecurityMasterGovernanceProfileSource ProfileSource,
    SecurityMasterGovernanceRequirementBasis GovernedTermAmendments,
    SecurityMasterGovernanceRequirementBasis IndependentReviewer,
    SecurityMasterGovernanceRequirementBasis IndependentProfileApprover,
    IReadOnlyList<string> ContradictedKeys)
{
    /// <summary>
    /// True when the Institutional profile is in force only because this process ran it earlier
    /// (<see cref="SecurityMasterGovernanceProfileSource.Latched"/>), not because the current
    /// configuration or posture declares it. A restart is required to leave it.
    /// </summary>
    public bool IsProfileLatched => ProfileSource == SecurityMasterGovernanceProfileSource.Latched;

    /// <summary>Effective <see cref="SecurityMasterWorkbenchOptions.RequireGovernedTermAmendments"/>.</summary>
    public bool RequireGovernedTermAmendments => GovernedTermAmendments != SecurityMasterGovernanceRequirementBasis.NotInForce;

    /// <summary>Effective <see cref="SecurityMasterWorkbenchOptions.RequireIndependentReviewer"/>.</summary>
    public bool RequireIndependentReviewer => IndependentReviewer != SecurityMasterGovernanceRequirementBasis.NotInForce;

    /// <summary>Effective <see cref="SecurityMasterWorkbenchOptions.RequireIndependentProfileApprover"/>.</summary>
    public bool RequireIndependentProfileApprover => IndependentProfileApprover != SecurityMasterGovernanceRequirementBasis.NotInForce;
}

/// <summary>
/// The one place the three Security Master governance requirements are decided. Every enforcement
/// point (<see cref="GovernedTermAmendmentPolicy"/>, <see cref="SecurityMasterWorkbenchCommandService"/>,
/// <see cref="SecurityAssetProfileGovernanceService"/>, the HTTP routes and the WPF editor) reads the
/// effective values here instead of the raw <see cref="SecurityMasterWorkbenchOptions"/> booleans, so
/// no reader can disagree about whether a requirement is in force.
/// </summary>
public interface ISecurityMasterGovernanceRequirements
{
    /// <summary>The effective requirements now. Read at each decision so the Standard profile hot-reloads.</summary>
    SecurityMasterGovernanceSnapshot Current { get; }
}

/// <summary>
/// Whether the host is a production composition as
/// <see cref="Meridian.Application.Composition.ProductionServiceRegistrationPolicy.IsProductionComposition"/>
/// defines it. Registered by
/// <see cref="Meridian.Application.Composition.SecurityMasterGovernanceServiceCollectionExtensions.AddSecurityMasterGovernanceRequirements"/>.
/// </summary>
public sealed record SecurityMasterGovernanceHostPosture(bool IsProductionComposition)
{
    /// <summary>A host that is not a production composition.</summary>
    public static SecurityMasterGovernanceHostPosture NonProduction { get; } = new(false);

    /// <summary>A production composition.</summary>
    public static SecurityMasterGovernanceHostPosture Production { get; } = new(true);
}

/// <inheritdoc cref="ISecurityMasterGovernanceRequirements" />
/// <remarks>
/// <para>The effective profile is Institutional when
/// <see cref="SecurityMasterWorkbenchOptions.GovernanceProfile"/> says so or the host is a production
/// composition. Under it all three requirements are in force whatever their own settings say. Under
/// the Standard profile each requirement follows its own setting, read from the current options value.</para>
/// <para>The Institutional profile latches. Once an instance has read it as in force (at start, or
/// after a reload that turned it on), a later reload cannot return the instance to Standard: setting
/// <c>GovernanceProfile</c> to Standard or removing the key leaves the profile and all three
/// requirements in force, reported as <see cref="SecurityMasterGovernanceProfileSource.Latched"/> with
/// the profile key contradicted. Only a restart, which composes a new instance, leaves the profile.</para>
/// <para>A configuration value that contradicts the effective profile never relaxes a requirement:
/// it is reported in <see cref="SecurityMasterGovernanceSnapshot.ContradictedKeys"/>, logged once per
/// options value, and refused at host start by the governance profile guard. Whether a key was set
/// explicitly is read from the host's configuration, not from the options value: the binder writes
/// false for an absent key exactly as for an explicit one, so the options cannot tell them apart.</para>
/// </remarks>
public sealed class SecurityMasterGovernanceRequirements : ISecurityMasterGovernanceRequirements, IDisposable
{
    private const string Section = SecurityMasterWorkbenchOptions.SectionName + ":";

    /// <summary>Configuration key of <see cref="SecurityMasterWorkbenchOptions.GovernanceProfile"/>.</summary>
    public const string GovernanceProfileKey = Section + nameof(SecurityMasterWorkbenchOptions.GovernanceProfile);

    /// <summary>Configuration key of <see cref="SecurityMasterWorkbenchOptions.RequireGovernedTermAmendments"/>.</summary>
    public const string RequireGovernedTermAmendmentsKey = Section + nameof(SecurityMasterWorkbenchOptions.RequireGovernedTermAmendments);

    /// <summary>Configuration key of <see cref="SecurityMasterWorkbenchOptions.RequireIndependentReviewer"/>.</summary>
    public const string RequireIndependentReviewerKey = Section + nameof(SecurityMasterWorkbenchOptions.RequireIndependentReviewer);

    /// <summary>Configuration key of <see cref="SecurityMasterWorkbenchOptions.RequireIndependentProfileApprover"/>.</summary>
    public const string RequireIndependentProfileApproverKey = Section + nameof(SecurityMasterWorkbenchOptions.RequireIndependentProfileApprover);

    private readonly IOptionsMonitor<SecurityMasterWorkbenchOptions> _options;
    private readonly bool _isProductionComposition;
    private readonly Func<string, string?> _configuredValue;
    private readonly ILogger<SecurityMasterGovernanceRequirements> _logger;
    private readonly IDisposable? _reloadSubscription;
    // Guards the latch together with the reading it was decided from, so two reads racing a reload
    // cannot cache a Standard reading after the profile has latched.
    private readonly object _gate = new();
    private Reading? _lastReading;
    private SecurityMasterWorkbenchOptions? _lastReportedContradiction;
    private bool _institutionalLatched;

    /// <param name="options">The bound workbench options; read at each decision.</param>
    /// <param name="hostPosture">Whether the host is a production composition.</param>
    /// <param name="configuration">
    /// The host's root configuration, used only to tell which governance keys were set explicitly
    /// and to what. Without it nothing is reported as contradicted; the requirements are in force
    /// all the same.
    /// </param>
    /// <param name="logger">Receives the warning for a contradictory configuration.</param>
    public SecurityMasterGovernanceRequirements(
        IOptionsMonitor<SecurityMasterWorkbenchOptions> options,
        SecurityMasterGovernanceHostPosture hostPosture,
        IConfiguration? configuration = null,
        ILogger<SecurityMasterGovernanceRequirements>? logger = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _isProductionComposition = (hostPosture ?? throw new ArgumentNullException(nameof(hostPosture))).IsProductionComposition;
        _configuredValue = configuration is null ? static _ => null : key => configuration[key];
        _logger = logger ?? NullLogger<SecurityMasterGovernanceRequirements>.Instance;
        // Read the starting value now, so an Institutional start latches even if a reload arrives
        // before anything asks. Options that cannot be bound are left for the first caller of
        // Current, which gets the binder's own failure (the startup guard turns it into a refusal).
        try
        {
            Read(_options.CurrentValue);
        }
        catch (Exception ex) when (ex is InvalidOperationException or OptionsValidationException)
        {
        }

        // Evaluate on reload so a contradictory value is logged when it arrives, not on the next write.
        _reloadSubscription = _options.OnChange(reloaded => Read(reloaded));
    }

    /// <inheritdoc />
    public SecurityMasterGovernanceSnapshot Current => Read(_options.CurrentValue);

    /// <summary>
    /// Decides the effective requirements for one options value. Nothing here can turn a requirement
    /// off under the Institutional profile: <paramref name="configuredValue"/> only decides which keys
    /// are reported as contradicted.
    /// </summary>
    /// <param name="options">The options value to decide for.</param>
    /// <param name="isProductionComposition">Whether the host is a production composition.</param>
    /// <param name="configuredValue">
    /// The raw configuration value for a key such as <see cref="GovernanceProfileKey"/>, or null when
    /// the key is absent. Omit it when no configuration is known; no key is then reported as contradicted.
    /// </param>
    /// <param name="institutionalLatched">
    /// Whether the Institutional profile was already in force earlier in this process. When it was,
    /// options that would yield Standard yield Institutional instead, with the profile key contradicted.
    /// </param>
    public static SecurityMasterGovernanceSnapshot Evaluate(
        SecurityMasterWorkbenchOptions options,
        bool isProductionComposition,
        Func<string, string?>? configuredValue = null,
        bool institutionalLatched = false)
    {
        ArgumentNullException.ThrowIfNull(options);
        configuredValue ??= static _ => null;

        // Anything other than Standard is treated as Institutional, so an undefined numeric value
        // bound from configuration cannot select a weaker profile.
        var declaredInstitutional = options.GovernanceProfile != SecurityMasterGovernanceProfile.Standard;
        var latchedOnly = institutionalLatched && !isProductionComposition && !declaredInstitutional;
        var institutional = isProductionComposition || declaredInstitutional || institutionalLatched;
        var profileSource = isProductionComposition
            ? SecurityMasterGovernanceProfileSource.ProductionComposition
            : latchedOnly
                ? SecurityMasterGovernanceProfileSource.Latched
                : declaredInstitutional || configuredValue(GovernanceProfileKey) is not null
                    ? SecurityMasterGovernanceProfileSource.Configuration
                    : SecurityMasterGovernanceProfileSource.Default;

        // A contradiction is what configuration says, parsed as the binder parses it. A value the
        // binder cannot parse is its failure to report, not a contradiction. A latched profile is
        // contradicted by whatever the profile key now says, an absent key included.
        List<string>? contradicted = null;
        if (latchedOnly
            || (isProductionComposition
                && Enum.TryParse<SecurityMasterGovernanceProfile>(configuredValue(GovernanceProfileKey), ignoreCase: true, out var configuredProfile)
                && configuredProfile == SecurityMasterGovernanceProfile.Standard))
        {
            (contradicted ??= []).Add(GovernanceProfileKey);
        }

        var governedTermAmendments = Resolve(options.RequireGovernedTermAmendments, RequireGovernedTermAmendmentsKey);
        var independentReviewer = Resolve(options.RequireIndependentReviewer, RequireIndependentReviewerKey);
        var independentProfileApprover = Resolve(options.RequireIndependentProfileApprover, RequireIndependentProfileApproverKey);

        return new SecurityMasterGovernanceSnapshot(
            institutional ? SecurityMasterGovernanceProfile.Institutional : SecurityMasterGovernanceProfile.Standard,
            profileSource,
            governedTermAmendments,
            independentReviewer,
            independentProfileApprover,
            contradicted is null ? [] : contradicted.AsReadOnly());

        SecurityMasterGovernanceRequirementBasis Resolve(bool setting, string key)
        {
            if (!institutional)
            {
                return setting
                    ? SecurityMasterGovernanceRequirementBasis.Setting
                    : SecurityMasterGovernanceRequirementBasis.NotInForce;
            }

            if (bool.TryParse(configuredValue(key), out var configured) && !configured)
            {
                (contradicted ??= []).Add(key);
            }

            return SecurityMasterGovernanceRequirementBasis.Profile;
        }
    }

    /// <inheritdoc />
    public void Dispose() => _reloadSubscription?.Dispose();

    private SecurityMasterGovernanceSnapshot Read(SecurityMasterWorkbenchOptions options)
    {
        var last = Volatile.Read(ref _lastReading);
        if (last is not null && ReferenceEquals(last.Options, options))
        {
            return last.Snapshot;
        }

        SecurityMasterGovernanceSnapshot snapshot;
        bool turnedOn;
        bool reportContradiction;
        lock (_gate)
        {
            last = _lastReading;
            if (last is not null && ReferenceEquals(last.Options, options))
            {
                return last.Snapshot;
            }

            snapshot = Evaluate(options, _isProductionComposition, _configuredValue, _institutionalLatched);
            // The latch: from the first Institutional reading on, this instance never reads Standard again.
            _institutionalLatched |= snapshot.Profile == SecurityMasterGovernanceProfile.Institutional;
            turnedOn = last is not null
                && last.Snapshot.Profile == SecurityMasterGovernanceProfile.Standard
                && snapshot.Profile == SecurityMasterGovernanceProfile.Institutional;
            // Once per options value: the monitor hands back the same instance until the next reload.
            reportContradiction = snapshot.ContradictedKeys.Count > 0 && !ReferenceEquals(_lastReportedContradiction, options);
            if (reportContradiction)
            {
                _lastReportedContradiction = options;
            }

            Volatile.Write(ref _lastReading, new Reading(options, snapshot));
        }

        if (turnedOn)
        {
            _logger.LogInformation(
                "Security Master governance profile changed from Standard to Institutional by a configuration reload ({GovernanceProfileSource}); it stays in force until this process restarts",
                snapshot.ProfileSource);
        }

        if (reportContradiction)
        {
            _logger.LogWarning(
                "Security Master governance configuration contradicts the {GovernanceProfile} profile ({GovernanceProfileSource}); the contradicted keys are ignored and every requirement stays in force: {ContradictedKeys}",
                snapshot.Profile,
                snapshot.ProfileSource,
                snapshot.ContradictedKeys);
        }

        return snapshot;
    }

    private sealed record Reading(SecurityMasterWorkbenchOptions Options, SecurityMasterGovernanceSnapshot Snapshot);
}
