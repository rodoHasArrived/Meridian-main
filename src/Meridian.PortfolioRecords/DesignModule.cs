namespace Meridian.PortfolioRecords;

/// <summary>Physical module marker for the Portfolio Records bounded context.</summary>
public static class DesignModule
{
    public const string Name = "Meridian.PortfolioRecords";
    public const string Conformance = "physical";
    public const string BoundedContext = "Portfolio Records";
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
        "src/Meridian.Ledger",
        "src/Meridian.Storage",
        "src/Meridian.Ui.Shared",
        "src/Meridian.Wpf",
        "src/Meridian.Ui/dashboard"
    ];
}
