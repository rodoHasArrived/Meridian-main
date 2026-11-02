using FluentAssertions;
using Meridian.Mcp.Services;
using Xunit;

namespace Meridian.Tests.Mcp;

/// <summary>
/// Root discovery and path-boundary enforcement for the MCP server's repository access.
/// <see cref="RepoPathService.ResolveInsideRoot"/> is the only guard between tool arguments
/// supplied by an MCP client and the filesystem, so these tests attack it directly.
/// </summary>
[Collection(McpRepoRootCollection.CollectionName)]
public sealed class RepoPathServiceTests
{
    // ── Root discovery ───────────────────────────────────────────────────────

    [Fact]
    public void Constructor_EnvironmentRootWithSolution_UsesThatRoot()
    {
        using var repo = new TempMeridianRepo();

        var service = new RepoPathService();

        service.Root.Should().Be(repo.Root);
    }

    [Fact]
    public void Constructor_EnvironmentRootWithoutSolution_ThrowsInsteadOfFallingBack()
    {
        using var repo = new TempMeridianRepo();
        var notARepo = Path.Combine(repo.Sandbox, "not-a-repo");
        Directory.CreateDirectory(notARepo);
        Environment.SetEnvironmentVariable(TempMeridianRepo.EnvVar, notARepo);

        var act = () => new RepoPathService();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*MDC_REPO_ROOT*does not contain 'Meridian.sln'*");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_EnvironmentRootUnsetOrBlank_WalksUpFromTestHostToRealRepository(string? value)
    {
        using var repo = new TempMeridianRepo(setEnvironment: false);
        Environment.SetEnvironmentVariable(TempMeridianRepo.EnvVar, value);

        var service = new RepoPathService();

        File.Exists(Path.Combine(service.Root, "Meridian.sln")).Should().BeTrue();
        Path.GetFullPath(AppContext.BaseDirectory)
            .Should().StartWith(Path.GetFullPath(service.Root) + Path.DirectorySeparatorChar,
                "discovery walks up from the executable directory");
        service.Root.Should().NotBe(repo.Root);
    }

    [Fact]
    public void WellKnownPaths_AreRootedUnderDiscoveredRoot()
    {
        using var repo = new TempMeridianRepo();
        var service = new RepoPathService();

        var paths = new[]
        {
            service.DocsPath, service.AdrPath, service.AiDocsPath, service.GithubPath,
            service.InstructionsPath, service.AgentsPath, service.AiClaudePath, service.TemplatesPath,
            service.AdaptersPath, service.AuditScriptPath, service.AiEditToolPath, service.KnownErrorsFile,
            service.CSharpInstructionsFile, service.CodeReviewAgentFile, service.ProvidersGuideFile,
            service.TestingGuideFile, service.StorageGuideFile, service.FSharpGuideFile
        };

        paths.Should().OnlyContain(path => path.StartsWith(repo.Root + Path.DirectorySeparatorChar, StringComparison.Ordinal));
        service.AiEditToolPath.Should().Be(Path.Combine(repo.Root, "build", "scripts", "ai", "ai-edit-tool.py"));
    }

    // ── Accepted paths ───────────────────────────────────────────────────────

    [Theory]
    [InlineData("docs/README.md")]
    [InlineData("src/Meridian.Mcp/../Meridian.Core/file.cs")]
    [InlineData("./build/./scripts/x.py")]
    [InlineData("does/not/exist/yet.json")]
    public void ResolveInsideRoot_RelativePathInsideRoot_ReturnsNormalizedFullPath(string relative)
    {
        using var repo = new TempMeridianRepo();
        var service = new RepoPathService();

        var resolved = service.ResolveInsideRoot(relative);

        resolved.Should().Be(Path.GetFullPath(Path.Combine(repo.Root, relative)));
        resolved.Should().StartWith(repo.Root + Path.DirectorySeparatorChar);
        resolved.Should().NotContain($"{Path.DirectorySeparatorChar}..{Path.DirectorySeparatorChar}");
    }

    [Theory]
    [InlineData(".")]
    [InlineData("src/..")]
    public void ResolveInsideRoot_PathEqualToRoot_IsAccepted(string relative)
    {
        using var repo = new TempMeridianRepo();
        var service = new RepoPathService();

        service.ResolveInsideRoot(relative).Should().Be(repo.Root);
    }

    [Fact]
    public void ResolveInsideRoot_AbsolutePathInsideRoot_IsAccepted()
    {
        using var repo = new TempMeridianRepo();
        var service = new RepoPathService();
        var inside = Path.Combine(repo.Root, "src", "file.cs");

        service.ResolveInsideRoot(inside).Should().Be(inside);
        service.ResolveInsideRoot(repo.Root).Should().Be(repo.Root);
    }

    // ── Rejected paths ───────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void ResolveInsideRoot_BlankPath_ThrowsArgumentException(string? path)
    {
        using var repo = new TempMeridianRepo();
        var service = new RepoPathService();

        var act = () => service.ResolveInsideRoot(path!);

        act.Should().Throw<ArgumentException>().WithParameterName("path");
    }

    [Theory]
    [InlineData("..")]
    [InlineData("../outside.txt")]
    [InlineData("../../etc/passwd")]
    [InlineData("src/../../outside.txt")]
    [InlineData("src/a/b/../../../../outside.txt")]
    [InlineData("./../outside.txt")]
    public void ResolveInsideRoot_RelativeTraversalOutsideRoot_Throws(string traversal)
    {
        using var repo = new TempMeridianRepo();
        var service = new RepoPathService();

        var act = () => service.ResolveInsideRoot(traversal);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*outside the Meridian repository root*");
    }

    [Fact]
    public void ResolveInsideRoot_AbsolutePathOutsideRoot_Throws()
    {
        using var repo = new TempMeridianRepo();
        var service = new RepoPathService();
        var outside = repo.WriteOutsideFile("outside.txt", "secret");

        var actOutside = () => service.ResolveInsideRoot(outside);
        var actSandbox = () => service.ResolveInsideRoot(repo.Sandbox);
        var actFsRoot = () => service.ResolveInsideRoot(Path.GetPathRoot(repo.Root)!);

        actOutside.Should().Throw<InvalidOperationException>();
        actSandbox.Should().Throw<InvalidOperationException>("the parent of the root is outside it");
        actFsRoot.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ResolveInsideRoot_SiblingDirectorySharingRootPrefix_Throws()
    {
        // Root ".../repo" must not admit ".../repo-evil/..." or ".../repository/..."; a naive
        // StartsWith(root) check without the trailing separator would let these through.
        using var repo = new TempMeridianRepo();
        var service = new RepoPathService();
        var siblings = new[]
        {
            Path.Combine(repo.Sandbox, "repo-evil", "payload.txt"),
            Path.Combine(repo.Sandbox, "repository", "payload.txt"),
            repo.Root + "2"
        };

        foreach (var sibling in siblings)
        {
            var act = () => service.ResolveInsideRoot(sibling);
            act.Should().Throw<InvalidOperationException>("'{0}' only shares a string prefix with the root", sibling);
        }

        var relativeAct = () => service.ResolveInsideRoot("../repo-evil/payload.txt");
        relativeAct.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ResolveInsideRoot_DifferentlyCasedSiblingOnLinux_Throws()
    {
        // On Linux "/x/repo" and "/x/REPO" are distinct directories; an ignore-case prefix
        // check would treat the sibling as inside the root.
        if (!OperatingSystem.IsLinux())
            return;

        using var repo = new TempMeridianRepo();
        var service = new RepoPathService();
        var sibling = Path.Combine(repo.Sandbox, "REPO", "payload.txt");

        var act = () => service.ResolveInsideRoot(sibling);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ResolveInsideRoot_SymlinkInsideRootPointingOutside_Throws()
    {
        if (OperatingSystem.IsWindows())
            return; // creating symlinks on Windows needs elevation or developer mode

        using var repo = new TempMeridianRepo();
        var outside = Path.Combine(repo.Sandbox, "outside");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "secret.txt"), "secret");
        Directory.CreateSymbolicLink(Path.Combine(repo.Root, "link"), outside);
        var service = new RepoPathService();

        var act = () => service.ResolveInsideRoot("link/secret.txt");

        act.Should().Throw<InvalidOperationException>("the link resolves outside the repository");
    }

    [Fact]
    public void ResolveInsideRoot_LinkWhoseTargetPassesThroughAnotherOutsideLink_Throws()
    {
        // repo/redirect -> <outside>, repo/alias -> repo/redirect/sub. Following only the final
        // component of alias's chain would leave "redirect" unresolved and admit the path.
        if (OperatingSystem.IsWindows())
            return;

        using var repo = new TempMeridianRepo();
        var outside = Path.Combine(repo.Sandbox, "outside");
        Directory.CreateDirectory(Path.Combine(outside, "sub"));
        File.WriteAllText(Path.Combine(outside, "sub", "secret.txt"), "secret");
        Directory.CreateSymbolicLink(Path.Combine(repo.Root, "redirect"), outside);
        Directory.CreateSymbolicLink(Path.Combine(repo.Root, "alias"), Path.Combine(repo.Root, "redirect", "sub"));
        var service = new RepoPathService();

        var act = () => service.ResolveInsideRoot("alias/secret.txt");

        act.Should().Throw<InvalidOperationException>("opening the path reads <outside>/sub/secret.txt");
    }

    [Fact]
    public void ResolveInsideRoot_SymlinkCycle_FailsClosed()
    {
        if (OperatingSystem.IsWindows())
            return;

        using var repo = new TempMeridianRepo();
        File.CreateSymbolicLink(Path.Combine(repo.Root, "a"), Path.Combine(repo.Root, "b"));
        File.CreateSymbolicLink(Path.Combine(repo.Root, "b"), Path.Combine(repo.Root, "a"));
        var service = new RepoPathService();

        var act = () => service.ResolveInsideRoot("a");

        act.Should().Throw<InvalidOperationException>().WithMessage("*symbolic links*");
    }

    [Fact]
    public void ResolveInsideRoot_SymlinkInsideRootPointingInside_IsAccepted()
    {
        if (OperatingSystem.IsWindows())
            return;

        using var repo = new TempMeridianRepo();
        repo.WriteFile(Path.Combine("docs", "a.md"), "a");
        Directory.CreateSymbolicLink(Path.Combine(repo.Root, "docs-link"), Path.Combine(repo.Root, "docs"));
        var service = new RepoPathService();

        var resolved = service.ResolveInsideRoot("docs-link/a.md");

        resolved.Should().Be(Path.Combine(repo.Root, "docs-link", "a.md"));
    }

    [Fact]
    public void Constructor_EnvironmentRootWithTrailingSeparator_StillAdmitsPathsInsideRoot()
    {
        using var repo = new TempMeridianRepo();
        Environment.SetEnvironmentVariable(TempMeridianRepo.EnvVar, repo.Root + Path.DirectorySeparatorChar);
        var service = new RepoPathService();

        var resolved = service.ResolveInsideRoot(Path.Combine("docs", "a.md"));

        resolved.Should().Be(Path.Combine(repo.Root, "docs", "a.md"));
        service.Root.Should().Be(repo.Root);
    }

    [Fact]
    public void ResolveInsideRoot_AbsolutePathWithTraversalBackOutside_Throws()
    {
        using var repo = new TempMeridianRepo();
        var service = new RepoPathService();
        var sneaky = repo.Root + Path.DirectorySeparatorChar + "src" + Path.DirectorySeparatorChar +
                     ".." + Path.DirectorySeparatorChar + ".." + Path.DirectorySeparatorChar + "outside.txt";

        var act = () => service.ResolveInsideRoot(sneaky);

        act.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData(@"..\outside.txt")]
    [InlineData(@"src\..\..\outside.txt")]
    [InlineData(@"src/..\../outside.txt")]
    public void ResolveInsideRoot_BackslashOrMixedSeparators_NeverResolvesOutsideRoot(string path)
    {
        // On Windows '\' is a separator and these are traversals that must be rejected. On Linux
        // '\' is an ordinary filename character, so the segment stays inside the root. Either way
        // the resolved path must never land outside the root.
        using var repo = new TempMeridianRepo();
        var service = new RepoPathService();

        string? resolved = null;
        var act = () => resolved = service.ResolveInsideRoot(path);

        if (Path.DirectorySeparatorChar == '\\' || Path.AltDirectorySeparatorChar == '\\')
        {
            act.Should().Throw<InvalidOperationException>();
        }
        else
        {
            var lexicallyEscapes = Path.GetFullPath(Path.Combine(repo.Root, path))
                .StartsWith(repo.Root + Path.DirectorySeparatorChar, StringComparison.Ordinal) is false;
            if (lexicallyEscapes)
            {
                act.Should().Throw<InvalidOperationException>();
            }
            else
            {
                act.Should().NotThrow();
                resolved.Should().StartWith(repo.Root + Path.DirectorySeparatorChar);
            }
        }
    }

    [Fact]
    public void ResolveInsideRoot_RejectedPath_DoesNotTouchFilesystem()
    {
        using var repo = new TempMeridianRepo();
        var service = new RepoPathService();
        repo.WriteOutsideFile("outside.txt", "original");
        var before = repo.SnapshotSandbox();

        foreach (var path in new[] { "../outside.txt", "../new-dir/new.txt", Path.Combine(repo.Sandbox, "x.txt") })
        {
            var act = () => service.ResolveInsideRoot(path);
            act.Should().Throw<InvalidOperationException>();
        }

        repo.SnapshotSandbox().Should().BeEquivalentTo(before);
        Directory.Exists(Path.Combine(repo.Sandbox, "new-dir")).Should().BeFalse();
    }
}
