using Ebi.Alas.Api.Features.Pagination;

namespace Ebi.Alas.Api.Tests.Features.Pagination;

public sealed class PageRequestTests
{
    [Theory]
    [InlineData(0, 20, 1, 20)]
    [InlineData(-5, 20, 1, 20)]
    [InlineData(3, 20, 3, 20)]
    public void Normalize_ClampsPageToAtLeastOne(int page, int size, int expectedPage, int expectedSize)
    {
        var request = new PageRequest { Page = page, PageSize = size }
            .Normalize(maxPageSize: 100, defaultPageSize: 20);

        Assert.Equal(expectedPage, request.Page);
        Assert.Equal(expectedSize, request.PageSize);
    }

    [Theory]
    [InlineData(5, 0, 5, 20)]
    [InlineData(5, -1, 5, 20)]
    public void Normalize_WhenPageSizeLessThanOne_UsesDefaultPageSize(int page, int size, int expectedPage, int expectedSize)
    {
        var request = new PageRequest { Page = page, PageSize = size }
            .Normalize(maxPageSize: 100, defaultPageSize: 20);

        Assert.Equal(expectedPage, request.Page);
        Assert.Equal(expectedSize, request.PageSize);
    }

    [Theory]
    [InlineData(5, 500, 5, 100)]
    [InlineData(5, 101, 5, 100)]
    [InlineData(5, 100, 5, 100)]
    public void Normalize_WhenPageSizeExceedsMax_ClampsToMaxPageSize(int page, int size, int expectedPage, int expectedSize)
    {
        var request = new PageRequest { Page = page, PageSize = size }
            .Normalize(maxPageSize: 100, defaultPageSize: 20);

        Assert.Equal(expectedPage, request.Page);
        Assert.Equal(expectedSize, request.PageSize);
    }

    [Fact]
    public void Normalize_ClampsToSafeBounds()
    {
        var request = new PageRequest { Page = 0, PageSize = 500 }
            .Normalize(maxPageSize: 100, defaultPageSize: 20);

        Assert.Equal(1, request.Page);
        Assert.Equal(100, request.PageSize);
    }

    [Theory]
    [InlineData(1, 20, 0)]
    [InlineData(2, 20, 20)]
    [InlineData(3, 15, 30)]
    public void Skip_ReturnsPageOffset(int page, int size, int expectedSkip)
    {
        var request = new PageRequest { Page = page, PageSize = size };

        Assert.Equal(expectedSkip, request.Skip);
    }

    [Fact]
    public void Skip_AfterNormalize_UsesNormalizedValues()
    {
        var request = new PageRequest { Page = 0, PageSize = 0 }
            .Normalize(maxPageSize: 100, defaultPageSize: 20);

        Assert.Equal(0, request.Skip);
    }

    [Theory]
    [InlineData(0, 20, 0)]
    [InlineData(1, 20, 1)]
    [InlineData(20, 20, 1)]
    [InlineData(21, 20, 2)]
    [InlineData(40, 20, 2)]
    [InlineData(41, 20, 3)]
    public void PageResult_TotalPages_RoundsUp(int totalCount, int pageSize, int expectedTotalPages)
    {
        var result = new PageResult<int>([], totalCount, Page: 1, PageSize: pageSize);

        Assert.Equal(expectedTotalPages, result.TotalPages);
    }

    [Fact]
    public void PageResult_WhenPageSizeIsZero_ReturnsZeroTotalPages()
    {
        var result = new PageResult<int>([], TotalCount: 10, Page: 1, PageSize: 0);

        Assert.Equal(0, result.TotalPages);
    }
}
