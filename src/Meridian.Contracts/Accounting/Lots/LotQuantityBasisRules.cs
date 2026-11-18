using Meridian.Contracts.SecurityMaster;

namespace Meridian.Contracts.Accounting.Lots;

/// <summary>
/// Which <see cref="LotQuantityBasis"/> an acquisition of a Security Master asset class may state.
/// <para>
/// A lot is booked by face only when its class is denominated in face value
/// (<see cref="SecurityAssetClassDescriptor.LotUnit"/> is <see cref="SecurityLotUnitKind.FaceValue"/>):
/// a Face lot of an equity, option or fund would scale quantity and cost by a par basis the security
/// does not have. The converse is not enforced. A face-value class (a deposit, repo or loan, for
/// example) may still be posted by units when the posting carries no face-value terms; the
/// acquisition's own <c>FaceValueTerms</c> decide that.
/// </para>
/// </summary>
public static class LotQuantityBasisRules
{
    /// <summary>
    /// Whether an acquisition of <paramref name="assetClass"/> may state <paramref name="basis"/>.
    /// An unknown class is treated as unit-denominated, so it may not be booked by face.
    /// </summary>
    public static bool Permits(string? assetClass, LotQuantityBasis basis)
        => basis != LotQuantityBasis.Face
           || SecurityAssetClassCatalog.GetOrDefault(assetClass).LotUnit == SecurityLotUnitKind.FaceValue;
}
