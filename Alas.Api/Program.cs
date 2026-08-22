using System.Text;
using System.Threading.RateLimiting;
using Alas.Api.Endpoints;
using Alas.Api.Security;
using Alas.Application.Common.Security;
using Alas.Infrastructure.Identity;
using Alas.Infrastructure.Persistence;
using Alas.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMemoryCache();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("auth", context =>
    {
        var ipAddress = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: $"auth: {ipAddress}",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 20,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            });
    });
});

builder.Services.AddDbContext<AlasDbContext>(options =>
{
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("Alas"),
        sql =>
        {
            sql.EnableRetryOnFailure(maxRetryCount: 3);
            sql.MigrationsAssembly(typeof(AlasDbContext).Assembly.FullName);
        });
    options.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
});

builder.Services.AddIdentityCore<AppUser>(options =>
    {
        options.User.RequireUniqueEmail = true;

        options.Password.RequiredLength = 12;
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireNonAlphanumeric = true;

        options.Lockout.AllowedForNewUsers = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    })
    .AddRoles<AppRole>()
    .AddEntityFrameworkStores<AlasDbContext>()
    .AddDefaultTokenProviders();

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));

builder.Services.AddScoped<TokenService>();
builder.Services.AddScoped<RefreshTokenService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<IUserPermissionProvider, UserPermissionProvider>();
builder.Services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();


builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var jwtOptions = builder.Configuration.GetSection("Jwt")
    .Get<JwtOptions>() ?? throw new InvalidOperationException("JWT options are missing.");
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.RequireHttpsMetadata = true;

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwtOptions.SigningKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });

builder.Services.AddAuthorization(options =>
{
    foreach (var permission in AlasPermissions.All)
    {
        options.AddPolicy(permission, policy =>
        {
            policy.RequirePermission(permission)
                ;
        });
    }
});

builder.Services.AddEndpointsApiExplorer();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapAuthEndpoints();


app.Run();

