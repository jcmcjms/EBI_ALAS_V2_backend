namespace EBI.ALAS.Api.Common.Models;

/// <summary>
/// Generic paged result wrapper for paginated list responses.
/// </summary>
public class PagedResult<T>
{
    public List<T> Items { get; set; } = new();
    public int CurrentPage { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasPreviousPage => CurrentPage > 1;
    public bool HasNextPage => CurrentPage < TotalPages;

    public PagedResult() { }

    public PagedResult(List<T> items, int totalCount, int currentPage, int pageSize)
    {
        Items = items;
        TotalCount = totalCount;
        CurrentPage = currentPage;
        PageSize = pageSize;
    }

    public static PagedResult<T> Create(IEnumerable<T> source, int totalCount, int currentPage, int pageSize)
    {
        return new PagedResult<T>
        {
            Items = source.ToList(),
            TotalCount = totalCount,
            CurrentPage = currentPage,
            PageSize = pageSize
        };
    }
}

/// <summary>
/// Pagination request parameters for list endpoints.
/// </summary>
public class PaginationParams
{
    private const int MaxPageSize = 100;
    private int _pageSize = 20;

    public int Page { get; set; } = 1;

    public int PageSize
    {
        get => _pageSize;
        set => _pageSize = Math.Min(value, MaxPageSize);
    }
}
