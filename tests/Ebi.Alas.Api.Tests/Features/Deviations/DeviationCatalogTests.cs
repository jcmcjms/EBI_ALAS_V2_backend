using Ebi.Alas.Api.Features.Deviations;

namespace Ebi.Alas.Api.Tests.Features.Deviations;

public sealed class DeviationCatalogTests
{
    [Fact]
    public void Catalog_ContainsFourMajorAndSeventeenMinor()
    {
        Assert.Equal(21, DeviationCatalog.All.Count);
        Assert.Equal(4, DeviationCatalog.All.Count(i => i.Severity == 2));
        Assert.Equal(17, DeviationCatalog.All.Count(i => i.Severity == 1));
    }

    [Fact]
    public void Catalog_MajorItemsMatchPriorSystem()
    {
        var majors = DeviationCatalog.All
            .Where(i => i.Severity == 2)
            .Select(i => i.Description)
            .OrderBy(d => d, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(
            [
                "Age not within the prescribed parameters",
                "Discounted Application Fee",
                "Interest rate reduction",
                "With past due account - non performing loan",
            ],
            majors);
    }

    [Fact]
    public void Catalog_AllItemsHaveKnownSeverity()
    {
        Assert.All(DeviationCatalog.All, i => Assert.InRange(i.Severity, 1, 2));
    }
}
