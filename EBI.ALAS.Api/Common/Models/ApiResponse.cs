namespace EBI.ALAS.Api.Common.Models;

/// <summary>
/// Generic API response wrapper with success/error factory methods.
/// Immutable after construction — all properties use init setters.
/// </summary>
public sealed record ApiResponse<T>
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public T? Data { get; init; }
    public List<string> Errors { get; init; } = [];
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    public static ApiResponse<T> SuccessResponse(T data, string message = "Operation completed successfully")
        => new()
        {
            Success = true,
            Message = message,
            Data = data,
            Timestamp = DateTime.UtcNow
        };

    public static ApiResponse<T> ErrorResponse(string message, List<string>? errors = null)
        => new()
        {
            Success = false,
            Message = message,
            Errors = errors ?? [],
            Timestamp = DateTime.UtcNow
        };
}

/// <summary>
/// Non-generic API response for operations that return no data.
/// Immutable after construction — all properties use init setters.
/// </summary>
public sealed record ApiResponse
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public List<string> Errors { get; init; } = [];
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    public static ApiResponse SuccessResponse(string message = "Operation completed successfully")
        => new()
        {
            Success = true,
            Message = message,
            Timestamp = DateTime.UtcNow
        };

    public static ApiResponse ErrorResponse(string message, List<string>? errors = null)
        => new()
        {
            Success = false,
            Message = message,
            Errors = errors ?? [],
            Timestamp = DateTime.UtcNow
        };
}
