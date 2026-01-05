using Xunit;

namespace ComplexBot.Integration;

/// <summary>
/// Collection definition for integration tests (ensures sequential execution)
/// </summary>
[CollectionDefinition("Integration")]
public class IntegrationCollection : ICollectionFixture<IntegrationTestFixture>
{
    // This has no code, and never creates an instance of the collection.
    // It's just a place to apply [CollectionDefinition] and all the
    // ICollectionFixture<T> interfaces.
}
