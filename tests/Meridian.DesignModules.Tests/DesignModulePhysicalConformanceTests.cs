using Xunit;

namespace Meridian.DesignModules.Tests;

public sealed class DesignModulePhysicalConformanceTests
{
    [Fact]
    public void DesignModuleProjectsExposeCanonicalPhysicalDescriptors()
    {
        var descriptors = new (string Name, string Conformance, string BoundedContext, string Owner, IReadOnlyList<string> Facets, IReadOnlyList<string> Sources)[]
        {
            (global::Meridian.Platform.DesignModule.Name, global::Meridian.Platform.DesignModule.Conformance, global::Meridian.Platform.DesignModule.BoundedContext, global::Meridian.Platform.DesignModule.PrimaryCurrentOwner, global::Meridian.Platform.DesignModule.RequiredFacets, global::Meridian.Platform.DesignModule.CurrentSourcePaths),
            (global::Meridian.Identity.DesignModule.Name, global::Meridian.Identity.DesignModule.Conformance, global::Meridian.Identity.DesignModule.BoundedContext, global::Meridian.Identity.DesignModule.PrimaryCurrentOwner, global::Meridian.Identity.DesignModule.RequiredFacets, global::Meridian.Identity.DesignModule.CurrentSourcePaths),
            (global::Meridian.Entities.DesignModule.Name, global::Meridian.Entities.DesignModule.Conformance, global::Meridian.Entities.DesignModule.BoundedContext, global::Meridian.Entities.DesignModule.PrimaryCurrentOwner, global::Meridian.Entities.DesignModule.RequiredFacets, global::Meridian.Entities.DesignModule.CurrentSourcePaths),
            (global::Meridian.DataIntegration.DesignModule.Name, global::Meridian.DataIntegration.DesignModule.Conformance, global::Meridian.DataIntegration.DesignModule.BoundedContext, global::Meridian.DataIntegration.DesignModule.PrimaryCurrentOwner, global::Meridian.DataIntegration.DesignModule.RequiredFacets, global::Meridian.DataIntegration.DesignModule.CurrentSourcePaths),
            (global::Meridian.ReferenceData.DesignModule.Name, global::Meridian.ReferenceData.DesignModule.Conformance, global::Meridian.ReferenceData.DesignModule.BoundedContext, global::Meridian.ReferenceData.DesignModule.PrimaryCurrentOwner, global::Meridian.ReferenceData.DesignModule.RequiredFacets, global::Meridian.ReferenceData.DesignModule.CurrentSourcePaths),
            (global::Meridian.Instruments.DesignModule.Name, global::Meridian.Instruments.DesignModule.Conformance, global::Meridian.Instruments.DesignModule.BoundedContext, global::Meridian.Instruments.DesignModule.PrimaryCurrentOwner, global::Meridian.Instruments.DesignModule.RequiredFacets, global::Meridian.Instruments.DesignModule.CurrentSourcePaths),
            (global::Meridian.PortfolioRecords.DesignModule.Name, global::Meridian.PortfolioRecords.DesignModule.Conformance, global::Meridian.PortfolioRecords.DesignModule.BoundedContext, global::Meridian.PortfolioRecords.DesignModule.PrimaryCurrentOwner, global::Meridian.PortfolioRecords.DesignModule.RequiredFacets, global::Meridian.PortfolioRecords.DesignModule.CurrentSourcePaths),
            (global::Meridian.FinancialOperations.DesignModule.Name, global::Meridian.FinancialOperations.DesignModule.Conformance, global::Meridian.FinancialOperations.DesignModule.BoundedContext, global::Meridian.FinancialOperations.DesignModule.PrimaryCurrentOwner, global::Meridian.FinancialOperations.DesignModule.RequiredFacets, global::Meridian.FinancialOperations.DesignModule.CurrentSourcePaths),
            (global::Meridian.Workflow.DesignModule.Name, global::Meridian.Workflow.DesignModule.Conformance, global::Meridian.Workflow.DesignModule.BoundedContext, global::Meridian.Workflow.DesignModule.PrimaryCurrentOwner, global::Meridian.Workflow.DesignModule.RequiredFacets, global::Meridian.Workflow.DesignModule.CurrentSourcePaths),
            (global::Meridian.Audit.DesignModule.Name, global::Meridian.Audit.DesignModule.Conformance, global::Meridian.Audit.DesignModule.BoundedContext, global::Meridian.Audit.DesignModule.PrimaryCurrentOwner, global::Meridian.Audit.DesignModule.RequiredFacets, global::Meridian.Audit.DesignModule.CurrentSourcePaths),
            (global::Meridian.Reporting.DesignModule.Name, global::Meridian.Reporting.DesignModule.Conformance, global::Meridian.Reporting.DesignModule.BoundedContext, global::Meridian.Reporting.DesignModule.PrimaryCurrentOwner, global::Meridian.Reporting.DesignModule.RequiredFacets, global::Meridian.Reporting.DesignModule.CurrentSourcePaths),
            (global::Meridian.Documents.DesignModule.Name, global::Meridian.Documents.DesignModule.Conformance, global::Meridian.Documents.DesignModule.BoundedContext, global::Meridian.Documents.DesignModule.PrimaryCurrentOwner, global::Meridian.Documents.DesignModule.RequiredFacets, global::Meridian.Documents.DesignModule.CurrentSourcePaths)
        };

        Assert.Equal(ExpectedModules, descriptors.Select(descriptor => descriptor.Name).ToArray());
        Assert.Equal(ExpectedBoundedContexts, descriptors.Select(descriptor => descriptor.BoundedContext).ToArray());
        Assert.All(descriptors, descriptor =>
        {
            Assert.Equal("physical", descriptor.Conformance);
            Assert.False(string.IsNullOrWhiteSpace(descriptor.Owner));
            Assert.Equal(ExpectedFacets, descriptor.Facets);
            Assert.NotEmpty(descriptor.Sources);
        });
    }

    [Fact]
    public void DesignDocumentAdaptationCoversContextsWorkspacesAndScreens()
    {
        var root = FindRepositoryRoot();
        var adaptationMap = File.ReadAllText(Path.Combine(root, "docs", "architecture", "design-document-adaptation.yml"));

        foreach (var module in ExpectedModules)
        {
            Assert.Contains($"name: {module}", adaptationMap);
            Assert.Contains($"physical_project: src/{module}", adaptationMap);
        }

        foreach (var context in ExpectedBoundedContexts.Skip(1))
        {
            Assert.Contains($"name: {context}", adaptationMap);
        }

        foreach (var workspace in ExpectedRootWorkspaces)
        {
            Assert.Contains($"    - {workspace}", adaptationMap);
        }

        foreach (var screen in ExpectedMvpScreens)
        {
            Assert.Contains($"  - name: {screen}", adaptationMap);
        }
    }

    [Fact]
    public void DesignModuleProjectsArePresentInTheSolutionAndConformanceMap()
    {
        var root = FindRepositoryRoot();
        var solution = File.ReadAllText(Path.Combine(root, "Meridian.sln"));
        var conformanceMap = File.ReadAllText(Path.Combine(root, "docs", "architecture", "design-module-conformance.yml"));

        foreach (var module in ExpectedModules)
        {
            var projectPath = $"src\\{module}\\{module}.csproj";
            Assert.Contains(projectPath, solution);
            Assert.Contains($"physical_project: src/{module}", conformanceMap);
        }
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Meridian.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate Meridian.sln from the test output path.");
    }

    private static readonly string[] ExpectedModules =
    [
        "Meridian.Platform",
        "Meridian.Identity",
        "Meridian.Entities",
        "Meridian.DataIntegration",
        "Meridian.ReferenceData",
        "Meridian.Instruments",
        "Meridian.PortfolioRecords",
        "Meridian.FinancialOperations",
        "Meridian.Workflow",
        "Meridian.Audit",
        "Meridian.Reporting",
        "Meridian.Documents"
    ];

    private static readonly string[] ExpectedBoundedContexts =
    [
        "Platform Runtime",
        "Identity & Access",
        "Entity & Relationship",
        "Data Provider & Integration",
        "Reference Data",
        "Instrument, Contract & Obligation",
        "Portfolio Records",
        "Financial Operations",
        "Workflow & Task",
        "Audit & Compliance",
        "Reporting & Client Delivery",
        "Document & Knowledge"
    ];

    private static readonly string[] ExpectedFacets =
    [
        "Domain model",
        "Application services",
        "Contracts / APIs",
        "Infrastructure",
        "UI components",
        "Tests"
    ];

    private static readonly string[] ExpectedRootWorkspaces =
    [
        "Trading",
        "Portfolio",
        "Accounting",
        "Reporting",
        "Strategy",
        "Data",
        "Settings"
    ];

    private static readonly string[] ExpectedMvpScreens =
    [
        "Home Dashboard",
        "Provider Center",
        "Import Runs",
        "Data Quality",
        "Entity Directory",
        "Account Directory",
        "Instrument / Contract Registry",
        "Portfolio Records",
        "Reconciliation Workbench",
        "Exception Queue",
        "Workflow Tasks",
        "Audit Log",
        "Document Vault",
        "Reporting Packages",
        "Administration Settings"
    ];
}
