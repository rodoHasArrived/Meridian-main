using FluentAssertions;
using Meridian.Ui.Shared.Endpoints;
using Xunit;

namespace Meridian.Tests.Architecture;

/// <summary>
/// Meridian.Workstation.Abstractions holds the seams that domain services share with the web
/// adapter (docs/engineering/plans/ui-shared-decomposition-plan.md, Phase 1a). It must stay free of
/// ASP.NET Core and must never depend back on Meridian.Ui.Shared, or the WPF host and the extracted
/// domain projects would inherit the web stack again.
/// </summary>
[Trait("Category", "Architecture")]
public sealed class WorkstationAbstractionsBoundaryTests
{
    private static readonly System.Reflection.Assembly AbstractionsAssembly =
        typeof(IWorkstationTenantContextAccessor).Assembly;

    [Fact]
    public void TenantContextSeam_LivesInAbstractionsAssembly()
        => AbstractionsAssembly.GetName().Name.Should().Be("Meridian.Workstation.Abstractions");

    [Fact]
    public void TenantContextSeam_RetainsLegacyUiSharedIdentity()
        => Type.GetType($"{typeof(IWorkstationTenantContextAccessor).FullName}, Meridian.Ui.Shared", throwOnError: true)
            .Should().Be(typeof(IWorkstationTenantContextAccessor));

    [Fact]
    public void Abstractions_DoesNotReference_AspNetCoreAssemblies()
    {
        var aspNet = AbstractionsAssembly
            .GetReferencedAssemblies()
            .Select(static reference => reference.Name ?? string.Empty)
            .Where(static name => name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal))
            .ToArray();

        aspNet.Should().BeEmpty("Meridian.Workstation.Abstractions must stay usable from WPF and domain projects");
    }

    [Theory]
    [InlineData("Debug")]
    [InlineData("Release")]
    public async Task Abstractions_ShouldNot_Reference_UiShared_DirectlyOrTransitively(string configuration)
    {
        var repositoryRoot = FindRepositoryRoot();
        await ProjectReferenceGraph.AssertNoDependencyAsync(
            Path.Combine(repositoryRoot, "src", "Meridian.Workstation.Abstractions", "Meridian.Workstation.Abstractions.csproj"),
            Path.Combine(repositoryRoot, "src", "Meridian.Ui.Shared", "Meridian.Ui.Shared.csproj"),
            configuration);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Meridian.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the repository root from the test output directory.");
    }
}
