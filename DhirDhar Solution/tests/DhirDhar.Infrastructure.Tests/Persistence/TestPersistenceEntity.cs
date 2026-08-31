namespace DhirDhar.Infrastructure.Tests.Persistence;

/// <summary>
/// A minimal, non-financial entity used only to exercise the generic repository and
/// unit-of-work infrastructure. It is not part of the production data model.
/// </summary>
public sealed class TestPersistenceEntity
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;
}
