namespace Meridian.TestSupport;

/// <summary>
/// Container image and credentials used when no external connection string is supplied.
/// The defaults mirror the values every Meridian PostgreSQL test module previously
/// duplicated inline; only the initial <see cref="Database"/> name varies between modules.
/// </summary>
public sealed record PostgresTestContainerOptions
{
    /// <summary>PostgreSQL image reference used for the disposable test container.</summary>
    public string Image { get; init; } = "postgres:16-alpine";

    /// <summary>Initial database created inside the container.</summary>
    public string Database { get; init; } = "meridian_test";

    /// <summary>Superuser created inside the container.</summary>
    public string Username { get; init; } = "testuser";

    /// <summary>Superuser password created inside the container.</summary>
    public string Password { get; init; } = "testpass";
}
