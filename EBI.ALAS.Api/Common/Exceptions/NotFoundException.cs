namespace EBI.ALAS.Api.Common.Exceptions;

/// <summary>
/// Thrown when a requested resource does not exist.
/// Carries the resource name and key for structured error responses.
/// </summary>
public sealed class NotFoundException : Exception
{
    public string ResourceName { get; }
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
