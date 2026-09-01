namespace EBI.ALAS.Api.Common.Models;
public record PaginationRequest(int Page = 1, int PageSize = 20)
{
    public const int DefaultPageSize = 20;

    public const int MaxPageSize = 100;
    public PaginationRequest Sanitized() => this with
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
public record PagedResponse<T>(
    IReadOnlyList<T> Items,
    int TotalCount,
    int Page,
    int PageSize)
{
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);

    public bool HasPreviousPage => Page > 1;

    public bool HasNextPage => Page < TotalPages;
}
