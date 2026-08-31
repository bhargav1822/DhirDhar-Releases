namespace DhirDhar.Application.Search.Models;

public sealed record SearchResult(
    string EntityType,
    string Id,
    string Title,
    string Subtitle,
    string Status,
    DateTime? Date,
    decimal? Amount);
