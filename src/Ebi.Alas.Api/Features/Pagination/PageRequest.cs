namespace Ebi.Alas.Api.Features.Pagination;

public sealed record PageRequest
{
    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 20;

    public int Skip => (Page - 1) * PageSize;

    public PageRequest Normalize(int maxPageSize, int defaultPageSize)
    {
        var page = Page < 1 ? 1 : Page;
        var pageSize = PageSize < 1 ? defaultPageSize : PageSize;

        if (pageSize > maxPageSize)
        {
            pageSize = maxPageSize;
        }

        return this with
        {
            Page = page,
            PageSize = pageSize
        };
    }
}
