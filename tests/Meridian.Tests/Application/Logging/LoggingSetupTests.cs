using FluentAssertions;
using Xunit;

namespace Meridian.Tests.Application.Logging;

public sealed class LoggingSetupTests
{
    [Fact]
    public void Initialize_UsesSizeBoundedRollingFileSink()
    {
        var repositoryRoot = FindRepositoryRoot();
        var sourcePath = Path.Combine(repositoryRoot, "src", "Meridian.Core", "Logging", "LoggingSetup.cs");
        var source = File.ReadAllText(sourcePath);

        source.Should().Contain("fileSizeLimitBytes: LogFileSizeLimitBytes");
        source.Should().Contain("rollOnFileSizeLimit: true");
        source.Should().Contain("retainedFileCountLimit: RetainedLogFileCountLimit");
    }

    private static string FindRepositoryRoot()
    {
        var candidate = new DirectoryInfo(AppContext.BaseDirectory);
        while (candidate is not null)
        {
            if (File.Exists(Path.Combine(candidate.FullName, "Meridian.sln")))
            {
                return candidate.FullName;
            }

            candidate = candidate.Parent;
        }

        return AppContext.BaseDirectory;
    }
}
