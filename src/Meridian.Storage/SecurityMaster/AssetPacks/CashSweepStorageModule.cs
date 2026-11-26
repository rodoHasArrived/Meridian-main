using Meridian.Contracts.SecurityMaster;
using static Meridian.Storage.SecurityMaster.AssetPacks.SecurityTermsStorageColumns;

namespace Meridian.Storage.SecurityMaster.AssetPacks;

/// <summary>Class-owned relational projection contribution; retained column and reader semantics are unchanged.</summary>
internal static class CashSweepStorageModule
{
    internal static SecurityTermsStorageContribution Contribution { get; } =
        SecurityTermsStorageContribution.Declarative(new(
            AssetClass: "CashSweep",
            TableName: "cash_sweep_projection",
            Columns:
            [
                Gate("program_name", "programName", SecurityAssetTermFieldType.String),
                Gate("sweep_vehicle_type", "sweepVehicleType", SecurityAssetTermFieldType.String),
                Optional("sweep_frequency", "sweepFrequency", SecurityAssetTermFieldType.String),
                Optional("target_account_type", "targetAccountType", SecurityAssetTermFieldType.String),
                Optional("yield_rate", "yieldRate", SecurityAssetTermFieldType.Decimal)
            ],
            ChildTables: []));
}
