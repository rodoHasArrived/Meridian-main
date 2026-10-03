using Xunit;
using Xunit.Sdk;

namespace Meridian.Tests.Architecture;

/// <summary>
/// Protects the architecture gate from source-free and property-conditioned project dependencies.
/// </summary>
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

    [Theory]
    [InlineData("AdditionalProperties")]
    [InlineData("additionalproperties")]
    [InlineData("Properties")]
    public async Task ReferenceProperties_ShouldReveal_SourceFreeConditionalTransitiveEdge(string metadata)
    {
        using var fixture = new ProjectGraphFixture();
        var storage = fixture.WriteProject("Storage");
        fixture.WriteProject("Shared", """
            <ItemGroup Condition="'$(IncludeStorage)' == 'true'">
              <ProjectReference Include="../Storage/Storage.csproj" />
            </ItemGroup>
            """);
        var infrastructure = fixture.WriteProject("Infrastructure", $"""
            <ItemGroup>
              <ProjectReference Include="../Shared/Shared.csproj" {metadata}="IncludeStorage=true" />
            </ItemGroup>
            """);

        Assert.Empty(Directory.EnumerateFiles(fixture.Root, "*.cs", SearchOption.AllDirectories));
        var failure = await Assert.ThrowsAsync<TrueException>(() =>
            ProjectReferenceGraph.AssertNoDependencyAsync(infrastructure, storage, "Release"));

        Assert.Contains("Infrastructure -> Shared -> Storage", failure.Message);
    }

    [Fact]
    public async Task RepeatedProjectPath_ShouldEvaluate_EachPropertyContext()
    {
        using var fixture = new ProjectGraphFixture();
        var storage = fixture.WriteProject("Storage");
        fixture.WriteProject("Shared", """
            <ItemGroup Condition="'$(IncludeStorage)' == 'true'">
              <ProjectReference Include="../Storage/Storage.csproj" />
            </ItemGroup>
            """);
        var infrastructure = fixture.WriteProject("Infrastructure", """
            <ItemGroup>
              <ProjectReference Include="../Shared/Shared.csproj" AdditionalProperties="IncludeStorage=false" />
              <ProjectReference Include="../Shared/Shared.csproj" AdditionalProperties="IncludeStorage=true" />
            </ItemGroup>
            """);

        var failure = await Assert.ThrowsAsync<TrueException>(() =>
            ProjectReferenceGraph.AssertNoDependencyAsync(infrastructure, storage, "Release"));

        Assert.Contains("Infrastructure -> Shared -> Storage", failure.Message);
    }

    [Fact]
    public async Task ReferenceProperties_ShouldInheritAndOverride_WithMSBuildPrecedence()
    {
        using var fixture = new ProjectGraphFixture();
        var storage = fixture.WriteProject("Storage");
        fixture.WriteProject("Shared", """
            <ItemGroup Condition="'$(IncludeStorage)' == 'true' and '$(InheritedMarker)' == 'present' and '$(Configuration)' == 'Release'">
              <ProjectReference Include="../Storage/Storage.csproj" />
            </ItemGroup>
            """);
        fixture.WriteProject("Bridge", """
            <ItemGroup>
              <ProjectReference Include="../Shared/Shared.csproj"
                                SetConfiguration="Configuration=Debug"
                                Properties="IncludeStorage=false"
                                AdditionalProperties="includestorage=true" />
            </ItemGroup>
            """);
        var infrastructure = fixture.WriteProject("Infrastructure", """
            <ItemGroup>
              <ProjectReference Include="../Bridge/Bridge.csproj"
                                AdditionalProperties="IncludeStorage=false;InheritedMarker=present" />
            </ItemGroup>
            """);

        var failure = await Assert.ThrowsAsync<TrueException>(() =>
            ProjectReferenceGraph.AssertNoDependencyAsync(infrastructure, storage, "Release"));

        Assert.Contains("Infrastructure -> Bridge -> Shared -> Storage", failure.Message);
    }

    [Theory]
    [InlineData("SetConfiguration", "Configuration")]
    [InlineData("SetPlatform", "Platform")]
    [InlineData("SetTargetFramework", "TargetFramework")]
    public async Task ReferenceSetProperties_ShouldControl_ChildEvaluation(string metadata, string property)
    {
        using var fixture = new ProjectGraphFixture();
        var storage = fixture.WriteProject("Storage");
        fixture.WriteProject("Shared", $"""
            <ItemGroup Condition="'$({property})' == 'Boundary'">
              <ProjectReference Include="../Storage/Storage.csproj" />
            </ItemGroup>
            """);
        var infrastructure = fixture.WriteProject("Infrastructure", $"""
            <ItemGroup>
              <ProjectReference Include="../Shared/Shared.csproj" {metadata}="{property}=Boundary" />
            </ItemGroup>
            """);

        var failure = await Assert.ThrowsAsync<TrueException>(() =>
            ProjectReferenceGraph.AssertNoDependencyAsync(infrastructure, storage, "Release"));

        Assert.Contains("Infrastructure -> Shared -> Storage", failure.Message);
    }

    [Theory]
    [InlineData("GlobalPropertiesToRemove")]
    [InlineData("UndefineProperties")]
    public async Task RemovedReferenceProperties_ShouldAllow_ChildDefaultsAfterOverrides(string metadata)
    {
        using var fixture = new ProjectGraphFixture();
        var storage = fixture.WriteProject("Storage");
        fixture.WriteProject("Shared", """
            <PropertyGroup>
              <IncludeStorage Condition="'$(IncludeStorage)' == ''">true</IncludeStorage>
            </PropertyGroup>
            <ItemGroup Condition="'$(IncludeStorage)' == 'true' and '$(InheritedMarker)' == 'present'">
              <ProjectReference Include="../Storage/Storage.csproj" />
            </ItemGroup>
            """);
        fixture.WriteProject("Bridge", $"""
            <ItemGroup>
              <ProjectReference Include="../Shared/Shared.csproj"
                                AdditionalProperties="IncludeStorage=false" {metadata}="includestorage" />
            </ItemGroup>
            """);
        var infrastructure = fixture.WriteProject("Infrastructure", """
            <ItemGroup>
              <ProjectReference Include="../Bridge/Bridge.csproj"
                                AdditionalProperties="IncludeStorage=false;InheritedMarker=present" />
            </ItemGroup>
            """);

        var failure = await Assert.ThrowsAsync<TrueException>(() =>
            ProjectReferenceGraph.AssertNoDependencyAsync(infrastructure, storage, "Release"));

        Assert.Contains("Infrastructure -> Bridge -> Shared -> Storage", failure.Message);
    }

    [Fact]
    public async Task ReferencePropertyValues_ShouldPreserve_MSBuildEscaping()
    {
        using var fixture = new ProjectGraphFixture();
        var storage = fixture.WriteProject("Storage");
        fixture.WriteProject("Shared", """
            <ItemGroup Condition="'$(Marker)' == 'west;east,percent%'">
              <ProjectReference Include="../Storage/Storage.csproj" />
            </ItemGroup>
            """);
        var infrastructure = fixture.WriteProject("Infrastructure", """
            <ItemGroup>
              <ProjectReference Include="../Shared/Shared.csproj" AdditionalProperties="Marker=west%3Beast,percent%25" />
            </ItemGroup>
            """);

        var failure = await Assert.ThrowsAsync<TrueException>(() =>
            ProjectReferenceGraph.AssertNoDependencyAsync(infrastructure, storage, "Release"));

        Assert.Contains("Infrastructure -> Shared -> Storage", failure.Message);
    }

    [Fact]
    public async Task EquivalentPropertyContexts_ShouldTerminate_EvenWithCycle()
    {
        using var fixture = new ProjectGraphFixture();
        var storage = fixture.WriteProject("Storage");
        fixture.WriteProject("Shared", """
            <ItemGroup>
              <ProjectReference Include="../Infrastructure/Infrastructure.csproj" AdditionalProperties="marker=present;INCLUDESTORAGE=false" />
              <ProjectReference Include="../Storage/Storage.csproj" Condition="'$(IncludeStorage)' == 'true'" />
            </ItemGroup>
            """);
        var infrastructure = fixture.WriteProject("Infrastructure", """
            <ItemGroup>
              <ProjectReference Include="../Shared/Shared.csproj" AdditionalProperties="IncludeStorage=false;Marker=present" />
            </ItemGroup>
            """);

        await ProjectReferenceGraph.AssertNoDependencyAsync(infrastructure, storage, "Release");
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
