namespace EBI.ALAS.Api.Shared.Errors;
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
