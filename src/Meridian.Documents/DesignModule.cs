namespace Meridian.Documents;

/// <summary>Physical module marker for the Documents bounded context.</summary>
public static class DesignModule
{
    public const string Name = "Meridian.Documents";
    public const string Conformance = "physical";
    public const string BoundedContext = "Document & Knowledge";
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
        "src/Meridian.Contracts",
        "src/Meridian.Storage",
        "src/Meridian.Ui.Shared",
        "src/Meridian.Ui.Services"
    ];
}
