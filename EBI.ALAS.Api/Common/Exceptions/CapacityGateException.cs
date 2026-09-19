namespace EBI.ALAS.Api.Common.Exceptions;

/// <summary>
/// Thrown when server-side computation gates (capacity-to-pay, NTHP minimum)
/// fail during submission. The endpoint layer catches this and returns a 400
/// with the error dictionary. Named to avoid clash with
/// FluentValidation.ValidationException.
/// </summary>
public sealed class CapacityGateException : Exception
{
    public Dictionary<string, string[]> Errors { get; }

    public CapacityGateException(string message, Dictionary<string, string[]> errors)
        : base(message)
    {
        ArgumentNullException.ThrowIfNull(errors);
        Errors = errors;
    }
}
