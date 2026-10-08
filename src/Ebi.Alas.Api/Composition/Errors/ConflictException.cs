namespace Ebi.Alas.Api.Composition.Errors;

public sealed class ConflictException(string message) : Exception(message);
