using Ebi.Alas.Api.Features.Users.Domain;

namespace Ebi.Alas.Api.Features.Users;

public static class UserMapping
{
    public static UserResponse ToResponse(this User user) => new(
        user.Id,
        user.UserName,
        user.FirstName,
        user.LastName,
        user.MiddleName,
        user.Email,
        user.BranchId,
        user.JobTitle,
        user.Role.ToString(),
        user.Status.ToString(),
        user.MustChangePassword,
        user.CreatedAt);
}
