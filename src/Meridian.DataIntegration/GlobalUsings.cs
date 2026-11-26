// Expose internal diagnostics to the shared test assembly.
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Meridian.Tests")]
[assembly: InternalsVisibleTo("Meridian.Storage.Tests")]
[assembly: InternalsVisibleTo("Meridian.Execution.Tests")]
