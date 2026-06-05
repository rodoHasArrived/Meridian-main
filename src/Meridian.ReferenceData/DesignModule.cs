namespace Meridian.ReferenceData;

/// <summary>Physical module marker for the Reference Data bounded context.</summary>
public static class DesignModule
{
    public const string Name = "Meridian.ReferenceData";
    public const string Conformance = "physical";
    public const string BoundedContext = "Reference Data";
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
        "src/Meridian.Application",
        "src/Meridian.Contracts",
        "src/Meridian.Storage",
        "src/Meridian.FSharp.DirectLending.Aggregates",
        "src/Meridian.Ui.Shared"
    ];
}
