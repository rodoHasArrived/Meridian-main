namespace Meridian.Platform;

/// <summary>Physical module marker for the Platform bounded context.</summary>
public static class DesignModule
{
    public const string Name = "Meridian.Platform";
    public const string Conformance = "physical";
    public const string BoundedContext = "Platform Runtime";
    public const string PrimaryCurrentOwner = "Runtime Host";
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
        "src/Meridian",
        "src/Meridian.Core",
        "src/Meridian.Application",
        "src/Meridian.Storage"
    ];
}
