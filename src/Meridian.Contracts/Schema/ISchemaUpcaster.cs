namespace Meridian.Contracts.Schema;

/// <summary>
/// Contract for upgrading a stored record from an older schema version to the current one.
/// </summary>
/// <typeparam name="T">The current (latest) domain type that this upcaster produces.</typeparam>
/// <remarks>
/// Implement this interface for each schema version transition so that old JSONL files
/// remain readable as the domain model evolves, without requiring bulk data migration.
///
/// <para>Registration pattern:</para>
/// <code>
/// // Register in DI:
/// services.AddSingleton&lt;ISchemaUpcaster&lt;Trade&gt;&gt;, TradeV1ToCurrentUpcaster&gt;();
/// </code>
///
/// <para>Convention: the implementing class name should follow the pattern
/// <c>{DomainType}V{FromVersion}ToCurrentUpcaster</c> so it is self-documenting.</para>
/// </remarks>
public interface ISchemaUpcaster<out T>
{
    /// <summary>
    /// The schema version this upcaster reads from (e.g., <c>"1"</c>, <c>"1.0.0"</c>).
    /// </summary>
    int FromSchemaVersion { get; }

    /// <summary>
    /// The schema version this upcaster writes to (the <em>current</em> version of <typeparamref name="T"/>).
    /// </summary>
    int ToSchemaVersion { get; }

    /// <summary>
    /// Converts a raw JSON element that was serialised under <see cref="FromSchemaVersion"/>
    /// into an instance of the current domain type <typeparamref name="T"/>.
    /// </summary>
    /// <param name="json">The raw JSON text of the stored record.</param>
    /// <returns>The up-cast domain object, or <see langword="null"/> if the record cannot be read.</returns>
    T? Upcast(string json);
}
