namespace Meridian.Workflow;

/// <summary>Physical module marker for the Workflow bounded context.</summary>
public static class DesignModule
{
    public const string Name = "Meridian.Workflow";
    public const string Conformance = "physical";
    public const string BoundedContext = "Workflow & Task";
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
        "src/Meridian.Workflow",
        "src/Meridian.Application",
        "src/Meridian.Contracts",
        "src/Meridian.Ui.Shared",
        "src/Meridian.Wpf",
        "src/Meridian.Ui/dashboard"
    ];
}
