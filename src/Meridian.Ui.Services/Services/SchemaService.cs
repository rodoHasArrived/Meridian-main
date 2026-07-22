namespace Meridian.Ui.Services;

/// <summary>
/// Default schema service for the shared UI services layer, backed entirely by
/// <see cref="SchemaServiceBase"/>. The WPF host extends the same base with desktop
/// persistence (see <c>Meridian.Wpf.Services.SchemaService</c>).
/// </summary>
public sealed class SchemaService : SchemaServiceBase
{
    private static readonly Lazy<SchemaService> _instance = new(() => new SchemaService());

    public static SchemaService Instance => _instance.Value;
}
