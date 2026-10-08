using Microsoft.AspNetCore.Identity;

namespace Ebi.Alas.Api.Features.Auth;

public sealed class PasswordHasher
{
    private readonly PasswordHasher<object> _hasher = new();

    public string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        return _hasher.HashPassword(new object(), password);
    }

    public bool Verify(string password, string hash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        ArgumentException.ThrowIfNullOrWhiteSpace(hash);
        return _hasher.VerifyHashedPassword(new object(), hash, password) != PasswordVerificationResult.Failed;
    }
}
