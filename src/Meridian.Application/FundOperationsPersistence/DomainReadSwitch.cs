namespace Meridian.Application.FundOperationsPersistence;

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
