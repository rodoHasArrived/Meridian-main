using Xunit;

namespace Meridian.Tests.SecurityMaster;

[AttributeUsage(AttributeTargets.Method)]
public sealed class SecurityMasterDatabaseFactAttribute : FactAttribute
{
    private const string ConnectionStringVariable = "MERIDIAN_SECURITY_MASTER_CONNECTION_STRING";

    public SecurityMasterDatabaseFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionStringVariable)))
        {
            Skip = $"Set {ConnectionStringVariable} to run Security Master PostgreSQL integration tests.";
        }
    }
}
