namespace Meridian.Ui.Services;

/// <summary>
/// Default schema service for the shared UI services layer.
/// Platform-specific projects (WPF) override this with their own implementations
/// by setting the Instance property during app startup.
/// </summary>
public sealed class SchemaService : SchemaServiceBase
{
    private static readonly Lazy<SchemaService> _instance = new(() => new SchemaService());

    public static SchemaService Instance => _instance.Value;
}
