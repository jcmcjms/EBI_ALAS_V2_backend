namespace EBI.ALAS.Api.Common.Exceptions;
public class NotFoundException : Exception
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
