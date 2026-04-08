using System.IO;
using System.IO.Pipes;
using Xunit;

namespace Meridian.DirectLending.Tests;

[AttributeUsage(AttributeTargets.Method)]
public sealed class DirectLendingDatabaseFactAttribute : FactAttribute
{
    public DirectLendingDatabaseFactAttribute()
    {
        if (DirectLendingDatabaseTestEnvironment.TryGetSkipReason(out var reason))
        {
            Skip = reason;
        }
    }
}

internal static class DirectLendingDatabaseTestEnvironment
{
    internal const string ConnectionStringVariable = "MERIDIAN_DIRECT_LENDING_CONNECTION_STRING";
    internal const string DisableDockerVariable = "MERIDIAN_DISABLE_DOCKER_TESTS";

    public static string? GetExternalConnectionString() =>
        Environment.GetEnvironmentVariable(ConnectionStringVariable);

    public static bool TryGetSkipReason(out string? reason)
    {
        if (string.Equals(
                Environment.GetEnvironmentVariable(DisableDockerVariable),
                "true",
                StringComparison.OrdinalIgnoreCase))
        {
            reason = $"Direct Lending PostgreSQL integration tests are skipped because {DisableDockerVariable}=true. " +
                     $"Set {ConnectionStringVariable} to an external Postgres instance, " +
                     "or ensure Docker is available and unset that variable.";
            return true;
        }

        if (!string.IsNullOrWhiteSpace(GetExternalConnectionString()))
        {
            reason = null;
            return false;
        }

        if (IsDockerAvailable())
        {
            reason = null;
            return false;
        }

        reason = "Direct Lending PostgreSQL integration tests are skipped because Docker is unavailable. " +
                 $"Start Docker, set {ConnectionStringVariable} to an external Postgres instance, " +
                 $"or set {DisableDockerVariable}=true to suppress this suite explicitly.";
        return true;
    }

    private static bool IsDockerAvailable()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                using var pipe = new NamedPipeClientStream(".", "docker_engine", PipeDirection.InOut, PipeOptions.Asynchronous);
                pipe.Connect(250);
                return pipe.IsConnected;
            }

            return File.Exists("/var/run/docker.sock");
        }
        catch (TimeoutException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
