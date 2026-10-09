namespace Ebi.Alas.Api.Features.LoanProducts;

public sealed class LoanProduct
{
    private LoanProduct()
    {
        Code = string.Empty;
        Name = string.Empty;
        AmortizationMode = "DIM";
    }

    public Guid Id { get; private set; }

    public string Code { get; private set; }

    public string Name { get; private set; }

    public decimal InterestRatePerMonth { get; private set; }

    public int MinTermDays { get; private set; }

    public int MaxTermDays { get; private set; }

    public bool IsActive { get; private set; }

    public decimal MinAmount { get; private set; }

    public decimal MaxAmount { get; private set; }

    public decimal NotarialFee { get; private set; }

    public decimal DocStampFee { get; private set; }

    public decimal InsuranceFee { get; private set; }

    public decimal AdvanceInterestRate { get; private set; }

    public decimal ApplicationChargeRate { get; private set; }

    public string AmortizationMode { get; private set; }

    public bool ChargeAdvanceInterest { get; private set; }

    public DateTimeOffset LastSyncedAt { get; private set; }

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
            IsActive = true,
            LastSyncedAt = now
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

    public void UpdatePolicy(
        decimal minAmount,
        decimal maxAmount,
        int minTermDays,
        int maxTermDays,
        decimal notarialFee,
        decimal docStampFee,
        decimal insuranceFee,
        decimal advanceInterestRate)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(minAmount);
        if (maxAmount < minAmount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxAmount),
                "Max amount must be greater than or equal to min amount.");
        }

        ArgumentOutOfRangeException.ThrowIfNegative(minTermDays);
        if (maxTermDays < minTermDays)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxTermDays),
                "Max term must be greater than or equal to min term.");
        }

        ArgumentOutOfRangeException.ThrowIfNegative(notarialFee);
        ArgumentOutOfRangeException.ThrowIfNegative(docStampFee);
        ArgumentOutOfRangeException.ThrowIfNegative(insuranceFee);
        if (advanceInterestRate is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(advanceInterestRate),
                "Advance interest rate must be between 0 and 1.");
        }

        MinAmount = minAmount;
        MaxAmount = maxAmount;
        MinTermDays = minTermDays;
        MaxTermDays = maxTermDays;
        NotarialFee = notarialFee;
        DocStampFee = docStampFee;
        InsuranceFee = insuranceFee;
        AdvanceInterestRate = advanceInterestRate;
    }

    /// <summary>
    /// Applies webloan catalog fields while preserving ALAS-owned policy fields.
    /// Returns true when catalog values changed.
    /// </summary>
    public bool ApplyCatalog(string name, decimal interestRatePerMonth, DateTimeOffset syncedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var changed = Name != name.Trim() || InterestRatePerMonth != interestRatePerMonth;
        Name = name.Trim();
        InterestRatePerMonth = interestRatePerMonth;
        LastSyncedAt = syncedAt;
        return changed;
    }

    public void ApplyPolicyOptions(
        decimal? applicationChargeRate,
        string? amortizationMode,
        bool? chargeAdvanceInterest)
    {
        if (applicationChargeRate is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(applicationChargeRate),
                "Application charge rate must be between 0 and 1.");
        }

        if (amortizationMode is not null and not ("DIM" or "MIC"))
        {
            throw new ArgumentOutOfRangeException(
                nameof(amortizationMode),
                "Amortization mode must be DIM or MIC.");
        }

        if (applicationChargeRate is { } chargeRate)
        {
            ApplicationChargeRate = chargeRate;
        }

        if (amortizationMode is { Length: > 0 } mode)
        {
            AmortizationMode = mode;
        }

        if (chargeAdvanceInterest is { } flag)
        {
            ChargeAdvanceInterest = flag;
        }
    }

    public void Deactivate() => IsActive = false;

    public void SetRetired(bool isRetired) => IsActive = !isRetired;
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
