using FluentAssertions;
using Meridian.Ui.Shared.Endpoints;
using Xunit;

namespace Meridian.Tests.Ui;

/// <summary>
/// Package download/delete must only ever touch a direct child of the packages directory.
/// </summary>
public sealed class PackagingEndpointsPathContainmentTests
{
    private static readonly string PackagesDirectory = Path.Combine(Path.GetTempPath(), "meridian-packages-root");

    [Theory]
    [InlineData("C:x")]            // drive-relative on Windows: no separator, escapes the folder
    [InlineData("C:")]
    [InlineData("..")]
    [InlineData(".")]
    [InlineData("../secrets.zip")]
    [InlineData("..\\secrets.zip")]
    [InlineData("nested/pkg.zip")]
    [InlineData("stream.zip:ads")]
    [InlineData("")]
    [InlineData("   ")]
    public void TryResolvePackageFile_RejectsNamesThatCanLeaveThePackagesDirectory(string fileName)
    {
        PackagingEndpoints.TryResolvePackageFile(fileName, PackagesDirectory, out var resolved)
            .Should().BeFalse();
        resolved.Should().BeEmpty();
    }

    [Theory]
    [InlineData("package_20240102.zip")]
    [InlineData("archive.tar.gz")]
    [InlineData("..hidden-but-plain.zip")]
    public void TryResolvePackageFile_AcceptsPlainFileNamesInsideThePackagesDirectory(string fileName)
    {
        PackagingEndpoints.TryResolvePackageFile(fileName, PackagesDirectory, out var resolved)
            .Should().BeTrue();
        resolved.Should().Be(Path.Combine(Path.GetFullPath(PackagesDirectory), fileName));
    }
}
