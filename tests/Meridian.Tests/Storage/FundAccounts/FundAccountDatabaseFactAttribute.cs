using Xunit;

namespace Meridian.Tests.Storage.FundAccounts;

[AttributeUsage(AttributeTargets.Method)]
public sealed class FundAccountDatabaseFactAttribute : FactAttribute
{
    private const string DisableDockerVariable = "MERIDIAN_DISABLE_DOCKER_TESTS";
    private const string ConnectionStringVariable = "MERIDIAN_FUND_ACCOUNTS_CONNECTION_STRING";

    public FundAccountDatabaseFactAttribute()
    {
        if (string.Equals(
                Environment.GetEnvironmentVariable(DisableDockerVariable),
                "true",
                StringComparison.OrdinalIgnoreCase))
        {
            Skip = $"Fund-account PostgreSQL tests are skipped because {DisableDockerVariable}=true. " +
                   $"Set {ConnectionStringVariable} to an external Postgres instance, " +
                   "or ensure Docker is available and unset that variable.";
            return;
        }

        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionStringVariable)))
        {
            return;
        }

        if (!DockerAvailability.IsDockerAvailable())
        {
            Skip = "Fund-account PostgreSQL tests are skipped because Docker is unavailable. " +
                   $"Start Docker, set {ConnectionStringVariable} to an external Postgres instance, " +
                   $"or set {DisableDockerVariable}=true to suppress this suite explicitly.";
        }
    }
}

// Duplicated from the SecurityMaster suite because its helper is file-scoped.
file static class DockerAvailability
{
    public static bool IsDockerAvailable()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                using var pipe = new System.IO.Pipes.NamedPipeClientStream(
                    ".",
                    "docker_engine",
                    System.IO.Pipes.PipeDirection.InOut,
                    System.IO.Pipes.PipeOptions.Asynchronous);
                pipe.Connect(250);
                return pipe.IsConnected;
            }

            return File.Exists("/var/run/docker.sock");
        }
        catch
        {
            return false;
        }
    }
}
