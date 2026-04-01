using Xunit;

namespace Meridian.Tests.SecurityMaster;

[AttributeUsage(AttributeTargets.Method)]
public sealed class SecurityMasterDatabaseFactAttribute : FactAttribute
{
    private const string DisableDockerVariable = "MERIDIAN_DISABLE_DOCKER_TESTS";

    public SecurityMasterDatabaseFactAttribute()
    {
        if (string.Equals(
                Environment.GetEnvironmentVariable(DisableDockerVariable),
                "true",
                StringComparison.OrdinalIgnoreCase))
        {
            Skip = $"Security Master PostgreSQL tests are skipped because {DisableDockerVariable}=true. " +
                   "Set MERIDIAN_SECURITY_MASTER_CONNECTION_STRING to an external Postgres instance, " +
                   "or ensure Docker is available and unset that variable.";
        }
    }
}
