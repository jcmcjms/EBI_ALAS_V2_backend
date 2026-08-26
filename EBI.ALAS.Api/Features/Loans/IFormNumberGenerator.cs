namespace EBI.ALAS.Api.Features.Loans;

/// <summary>
/// Interface for generating unique form numbers for loan applications.
/// </summary>
public interface IFormNumberGenerator
{
    Task<string> GenerateFormNumberAsync();
}
