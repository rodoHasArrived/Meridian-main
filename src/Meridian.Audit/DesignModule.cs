namespace Meridian.Audit;

/// <summary>Physical module marker for the Audit bounded context.</summary>
public static class DesignModule
{
    public const string Name = "Meridian.Audit";
    public const string Conformance = "physical";
    public const string BoundedContext = "Audit & Compliance";
    public const string PrimaryCurrentOwner = "Accounting and Ledger";
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
        "src/Meridian.Ui.Shared"
    ];
}
