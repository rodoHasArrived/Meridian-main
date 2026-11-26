namespace Meridian.Storage.SecurityMaster.AssetPacks;

/// <summary>OCC option identity and contract rows retain their specialized writer.</summary>
internal static class OptionStorageModule
{
    internal static SecurityTermsStorageContribution Contribution { get; } =
        SecurityTermsStorageContribution.Specialized(
            "Option", "OCC option contract projection is written by the specialized Option writer.",
            static (store, connection, transaction, record, ct) =>
                store.UpsertOptionProjectionTablesAsync(connection, transaction, record, ct));
}
