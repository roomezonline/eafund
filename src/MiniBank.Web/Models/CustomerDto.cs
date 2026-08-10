namespace MiniBank.Web.Models;

public record CustomerDto(
    int CustomerID,
    string? NationalID,
    string FirstName,
    string LastName,
    string? PhoneNumber,
    bool IsActive,
    string RegistrationDateUTC
);
