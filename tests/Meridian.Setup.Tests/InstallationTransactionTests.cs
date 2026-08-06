using System.Text;
using FluentAssertions;
using Xunit;

namespace Meridian.Setup.Tests;

public sealed class InstallationTransactionTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"meridian-setup-tests-{Guid.NewGuid():N}");

    public InstallationTransactionTests()
    {
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public void Promote_ReplacesCompleteTreeAndRetainsRollback()
    {
        var installRoot = CreateCurrentInstallation();
        var staged = Stage(installRoot, ("Meridian.exe", "new-app"), ("config/new.json", "new-config"));

        var rollback = InstallationTransaction.Promote(staged, installRoot);

        File.ReadAllText(Path.Combine(installRoot, "Meridian.exe")).Should().Be("new-app");
        File.ReadAllText(Path.Combine(installRoot, "config", "new.json")).Should().Be("new-config");
        File.Exists(Path.Combine(installRoot, "removed-in-v2.txt")).Should().BeFalse();
        File.Exists(Path.Combine(installRoot, InstallationTransaction.ManifestFileName)).Should().BeTrue();
        rollback.Should().Be(InstallationTransaction.GetRollbackPath(installRoot));
        File.ReadAllText(Path.Combine(rollback!, "Meridian.exe")).Should().Be("old-app");
        File.Exists(Path.Combine(rollback!, "removed-in-v2.txt")).Should().BeTrue();
    }

    [Fact]
    public void Verify_CorruptStagedFileRejectsBeforeCurrentTreeChanges()
    {
        var installRoot = CreateCurrentInstallation();
        var staged = Stage(installRoot, ("Meridian.exe", "new-app"));
        File.WriteAllText(Path.Combine(staged.Path, "Meridian.exe"), "tampered");

        var act = () => InstallationTransaction.Promote(staged, installRoot);

        act.Should().Throw<InvalidDataException>().WithMessage("*invalid length*");
        File.ReadAllText(Path.Combine(installRoot, "Meridian.exe")).Should().Be("old-app");
        Directory.Exists(InstallationTransaction.GetRollbackPath(installRoot)).Should().BeFalse();
    }

    [Fact]
    public void Promote_FaultAfterRollbackMoveRestoresCurrentTree()
    {
        var installRoot = CreateCurrentInstallation();
        var staged = Stage(installRoot, ("Meridian.exe", "new-app"));

        var act = () => InstallationTransaction.Promote(
            staged,
            installRoot,
            () => throw new IOException("promotion interrupted"));

        act.Should().Throw<IOException>().WithMessage("promotion interrupted");
        File.ReadAllText(Path.Combine(installRoot, "Meridian.exe")).Should().Be("old-app");
        Directory.Exists(staged.Path).Should().BeTrue();
        Directory.Exists(InstallationTransaction.GetRollbackPath(installRoot)).Should().BeFalse();
    }

    [Fact]
    public void RecoverInterruptedPromotion_RestoresRetainedRollbackWhenCurrentIsMissing()
    {
        var installRoot = CreateCurrentInstallation();
        var rollback = InstallationTransaction.GetRollbackPath(installRoot);
        Directory.Move(installRoot, rollback);

        InstallationTransaction.RecoverInterruptedPromotion(installRoot).Should().BeTrue();

        File.ReadAllText(Path.Combine(installRoot, "Meridian.exe")).Should().Be("old-app");
        Directory.Exists(rollback).Should().BeFalse();
    }

    [Fact]
    public void Stage_PathTraversalPayloadIsRejectedWithoutWritingOutsideStage()
    {
        var installRoot = Path.Combine(_root, "Meridian");
        var outside = Path.Combine(_root, "escape.txt");
        var installer = WriteInstaller();
        var payload = new[]
        {
            Source("Meridian.exe", "new-app"),
            Source("../escape.txt", "escape")
        };

        var act = () => InstallationTransaction.Stage(
            installRoot,
            "2.0.0",
            "win-x64",
            payload,
            installer);

        act.Should().Throw<InvalidDataException>().WithMessage("*payload path*");
        File.Exists(outside).Should().BeFalse();
    }

    private string CreateCurrentInstallation()
    {
        var installRoot = Path.Combine(_root, "Meridian");
        Directory.CreateDirectory(installRoot);
        File.WriteAllText(Path.Combine(installRoot, "Meridian.exe"), "old-app");
        File.WriteAllText(Path.Combine(installRoot, "removed-in-v2.txt"), "obsolete");
        return installRoot;
    }

    private StagedInstallation Stage(
        string installRoot,
        params (string RelativePath, string Content)[] files)
        => InstallationTransaction.Stage(
            installRoot,
            "2.0.0",
            "win-x64",
            files.Select(file => Source(file.RelativePath, file.Content)).ToArray(),
            WriteInstaller());

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
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
