namespace EBI.ALAS.Api.Common.Models;

/// <summary>
/// Pagination request parameters expressed as an immutable record. Used by
/// WebLoan (and other read-only) endpoints that need page + page size binding
/// from query string parameters with strict defaults and an enforced ceiling.
/// </summary>
/// <remarks>
/// Use this type when the endpoint should expose <c>?page=&amp;pageSize=</c>
/// query parameters directly bound by ASP.NET Minimal APIs. Existing endpoints
/// already using the mutable <see cref="PaginationParams"/> class are kept
/// intact for backward compatibility.
/// </remarks>
/// <param name="Page">1-based page number. Must be greater than or equal to 1.</param>
/// <param name="PageSize">Number of items per page. Capped at <see cref="MaxPageSize"/>.</param>
public record PaginationRequest(int Page = 1, int PageSize = 20)
{
    /// <summary>Default page size used when the caller does not specify one.</summary>
    public const int DefaultPageSize = 20;

    /// <summary>Hard ceiling on page size to prevent runaway payloads.</summary>
    public const int MaxPageSize = 100;

    /// <summary>
    /// Returns a sanitized copy: page is clamped to at least 1 and pageSize is
    /// clamped to the inclusive range [1, <see cref="MaxPageSize"/>]. Used by
    /// the service layer as a defence-in-depth measure even when FluentValidation
    /// has already accepted the request.
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
/// Paged payload envelope for list endpoints that follow the API contract
/// described in the audit (page / pageSize / totalCount / items).
/// </summary>
/// <remarks>
/// This is the canonical shape used by the new WebLoan paginated endpoints.
/// Existing list endpoints use the older <see cref="PagedResult{T}"/> class
/// to preserve their JSON contract; do not switch them without coordinating
/// with the frontend.
/// </remarks>
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
    /// <summary>Total pages computed from <paramref name="TotalCount"/> and <paramref name="PageSize"/>.</summary>
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);

    /// <summary>True when there is at least one page before this one.</summary>
    public bool HasPreviousPage => Page > 1;

    /// <summary>True when there is at least one page after this one.</summary>
    public bool HasNextPage => Page < TotalPages;
}