using Ebi.Alas.Api.Features.LoanProducts;

namespace Ebi.Alas.Api.Features.LoanProducts;

public sealed class LoanProduct
{
    private LoanProduct()
    {
        Code = string.Empty;
        Name = string.Empty;
    }

    public Guid Id { get; private set; }

    public string Code { get; private set; }

    public string Name { get; private set; }

    public decimal InterestRatePerMonth { get; private set; }

    public int MinTermDays { get; private set; }

    public int MaxTermDays { get; private set; }

    public bool IsActive { get; private set; }

    public static LoanProduct Create(
        string code,
        string name,
        decimal interestRatePerMonth,
        int minTermDays,
        int maxTermDays,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentOutOfRangeException.ThrowIfLessThan(minTermDays, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxTermDays, minTermDays);
        return new LoanProduct
        {
            Id = Guid.NewGuid(),
            Code = code.Trim(),
            Name = name.Trim(),
            InterestRatePerMonth = interestRatePerMonth,
            MinTermDays = minTermDays,
            MaxTermDays = maxTermDays,
            IsActive = true
        };
    }

    public void UpdateRates(decimal interestRatePerMonth, int minTermDays, int maxTermDays)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(minTermDays, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxTermDays, minTermDays);
        InterestRatePerMonth = interestRatePerMonth;
        MinTermDays = minTermDays;
        MaxTermDays = maxTermDays;
    }

    public void Deactivate() => IsActive = false;
}

public static class LoanProductCsv
{
    public static IReadOnlyList<(string Code, string Name, decimal Rate, int MinTerm, int MaxTerm)> Parse(string csv)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(csv);
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var rows = new List<(string, string, decimal, int, int)>();
        foreach (var line in lines.Skip(1))
        {
            var parts = line.Split(',', StringSplitOptions.TrimEntries);
            if (parts.Length < 5)
            {
                continue;
            }

            if (decimal.TryParse(parts[2], out var rate)
                && int.TryParse(parts[3], out var min)
                && int.TryParse(parts[4], out var max))
            {
                rows.Add((parts[0], parts[1], rate, min, max));
            }
        }

        return rows;
    }
}
