using System;

namespace DhirDhar.Application.Borrowers.Models;

public sealed record UpdateBorrowerRequest(
    Guid Id,
    string Name,
    string? FatherName = null,
    string? Surname = null,
    string? Village = null,
    string? Phone = null,
    string? Address = null,
    string? AadharNumber = null,
    string? Notes = null,
    string? BorrowerPhotoPath = null,
    string? OrnamentPhotoPath = null,
    string? LoanType = null,
    string? OrnamentType = null,
    decimal? OrnamentWeight = null,
    decimal? LoanAmount = null,
    DateTime? LoanDate = null,
    decimal? InterestRate = null,
    string? BorrowerNumber = null);
