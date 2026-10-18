using Meridian.Contracts.Ledger;

namespace Meridian.Contracts.AssetOperations;

/// <summary>
/// A versioned corporate-action methodology profile: the source-proposed treatment of each
/// normalized action type under each accounting basis. A profile is a projection input, not GAAP,
/// statutory or tax authority, and policy-dependent branches stay explicit so a vendor default can
/// never be mistaken for an approved conclusion. The domain projector depends on this contract
/// only; the vendor-specific profiles live outside the domain and are injected by the composition
/// root.
/// </summary>
public interface ICorporateActionRuleProfile
{
    /// <summary>Stable profile identifier (persisted in projection fingerprints and methodology references).</summary>
    string ProfileId { get; }

    string ProfileVersion { get; }

    /// <summary><see cref="ProfileId"/> and <see cref="ProfileVersion"/> joined by a slash.</summary>
    string ProfileKey { get; }

    string SelectedRuleVersion { get; }

    /// <summary>Meridian adoption date of this captured methodology; not when the underlying methodology took effect.</summary>
    DateOnly EffectiveFrom { get; }

    bool IsEffectiveOn(DateOnly evaluationDate);

    CorporateActionTreatmentDecisionDto Resolve(
        CorporateActionAccountingTypeDto actionType,
        AccountingBasisKindDto accountingBasis);
}
