using EBI.ALAS.Api.Features.Loans;
using EBI.ALAS.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
namespace EBI.ALAS.Tests;
public class LoanProductRepositoryTests : IDisposable
{
    private readonly AppDbContext _context;
    private readonly LoanProductRepository _repository;
    public LoanProductRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _context = new AppDbContext(options);
        _repository = new LoanProductRepository(_context);
    }
    public void Dispose() => _context.Dispose();
    private static LoanProduct CreateProduct(string code, string description = "") => new()
    {
        Code = code,
        Description = string.IsNullOrEmpty(description) ? $"Product {code}" : description,
    };
    [Fact]
    public async Task GetByCodesAsync_ReturnsMatchingProducts()
    {
        _context.LoanProducts.Add(CreateProduct("A"));
        _context.LoanProducts.Add(CreateProduct("B"));
        _context.LoanProducts.Add(CreateProduct("C"));
        await _context.SaveChangesAsync();
        var result = await _repository.GetByCodesAsync(["A", "C"]);
        Assert.Equal(2, result.Count);
        Assert.Contains(result, p => p.Code == "A");
        Assert.Contains(result, p => p.Code == "C");
    }
    [Fact]
    public async Task GetByCodesAsync_EmptyCodes_ReturnsEmpty()
    {
        _context.LoanProducts.Add(CreateProduct("A"));
        await _context.SaveChangesAsync();
        var result = await _repository.GetByCodesAsync([]);
        Assert.Empty(result);
    }
    [Fact]
    public async Task GetByCodesAsync_NullCodes_ReturnsEmpty()
    {
        _context.LoanProducts.Add(CreateProduct("A"));
        await _context.SaveChangesAsync();
        var result = await _repository.GetByCodesAsync(null!);
        Assert.Empty(result);
    }
    [Fact]
    public async Task GetByCodesAsync_NoMatch_ReturnsEmpty()
    {
        _context.LoanProducts.Add(CreateProduct("A"));
        await _context.SaveChangesAsync();
        var result = await _repository.GetByCodesAsync(["NONEXISTENT"]);
        Assert.Empty(result);
    }
}