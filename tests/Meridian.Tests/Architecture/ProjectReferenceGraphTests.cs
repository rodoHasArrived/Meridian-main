using Xunit;
using Xunit.Sdk;

namespace Meridian.Tests.Architecture;

public sealed class ProjectReferenceGraphTests
{
    [Theory]
    [InlineData("../Storage/Storage.csproj")]
    [InlineData("..\\Storage\\Storage.csproj")]
    public async Task UnusedProjectReference_ShouldFail_WithoutAnyCompiledTypes(string reference)
    {
        using var fixture = new ProjectGraphFixture();
        var storage = fixture.WriteProject("Storage");
        var infrastructure = fixture.WriteProject("Infrastructure", $"""
            <ItemGroup>
              <ProjectReference Include="{reference}" ReferenceOutputAssembly="false" />
            </ItemGroup>
            """);

        // These projects have no source files or SDK imports: no compiled type can use the edge.
        Assert.Empty(Directory.EnumerateFiles(fixture.Root, "*.cs", SearchOption.AllDirectories));
        var failure = await Assert.ThrowsAsync<TrueException>(() =>
            ProjectReferenceGraph.AssertNoDependencyAsync(infrastructure, storage, "Release"));

        Assert.Contains("Infrastructure -> Storage", failure.Message);
    }

    [Fact]
    public async Task TransitiveProjectReference_ShouldFail_WithDependencyPath()
    {
        using var fixture = new ProjectGraphFixture();
        var storage = fixture.WriteProject("Storage");
        fixture.WriteProject("Shared", """
            <ItemGroup><ProjectReference Include="../Storage/Storage.csproj" /></ItemGroup>
            """);
        var infrastructure = fixture.WriteProject("Infrastructure", """
            <ItemGroup><ProjectReference Include="../Shared/Shared.csproj" /></ItemGroup>
            """);

        var failure = await Assert.ThrowsAsync<TrueException>(() =>
            ProjectReferenceGraph.AssertNoDependencyAsync(infrastructure, storage, "Release"));

        Assert.Contains("Infrastructure -> Shared -> Storage", failure.Message);
    }

    [Fact]
    public async Task ImportedConditionalProjectReference_ShouldBeEvaluated()
    {
        using var fixture = new ProjectGraphFixture();
        var storage = fixture.WriteProject("Storage");
        var infrastructure = fixture.WriteProject("Infrastructure", """
            <PropertyGroup><StorageProject>../Storage/Storage.csproj</StorageProject></PropertyGroup>
            <Import Project="References.props" />
            """);
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(infrastructure)!, "References.props"), """
            <Project>
              <ItemGroup Condition="'$(Configuration)' == 'Release'">
                <ProjectReference Include="$(StorageProject)" />
              </ItemGroup>
            </Project>
            """);

        await ProjectReferenceGraph.AssertNoDependencyAsync(infrastructure, storage, "Debug");
        var failure = await Assert.ThrowsAsync<TrueException>(() =>
            ProjectReferenceGraph.AssertNoDependencyAsync(infrastructure, storage, "Release"));

        Assert.Contains("Infrastructure -> Storage", failure.Message);
    }

    [Fact]
    public async Task GraphWithoutStoragePath_ShouldTerminate_EvenWithCycle()
    {
        using var fixture = new ProjectGraphFixture();
        var storage = fixture.WriteProject("Storage");
        fixture.WriteProject("Contracts");
        fixture.WriteProject("Shared", """
            <ItemGroup>
              <ProjectReference Include="../Infrastructure/Infrastructure.csproj" />
              <ProjectReference Include="../Contracts/Contracts.csproj" />
            </ItemGroup>
            """);
        var infrastructure = fixture.WriteProject("Infrastructure", """
            <ItemGroup>
              <ProjectReference Include="../Shared/Shared.csproj" />
              <ProjectReference Include="../Contracts/Contracts.csproj" />
            </ItemGroup>
            """);

        await ProjectReferenceGraph.AssertNoDependencyAsync(infrastructure, storage, "Release");
    }

    private sealed class ProjectGraphFixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), $"Meridian-ProjectGraphTests-{Guid.NewGuid():N}");

        public string WriteProject(string name, string contents = "")
        {
            var directory = Path.Combine(Root, name);
            Directory.CreateDirectory(directory);
            var project = Path.Combine(directory, $"{name}.csproj");
            File.WriteAllText(project, $"<Project>{Environment.NewLine}{contents}{Environment.NewLine}</Project>");
            return project;
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
