namespace DhirDhar.Application.Borrowers.Models;

public enum BorrowerFilter
{
    All,
    Active,
    Inactive,
    Closed,
    Archived = Closed
}
