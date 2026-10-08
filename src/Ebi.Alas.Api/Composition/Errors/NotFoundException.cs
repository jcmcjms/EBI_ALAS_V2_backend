namespace Ebi.Alas.Api.Composition.Errors;

public sealed class NotFoundException(string resource, string key)
    : Exception($"{resource} '{key}' was not found.")
{
    public string Resource { get; } = resource;

    public string Key { get; } = key;
}
