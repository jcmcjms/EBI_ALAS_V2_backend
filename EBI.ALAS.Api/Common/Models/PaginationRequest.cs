namespace EBI.ALAS.Api.Common.Models;

/// <summary>
/// Query-string pagination parameters for read-only endpoints. Prefer this over
/// the older <see cref="PaginationParams"/> class on new endpoints.
/// </summary>
/// <param name="Page">1-based page number; must be &gt;= 1.</param>
/// <param name="PageSize">Items per page; capped at <see cref="MaxPageSize"/>.</param>
public record PaginationRequest(int Page = 1, int PageSize = 20)
{
    public const int DefaultPageSize = 20;

    public const int MaxPageSize = 100;

    /// <summary>
    /// Returns a copy with <c>Page</c> clamped to &gt;= 1 and <c>PageSize</c>
    /// clamped to [1, <see cref="MaxPageSize"/>]. Defence-in-depth even when
    /// FluentValidation has already accepted the request.
    /// </summary>
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

/// <summary>
/// Canonical paged payload envelope. Existing list endpoints still use the
/// older <see cref="PagedResult{T}"/> class to preserve their JSON contract;
/// do not switch them without coordinating with the frontend.
/// </summary>
/// <typeparam name="T">Item type returned on the current page.</typeparam>
/// <param name="Items">Items on the current page. Never null.</param>
/// <param name="TotalCount">Total items across all pages.</param>
/// <param name="Page">Current 1-based page number.</param>
/// <param name="PageSize">Page size used to produce this page.</param>
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