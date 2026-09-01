namespace EBI.ALAS.Api.Features.Loans;
public interface IFormNumberGenerator
{
    Task<string> GenerateFormNumberAsync();
}
