namespace EBI.ALAS.Api.Common.Models;

/// <summary>
/// Generic paged result with computed pagination metadata.
/// Immutable after construction — all properties use init setters.
/// </summary>
public sealed record PagedResult<T>
{
    public List<T> Items { get; init; } = [];
    public int CurrentPage { get; init; }
    public int PageSize { get; init; }
    public int TotalCount { get; init; }
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
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
        => new()
        {
            Items = source.ToList(),
            TotalCount = totalCount,
            CurrentPage = currentPage,
            PageSize = pageSize
        };
}

/// <summary>
/// Pagination parameters with a hard ceiling on page size.
/// Immutable after construction.
/// </summary>
public sealed record PaginationParams
{
    private const int MaxPageSize = 100;

    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;

    public PaginationParams Sanitized() => this with
    {
        Page = Page < 1 ? 1 : Page,
        PageSize = PageSize switch
        {
            < 1 => 1,
            > MaxPageSize => MaxPageSize,
            _ => PageSize
        }
    };
}
