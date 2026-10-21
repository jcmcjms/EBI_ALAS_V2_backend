namespace Ebi.Alas.Api.Composition.Errors;

/// <summary>Maps to HTTP 400 with the exception message as the problem detail.</summary>
public sealed class ValidationException : Exception
{
    public ValidationException(string message, IReadOnlyList<string>? errors = null)
        : base(message)
    {
        Errors = errors ?? [];
    }

    public IReadOnlyList<string> Errors { get; }
}
