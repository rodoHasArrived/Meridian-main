namespace Meridian.FinancialOperations;

/// <summary>Physical module marker for the Financial Operations bounded context.</summary>
public static class DesignModule
{
    public const string Name = "Meridian.FinancialOperations";
    public const string Conformance = "physical";
    public const string BoundedContext = "Financial Operations";
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
        "src/Meridian.FinancialOperations",
        "src/Meridian.Application",
        "src/Meridian.Contracts",
        "src/Meridian.Ledger",
        "src/Meridian.FSharp.Ledger",
        "src/Meridian.Ui.Shared",
        "src/Meridian.Wpf",
        "src/Meridian.Ui/dashboard"
    ];
}
