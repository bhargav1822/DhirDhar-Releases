using System.Reflection;

namespace DhirDhar.Infrastructure;

/// <summary>
/// Provides a stable, type-safe reference to the infrastructure assembly.
/// </summary>
public static class InfrastructureAssemblyMarker
{
    public static readonly Assembly Assembly = typeof(InfrastructureAssemblyMarker).Assembly;
}
