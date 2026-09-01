namespace EBI.ALAS.Api.Common.Exceptions;
public class ForbiddenAccessException : Exception
{
    public string? RequiredPermission { get; }

    public ForbiddenAccessException()
        : base("You do not have permission to perform this action.")
    {
    }

    public ForbiddenAccessException(string message)
        : base(message)
    {
    }

    public ForbiddenAccessException(string message, string requiredPermission)
        : base(message)
    {
        RequiredPermission = requiredPermission;
    }

    public ForbiddenAccessException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
