namespace Meridian.Reporting;

/// <summary>Physical module marker for the Reporting bounded context.</summary>
public static class DesignModule
{
    public const string Name = "Meridian.Reporting";
    public const string Conformance = "physical";
    public const string BoundedContext = "Reporting & Client Delivery";
    public const string PrimaryCurrentOwner = "Workstation Shell and UX";
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
        "src/Meridian.Contracts",
        "src/Meridian.Ledger",
        "src/Meridian.Ui.Shared",
        "src/Meridian.Ui.Services",
        "src/Meridian.Wpf",
        "src/Meridian.Ui/dashboard"
    ];
}
