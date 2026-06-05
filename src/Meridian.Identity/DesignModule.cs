namespace Meridian.Identity;

/// <summary>Physical module marker for the Identity bounded context.</summary>
public static class DesignModule
{
    public const string Name = "Meridian.Identity";
    public const string Conformance = "physical";
    public const string BoundedContext = "Identity & Access";
    public const string PrimaryCurrentOwner = "Meridian.Identity";
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
        "src/Meridian.Identity",
        "src/Meridian.Application",
        "src/Meridian.Ui.Shared",
        "src/Meridian.Wpf",
        "src/Meridian.Ui/dashboard"
    ];
}
