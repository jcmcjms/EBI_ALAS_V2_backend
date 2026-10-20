using System.Text.Json;
using Ebi.Alas.Api.Composition.Errors;
using Ebi.Alas.Api.Features.LoanApplications;

namespace Ebi.Alas.Api.Tests.Features.LoanApplications;

public sealed class NthpDateGuardTests
{
    private static readonly DateOnly FirstHalf = new(2026, 10, 5);
    private static readonly DateOnly SecondHalf = new(2026, 10, 20);

    private static JsonElement FormWithNthp(params string?[] nthpDates)
    {
        var loans = nthpDates
            .Select(d => new { parameters = new { nthpDate = d } })
            .ToArray();
        var json = JsonSerializer.Serialize(new { loans });
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    [Fact]
    public void FirstHalf_AllowsSecondHalfOfAugust()
    {
        NthpDateGuard.EnsureAcceptable(FormWithNthp("2026-08-20"), FirstHalf);
        NthpDateGuard.EnsureAcceptable(FormWithNthp("2026-08-16"), FirstHalf);
    }

    [Fact]
    public void FirstHalf_RejectsBeforeAugust16()
    {
        var ex = Assert.Throws<ValidationException>(
            () => NthpDateGuard.EnsureAcceptable(FormWithNthp("2026-08-15"), FirstHalf));
        Assert.Equal("Use a payslip from August 16 or later.", ex.Message);
    }

    [Fact]
    public void SecondHalf_RequiresSeptember()
    {
        NthpDateGuard.EnsureAcceptable(FormWithNthp("2026-09-01"), SecondHalf);
        var ex = Assert.Throws<ValidationException>(
            () => NthpDateGuard.EnsureAcceptable(FormWithNthp("2026-08-20"), SecondHalf));
        Assert.Equal("Use a payslip from September 1 or later.", ex.Message);
    }

    [Fact]
    public void RejectsMissingAndFuture()
    {
        Assert.Equal(
            "Add the payslip date before continuing.",
            Assert.Throws<ValidationException>(
                () => NthpDateGuard.EnsureAcceptable(FormWithNthp(""), FirstHalf)).Message);
        Assert.Equal(
            "The payslip date can't be in the future.",
            Assert.Throws<ValidationException>(
                () => NthpDateGuard.EnsureAcceptable(FormWithNthp("2026-10-06"), FirstHalf)).Message);
    }

    [Fact]
    public void NullForm_DoesNotThrow()
    {
        NthpDateGuard.EnsureAcceptable(null, FirstHalf);
    }
}
