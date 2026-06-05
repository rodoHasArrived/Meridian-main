namespace Meridian.Instruments;

/// <summary>Physical module marker for the Instruments bounded context.</summary>
public static class DesignModule
{
    public const string Name = "Meridian.Instruments";
    public const string Conformance = "physical";
    public const string BoundedContext = "Instrument, Contract & Obligation";
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
        "src/Meridian.FSharp",
        "src/Meridian.FSharp.DirectLending.Aggregates",
        "src/Meridian.FSharp.Ledger",
        "src/Meridian.Ledger",
        "src/Meridian.Storage"
    ];
}
