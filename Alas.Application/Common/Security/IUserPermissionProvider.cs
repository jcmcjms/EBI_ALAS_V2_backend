namespace Alas.Application.Common.Security;

public interface IUserPermissionProvider
{
    Task<UserPermissionSet> GetAsync(Guid userId, CancellationToken cancellationToken);
    Task InvalidateAsync(Guid userId, CancellationToken cancellationToken);
}