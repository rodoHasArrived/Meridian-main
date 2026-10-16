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

    [Fact]
    public async Task ExpandingPropertyCycle_ShouldFailClosed_WithDiagnostic()
    {
        using var fixture = new ProjectGraphFixture();
        var storage = fixture.WriteProject("Storage");
        fixture.WriteProject("Shared", """
            <ItemGroup><ProjectReference Include="../Infrastructure/Infrastructure.csproj"
                AdditionalProperties="Marker=$(Marker)x" /></ItemGroup>
            """);
        var infrastructure = fixture.WriteProject("Infrastructure", """
            <ItemGroup><ProjectReference Include="../Shared/Shared.csproj"
                AdditionalProperties="Marker=$(Marker)x" /></ItemGroup>
            """);
        var failure = await Assert.ThrowsAsync<TrueException>(() =>
            ProjectReferenceGraph.AssertNoDependencyAsync(infrastructure, storage, "Release"));
        Assert.Contains("context budget exceeded", failure.Message);
        Assert.Contains("non-stabilizing property cycle", failure.Message);
    }

    [Fact]
    public async Task ResidualEscapedAssignmentDelimiter_ShouldMatch_RealMSBuildTask()
    {
        using var fixture = new ProjectGraphFixture();
        var storage = fixture.WriteProject("Storage");
        fixture.WriteProject("Shared", """
            <ItemGroup Condition="'$(Marker)' == 'west%253Beast=1'">
              <ProjectReference Include="../Storage/Storage.csproj" />
            </ItemGroup>
            <Target Name="Probe"><Error Condition="'$(Marker)' != 'west%253Beast=1'" Text="Wrong Marker: $(Marker)" /></Target>
            """);
        var infrastructure = fixture.WriteProject("Infrastructure", """
            <ItemGroup><ProjectReference Include="../Shared/Shared.csproj"
                AdditionalProperties="Marker=west%253Beast=1" /></ItemGroup>
            <Target Name="Probe"><MSBuild Projects="@(ProjectReference)" Targets="Probe" /></Target>
            """);
        await AssertRealMSBuildProbeAsync(infrastructure);
        var failure = await Assert.ThrowsAsync<TrueException>(() =>
            ProjectReferenceGraph.AssertNoDependencyAsync(infrastructure, storage, "Release"));
        Assert.Contains("Infrastructure -> Shared -> Storage", failure.Message);
    }

    [Theory]
    [InlineData("west%3B", "west")]
    [InlineData("west%3B%3Beast", "west;east")]
    public async Task EmptyEscapedFragments_ShouldMatch_RealMSBuildTask(string encoded, string expected)
    {
        using var fixture = new ProjectGraphFixture();
        var storage = fixture.WriteProject("Storage");
        fixture.WriteProject("Shared", $"""
            <ItemGroup Condition="'$(Marker)' == '{expected}'"><ProjectReference Include="../Storage/Storage.csproj" /></ItemGroup>
            <Target Name="Probe"><Error Condition="'$(Marker)' != '{expected}'" Text="Wrong empty fragments: $(Marker)" /></Target>
            """);
        var infrastructure = fixture.WriteProject("Infrastructure", $"""
            <ItemGroup><ProjectReference Include="../Shared/Shared.csproj" AdditionalProperties="Marker={encoded}" /></ItemGroup>
            <Target Name="Probe"><MSBuild Projects="@(ProjectReference)" Targets="Probe" /></Target>
            """);
        await AssertRealMSBuildProbeAsync(infrastructure);
        var failure = await Assert.ThrowsAsync<TrueException>(() =>
            ProjectReferenceGraph.AssertNoDependencyAsync(infrastructure, storage, "Release"));
        Assert.Contains("Infrastructure -> Shared -> Storage", failure.Message);
    }

    [Fact]
    public async Task TargetLevelRemovals_ShouldMatch_RealMSBuildTask()
    {
        using var fixture = new ProjectGraphFixture();
        var storage = fixture.WriteProject("Storage");
        fixture.WriteProject("Shared", """
            <PropertyGroup><Configuration Condition="'$(Configuration)' == ''">ChildDefault</Configuration></PropertyGroup>
            <ItemGroup Condition="'$(Configuration)' == 'ChildDefault'"><ProjectReference Include="../Storage/Storage.csproj" /></ItemGroup>
            <Target Name="Probe"><Error Condition="'$(Configuration)' != 'ChildDefault'" Text="Configuration was not removed" /></Target>
            """);
        var infrastructure = fixture.WriteProject("Infrastructure", """
            <PropertyGroup><_GlobalPropertiesToRemoveFromProjectReferences>Configuration</_GlobalPropertiesToRemoveFromProjectReferences></PropertyGroup>
            <ItemGroup><ProjectReference Include="../Shared/Shared.csproj" /></ItemGroup>
            <Target Name="Probe"><MSBuild Projects="@(ProjectReference)" Targets="Probe"
                RemoveProperties="$(_GlobalPropertiesToRemoveFromProjectReferences)" /></Target>
            """);
        await AssertRealMSBuildProbeAsync(infrastructure);
        var failure = await Assert.ThrowsAsync<TrueException>(() =>
            ProjectReferenceGraph.AssertNoDependencyAsync(infrastructure, storage, "Release"));
        Assert.Contains("Infrastructure -> Shared -> Storage", failure.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PreparedFrameworkAndPlatform_ShouldMatch_RealMSBuildTask(bool dynamicPlatform)
    {
        using var fixture = new ProjectGraphFixture();
        var storage = fixture.WriteProject("Storage");
        fixture.WriteSdkProject("Shared", """
            <PropertyGroup><TargetFrameworks>net9.0;net10.0</TargetFrameworks><Platforms>x64</Platforms><Platform>x64</Platform></PropertyGroup>
            <ItemGroup Condition="'$(TargetFramework)' == 'net10.0' and '$(Platform)' == 'x64'">
              <ProjectReference Include="../Storage/Storage.csproj" SkipGetTargetFrameworkProperties="true" />
            </ItemGroup>
            <Target Name="Probe"><Error Condition="'$(TargetFramework)' != 'net10.0' or '$(Platform)' != 'x64'" Text="Wrong negotiated framework/platform" /></Target>
            """);
        var infrastructure = fixture.WriteSdkProject("Infrastructure", $"""
            <PropertyGroup><TargetFramework>net10.0</TargetFramework><Platform>{(dynamicPlatform ? "AnyCPU" : "x64")}</Platform>
              <EnableDynamicPlatformResolution>{dynamicPlatform.ToString().ToLowerInvariant()}</EnableDynamicPlatformResolution>
              <PlatformLookupTable>AnyCPU=x64</PlatformLookupTable>
            </PropertyGroup>
            <ItemGroup><ProjectReference Include="../Shared/Shared.csproj" /></ItemGroup>
            <Target Name="Probe" DependsOnTargets="PrepareProjectReferences">
              <MSBuild Projects="@(_MSBuildProjectReferenceExistent)" Targets="Probe"
                Properties="%(_MSBuildProjectReferenceExistent.SetConfiguration);%(_MSBuildProjectReferenceExistent.SetPlatform);%(_MSBuildProjectReferenceExistent.SetTargetFramework)"
                RemoveProperties="%(_MSBuildProjectReferenceExistent.GlobalPropertiesToRemove);$(_GlobalPropertiesToRemoveFromProjectReferences)" />
            </Target>
            """);
        Assert.Empty(Directory.EnumerateFiles(fixture.Root, "*.cs", SearchOption.AllDirectories));
        await AssertRealMSBuildProbeAsync(infrastructure);
        var failure = await Assert.ThrowsAsync<TrueException>(() =>
            ProjectReferenceGraph.AssertNoDependencyAsync(infrastructure, storage, "Release"));
        Assert.Contains("Infrastructure -> Shared -> Storage", failure.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NonstandardPreparation_ShouldPreserve_UnrepresentedDeclaredReferences(bool partialPreparation)
    {
        using var fixture = new ProjectGraphFixture();
        var storage = fixture.WriteProject("Storage");
        fixture.WriteProject("Contracts");
        var infrastructure = fixture.WriteProject("Infrastructure", $"""
            <ItemGroup><ProjectReference Include="../Storage/Storage.csproj" ReferenceOutputAssembly="false" /></ItemGroup>
            <Target Name="PrepareProjectReferences">
              {(partialPreparation ? "<ItemGroup><_MSBuildProjectReferenceExistent Include=\"../Contracts/Contracts.csproj\" /></ItemGroup>" : "")}
            </Target>
            """);
        Assert.Empty(Directory.EnumerateFiles(fixture.Root, "*.cs", SearchOption.AllDirectories));
        var failure = await Assert.ThrowsAsync<TrueException>(() =>
            ProjectReferenceGraph.AssertNoDependencyAsync(infrastructure, storage, "Release"));
        Assert.Contains("Infrastructure -> Storage", failure.Message);
    }

    [Theory]
    [InlineData("PrepareProjectReferences")]
    [InlineData("prepareprojectreferences")]
    public async Task RepresentedPreparation_ShouldUse_NegotiatedContextWithoutRawDuplicate(string targetName)
    {
        using var fixture = new ProjectGraphFixture();
        var storage = fixture.WriteProject("Storage");
        fixture.WriteProject("Shared", """
            <ItemGroup Condition="'$(IncludeStorage)' != 'false'"><ProjectReference Include="../Storage/Storage.csproj" /></ItemGroup>
            <Target Name="Probe"><Error Condition="'$(IncludeStorage)' != 'false'" Text="Prepared context was lost" /></Target>
            """);
        var infrastructure = fixture.WriteProject("Infrastructure", $"""
            <ItemGroup><ProjectReference Include="../Shared/Shared.csproj" /></ItemGroup>
            <Target Name="{targetName}">
              <ItemGroup><_MSBuildProjectReferenceExistent Include="../Shared/Shared.csproj" AdditionalProperties="IncludeStorage=false" /></ItemGroup>
            </Target>
            <Target Name="Probe" DependsOnTargets="PrepareProjectReferences">
              <MSBuild Projects="@(_MSBuildProjectReferenceExistent)" Targets="Probe" />
            </Target>
            """);
        await AssertRealMSBuildProbeAsync(infrastructure);
        await ProjectReferenceGraph.AssertNoDependencyAsync(infrastructure, storage, "Release");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PartialPreparation_ShouldPreserve_DistinctSamePathProperties(bool enabledFirst)
    {
        using var fixture = new ProjectGraphFixture();
        var storage = fixture.WriteProject("Storage");
        fixture.WriteProject("Shared", """
            <ItemGroup Condition="'$(IncludeStorage)' == 'true'"><ProjectReference Include="../Storage/Storage.csproj" /></ItemGroup>
            """);
        var infrastructure = fixture.WriteProject("Infrastructure", $"""
            <ItemGroup>
              <ProjectReference Include="../Shared/Shared.csproj" AdditionalProperties="IncludeStorage={enabledFirst.ToString().ToLowerInvariant()}" />
              <ProjectReference Include="../Shared/Shared.csproj" AdditionalProperties="IncludeStorage={(!enabledFirst).ToString().ToLowerInvariant()}" />
            </ItemGroup>
            <Target Name="PrepareProjectReferences">
              <ItemGroup><_MSBuildProjectReferenceExistent Include="../Shared/Shared.csproj" AdditionalProperties="IncludeStorage=false" /></ItemGroup>
            </Target>
            """);
        var failure = await Assert.ThrowsAsync<TrueException>(() =>
            ProjectReferenceGraph.AssertNoDependencyAsync(infrastructure, storage, "Release"));
        Assert.Contains("Infrastructure -> Shared -> Storage", failure.Message);
    }

    [Fact]
    public async Task PartialPreparation_ShouldPreserve_DistinctSamePathToolsets()
    {
        using var fixture = new ProjectGraphFixture();
        var storage = fixture.WriteProject("Storage");
        fixture.WriteProject("Shared");
        var infrastructure = fixture.WriteProject("Infrastructure", """
            <ItemGroup>
              <ProjectReference Include="../Shared/Shared.csproj" ToolsVersion="Current" />
              <ProjectReference Include="../Shared/Shared.csproj" ToolsVersion="BoundaryUnsupportedToolset" />
            </ItemGroup>
            <Target Name="PrepareProjectReferences">
              <ItemGroup><_MSBuildProjectReferenceExistent Include="../Shared/Shared.csproj" ToolsVersion="Current" /></ItemGroup>
            </Target>
            """);
        var failure = await Assert.ThrowsAsync<TrueException>(() =>
            ProjectReferenceGraph.AssertNoDependencyAsync(infrastructure, storage, "Release"));
        Assert.Contains("BoundaryUnsupportedToolset", failure.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnsupportedReferenceToolsVersion_ShouldFailClosed_LikeRealMSBuildTask(bool repeatPath)
    {
        using var fixture = new ProjectGraphFixture();
        var storage = fixture.WriteProject("Storage");
        fixture.WriteProject("Shared", "<Target Name=\"Probe\" />");
        var infrastructure = fixture.WriteProject("Infrastructure", $"""
            <ItemGroup>
              {(repeatPath ? "<ProjectReference Include=\"../Shared/Shared.csproj\" ToolsVersion=\"Current\" />" : "")}
              <ProjectReference Include="../Shared/Shared.csproj" ToolsVersion="BoundaryUnsupportedToolset" />
            </ItemGroup>
            <Target Name="Probe"><MSBuild Projects="@(ProjectReference)" Targets="Probe" /></Target>
            """);
        var taskFailure = await Assert.ThrowsAsync<TrueException>(() => AssertRealMSBuildProbeAsync(infrastructure));
        Assert.Contains("MSB4132", taskFailure.Message);
        var graphFailure = await Assert.ThrowsAsync<TrueException>(() =>
            ProjectReferenceGraph.AssertNoDependencyAsync(infrastructure, storage, "Release"));
        Assert.Contains("BoundaryUnsupportedToolset", graphFailure.Message);
        Assert.Contains("MSBuild could not evaluate", graphFailure.Message);
    }

    private static async Task AssertRealMSBuildProbeAsync(string project)
    {
        var start = new System.Diagnostics.ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in new[] { "msbuild", project, "-nologo", "-verbosity:quiet", "-target:Probe", "-maxcpucount:1", "-nodeReuse:false", "-property:Configuration=Release" })
        {
            start.ArgumentList.Add(argument);
        }
        using var process = System.Diagnostics.Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            throw;
        }
        Assert.True(process.ExitCode == 0, await output + await error);
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

        public string WriteSdkProject(string name, string contents)
        {
            var path = WriteProject(name, contents);
            // Fixtures have no packages or compiled sources. Keep the real SDK reference
            // negotiation while avoiding unrelated package-assets resolution.
            File.WriteAllText(path, File.ReadAllText(path).Replace("<Project>",
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup>" +
                "<SkipResolvePackageAssets>true</SkipResolvePackageAssets></PropertyGroup>"));
            return path;
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
