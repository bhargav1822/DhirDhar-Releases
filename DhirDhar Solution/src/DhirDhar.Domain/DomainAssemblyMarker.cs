using System.Reflection;

namespace DhirDhar.Domain;

/// <summary>
/// Provides a stable, type-safe reference to the domain assembly
/// for reflection-based scenarios such as assembly loading tests.
/// </summary>
public static class DomainAssemblyMarker
{
    public static readonly Assembly Assembly = typeof(DomainAssemblyMarker).Assembly;
}
