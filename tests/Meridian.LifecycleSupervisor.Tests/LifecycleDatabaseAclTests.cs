using System.Security.AccessControl;
using System.Security.Principal;
using FluentAssertions;
using Xunit;

namespace Meridian.LifecycleSupervisor.Tests;

public sealed class LifecycleDatabaseAclTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "meridian-lifecycle-acl-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void GrantCurrentUserInheritableFullControl_AddsExplicitInheritableUserAce()
    {
        Directory.CreateDirectory(_root);

        LifecycleDatabaseController.GrantCurrentUserInheritableFullControl(_root);

        if (!OperatingSystem.IsWindows())
            return; // Off Windows the grant is a contract-level no-op; not throwing is the assertion.

        using var identity = WindowsIdentity.GetCurrent();
        var rules = new DirectoryInfo(_root)
            .GetAccessControl()
            .GetAccessRules(true, false, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>();

        rules.Should().Contain(rule =>
            rule.IdentityReference == identity.User &&
            rule.AccessControlType == AccessControlType.Allow &&
            rule.FileSystemRights.HasFlag(FileSystemRights.FullControl) &&
            rule.InheritanceFlags.HasFlag(InheritanceFlags.ContainerInherit) &&
            rule.InheritanceFlags.HasFlag(InheritanceFlags.ObjectInherit));
    }

    [Fact]
    public void GrantCurrentUserInheritableFullControl_DoesNotThrowForMissingDirectory()
    {
        var missing = Path.Combine(_root, "missing");

        var act = () => LifecycleDatabaseController.GrantCurrentUserInheritableFullControl(missing);

        act.Should().NotThrow();
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
