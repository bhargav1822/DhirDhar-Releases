using System.Reflection;

namespace DhirDhar.Application;

/// <summary>
/// Provides a stable, type-safe reference to the application assembly.
/// </summary>
public static class ApplicationAssemblyMarker
{
    public static readonly Assembly Assembly = typeof(ApplicationAssemblyMarker).Assembly;
}
