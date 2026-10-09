using System.Security.Claims;
using Ebi.Alas.Api.Composition.Auth;
using Microsoft.AspNetCore.Mvc;

namespace Ebi.Alas.Api.Composition;

public static class AuthSecurityMiddleware
{
    public static IApplicationBuilder UseAuthSecurity(this IApplicationBuilder app)
    {
        return app.Use(async (context, next) =>
        {
            var user = context.User;
            var path = context.Request.Path.Value ?? "/";
            var method = context.Request.Method;

            if (MustChangePasswordGate.IsBlocked(user, method, path))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new ProblemDetails
                {
                    Status = StatusCodes.Status403Forbidden,
                    Title = "Password change required",
                    Detail = "Change your password before using the application.",
                });
                return;
            }

            if (CsrfGuard.IsBlocked(method, user, context.Request.Headers[CsrfGuard.HeaderName].FirstOrDefault()))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new ProblemDetails
                {
                    Status = StatusCodes.Status403Forbidden,
                    Title = "CSRF validation failed",
                    Detail = "Missing or invalid X-XSRF-TOKEN header.",
                });
                return;
            }

            await next();
        });
    }
}
