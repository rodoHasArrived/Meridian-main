namespace Meridian.Storage.SecurityMaster.AssetPacks;

/// <summary>Governed profile fields have a dynamic field set and retain their specialized writer.</summary>
internal static class CustomAssetStorageModule
{
    internal static SecurityTermsStorageContribution Contribution { get; } =
        SecurityTermsStorageContribution.Specialized(
            "CustomAsset", "Profile-version-keyed fields use the specialized custom asset profile-field projection.",
            static (store, connection, transaction, record, ct) =>
                store.UpsertCustomAssetProfileFieldProjectionAsync(connection, transaction, record, ct));
}
