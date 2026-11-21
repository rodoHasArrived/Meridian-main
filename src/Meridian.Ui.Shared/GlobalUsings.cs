// Expose internal members to the main test assembly for unit testing
using System.Runtime.CompilerServices;
[assembly: InternalsVisibleTo("Meridian.Tests")]
[assembly: InternalsVisibleTo("Meridian.Storage.Tests")]
[assembly: InternalsVisibleTo("Meridian.Execution.Tests")]
