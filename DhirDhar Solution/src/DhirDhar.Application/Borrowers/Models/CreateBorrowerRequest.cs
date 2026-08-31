using System;

namespace DhirDhar.Application.Borrowers.Models;

public sealed record CreateBorrowerRequest(
    string BorrowerNumber,
    string Name,
    string? FatherName = null,
    string? Surname = null,
    string? Village = null,
    string? Contact = null,
    string? Address = null,
    string? AadharNumber = null,
    DateTime EntryDate = default,
    decimal? LoanAmount = null,
    DateTime? LoanDate = null,
    string? Notes = null,
    string? BorrowerPhotoPath = null,
    string? OrnamentPhotoPath = null,
    string? LoanType = null,
    string? OrnamentType = null,
    decimal? OrnamentWeight = null,
    decimal? InterestRate = null);
