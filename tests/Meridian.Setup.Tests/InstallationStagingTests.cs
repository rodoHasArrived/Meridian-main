using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Xunit;

namespace Meridian.Setup.Tests;

/// <summary>
/// Payload-path validation, manifest integrity, and sibling-directory safety rules of
/// <see cref="InstallationTransaction"/> that the promotion tests do not exercise directly.
/// </summary>
public sealed class InstallationStagingTests : IDisposable
{
    private const string StagePrefix = ".Meridian.stage-";
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"meridian-setup-staging-tests-{Guid.NewGuid():N}");

    public InstallationStagingTests()
    {
        Directory.CreateDirectory(_root);
    }

    private string InstallRoot => Path.Combine(_root, "Meridian");

    // ── Payload path validation ──────────────────────────────────────────────

    [Theory]
    [InlineData("../escape.txt")]
    [InlineData(@"..\escape.txt")]
    [InlineData(@"config\..\..\escape.txt")]
    [InlineData("config/../../escape.txt")]
    [InlineData("./Meridian.dll")]
    [InlineData("config//double.json")]
    [InlineData("config/./x.json")]
    [InlineData("config/")]
    public void Stage_UnsafeRelativePayloadPath_IsRejectedAndLeavesNoStage(string unsafePath)
    {
        var act = () => Stage(("Meridian.exe", "app"), (unsafePath, "payload"));

        act.Should().Throw<InvalidDataException>().WithMessage("*payload path*");
        File.Exists(Path.Combine(_root, "escape.txt")).Should().BeFalse();
        StageDirectories().Should().BeEmpty("a failed stage must be removed");
    }

    [Fact]
    public void Stage_RootedPayloadPath_IsRejected()
    {
        var rooted = Path.Combine(_root, "rooted.txt");

        var act = () => Stage(("Meridian.exe", "app"), (rooted, "payload"));

        act.Should().Throw<InvalidDataException>().WithMessage("*payload path*");
        File.Exists(rooted).Should().BeFalse();
        StageDirectories().Should().BeEmpty();
    }

    [Theory]
    [InlineData("config/app.json", "config/app.json")]
    [InlineData("config/app.json", @"config\app.json")]
    [InlineData("config/app.json", "CONFIG/App.JSON")]
    public void Stage_DuplicatePayloadPath_IsRejectedEvenAcrossSeparatorsAndCase(string first, string second)
    {
        var act = () => Stage(("Meridian.exe", "app"), (first, "one"), (second, "two"));

        act.Should().Throw<InvalidDataException>().WithMessage("*duplicate path*");
        StageDirectories().Should().BeEmpty();
    }

    [Theory]
    [InlineData("Meridian-Setup.exe")]
    [InlineData("meridian-setup.EXE")]
    public void Stage_PayloadProvidingReservedInstallerPath_IsRejected(string reserved)
    {
        var act = () => Stage(("Meridian.exe", "app"), (reserved, "fake-installer"));

        act.Should().Throw<InvalidDataException>().WithMessage("*reserved installer path*");
        StageDirectories().Should().BeEmpty();
    }

    [Fact]
    public void Stage_PayloadWithoutMeridianExecutable_IsRejected()
    {
        var act = () => Stage(("Meridian.dll", "lib"));

        act.Should().Throw<InvalidDataException>().WithMessage("*does not contain Meridian.exe*");
        StageDirectories().Should().BeEmpty();
    }

    [Fact]
    public void Stage_SourceReturningNullStream_IsRejected()
    {
        var payload = new[]
        {
            Source("Meridian.exe", "app"),
            new PayloadSource("missing.dll", () => null!)
        };

        var act = () => InstallationTransaction.Stage(InstallRoot, "2.0.0", "linux-x64", payload, WriteInstaller());

        act.Should().Throw<InvalidDataException>().WithMessage("*missing 'missing.dll'*");
        StageDirectories().Should().BeEmpty();
    }

    // ── Manifest integrity ───────────────────────────────────────────────────

    [Fact]
    public void Stage_WritesSortedManifestWithExactLengthsHashesAndInstallerCopy()
    {
        var staged = Stage(("Meridian.exe", "app-bytes"), ("config/app.json", "{}"));

        Path.GetFileName(staged.Path).Should().StartWith(StagePrefix + "2.0.0-");
        Directory.GetParent(staged.Path)!.FullName.Should().Be(_root, "stages are siblings of the install root");
        staged.Manifest.SchemaVersion.Should().Be(1);
        staged.Manifest.ProductVersion.Should().Be("2.0.0");
        staged.Manifest.Runtime.Should().Be("linux-x64");
        staged.Manifest.Files.Select(f => f.RelativePath)
            .Should().Equal(new[] { Path.Combine("config", "app.json"), "Meridian-Setup.exe", "Meridian.exe" }
                .OrderBy(p => p, StringComparer.Ordinal));
        foreach (var entry in staged.Manifest.Files)
        {
            var bytes = File.ReadAllBytes(Path.Combine(staged.Path, entry.RelativePath));
            entry.Length.Should().Be(bytes.Length);
            entry.Sha256.Should().Be(Convert.ToHexString(SHA256.HashData(bytes)));
        }

        File.ReadAllText(Path.Combine(staged.Path, "Meridian-Setup.exe")).Should().Be("signed-installer-placeholder");
        File.Exists(Path.Combine(staged.Path, InstallationTransaction.ManifestFileName)).Should().BeTrue();
    }

    [Fact]
    public void Stage_ProductVersionWithPathCharacters_IsSanitizedIntoASiblingStageName()
    {
        var staged = InstallationTransaction.Stage(
            InstallRoot,
            "2.0.0/../../evil",
            "linux-x64",
            [Source("Meridian.exe", "app")],
            WriteInstaller());

        Directory.GetParent(staged.Path)!.FullName.Should().Be(_root);
        Path.GetFileName(staged.Path).Should().StartWith(StagePrefix).And.NotContain("/").And.NotContain("\\");
    }

    [Fact]
    public void Verify_SameLengthContentSubstitution_FailsShaCheck()
    {
        var staged = Stage(("Meridian.exe", "new-app"));
        File.WriteAllText(Path.Combine(staged.Path, "Meridian.exe"), "NEW-APP");

        var act = () => InstallationTransaction.Verify(staged);

        act.Should().Throw<InvalidDataException>().WithMessage("*SHA-256*");
    }

    [Fact]
    public void Verify_MissingStagedFile_Fails()
    {
        var staged = Stage(("Meridian.exe", "new-app"), ("lib/a.dll", "a"));
        File.Delete(Path.Combine(staged.Path, "lib", "a.dll"));

        var act = () => InstallationTransaction.Verify(staged);

        act.Should().Throw<InvalidDataException>().WithMessage("*is missing*");
    }

    [Fact]
    public void Verify_ManifestWithWrongSchemaOrNoFiles_IsInvalid()
    {
        var staged = Stage(("Meridian.exe", "new-app"));

        var wrongSchema = () => InstallationTransaction.Verify(
            staged with { Manifest = staged.Manifest with { SchemaVersion = 2 } });
        var empty = () => InstallationTransaction.Verify(
            staged with { Manifest = staged.Manifest with { Files = [] } });

        wrongSchema.Should().Throw<InvalidDataException>().WithMessage("*manifest is invalid*");
        empty.Should().Throw<InvalidDataException>().WithMessage("*manifest is invalid*");
    }

    [Fact]
    public void Verify_TamperedManifestEntryEscapingStage_IsRejected()
    {
        var staged = Stage(("Meridian.exe", "new-app"));
        var outside = Path.Combine(_root, "outside.bin");
        File.WriteAllText(outside, "x");
        var tampered = staged with
        {
            Manifest = staged.Manifest with
            {
                Files = [.. staged.Manifest.Files, new PayloadManifestEntry("../outside.bin", 1, "00")]
            }
        };

        var act = () => InstallationTransaction.Verify(tampered);

        act.Should().Throw<InvalidDataException>().WithMessage("*payload path*");
    }

    // ── Promotion and sibling-directory safety ───────────────────────────────

    [Fact]
    public void Promote_FirstInstall_ReturnsNoRollback()
    {
        var staged = Stage(("Meridian.exe", "first"));

        var rollback = InstallationTransaction.Promote(staged, InstallRoot);

        rollback.Should().BeNull();
        File.ReadAllText(Path.Combine(InstallRoot, "Meridian.exe")).Should().Be("first");
        Directory.Exists(staged.Path).Should().BeFalse();
    }

    [Fact]
    public void Promote_ReplacesPreviousRollbackWithOnlyOneGeneration()
    {
        InstallationTransaction.Promote(Stage(("Meridian.exe", "v1")), InstallRoot);
        InstallationTransaction.Promote(Stage(("Meridian.exe", "v2")), InstallRoot);

        var rollback = InstallationTransaction.Promote(Stage(("Meridian.exe", "v3")), InstallRoot);

        File.ReadAllText(Path.Combine(InstallRoot, "Meridian.exe")).Should().Be("v3");
        File.ReadAllText(Path.Combine(rollback!, "Meridian.exe")).Should().Be("v2");
        Directory.GetDirectories(_root).Select(Path.GetFileName)
            .Should().BeEquivalentTo("Meridian", "Meridian.rollback");
    }

    [Fact]
    public void Promote_StageThatIsNotASiblingOfTheInstallRoot_IsRefused()
    {
        var staged = Stage(("Meridian.exe", "app"));
        var elsewhere = Path.Combine(_root, "nested", Path.GetFileName(staged.Path));
        Directory.CreateDirectory(Path.GetDirectoryName(elsewhere)!);
        Directory.Move(staged.Path, elsewhere);

        var act = () => InstallationTransaction.Promote(staged with { Path = elsewhere }, InstallRoot);

        act.Should().Throw<InvalidOperationException>().WithMessage("*sibling*");
        Directory.Exists(InstallRoot).Should().BeFalse();
    }

    [Fact]
    public void Promote_SiblingDirectoryWithoutStagePrefix_IsRefused()
    {
        var staged = Stage(("Meridian.exe", "app"));
        var renamed = Path.Combine(_root, "not-a-stage");
        Directory.Move(staged.Path, renamed);

        var act = () => InstallationTransaction.Promote(staged with { Path = renamed }, InstallRoot);

        act.Should().Throw<InvalidOperationException>().WithMessage("*sibling*");
        Directory.Exists(renamed).Should().BeTrue();
    }

    [Fact]
    public void RecoverInterruptedPromotion_WithCurrentPresent_LeavesRollbackAlone()
    {
        Directory.CreateDirectory(InstallRoot);
        var rollback = InstallationTransaction.GetRollbackPath(InstallRoot);
        Directory.CreateDirectory(rollback);

        InstallationTransaction.RecoverInterruptedPromotion(InstallRoot).Should().BeFalse();
        Directory.Exists(rollback).Should().BeTrue();
        InstallationTransaction.RecoverInterruptedPromotion(Path.Combine(_root, "never-installed")).Should().BeFalse();
    }

    [Fact]
    public void CleanupAbandonedStages_RemovesOnlyStageDirectories()
    {
        Directory.CreateDirectory(InstallRoot);
        File.WriteAllText(Path.Combine(InstallRoot, "Meridian.exe"), "current");
        Directory.CreateDirectory(InstallationTransaction.GetRollbackPath(InstallRoot));
        Directory.CreateDirectory(Path.Combine(_root, StagePrefix + "1.0.0-abandoned"));
        Directory.CreateDirectory(Path.Combine(_root, StagePrefix + "1.0.1-abandoned", "deep"));
        Directory.CreateDirectory(Path.Combine(_root, "Meridian.stage-lookalike"));
        Directory.CreateDirectory(Path.Combine(_root, "UserData"));

        InstallationTransaction.CleanupAbandonedStages(InstallRoot);

        Directory.GetDirectories(_root).Select(Path.GetFileName)
            .Should().BeEquivalentTo("Meridian", "Meridian.rollback", "Meridian.stage-lookalike", "UserData");
        File.ReadAllText(Path.Combine(InstallRoot, "Meridian.exe")).Should().Be("current");
    }

    [Fact]
    public void CleanupAbandonedStages_MissingParent_IsANoOp()
    {
        var act = () => InstallationTransaction.CleanupAbandonedStages(Path.Combine(_root, "absent", "Meridian"));

        act.Should().NotThrow();
    }

    [Fact]
    public void CaptureForUninstall_MovesCurrentAndRollbackToUniqueNamesAndFreesReusablePaths()
    {
        InstallationTransaction.Promote(Stage(("Meridian.exe", "v1")), InstallRoot);
        InstallationTransaction.Promote(Stage(("Meridian.exe", "v2")), InstallRoot);

        var captured = InstallationTransaction.CaptureForUninstall(InstallRoot);

        captured.Should().HaveCount(2).And.OnlyHaveUniqueItems();
        captured.Should().OnlyContain(path =>
            Path.GetFileName(path).StartsWith(".Meridian.uninstall-", StringComparison.Ordinal) &&
            Directory.GetParent(path)!.FullName == _root);
        Directory.Exists(InstallRoot).Should().BeFalse();
        Directory.Exists(InstallationTransaction.GetRollbackPath(InstallRoot)).Should().BeFalse();
        captured.Select(path => File.ReadAllText(Path.Combine(path, "Meridian.exe")))
            .Should().BeEquivalentTo("v1", "v2");
    }

    [Fact]
    public void CaptureForUninstall_NothingInstalled_CapturesNothing()
    {
        InstallationTransaction.CaptureForUninstall(InstallRoot).Should().BeEmpty();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private StagedInstallation Stage(params (string RelativePath, string Content)[] files)
        => InstallationTransaction.Stage(
            InstallRoot,
            "2.0.0",
            "linux-x64",
            files.Select(file => Source(file.RelativePath, file.Content)).ToArray(),
            WriteInstaller());

    private IEnumerable<string> StageDirectories()
        => Directory.EnumerateDirectories(_root, StagePrefix + "*", SearchOption.TopDirectoryOnly);

    private string WriteInstaller()
    {
        var path = Path.Combine(_root, "source-setup.exe");
        File.WriteAllText(path, "signed-installer-placeholder");
        return path;
    }

    private static PayloadSource Source(string relativePath, string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        return new PayloadSource(relativePath, () => new MemoryStream(bytes, writable: false));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
