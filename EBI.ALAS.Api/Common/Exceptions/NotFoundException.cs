namespace EBI.ALAS.Api.Common.Exceptions;

/// <summary>
/// Exception thrown when a requested resource is not found.
/// Mapped to HTTP 404 by the GlobalExceptionHandler.
/// </summary>
public class NotFoundException : Exception
{
    /// <summary>
    /// The type name of the resource that was not found (e.g. "LoanApplication").
    /// </summary>
    public string ResourceName { get; }

    /// <summary>
    /// The key/value used to look up the resource.
    /// </summary>
    public object? Key { get; }

    public NotFoundException(string message)
        : base(message)
    {
        ResourceName = string.Empty;
    }

    public NotFoundException(string resourceName, object key)
        : base($"{resourceName} with key '{key}' was not found.")
    {
        ResourceName = resourceName;
        Key = key;
    }

    public NotFoundException(string resourceName, object key, Exception innerException)
        : base($"{resourceName} with key '{key}' was not found.", innerException)
    {
        ResourceName = resourceName;
        Key = key;
    }
}
