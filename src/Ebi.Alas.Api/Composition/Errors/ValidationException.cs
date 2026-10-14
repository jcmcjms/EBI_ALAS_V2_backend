namespace Ebi.Alas.Api.Composition.Errors;

/// <summary>Maps to HTTP 400 with the exception message as the problem detail.</summary>
public sealed class ValidationException(string message) : Exception(message);
