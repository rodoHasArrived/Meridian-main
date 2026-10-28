using System.Text.Json.Nodes;
using Meridian.Contracts.Text;

namespace Meridian.Contracts.SecurityMaster;

/// <summary>How one asset-class terms-version step changes a flat payload (ADR-024 decision 3).</summary>
public enum SecurityTermsUpgradeKind
{
    /// <summary>A version-n payload already reads correctly as version n+1; the reader keeps the stored element.</summary>
    Identity = 0,

    /// <summary>The step rewrites the payload in memory (a rename, a retype, a new default) and ships in and out fixtures.</summary>
    Transform = 1
}

/// <summary>
/// One declared step of an asset class's flat-terms evolution, from <see cref="FromVersion"/> to
/// <see cref="ToVersion"/>. Declared on <see cref="SecurityAssetClassDescriptor.TermsUpgrades"/>, run
/// on read by <see cref="SecurityAssetClassTermsUpgrader"/>, and checked by
/// <c>SecurityAssetClassTermsHistoryTests</c> against the committed version manifests under
/// <c>tests/fixtures/security-master/terms-versions</c>.
/// </summary>
/// <remarks>
/// Built only through <see cref="Identity"/> and <see cref="Transforming"/>, so an identity step never
/// carries a delegate, a transform step always does, and no step starts below version 1. Record
/// equality compares <see cref="Transform"/> by reference. The type is never serialized.
/// </remarks>
public sealed record SecurityAssetClassTermsUpgrade
{
    private SecurityAssetClassTermsUpgrade(
        int fromVersion,
        SecurityTermsUpgradeKind kind,
        string rationale,
        Func<JsonObject, JsonObject>? transform)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(fromVersion, SecurityTermsEnvelope.DefaultAssetClassTermsVersion);
        FromVersion = fromVersion;
        Kind = kind;
        Rationale = TextPrimitives.RequireText(rationale);
        Transform = transform;
    }

    /// <summary>The terms version this step reads.</summary>
    public int FromVersion { get; }

    /// <summary>The terms version this step produces: always <see cref="FromVersion"/> + 1.</summary>
    public int ToVersion => FromVersion + 1;

    /// <summary>Whether the step keeps the stored payload or rewrites it.</summary>
    public SecurityTermsUpgradeKind Kind { get; }

    /// <summary>Why the step is what it is: what the next version changed and why that reads this way.</summary>
    public string Rationale { get; }

    /// <summary>
    /// <see langword="null"/> for an identity step. For a transform step: receives a private, mutable
    /// copy of the version-<see cref="FromVersion"/> payload (envelope keys included) and returns the
    /// version-<see cref="ToVersion"/> payload. It may mutate and return its argument. It must not
    /// return <see langword="null"/> or change <c>schemaVersion</c>, and it must leave a payload that
    /// is already in the version-<see cref="ToVersion"/> shape unchanged, because an unstamped payload
    /// reads as version 1 even when a current writer built it. The runner restamps
    /// <c>termsFormat</c> and <c>assetClassTermsVersion</c> afterwards.
    /// </summary>
    public Func<JsonObject, JsonObject>? Transform { get; }

    /// <summary>A step whose next version reads every version-<paramref name="fromVersion"/> payload unchanged.</summary>
    public static SecurityAssetClassTermsUpgrade Identity(int fromVersion, string rationale)
        => new(fromVersion, SecurityTermsUpgradeKind.Identity, rationale, transform: null);

    /// <summary>A step that rewrites a version-<paramref name="fromVersion"/> payload in memory on read.</summary>
    public static SecurityAssetClassTermsUpgrade Transforming(
        int fromVersion,
        string rationale,
        Func<JsonObject, JsonObject> transform)
    {
        ArgumentNullException.ThrowIfNull(transform);
        return new(fromVersion, SecurityTermsUpgradeKind.Transform, rationale, transform);
    }
}
