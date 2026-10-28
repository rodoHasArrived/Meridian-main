namespace Meridian.Platform.FundOperationsPersistence;

/// <summary>Selects legacy or persisted-projection readers according to the domain cutover configuration.</summary>
public static class DomainReadSwitch
{
    public static T Select<T>(
        FundOperationsPersistenceOptions options,
        FundOperationsDomain domain,
        Func<T> legacyReader,
        Func<T> persistedReader)
    {
        var mode = options.GetMode(domain);
        return mode.ReadMode == DomainReadMode.PersistedProjection
            ? persistedReader()
            : legacyReader();
    }
}
