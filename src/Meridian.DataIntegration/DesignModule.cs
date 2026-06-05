namespace Meridian.DataIntegration;

/// <summary>Physical module marker for the Data Integration bounded context.</summary>
public static class DesignModule
{
    public const string Name = "Meridian.DataIntegration";
    public const string Conformance = "physical";
    public const string BoundedContext = "Data Provider & Integration";
    public const string PrimaryCurrentOwner = "Data Confidence and Validation";
    public static readonly string[] RequiredFacets =
    [
        "Domain model",
        "Application services",
        "Contracts / APIs",
        "Infrastructure",
        "UI components",
        "Tests"
    ];

    public static readonly string[] CurrentSourcePaths =
    [
        "src/Meridian.ProviderSdk",
        "src/Meridian.Infrastructure",
        "src/Meridian.Storage",
        "src/Meridian.Application",
        "src/Meridian.Ui.Shared",
        "src/Meridian.Ui/dashboard"
    ];
}
