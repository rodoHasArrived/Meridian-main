namespace Meridian.Tests.Reconciliation.Connectors;

internal static class StatementConnectorTestData
{
    public static string GetFixturePath(string fileName)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(
                current.FullName, "tests", "Meridian.Tests", "TestData", "Golden", "statement-connectors", fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent!;
        }

        throw new FileNotFoundException($"Unable to locate statement connector fixture '{fileName}'.");
    }

    public static byte[] ReadFixture(string fileName)
        => File.ReadAllBytes(GetFixturePath(fileName));

    public static string CreateTempRoot(string prefix)
    {
        var root = Path.Combine(Path.GetTempPath(), $"{prefix}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }
}
